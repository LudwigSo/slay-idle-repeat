using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §5's statuses on <b>one</b> actor — <em>"one instance per <c>statusId</c> per
/// target"</em>, keyed exactly that way.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A dictionary keyed on the status id is the "one instance per <c>statusId</c>" rule made
/// structural.</b> A list would let two <c>BURN</c>s coexist, at which point they would have two
/// cadence anchors and the section's first sentence would be false with nothing to notice it.
/// </para>
/// <para>
/// 🔒 <b><see cref="Ordered"/> materialises.</b> `05` §3.1 slot 2 walks the statuses expiring them,
/// and an expiry removes from this collection; slot 1's cadence can kill an actor, whose
/// <c>ON_DEATH</c> effects can apply or remove one. Both mutate under the walk. The order itself is
/// `18` §8's ordinal effect-id order, through <c>EffectOrder.IdComparer</c> — a bare
/// <c>OrderBy(x =&gt; x.Id)</c> would consult the ambient collation, which
/// <c>StringOrderingRuleTests</c> fails the build on.
/// </para>
/// </remarks>
internal sealed class ActorStatuses
{
    private readonly Dictionary<string, StatusInstance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _immuneUntilSeconds = new(StringComparer.Ordinal);
    private readonly List<TimedFraction> _outgoingPower = new();
    private readonly List<TimedFraction> _incomingDuration = new();

    /// <summary>Builds an empty set, with this fight's `05` §5 stun limits.</summary>
    /// <param name="stun">`05` §5's per-application cap and mandatory immunity window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stun"/> is null.</exception>
    internal ActorStatuses(StunWindow stun)
    {
        ArgumentNullException.ThrowIfNull(stun);

        Stun = stun;
    }

    /// <summary>`05` §5's <c>STUN</c> state for this actor — the cap and the immunity window.</summary>
    internal StunWindow Stun { get; }

    /// <summary>
    /// Every live instance, in `05` §3.1's ascending effect-id order, materialised.
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

        ordered.Sort(static (left, right) =>
        {
            var byEffect = EffectOrder.IdComparer.Compare(left.SourceEffectId, right.SourceEffectId);

            // 🔒 Total, and the tie-break is the status id. Two statuses applied by ONE effect share
            // its id — `18` §7.10's blocks do exactly that — and a tie left to the dictionary's
            // enumeration order would make slot 2's expiry order depend on hash layout, which is a
            // `14` §8.2 determinism break that reproduces only sometimes.
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
    internal void Add(StatusInstance instance) => _instances[instance.Definition.Id] = instance;

    /// <summary>Drops an instance.</summary>
    internal void Remove(string statusId) => _instances.Remove(statusId);

    /// <summary>
    /// `05` §3.1's <em>"current stack count"</em> for one status — <c>0</c> when it is not carried.
    /// </summary>
    internal int Stacks(string statusId) =>
        _instances.TryGetValue(statusId, out var found) ? found.Stacks.Count : 0;

    /// <summary>`18` §2.3's <c>IMMUNE_STATUS</c>, for one status, until a battle time.</summary>
    /// <remarks>
    /// A grant with no <c>duration.seconds</c> lasts the fight: `18` §6 makes an absent timer an
    /// effect that ends at its scope's boundary, and the battle is this evaluator's outermost one.
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

    /// <summary>Whether `18` §2.3's <c>IMMUNE_STATUS</c> is refusing this status right now.</summary>
    internal bool IsImmuneTo(string statusId, int tick) =>
        _immuneUntilSeconds.TryGetValue(statusId, out var until) &&
        BattleClock.SecondsAt(tick) < until;

    /// <summary>`18` §2.3's <c>STATUS_POWER_PCT</c> — outgoing.</summary>
    internal void AddOutgoingPower(double fraction, double? seconds, int tick) =>
        _outgoingPower.Add(TimedFraction.Of(fraction, seconds, tick));

    /// <summary>`18` §2.3's <c>STATUS_DURATION_PCT</c> — incoming.</summary>
    internal void AddIncomingDuration(double fraction, double? seconds, int tick) =>
        _incomingDuration.Add(TimedFraction.Of(fraction, seconds, tick));

    /// <summary>
    /// The multiplier `18` §2.3's <c>STATUS_POWER_PCT</c> puts on a status this actor applies.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>An additive percent bucket, not a product.</b> The op's name ends <c>_PCT</c>, which is
    /// `18` §2.1's <c>STAT_ADD_PCT</c> family rather than its <c>STAT_MULT</c> one, and R1 scopes
    /// <em>the value IS the multiplier</em> to <c>STAT_MULT</c>. Two +20% grants are therefore ×1.4,
    /// not ×1.44.
    /// </remarks>
    internal double OutgoingPowerScale(int tick) => Scale(_outgoingPower, tick);

    /// <summary>The multiplier `18` §2.3's <c>STATUS_DURATION_PCT</c> puts on an incoming duration.</summary>
    internal double IncomingDurationScale(int tick) => Scale(_incomingDuration, tick);

    private static double Scale(List<TimedFraction> grants, int tick)
    {
        if (grants.Count == 0)
        {
            return 1.0;
        }

        var now = BattleClock.SecondsAt(tick);
        var total = 0.0;

        // By index rather than through LINQ: `18` §8's whole point is that two implementations of one
        // rule produce the same double, and a fold's order is part of its answer in floating point.
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
/// 🔒 `05` §7's <c>dataId</c> for a status event — which of `05` §5's twelve the event names.
/// </summary>
/// <remarks>
/// <para>
/// A <c>CombatEvent</c> has one <c>ushort</c> to spend and `05` §5's ids are strings, so the event
/// carries the status's <b>position in `05` §5's table</b>, one-based. One-based because
/// <c>CombatLog.NoDataId</c> is <c>0</c> and means <em>"names no content"</em>: a zero-based
/// <c>BURN</c> would be indistinguishable from an event that names nothing.
/// </para>
/// <para>
/// 🔒 <b>The order is `05` §5's table order and is therefore inside <c>LogHash</c>.</b> It is read
/// from the ids the catalogue loaded rather than from a second list in code, so the data file and the
/// log agree by construction; <c>StatusLogIdTests</c> pins the twelve positions, because reordering
/// the rows of <c>content/statuses.json</c> would silently renumber every status event in every
/// committed reference log.
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

    /// <summary>Every status id this mapping covers — the S3 floor's subject.</summary>
    internal static IReadOnlyCollection<string> All { get; } = Ordinals.Keys.ToList();

    /// <summary>The <c>dataId</c> a status event carries.</summary>
    /// <exception cref="EffectContextException">The id is outside `05` §5's twelve.</exception>
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
