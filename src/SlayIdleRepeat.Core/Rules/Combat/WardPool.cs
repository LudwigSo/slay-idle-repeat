using System.Globalization;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §4.1 — one segment of an actor's ward pool: <c>{amount, expiresAt?, sourceEffectId}</c>.
/// </summary>
/// <param name="Amount">What is left of this segment. Never negative; a spent segment is removed.</param>
/// <param name="ExpiresAtTick">
/// 🔒 `05` §4.1's <c>expiresAt?</c>, as a tick. <c>null</c> for a segment with no timer, which
/// <see cref="WardPool.Absorb"/> puts <b>last</b>.
/// </param>
/// <param name="SourceEffectId">
/// 🔒 The `18` §8 effect id that granted it. Not a log label: `18` §2.2 caps <em>"the total unbroken
/// ward contributed by that effect <b>instance</b>"</em>, so the id is what a
/// <c>sourceCapPct</c> is measured against.
/// </param>
/// <param name="GrantOrder">
/// 🔒 `05` §4.1's tie-break — <em>"ties broken by grant order (oldest first)"</em>. A monotonic
/// counter rather than the tick the grant landed on: two grants in one tick are ordinary (pre-tick
/// 0b fires every <c>ON_BATTLE_START</c> in one go) and a tick would tie them again.
/// </param>
internal readonly record struct WardSegment(
    double Amount, int? ExpiresAtTick, string SourceEffectId, long GrantOrder);

