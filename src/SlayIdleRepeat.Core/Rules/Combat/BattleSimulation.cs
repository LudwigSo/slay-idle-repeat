using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Effects.Values;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The three value-mode bases that exist only inside the moment that fired, as the tick loop carries them from the event to <see cref="EffectOpContext"/>.</summary>
/// <param name="DamageDealt">
/// The <c>DAMAGE_DEALT_PCT</c> basis — the on-damage basis: the post-mitigation, post-floor hit
/// before ward absorption, so a leech on an <c>ON_HIT</c> still reads a fully-warded hit.
/// </param>
/// <param name="HealAmount">The healed amount — <c>HEAL_AMOUNT</c>'s subject in an <c>ON_HEAL</c>.</param>
/// <param name="OverhealAmount">The overheal — <c>OVERHEAL_AMOUNT</c>'s subject, likewise.</param>
/// <remarks>
/// Every member is nullable and none defaults to 0: a value-mode reader throws rather than reading zero
/// for a mode whose basis the context does not carry, which is what makes "exist only inside
/// <c>ON_HEAL</c> contexts" enforceable at all. A zero here would turn it into a silent no-op.
/// </remarks>
internal readonly record struct EventReadings(
    double? DamageDealt = null, double? HealAmount = null, double? OverhealAmount = null);

/// <summary>The battle-start pre-tick and the strict eight-step tick loop, for one fight.</summary>
/// <remarks>
/// <para><b>The tick order, and where each slot's work lives.</b></para>
/// <code>
/// PreTick (once, before tick 0)
///   0a  aggregate every actor's stats; attackCooldown = 0 for every opener
///   0b  fire every ON_BATTLE_START — hero side first, then enemies by index,
///       within one actor in ascending effect-id order
///   0c  the boss's phase 1 counts as entered                       → IBossPhases
///   0d  emit BattleStart
///
/// for tick in 0..MaxTicks-1
///   1   status timers advance; DoT/HoT cadence boundaries apply    → IStatusTimeline
///   2   statuses whose duration reached 0 expire                   → IStatusTimeline
///   2a  boss telegraphs — 1.0–1.5 s wind-up                        → IBossPhases
///   3   PERIODIC triggers fire — the ONLY PERIODIC path            → TriggerRegistry.PeriodicDue
///   4   basic attacks in fixed initiative order                    → IAttackPipeline
///   5   pet ability cooldowns advance                              → IPetAbilities
///   6   deaths resolve — ON_DEATH, then removal
///   7   (nothing: events are appended as they happen, by everyone)
///   8   if hero dead OR all enemies dead: break
/// </code>
/// <para><b>Four things this class owns that nothing else can check.</b></para>
/// <list type="number">
///   <item>
///     <b>Battle time is <c>BattleClock.SecondsAt(tick)</c>, never accumulated.</b> See
///     <see cref="BattleClock"/> — an accumulator puts an enrage's start delay a tick late on one
///     architecture and not the other.
///   </item>
///   <item>
///     <b>The attack cooldown is rounded at every accumulation point.</b> Unrounded, twenty
///     subtractions of <c>0.05</c> from <c>1.0</c> land just below zero, so a 1.0-ASPD actor still fires
///     on tick 20 and looks correct — but ten subtractions from <c>0.5</c> land just above zero, so a
///     2.0-ASPD actor waits an eleventh tick: 164 swings across a 90 s fight instead of 180, a 9% DPS
///     error on exactly the fast builds the balance harness calibrates against.
///   </item>
///   <item>
///     <b>Initiative is fixed</b> — hero, then enemies by index, pets never. Computed by
///     <see cref="InitiativeOrder"/> and nowhere else, which is the seam a duel's "the attacker's side
///     acts first" replaces.
///   </item>
///   <item>
///     <b><c>PeriodicDue</c> is called once per actor per tick, in actor order</b>, and
///     <see cref="TriggerRegistry.Evaluate"/> is never called with a <c>PERIODIC</c> — the registry
///     throws on that, which is what keeps the periodic schedule from being advanced twice for one tick.
///   </item>
/// </list>
/// <para>A stateful class, on <c>CombatLog</c>'s precedent — a 1800-tick loop is an accumulator. One instance per fight, one caller, never static.</para>
/// </remarks>
internal sealed class BattleSimulation
{
    /// <summary>The <c>sourceEffectId</c> a basic attack carries. Not an effect id and it cannot collide with one: an authored effect id is an identifier, and no identifier starts with <c>(</c>.</summary>
    /// <remarks>A basic attack is the damage pipeline invoked by the tick loop rather than by a DSL op, so it has no authored id to give.</remarks>
    internal const string BasicAttackSourceId = "(basic attack)";

    /// <summary>A defence-in-depth bound on trigger cascades resolving depth-first.</summary>
    /// <remarks>
    /// Not a game rule and not a tunable — the real anti-loop rules (thorns never re-triggers thorns;
    /// death saves fire at most their authored <c>once</c>) are implemented where they belong. This is
    /// the backstop for a cascade nobody anticipated: content that is mutually triggering would
    /// otherwise overflow the stack, which reports as a process crash rather than as the authoring
    /// error it is. Reached only by content, never by the loop.
    /// </remarks>
    private const int MaxCascadeDepth = 64;

    private readonly BattlePlan _plan;
    private readonly List<BattleActor> _actors;
    private readonly BattleSeams _seams;
    private readonly BattleServices _services;
    private readonly BattleFlowSink _flow;
    private readonly BattleTriggeredStatSink _triggeredStats;
    private readonly BattleRunEffects _runEffects;
    private readonly Dictionary<string, ushort> _effectIndex;
    private readonly BattleActor _hero;

    private List<BattleActor>? _initiative;
    private List<BattleActor>? _petOrder;
    private int _nextEnemyIndex;
    private int _nextLogId;
    private int _cascadeDepth;

    /// <summary>Builds the simulation for one fight. Nothing runs until <see cref="Run"/>.</summary>
    internal BattleSimulation(BattlePlan plan)
    {
        _plan = plan.Validated();

        Log = new CombatLog();
        Rng = BattleRngScope.Open(_plan.BattleSeed, _plan.RngPosition);
        Triggers = new TriggerRegistry(_plan.RunCounters);

        _services = new BattleServices(this);
        _seams = _plan.Seams(_services) ??
            throw new ArgumentException(
                "The seam factory returned null. Use BattleSeams.Strict for the M2-08 behaviour — a " +
                "null seam set would fail at the first tick with a NullReferenceException naming " +
                "nothing.",
                nameof(plan));

        _actors = _plan.Actors
            .OrderBy(a => a.Index)
            .Select(a => new BattleActor(a, _seams.Timeline))
            .ToList();

        _nextEnemyIndex = _actors[^1].Index + 1;
        _nextLogId = _actors.Max(a => a.LogId) + 1;

        _effectIndex = BuildEffectIndex(_plan.Actors);
        _flow = new BattleFlowSink(this);
        _triggeredStats = new BattleTriggeredStatSink(this);
        _runEffects = new BattleRunEffects(this);

        // Resolved once, here rather than lazily: a side always has exactly one hero and no op adds
        // or removes one (a summon is always an enemy). A `??=` would never latch a null and would
        // re-scan the roster at slot 8 of every tick.
        _hero = _actors.First(a => a.Side == BattleSide.HERO && a.Kind == EffectActorKind.HERO);
    }

    /// <summary>This fight's log.</summary>
    internal CombatLog Log { get; }

    /// <summary>This fight's combat stream, opened through <see cref="BattleRngScope"/>.</summary>
    internal DeterministicRng Rng { get; }

    /// <summary>This battle's trigger registry — one per battle, over the run's counters.</summary>
    internal TriggerRegistry Triggers { get; }

    /// <summary>The fight's bounds.</summary>
    internal CombatRules Rules => _plan.Rules;

    /// <summary>The two most important balance dials in the game, as the plan was given them.</summary>
    internal MitigationConstants Mitigation => _plan.Mitigation;

    /// <summary>The ward pool ceiling, likewise.</summary>
    internal double WardCapPct => _plan.WardCapPct;

    /// <summary>Every actor in index order, summons appended.</summary>
    internal IReadOnlyList<BattleActor> Actors => _actors;

    /// <summary>The tick being run. <c>0</c> during the pre-tick.</summary>
    internal int Tick { get; private set; }

