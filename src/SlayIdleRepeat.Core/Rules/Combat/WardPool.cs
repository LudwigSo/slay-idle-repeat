using System.Globalization;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>One segment of an actor's ward pool: <c>{amount, expiresAt?, sourceEffectId}</c>.</summary>
/// <param name="Amount">What is left of this segment. Never negative; a spent segment is removed.</param>
/// <param name="ExpiresAtTick">The expiry, as a tick. <c>null</c> for a segment with no timer, which <see cref="WardPool.Absorb"/> puts last.</param>
/// <param name="SourceEffectId">
/// The effect id that granted it. Not a log label: a per-instance ward cap is measured against the
/// total unbroken ward contributed by that effect instance, so the id is what it is measured against.
/// </param>
/// <param name="GrantOrder">
/// The tie-break — ties broken by grant order (oldest first). A monotonic counter rather than the tick
/// the grant landed on: two grants in one tick are ordinary (a battle-start sweep fires many at once)
/// and a tick would tie them again.
/// </param>
internal readonly record struct WardSegment(
    double Amount, int? ExpiresAtTick, string SourceEffectId, long GrantOrder);

/// <summary>One actor's absorb pool: the segments, the cap, the absorption order, and the one distinction the whole section turns on (<c>WardBroken</c> is damage, expiry is not).</summary>
/// <remarks>
/// <para>
/// This is the object the damage pipeline calls <c>Absorb</c> on. It holds segments and nothing else:
/// it does not know what HP is, it never logs, and it never decides whether a hit bypasses it — that is
/// a property of the damage, decided by <see cref="AttackPipeline"/>.
/// </para>
/// <para>The four rules, and why each is here rather than at the call site:</para>
/// <list type="number">
///   <item>
///     <b>Pool cap</b> — a content-authored fraction of the actor's Max HP as it stood after step-7
///     stat aggregation (post-multiplier, pre-<c>STAT_SET</c>). A grant that would exceed the cap is
///     clipped. The basis is handed in per grant rather than held, because a boss's post-step-7 Max HP
///     is not a battle constant — a cached basis would cap against a stale number after an enrage.
///   </item>
///   <item>
///     <b>Absorption order</b> — soonest-expiring segment first; ties broken by grant order (oldest
///     first); non-expiring segments last. Imposed here, on every absorb, so no caller can absorb in
///     insertion order.
///   </item>
///   <item>
///     <b><c>WardBroken</c> is through damage only.</b> <see cref="Absorb"/> reports it;
///     <see cref="ExpireDue"/> never does — segment expiry silently removes its remainder and does not
///     fire <c>WardBroken</c>, since a "until ward broken" duration terminator is built on that
///     distinction. Blurring it would end that buff on a timer nobody watched.
///   </item>
///   <item>
///     <b>Per-source cap</b> — the total unbroken ward contributed by one effect instance is clamped at
///     its own authored fraction of Max HP. A running total over the segments still in the pool, which
///     is why it lives here: a cap applied per grant at the call site would let repeated small grants
///     stack past it.
///   </item>
/// </list>
/// <para>A stateful class, on <c>CombatLog</c>'s and <c>CombatFlowState</c>'s precedent: a ward outlives the hit that failed to break it. One instance per <see cref="BattleActor"/>, never shared, never static.</para>
/// </remarks>
internal sealed class WardPool
{
    private readonly List<WardSegment> _segments = new();

    private long _nextGrantOrder;

    /// <summary>What the pool would absorb right now. <c>0</c> when it is empty.</summary>
    internal double Total
    {
        get
        {
            var total = 0.0;
            for (var i = 0; i < _segments.Count; i++)
            {
                total += _segments[i].Amount;
            }

            return StatRounding.Round(total);
        }
    }

    /// <summary>The live segments, in absorption order.</summary>
    /// <remarks>
    /// Ordered rather than raw: a reader that saw insertion order would draw the wrong one first. Not a
    /// hot path — it copies and sorts on every get; it exists so tests can state the ordering rule over
    /// the segments themselves rather than inferring it from which ones survived a damage number.
    /// </remarks>
    internal IReadOnlyList<WardSegment> Segments
    {
        get
        {
            var ordered = new List<WardSegment>(_segments);
            ordered.Sort(AbsorptionOrder);

            return ordered;
        }
    }

    /// <summary>Adds a segment, clipped by the pool cap and by the granting effect's own per-source cap.</summary>
    /// <param name="amount">The ward the effect authored, before either clip.</param>
    /// <param name="sourceCapPct">
    /// The per-instance ceiling as a fraction of <paramref name="maxHpBasis"/>, or <c>null</c> where
    /// the effect authors none. <c>null</c> is not 0 and is not coerced to one — a 0 would make every
    /// shield without the field grant nothing.
    /// </param>
    /// <param name="sourceEffectId">The effect id the segment carries.</param>
    /// <param name="expiresAtTick">The expiry, or <c>null</c> for no timer.</param>
    /// <param name="wardCapPct">The pool cap fraction, from content.</param>
    /// <param name="maxHpBasis">
    /// Max HP as it stood after step-7 stat aggregation. Not the final aggregated Max HP: an effect
    /// that inflates ATK/DMG while setting Max HP to 1 would otherwise cap every ward on that build at
    /// 1 HP.
    /// </param>
    /// <returns>
    /// What was actually added, after both clips — <c>0</c> when the pool is already at its cap.
    /// <c>Shield</c> fires on every grant regardless, so the caller logs regardless; this is the number
    /// it logs.
    /// </returns>
    internal double Grant(
        double amount,
        double? sourceCapPct,
        string sourceEffectId,
        int? expiresAtTick,
        double wardCapPct,
        double maxHpBasis)
    {
        ArgumentNullException.ThrowIfNull(sourceEffectId);

        if (double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount,
                $"'{sourceEffectId}' granted a ward of " +
                $"{amount.ToString("R", CultureInfo.InvariantCulture)}. `05` §4.1's pool holds real " +
                "quantities; a NaN compares false against every bound below and would be clipped by " +
                "neither cap, then reach CombatLog as a Shield value it refuses.");
        }