/// <summary>
/// 🔒 `05` §4.1 — one actor's absorb pool: the segments, the cap, the absorption order, and the one
/// distinction the whole section turns on (<c>WardBroken</c> is damage, expiry is not).
/// </summary>
/// <remarks>
/// <para>
/// `05` §4 step 9 writes <c>dmg = defender.Wards.Absorb(dmg)</c>, and this is that object. It holds
/// segments and nothing else: it does not know what HP is, it never logs, and it never decides
/// whether a hit bypasses it — <see cref="AttackPipeline"/> owns all three, because `05` §4.1's
/// bypass list is a property of the <em>damage</em> and not of the pool.
/// </para>
/// <para>
/// ═══ 🔒 <b>THE FOUR RULES, AND WHY EACH IS HERE RATHER THAN AT THE CALL SITE</b> ═══
/// </para>
/// <list type="number">
///   <item>
///     <b>Pool cap</b> — 📐 <c>wardCapPct</c> × the actor's Max HP <em>"as it stood after `18` §8
///     step 7"</em> (post-multiplier, pre-<c>STAT_SET</c>), which is
///     <c>AggregatedStats.PostMultiplierMaxHp</c>. <em>"A grant that would exceed the cap is
///     clipped."</em> The basis is handed in per grant rather than held, because
///     <c>AggregatedStats</c> is re-read on every re-aggregation — `05` §3.1's <c>SYS_ENRAGE</c>
///     adds a <c>STAT_MULT</c> every second from 70 s, so a boss's post-step-7 Max HP is not a
///     battle constant and a pool that cached it would cap against a stale number.
///   </item>
///   <item>
///     <b>Absorption order</b> — <em>"soonest-expiring segment first; ties broken by grant order
///     (oldest first); non-expiring segments last. Deterministic, and expiring wards are used before
///     they are wasted."</em> Imposed here, on every absorb, so no caller can absorb in insertion
///     order.
///   </item>
///   <item>
///     🔒 <b><c>WardBroken</c> is <em>through damage</em> only.</b> <see cref="Absorb"/> reports it;
///     <see cref="ExpireDue"/> never does. `05` §4.1: <em>"segment expiry silently removes its
///     remainder (<c>StatusExpired</c>), and does <b>not</b> fire <c>WardBroken</c>"</em> — the
///     distinction `18` §6's <c>until: WARD_BROKEN</c> terminator is built on (`17` §4's Ossify).
///     Blurring it would end Ossify's DR buff on a timer nobody watched.
///   </item>
///   <item>
///     <b>Per-source cap</b> — `18` §2.2: <em>"the total <b>unbroken</b> ward contributed by that
///     effect instance is clamped at <c>sourceCapPct × Max HP</c>"</em> (<c>PK_TRANSFUSION</c>, 20%).
///     A running total over the segments still in the pool, which is why it lives here: a cap
///     applied per grant at the call site would let four 20% segments stack.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>A stateful class under <c>Rules/</c></b>, on <c>CombatLog</c>'s and
/// <c>CombatFlowState</c>'s precedent and for their reason — a ward outlives the hit that failed to
/// break it. One instance per <see cref="BattleActor"/>, never shared, never static.
/// </para>
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

    /// <summary>The live segments, in `05` §4.1's absorption order.</summary>
    /// <remarks>
    /// Ordered rather than raw: a reader that saw insertion order would draw the wrong one first.
    /// <para>
    /// ⚠️ <b>An assertion surface, not a hot path.</b> It copies and sorts on every get, and nothing
    /// in production reads it — <see cref="Absorb"/> and <see cref="ExpireDue"/> both walk the list
    /// directly. It exists so that <c>WardPoolTests</c> can state `05` §4.1's ordering rule over the
    /// segments themselves rather than inferring it from which ones survived a damage number.
    /// </para>
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

    /// <summary>
    /// 🔒 `05` §4.1 — adds a segment, clipped by the pool cap and by the granting effect's own
    /// <c>sourceCapPct</c>.
    /// </summary>
    /// <param name="amount">The ward the effect authored, before either clip.</param>
    /// <param name="sourceCapPct">
    /// `18` §2.2's per-instance ceiling as a fraction of <paramref name="maxHpBasis"/>, or
    /// <c>null</c> where the effect authors none. 🔒 <c>null</c> is <b>not</b> 0 and is not coerced
    /// to one — a 0 would make every <c>SHIELD</c> without the field grant nothing.
    /// </param>
    /// <param name="sourceEffectId">The `18` §8 id the segment carries.</param>
    /// <param name="expiresAtTick">`05` §4.1's <c>expiresAt?</c>, or <c>null</c> for no timer.</param>
    /// <param name="wardCapPct">📐 <c>combat_caps.json#/wardCapPct</c>.</param>
    /// <param name="maxHpBasis">
    /// 🔒 <c>AggregatedStats.PostMultiplierMaxHp</c> — Max HP as it stood after `18` §8 step 7.
    /// <b>Not</b> <c>Final[MAX_HP]</c>: `18` §9.1's <c>CP_GLASS_HEART</c> is
    /// <c>STAT_MULT ALL_COMBAT ×2</c> plus <c>STAT_SET MAX_HP 1</c>, so the final block says 1 HP and
    /// every ward on that build would be capped at 1.
    /// </param>
    /// <returns>
    /// What was actually added, after both clips — <c>0</c> when the pool is already at its cap.
    /// `05` §4.1 fires <c>Shield</c> on <b>every</b> grant, so the caller logs regardless; this is
    /// the number it logs.
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

        // A negative grant is not a ward and is not a way to remove one: `18` §2.3's REMOVE_STATUS is.
        // Clamped to zero rather than refused, because a valueScale can legitimately drive an authored
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

    /// <summary>
    /// 🔒 `05` §4 step 9 / §4.1 — absorbs what it can, in <b>soonest-expiring-first</b> order, and
    /// returns the remainder that reaches HP.
    /// </summary>
    /// <param name="damage">The post-floor hit (`05` §4 step 7). Already rounded.</param>
    /// <param name="brokenByDamage">
    /// 🔒 <c>true</c> exactly when this call took the pool from a positive total to <c>0</c> —
    /// `05` §4.1's <em>"the moment the pool reaches 0 <b>through damage</b>"</em>. <c>false</c> when
    /// the pool was already empty, which is not a break and must not end an <c>until:
    /// WARD_BROKEN</c> buff a second time.
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

        // 🔒 The pool was non-empty on entry (the early return above), so an empty one here is a
        // break through damage. `05` §4.1 is explicit that this is the ONLY thing that fires
        // WardBroken; ExpireDue below reaches the same state and reports nothing.
        brokenByDamage = _segments.Count == 0;

        return remaining;
    }

    /// <summary>
    /// 🔒 `05` §4.1 — drops every segment whose timer has run out, and returns the remainders that
    /// were dropped.
    /// </summary>
    /// <param name="tick">The tick being run. A segment expires when <c>expiresAt &lt;= tick</c>.</param>
    /// <returns>
    /// The removed segments, in absorption order. `05` §4.1: <em>"segment expiry silently removes its
    /// remainder (<c>StatusExpired</c>)"</em> — so the caller emits <c>StatusExpired</c> per entry
    /// and 🔒 <b>never <c>WardBroken</c></b>, even when the pool is emptied.
    /// </returns>
    internal IReadOnlyList<WardSegment> ExpireDue(int tick)
    {
        var dropped = new List<WardSegment>();

        if (_segments.Count == 0)
        {
            return dropped;
        }

        _segments.Sort(AbsorptionOrder);

        // 🔒 ONE predicate, used for both the report and the removal. Written twice — once to collect
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

    /// <summary>
    /// `18` §2.2's <em>"total <b>unbroken</b> ward contributed by that effect instance"</em> — what
    /// is still in the pool from one source.
    /// </summary>
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

    /// <summary>
    /// 🔒 `05` §4.1's absorption order: soonest-expiring first, ties by grant order (oldest first),
    /// non-expiring last.
    /// </summary>
    /// <remarks>
    /// A total order over two integers, deliberately — no string comparison, so nothing here can
    /// reach the ambient collation (`18` §8, <c>StringOrderingRuleTests</c>). The
    /// <see cref="WardSegment.GrantOrder"/> tie-break is what makes it total: two segments granted in
    /// one pre-tick with the same expiry would otherwise be ordered by whatever
    /// <see cref="List{T}.Sort"/> happened to do, and `05` §4.1 asks for the older one first.
    /// </remarks>
    private static int AbsorptionOrder(WardSegment left, WardSegment right)
    {
        if (left.ExpiresAtTick != right.ExpiresAtTick)
        {
            return (left.ExpiresAtTick, right.ExpiresAtTick) switch
            {
                (null, _) => 1,
                (_, null) => -1,
                var (l, r) => l!.Value.CompareTo(r!.Value),
            };
        }

        return left.GrantOrder.CompareTo(right.GrantOrder);
    }
}
