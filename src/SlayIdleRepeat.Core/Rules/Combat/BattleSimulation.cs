using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Effects.Values;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 The three `18` §2.2 value-mode bases that exist only <b>inside the moment that fired</b>, as
/// the tick loop carries them from the event to <see cref="EffectOpContext"/>.
/// </summary>
/// <param name="DamageDealt">
/// `18` §2.2's <c>DAMAGE_DEALT_PCT</c> basis — 🔒 `05` §4 step 8's <em>on-damage basis</em>, the
/// post-mitigation, post-floor hit <b>before</b> ward absorption, so a leech on an <c>ON_HIT</c>
/// still reads a fully-warded hit (`05` §4.1).
/// </param>
/// <param name="HealAmount">`05` §4.3's <c>healed</c> — <c>HEAL_AMOUNT</c>'s subject in an <c>ON_HEAL</c>.</param>
/// <param name="OverhealAmount">`05` §4.3's <c>overheal</c> — <c>OVERHEAL_AMOUNT</c>'s subject, likewise.</param>
/// <remarks>
/// 🔒 <b>Every member is nullable and none defaults to 0.</b> <c>OpValue</c> throws rather than
/// reading zero for a mode whose basis the context does not carry (steering S6), and that refusal is
/// what makes `18` §2.2's <em>"exist only inside <c>ON_HEAL</c> contexts"</em> enforceable at all. A
/// zero here would turn it into a silent no-op.
/// </remarks>
internal readonly record struct EventReadings(
    double? DamageDealt = null, double? HealAmount = null, double? OverhealAmount = null);

/// <summary>
/// 🔒 `05` §3.1 — the battle-start pre-tick and the strict eight-step tick loop, for one fight.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THE TICK ORDER, AND WHERE EACH SLOT'S WORK LIVES</b> ═══
/// </para>
/// <code>
/// PreTick (once, before tick 0)
///   0a  aggregate every actor's stats (`18` §8); attackCooldown = 0 for every opener
///   0b  fire every ON_BATTLE_START — hero side first, then enemies by index,
///       within one actor in ascending effect-id order
///   0c  the boss's phase 1 counts as entered                       → IBossPhases   (M2-12)
///   0d  emit BattleStart
///
/// for tick in 0..MaxTicks-1
///   1   status timers advance; DoT/HoT cadence boundaries apply    → IStatusTimeline (M2-10)
///   2   statuses whose duration reached 0 expire                   → IStatusTimeline (M2-10)
///   2a  boss telegraphs — `17` §1's 1.0–1.5 s wind-up             → IBossPhases     (M2-12)
///   3   PERIODIC triggers fire — the ONLY PERIODIC path            → TriggerRegistry.PeriodicDue
///   4   basic attacks in fixed initiative order                    → IAttackPipeline (M2-09)
///   5   pet ability cooldowns advance                              → IPetAbilities
///   6   deaths resolve — ON_DEATH, then removal
///   7   (nothing: events are appended as they happen, by everyone)
///   8   if hero dead OR all enemies dead: break
/// </code>
/// <para>
/// ═══ <b>THE FOUR THINGS THIS CLASS OWNS THAT NOTHING ELSE CAN CHECK</b> ═══
/// </para>
/// <list type="number">
///   <item>
///     🔴 <b>Battle time is <c>BattleClock.SecondsAt(tick)</c>, never accumulated.</b> See
///     <see cref="BattleClock"/> — an accumulator puts <c>SYS_ENRAGE</c>'s <c>startDelay: 70.0</c> at
///     <c>69.99999999999967</c> and the enrage starts a tick late on one architecture and not the
///     other.
///   </item>
///   <item>
///     🔴 <b>The attack cooldown is rounded at every accumulation point</b> (`05` §1.1), and the
///     defect it closes hides in a specific place. Unrounded, twenty subtractions of <c>0.05</c> from
///     <c>1.0</c> land on <c>-3.19e-16</c> — <em>below</em> zero — so a 1.0-ASPD actor still fires on
///     tick 20 and looks correct. Ten subtractions from <c>0.5</c> land on <c>+6.94e-17</c>, so a
///     <b>2.0-ASPD</b> actor waits an eleventh tick: 164 swings across a 90 s fight instead of 180, a
///     9% DPS error on exactly the fast builds `05` §9's harness calibrates against. Both cadences
///     are pinned by <c>AttackCadenceTests</c>, because which side of zero the residue lands on is
///     not a property to leave to chance.
///   </item>
///   <item>
///     🔒 <b>Initiative is fixed</b> (`05` §3.1) — hero, then enemies by index, pets never. It is
///     computed by <see cref="InitiativeOrder"/> and nowhere else, which is the seam `05` §3.3's
///     <em>"the attacker's side acts first"</em> replaces. M2-14 overrides that one method.
///   </item>
///   <item>
///     🔒 <b><c>PeriodicDue</c> is called once per actor per tick, in actor order</b>, and
///     <see cref="TriggerRegistry.Evaluate"/> is never called with a <c>PERIODIC</c> — the registry
///     throws on that, which is what keeps the R8 schedule from being advanced twice for one tick.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>A stateful class under <c>Rules/</c></b>, on <c>CombatLog</c>'s precedent and for its
/// reason — a 1800-tick loop is an accumulator. One instance per fight, one caller, never static.
/// </para>
/// </remarks>
internal sealed class BattleSimulation
{
    /// <summary>
    /// 🔒 The <c>sourceEffectId</c> a basic attack carries. Not an effect id and it cannot collide
    /// with one: `18` §8 makes an authored effect id an identifier, and no identifier starts with
    /// <c>(</c>.
    /// </summary>
    /// <remarks>
    /// A basic attack is `05` §4's pipeline invoked by the tick loop rather than by a DSL op, so it
    /// has no `18` §8 id to give. Named and greppable rather than an empty string, which would read
    /// as an absent id (steering S6).
    /// </remarks>
    internal const string BasicAttackSourceId = "(basic attack)";

    /// <summary>
    /// A defence-in-depth bound on `05` §3.1's <em>"trigger cascades resolve depth-first"</em>.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Not a game rule and not a tunable.</b> `05` §3.1 names two anti-loop rules — thorns
    /// never re-triggers thorns, and the two death saves fire at most their authored <c>once</c> —
    /// and both are implemented where they belong (M2-09's reflect, and <c>CombatFlowState</c>). This
    /// is the backstop for a cascade nobody anticipated: content that is mutually triggering would
    /// otherwise overflow the stack, which reports as a process crash rather than as the authoring
    /// error it is. Reached only by content, never by the loop.
    /// </remarks>
    private const int MaxCascadeDepth = 64;

    private readonly BattlePlan _plan;
    private readonly List<BattleActor> _actors;
    private readonly BattleSeams _seams;
    private readonly BattleServices _services;
    private readonly BattleFlowSink _flow;
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
        Rng = new DeterministicRng(_plan.BattleSeed, RngStreams.Combat);
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
        _runEffects = new BattleRunEffects(this);

