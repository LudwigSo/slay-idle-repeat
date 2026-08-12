using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 One effect instance's trigger state, and the predicate that decides whether it fires.
/// </summary>
/// <remarks>
/// <para>
/// Everything `18` §3 makes stateful lives here and nowhere else: the <c>everyNth</c> counter, the
/// <c>once</c> latch, the internal <c>cooldown</c>, <c>PERIODIC</c>'s R8 anchor and next firing, and
/// <c>ON_LOW_HP</c>'s armed flag. It is per <see cref="EffectInstanceId"/>, which is what makes
/// `18` §3's <em>"counters live on the effect instance"</em> true rather than asserted.
/// </para>
/// <para>
/// 🔒 <b>It never reads a clock, a roster or a random source it was not handed.</b> The tick comes
/// from the <see cref="TriggerOccurrence"/> and the draw stream is a parameter, so a predicate is a
/// function of this object plus its two arguments. That is what lets M2-08 drive it from a loop this
/// task did not write, and what lets this task's tests drive it from a hand-cranked one.
/// </para>
///
/// <para>
/// ═══ 🔒 <b>THE GATE ORDER, AND WHY THE DRAW IS LAST</b> ═══
/// </para>
/// <para>
/// Kind → active → duel ban → <c>once</c> latch → filter → <c>cooldown</c> → <c>everyNth</c> →
/// <c>chance</c>. The draw is last because a battle has <b>one</b> combat stream (`14` §8.1) and
/// every draw advances it for everybody: a trigger that consumed a draw and then refused on a
/// cheaper gate would shift every later dodge, crit and <c>RANDOM_ENEMY</c> in the fight. Putting
/// the draw behind the deterministic gates means the stream's position is a function of what
/// actually needed deciding.
/// </para>
/// <para>
/// ⚠️ <b>The <c>everyNth</c> counter advances before its own gate, and regardless of the draw.</b>
/// `18` §3 counts <em>occurrences</em> — <c>PK_FLURRY</c> is "every 5th attack", not "every 5th
/// attack that also passed a roll". So the counter moves on every occurrence the instance is live
/// for, and only the firing is gated.
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

        // 🔒 Computed once, here, rather than at every firing. `Validate` has already refused a
        // cooldown that is not a whole number of ticks, so this cannot throw — and that is the
        // point: an earlier draft converted it inside Fire(), so `{"kind":"ON_DODGE","cooldown":0.03}`
        // passed the schema, passed validation, and threw out of `05` §3.1 slot 4 on the first
        // successful dodge of a live fight.
        _cooldownTicks = TriggerSchedule.CooldownTicks(Trigger);

        Arm(activationTick, holderHpFraction);
        IsActive = true;
    }

    /// <summary>The instance's stable id — the key `18` §3's counters live on.</summary>
    internal EffectInstanceId Id { get; }

    /// <summary>The authored effect. Ordered by <c>EffectOrder</c>, never by this type.</summary>
    internal EffectDefinition Effect { get; }

    /// <summary>The effect's trigger, already validated against `18` §3's partition.</summary>
    internal EffectTrigger Trigger { get; }

    /// <summary>
    /// Whether the instance is live. `18` §6's <c>PHASE</c> scope ends at the boss's phase exit
    /// (`05` §3.1), and M2-06 and M2-08 own when that happens; this is the flag they set.
    /// </summary>
    internal bool IsActive { get; private set; }

    /// <summary>
    /// 🔒 R8 — the tick this instance's <c>PERIODIC</c> clock started on, or <c>null</c> for a
    /// trigger that has no clock.
    /// </summary>
    internal int? AnchorTick => _anchorTick;

    /// <summary>The next tick a <c>PERIODIC</c> fires on, or <c>null</c> when it is not one.</summary>
    internal int? NextFiringTick => _nextFiringTick;

    /// <summary>
    /// How many times this instance has fired this battle.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Also the hook for `05` §3.1's anti-loop bound on <c>SURVIVE_LETHAL</c> and
    /// <c>REVIVE</c></b> — <em>"fire at most their authored <c>once</c> count per battle"</em>. That
    /// bound is on the <b>op</b>, not on the trigger, so it is M2-08's to apply and this is what it
    /// reads. An authored <c>SURVIVE_LETHAL</c> that carries no <c>once: true</c> is unbounded by
    /// `18` §3 alone; §7.4's <c>PK_UNBREAKABLE</c> writes the flag and every authored one should.
    /// Recorded rather than defaulted (steering S6).
    /// </remarks>
    internal int FireCount => _fireCount;

    /// <summary>
    /// The instance's <c>everyNth</c> counter — battle-scoped for <c>ON_ATTACK</c>, read through
    /// <see cref="IRunTriggerCounters"/> for <c>ON_KILL</c> (`18` §3).
    /// </summary>
    internal int OccasionCount =>
        Trigger.Kind == TriggerKind.ON_KILL ? _runCounters.Read(Id) : _battleOccasions;

    /// <summary>
    /// `18` §3's <c>threshold</c>, which <c>ON_LOW_HP</c> cannot be read without.
    /// </summary>
    /// <remarks>
    /// 🔒 Throws rather than coercing an absent threshold to <c>0.0</c> (steering S6). It is
    /// unreachable today because <see cref="TriggerCatalogue.Validate"/> requires <c>threshold</c> on
    /// the one kind that reads it — but a coerced <c>0.0</c> is a threshold of "at zero HP", which
    /// reads as a working trigger that simply never fires, and the day the partition changes is not
    /// the day to find that out.
    /// </remarks>
    private double Threshold =>
        Trigger.Threshold ?? throw new EffectContextException(
            Trigger.Kind.ToString(),
            $"'{Effect.Id}' carries no threshold",
            "`18` §3's ON_LOW_HP fires when self HP crosses a THRESHOLD downward; with none there is " +
            "no crossing, and 0.0 would be a trigger that looks live and never fires.");

    /// <summary>
    /// 🔒 R8 — starts the instance's clock, <b>once</b>. Re-anchoring a live instance is refused.
    /// </summary>
    /// <param name="tick">The tick the effect became active on.</param>
    /// <param name="holderHpFraction">
    /// The holder's HP fraction now, for <c>ON_LOW_HP</c>'s armed flag. Required for that kind and
    /// ignored by every other — a re-grant is a new arming for the same reason it is a new clock.
    /// </param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The double-anchor guard is the point.</b> `05` §3.1's phase check runs after every boss
    /// HP decrease and <em>"a burst from 70% to 20% therefore fires phase 2's entry, then phase 3's"</em>
    /// — two entries inside one tick. A phase-2 effect that re-anchored on phase 3's entry would push
    /// its first firing a full interval further out for free, and one that re-anchored on <em>every</em>
    /// entry would never fire at all in a fight that changed phase faster than its interval. Phases
    /// never revert (`05` §3.1), so a live instance's anchor is never legitimately moved.
    /// </para>
    /// <para>
    /// A <b>deactivated</b> instance that is activated again does re-anchor, and that is a different
    /// case: its `18` §6 <c>PHASE</c>-scoped effect ended and a new grant is a new clock.
    /// </para>
    /// <para>
    /// ⚠️ <b>And a new <em>arming</em>, which is why this takes the HP reading.</b> The armed flag is
    /// stateful and <see cref="Crossing"/> is unreachable while the instance is inactive, so an
    /// instance deactivated while armed and re-granted after the holder had already fallen below its
    /// threshold would fire on the next scratch — a crossing that never happened, and precisely the
    /// case the constructor refuses to guess.
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
    /// Ends the instance — `18` §6's <c>PHASE</c> scope at a phase exit, or any other removal.
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

    /// <summary>
    /// 🔒 Whether this instance fires on <paramref name="occurrence"/>, and which rule decided.
    /// </summary>
    /// <param name="occurrence">The moment.</param>
    /// <param name="rng">
    /// The battle's combat draw stream (`14` §8.1), for `18` §3's <c>chance</c>. May be
    /// <c>null</c> only when no live trigger carries a <c>chance</c>.
    /// </param>
    /// <exception cref="EffectContextException">
    /// A <c>chance</c> had to be drawn and no stream was handed in.
    /// </exception>
    internal TriggerOutcome Evaluate(in TriggerOccurrence occurrence, DeterministicRng? rng)
    {
        // 🔒 Refused HERE and not only in TriggerRegistry.Evaluate, because this method is reachable
        // on every instance the registry hands out — Register's return value, the indexer, PeriodicDue's
        // result, Instances. Without this a PERIODIC walked through the ordinary path passes every
        // gate (no filter arm, no cooldown, no everyNth, no chance) and FIRES, leaving _nextFiringTick
        // untouched — so it fires again at its scheduled tick. The registry's guard carries the
        // message; this one carries the invariant.
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

        // 🔒 `05` §3.3 — before the counter, deliberately. A duel is not part of a run, so a duel
        // kill must not advance PK_MIDAS's run-scoped count: a player could otherwise farm the
        // counter in the arena and walk into the next run with the perk half-charged.
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

    /// <summary>
    /// 🔒 <c>PERIODIC</c> only — whether a firing is due on <paramref name="tick"/>, advancing the
    /// schedule when it is. Reached only through <see cref="TriggerRegistry.PeriodicDue"/>.
    /// </summary>
    /// <param name="tick">The tick `05` §3.1 slot 3 is running.</param>
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

        // 🔒 Advanced by one interval rather than reset to `tick + interval`. `05` §3's tick is
        // 0.05 s and every authored interval is whole seconds, so this is the same number today —
        // but if a slot were ever skipped, adding the interval keeps the schedule on the anchor's
        // grid and catches up deterministically, where resetting from `tick` would let the cadence
        // drift by whatever the delay happened to be. `18` §3's period is of the clock, not of the
        // last firing.
        _nextFiringTick = due + TriggerSchedule.IntervalTicks(Trigger);

        return Fire(tick);
    }

    /// <summary>The occasion filters that are pure predicates on the moment.</summary>
    private TriggerOutcome? Filter(in TriggerOccurrence occurrence) => Trigger.Kind switch
    {
        // 🔒 An absent onlyIfWon is no restriction, not `false`-meaning-something-else: `18` §7.5's
        // CP_BLOOD_PRICE drawback writes none and has to land on a loss.
        TriggerKind.ON_BATTLE_END when Trigger.OnlyIfWon == true && !occurrence.HeroWon =>
            TriggerOutcome.ONLY_IF_WON_AND_LOST,

        TriggerKind.ON_PHASE_ENTER when occurrence.Phase != Trigger.Phase =>
            TriggerOutcome.PHASE_MISMATCH,

        TriggerKind.ON_LOW_HP => Crossing(occurrence.HpFraction),

        TriggerKind.ON_TILE_RESOLVED => Matches(Trigger.TileType, occurrence.TileType),
        TriggerKind.ON_ROLL => Matches(Trigger.FaceKind, occurrence.FaceKind),
        TriggerKind.ON_PERK_TAKEN => Matches(Trigger.Category, occurrence.Category),

        _ => null,
    };

    /// <summary>
    /// 🔒 `18` §3 — <em>"self HP crosses a threshold <b>downward</b>"</em>, which is a transition and
    /// not a reading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The flag re-arms when HP goes back above the threshold, and that is entailed rather than
    /// invented.</b> `18` §3 gives the kind a <c>once</c> parameter. If a crossing could only ever
    /// happen once, <c>once</c> would say nothing — so the kind must be able to fire again, and
    /// firing again requires crossing downward again, which requires having gone back up. The
    /// Ossuary King's Rise Again writes <c>once: true</c> precisely because it must not (`17` §4).
    /// </para>
    /// <para>
    /// The reading is rounded to 4 dp before comparison (`05` §1.1). HP and Max HP are already
    /// rounded, but their quotient is not, and a boss at exactly 33% of an odd Max HP would
    /// otherwise cross or not depending on the last bit of a division.
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

    /// <summary>
    /// A run-layer filter: an absent one matches everything, and a written one is compared ordinally.
    /// </summary>
    /// <remarks>
    /// 🔒 An absent filter matches rather than throws, for <see cref="TriggerCatalogue"/>'s stated
    /// reason: `18` §3's row for <c>ON_ROLL</c> is <em>"a die roll completes"</em> and
    /// <c>faceKind</c> narrows it. An occurrence that carries no value where the trigger names one
    /// does <b>not</b> match — the run controller knows which tile resolved, and a moment that does
    /// not is a wiring gap rather than a wildcard.
    /// </remarks>
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

        // 🔒 The ON_KILL counter is the RUN's, not the battle's (`18` §3), so it is read and written
        // through the seam rather than held here. Read-modify-write in one place: two call sites
        // would be two chances to write a count that was read before the last kill.
        var advanced = _runCounters.Read(Id) + 1;
        _runCounters.Write(Id, advanced);

        return advanced;
    }

    /// <summary>`18` §3's <c>chance</c>, drawn from the battle's stream.</summary>
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

        // `05` §4's own spelling — `Rng.NextDouble() < p` — so a trigger chance and a dodge roll
        // treat their boundaries identically.
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

    /// <summary>
    /// 🔒 Everything an activation sets: R8's clock, and <c>ON_LOW_HP</c>'s armed flag. One method,
    /// so the constructor and a re-grant cannot set different subsets of it.
    /// </summary>
    private void Arm(int tick, double? holderHpFraction)
    {
        if (Trigger.Kind == TriggerKind.ON_LOW_HP)
        {
            // 🔒 The armed flag cannot be guessed, and there is no safe default: assume armed and an
            // effect granted while the holder is already under its threshold fires on the next
            // scratch that was never a crossing; assume disarmed and PK_UNBREAKABLE never fires
            // against a hero who goes from full HP to 20% in one blow. Steering S6 — the hole stays
            // a hole and says so.
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
                    $"{fraction.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
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

    /// <summary>🔒 `05` §1.1 — 4 dp, at the one place this file derives a number.</summary>
    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.ToEven);
}
