using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>One effect instance's trigger state, and the predicate that decides whether it fires.</summary>
/// <remarks>
/// <para>
/// Everything a trigger kind makes stateful lives here and nowhere else: the <c>everyNth</c>
/// counter, the <c>once</c> latch, the internal cooldown, <c>PERIODIC</c>'s anchor and next firing,
/// and <c>ON_LOW_HP</c>'s armed flag — keyed per <see cref="EffectInstanceId"/>, so counters live on
/// the instance rather than being shared across copies.
/// </para>
/// <para>
/// Never reads a clock, roster or random source it wasn't handed — the tick comes from the
/// <see cref="TriggerOccurrence"/> and the draw stream is a parameter, so a predicate is a pure
/// function of this object plus its two arguments.
/// </para>
/// <para>
/// Gate order: kind → active → duel ban → once latch → filter → cooldown → everyNth → chance. The
/// draw is last because a battle has one combat stream and every draw advances it for everybody — a
/// trigger that consumed a draw and then refused on a cheaper gate would shift every later dodge,
/// crit and <c>RANDOM_ENEMY</c> in the fight.
/// </para>
/// <para>
/// The <c>everyNth</c> counter advances before its own gate and regardless of the draw: it counts
/// occurrences, not "occurrences that also passed a roll", so the counter moves on every occurrence
/// the instance is live for, and only the firing itself is gated.
/// </para>
/// </remarks>
internal sealed class TriggerInstance
{
    private readonly IRunTriggerCounters _runCounters;

    private readonly int _cooldownTicks;

    private int _battleOccasions;
    private int _fireCount;
    private int _cooldownReadyTick;
    private bool _lowHpArmed;
    private int? _anchorTick;
    private int? _nextFiringTick;

    /// <summary>Builds and activates an instance. Only <see cref="TriggerRegistry"/> calls this.</summary>
    /// <param name="id">The instance's stable id.</param>
    /// <param name="effect">The authored effect. Its <c>Trigger</c> must not be null.</param>
    /// <param name="runCounters">The run-scoped counter seam, for <c>ON_KILL</c>.</param>
    /// <param name="activationTick">The tick the effect becomes active on — <c>PERIODIC</c>'s R8 anchor.</param>
    /// <param name="holderHpFraction">
    /// The holder's HP fraction at activation, for <c>ON_LOW_HP</c>'s armed flag. Required for that
    /// kind and ignored by every other.
    /// </param>
    internal TriggerInstance(
        EffectInstanceId id,
        EffectDefinition effect,
        IRunTriggerCounters runCounters,
        int activationTick,
        double? holderHpFraction)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(runCounters);

        Id = id;
        Effect = effect;
        _runCounters = runCounters;

        Trigger = effect.Trigger ?? throw new EffectContextException(
            effect.Id,
            "the effect carries no trigger",
            "`18` §1's eight-part shape names one, and §9.1's CP_GLASS_HEART and §7.7's pet actives " +
            "are the authored exceptions — a pet ability's cadence is the wrapper's cooldown, not a " +
            "trigger. An effect with no trigger has nothing for this layer to fire.");

        TriggerCatalogue.Validate(Trigger);

        // Computed once here, not at every firing — Validate has already refused a cooldown that
        // isn't a whole number of ticks, so this cannot throw.
        _cooldownTicks = TriggerSchedule.CooldownTicks(Trigger);