    /// <summary>Runs the pre-tick and the loop, and seals the log.</summary>
    internal SimulationResult Run()
    {
        PreTick();

        var ranTicks = 0;

        for (Tick = 0; Tick < Rules.MaxTicks; Tick++)
        {
            ranTicks = Tick + 1;

            // The start-of-tick snapshot is taken AFTER the aggregation is brought up to date, so a
            // STAT_COPY reads the block as it stands at the top of this tick rather than as it stood
            // before last tick's ops landed.
            foreach (var actor in _actors)
            {
                RefreshStats(actor);
                actor.FreezeStartOfTick();
            }

            // ── 1 · status timers and DoT/HoT cadence ────────────────────────────────────────
            //
            // Indexed rather than foreach, here and in every slot below: slot 3 can summon, which
            // appends to the roster, and a foreach over a mutated list throws.
            for (var i = 0; i < _actors.Count; i++)
            {
                _seams.Timeline.AdvanceTimers(_actors[i], Tick);
            }

            // ── 2 · expiries, ascending effect-id order ──────────────────────────────────────
            //
            // Two kinds of expiry, and the ward half is the loop's rather than the seam's: a shield
            // grants a segment in a fight that wires no status timeline and holds no status, so a
            // seam-owned sweep would be a no-op for exactly the fights that have a ward and nothing
            // else. Wards first, per actor.
            for (var i = 0; i < _actors.Count; i++)
            {
                ExpireWards(_actors[i]);
                _seams.Timeline.ExpireDue(_actors[i], Tick);

                // The third kind of expiry, for the identical reason ExpireWards is the loop's and
                // not a seam's: BattleActor.TriggeredStatFirings is written by ITriggeredStatSink
                // (Rules.Combat), and the intra-Rules layering forbids IStatusTimeline's layer from
                // naming it.
                ExpireTriggeredStatEffects(_actors[i], Tick);
            }

            // ── 2a · boss telegraphs — 1.0-1.5 s wind-up ─────────────────────────────────────
            //
            // Not one of the eight ordinary slots, added deliberately: a wind-up is emitted AHEAD of
            // the firing it announces, so nothing that happens at the firing can raise it. It sits
            // before slot 3 because slot 3 is what advances a PERIODIC's schedule, and the pass reads
            // TriggerInstance.NextFiringTick. NoBossPhases.AdvanceTick is a no-op, so a fight with no
            // boss logs — and hashes — exactly as it did before this slot existed.
            //
            // Bounded before the walk for RunPeriodics' reason: the roster can grow mid-tick.
            var standing = _actors.Count;
            for (var i = 0; i < standing; i++)
            {
                _seams.Phases.AdvanceTick(_actors[i], Tick);
            }

            // ── 3 · PERIODIC triggers, actor order then effect-id order ──────────────────────
            RunPeriodics();

            // ── 4 · basic attacks, fixed initiative order ────────────────────────────────────
            RunBasicAttacks();

            // ── 5 · pet ability cooldowns, pets in slot order ────────────────────────────────
            RunPetAbilities();

            // ── 6 · deaths resolve, actor-index order ───────────────────────────────────────
            ResolveDeaths();

            // ── 7 · nothing: every state change appended itself as it happened ──────────────

            // ── 8 · the fight is over when one side has nobody left ────────────────────────
            if (HeroIsDown() || EnemiesAreCleared())
            {
                break;
            }
        }

        Tick = ranTicks - 1;

        var heroWon = Outcome();

        // ON_BATTLE_END fires after slot 8's break, at the fight's last tick, with
        // TriggerOccurrence.HeroWon set from the outcome — the only kind that reads that field. A
        // win-only reward grant would silently do nothing without this sweep.
        //
        // The run queue's position relative to this sweep is fixed: it applies "after the outcome is
        // fixed, before ON_BATTLE_END effects are granted" — which is why the outcome is computed
        // first, and where the queue's drain goes.
        foreach (var actor in BattleStartOrder())
        {
            FireTriggers(actor, new TriggerOccurrence
            {
                Kind = TriggerKind.ON_BATTLE_END,
                Tick = Tick,
                IsPvp = Rules.IsPvp,
                HeroWon = heroWon,
                HpFraction = actor.HpFraction,
            });
        }

        return Log.Complete(heroWon, ranTicks, _hero.CurrentHp);
    }

    // ══════════════════════════════════════════════════════════════════ the pre-tick