        // Resolved once, here rather than lazily: `05` §3.2 gives a side one hero and no op adds or
        // removes one (a summon is always an enemy, `18` §2.4). A `??=` would never latch a null and
        // would re-scan the roster at slot 8 of every tick.
        _hero = _actors.First(a => a.Side == BattleSide.HERO && a.Kind == EffectActorKind.HERO);
    }

    /// <summary>This fight's log.</summary>
    internal CombatLog Log { get; }

    /// <summary>🔒 `14` §8.1's combat stream: <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>.</summary>
    internal DeterministicRng Rng { get; }

    /// <summary>This battle's trigger registry — one per battle, over the <b>run's</b> counters.</summary>
    internal TriggerRegistry Triggers { get; }

    /// <summary>`05` §3 / §3.3's bounds.</summary>
    internal CombatRules Rules => _plan.Rules;

    /// <summary>🔒 `05` §4's two 📐 dials, as the plan was given them (`combat_caps.json`).</summary>
    internal MitigationConstants Mitigation => _plan.Mitigation;

    /// <summary>🔒 `05` §4.1's 📐 ward pool ceiling, likewise.</summary>
    internal double WardCapPct => _plan.WardCapPct;

    /// <summary>Every actor in `05` §3.1 index order, summons appended.</summary>
    internal IReadOnlyList<BattleActor> Actors => _actors;

    /// <summary>The tick being run. <c>0</c> during the pre-tick.</summary>
    internal int Tick { get; private set; }

    /// <summary>🔒 Runs the pre-tick and the loop, and seals the log.</summary>
    internal SimulationResult Run()
    {
        PreTick();

        var ranTicks = 0;

        for (Tick = 0; Tick < Rules.MaxTicks; Tick++)
        {
            ranTicks = Tick + 1;

            // 🔒 The start-of-tick snapshot `18` §2.4 reads is taken AFTER the aggregation is
            // brought up to date, so a STAT_COPY reads the block as it stands at the top of this
            // tick rather than as it stood before last tick's ops landed.
            foreach (var actor in _actors)
            {
                RefreshStats(actor);
                actor.FreezeStartOfTick();
            }

            // ── 1 · status timers and DoT/HoT cadence (M2-10) ────────────────────────────────
            //
            // Indexed rather than foreach, here and in every slot below: slot 3 can summon, `05`
            // §3.1 appends a summon to the roster, and a foreach over a mutated list throws.
            for (var i = 0; i < _actors.Count; i++)
            {
                _seams.Timeline.AdvanceTimers(_actors[i], Tick);
            }

            // ── 2 · expiries, ascending effect-id order (M2-10) ──────────────────────────────
            for (var i = 0; i < _actors.Count; i++)
            {
                _seams.Timeline.ExpireDue(_actors[i], Tick);
            }

            // ── 2a · boss telegraphs — `17` §1's 1.0-1.5 s wind-up (M2-12) ──────────────────
            //
            // 🔒 Not one of `05` §3.1's eight slots, and added deliberately rather than folded into
            // one: a wind-up is emitted AHEAD of the firing it announces, so nothing that happens at
            // the firing can raise it, and `05` §3.1 makes no per-tick call into IBossPhases at all.
            // It sits before slot 3 because slot 3 is what advances a PERIODIC's schedule, and the
            // pass reads TriggerInstance.NextFiringTick. NoBossPhases.AdvanceTick is a no-op, so a
            // fight with no boss logs — and hashes — exactly as it did before this slot existed.
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

        // ON_BATTLE_END - TriggerRegistry's contract: "after slot 8's break, at the fight's last
        // tick, with TriggerOccurrence.HeroWon set from the outcome. It is the only kind that reads
        // that field." Nothing else in the repository writes it, so `18` §9.2's PET_DICEBEAST
        // win-only grant would silently do nothing without this sweep.
        //
        // `18` §2.5 fixes its position relative to the run queue — the queue is applied "after the
        // outcome is fixed, before ON_BATTLE_END effects are granted" - which is why the outcome is
        // computed first, and where M3's drain goes.
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

    /// <summary>🔒 `05` §3.1's battle-start pre-tick — steps 0a, 0b, 0c and 0d, in that order.</summary>
    private void PreTick()
    {
        Tick = 0;

        // ── 0a · aggregate every actor's stats; attackCooldown = 0 for every opener.
        //
        // 🔒 Every actor in the opening roster is an opener: `05` §3.1 says "attackCooldown = 0 for
        // all battle-opening actors (the first basic attack lands on tick 0)". A summon is not one,
        // and AdmitSummon gives it a full cooldown instead.
        foreach (var actor in _actors)
        {
            RefreshStats(actor);
            actor.AttackCooldown = 0.0;
        }

        // ── 0a/0b · register every effect active at battle start, at activationTick 0. That is the
        //    R8 anchor for the perk periodics and for SYS_ENRAGE, which is BATTLE-scoped and
        //    therefore anchors here rather than at any phase.
        foreach (var actor in _actors)
        {
            RegisterHoldings(actor, 0);
        }

        // ── 0b · fire ON_BATTLE_START: hero side first (hero, then pets in slot order), then
        //    enemies by index; within one actor in ascending effect-id order.
        //
        // 🔒 The order is MATERIALISED before the walk, for step 0c's reason below: BattleStartOrder()
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
        // 🔴 Indexed over a count taken BEFORE the walk, exactly as the tick loop's slots 2a-6 are and
        // for the identical reason: entering phase 1 fires ON_PHASE_ENTER, an ON_PHASE_ENTER may be a
        // SUMMON, and `05` §3.1 appends a summon to `_actors` — so a foreach here throws
        // "Collection was modified". This is not hypothetical: BOSS_OSSUARY_KING's phase 1 carries
        // BOSS_OSSUARY_KING_P1_COURT, a SUMMON on ON_PHASE_ENTER phase 1, and every Chapter 3 boss
        // fight crashed on tick 0 before this loop was indexed. It went unseen because the three
        // scripts M2-12 exercised summon on phase 2 or 3, where the tick loop's already-indexed walks
        // admit the summon safely; the pre-tick was the one walk left as a foreach.
        //
        // The bound is taken before the walk rather than re-read each step so that a boss summoned by
        // another boss's phase 1 cannot have its own phase 1 entered in the same pass — `05` §3 has one
        // boss per fight, and a roster that grew into a second one mid-pre-tick would enter phases in
        // an order that depends on how many adds landed first.
        var opening = _actors.Count;
        for (var i = 0; i < opening; i++)
        {
            if (_actors[i].IsBoss)
            {
                _seams.Phases.EnterInitialPhase(_actors[i], 0);
            }
        }

        // ── 0d · emit BattleStart. Last, so every ward grant and opening buff of 0b already sits
        //    before it in the log — which is what a replayer needs to draw the opening banner over
        //    an arena that is already in its starting state.
        Log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);
    }

    /// <summary>
    /// 🔒 `05` §3.1 step 0b's order — <em>"hero side first (hero, then pets in slot order), then
    /// enemies by index"</em>, which is also `05` §3.3's <em>"the attacker's side acts first"</em>.
    /// </summary>
    /// <remarks>
    /// Stated as a side-then-index sort rather than as the plain index order the rest of the loop
    /// uses, because they are not the same claim: `05` §3.3's duel puts a <em>hero</em> on the enemy
    /// side, and the index order alone would then interleave the two sides' openers by position.
    /// <para>
    /// 🔒 <b>The two documents ask for the same sequence and it is written once.</b> §3.1 step 0b
    /// names the hero side first; §3.3 names the attacker's side first, and a duel's attacker
    /// <em>is</em> the hero side (<c>CombatActor</c>, <c>BattleSide</c>). They will not drift, because
    /// they are one document.
    /// </para>
    /// <para>
    /// ⚠️ <b>Three callers, and only two of them are §3.1 step 0b.</b> <see cref="PreTick"/>'s
    /// <c>ON_BATTLE_START</c> sweep is the quoted rule; <see cref="ActingOrder"/> is `05` §3.3's duel
    /// initiative, which is the same sequence for the reason above. The third — <see cref="Run"/>'s
    /// <c>ON_BATTLE_END</c> sweep — is <b>neither</b>: no document orders it, and it takes this order
    /// because a battle's closing sweep matching its opening one is the least surprising choice. It
    /// therefore inherits the duel's side-first sequence too. Stated because it is a ruling, not a
    /// quotation.
    /// </para>
    /// </remarks>
    private IEnumerable<BattleActor> BattleStartOrder() =>
        _actors.Where(a => a.Side == BattleSide.HERO).OrderBy(a => a.Index)
            .Concat(_actors.Where(a => a.Side != BattleSide.HERO).OrderBy(a => a.Index));

    /// <summary>
    /// 🔒 The order the tick loop's two acting slots walk the roster in — `05` §3.1's index order in
    /// PvE, and `05` §3.3's <em>attacker's side first</em> in a duel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ═══ 🔒 <b>`05` §3.1 AND `05` §3.3 BOTH HOLD, AND THIS IS WHERE THEY MEET</b> ═══
    /// </para>
    /// <para>
    /// `05` §3.3's row reads <em>"within a tick: the <b>attacker's side acts first</b> (hero, then pet
    /// abilities), then the defender's side"</em>. Read as a licence to interleave, it would move a
    /// pet ability (slot 5) in front of a basic attack (slot 4) — overriding `05` §3.1's 🔒 eight-slot
    /// order with a parenthetical. ⚠️ <b>Errata, recorded rather than resolved:</b> the reading
    /// implemented is the one that leaves both locked statements true — §3.1 keeps its slots, and §3.3
    /// orders the <b>sides</b> <em>within</em> each of them. The parenthetical then enumerates what a
    /// side's acting consists of (its hero's swing, its pets' abilities) rather than fusing the slots.
    /// </para>
    /// <para>
    /// 🔴 <b>On a well-formed duel roster this changes nothing observable, and that is the trap.</b>
    /// <c>CombatActor</c>'s layout gives the attacker's side indices <c>0..3</c> and the defender's
    /// side <c>4..7</c>, so side-then-index and plain index order coincide — and slot 4 sees only the
    /// two heroes, because `05` §3.2 keeps pets out of it entirely. A test written against a
    /// conventional roster passes identically with this method and without it.
    /// <c>PvpDuelTests.The_override_is_invisible_on_a_conventionally_indexed_duel</c> pins that
    /// finding, and the rest of that suite probes on a roster whose indices are deliberately inverted
    /// — M2-05's technique, which pinned <c>ENEMY_COUNT</c> with a stray actor and
    /// <c>TARGET_IS_ELITE</c> with a mislabelled ghost for exactly this reason. §3.3 states a rule
    /// about sides; an implementation that only worked because the indices happened to agree would be
    /// a coincidence, and one an ill-formed ghost would break in production and nowhere else.
    /// </para>
    /// <para>
    /// 🔒 <b>Gated on <see cref="CombatRules.IsPvp"/> and on nothing else.</b> A second flag saying
    /// "order by side" would be a second statement of "is this a duel"; `18` §4's <c>IS_PVP</c> is
    /// already that fact, and <c>CombatRules</c>' own remarks refuse the duplicate.
    /// </para>
    /// <para>
    /// ⚠️ <b>SCOPE: slots 4 and 5, and NOT slots 1, 2 or 3 — a boundary, not an omission.</b> `05`
    /// §3.3's row is titled <em>Initiative</em> and enumerates what it reorders: <em>"hero, then pet
    /// abilities"</em>. Those are slot 4 and slot 5 exactly. It says nothing about status timers
    /// (slot 1), expiries (slot 2) or <c>PERIODIC</c>s (slot 3), which keep `05` §3.1's actor order in
    /// a duel as in a fight. ⚠️ <b>The consequence is real and is recorded rather than smoothed
    /// over:</b> on a roster whose indices are not side-grouped, slot 3 fires the defender's
    /// <c>PERIODIC</c>s first while slot 4 swings the attacker first — and slot 3 draws, so that is
    /// visible in <c>LogHash</c>. Extending the order to the other three slots is a one-line change —
    /// route their walks through this method — and it is deliberately <b>not</b> made here, because
    /// `05` §3.3 does not authorise it and inventing the extension would be a rule the document did
    /// not write (steering S6).
    /// </para>
    /// <para>
    /// ⚠️ <b>The alternative that was considered and not taken:</b> making this unconditional, since
    /// <c>_actors</c> is already built in index order and side-then-index differs from it only on an
    /// ill-formed roster — so `05` §3.1's own <em>"Hero, then enemies by index"</em> arguably reads as
    /// side-first too. It was rejected as the wrong risk to take here: it would change PvE ordering on
    /// exactly the rosters nothing else in the repository constrains, and PvE ordering is inside
    /// <c>LogHash</c> and inside the committed reference vectors. Taking the PvE half needs its own
    /// task and its own re-baseline; the duel half is this one's and does not touch them.
    /// </para>
    /// </remarks>
    private IEnumerable<BattleActor> ActingOrder() =>
        Rules.IsPvp ? BattleStartOrder() : _actors.OrderBy(a => a.Index);

    // ══════════════════════════════════════════════════════════════════ the eight slots

    /// <summary>
    /// 🔒 Slot 3 — <c>PERIODIC</c> triggers, <em>"in actor order … within one actor in ascending
    /// effect-id order"</em>. <see cref="TriggerRegistry.PeriodicDue"/> imposes the second order and
    /// advances the schedules; this imposes the first.
    /// </summary>
    private void RunPeriodics()
    {
        // The roster is bounded BEFORE the walk, for slot 4's reason: a PERIODIC can summon, and
        // `05` §3.1 gives a summon a full cooldown so that it "never attacks on its spawn tick". An
        // unbounded walk would reach the newcomer in the same slot 3 and fire its just-registered
        // periodics at their own anchor tick, which is neither what R8 means nor consistent with the
        // shielding slot 4 already has.
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

    /// <summary>
    /// 🔒 Slot 4 — basic attacks, in <see cref="InitiativeOrder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two sub-steps are `05` §3.1's exactly, and their order is load-bearing: <b>4a</b> fires
    /// when <c>attackCooldown &lt;= 0</c> and sets it to <c>1.0 / ASPD</c>, and <b>4b</b> then
    /// subtracts <c>TICK</c> — <b>whether or not 4a fired</b>. Subtracting only on the non-firing
    /// path would make every attack land one tick late.
    /// </para>
    /// <para>
    /// 🔒 <b>ASPD is read at fire time</b>, from the live block rather than the start-of-tick
    /// snapshot: `05` §3.1 says so, and it is what makes <c>HASTE</c> applied by a slot-3 periodic
    /// shorten the very next cooldown rather than the one after.
    /// </para>
    /// </remarks>
    private void RunBasicAttacks()
    {
        foreach (var attacker in InitiativeOrder())
        {
            if (attacker.AttackCooldown <= 0.0 && attacker.IsAlive && _seams.Timeline.CanAct(attacker))
            {
                var target = SelectTarget(attacker);

                // The cooldown is consumed only by a swing that actually RESOLVED. An ON_ATTACK
                // trigger can finish the target, and `05` §3.1 step 6 puts it out of play at that
                // moment — leaving the actor with nothing to hit, which is the same situation as
                // `target is null` above and must cost the same: nothing.
                if (target is not null && ResolveBasicAttack(attacker, target))
                {
                    // "ASPD read at FIRE TIME" — the live block, brought up to date here rather
                    // than the start-of-tick snapshot, so a HASTE applied by this tick's slot 3
                    // shortens THIS cooldown.
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

            // 4b — 🔴 rounded at the accumulation point (`05` §1.1). Raw, ten subtractions of 0.05
            // from 0.5 leave +6.94e-17 rather than 0, and a 2.0-ASPD actor then waits an eleventh
            // tick — 164 swings a fight instead of 180. (From 1.0 the residue is negative and the
            // defect is invisible, which is why the 2.0 case is the one that pins this.)
            attacker.AttackCooldown = StatRounding.Round(
                attacker.AttackCooldown - BattleClock.TickSeconds);
        }
    }

    /// <summary>
    /// 🔒 `05` §3.1 — <em>"fixed initiative order (Hero, then enemies by index — pets never
    /// basic-attack)"</em>. <b>Fixed, not randomised</b>: <em>"this removes a whole class of
    /// nondeterminism."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 <b>In a duel the sequence is <see cref="ActingOrder"/>'s</b> — `05` §3.3's <em>"within a
    /// tick: the <b>attacker's side acts first</b> … then the defender's side"</em>. It is a different
    /// sequence over the same roster, not a different rule about cooldowns or targets, so nothing
    /// else in slot 4 changes. Read that method before touching this one: on a conventionally indexed
    /// duel the two orders coincide, which is why the claim is probed on an inverted roster.
    /// <para>
    /// Materialised before the walk, deliberately: an attack can kill, summon or revive, and slot 4's
    /// order is <em>fixed</em> — an actor summoned by the third enemy's swing does not act in the same
    /// tick, because `05` §3.1 gives it a full cooldown and puts it at the end of the list.
    /// </para>
    /// <para>
    /// 🔒 <b>Held between ticks, and that is not a cache of a live reading.</b> `05` §3.1 calls the
    /// order <em>fixed</em>: it is a function of the roster's membership, sides and indices, none of
    /// which changes except when a summon is admitted — and <see cref="AdmitSummon"/> clears it. A
    /// death does not change it, because the <c>alive</c> test is inside the walk where `05` §3.1
    /// puts it, not in the order. Rebuilding it 1800 times a fight was 5% of `05`'s whole budget.
    /// </para>
    /// </remarks>
    private List<BattleActor> InitiativeOrder() =>
        _initiative ??= Acting(a => a.Kind != EffectActorKind.PET);

    /// <summary>🔒 `05` §3.2 — who this actor swings at.</summary>
    private BattleActor? SelectTarget(BattleActor attacker) =>
        attacker.Kind == EffectActorKind.HERO
            ? TargetSelection.ForBasicAttack(ContextFor(attacker))
            : TargetSelection.ForEnemyAttack(attacker, _actors);

    /// <summary>
    /// 🔒 Slot 4a — one swing: <c>ON_ATTACK</c>, then `05` §4's pipeline, then the on-hit family
    /// depth-first in ascending effect-id order, with the phase check after every HP change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ═══ 🔒 <b>HOW SLOT 4 INVOKES <c>IAttackPipeline</c> — the M2-09 contract</b> ═══
    /// </para>
    /// <list type="number">
    ///   <item>The loop fires <c>ON_ATTACK</c> on the attacker <b>before</b> the swing resolves, so a
    ///   trigger that grants an <c>ATTACK_MULT_NEXT</c> charge applies to <em>this</em> swing.</item>
    ///   <item>It then reads `05` §4's <c>AttackMultiplier</c> out of <c>CombatFlowState</c> —
    ///   <b>base 1.0</b>, times every armed charge in ascending effect-id order, each spent. The
    ///   transient is never stored, which is how <em>"it resets to 1.0 after every resolved
    ///   attack"</em> holds.</item>
    ///   <item>It emits <c>Attack</c> and calls
    ///   <c>ResolveAttack(attacker, defender, multiplier, "(basic attack)")</c>. 🔒 <b>M2-09 owns
    ///   every event from there</b> — <c>Miss</c>, <c>Hit</c>, <c>Crit</c>, <c>Block</c>,
    ///   <c>Shield</c>, <c>WardBroken</c>, <c>Heal</c> — because `05` §4's <c>log(...)</c> calls sit
    ///   inside the pipeline and step 7 requires them appended as they happen.</item>
    ///   <item>M2-09 routes every HP change through <c>BattleServices.AfterHpDecrease</c>, which is
    ///   `05` §4 step 9's <c>PhaseCheck(defender)</c> and <c>ON_LOW_HP</c> in one call.</item>
    ///   <item>
    ///     The loop reads the returned <see cref="AttackResolution"/> and fires the on-hit family
    ///     from it, in <b>this order</b>: <c>ON_DODGE</c> on the defender (and nothing further -
    ///     `05` §4 step 1 returns); otherwise <c>ON_BLOCK</c> on the defender, then <c>ON_HIT</c>
    ///     and <c>ON_CRIT</c> on the attacker, then <c>ON_HIT_TAKEN</c> on the defender, then
    ///     <c>ON_KILL</c> on the attacker when the defender went down — <b>unless
    ///     <c>CombatRules.OnKillTriggersFire</c> is false</b>, which is `05` §3.3's duel rule.
    ///     <para>
    ///     WARNING: THE ORDER WITHIN THE FAMILY IS A RULING, NOT A QUOTATION. `05` §3.1 fixes only
    ///     that they resolve "immediately, depth-first, in ascending effect-id order" and says
    ///     nothing about which kind precedes which. It follows `05` §4's own step order - block is
    ///     step 5, the hit lands at step 9 - and puts the defender's reaction after the attacker's
    ///     so that a reactive effect sees the damage already applied. M2-09 must not reorder it
    ///     without saying so.
    ///     </para>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <returns>Whether the swing resolved. See the caller for why that decides the cooldown.</returns>
    private bool ResolveBasicAttack(BattleActor attacker, BattleActor defender)
    {
        FireTriggers(attacker, Occurrence(TriggerKind.ON_ATTACK, attacker), defender);

        // The defender may have died to an ON_ATTACK trigger's own damage. `05` §3.1 step 6 puts it
        // out of play at that moment, so the swing has nothing to land on.
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

            // A dodge IS a resolved swing: `05` §4 step 1 logs the MISS and returns, and the attacker
            // has taken its shot.
            return true;
        }

        // 🔒 `18` §2.2's DAMAGE_DEALT_PCT basis is `05` §4 step 8's ON-DAMAGE BASIS — the
        // post-mitigation, post-floor hit BEFORE ward absorption — and not step 9's HpLost. `05`
        // §4.1: "a lifesteal attacker still heals off a fully-warded hit". A leech on an ON_HIT
        // reading the post-absorption number would heal nothing off a shielded target, which is the
        // opposite of what that ruling says.
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

    /// <summary>
    /// 🔒 Slot 5 — <em>"pet ability cooldowns advance; ready abilities fire, pets in slot order."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 <b>In a duel, the attacker's side's pets before the defender's</b> — the other half of `05`
    /// §3.3's <em>"the attacker's side acts first (hero, then <b>pet abilities</b>)"</em>. See
    /// <see cref="ActingOrder"/> for why that is a within-slot ordering rather than an interleaving of
    /// slots 4 and 5, and why it is unobservable on a conventionally indexed roster.
    /// <para>
    /// Held between ticks for <see cref="InitiativeOrder"/>'s reason and cleared by the same event: a
    /// summon is never a pet (`18` §2.4 spawns enemies), so this list is even more stable than slot
    /// 4's — but it is invalidated alongside it rather than reasoned about separately, because "the
    /// roster changed" is one fact.
    /// </para>
    /// </remarks>
    private void RunPetAbilities()
    {
        // 🔴 Materialised into a LOCAL before the walk, and the field is never re-read inside it.
        // A pet ability can resolve a SUMMON (`18` §2.4), AdmitSummon nulls this cache, and a loop
        // whose bound is `_petOrder.Count` would then dereference null on its next iteration. Slot 4
        // is immune only by the accident of `foreach (… in InitiativeOrder())` evaluating the call
        // once; this slot indexes, so it has to hold the list itself. Unreachable today — the default
        // IPetAbilities is a no-op — which is exactly what would have made it a crash in a fight
        // rather than a wiring error on the day the hero/pet milestone lands one.
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

    /// <summary>
    /// 🔒 Slot 6 — <em>"every actor at 0 HP, in actor-index order, fires its <c>ON_DEATH</c> effects
    /// and is then removed."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Nothing here filters on liveness</b>, and that is M2-05's rule: <em>liveness filters
    /// selection, not naming</em>. `18` §7.10's Volatile elite explodes for 15% of hero Max HP from
    /// its own <c>ON_DEATH</c>, which it could never do if a dead actor's effects stopped being named.
    /// <para>
    /// Re-swept until it settles, because an <c>ON_DEATH</c> can kill: the Volatile explosion can
    /// finish a second enemy, and `05` §3.1's <em>"every actor at 0 HP"</em> is a statement about the
    /// slot, not about the roster as it stood when the slot began.
    /// </para>
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

    /// <summary>
    /// 🔒 Fires one moment over one actor's instances — <b>depth-first, in ascending effect-id
    /// order</b> (`05` §3.1 slot 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Driven one instance at a time through <see cref="TriggerRegistry.Evaluate"/>, not
    /// through <c>EvaluateAll</c></b>, and the registry says why: <em>"Not for a cascade that changes
    /// the roster. `05` §3.1 resolves on-hit triggers immediately, depth-first: if firing one
    /// instance can register another, or kill the holder, the tick loop must drive them one at a time
    /// and order them itself."</em> Every moment this method serves can do exactly that.
    /// </para>
    /// <para>
    /// The candidate list is ordered <b>before</b> anything fires, for the registry's reason —
    /// ordering after would put `05` §3.1's rule on the wrong side of the firings' side effects.
    /// </para>
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

        // Walked by index over BattleActor.Instances, which is ALREADY in `05` §3.1's ascending
        // effect-id order because AddInstance imposes it on insert. A firing can register another
        // instance on this actor, so the count is bounded first: a cascade resolves depth-first
        // through its own FireTriggers call, not by growing the list this one is walking.
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

    /// <summary>
    /// Resolves one fired effect — through `18` §2.5's routing first, so a combat trigger carrying a
    /// run/board op is <b>emitted, never resolved</b>.
    /// </summary>
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

        // 🔒 `18` §2.5 — the one boundary, decided in one place. `0` is the runtime-resolved
        // argument: the sanctioned case (the Dicelord's Scramble firing MODIFY_DIE_FACE) carries
        // faceIndex and newFace on the EffectDefinition, so every argument is authored.
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

        // Conservative, and it has to be THIS conservative: an op resolves onto `18` §5's whole
        // token set - ATTACKER, ALL_ENEMIES, ALL_PETS, LOWEST_HP_ENEMY, OTHER_ENEMIES — so the
        // subjects it touched are not knowable from here, and invalidating only the holder and the
        // current target would leave the rest reading a stale aggregation until something unrelated
        // dirtied them. It is a bool on at most nine objects, and the aggregation itself is still
        // gated by StatsAreStale, so the cost is the flag and not the pass.
        for (var i = 0; i < _actors.Count; i++)
        {
            _actors[i].InvalidateStats();
        }
    }

    /// <summary>
    /// One moment, for one holder. The attacker is <b>not</b> on it - <c>TriggerOccurrence</c> has no
    /// such field, and `18` §4's three <c>ATTACKER_IS_*</c> conditions read it off
    /// <c>EffectEvaluationContext.Attacker</c>, which <see cref="FireTriggers"/> carries separately.
    /// </summary>
    private TriggerOccurrence Occurrence(TriggerKind kind, BattleActor holder) =>
        new()
        {
            Kind = kind,
            Tick = Tick,
            IsPvp = Rules.IsPvp,
            HpFraction = holder.HpFraction,
        };

    /// <summary>
    /// 🔒 Registers every effect an actor holds, at <paramref name="activationTick"/> — the R8 anchor
    /// for its <c>PERIODIC</c>s.
    /// </summary>
    private void RegisterHoldings(BattleActor actor, int activationTick)
    {
        var minted = 0;

        foreach (var held in actor.Plan.Effects)
        {
            if (held.Effect.Trigger is null)
            {
                // An untriggered effect is a passive: `18` §1.1 re-evaluates it at every resolution
                // pass, which is the aggregation's business, not the registry's.
                continue;
            }

            var id = held.InstanceId ?? EffectInstanceId.Of(
                $"{actor.Id}#{held.Effect.Id}#{minted++.ToString(CultureInfo.InvariantCulture)}");

            Triggers.Register(id, held.Effect, activationTick, actor.HpFraction);
            actor.AddInstance(id, held.Effect);
        }
    }

    // ══════════════════════════════════════════════════════════════════ HP, deaths, summons

    /// <summary>
    /// 🔒 `18` §6 — the phase <b>this fight</b> is in, for a <c>PHASE</c>-scoped duration.
    /// <c>null</c> when the roster carries no boss, which is §6's <em>"outside a boss fight"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The fight's boss, singular, and the documents are what make that well-defined.</b> `17`
    /// §1 gives a boss node one boss and `18` §6 writes <em>"the boss"</em>; `05` §3.1's roster puts
    /// it at <c>CombatActor.FirstEnemy</c>. A roster carrying two bosses is outside every document,
    /// and this answers with the first in `05` §3.1 index order rather than inventing a rule for a
    /// case nothing authors — recorded here so the assumption is greppable rather than implied
    /// (steering S6).
    /// </para>
    /// <para>
    /// ⚠️ It walks the roster on each call rather than caching, because <c>AdmitSummon</c> appends to
    /// it mid-fight and a cached boss would be a second, staler answer to a question the seam
    /// already owns.
    /// </para>
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

    /// <summary>
    /// 🔒 `05` §3.1's pre-tick 0c and phase check — <em>"the boss's phase 1 counts as entered: fire
    /// its <c>ON_PHASE_ENTER(1)</c> effects"</em>, and the same for every later entry.
    /// </summary>
    /// <param name="boss">The boss that just entered a phase.</param>
    /// <param name="phase">The phase entered, <c>1..3</c>.</param>
    /// <remarks>
    /// 🔒 <b>A routing, not a second implementation.</b> <see cref="IBossPhases"/> decides <em>when</em>
    /// a phase is entered and what that does to `18` §6's scopes; what it cannot do for itself is
    /// resolve the effects the entry fires, because `18` §2.5's routing, `05` §3.1's cascade bound and
    /// the op seams are all the loop's. <see cref="FireTriggers"/> already walks the holder's
    /// instances in ascending effect-id order, which is the order `05` §3.1 gives this sweep.
    /// </remarks>
    internal void FirePhaseEntry(BattleActor boss, int phase)
    {
        ArgumentNullException.ThrowIfNull(boss);

        FireTriggers(boss, Occurrence(TriggerKind.ON_PHASE_ENTER, boss) with { Phase = phase });
    }

    /// <summary>
    /// 🔒 `18` §10.1 E6 — resolves the <b>one</b> effect a <c>RANDOM_OUTCOME</c>'s draw named, once
    /// <see cref="IBossOutcomes"/> has found it among the holder's own holdings.
    /// </summary>
    /// <param name="holder">The actor whose roll it was.</param>
    /// <param name="effect">The winning row's effect.</param>
    /// <remarks>
    /// 🔒 An outcome row carries <b>no trigger of its own</b> — the <c>RANDOM_OUTCOME</c>'s own
    /// cadence is the roll's — so it is never on the registry and cannot be reached through
    /// <see cref="FireTriggers"/>. This is the same resolution path every fired effect takes, which
    /// is what keeps `18` §2.5's routing and the cascade bound applying to it too.
    /// </remarks>
    internal void ResolveOutcome(BattleActor holder, EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(holder);

        ResolveFired(holder, effect, Occurrence(TriggerKind.PERIODIC, holder), target: null, attacker: null);
    }

    /// <summary>
    /// 🔒 `05` §3.1's phase check plus <c>ON_LOW_HP</c> — called after every HP decrease, by the loop
    /// and by M2-09/M2-10 through <see cref="BattleServices.AfterHpDecrease"/>.
    /// </summary>
    internal void AfterHpDecrease(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _seams.Phases.AfterHpDecrease(actor, Tick);

        AfterHpChange(actor);
    }

    /// <summary>
    /// <c>ON_LOW_HP</c> — `18` §3's crossing, observed after <b>every</b> HP change of the holder so
    /// that a firing cannot be lost between two observations (<c>TriggerRegistry</c>).
    /// </summary>
    internal void AfterHpChange(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        FireTriggers(actor, Occurrence(TriggerKind.ON_LOW_HP, actor));
    }

    /// <summary>
    /// 🔒 `05` §4.3's <c>ON_HEAL</c>, fired after the HP is applied — see
    /// <see cref="BattleServices.AfterHeal"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Both readings travel to the op layer.</b> `18` §2.2's <c>HEAL_AMOUNT</c> and
    /// <c>OVERHEAL_AMOUNT</c> throw rather than read zero when the context does not carry them
    /// (<c>OpValue</c>, steering S6), so a heal that fired the trigger without them would make
    /// <c>PK_TRANSFUSION</c> an exception rather than a perk.
    /// </remarks>
    internal void AfterHeal(BattleActor actor, double healed, double overheal)
    {
        ArgumentNullException.ThrowIfNull(actor);

        FireTriggers(
            actor,
            Occurrence(TriggerKind.ON_HEAL, actor),
            readings: new EventReadings(HealAmount: healed, OverhealAmount: overheal));
    }

    /// <summary>
    /// 🔒 `05` §4.1's ward expiry — see <see cref="BattleServices.ExpireWards"/> for why it is a
    /// routing here rather than a call M2-10 makes on the pool directly.
    /// </summary>
    internal int ExpireWards(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var dropped = actor.Wards.ExpireDue(Tick);

        // 🔒 TWO different orders, and they are not the same rule. `05` §4.1 orders ABSORPTION by
        // soonest expiry then grant order, which is what WardPool returns; `05` §3.1 slot 2 orders
        // EXPIRY EMISSION — "statuses whose duration reached 0 expire, in ascending effect-id
        // order" — and `05` §5 lists WARD among the statuses. Two segments from different effects
        // expiring on one tick would otherwise land in the log in an order `05` §3.1 does not
        // authorise, and the log is inside LogHash, which `11` §6 recomputes server-side.
        foreach (var segment in dropped.OrderBy(s => s.SourceEffectId, EffectOrder.IdComparer))
        {
            // 🔒 StatusExpired, and NEVER WardBroken. `05` §4.1: "segment expiry silently removes
            // its remainder (StatusExpired), and does not fire WardBroken" — the distinction
            // `18` §6's `until: WARD_BROKEN` terminator is built on.
            Log.Append(
                Tick, CombatEventType.StatusExpired, CombatActor.None, actor.LogId, segment.Amount);
        }

        return dropped.Count;
    }

    /// <summary>🔒 `05` §4.1's ward grant with an expiry — see <see cref="BattleServices.GrantWard"/>.</summary>
    /// <remarks>
    /// 🔒 <b>The pool is written here rather than through <see cref="IAttackPipeline"/>, and that is
    /// deliberate.</b> An earlier draft delegated to <c>AttackPipeline</c> behind an
    /// <c>is not AttackPipeline ? throw</c>, which made the one route `18` §6's durations must take
    /// unusable from the seam composition <c>BattleSeams.Strict</c>'s own remarks recommend
    /// (<c>Strict with { Statuses = … }</c>) — so every duration-bearing grant in M2-10's suite would
    /// have thrown. <c>DamageResolutionRuleTests.Only_the_attack_pipeline_absorbs_damage_with_a_ward</c>
    /// is stated over <c>Absorb</c> and not over <c>Grant</c> for exactly this reason: absorption is
    /// where a second caller would re-decide `05` §4.1's order and its break, while granting from
    /// two places is already the shape (`05` §4.2's <c>SHIELD</c> and `18` §6's durations).
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

            // 🔒 RE-READ on every grant, never cached. `05` §3.1's SYS_ENRAGE adds a STAT_MULT every
            // second from 70 s, so a boss's post-step-7 Max HP is not a battle constant.
            target.PostMultiplierMaxHp);

        // 🔒 `05` §4.1 — Shield on EVERY grant, clipped ones included. `05` §8 makes the log the
        // replay: a cast the player watched happen must have an event to draw, and a value of 0 is
        // the information rather than the absence of it.
        Log.Append(Tick, CombatEventType.Shield, CombatActor.None, target.LogId, granted);
    }

    /// <summary>
    /// 🔒 `05` §3.1's summon entry rule, implemented here because it is the roster's: <em>"summons
    /// enter at the end of the enemy index list with a full attack cooldown (1.0 / ASPD — they never
    /// attack on their spawn tick) and become targetable at the next targeting evaluation."</em>
    /// </summary>
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

        // 🔒 The one thing that changes slot 4's and slot 5's fixed orders. Cleared here so the summon
        // takes its place at the end of the enemy index list on the next tick.
        _initiative = null;
        _petOrder = null;

        RefreshStats(actor);

        // `18` §2.4's STAT_COPY reads the start-of-tick snapshot, and the per-tick sweep that takes
        // one has already run by the time a summon is admitted. Its opening block IS its correct
        // snapshot for the rest of this tick; without this a STAT_COPY reading it would hit
        // BattleStatReader's refusal with a message blaming the battle-start pre-tick.
        actor.FreezeStartOfTick();

        RegisterHoldings(actor, Tick);

        // 🔒 A FULL cooldown, not zero. Pre-tick 0a's "attackCooldown = 0" is about battle-opening
        // actors; a summon "never attacks on its spawn tick". Read after RefreshStats so it is the
        // summon's own aggregated ASPD.
        var aspd = actor.Stats[StatId.ASPD];
        actor.AttackCooldown = aspd > 0.0 ? StatRounding.Round(1.0 / aspd) : BattleClock.TickSeconds;

        return actor;
    }

    // ══════════════════════════════════════════════════════════════════ outcome

    private bool HeroIsDown() => !_hero.IsAlive;

    /// <summary>
    /// Slot 8's second half. A plain loop rather than LINQ: it runs on every one of 1800 ticks, and a
    /// closure-capturing <c>Any</c> here was measurable against `05`'s &lt; 5 ms budget.
    /// </summary>
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

    /// <summary>
    /// 🔒 `05` §3 — who won. A cleared enemy side is a win, a downed hero is a loss, and on the
    /// timeout <em>"the side with the higher <b>remaining HP fraction</b> wins"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The fraction is the <em>side's</em>, not the hero's.</b> `05` §3 says "the side", and a
    /// pack of five enemies has five HP bars: comparing the hero's fraction against one enemy's would
    /// let a hero at 40% lose a timeout to a pack that is at 41% on its last unit and dead on the
    /// other four. Totals over the side's killable actors — pets are excluded because `05` §3.2 makes
    /// them unkillable, so they have no stake in a war of attrition.
    /// </para>
    /// <para>
    /// ⚠️ <b>An exact tie is a loss for the hero in PvE, and that is errata.</b> `05` §3 authors no
    /// tie rule for PvE. The timeout is a failure to clear, and `05` §9 defines <c>ParPower</c> by
    /// <em>clear rate</em>: a fight that ran the full 90 s without killing anything has not been
    /// cleared, so counting it as a clear would inflate exactly the number the balance harness
    /// calibrates against. Recorded rather than hidden.
    /// </para>
    /// <para>
    /// 🔒 <b>A duel does author one, and it goes the other way.</b> `11` §4.3: <em>"On an exact tie,
    /// the <b>lower-rated</b> player wins (a small underdog bias that prevents stagnation at the
    /// top)."</em> Which side that is arrives on <see cref="CombatRules.ExactTieWinner"/>, because
    /// rating is `11` §5's and the simulator has no business holding an Elo number. Absent — every
    /// PvE fight — the comparison stays strict and the paragraph above holds unchanged.
    /// </para>
    /// <para>
    /// 🔒 <b>"Exact" is exact at `05` §1.1's four decimal places</b>, because that is the precision
    /// <see cref="SideHpFraction"/> produces and the precision every other combat number is compared
    /// at. A tie rule that keyed on raw <see cref="double"/> equality would fire on almost nothing and
    /// would fire differently on two architectures — and `11` §6 re-runs the duel server-side, so a
    /// tie that broke one way on the client and the other on the server is a discarded honest result.
    /// </para>
    /// <para>
    /// ⚠️ <b>A MUTUAL death is an attacker loss, in a duel too, and that is errata.</b> The downed-hero
    /// arm runs first, so two heroes reaching 0 HP on the same tick — reachable through thorns on the
    /// killing blow, a DoT landing on both in slot 1, or an <c>ON_DEATH</c> — ends as a defeat whatever
    /// <see cref="CombatRules.ExactTieWinner"/> says. Nothing authors it: `05` §3.3 says only <em>"the
    /// only death in a duel ends the fight"</em> without saying whose, and `11` §4.3's tie rule is
    /// scoped to the <b>timeout</b>. It is the one path where §3.3's <em>"slight attacker edge"</em>
    /// reverses, so it is recorded rather than left to be rediscovered — and it is <b>not</b> treated
    /// as a 0.0/0.0 tie, because a fight that ended in deaths did not reach the timeout at all.
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

        // 🔒 `==` and deliberately not `double.Equals`, which differs from it at exactly one value:
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
            // `18` §2.4: "despawned ≠ killed", so a CLEAR_SUMMONS'd actor keeps its HP — but it
            // has left the fight, and `05` §3 decides the timeout on the side's REMAINING HP. Counting
            // a killed actor at 0/max is right; counting a despawned one at max/max would hand the
            // timeout to the side whose summons were cleared.
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

    /// <summary>
    /// 🔒 `18` §8 for one actor, through M2-05's condition gate and M2-06's <c>valueScale</c>
    /// evaluator — the wiring <c>ScaledEffectValue</c> was written for and had no production consumer
    /// until now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>One reader per pass, bound to one context, never cached across ticks.</b>
    /// <c>ScaledEffectValue</c>'s own remarks require it: `18` §1.1 re-evaluates a scale <em>"at every
    /// resolution pass"</em>, so a reader built once and reused would freeze <c>PK_BERSERK</c>'s step
    /// count at whatever the hero's HP was when the fight started. <c>StatAggregationSeams.Strict</c>
    /// is a static singleton and structurally cannot hold a per-pass context, which is what made this
    /// M2-08's to wire.
    /// </para>
    /// <para>
    /// 🔒 <b>Why the re-aggregation is conditional, and why the condition is not a cache.</b> `18`
    /// §8 over nine actors × 1800 ticks is the whole of `05`'s &lt; 5 ms budget, so it runs only when
    /// the answer can have changed. It can change two ways, and both are covered: an effect was added
    /// or removed (<see cref="BattleActor.StatsAreStale"/>, raised by every op and every registration),
    /// or the actor holds an effect whose value or activation <em>reads live state</em> — a
    /// <c>valueScale</c> or a `18` §4 condition — in which case it is re-aggregated every tick
    /// unconditionally. An actor with neither has an aggregation that is a pure function of inputs
    /// that did not change, so skipping it is not a cache of a live reading.
    /// </para>
    /// </remarks>
    private void RefreshStats(BattleActor actor)
    {
        if (!actor.StatsAreStale && !actor.StatsDependOnLiveState)
        {
            return;
        }

        var context = ContextFor(actor);

        // 🔒 UNTRIGGERED effects only, and this is the `18` §8 step 1 boundary rather than an
        // omission. §1.1 splits the two: an untriggered effect is a standing modifier re-evaluated
        // "at every resolution pass", while a triggered one applies "at fire time" and lives for its
        // `18` §6 duration afterwards. Aggregating a triggered effect here would apply SYS_ENRAGE's
        // ×1.08 from tick 0 — 70 seconds early, and exactly once instead of once a second.
        //
        // ⚠️ THE OTHER HALF IS M2-02'S AND IS NOT WIRED HERE. `18` §8 step 1 is "collect all ACTIVE
        // effects", which includes a triggered effect that has fired and whose duration has not
        // ended — that set is EffectResolver's (M2-02) over M2-06's EffectStackSet, and neither is
        // on this branch. A fired stat op therefore reaches EffectOpResolver and changes nothing
        // that outlives the call. Left absent and greppable rather than approximated (steering S6):
        // an approximation here would be a second, disagreeing statement of `18` §6's stacking.
        // `18` §2.4's STAT_COPY writes land on the holder as percent-bucket adds that `18` §8 step 5
        // picks up. They are not authored effects, so they are stated as synthetic STAT_ADD_PCTs
        // under an id no authored effect can take (`18` §8 makes an id an identifier).
        //
        // The standing list is handed over as-is when there are none, which is every actor in every
        // fight until a STAT_COPY fires: this method runs for every state-dependent actor on every
        // one of 1800 ticks, and copying a constant list each time was measurable against `05`'s
        // < 5 ms budget.
        //
        // ⚠️ ONE PART OF THAT ABSENT HALF IS NOW WIRED, and only one: `05` §5's stat-modifying
        // statuses, through IStatusTimeline.StatModifiers (M2-10). Six of §5's twelve are stat
        // modifiers — FREEZE, WEAKEN, SUNDER, SPORE, RAGE, HASTE — and a status that never reached
        // this aggregation would be a status that does nothing. They arrive in the same synthetic
        // STAT_ADD_PCT shape as the STAT_COPY buckets and for the same reason. The rest of the
        // absent half is still absent and still M2-02's.
        IReadOnlyList<EffectDefinition> effects;
        var statuses = _seams.Timeline.StatModifiers(actor);

        if (actor.Flow.PercentBuckets.Count == 0 && statuses.Count == 0)
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

            effects = withBuckets;
        }

        var aggregated = StatAggregation.Aggregate(
            actor.Plan.BaseStats,
            effects,
            _plan.Caps,
            new StatAggregationSeams(
                new BattleConditionGate(context),
                new ScaledEffectValue(context),
                StatOpBehaviour.Instance));

        // `05` §3.1's phase check runs after EVERY HP decrease, and a shrinking MAX_HP is one:
        // `18` §9.1's CP_GLASS_HEART re-bases Max HP mid-fight, and a boss clipped below 66% must
        // enter phase 2 there rather than on whatever unrelated swing lands next. ON_LOW_HP is a
        // crossing for the same reason.
        // 🔒 The WHOLE record, not `aggregated.Final` — M2-07's first stated obligation on M2-09.
        // AggregatedStats' remarks: "a consumer that keeps Final and discards the wrapper caps every
        // CP_GLASS_HEART ward at 1 HP with nothing going red". And its second: this runs on every
        // re-aggregation, so `05` §3.1's SYS_ENRAGE moves the ward cap with the boss's Max HP.
        if (actor.SetStats(aggregated))
        {
            AfterHpDecrease(actor);
        }
    }

    /// <summary>
    /// The `18` §4/§5 context for one holder — this fight's roster, this fight's clock, this fight's
    /// draw stream.
    /// </summary>
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
            _runEffects);

    /// <summary>
    /// 🔒 The battle's effect table: every authored effect id in the opening roster, distinct, in
    /// `18` §8's ascending ordinal order. Its positions are `05` §7's <c>RunEffectQueued</c> indices.
    /// </summary>
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

    /// <summary>
    /// 🔒 One effect's position in the battle's effect table — the <c>ushort</c> `05` §7's
    /// <c>RunEffectQueued</c> and <c>Telegraph</c> both carry.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>One table, built once, read by everyone.</b> It is <c>internal</c> rather than private
    /// because M2-12's telegraph pass needs the same positions, and the alternative — a seam
    /// rebuilding <see cref="BuildEffectIndex"/>'s expression for itself — would be a second table
    /// that has to be kept identical to this one by hand, on a number that is inside every committed
    /// <c>LogHash</c>.
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

    // ══════════════════════════════════════════════════════════════════ the seams M2-08 implements

    /// <summary>`18` §8 step 2's gate, over M2-05's evaluator bound to one pass's context.</summary>
    private sealed class BattleConditionGate : IEffectConditionGate
    {
        private readonly EffectEvaluationContext _context;

        internal BattleConditionGate(EffectEvaluationContext context) => _context = context;

        public bool IsActive(EffectDefinition effect) =>
            ConditionEvaluator.IsSatisfied(effect.Condition, _context);
    }

    /// <summary>
    /// `18` §1.1's <c>effectiveValue = value × steps</c> for the op layer, over M2-06's evaluator.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>ScaledEffectValue</c> and not a duplicate of it: that one is
    /// <c>IEffectValueReader</c> for `18` §8 and refuses a non-<c>FLAT</c> <c>valueMode</c> on a stat
    /// op; this is <c>IScaledValueReader</c> for `18` §2.2 and must <b>not</b>, because the value mode
    /// is exactly what M2-03's ops apply on top. <c>EffectOpSeams</c> states that split.
    /// </remarks>
    private sealed class BattleScaledValue : IScaledValueReader
    {
        private readonly EffectEvaluationContext _context;

        internal BattleScaledValue(EffectEvaluationContext context) => _context = context;

        public double ScaledValue(EffectDefinition effect) =>
            ValueScaleEvaluator.EffectiveValue(effect, _context);
    }

    /// <summary>
    /// `18` §2.4's <c>STAT_COPY</c> reading — <em>"the start-of-tick snapshot, so mutual copies cannot
    /// recurse"</em>, which is <see cref="BattleActor.StartOfTickStats"/> and never
    /// <see cref="BattleActor.Stats"/>.
    /// </summary>
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

    /// <summary>`18` §2.4's per-actor flow state, and the roster half of the op that needs one.</summary>
    private sealed class BattleFlowSink : ICombatFlowSink
    {
        private readonly BattleSimulation _battle;

        internal BattleFlowSink(BattleSimulation battle) => _battle = battle;

        public void ExtraAttack(
            IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId)
        {
            var from = Actor(attacker);
            var to = Actor(target);

            // 🔒 "Perform an additional attack IMMEDIATELY" (`18` §2.4) — inside the cascade, not by
            // zeroing the cooldown, which would merely let slot 4 fire it on some later tick and
            // would stack with the real one.
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

        // 🔒 `18` §10.1 E6 — RANDOM_OUTCOME's winner, routed to the seam that knows what an effect
        //    id IS. R17 forbids `Rules/Effects/Ops/` naming a `Rules.Combat.Bosses` type, so the op
        //    validates and draws (exactly one WeightedPick) and this carries the id across. The
        //    strict default is NoBossOutcomes, which throws naming M2-12/M2-13 — the seam is only
        //    reached because authored content rolled.
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

    /// <summary>
    /// `18` §2.5's queue, both ways in: M2-03's op seam and M2-04's trigger seam, translated into the
    /// one <c>RunEffectQueued</c> event `05` §7 declares.
    /// </summary>
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

    /// <summary>
    /// `05` §3.1's <em>"at most their authored <c>once</c> count per battle"</em>, read off the
    /// arming effect's trigger.
    /// </summary>
    /// <remarks>
    /// Read off the ARMING actor's own holdings and not the roster's. Two actors can hold the same
    /// authored effect id with different triggers — a boss and its summon both carrying a phase
    /// block - and a first-match scan of the whole roster would answer for whichever came first in
    /// index order, which is not the one that armed the save.
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
