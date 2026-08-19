using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// The statuses and the DoT/HoT cadence, for one fight.
/// </summary>
/// <remarks>
/// <para>
/// Both seams at once — <see cref="IStatusEngine"/> and <see cref="IStatusTimeline"/> — because they
/// are one store read two ways; splitting them would be two views of one fight's statuses.
/// </para>
/// <para>
/// Three obligations, each met by routing through the shared owner rather than duplicating it: a
/// DoT tick goes through <see cref="IAttackPipeline.DealMaxHpPctDamage"/>, which owns ward
/// absorption, the HP write and the low-HP/phase check that follows it (calling that check again
/// here would fire <c>ON_LOW_HP</c> twice); a HoT tick goes through <see cref="IAttackPipeline.Heal"/>
/// instead, so <c>HEAL%</c> applies and <c>ON_HEAL</c> fires.
/// </para>
/// <para>
/// <c>DealMaxHpPctDamage</c> is the right routing because its exemption list (no dodge/crit/block/
/// mitigation/floor; DR% and DAMAGE_TAKEN_MULT apply; wards absorb; no lifesteal/thorns) matches a
/// DoT tick's item for item. What it costs is one log event: this type emits its own
/// <c>StatusTick</c> since the attack pipeline has no way to say "this HP decrease is a status tick".
/// </para>
/// <para>
/// Four stateful types live under <c>Rules/</c> despite the layer's usual "stateless calculator"
/// convention, because a status is written by one tick and read by a later one: this type,
/// <see cref="ActorStatuses"/> (one per actor), <see cref="StunWindow"/> (one per actor) and
/// <see cref="StatusInstance"/> (one per status per actor). This type is the sole owner of the
/// other three.
/// </para>
/// </remarks>
internal sealed class StatusTimeline : IStatusTimeline, IStatusEngine
{
    /// <summary>
    /// The prefix of the synthetic ids this type puts into aggregation and failure messages. Cannot
    /// collide with an authored effect id: no authored identifier starts with <c>(</c>.
    /// </summary>
    private const string SyntheticIdPrefix = "(status:";

    private readonly BattleServices _services;
    private readonly StatusCatalogue _catalogue;

    private readonly Dictionary<string, ActorStatuses> _byActor = new(StringComparer.Ordinal);

    /// <summary>Builds the timeline for one fight.</summary>
    /// <param name="services">The battle — its log, its clock and its roster.</param>
    /// <param name="attack">
    /// The attack pipeline. Every HP change a status causes goes through it (see the type remarks);
    /// nothing here writes HP directly.
    /// </param>
    /// <param name="catalogue"><c>content/statuses.json</c>, read.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal StatusTimeline(BattleServices services, IAttackPipeline attack, StatusCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(catalogue);

