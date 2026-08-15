using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// The statuses on one actor — one instance per status id per target, keyed exactly that way.
/// </summary>
/// <remarks>
/// <para>
/// A dictionary keyed on the status id makes "one instance per status id" structural: a list would
/// let two <c>BURN</c>s coexist, each with its own cadence anchor.
/// </para>
/// <para>
/// <see cref="Ordered"/> materialises rather than returning a live view, because expiry can mutate
/// the underlying set while a caller is walking it. The order is the ordinal effect-id order,
/// through <c>EffectOrder.IdComparer</c> rather than a bare <c>OrderBy</c>, which would consult the
/// ambient collation.
/// </para>
/// </remarks>
internal sealed class ActorStatuses
{
    private readonly Dictionary<string, StatusInstance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _immuneUntilSeconds = new(StringComparer.Ordinal);
    private readonly List<TimedFraction> _outgoingPower = new();
    private readonly List<TimedFraction> _incomingDuration = new();

    /// <summary>Builds an empty set, with this fight's stun limits.</summary>
    /// <param name="stun">The per-application cap and mandatory immunity window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stun"/> is null.</exception>
    internal ActorStatuses(StunWindow stun)
    {
        ArgumentNullException.ThrowIfNull(stun);

        Stun = stun;
    }

    /// <summary>The <c>STUN</c> state for this actor — the cap and the immunity window.</summary>
    internal StunWindow Stun { get; }

    /// <summary>
    /// Every live instance, in ascending effect-id order, materialised.
    /// </summary>
    internal IReadOnlyList<StatusInstance> Ordered()
    {
        if (_instances.Count == 0)
        {
            return [];
        }

        var ordered = new List<StatusInstance>(_instances.Count);
        foreach (var instance in _instances.Values)
        {
            ordered.Add(instance);
        }

        // One instance is already in order; the sort below is a delegate call per comparison and
        // this is the overwhelmingly common case on a 1800-tick loop.
        if (ordered.Count == 1)
        {
            return ordered;
        }

        ordered.Sort(static (left, right) =>
        {
            var byEffect = EffectOrder.IdComparer.Compare(left.SourceEffectId, right.SourceEffectId);

            // Total order: a tie left to dictionary enumeration order would make expiry order
            // depend on hash layout, a determinism break that reproduces only sometimes.
            return byEffect != 0
                ? byEffect
                : string.CompareOrdinal(left.Definition.Id, right.Definition.Id);
        });

        return ordered;
    }

    /// <summary>The instance of one status, or <c>null</c>.</summary>
    internal StatusInstance? Find(string statusId) =>
        _instances.TryGetValue(statusId, out var found) ? found : null;

    /// <summary>Records a first application.</summary>
    internal void Add(StatusInstance instance)
    {
        // Through Remove rather than by assignment, so StatModifierCount cannot double-count a
        // status re-added over a live one — Apply's reapplication path does not come through here,
        // but ApplyStun's does.
        Remove(instance.Definition.Id);

        _instances[instance.Definition.Id] = instance;

        if (instance.Definition.Basis == StatusPotencyBasis.TargetStatPct)
        {
            StatModifierCount++;
        }

        if (instance.Definition.Ticks)
        {
            TickingCount++;
        }
    }

    /// <summary>Drops an instance.</summary>
    internal void Remove(string statusId)
    {
        if (!_instances.Remove(statusId, out var removed))
        {
            return;
        }

        if (removed.Definition.Basis == StatusPotencyBasis.TargetStatPct)
        {
            StatModifierCount--;
        }

        if (removed.Definition.Ticks)
        {
            TickingCount--;
        }
    }

    /// <summary>
    /// The current stack count for one status — <c>0</c> when it is not carried.
    /// </summary>
    internal int Stacks(string statusId) =>
        _instances.TryGetValue(statusId, out var found) ? found.Stacks.Count : 0;

    /// <summary>
    /// How many live instances feed stat aggregation.
    /// </summary>
    /// <remarks>
    /// A counter rather than a query, for performance: this is asked for every state-dependent actor
    /// on every one of 1800 ticks in a fight budgeted under 5 ms, and the commonest answer is none.
    /// Maintained in <see cref="Add"/> and <see cref="Remove"/>, the only members that change the set.
    /// </remarks>
    internal int StatModifierCount { get; private set; }

    /// <summary>
    /// How many live instances the cadence drives — the DoT and HoT rows.
    /// </summary>
    /// <remarks>
    /// <see cref="StatModifierCount"/>'s twin, for the same reason: an actor carrying any status at
    /// all (e.g. a lone FREEZE, which never ticks) would otherwise pay a list allocation and a sort
    /// per tick to be told nothing was due.
    /// </remarks>
    internal int TickingCount { get; private set; }