    /// <summary>The battle-start pre-tick — steps 0a, 0b, 0c and 0d, in that order.</summary>
    private void PreTick()
    {
        Tick = 0;

        // ── 0a · aggregate every actor's stats; attackCooldown = 0 for every opener.
        //
        // Every actor in the opening roster is an opener — the first basic attack lands on tick 0.
        // A summon is not one, and AdmitSummon gives it a full cooldown instead.
        foreach (var actor in _actors)
        {
            RefreshStats(actor);

            // 🔴 Opened AFTER the first aggregation, never before it. An actor is constructed on its
            // base block, so opening on that block means a hero whose loadout raises Max HP starts
            // every fight partly dead — and the timeout is decided on HP fraction, so it loses fights
            // its own build wins. A summon takes no part in this: it is admitted mid-fight, carries no
            // standing effects, and its aggregated block is its base block.
            actor.SetCurrentHp(actor.Plan.StartingHp ?? actor.MaxHp);

            actor.AttackCooldown = 0.0;

            // 🔒 The roster is announced BEFORE anything happens to it, and after the aggregation
            // above rather than before. Both halves are load-bearing: this number is the denominator
            // of the actor's health bar, so a value read off the base block would be smaller than the
            // opening health the line above just set for any actor whose holdings raise Max HP — a bar
            // that opens past its own right-hand end — and an event that arrived after the first ward
            // grant would be a bar the replayer had already been asked to move.
            Log.AppendActorSpawn(0, CombatActor.None, actor.LogId, actor.MaxHp);
        }

        // ── 0a/0b · register every effect active at battle start, at activationTick 0. That is
        //    the anchor for perk periodics and for the enrage, which is battle-scoped and
        //    therefore anchors here rather than at any phase.
        foreach (var actor in _actors)
        {
            RegisterHoldings(actor, 0);
        }

        // ── 0b · fire ON_BATTLE_START: hero side first (hero, then pets in slot order), then
        //    enemies by index; within one actor in ascending effect-id order.
        //
        // The order is MATERIALISED before the walk, for step 0c's reason below: BattleStartOrder()
        // is a deferred LINQ query over `_actors`, so an ON_BATTLE_START that summons would invalidate
        // it mid-iteration. The opening roster is what fires ON_BATTLE_START — a summon admitted here
        // is not a battle-opening actor and AdmitSummon gives it a full cooldown rather than an
        // opener's zero — so freezing the sequence is also the correct semantics, not just the safe one.
        foreach (var actor in BattleStartOrder().ToList())
        {
            FireTriggers(actor, new TriggerOccurrence
            {
                Kind = TriggerKind.ON_BATTLE_START,
                Tick = 0,
                IsPvp = Rules.IsPvp,
                HpFraction = actor.HpFraction,
            });
        }

        // ── 0c · the boss's phase 1 counts as entered.
        //
        // Indexed over a count taken BEFORE the walk, exactly as the tick loop's slots 2a-6 are and
        // for the identical reason: entering phase 1 fires ON_PHASE_ENTER, an ON_PHASE_ENTER may be
        // a SUMMON, and a summon is appended to `_actors` — so a foreach here throws "Collection was
        // modified". This is not hypothetical: it crashed every boss fight on tick 0 whose phase 1
        // itself summons, before this loop was indexed.
        //
        // The bound is taken before the walk rather than re-read each step so that a boss summoned by
        // another boss's phase 1 cannot have its own phase 1 entered in the same pass — a fight has
        // one boss, and a roster that grew into a second one mid-pre-tick would enter phases in an
        // order that depends on how many adds landed first.
        var opening = _actors.Count;
        for (var i = 0; i < opening; i++)
        {
            if (_actors[i].IsBoss)
            {
                _seams.Phases.EnterInitialPhase(_actors[i], 0);
            }
        }

        // ── 0d · emit BattleStart. Last, so the roster of 0a and every ward grant and opening buff
        //    of 0b already sit before it in the log — which is what a replayer needs to draw the
        //    opening banner over an arena that is already in its starting state.
        Log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);
    }

    /// <summary>Step 0b's order — hero side first (hero, then pets in slot order), then enemies by index — which is also a duel's "the attacker's side acts first".</summary>
    /// <remarks>
    /// Stated as a side-then-index sort rather than the plain index order the rest of the loop uses,
    /// because they are not the same claim: a duel puts a hero on the enemy side, and index order
    /// alone would interleave the two sides' openers by position.
    /// <para>The two rules ask for the same sequence and it is written once — a duel's attacker is the hero side, so they will not drift.</para>
    /// <para>
    /// Three callers, and only two of them are the battle-start rule. <see cref="PreTick"/>'s
    /// <c>ON_BATTLE_START</c> sweep and <see cref="ActingOrder"/>'s duel initiative both are; the
    /// third — <see cref="Run"/>'s <c>ON_BATTLE_END</c> sweep — is neither: no document orders it, and
    /// it takes this order because a battle's closing sweep matching its opening one is the least
    /// surprising choice.
    /// </para>
    /// </remarks>
    private IEnumerable<BattleActor> BattleStartOrder() =>
        _actors.Where(a => a.Side == BattleSide.HERO).OrderBy(a => a.Index)
            .Concat(_actors.Where(a => a.Side != BattleSide.HERO).OrderBy(a => a.Index));

    /// <summary>The order the tick loop's two acting slots walk the roster in — index order in PvE, and "attacker's side first" in a duel.</summary>
    /// <remarks>
    /// <para>
    /// A duel's row reads "the attacker's side acts first (hero, then pet abilities), then the
    /// defender's side". Read as a licence to interleave, it would move a pet ability (slot 5) in
    /// front of a basic attack (slot 4), overriding the fixed eight-slot order with a parenthetical.
    /// The reading implemented instead leaves both statements true: the eight slots keep their order,
    /// and the duel rule orders the sides <em>within</em> each of them.
    /// </para>
    /// <para>
    /// On a well-formed duel roster this changes nothing observable, and that is the trap: the
    /// attacker's side gets indices <c>0..3</c> and the defender's side <c>4..7</c>, so side-then-index
    /// and plain index order coincide. Tests probe this on a roster with deliberately inverted indices
    /// for exactly that reason.
    /// </para>
    /// <para>Gated on <see cref="CombatRules.IsPvp"/> and on nothing else — a second flag saying "order by side" would be a second statement of "is this a duel".</para>
    /// <para>
    /// Scope: slots 4 and 5 only, and not slots 1, 2 or 3 — a boundary, not an omission. Status timers,
    /// expiries and periodics keep index order in a duel as in a fight; on a roster whose indices are
    /// not side-grouped, slot 3 fires the defender's periodics first while slot 4 swings the attacker
    /// first, and slot 3 draws, so that is visible in <c>LogHash</c>. Extending the order to those
    /// three slots would be a one-line change and is deliberately not made here, since nothing
    /// authorises it.
    /// </para>
    /// </remarks>
    private IEnumerable<BattleActor> ActingOrder() =>
        Rules.IsPvp ? BattleStartOrder() : _actors.OrderBy(a => a.Index);

    // ══════════════════════════════════════════════════════════════════ the eight slots

    /// <summary>Slot 3 — <c>PERIODIC</c> triggers, in actor order, within one actor in ascending effect-id order.</summary>
    private void RunPeriodics()
    {
        // The roster is bounded BEFORE the walk, for slot 4's reason: a PERIODIC can summon, and a
        // summon gets a full cooldown so it never attacks on its spawn tick. An unbounded walk would
        // reach the newcomer in the same slot 3 and fire its just-registered periodics at their own
        // anchor tick.
        var standing = _actors.Count;

        for (var i = 0; i < standing; i++)
        {
            var actor = _actors[i];
            if (!actor.HoldsAPeriodic)
            {
                continue;
            }

            // Snapshotted: a firing may register another instance on the same actor, and
            // PeriodicDue refuses a candidate list that changed under it.
            var candidates = actor.Periodics.ToArray();

            foreach (var instance in Triggers.PeriodicDue(candidates, Tick))
            {
                ResolveFired(actor, instance.Effect, Occurrence(TriggerKind.PERIODIC, actor), null, null);
            }
        }
    }

    /// <summary>Slot 4 — basic attacks, in <see cref="InitiativeOrder"/>.</summary>
    /// <remarks>
    /// <para>
    /// The two sub-steps' order is load-bearing: 4a fires when <c>attackCooldown &lt;= 0</c> and sets
    /// it to <c>1.0 / ASPD</c>, and 4b then subtracts <c>TICK</c> whether or not 4a fired. Subtracting
    /// only on the non-firing path would make every attack land one tick late.
    /// </para>
    /// <para>ASPD is read at fire time, from the live block rather than the start-of-tick snapshot, which is what makes a haste applied by a slot-3 periodic shorten the very next cooldown rather than the one after.</para>
    /// </remarks>
    private void RunBasicAttacks()
    {
        foreach (var attacker in InitiativeOrder())
        {
            if (attacker.AttackCooldown <= 0.0 && attacker.IsAlive && _seams.Timeline.CanAct(attacker))
            {
                var target = SelectTarget(attacker);

                // The cooldown is consumed only by a swing that actually RESOLVED. An ON_ATTACK
                // trigger can finish the target, which puts it out of play at that moment — leaving
                // the actor with nothing to hit, the same situation as `target is null` above, and
                // must cost the same: nothing.
                if (target is not null && ResolveBasicAttack(attacker, target))
                {
                    // ASPD read at fire time — the live block, brought up to date here rather than
                    // the start-of-tick snapshot, so a haste applied by this tick's slot 3 shortens
                    // THIS cooldown.
                    RefreshStats(attacker);

                    var aspd = attacker.Stats[StatId.ASPD];
                    if (aspd <= 0.0)
                    {
                        throw new EffectContextException(
                            attacker.Id,
                            $"its ASPD is {aspd.ToString("R", CultureInfo.InvariantCulture)} at fire time",
                            "`05` §3.1 sets attackCooldown = 1.0 / ASPD, which a zero or negative ASPD " +
                            "turns into an infinity or a negative cooldown — the second makes the actor " +
                            "attack on every tick for the rest of the fight. `05` §1 gives every actor a " +
                            "base ASPD of 1.00 and `05` §5's FREEZE is -50%, so reaching 0 means an " +
                            "aggregation produced a stat the caps should have floored.");
                    }

                    attacker.AttackCooldown = StatRounding.Round(1.0 / aspd);
                }
            }

            // 4b — rounded at the accumulation point. Raw, ten subtractions of 0.05 from 0.5 leave a
            // residue just above 0, and a 2.0-ASPD actor then waits an eleventh tick — 164 swings a
            // fight instead of 180. (From 1.0 the residue is negative and the defect is invisible,
            // which is why the 2.0 case is the one that pins this.)
            attacker.AttackCooldown = StatRounding.Round(
                attacker.AttackCooldown - BattleClock.TickSeconds);
        }
    }

    /// <summary>Fixed initiative order — Hero, then enemies by index; pets never basic-attack. Fixed, not randomised: this removes a whole class of nondeterminism.</summary>
    /// <remarks>
    /// <para>
    /// In a duel the sequence is <see cref="ActingOrder"/>'s — "the attacker's side acts first … then
    /// the defender's side". It is a different sequence over the same roster, not a different rule
    /// about cooldowns or targets. On a conventionally indexed duel the two orders coincide.
    /// </para>
    /// <para>Materialised before the walk: an attack can kill, summon or revive, and slot 4's order is fixed — an actor summoned by the third enemy's swing does not act in the same tick, since it gets a full cooldown and goes to the end of the list.</para>
    /// <para>Held between ticks, and that is not a cache of a live reading: the order is a function of the roster's membership, sides and indices, none of which changes except when a summon is admitted — and <see cref="AdmitSummon"/> clears it. A death does not change it, because the <c>alive</c> test is inside the walk, not in the order. Rebuilding it 1800 times a fight was 5% of the whole time budget.</para>
    /// </remarks>
    private List<BattleActor> InitiativeOrder() =>
        _initiative ??= Acting(a => a.Kind != EffectActorKind.PET);

    /// <summary>Who this actor swings at.</summary>
    private BattleActor? SelectTarget(BattleActor attacker) =>
        attacker.Kind == EffectActorKind.HERO
            ? TargetSelection.ForBasicAttack(ContextFor(attacker))
            : TargetSelection.ForEnemyAttack(attacker, _actors);

    /// <summary>Slot 4a — one swing: <c>ON_ATTACK</c>, then the damage pipeline, then the on-hit family depth-first in ascending effect-id order, with the phase check after every HP change.</summary>
    /// <remarks>
    /// <para><b>How slot 4 invokes the attack pipeline.</b></para>
    /// <list type="number">
    ///   <item>The loop fires <c>ON_ATTACK</c> on the attacker before the swing resolves, so a trigger that grants an <c>ATTACK_MULT_NEXT</c> charge applies to this swing.</item>
    ///   <item>It then reads the attack multiplier out of <c>CombatFlowState</c> — base 1.0, times every armed charge in ascending effect-id order, each spent. The transient is never stored, which is how "it resets to 1.0 after every resolved attack" holds.</item>
    ///   <item>It emits <c>Attack</c> and calls <c>ResolveAttack(attacker, defender, multiplier, "(basic attack)")</c>. The pipeline owns every event from there — <c>Miss</c>, <c>Hit</c>, <c>Crit</c>, <c>Block</c>, <c>Shield</c>, <c>WardBroken</c>, <c>Heal</c>.</item>
    ///   <item>Every HP change is routed through <c>BattleServices.AfterHpDecrease</c>, which is the phase check and <c>ON_LOW_HP</c> in one call.</item>
    ///   <item>
    ///     The loop reads the returned <see cref="AttackResolution"/> and fires the on-hit family from
    ///     it, in this order: <c>ON_DODGE</c> on the defender (and nothing further — the attack ends on
    ///     a dodge); otherwise <c>ON_BLOCK</c> on the defender, then <c>ON_HIT</c> and <c>ON_CRIT</c> on
    ///     the attacker, then <c>ON_HIT_TAKEN</c> on the defender, then <c>ON_KILL</c> on the attacker
    ///     when the defender went down — unless <c>CombatRules.OnKillTriggersFire</c> is false, which
    ///     is the duel rule.
    ///     <para>
    ///     The order within the family is a ruling, not a quotation: it follows the damage formula's own
    ///     step order — block precedes the hit landing — and puts the defender's reaction after the
    ///     attacker's so a reactive effect sees the damage already applied.
    ///     </para>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <returns>Whether the swing resolved. See the caller for why that decides the cooldown.</returns>
    private bool ResolveBasicAttack(BattleActor attacker, BattleActor defender)
    {
        FireTriggers(attacker, Occurrence(TriggerKind.ON_ATTACK, attacker), defender);

        // The defender may have died to an ON_ATTACK trigger's own damage, which puts it out of play
        // at that moment, so the swing has nothing to land on.
        if (!defender.IsAlive || !attacker.IsAlive)
        {
            return false;
        }

        var multiplier = attacker.Flow.ConsumeAttackMultiplier();

        Log.Append(Tick, CombatEventType.Attack, attacker.LogId, defender.LogId);

        var resolution = _seams.Attack.ResolveAttack(attacker, defender, multiplier, BasicAttackSourceId);

        if (resolution.Missed)
        {
            FireTriggers(defender, Occurrence(TriggerKind.ON_DODGE, defender), attacker, attacker);

            // A dodge IS a resolved swing: the pipeline logs the MISS and returns, and the attacker
            // has taken its shot.
            return true;
        }

        // DAMAGE_DEALT_PCT's basis is the on-damage basis — the post-mitigation, post-floor hit
        // BEFORE ward absorption. A lifesteal attacker still heals off a fully-warded hit; a leech on
        // an ON_HIT reading the post-absorption number would heal nothing off a shielded target,
        // which is the opposite of that ruling.
        var dealt = new EventReadings(DamageDealt: resolution.Basis);

        if (resolution.Blocked)
        {
            FireTriggers(defender, Occurrence(TriggerKind.ON_BLOCK, defender), attacker, attacker, dealt);
        }

        FireTriggers(attacker, Occurrence(TriggerKind.ON_HIT, attacker), defender, null, dealt);

        if (resolution.Crit)
        {
            FireTriggers(attacker, Occurrence(TriggerKind.ON_CRIT, attacker), defender, null, dealt);
        }

        FireTriggers(defender, Occurrence(TriggerKind.ON_HIT_TAKEN, defender), attacker, attacker, dealt);

        if (!defender.IsAlive && Rules.OnKillTriggersFire)
        {
            FireTriggers(attacker, Occurrence(TriggerKind.ON_KILL, attacker), defender, null, dealt);
        }

        return true;
    }

    /// <summary>Slot 5 — pet ability cooldowns advance; ready abilities fire, pets in slot order.</summary>
    /// <remarks>
    /// <para>In a duel, the attacker's side's pets before the defender's — the other half of "attacker's side acts first (hero, then pet abilities)". See <see cref="ActingOrder"/>.</para>
    /// <para>Held between ticks for <see cref="InitiativeOrder"/>'s reason and cleared by the same event.</para>
    /// </remarks>
    private void RunPetAbilities()
    {
        // Materialised into a LOCAL before the walk, and the field is never re-read inside it. A pet
        // ability can resolve a SUMMON, AdmitSummon nulls this cache, and a loop whose bound is
        // `_petOrder.Count` would then dereference null on its next iteration.
        var pets = _petOrder ??= Acting(a => a.Kind == EffectActorKind.PET);

        for (var i = 0; i < pets.Count; i++)
        {
            _seams.Pets.Advance(pets[i], Tick);
        }
    }

    /// <summary>The roster in <see cref="ActingOrder"/>, narrowed to the actors one slot walks.</summary>
    private List<BattleActor> Acting(Func<BattleActor, bool> included)
    {
        var order = new List<BattleActor>(_actors.Count);

        foreach (var actor in ActingOrder())
        {
            if (included(actor))
            {
                order.Add(actor);
            }
        }

        return order;
    }

    /// <summary>Slot 6 — every actor at 0 HP, in actor-index order, fires its <c>ON_DEATH</c> effects and is then removed.</summary>
    /// <remarks>
    /// <para>Nothing here filters on liveness: liveness filters selection, not naming — an on-death explosion effect fires even though its holder is already dead.</para>
    /// <para>Re-swept until it settles, because an <c>ON_DEATH</c> can kill: an explosion can finish a second enemy, and "every actor at 0 HP" is a statement about the slot, not about the roster as it stood when the slot began.</para>
    /// </remarks>
    private void ResolveDeaths()
    {
        bool swept;

        do
        {
            swept = false;

            for (var i = 0; i < _actors.Count; i++)
            {
                var actor = _actors[i];

                if (actor.DeathResolved || actor.CurrentHp > 0.0 || actor.Kind == EffectActorKind.PET)
                {
                    continue;
                }

                FireTriggers(actor, Occurrence(TriggerKind.ON_DEATH, actor));

                // REVIVE, consumed HERE and nowhere else — after ON_DEATH, since a return is armed
                // "from 0 HP" and the actor must have reached it. That also lets an ON_DEATH holding
                // arm the save that saves its own holder, which is the shape the re-sweep exists to
                // tolerate.
                if (actor.Flow.ConsumeDeathSave(revive: true) is { } save)
                {
                    actor.SetCurrentHp(save.Hp);

                    // Fired only when the save actually restored HP. A REVIVE authored at 0 — or
                    // clamped to 0 by a Max HP of 0 — leaves a body, and ON_REVIVE over an actor
                    // still at 0 would announce a return that did not happen.
                    if (actor.CurrentHp > 0.0)
                    {
                        FireTriggers(actor, Occurrence(TriggerKind.ON_REVIVE, actor));
                    }
                }

                // Re-checked: an ON_DEATH may have carried a REVIVE, and an actor that came back is
                // not a body to remove.
                if (actor.CurrentHp > 0.0)
                {
                    continue;
                }

                actor.MarkDeathResolved();
                Log.Append(Tick, CombatEventType.ActorDeath, CombatActor.None, actor.LogId);
                swept = true;
            }
        }
        while (swept);
    }

    // ══════════════════════════════════════════════════════════════════ triggers

    /// <summary>Fires one moment over one actor's instances — depth-first, in ascending effect-id order.</summary>
    /// <remarks>
    /// <para>
    /// Driven one instance at a time through <see cref="TriggerRegistry.Evaluate"/>, not through a
    /// bulk evaluation: a cascade that changes the roster — one instance registering another, or
    /// killing the holder — must be driven and ordered by the tick loop one at a time. Every moment
    /// this method serves can do exactly that.
    /// </para>
    /// <para>The candidate list is ordered before anything fires: ordering after would put the ordering rule on the wrong side of the firings' side effects.</para>
    /// </remarks>
    private void FireTriggers(
        BattleActor holder,
        in TriggerOccurrence occurrence,
        BattleActor? target = null,
        BattleActor? attacker = null,
        EventReadings readings = default)
    {
        if (holder.Instances.Count == 0)
        {
            return;
        }

        // Walked by index over BattleActor.Instances, which is ALREADY in ascending effect-id order
        // because AddInstance imposes it on insert. A firing can register another instance on this
        // actor, so the count is bounded first: a cascade resolves depth-first through its own
        // FireTriggers call, not by growing the list this one is walking.
        var standing = holder.Instances.Count;

        for (var i = 0; i < standing; i++)
        {
            var held = holder.Instances[i];

            if (held.Kind != occurrence.Kind || !Triggers.IsRegistered(held.Id))
            {
                continue;
            }

            if (Triggers.Evaluate(held.Id, occurrence, Rng) == TriggerOutcome.FIRES)
            {
                ResolveFired(holder, Triggers[held.Id].Effect, occurrence, target, attacker, readings);
            }
        }
    }

    /// <summary>Resolves one fired effect — through the run/board routing first, so a combat trigger carrying a run/board op is emitted, never resolved.</summary>
    private void ResolveFired(
        BattleActor holder,
        EffectDefinition effect,
        in TriggerOccurrence occurrence,
        BattleActor? target,
        BattleActor? attacker,
        EventReadings readings = default)
    {
        if (_cascadeDepth >= MaxCascadeDepth)
        {
            throw new EffectContextException(
                effect.Id,
                $"it is {MaxCascadeDepth.ToString(CultureInfo.InvariantCulture)} levels deep in one " +
                "trigger cascade",
                "`05` §3.1 resolves cascades depth-first with two anti-loop rules — thorns never " +
                "re-triggers thorns, and SURVIVE_LETHAL/REVIVE fire at most their authored once count. " +
                "A cascade this deep is content that triggers itself through some third path, which " +
                "would otherwise overflow the stack and report as a crash rather than as the authoring " +
                "error it is.");
        }

        // The one run/board boundary, decided in one place. `0` is the runtime-resolved argument:
        // the sanctioned case (a die-face modifier) carries its face index and new face on the
        // EffectDefinition, so every argument is authored.
        if (TriggerRouting.Route(effect, occurrence, TriggerLayer.COMBAT, holder, _runEffects)
            != EffectRouting.RESOLVE)
        {
            return;
        }

        _cascadeDepth++;

        try
        {
            EffectOpResolver.Resolve(effect, new EffectOpContext
            {
                Evaluation = ContextFor(holder, target, attacker),
                Seams = OpSeamsFor(holder, target, attacker),
                DamageDealt = readings.DamageDealt,
                HealAmount = readings.HealAmount,
                OverhealAmount = readings.OverhealAmount,
            });
        }
        finally
        {
            _cascadeDepth--;
        }

        // Conservative, and it has to be THIS conservative: an op resolves onto the DSL's whole token
        // set (attacker, all enemies, all pets, lowest-HP enemy, and so on), so the subjects it
        // touched are not knowable from here, and invalidating only the holder and the current target
        // would leave the rest reading a stale aggregation until something unrelated dirtied them. It
        // is a bool on at most nine objects, and the aggregation itself is still gated by
        // StatsAreStale, so the cost is the flag and not the pass.
        for (var i = 0; i < _actors.Count; i++)
        {
            _actors[i].InvalidateStats();
        }
    }

    /// <summary>One moment, for one holder. The attacker is not on it — three-way condition checks read it off <c>EffectEvaluationContext.Attacker</c>, which <see cref="FireTriggers"/> carries separately.</summary>
    private TriggerOccurrence Occurrence(TriggerKind kind, BattleActor holder) =>
        new()
        {
            Kind = kind,
            Tick = Tick,
            IsPvp = Rules.IsPvp,
            HpFraction = holder.HpFraction,
        };

    /// <summary>Registers every effect an actor holds, at <paramref name="activationTick"/> — the schedule anchor for its <c>PERIODIC</c>s.</summary>
    private void RegisterHoldings(BattleActor actor, int activationTick)
    {
        var minted = 0;

        foreach (var held in actor.Plan.Effects)
        {
            if (held.Effect.Trigger is null)
            {
                // An untriggered effect is a passive: aggregation re-evaluates it at every
                // resolution pass, which is the aggregation's business, not the registry's.
                continue;
            }

            var id = held.InstanceId ?? EffectInstanceId.Of(
                $"{actor.Id}#{held.Effect.Id}#{minted++.ToString(CultureInfo.InvariantCulture)}");

            Triggers.Register(id, held.Effect, activationTick, actor.HpFraction);
            actor.AddInstance(id, held.Effect);
        }
    }

    // ══════════════════════════════════════════════════════════════════ HP, deaths, summons

    /// <summary>The phase this fight is in, for a phase-scoped duration. <c>null</c> when the roster carries no boss.</summary>
    /// <remarks>
    /// <para>
    /// The fight's boss, singular: a roster carrying two bosses is outside anything authored, and this
    /// answers with the first in index order rather than inventing a rule for a case nothing authors.
    /// </para>
    /// <para>It walks the roster on each call rather than caching, since <c>AdmitSummon</c> appends to it mid-fight and a cached boss would be a second, staler answer.</para>
    /// </remarks>
    internal int? CurrentBossPhase
    {
        get
        {
            for (var i = 0; i < _actors.Count; i++)
            {
                if (_actors[i].IsBoss)
                {
                    return _seams.Phases.CurrentPhase(_actors[i]);
                }
            }

            return null;
        }
    }

    /// <summary>The pre-tick 0c and phase-check obligation — the boss's phase 1 counts as entered: fire its <c>ON_PHASE_ENTER(1)</c> effects, and the same for every later entry.</summary>
    /// <param name="boss">The boss that just entered a phase.</param>
    /// <param name="phase">The phase entered, <c>1..3</c>.</param>
    /// <remarks>
    /// A routing, not a second implementation: the phase controller decides when a phase is entered,
    /// but resolving the effects the entry fires needs the loop's trigger routing, cascade bound and
    /// op seams. <see cref="FireTriggers"/> already walks the holder's instances in ascending
    /// effect-id order.
    /// </remarks>
    internal void FirePhaseEntry(BattleActor boss, int phase)
    {
        ArgumentNullException.ThrowIfNull(boss);

        FireTriggers(boss, Occurrence(TriggerKind.ON_PHASE_ENTER, boss) with { Phase = phase });
    }

    /// <summary>Resolves the one effect a <c>RANDOM_OUTCOME</c>'s draw named, once <see cref="IBossOutcomes"/> has found it among the holder's own holdings.</summary>
    /// <param name="holder">The actor whose roll it was.</param>
    /// <param name="effect">The winning row's effect.</param>
    /// <remarks>
    /// An outcome row carries no trigger of its own — the roll's own cadence is what fires it — so it
    /// is never on the registry and cannot be reached through <see cref="FireTriggers"/>. This is the
    /// same resolution path every fired effect takes, which keeps the run/board routing and the
    /// cascade bound applying to it too.
    /// </remarks>
    internal void ResolveOutcome(BattleActor holder, EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(holder);

        ResolveFired(holder, effect, Occurrence(TriggerKind.PERIODIC, holder), target: null, attacker: null);
    }

    /// <summary>The phase check plus <c>ON_LOW_HP</c> — called after every HP decrease, by the loop and by the pipeline/status engine through <see cref="BattleServices.AfterHpDecrease"/>.</summary>
    internal void AfterHpDecrease(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _seams.Phases.AfterHpDecrease(actor, Tick);

        AfterHpChange(actor);
    }

    /// <summary><c>ON_LOW_HP</c> — a crossing, observed after every HP change of the holder so that a firing cannot be lost between two observations.</summary>
    internal void AfterHpChange(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        FireTriggers(actor, Occurrence(TriggerKind.ON_LOW_HP, actor));
    }

    /// <summary><c>ON_LETHAL</c> — "would take fatal damage", fired between ward absorption and the HP write, when the post-absorption hit would take the holder to <c>&lt;= 0</c> HP. See <see cref="BattleServices.FireLethal"/> and <c>AttackPipeline.ApplyToHp</c>, the one call site.</summary>
    /// <remarks>
    /// <para>This is what lets <c>SURVIVE_LETHAL</c>/<c>REVIVE</c> arm on <c>ON_LETHAL</c> at all: firing this before <c>ApplyToHp</c> asks <c>CombatFlowState.ConsumeDeathSave</c> gives the op somewhere to arm the save the consume call is about to look for.</para>
    /// <para>One call, one occurrence, never per damage sub-component: <c>ApplyToHp</c> is the pipeline's single choke point for the attack, <c>DAMAGE_MAXHP_PCT</c> and the thorns reflect, so a single call from there fires this exactly once no matter which of the three routes produced it.</para>
    /// </remarks>
    internal void FireLethal(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        FireTriggers(actor, Occurrence(TriggerKind.ON_LETHAL, actor));
    }

    /// <summary><c>ON_HEAL</c>, fired after the HP is applied — see <see cref="BattleServices.AfterHeal"/>.</summary>
    /// <remarks>Both readings travel to the op layer: a heal-amount/overheal-amount reader throws rather than reading zero when the context does not carry them, so a heal that fired the trigger without them would make an overheal-reading perk an exception rather than a perk.</remarks>
    internal void AfterHeal(BattleActor actor, double healed, double overheal)
    {
        ArgumentNullException.ThrowIfNull(actor);

        FireTriggers(
            actor,
            Occurrence(TriggerKind.ON_HEAL, actor),
            readings: new EventReadings(HealAmount: healed, OverhealAmount: overheal));
    }

    /// <summary>Ward expiry — see <see cref="BattleServices.ExpireWards"/> for why it is a routing here rather than a call the status engine makes on the pool directly.</summary>
    internal int ExpireWards(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var dropped = actor.Wards.ExpireDue(Tick);

        // TWO different orders, and they are not the same rule. Absorption is ordered by soonest
        // expiry then grant order, which is what WardPool returns; expiry EMISSION is ordered by
        // ascending effect-id, since wards are among the statuses for this purpose. Two segments from
        // different effects expiring on one tick would otherwise land in the log in an unauthorised
        // order, and the log is inside LogHash.
        foreach (var segment in dropped.OrderBy(s => s.SourceEffectId, EffectOrder.IdComparer))
        {
            // StatusExpired, and NEVER WardBroken — segment expiry silently removes its remainder
            // and does not fire WardBroken, the distinction a "until ward broken" duration
            // terminator is built on.
            Log.Append(
                Tick, CombatEventType.StatusExpired, CombatActor.None, actor.LogId, segment.Amount);
        }

        return dropped.Count;
    }

    /// <summary>The third kind of expiry: a FIRED stat op whose duration has ended.</summary>
    /// <remarks>
    /// Modelled on <c>StatusTimeline.ExpireDue</c>: the same <see cref="DurationEvaluator"/>, the same
    /// <see cref="DurationProbe"/> shape, and the same ascending-effect-id emission order. No
    /// <c>CombatEvent</c> is emitted on expiry, on the same reasoning as a fired enrage stack: this is
    /// a stat buff, not the phase/telegraph events the log is asked to carry.
    /// </remarks>
    private void ExpireTriggeredStatEffects(BattleActor actor, int tick)
    {
        var firings = actor.TriggeredStatFirings;

        if (firings.Count == 0)
        {
            return;
        }

        var probe = new DurationProbe
        {
            BattleTimeSeconds = BattleClock.SecondsAt(tick),
            CurrentPhase = CurrentBossPhase,
        };

        List<string>? expired = null;

        foreach (var (effectId, instance) in firings)
        {
            if (!DurationEvaluator.Evaluate(instance.Application, probe).HasEnded)
            {
                continue;
            }

            expired ??= new List<string>();
            expired.Add(effectId);
        }

        if (expired is null)
        {
            return;
        }

        // Expiry is emitted in ascending effect-id order. A dictionary walk carries no order at
        // all, so the removal itself needs one even though nothing is logged here: RefreshStats
        // folds whatever remains through StatAggregation, which re-sorts independently — but two
        // firings of ONE actor expiring on one tick must still leave the store in an order a later
        // direct reader can trust.
        expired.Sort(EffectOrder.IdComparer);

        foreach (var effectId in expired)
        {
            firings.Remove(effectId);
        }

        actor.InvalidateStats();
    }

    /// <summary>Ward grant with an expiry — see <see cref="BattleServices.GrantWard"/>.</summary>
    /// <remarks>
    /// The pool is written here rather than through <see cref="IAttackPipeline"/>, deliberately: an
    /// earlier draft delegated to <c>AttackPipeline</c> behind an <c>is not AttackPipeline ? throw</c>,
    /// which made the one route duration-bearing wards must take unusable from a seam composition that
    /// swaps out the attack pipeline — so every duration-bearing grant would have thrown.
    /// </remarks>
    internal void GrantWard(
        BattleActor target, double amount, double? sourceCapPct, string sourceEffectId, int? expiresAtTick)
    {
        ArgumentNullException.ThrowIfNull(target);

        var granted = target.Wards.Grant(
            amount,
            sourceCapPct,
            sourceEffectId,
            expiresAtTick,
            WardCapPct,

            // RE-READ on every grant, never cached: a boss can gain a stat multiplier mid-fight, so
            // its post-step-7 Max HP is not a battle constant.
            target.PostMultiplierMaxHp);

        // Shield on EVERY grant, clipped ones included: a cast the player watched happen must have
        // an event to draw, and a value of 0 is the information rather than the absence of it.
        Log.Append(Tick, CombatEventType.Shield, CombatActor.None, target.LogId, granted);
    }

    /// <summary>The summon entry rule, implemented here because it is the roster's: summons enter at the end of the enemy index list with a full attack cooldown (they never attack on their spawn tick) and become targetable at the next targeting evaluation.</summary>
    internal BattleActor AdmitSummon(ActorPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (_nextLogId > CombatActor.MaxId)
        {
            throw new InvalidOperationException(
                $"A summon needs log id {_nextLogId.ToString(CultureInfo.InvariantCulture)}, above " +
                $"CombatActor.MaxId ({CombatActor.MaxId.ToString(CultureInfo.InvariantCulture)}). Ids " +
                "are never reused, because the log is the replay and two actors on one id would draw " +
                "the second resuming the first's HP bar.");
        }

        var admitted = plan with
        {
            Index = _nextEnemyIndex++,
            LogId = (byte)_nextLogId++,
            IsSummon = true,
        };

        var actor = new BattleActor(admitted, _seams.Timeline);
        _actors.Add(actor);

        // The one thing that changes slot 4's and slot 5's fixed orders. Cleared here so the summon
        // takes its place at the end of the enemy index list on the next tick.
        _initiative = null;
        _petOrder = null;

        RefreshStats(actor);

        // 🔒 Announced on the tick it enters on, not left to the pre-tick's roster: a summon appears
        // mid-fight and the replayer has to draw a bar for it from that tick onward, so an actor with
        // no spawn event of its own would take hits against no denominator — which is the whole defect
        // ActorSpawned exists to end, reintroduced for exactly the actors a boss fight adds. After
        // RefreshStats for the pre-tick's reason: this is the bar's denominator.
        Log.AppendActorSpawn(Tick, SummonerLogIdOf(admitted), actor.LogId, actor.MaxHp);

        // A STAT_COPY reads the start-of-tick snapshot, and the per-tick sweep that takes one has
        // already run by the time a summon is admitted. Its opening block IS its correct snapshot for
        // the rest of this tick; without this a STAT_COPY reading it would hit BattleStatReader's
        // refusal with a message blaming the battle-start pre-tick.
        actor.FreezeStartOfTick();

        RegisterHoldings(actor, Tick);

        // A FULL cooldown, not zero: pre-tick 0a's "attackCooldown = 0" is about battle-opening
        // actors, and a summon never attacks on its spawn tick. Read after RefreshStats so it is the
        // summon's own aggregated ASPD.
        var aspd = actor.Stats[StatId.ASPD];
        actor.AttackCooldown = aspd > 0.0 ? StatRounding.Round(1.0 / aspd) : BattleClock.TickSeconds;

        return actor;
    }

    /// <summary>The log id of a summon's summoner, or <see cref="CombatActor.None"/> when nothing names one.</summary>
    /// <remarks>
    /// <c>OwnerId</c> is the roster's own string identity — <c>OWNER</c>'s subject — and the log
    /// addresses actors by byte, so the two are bridged here rather than by widening the plan. Absent
    /// rather than refused when the owner is unnamed or has left the roster: the spawn event's job is
    /// the newcomer's health bar, and a summon whose summoner already died is an ordinary end to a
    /// boss fight rather than a reason to lose the bar.
    /// </remarks>
    private byte SummonerLogIdOf(ActorPlan summon)
    {
        if (summon.OwnerId is not { } owner)
        {
            return CombatActor.None;
        }

        foreach (var actor in _actors)
        {
            if (string.Equals(actor.Id, owner, StringComparison.Ordinal))
            {
                return actor.LogId;
            }
        }

        return CombatActor.None;
    }

    // ══════════════════════════════════════════════════════════════════ outcome

    private bool HeroIsDown() => !_hero.IsAlive;

    /// <summary>Slot 8's second half. A plain loop rather than LINQ: it runs on every one of 1800 ticks, and a closure-capturing <c>Any</c> here was measurable against the time budget.</summary>
    private bool EnemiesAreCleared()
    {
        for (var i = 0; i < _actors.Count; i++)
        {
            var actor = _actors[i];
            if (actor.Side == BattleSide.ENEMY && actor.Kind != EffectActorKind.PET && actor.IsAlive)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Who won. A cleared enemy side is a win, a downed hero is a loss, and on the timeout the side with the higher remaining HP fraction wins.</summary>
    /// <remarks>
    /// <para>
    /// The fraction is the side's, not the hero's: a pack of five enemies has five HP bars, and
    /// comparing the hero's fraction against one enemy's would let a hero at 40% lose a timeout to a
    /// pack at 41% on its last unit and dead on the other four. Totals are over the side's killable
    /// actors — pets are excluded since they are unkillable and have no stake in a war of attrition.
    /// </para>
    /// <para>
    /// An exact tie is a loss for the hero in PvE, and that is errata: nothing authors a PvE tie rule,
    /// and a timeout is a failure to clear — counting it as a clear would inflate the balance harness's
    /// calibrated clear rate.
    /// </para>
    /// <para>A duel does author one, and it goes the other way: the lower-rated player wins an exact tie, a small underdog bias that prevents stagnation at the top. Which side that is arrives on <see cref="CombatRules.ExactTieWinner"/>, since the simulator has no business holding a rating. Absent — every PvE fight — the comparison stays strict.</para>
    /// <para>"Exact" is exact at four decimal places, the precision <see cref="SideHpFraction"/> produces and every other combat number is compared at — raw double equality would fire on almost nothing and would fire differently on two architectures, and the server re-runs a duel, so a tie that broke one way on the client and the other on the server would discard an honest result.</para>
    /// <para>
    /// A mutual death is an attacker loss, in a duel too, and that is errata: the downed-hero arm runs
    /// first, so two heroes reaching 0 HP on the same tick ends as a defeat whatever
    /// <see cref="CombatRules.ExactTieWinner"/> says. It is the one path where a duel's slight attacker
    /// edge reverses, and it is not treated as a 0.0/0.0 tie, because a fight that ended in deaths did
    /// not reach the timeout at all.
    /// </para>
    /// </remarks>
    private bool Outcome()
    {
        if (HeroIsDown())
        {
            return false;
        }

        if (EnemiesAreCleared())
        {
            return true;
        }

        var hero = SideHpFraction(BattleSide.HERO);
        var enemies = SideHpFraction(BattleSide.ENEMY);

        // `==` and deliberately not `double.Equals`, which differs from it at exactly one value:
        // Equals answers TRUE for NaN against NaN and would hand out a tie win, where `==` denies it.
        // NaN cannot reach here — ActorStats refuses an unrounded value and SetCurrentHp refuses NaN
        // outright — so this is the spelling whose failure mode points the safe way if that ever
        // stops being true.
        if (Rules.ExactTieWinner is { } underdog && hero == enemies)
        {
            return underdog == BattleSide.HERO;
        }

        return hero > enemies;
    }

    private double SideHpFraction(BattleSide side)
    {
        var current = 0.0;
        var maximum = 0.0;

        foreach (var actor in _actors)
        {
            // Despawned ≠ killed, so a CLEAR_SUMMONS'd actor keeps its HP — but it has left the
            // fight, and the timeout is decided on the side's REMAINING HP. Counting a killed actor
            // at 0/max is right; counting a despawned one at max/max would hand the timeout to the
            // side whose summons were cleared.
            if (actor.Side != side || actor.Kind == EffectActorKind.PET || actor.Despawned)
            {
                continue;
            }

            current += actor.CurrentHp;
            maximum += actor.Stats[StatId.MAX_HP];
        }

        return maximum <= 0.0 ? 0.0 : StatRounding.Round(current / maximum);
    }

    // ══════════════════════════════════════════════════════════════════ stats and context

    /// <summary>Stat aggregation for one actor, through the condition gate and the <c>valueScale</c> evaluator.</summary>
    /// <remarks>
    /// <para>
    /// One reader per pass, bound to one context, never cached across ticks: a scale is re-evaluated at
    /// every resolution pass, so a reader built once and reused would freeze a berserk perk's step
    /// count at whatever the hero's HP was when the fight started.
    /// </para>
    /// <para>
    /// The re-aggregation is conditional, and the condition is not a cache: aggregation over nine
    /// actors × 1800 ticks is the whole of the fight's time budget, so it runs only when the answer can
    /// have changed. It can change two ways: an effect was added or removed
    /// (<see cref="BattleActor.StatsAreStale"/>, raised by every op and every registration), or the
    /// actor holds an effect whose value or activation reads live state (a <c>valueScale</c> or a
    /// condition), in which case it is re-aggregated every tick unconditionally.
    /// </para>
    /// </remarks>
    private void RefreshStats(BattleActor actor)
    {
        if (!actor.StatsAreStale && !actor.StatsDependOnLiveState)
        {
            return;
        }

        var aggregated = AggregateFor(actor, ContextFor(actor));

        // The phase check runs after EVERY HP decrease, and a shrinking MAX_HP is one: a stat effect
        // can re-base Max HP mid-fight, and a boss clipped below a threshold must enter the next
        // phase there rather than on whatever unrelated swing lands next. ON_LOW_HP is a crossing for
        // the same reason.
        // The WHOLE record is kept, not just the final block: a consumer that discarded the wrapper
        // would cap every ward at whatever Max HP the aggregation last produced. And this runs on
        // every re-aggregation, so an enrage moves the ward cap with the boss's Max HP.
        if (actor.SetStats(aggregated))
        {
            AfterHpDecrease(actor);
        }
    }

    /// <summary>
    /// One actor's stat block as it stands against one attack's other party — the conditional
    /// standing-effect bucket's per-pair re-aggregation.
    /// </summary>
    /// <remarks>
    /// The ambient block is returned UNCHANGED — the same instance, not a recomputation — for every
    /// actor holding no context-gated standing effect, which keeps the ungated path byte-identical
    /// to a world without the bucket. Only an actor whose standing stat ops read the current target
    /// or the attacker pays for a fresh pass, over the same collected effect list, with the pair in
    /// context — where a target-gated effect is finally carried and evaluated. The result is a
    /// local reading for one resolution: it is never stored on the actor, so the ambient block, the
    /// phase check and the start-of-tick snapshot are untouched by it.
    /// </remarks>
    /// <param name="actor">Whose stats are being read.</param>
    /// <param name="target">The actor's current target, when it is the swing's source.</param>
    /// <param name="attacker">The actor hitting it, when it is the swing's defender.</param>
    internal AggregatedStats StatsAgainst(BattleActor actor, BattleActor? target, BattleActor? attacker)
    {
        if (!actor.HoldsContextGatedStanding || (target is null && attacker is null))
        {
            return actor.Aggregated;
        }

        return AggregateFor(actor, ContextFor(actor, target, attacker));
    }

    /// <summary>One aggregation pass over the actor's collected effects, against one context.</summary>
    private AggregatedStats AggregateFor(BattleActor actor, EffectEvaluationContext context)
    {
        // UNTRIGGERED effects, PLUS every live FIRED stat op — the full "collect all active
        // effects", not the untriggered half alone. An untriggered effect is a standing modifier
        // re-evaluated at every resolution pass, while a triggered one applies at fire time and then
        // lives for its own duration — which is exactly BattleActor.TriggeredStatFirings, folded in
        // below. Aggregating a fired op straight from BattleActor.StandingEffects would apply an
        // enrage's multiplier from tick 0 instead of at its real start delay, and exactly once instead
        // of once a second — which is why it is read from the fired store's EffectStackSet.CombinedValue
        // instead: that value already folds every activation since the effect fired, per its own
        // stacking mode.
        //
        // STAT_COPY writes land on the holder as percent-bucket adds that aggregation step 5 picks
        // up. They are not authored effects, so they are stated as synthetic STAT_ADD_PCTs under an id
        // no authored effect can take. A fired stat op needs no such synthesis — see
        // TriggeredStatInstance's remarks on why its own id is safe to reuse here.
        //
        // The standing list is handed over as-is when there is nothing else to fold in, which is
        // every actor in every fight until a STAT_COPY fires, a stat-modifying status lands, or a
        // triggered stat op fires: this method runs for every state-dependent actor on every one of
        // 1800 ticks, and copying a constant list each time was measurable against the time budget.
        //
        // Stat-modifying statuses fold in through IStatusTimeline.StatModifiers. Several of the
        // status vocabulary are stat modifiers (freeze, weaken, sunder, and so on), and a status that
        // never reached this aggregation would be a status that does nothing. They arrive in the same
        // synthetic STAT_ADD_PCT shape as the STAT_COPY buckets and for the same reason.
        IReadOnlyList<EffectDefinition> effects;
        var statuses = _seams.Timeline.StatModifiers(actor);
        var firings = actor.TriggeredStatFirings;

        if (actor.Flow.PercentBuckets.Count == 0 && statuses.Count == 0 && firings.Count == 0)
        {
            effects = actor.StandingEffects;
        }
        else
        {
            var withBuckets = new List<EffectDefinition>(actor.StandingEffects);

            foreach (var (stat, fraction) in actor.Flow.PercentBuckets)
            {
                withBuckets.Add(new EffectDefinition
                {
                    Id = $"(stat-copy:{stat})",
                    Op = EffectOp.STAT_ADD_PCT,
                    Stat = StatSelector.Of(stat),
                    Value = fraction,
                });
            }

            for (var i = 0; i < statuses.Count; i++)
            {
                withBuckets.Add(statuses[i]);
            }

            // Aggregation step 1's other half. Reuses the FIRING effect's own id (not a synthetic
            // one): a fired triggered effect is never also in StandingEffects, and every authored id
            // is repository-unique, so there is nothing here to collide with.
            foreach (var (effectId, instance) in firings)
            {
                withBuckets.Add(new EffectDefinition
                {
                    Id = effectId,
                    Op = instance.Op,
                    Stat = StatSelector.Of(instance.Stat),
                    Value = instance.Stacks.CombinedValue,
                });
            }

            effects = withBuckets;
        }

        return StatAggregation.Aggregate(
            actor.Plan.BaseStats,
            effects,
            _plan.Caps,
            new StatAggregationSeams(
                new BattleConditionGate(context),
                new ScaledEffectValue(context),
                StatOpBehaviour.Instance));
    }

    /// <summary>The evaluation context for one holder — this fight's roster, this fight's clock, this fight's draw stream.</summary>
    internal EffectEvaluationContext ContextFor(
        BattleActor holder, BattleActor? target = null, BattleActor? attacker = null) =>
        new()
        {
            Holder = holder,
            CurrentTarget = target,
            Attacker = attacker,
            Actors = _actors,
            BattleTimeSeconds = BattleClock.SecondsAt(Tick),
            FightHorizonSeconds = Rules.HorizonSeconds,
            IsPvp = Rules.IsPvp,
            Run = _plan.Run,
            Rng = Rng,
        };

    private EffectOpSeams OpSeamsFor(BattleActor holder, BattleActor? target, BattleActor? attacker) =>
        new(
            new BattleScaledValue(ContextFor(holder, target, attacker)),
            _seams.Attack,
            _seams.Statuses,
            _flow,
            new BattleStatReader(),
            _runEffects,
            _triggeredStats);

    /// <summary>The battle's effect table: every authored effect id in the opening roster, distinct, in ascending ordinal order. Its positions are the <c>RunEffectQueued</c>/<c>Telegraph</c> indices.</summary>
    private static Dictionary<string, ushort> BuildEffectIndex(IReadOnlyList<ActorPlan> actors)
    {
        var ids = actors
            .SelectMany(a => a.Effects)
            .Select(e => e.Effect.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, EffectOrder.IdComparer)
            .ToArray();

        var index = new Dictionary<string, ushort>(ids.Length, StringComparer.Ordinal);
        for (var i = 0; i < ids.Length; i++)
        {
            index[ids[i]] = (ushort)i;
        }

        return index;
    }

    /// <summary>One effect's position in the battle's effect table — the <c>ushort</c> a <c>RunEffectQueued</c> or <c>Telegraph</c> event carries.</summary>
    /// <remarks>
    /// One table, built once, read by everyone. It is <c>internal</c> rather than private because the
    /// telegraph pass needs the same positions, and a seam rebuilding <see cref="BuildEffectIndex"/>'s
    /// expression for itself would be a second table that has to be kept identical to this one by
    /// hand, on a number inside every committed <c>LogHash</c>.
    /// </remarks>
    internal ushort EffectIndexOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return _effectIndex.TryGetValue(effect.Id, out var index)
            ? index
            : throw new EffectContextException(
                effect.Id,
                "it is not in the battle's effect table",
                "`05` §7's RunEffectQueued and Telegraph carry a battle-local INDEX into the table of authored " +
                "effect ids, built once from the opening roster in `18` §8's ordinal order — because " +
                "no string fits a ushort and SimulationResult's five fields cannot carry the table. An " +
                "effect that arrived mid-fight (a summon's) has no stable position in it: appending " +
                "would shift nothing, but re-sorting would move indices that are already inside " +
                "LogHash. No authored `18` §2.5 op is reachable from a summon, so this is refused " +
                "rather than solved by guessing which of the two is meant.");
    }

    // ══════════════════════════════════════════════════════════════════ the seams this class implements

    /// <summary>Stat aggregation's condition gate, over the condition evaluator bound to one pass's context.</summary>
    /// <remarks>
    /// The subject-presence check comes first, and it is the conditional standing-effect bucket's
    /// whole activation rule: a condition reading the current target or the attacker is evaluated
    /// only in a context that carries that subject, and is simply inactive elsewhere. Ambient
    /// re-aggregation carries neither, so a target-gated standing effect no longer throws out of
    /// every fight that holds one — it waits for the attack resolution whose pair context carries
    /// its subject. Every ambient condition passes the check in every context and evaluates exactly
    /// as before.
    /// </remarks>
    private sealed class BattleConditionGate : IEffectConditionGate
    {
        private readonly EffectEvaluationContext _context;

        internal BattleConditionGate(EffectEvaluationContext context) => _context = context;

        public bool IsActive(EffectDefinition effect) =>
            _context.Carries(ConditionSubjects.Of(effect.Condition)) &&
            ConditionEvaluator.IsSatisfied(effect.Condition, _context);
    }

    /// <summary><c>effectiveValue = value × steps</c> for the op layer, over the value-scale evaluator.</summary>
    /// <remarks>
    /// Distinct from <c>ScaledEffectValue</c> and not a duplicate of it: that one is
    /// <c>IEffectValueReader</c> for stat aggregation and refuses a non-<c>FLAT</c> <c>valueMode</c> on
    /// a stat op; this is <c>IScaledValueReader</c> for the op layer and must not, since the value mode
    /// is exactly what the ops apply on top.
    /// </remarks>
    private sealed class BattleScaledValue : IScaledValueReader
    {
        private readonly EffectEvaluationContext _context;

        internal BattleScaledValue(EffectEvaluationContext context) => _context = context;

        public double ScaledValue(EffectDefinition effect) =>
            ValueScaleEvaluator.EffectiveValue(effect, _context);
    }

    /// <summary><c>STAT_COPY</c> reading — the start-of-tick snapshot, so mutual copies cannot recurse. This is <see cref="BattleActor.StartOfTickStats"/> and never <see cref="BattleActor.Stats"/>.</summary>
    private sealed class BattleStatReader : IResolvedStatReader
    {
        public double FinalStat(IEffectActorView actor, StatId stat) => Snapshot(actor)[stat];

        public StatId HighestPercentBonusStat(IEffectActorView actor) =>
            Battle(actor).Flow.HighestPercentBonusStat() ??
            throw new EffectContextException(
                nameof(EffectOp.STAT_COPY),
                $"'{actor.Id}' carries no percent bucket for HIGHEST_PCT_BONUS to name",
                "`18` §2.4 reads 'whichever stat carries the largest percent bucket at copy time'. " +
                "With no bucket there is no largest, and naming an arbitrary stat would make " +
                "Cogitator's Recalibrate copy something the fight never granted (steering S6).");

        private static ActorStats Snapshot(IEffectActorView actor) =>
            Battle(actor).StartOfTickStats ??
            throw new EffectContextException(
                nameof(EffectOp.STAT_COPY),
                $"'{actor.Id}' has no start-of-tick snapshot",
                "The snapshot is frozen at the top of every tick, before slot 1. Reaching this reader " +
                "before the first tick means a STAT_COPY resolved inside the battle-start pre-tick, " +
                "where `18` §2.4's 'so mutual copies cannot recurse' has nothing to read.");

        private static BattleActor Battle(IEffectActorView actor) =>
            actor as BattleActor ??
            throw new InvalidOperationException(
                $"A {actor.GetType().Name} reached the resolved-stat reader. A battle has one roster " +
                "and one view of it (IEffectActorView); a second implementation means two rosters.");
    }

    /// <summary>Per-actor flow state, and the roster half of the op that needs one.</summary>
    private sealed class BattleFlowSink : ICombatFlowSink
    {
        private readonly BattleSimulation _battle;

        internal BattleFlowSink(BattleSimulation battle) => _battle = battle;

        public void ExtraAttack(
            IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId)
        {
            var from = Actor(attacker);
            var to = Actor(target);

            // "Perform an additional attack IMMEDIATELY" — inside the cascade, not by zeroing the
            // cooldown, which would merely let slot 4 fire it on some later tick and would stack with
            // the real one.
            for (var i = 0; i < attacks && from.IsAlive && to.IsAlive; i++)
            {
                _battle.ResolveBasicAttack(from, to);
            }
        }

        public void GrantAttackMultiplierCharges(
            IEffectActorView holder, double multiplier, int charges, string sourceEffectId) =>
            Actor(holder).Flow.GrantAttackMultiplier(multiplier, charges, sourceEffectId);

        public void GrantForcedCritCharges(IEffectActorView holder, int charges, string sourceEffectId) =>
            Actor(holder).Flow.GrantForcedCrits(charges);

        public void ReduceCooldowns(IEffectActorView target, double fraction, string sourceEffectId) =>
            throw new EffectContextException(
                sourceEffectId,
                "REDUCE_COOLDOWN is not wired — `18` §2.4 reduces 'pet/boss ability cooldowns' and " +
                "neither exists yet",
                "`05` §3.1 slot 5's pet abilities and `17`'s boss ability cooldowns are the two things " +
                "this op reduces; the tick loop holds the BASIC-attack cooldown, which `18` §2.4 does " +
                "not name. Reducing that instead would give the op a visible effect that is the wrong " +
                "one. M2-12 brings the boss half; the pet half is the hero/pet milestone's.");

        public void ArmSurviveLethal(IEffectActorView holder, double hp, string sourceEffectId)
        {
            var actor = Actor(holder);
            actor.Flow.ArmDeathSave(new DeathSave(
                hp, IsRevive: false, sourceEffectId, FiresOnce(actor, sourceEffectId)));
        }

        public void ArmRevive(IEffectActorView holder, double hp, string sourceEffectId)
        {
            var actor = Actor(holder);
            actor.Flow.ArmDeathSave(new DeathSave(
                hp, IsRevive: true, sourceEffectId, FiresOnce(actor, sourceEffectId)));
        }

        public void Summon(
            IEffectActorView summoner, string archetype, int count, int? maxAlive, string sourceEffectId)
        {
            var owner = Actor(summoner);

            for (var i = 0; i < count; i++)
            {
                if (maxAlive is { } cap && _battle.LivingSummonsOf(owner) >= cap)
                {
                    return;
                }

                var plan = _battle._seams.Summons.Spawn(owner, archetype, sourceEffectId);
                _battle.AdmitSummon(plan with { OwnerId = owner.Id });
            }
        }

        // RANDOM_OUTCOME's winner, routed to the seam that knows what an effect id IS: the
        // intra-Rules layering forbids the op layer from naming a boss type, so the op validates and
        // draws (exactly one weighted pick) and this carries the id across. The strict default
        // throws naming the missing boss engine — the seam is only reached because authored content
        // rolled.
        public void RandomOutcome(IEffectActorView holder, string chosenEffectId, string sourceEffectId) =>
            _battle._seams.Outcomes.Resolve(Actor(holder), chosenEffectId, sourceEffectId);

        public void ClearSummons(IEffectActorView owner, string sourceEffectId)
        {
            var summoner = Actor(owner);

            foreach (var actor in _battle._actors)
            {
                if (actor.IsSummon && actor.OwnerId == summoner.Id && !actor.Removed)
                {
                    actor.Despawn();
                }
            }
        }

        public void SetTargetPriority(IEffectActorView target, double priority, string sourceEffectId) =>
            Actor(target).TargetPriority = priority;

        public void AddDamageTakenMultiplier(
            IEffectActorView target, double multiplier, EffectDuration? duration, string sourceEffectId) =>
            Actor(target).Flow.AddDamageTakenMultiplier(multiplier, sourceEffectId);

        public void AddPercentBucket(
            IEffectActorView holder, StatId stat, double fraction, EffectDuration? duration, string sourceEffectId)
        {
            var actor = Actor(holder);
            actor.Flow.AddPercentBucket(stat, fraction);
            actor.InvalidateStats();
        }

        private static BattleActor Actor(IEffectActorView view) =>
            view as BattleActor ??
            throw new InvalidOperationException(
                $"A {view.GetType().Name} reached the `18` §2.4 flow sink. The flow state lives on the " +
                "roster's own actor; a foreign view means the op is writing into a second roster that " +
                "the tick loop will never read.");
    }

    /// <summary>The four basic stat ops, at the moment a trigger fires one. The other half of aggregation's "collect all active effects": <c>RefreshStats</c> folds every live <see cref="TriggeredStatInstance"/> this writes into the same aggregation pass that reads <see cref="BattleActor.StandingEffects"/>.</summary>
    private sealed class BattleTriggeredStatSink(BattleSimulation battle) : ITriggeredStatSink
    {
        /// <summary>
        /// The canonical stacking block — <c>{"mode": "ADDITIVE", "maxStacks": 1}</c> — for an effect
        /// that authors none. A single-application cap of 1 changes nothing for the authored triggered
        /// stat ops that omit <c>stacking</c> today: every one of them either fires at most once or is
        /// phase-scoped and so cannot re-fire before it ends. Recorded as an assumption rather than
        /// left implicit — a future author giving one of them a real re-firing cadence has to name a
        /// mode explicitly, and this default will refuse a second application rather than silently
        /// changing what it means.
        /// </summary>
        private static readonly EffectStacking DefaultStacking =
            new() { Mode = StackingMode.ADDITIVE, MaxStacks = 1 };

        public void Apply(
            IReadOnlyList<IEffectActorView> targets, EffectOp op, StatId stat, double value,
            EffectDuration? duration, EffectStacking? stacking, string sourceEffectId)
        {
            var application = new EffectApplication
            {
                EffectId = sourceEffectId,
                Duration = duration,
                AppliedAtSeconds = BattleClock.SecondsAt(battle.Tick),

                // The PHASE half, restated for this store: a phase-scoped effect ends at the exit of
                // the phase in which it was applied, so the phase is stamped HERE, at application,
                // exactly as StatusTimeline.Apply stamps it — not re-read at expiry.
                AppliedInPhase = battle.CurrentBossPhase,
            };

            foreach (var view in targets)
            {
                var actor = Actor(view);
                var store = actor.TriggeredStatFirings;

                if (store.TryGetValue(sourceEffectId, out var instance))
                {
                    // Reapplication: stacking and its duration refresh, and nothing else — the op
                    // and the stat are the FIRING effect's and cannot change between one activation
                    // and the next of the same id.
                    instance.Reapply(value, application);
                }
                else
                {
                    store[sourceEffectId] = new TriggeredStatInstance
                    {
                        EffectId = sourceEffectId,
                        Op = op,
                        Stat = stat,
                        Stacks = EffectStackSet.Empty(sourceEffectId, stacking ?? DefaultStacking)
                            .Apply(value).Stacks,
                        Application = application,
                    };
                }

                // Redundant with ResolveFired's blanket invalidation of every actor after every
                // firing, and kept explicit anyway — the same convention BattleFlowSink.AddPercentBucket
                // follows, so a caller reading this seam in isolation is not left to trust an
                // invalidation that happens somewhere else.
                actor.InvalidateStats();
            }
        }

        private static BattleActor Actor(IEffectActorView view) =>
            view as BattleActor ??
            throw new InvalidOperationException(
                $"A {view.GetType().Name} reached the M2-R1 triggered-stat sink. The store lives on " +
                "the roster's own actor; a foreign view means the op is writing into a second roster " +
                "the tick loop will never read.");
    }

    /// <summary>The run/board queue, both ways in: the op seam and the trigger-routing seam, translated into the one <c>RunEffectQueued</c> event.</summary>
    private sealed class BattleRunEffects : IRunEffectQueue, IRunEffectSink
    {
        private readonly BattleSimulation _battle;

        internal BattleRunEffects(BattleSimulation battle) => _battle = battle;

        public void Queue(EffectDefinition effect, IEffectActorView source, double argument) =>
            Append(_battle.Tick, source, effect, argument);

        public void QueueRunEffect(int tick, IEffectActorView source, EffectDefinition effect, double argument) =>
            Append(tick, source, effect, argument);

        private void Append(int tick, IEffectActorView source, EffectDefinition effect, double argument) =>
            _battle.Log.AppendRunEffectQueued(
                tick,
                source is BattleActor actor ? actor.LogId : CombatActor.None,
                _battle.EffectIndexOf(effect),
                argument);
    }

    /// <summary>"At most their authored <c>once</c> count per battle", read off the arming effect's trigger.</summary>
    /// <remarks>
    /// Read off the ARMING actor's own holdings and not the roster's. Two actors can hold the same
    /// authored effect id with different triggers — a boss and its summon both carrying a phase block
    /// — and a first-match scan of the whole roster would answer for whichever came first in index
    /// order, which is not the one that armed the save.
    /// </remarks>
    private static bool FiresOnce(BattleActor holder, string sourceEffectId)
    {
        foreach (var held in holder.Plan.Effects)
        {
            if (string.Equals(held.Effect.Id, sourceEffectId, StringComparison.Ordinal))
            {
                return held.Effect.Trigger?.Once == true;
            }
        }

        return false;
    }

    private int LivingSummonsOf(BattleActor owner) =>
        _actors.Count(a => a.IsSummon && a.OwnerId == owner.Id && a.IsAlive);
}