        _services = services;
        Attack = attack;
        _catalogue = catalogue;
    }

    /// <summary>The attack pipeline, as this fight wired it.</summary>
    internal IAttackPipeline Attack { get; }

    // ══════════════════════════════════════════════════════════════════ IStatusTimeline

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// There is no timer to advance: battle time is a function of the tick index and never
    /// accumulated, so an instance's remaining duration is <c>now - appliedAt</c> and its cadence is
    /// integer arithmetic on the anchor. This applies every DoT/HoT instance whose cadence boundary
    /// falls on this tick.
    /// </para>
    /// <para>
    /// The walk is over a snapshot in ascending effect-id order: a tick can kill the actor, whose
    /// death can fire effects that apply or remove statuses, and a collection mutated under a
    /// <c>foreach</c> throws.
    /// </para>
    /// </remarks>
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Early-out on a counter: this runs for every actor on every tick, and an actor carrying
        // only a FREEZE has nothing here.
        if (!_byActor.TryGetValue(actor.Id, out var statuses) || statuses.TickingCount == 0)
        {
            return;
        }

        foreach (var instance in statuses.Ordered())
        {
            if (!instance.Definition.Ticks || !StatusCadence.LandsOn(instance.AnchorTick, tick))
            {
                continue;
            }

            // A DoT that killed the actor earlier in this same slot must not keep ticking a corpse.
            if (!actor.IsAlive)
            {
                return;
            }

            // The snapshot can go stale under this walk: the tick can resolve a REMOVE_STATUS (or a
            // remove-and-reapply) on an actor that is still alive, so the instance may no longer be
            // the live one even though it is still in this materialised list.
            if (!ReferenceEquals(statuses.Find(instance.Definition.Id), instance))
            {
                continue;
            }

            Tick(actor, instance, tick);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Statuses whose duration reached 0 expire, in ascending effect-id order. A DoT expiring
    /// exactly on a cadence boundary deals that tick first, then expires — bought entirely by slot
    /// order: <see cref="AdvanceTimers"/> runs for every actor before this runs any expiry, so an
    /// instance whose timer elapses on the same tick as a cadence boundary has already ticked.
    /// </para>
    /// <para>
    /// Ward segments are not this method's, even though WARD is a status: a SHIELD can grant a
    /// segment in a fight that holds no status timeline at all, so the sweep lives in the tick
    /// loop's own slot beside the call to this — see <c>BattleServices.ExpireWards</c>.
    /// </para>
    /// </remarks>
    public void ExpireDue(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!_byActor.TryGetValue(actor.Id, out var statuses))
        {
            return;
        }

        // CurrentBossPhase is what makes PHASE-scope duration mean anything inside a boss fight;
        // without it, DurationEvaluator's outside-a-boss-fight fallback would apply everywhere.
        var probe = new DurationProbe
        {
            BattleTimeSeconds = BattleClock.SecondsAt(tick),
            CurrentPhase = _services.CurrentBossPhase,
        };

        foreach (var instance in statuses.Ordered())
        {
            if (!DurationEvaluator.Evaluate(instance.Application, probe).HasEnded)
            {
                continue;
            }

            statuses.Remove(instance.Definition.Id);
            Expired(actor, instance, tick);
        }
    }

    /// <inheritdoc />
    public bool CanAct(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return !_byActor.TryGetValue(actor.Id, out var statuses) ||
               statuses.Stun.CanAct(_services.Tick);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Computed from <c>EffectStackSet.Count</c> on every call and never cached, since the set is
    /// immutable and a count belongs to a moment.
    /// <para>
    /// WARD answers 0: its full semantics (stacking, bypass, WardBroken) live in the attack
    /// pipeline's one absorb pool per actor, and this timeline keeps no second copy of it. No
    /// authored content reads <c>HAS_STATUS(WARD)</c> today.
    /// </para>
    /// </remarks>
    public int StacksOn(BattleActor actor, string statusId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return _byActor.TryGetValue(actor.Id, out var statuses) ? statuses.Stacks(statusId) : 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each live <see cref="StatusPotencyBasis.TargetStatPct"/> instance becomes one synthetic
    /// <c>STAT_ADD_PCT</c> carrying <c>EffectStackSet.CombinedValue</c>, the additive sum of its
    /// applications — a percent add, not a multiplier, so reading a -0.5 as a x(-0.5) would invert
    /// FREEZE into a negative ASPD. The id is synthetic (see <see cref="SyntheticIdPrefix"/>) since
    /// these are not authored effects.
    /// </remarks>
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Early-out on a counter, not a walk: this runs for every state-dependent actor on every
        // tick, and the commonest answer is "none" — see ActorStatuses.StatModifierCount.
        if (!_byActor.TryGetValue(actor.Id, out var statuses) || statuses.StatModifierCount == 0)
        {
            return [];
        }

        List<EffectDefinition>? modifiers = null;

        foreach (var instance in statuses.Ordered())
        {
            if (instance.Definition.Basis != StatusPotencyBasis.TargetStatPct)
            {
                continue;
            }

            modifiers ??= new List<EffectDefinition>(1);
            modifiers.Add(new EffectDefinition
            {
                Id = SyntheticIdPrefix + instance.Definition.Id + ")",
                Op = EffectOp.STAT_ADD_PCT,
                Stat = StatSelector.Of(instance.Definition.RequireStat()),
                Value = instance.Stacks.CombinedValue,
            });
        }

        return modifiers ?? (IReadOnlyList<EffectDefinition>)[];
    }

    // ══════════════════════════════════════════════════════════════════ IStatusEngine

    /// <inheritdoc />
    /// <remarks>
    /// Lets <see cref="StatusOps"/> tell FREEZE's value-less authored shape apart from a genuine
    /// authoring hole. See <see cref="Apply"/> for why <c>FixedPotency</c> wins over the applier's value.
    /// </remarks>
    public bool HasFixedPotency(string statusId) => _catalogue.Of(statusId).FixedPotency is not null;

    /// <inheritdoc />
    public void Apply(
        IEffectActorView applier, IEffectActorView target, string statusId, double potency,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(target);

        var definition = _catalogue.Of(statusId);
        var receiver = Actor(target, sourceEffectId);
        var statuses = For(receiver);

        if (statuses.IsImmuneTo(statusId, _services.Tick))
        {
            return;
        }

        // STUN first: it is the one row with no magnitude at all, so computing and discarding a
        // potency for it would run the STATUS_POWER_PCT multiply for nothing.
        if (definition.Basis == StatusPotencyBasis.None)
        {
            ApplyStun(receiver, statuses, duration, sourceEffectId);
            return;
        }

        // FREEZE is the one row stated as a literal ("-50% ASPD") rather than as the effect's value,
        // so it is the one status a STATUS_POWER_PCT build cannot amplify — there is no authored
        // value for that scale to apply to.
        var x = definition.FixedPotency ?? StatRounding.Round(
            potency * OutgoingPowerScale(applier, sourceEffectId));

        if (definition.Basis == StatusPotencyBasis.FlatHp)
        {
            // WARD's full semantics defer wholly to the attack pipeline's absorb pool. Granting a
            // segment IS applying the status; a second record here would be a second pool.
            Attack.GrantWard(target, x, sourceCapPct: null, sourceEffectId);

            return;
        }

        var contribution = definition.Basis == StatusPotencyBasis.ApplierAtkPctPerSecond

            // The applier-side half of the basis is folded in here and never again, since the
            // applier may be dead by the time a later tick lands.
            ? StatRounding.Round(x * Actor(applier, sourceEffectId).Stats[StatId.ATK])
            : x;

        var application = new EffectApplication
        {
            EffectId = sourceEffectId,
            Duration = ScaledDuration(receiver, duration),
            AppliedAtSeconds = BattleClock.SecondsAt(_services.Tick),

            // Stamped here, at application, and not re-read at expiry: PHASE scope ends the effect
            // when the boss exits the phase the effect WAS applied in.
            AppliedInPhase = _services.CurrentBossPhase,
        };

        var instance = statuses.Find(statusId);

        if (instance is null)
        {
            statuses.Add(new StatusInstance
            {
                Definition = definition,
                AnchorTick = _services.Tick,
                Stacks = EffectStackSet
                    .Empty(sourceEffectId, _catalogue.StackingFor(statusId, stacking))
                    .Apply(contribution)
                    .Stacks,
                Application = application,
                SourceEffectId = sourceEffectId,
            });
        }
        else
        {
            // Reapplication: stacks and duration. AnchorTick is init-only, so the cadence cannot be
            // moved from here even by mistake.
            instance.Reapply(contribution, application, sourceEffectId);
        }

        Log(receiver, CombatEventType.StatusApplied, definition, x);
        Restat(receiver, definition);
    }

    /// <inheritdoc />
    public void Remove(IEffectActorView target, string statusId, string sourceEffectId)
    {
        var receiver = Actor(target, sourceEffectId);

        // The catalogue call is not decoration: it refuses an id the catalogue does not carry, so a
        // REMOVE_STATUS naming a status that does not exist fails rather than silently removing
        // nothing, which is indistinguishable from succeeding.
        var definition = _catalogue.Of(statusId);

        if (!_byActor.TryGetValue(receiver.Id, out var statuses))
        {
            return;
        }

        if (definition.Basis == StatusPotencyBasis.None)
        {
            // The stun goes; the immunity window stays — a cleanse that also cleared it would make a
            // cleansed hero a better stun-lock target than an uncleansed one.
            statuses.Stun.Clear();
        }

        var instance = statuses.Find(statusId);
        if (instance is null)
        {
            return;
        }

        statuses.Remove(statusId);
        Expired(receiver, instance, _services.Tick);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A refusal, not a no-op: the tag <em>vocabulary</em> is missing (no status is tagged and no
    /// content authors a <c>tags</c> key), not the tag type itself. A silent no-op here would turn a
    /// missing feature into "this perk does nothing", a balance bug indistinguishable from working
    /// as intended. No authored content uses the tag form today.
    /// </remarks>
    /// <exception cref="EffectContextException">Always — no status carries a tag to match.</exception>
    public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(target);

        _ = Actor(target, sourceEffectId);

        throw new EffectContextException(
            sourceEffectId,
            $"it clears the status tag group '{tag.Value}' and no status carries a tag",
            "18 §2.3's REMOVE_STATUS takes a statusId OR a statusTag, and the tag form needs a " +
            "vocabulary. 05 §5 tags none of its statuses, content/statuses.json authors no " +
            "tags key, and StatusTag's own remarks assign that vocabulary to the status catalogue — " +
            "M2-10 declined to invent one (16 R6). Removing nothing and reporting success would be " +
            "indistinguishable from removing the right thing. Author the tags, or clear by statusId.");
    }

    /// <inheritdoc />
    public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId)
    {
        var receiver = Actor(target, sourceEffectId);
        _ = _catalogue.Of(statusId);

        if (!_byActor.TryGetValue(receiver.Id, out var statuses) ||
            statuses.Find(statusId) is not { } instance)
        {
            return;
        }

        var duration = instance.Application.Duration;
        if (duration?.Seconds is not { } current)
        {
            // An instance with no timer has nothing to extend, and inventing one would give a
            // permanent status an end. EXTEND_STATUS adds duration to an existing status; it does
            // not convert an untimed one.
            return;
        }

        // The timer moves and the anchor does not: an EXTEND_STATUS that re-anchored would reset a
        // DoT's cadence.
        instance.Application = instance.Application with
        {
            Duration = duration with { Seconds = StatRounding.Round(current + seconds) },
        };
    }

    /// <inheritdoc />
    public void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId)
    {
        var receiver = Actor(target, sourceEffectId);
        _ = _catalogue.Of(statusId);

        For(receiver).GrantImmunity(statusId, _services.Tick, duration);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Held on the actor and read at the moment that actor applies a status, which is what makes it
    /// outgoing.
    /// </remarks>
    public void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        For(Actor(target, sourceEffectId)).AddOutgoingPower(fraction, Seconds(duration), _services.Tick);

    /// <inheritdoc />
    /// <remarks>
    /// The opposite direction from <see cref="ScaleOutgoingPower"/>, read at the moment a status
    /// lands on this actor.
    /// </remarks>
    public void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        For(Actor(target, sourceEffectId)).AddIncomingDuration(fraction, Seconds(duration), _services.Tick);

    // ══════════════════════════════════════════════════════════════════ the cadence tick

    /// <summary>
    /// One cadence boundary, for one instance: per-tick amount = per-second potency * current stack
    /// count, read at the moment the tick lands.
    /// </summary>
    /// <remarks>
    /// The stack count is read here, inside the tick, and nowhere earlier — <c>EffectStackSet</c> is
    /// immutable so that <c>Count</c> belongs to a moment, and a cached count would be stale in any
    /// fight where the stacks changed between application and boundary. No draw is taken: a DoT tick
    /// is a damage event, not an attack.
    /// </remarks>
    private void Tick(BattleActor actor, StatusInstance instance, int tick)
    {
        var definition = instance.Definition;

        var perSecond = instance.Stacks.CombinedValue;

        var amount = definition.Basis == StatusPotencyBasis.TargetMaxHpPctPerSecond
            ? perSecond * actor.MaxHp
            : perSecond;

        if (definition.ScalesWithTargetMissingHp)
        {
            // BLEED: each tick scales by (1 + target's missing-HP fraction).
            var missing = actor.MaxHp <= 0.0
                ? 0.0
                : StatRounding.Round((actor.MaxHp - actor.CurrentHp) / actor.MaxHp);

            amount *= 1.0 + (_catalogue.BleedMissingHpScaling * missing);
        }

        amount = StatRounding.Round(amount);

        // Appended before the HP change, so a replayer knows which status the decrease that
        // follows belongs to.
        Log(actor, CombatEventType.StatusTick, definition, amount);

        if (definition.Kind == StatusKind.HoT)
        {
            Attack.Heal(actor, amount, instance.SourceEffectId);

            return;
        }

        // Ward absorption, the HP write and the phase check that follows it — see type remarks.
        Attack.DealMaxHpPctDamage(actor, amount, bypassesWards: false, instance.SourceEffectId);
    }

    // ══════════════════════════════════════════════════════════════════ helpers

    /// <summary>The STUN cap, the mandatory immunity window, and the instance.</summary>
    private void ApplyStun(
        BattleActor receiver, ActorStatuses statuses, EffectDuration? duration, string sourceEffectId)
    {
        // A STUN with no duration is refused, not defaulted: substituting the per-application cap
        // would silently grant the longest legal stun to every effect that forgot to author one.
        if (Seconds(duration) is not { } authored)
        {
            throw new EffectContextException(
                sourceEffectId,
                "it applies STUN with no duration",
                "05 §5 states STUN as 'cannot act for D s' and D is the applying effect's own " +
                "18 §6 duration. Substituting the 1.5 s per-application cap would silently grant " +
                "the longest legal stun to every effect that forgot to author one.");
        }

        // Scaled before the cap, so the ceiling binds on the scaled value rather than a number a
        // debuff could otherwise inflate past it.
        var requested = Seconds(ScaledDuration(receiver, duration)) ?? authored;
        var until = statuses.Stun.Apply(_services.Tick, requested);

        if (until is null)
        {
            // Refused by the immunity window. No instance, no event: nothing happened.
            return;
        }

        var granted = StatRounding.Round(
            BattleClock.SecondsAt(until.Value + 1) - BattleClock.SecondsAt(_services.Tick));

        var definition = _catalogue.Of(StunId);
        var application = new EffectApplication
        {
            EffectId = sourceEffectId,
            Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = granted },
            AppliedAtSeconds = BattleClock.SecondsAt(_services.Tick),
        };

        var instance = statuses.Find(StunId);
        if (instance is null)
        {
            statuses.Add(new StatusInstance
            {
                Definition = definition,
                AnchorTick = _services.Tick,
                Stacks = EffectStackSet
                    .Empty(sourceEffectId, _catalogue.StackingFor(StunId, authored: null))
                    .Apply(0.0)
                    .Stacks,
                Application = application,
                SourceEffectId = sourceEffectId,
            });
        }
        else
        {
            instance.Application = application;
            instance.SourceEffectId = sourceEffectId;
        }

        Log(receiver, CombatEventType.StatusApplied, definition, granted);
    }

    /// <summary>The STUN id — an alias of <see cref="StatusIds.Stun"/>, which is where it is spelled.</summary>
    private const string StunId = StatusIds.Stun;

    private ActorStatuses For(BattleActor actor)
    {
        if (!_byActor.TryGetValue(actor.Id, out var statuses))
        {
            statuses = new ActorStatuses(
                new StunWindow(
                    _catalogue.StunMaxSecondsPerApplication, _catalogue.StunImmunityWindowSeconds));

            _byActor[actor.Id] = statuses;
        }

        return statuses;
    }

    /// <summary>
    /// <c>STATUS_DURATION_PCT</c>, applied to the incoming application's duration.
    /// </summary>
    private EffectDuration? ScaledDuration(BattleActor receiver, EffectDuration? duration)
    {
        if (duration?.Seconds is not { } seconds)
        {
            return duration;
        }

        // For() has already run for every caller, so the actor always has a store by now.
        var scale = For(receiver).IncomingDurationScale(_services.Tick);

        return scale == 1.0
            ? duration
            : duration with { Seconds = StatRounding.Round(seconds * scale) };
    }

    private double OutgoingPowerScale(IEffectActorView applier, string sourceEffectId) =>
        _byActor.TryGetValue(Actor(applier, sourceEffectId).Id, out var statuses)
            ? statuses.OutgoingPowerScale(_services.Tick)
            : 1.0;

    private void Log(BattleActor actor, CombatEventType type, StatusDefinition definition, double value) =>
        _services.Log.Append(
            _services.Tick, type, actor.LogId, actor.LogId, value, StatusLogId.Of(definition.Id));

    private void Expired(BattleActor actor, StatusInstance instance, int tick)
    {
        _services.Log.Append(
            tick, CombatEventType.StatusExpired, actor.LogId, actor.LogId,
            0.0, StatusLogId.Of(instance.Definition.Id));

        Restat(actor, instance.Definition);
    }

    /// <summary>
    /// Every change to a status that feeds stat aggregation invalidates the actor's aggregation.
    /// </summary>
    /// <remarks>
    /// Only the <see cref="StatusPotencyBasis.TargetStatPct"/> rows feed it, and marking the rest
    /// stale would re-aggregate every actor on every DoT application for no change. Stated as a test
    /// of the basis rather than the id, so a status added to that basis is covered automatically.
    /// </remarks>
    private static void Restat(BattleActor actor, StatusDefinition definition)
    {
        if (definition.Basis == StatusPotencyBasis.TargetStatPct)
        {
            actor.InvalidateStats();
        }
    }

    private static double? Seconds(EffectDuration? duration) => duration?.Seconds;

    /// <summary>
    /// The <see cref="BattleActor"/> behind an <see cref="IEffectActorView"/>.
    /// </summary>
    /// <remarks>
    /// <c>BattleActor</c> is the only implementation a fight produces, so the cast is a fact rather
    /// than an assumption; a different implementation reaching here is a second roster.
    /// </remarks>
    private static BattleActor Actor(IEffectActorView view, string sourceEffectId)
    {
        // The guard lives here, once, rather than on each of the seven seam members.
        ArgumentNullException.ThrowIfNull(view);

        return view as BattleActor ?? throw new EffectContextException(
            sourceEffectId,
            $"its target is a {view.GetType().Name}, not a BattleActor",
            "05 §5's statuses live on the fight's own actors. BattleActor is the single " +
            "IEffectActorView a battle produces — its own remarks are that two views of one battle " +
            "are two chances to disagree about who is alive — so a second implementation reaching " +
            "the status engine is a second roster, not a substitutable view.");
    }
}