        // A negative grant is not a ward and is not a way to remove one — REMOVE_STATUS is. Clamped
        // to zero rather than refused, because a valueScale can legitimately drive an authored
        // magnitude to zero and a fight must not end on it.
        var granted = Math.Max(0.0, amount);

        if (sourceCapPct is { } fraction)
        {
            var ceiling = StatRounding.Round(fraction * maxHpBasis) - LiveFrom(sourceEffectId);
            granted = Math.Min(granted, Math.Max(0.0, ceiling));
        }

        var headroom = StatRounding.Round(wardCapPct * maxHpBasis) - Total;
        granted = StatRounding.Round(Math.Min(granted, Math.Max(0.0, headroom)));

        if (granted <= 0.0)
        {
            return 0.0;
        }

        _segments.Add(new WardSegment(granted, expiresAtTick, sourceEffectId, _nextGrantOrder++));

        return granted;
    }

    /// <summary>Absorbs what it can, in soonest-expiring-first order, and returns the remainder that reaches HP.</summary>
    /// <param name="damage">The post-floor hit. Already rounded.</param>
    /// <param name="brokenByDamage">
    /// <c>true</c> exactly when this call took the pool from a positive total to <c>0</c> — the pool
    /// reaching 0 through damage. <c>false</c> when the pool was already empty, which is not a break
    /// and must not end an "until ward broken" buff a second time.
    /// </param>
    /// <returns>The damage left over. Equal to <paramref name="damage"/> when the pool is empty.</returns>
    internal double Absorb(double damage, out bool brokenByDamage)
    {
        brokenByDamage = false;

        if (damage <= 0.0 || _segments.Count == 0)
        {
            return damage;
        }

        _segments.Sort(AbsorptionOrder);

        var remaining = damage;

        for (var i = 0; i < _segments.Count && remaining > 0.0; i++)
        {
            var segment = _segments[i];
            var taken = Math.Min(segment.Amount, remaining);

            _segments[i] = segment with { Amount = StatRounding.Round(segment.Amount - taken) };
            remaining = StatRounding.Round(remaining - taken);
        }

        _segments.RemoveAll(static s => s.Amount <= 0.0);

        // The pool was non-empty on entry (the early return above), so an empty one here is a break
        // through damage. This is the ONLY thing that fires WardBroken; ExpireDue below reaches the
        // same state and reports nothing.
        brokenByDamage = _segments.Count == 0;

        return remaining;
    }

    /// <summary>Drops every segment whose timer has run out, and returns the remainders that were dropped.</summary>
    /// <param name="tick">The tick being run. A segment expires when <c>expiresAt &lt;= tick</c>.</param>
    /// <returns>The removed segments, in absorption order. The caller emits <c>StatusExpired</c> per entry and never <c>WardBroken</c>, even when the pool is emptied.</returns>
    internal IReadOnlyList<WardSegment> ExpireDue(int tick)
    {
        var dropped = new List<WardSegment>();

        if (_segments.Count == 0)
        {
            return dropped;
        }

        _segments.Sort(AbsorptionOrder);

        // ONE predicate, used for both the report and the removal. Written twice — once to collect
        // the remainders and once inside RemoveAll — they are one edit away from disagreeing, and the
        // symptom would be a StatusExpired carrying a remainder that is still in the pool.
        for (var i = 0; i < _segments.Count; i++)
        {
            if (Due(_segments[i]))
            {
                dropped.Add(_segments[i]);
            }
        }

        if (dropped.Count > 0)
        {
            _segments.RemoveAll(Due);
        }

        return dropped;

        bool Due(WardSegment segment) => segment.ExpiresAtTick is { } expiry && expiry <= tick;
    }

    /// <summary>The total unbroken ward still in the pool from one source.</summary>
    internal double LiveFrom(string sourceEffectId)
    {
        var total = 0.0;
        for (var i = 0; i < _segments.Count; i++)
        {
            if (string.Equals(_segments[i].SourceEffectId, sourceEffectId, StringComparison.Ordinal))
            {
                total += _segments[i].Amount;
            }
        }

        return StatRounding.Round(total);
    }

    /// <summary>The absorption order: soonest-expiring first, ties by grant order (oldest first), non-expiring last.</summary>
    /// <remarks>
    /// A total order over two integers, deliberately — no string comparison, so nothing here can reach
    /// the ambient collation. The <see cref="WardSegment.GrantOrder"/> tie-break is what makes it
    /// total: two segments granted in one pre-tick with the same expiry would otherwise be ordered by
    /// whatever <see cref="List{T}.Sort"/> happened to do.
    /// </remarks>
    private static int AbsorptionOrder(WardSegment left, WardSegment right)
    {
        if (left.ExpiresAtTick != right.ExpiresAtTick)
        {
            return (left.ExpiresAtTick, right.ExpiresAtTick) switch
            {
                (null, _) => 1,
                (_, null) => -1,
                var (l, r) => l.Value.CompareTo(r.Value),
            };
        }

        return left.GrantOrder.CompareTo(right.GrantOrder);
    }
}