    /// <summary>An <c>IMMUNE_STATUS</c> grant, for one status, until a battle time.</summary>
    /// <remarks>
    /// A grant with no <c>duration.seconds</c> lasts the fight: an absent timer is an effect that
    /// ends at its scope's boundary, and the battle is this evaluator's outermost one.
    /// </remarks>
    internal void GrantImmunity(string statusId, int tick, EffectDuration? duration)
    {
        var until = duration?.Seconds is { } seconds
            ? StatRounding.Round(BattleClock.SecondsAt(tick) + seconds)
            : double.PositiveInfinity;

        _immuneUntilSeconds[statusId] =
            _immuneUntilSeconds.TryGetValue(statusId, out var existing) && existing > until
                ? existing
                : until;
    }

    /// <summary>Whether an <c>IMMUNE_STATUS</c> grant is refusing this status right now.</summary>
    internal bool IsImmuneTo(string statusId, int tick) =>
        _immuneUntilSeconds.TryGetValue(statusId, out var until) &&
        BattleClock.SecondsAt(tick) < until;

    /// <summary><c>STATUS_POWER_PCT</c> — outgoing.</summary>
    internal void AddOutgoingPower(double fraction, double? seconds, int tick) =>
        _outgoingPower.Add(TimedFraction.Of(fraction, seconds, tick));

    /// <summary><c>STATUS_DURATION_PCT</c> — incoming.</summary>
    internal void AddIncomingDuration(double fraction, double? seconds, int tick) =>
        _incomingDuration.Add(TimedFraction.Of(fraction, seconds, tick));

    /// <summary>
    /// The multiplier <c>STATUS_POWER_PCT</c> puts on a status this actor applies.
    /// </summary>
    /// <remarks>
    /// An additive percent bucket, not a product: two +20% grants are x1.4, not x1.44.
    /// </remarks>
    internal double OutgoingPowerScale(int tick) => Scale(_outgoingPower, tick);

    /// <summary>The multiplier <c>STATUS_DURATION_PCT</c> puts on an incoming duration.</summary>
    internal double IncomingDurationScale(int tick) => Scale(_incomingDuration, tick);

    private static double Scale(List<TimedFraction> grants, int tick)
    {
        if (grants.Count == 0)
        {
            return 1.0;
        }

        var now = BattleClock.SecondsAt(tick);
        var total = 0.0;

        // By index rather than through LINQ: a fold's order is part of its answer in floating point.
        for (var i = 0; i < grants.Count; i++)
        {
            if (now < grants[i].UntilSeconds)
            {
                total += grants[i].Fraction;
            }
        }

        return StatRounding.Round(1.0 + total);
    }

    private readonly record struct TimedFraction(double Fraction, double UntilSeconds)
    {
        internal static TimedFraction Of(double fraction, double? seconds, int tick) =>
            new(fraction,
                seconds is { } value
                    ? StatRounding.Round(BattleClock.SecondsAt(tick) + value)
                    : double.PositiveInfinity);
    }
}

/// <summary>
/// The <c>dataId</c> for a status event — which of the twelve statuses the event names.
/// </summary>
/// <remarks>
/// <para>
/// A <c>CombatEvent</c> has one <c>ushort</c> to spend and status ids are strings, so the event
/// carries the status's position in this table, one-based (zero means "names no content").
/// </para>
/// <para>
/// A hard-coded table, deliberately: the ordinals are inside <c>LogHash</c>, a wire format that
/// cannot be derived from a file that may legitimately be reordered. A thirteenth status is
/// appended here, never inserted.
/// </para>
/// </remarks>
internal static class StatusLogId
{
    private static readonly IReadOnlyDictionary<string, ushort> Ordinals =
        new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["BURN"] = 1,
            ["POISON"] = 2,
            ["BLEED"] = 3,
            ["FREEZE"] = 4,
            ["STUN"] = 5,
            ["WEAKEN"] = 6,
            ["SUNDER"] = 7,
            ["SPORE"] = 8,
            ["RAGE"] = 9,
            ["WARD"] = 10,
            ["HASTE"] = 11,
            ["REGEN"] = 12,
        };

    /// <summary>Every status id this mapping covers.</summary>
    internal static IReadOnlyCollection<string> All { get; } = Ordinals.Keys.ToList();

    /// <summary>The <c>dataId</c> a status event carries.</summary>
    /// <exception cref="EffectContextException">The id is outside the twelve.</exception>
    internal static ushort Of(string statusId) =>
        Ordinals.TryGetValue(statusId, out var ordinal)
            ? ordinal
            : throw new EffectContextException(
                statusId,
                "it has no 05 §7 dataId because it is not one of 05 §5's twelve statuses",
                "The log is the replay (05 §8), so an event naming a status the replayer cannot " +
                "resolve is an event it must draw as nothing. A thirteenth status needs a position " +
                "here — APPENDED, never inserted, because the ordinal is inside LogHash and every " +
                "committed reference log already carries the twelve.");
}