        Arm(activationTick, holderHpFraction);
        IsActive = true;
    }

    /// <summary>The instance's stable id — the key run-scoped counters live on.</summary>
    internal EffectInstanceId Id { get; }

    /// <summary>The authored effect. Ordered by <c>EffectOrder</c>, never by this type.</summary>
    internal EffectDefinition Effect { get; }

    /// <summary>The effect's trigger, already validated.</summary>
    internal EffectTrigger Trigger { get; }

    /// <summary>Whether the instance is live. A <c>PHASE</c> scope ends at the boss's phase exit; this is the flag that transition sets.</summary>
    internal bool IsActive { get; private set; }

    /// <summary>The tick this instance's <c>PERIODIC</c> clock started on, or <c>null</c> for a trigger that has no clock.</summary>
    internal int? AnchorTick => _anchorTick;

    /// <summary>The next tick a <c>PERIODIC</c> fires on, or <c>null</c> when it is not one.</summary>
    internal int? NextFiringTick => _nextFiringTick;

    /// <summary>How many times this instance has fired this battle.</summary>
    /// <remarks>
    /// Also the hook for the anti-loop bound on <c>SURVIVE_LETHAL</c> and <c>REVIVE</c> — fire at
    /// most their authored <c>once</c> count per battle. That bound is on the op, not the trigger, so
    /// it's applied elsewhere and this is what it reads; an authored effect with no <c>once: true</c>
    /// is unbounded.
    /// </remarks>
    internal int FireCount => _fireCount;

    /// <summary>The instance's <c>everyNth</c> counter — battle-scoped for <c>ON_ATTACK</c>, read through <see cref="IRunTriggerCounters"/> for <c>ON_KILL</c>.</summary>
    internal int OccasionCount =>
        Trigger.Kind == TriggerKind.ON_KILL ? _runCounters.Read(Id) : _battleOccasions;

    /// <summary>The <c>threshold</c> <c>ON_LOW_HP</c> cannot be read without.</summary>
    /// <remarks>
    /// Throws rather than coercing an absent threshold to <c>0.0</c> — that would read as a working
    /// trigger that simply never fires.
    /// </remarks>
    private double Threshold =>
        Trigger.Threshold ?? throw new EffectContextException(
            Trigger.Kind.ToString(),
            $"'{Effect.Id}' carries no threshold",
            "`18` §3's ON_LOW_HP fires when self HP crosses a THRESHOLD downward; with none there is " +
            "no crossing, and 0.0 would be a trigger that looks live and never fires.");

    /// <summary>Starts the instance's clock, once. Re-anchoring a live instance is refused.</summary>
    /// <param name="tick">The tick the effect became active on.</param>
    /// <param name="holderHpFraction">
    /// The holder's HP fraction now, for <c>ON_LOW_HP</c>'s armed flag. Required for that kind and
    /// ignored by every other — a re-grant is a new arming for the same reason it is a new clock.
    /// </param>
    /// <remarks>
    /// <para>
    /// The double-anchor guard matters because a boss can cross two phase boundaries in one HP-loss
    /// event: a phase-2 effect that re-anchored on phase 3's entry would push its first firing a full
    /// interval further out for free, and one that re-anchored on every entry would never fire at all
    /// in a fight that changes phase faster than its interval. A live instance's anchor is therefore
    /// never moved; only a deactivated instance being reactivated re-anchors, since its scope has
    /// genuinely ended.
    /// </para>
    /// <para>
    /// That reactivation is also a new arming, which is why this takes the HP reading: an instance
    /// deactivated while armed and re-granted after the holder had already fallen below its threshold
    /// would otherwise fire on the next scratch — a crossing that never happened.
    /// </para>
    /// </remarks>
    internal void Activate(int tick, double? holderHpFraction = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        if (IsActive)
        {
            return;
        }

        Arm(tick, holderHpFraction);
        IsActive = true;
    }

    /// <summary>
    /// Ends the instance — a <c>PHASE</c> scope at a phase exit, or any other removal.
    /// </summary>
    /// <remarks>
    /// The <c>everyNth</c> counters survive: an <c>ON_ATTACK</c> counter is battle-scoped and the
    /// battle is still running, and an <c>ON_KILL</c> counter is the run's. What is dropped is the
    /// clock, because a later re-grant is a later anchor.
    /// </remarks>
    internal void Deactivate()
    {
        IsActive = false;
        _anchorTick = null;
        _nextFiringTick = null;
    }

    /// <summary>Whether this instance fires on <paramref name="occurrence"/>, and which rule decided.</summary>
    /// <param name="occurrence">The moment.</param>
    /// <param name="rng">The battle's combat draw stream, for <c>chance</c>. May be <c>null</c> only when no live trigger carries a <c>chance</c>.</param>
    /// <exception cref="EffectContextException">A <c>chance</c> had to be drawn and no stream was handed in.</exception>
    internal TriggerOutcome Evaluate(in TriggerOccurrence occurrence, DeterministicRng? rng)
    {
        // Refused here, not only by the caller: this method is reachable through every accessor the
        // registry hands out. A PERIODIC walked through the ordinary path would pass every gate
        // (no filter, no cooldown, no everyNth, no chance) and fire without advancing its schedule,
        // so it would then fire again at its scheduled tick too.
        if (Trigger.Kind == TriggerKind.PERIODIC)
        {
            throw new EffectContextException(
                TriggerKind.PERIODIC.ToString(),
                $"'{Effect.Id}' was evaluated as a moment",
                $"A PERIODIC fires on a schedule anchored when its effect became active (R8), not on a " +
                $"moment. {nameof(TriggerRegistry)}.{nameof(TriggerRegistry.PeriodicDue)} is the one " +
                "path that advances that schedule — `05` §3.1 slot 3.");
        }

        if (occurrence.Kind != Trigger.Kind)
        {
            return TriggerOutcome.WRONG_KIND;
        }

        if (!IsActive)
        {
            return TriggerOutcome.NOT_ACTIVE;
        }

        // Before the counter, deliberately: a duel isn't part of a run, so a duel kill must not
        // advance a run-scoped counter — a player could otherwise farm it in the arena.
        if (occurrence.Kind == TriggerKind.ON_KILL && occurrence.IsPvp)
        {
            return TriggerOutcome.NEVER_FIRES_IN_A_DUEL;
        }

        if (Trigger.Once == true && _fireCount > 0)
        {
            return TriggerOutcome.ONCE_SPENT;
        }

        var filtered = Filter(occurrence);
        if (filtered is { } refusal)
        {
            return refusal;
        }

        if (_cooldownReadyTick > occurrence.Tick)
        {
            return TriggerOutcome.COOLDOWN_ACTIVE;
        }

        if (Trigger.EveryNth is { } everyNth && Advance() % everyNth != 0)
        {
            return TriggerOutcome.EVERY_NTH_PENDING;
        }

        if (Trigger.Chance is { } chance && !Draw(chance, rng))
        {
            return TriggerOutcome.CHANCE_MISSED;
        }

        return Fire(occurrence.Tick);
    }

    /// <summary><c>PERIODIC</c> only — whether a firing is due on <paramref name="tick"/>, advancing the schedule when it is. Reached only through <see cref="TriggerRegistry.PeriodicDue"/>.</summary>
    /// <param name="tick">The tick being evaluated.</param>
    internal TriggerOutcome EvaluatePeriodic(int tick)
    {
        if (Trigger.Kind != TriggerKind.PERIODIC)
        {
            return TriggerOutcome.WRONG_KIND;
        }

        if (!IsActive)
        {
            return TriggerOutcome.NOT_ACTIVE;
        }

        if (Trigger.Once == true && _fireCount > 0)
        {
            return TriggerOutcome.ONCE_SPENT;
        }

        if (_nextFiringTick is not { } due || tick < due)
        {
            return TriggerOutcome.NOT_DUE;
        }

        // Advanced by one interval rather than reset to tick + interval, so a skipped slot catches
        // up deterministically on the anchor's grid instead of drifting by however long the delay was.
        _nextFiringTick = due + TriggerSchedule.IntervalTicks(Trigger);

        return Fire(tick);
    }

    /// <summary>The occasion filters that are pure predicates on the moment.</summary>
    private TriggerOutcome? Filter(in TriggerOccurrence occurrence) => Trigger.Kind switch
    {
        // An absent onlyIfWon is no restriction — a drawback effect that writes none has to be
        // able to land on a loss.
        TriggerKind.ON_BATTLE_END when Trigger.OnlyIfWon == true && !occurrence.HeroWon =>
            TriggerOutcome.ONLY_IF_WON_AND_LOST,

        TriggerKind.ON_PHASE_ENTER when occurrence.Phase != Trigger.Phase =>
            TriggerOutcome.PHASE_MISMATCH,

        TriggerKind.ON_LOW_HP => Crossing(occurrence.HpFraction),

        TriggerKind.ON_TILE_RESOLVED => Matches(Trigger.TileType, occurrence.TileType),
        TriggerKind.ON_PERK_TAKEN => Matches(Trigger.Category, occurrence.Category),

        _ => null,
    };

    /// <summary>Self HP crosses a threshold downward — a transition, not a reading.</summary>
    /// <remarks>
    /// <para>
    /// The flag re-arms when HP goes back above the threshold, entailed rather than invented: the
    /// kind has a <c>once</c> parameter, and if a crossing could only ever happen once, <c>once</c>
    /// would say nothing. So firing again requires crossing downward again, which requires having
    /// gone back up.
    /// </para>
    /// <para>
    /// The reading is rounded before comparison — HP and Max HP are already rounded, but their
    /// quotient isn't, so a boss at exactly a round fraction could cross or not depending on the
    /// last bit of a division.
    /// </para>
    /// </remarks>
    private TriggerOutcome? Crossing(double hpFraction)
    {
        var fraction = Round(hpFraction);
        var above = fraction > Threshold;
        var crossed = _lowHpArmed && !above;

        _lowHpArmed = above;

        return crossed ? null : TriggerOutcome.THRESHOLD_NOT_CROSSED;
    }

    /// <summary>A run-layer filter: an absent authored value matches everything; a written one is compared ordinally.</summary>
    /// <remarks>An occurrence that carries no value where the trigger names one does not match — that's a wiring gap, not a wildcard.</remarks>
    private static TriggerOutcome? Matches(string? authored, string? occurred) =>
        authored is null || string.Equals(authored, occurred, StringComparison.Ordinal)
            ? null
            : TriggerOutcome.FILTER_MISMATCH;

    /// <summary>Advances the instance's <c>everyNth</c> counter and answers its new value.</summary>
    private int Advance()
    {
        if (Trigger.Kind != TriggerKind.ON_KILL)
        {
            return ++_battleOccasions;
        }

        // The ON_KILL counter is the run's, not the battle's, so it's read and written through the
        // seam rather than held here, in one read-modify-write to avoid a stale read.
        var advanced = _runCounters.Read(Id) + 1;
        _runCounters.Write(Id, advanced);

        return advanced;
    }

    /// <summary>The trigger's <c>chance</c>, drawn from the battle's stream.</summary>
    private bool Draw(double chance, DeterministicRng? rng)
    {
        if (rng is null)
        {
            throw new EffectContextException(
                Trigger.Kind.ToString(),
                $"'{Effect.Id}' carries a chance and no draw stream was handed in",
                "`14` §8.1: a combat draw is new DeterministicRng(battleSeed, RngStreams.Combat). " +
                "Deciding a chance without one would be a coin flip that reproduces only for " +
                "whoever wrote it.");
        }

        // Same comparison the dodge roll uses, so a trigger chance and a dodge roll treat their
        // boundaries identically.
        return rng.NextDouble() < chance;
    }

    /// <summary>Records a firing, starting the internal cooldown when the kind has one.</summary>
    private TriggerOutcome Fire(int tick)
    {
        _fireCount++;

        if (_cooldownTicks > 0)
        {
            _cooldownReadyTick = tick + _cooldownTicks;
        }

        return TriggerOutcome.FIRES;
    }

    /// <summary>Everything an activation sets: the clock, and <c>ON_LOW_HP</c>'s armed flag. One method, so the constructor and a re-grant can't set different subsets of it.</summary>
    private void Arm(int tick, double? holderHpFraction)
    {
        if (Trigger.Kind == TriggerKind.ON_LOW_HP)
        {
            // The armed flag can't be guessed: assuming armed would fire on the next scratch for an
            // effect granted while already under its threshold; assuming disarmed would miss a hero
            // who drops from full HP to near-zero in one blow. So it's required, not defaulted.
            if (holderHpFraction is not { } fraction)
            {
                throw new EffectContextException(
                    TriggerKind.ON_LOW_HP.ToString(),
                    $"'{Effect.Id}' was activated without the holder's HP fraction",
                    "`18` §3 fires it when self HP CROSSES a threshold downward, and a crossing needs " +
                    "the reading before the change as well as the one after it. Hand in the holder's " +
                    "HP fraction at activation.");
            }

            if (double.IsNaN(fraction) || fraction is < 0.0 or > 1.0)
            {
                throw new EffectContextException(
                    TriggerKind.ON_LOW_HP.ToString(),
                    $"'{Effect.Id}' was activated at an HP fraction of " +
                    $"{InvariantText.Text(fraction)}",
                    "An HP fraction is 0..1 (`18` §4's SELF_HP_PCT). A NaN would silently start the " +
                    "instance disarmed, because every comparison against it is false.");
            }

            _lowHpArmed = Round(fraction) > Threshold;
        }

        if (Trigger.Kind != TriggerKind.PERIODIC)
        {
            return;
        }

        _anchorTick = tick;
        _nextFiringTick = TriggerSchedule.FirstFiringTick(Trigger, tick);
    }

    /// <summary>4 dp, at the one place this file derives a number.</summary>
    private static double Round(double value) => DeterminismRounding.Round(value);
}
