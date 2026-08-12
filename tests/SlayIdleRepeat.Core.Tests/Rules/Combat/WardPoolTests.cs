using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4.1 — the ward pool's four rules, stated over <see cref="WardPool"/> itself.
/// </summary>
/// <remarks>
/// The pool is asserted here without a battle, because none of its four rules mentions one: they are
/// about segments, an order, two ceilings and one distinction. What a battle adds — the
/// <c>Shield</c>/<c>WardBroken</c> events, the post-step-7 cap basis and the bypass list — is
/// <c>DamageResolutionTests</c> and <c>WardCapTests</c>.
/// </remarks>
public sealed class WardPoolTests
{
    /// <summary>
    /// 📐 <c>combat_caps.json#/wardCapPct</c> as shipped. ⚠️ <b>Not "uncapped"</b>: against the
    /// 1000-point basis below every case in this file runs under a real 1000-point ceiling, which is
    /// why <see cref="A_grant_is_clipped_at_wardCapPct_times_the_Max_HP_basis"/> carries a 0.25 row —
    /// at 1.0 the ceiling and the basis are numerically the same number.
    /// </summary>
    private const double ShippedCapPct = 1.0;

    /// <summary>The post-`18` §8-step-7 Max HP every case measures its ceilings against.</summary>
    private const double Thousand = 1000.0;

    /// <summary>🔒 `05` §4.1 — <em>"a grant that would exceed the cap is clipped"</em>.</summary>
    /// <remarks>
    /// Two shapes, because one would not discriminate: at <c>wardCapPct = 1.0</c> the cap is
    /// numerically the Max HP basis, so a pool that ignored the percentage entirely would pass. The
    /// 0.25 row is what separates "clips at the cap" from "clips at Max HP".
    /// </remarks>
    [Theory]
    [InlineData(1.0, 1000.0, 400.0)]
    [InlineData(0.25, 250.0, 0.0)]
    [InlineData(2.0, 2000.0, 1400.0)]
    public void A_grant_is_clipped_at_wardCapPct_times_the_Max_HP_basis(
        double wardCapPct, double expectedCap, double expectedSecondGrant)
    {
        var pool = new WardPool();

        var first = pool.Grant(600.0, null, "EFF_A", null, wardCapPct, Thousand);
        var second = pool.Grant(2000.0, null, "EFF_B", null, wardCapPct, Thousand);

        (first + second).ShouldBe(expectedCap);
        second.ShouldBe(expectedSecondGrant);
        pool.Total.ShouldBe(expectedCap);
    }

    /// <summary>
    /// 🔒 `05` §4.1 — <em>"soonest-expiring segment first; ties broken by grant order (oldest
    /// first); non-expiring segments last."</em>
    /// </summary>
    /// <remarks>
    /// One damage number walks the whole order, so the assertion is which segments survive. The
    /// three rules are separable and each has its own case below; this is the composite that would
    /// catch any of them being applied in the wrong sequence.
    /// </remarks>
    [Fact]
    public void Absorption_takes_the_soonest_expiring_first_then_grant_order_then_the_non_expiring()
    {
        var pool = new WardPool();

        // Granted deliberately OUT of absorption order: the non-expiring one first, the latest
        // expiry second, and the two that tie on tick 5 last — so insertion order and absorption
        // order disagree at every position.
        pool.Grant(10.0, null, "EFF_NONE", expiresAtTick: null, ShippedCapPct, Thousand);
        pool.Grant(10.0, null, "EFF_LATE", expiresAtTick: 9, ShippedCapPct, Thousand);
        pool.Grant(10.0, null, "EFF_TIE_OLD", expiresAtTick: 5, ShippedCapPct, Thousand);
        pool.Grant(10.0, null, "EFF_TIE_NEW", expiresAtTick: 5, ShippedCapPct, Thousand);

        var reached = pool.Absorb(25.0, out var broken);

        reached.ShouldBe(0.0);
        broken.ShouldBeFalse("15 of the 40 granted is still in the pool");

        // 25 spent the tick-5 pair (oldest first) and half of the tick-9 segment.
        pool.Segments.Select(s => s.SourceEffectId)
            .ShouldBe(new[] { "EFF_LATE", "EFF_NONE" });
        pool.LiveFrom("EFF_LATE").ShouldBe(5.0);
        pool.LiveFrom("EFF_TIE_OLD").ShouldBe(0.0);
        pool.LiveFrom("EFF_TIE_NEW").ShouldBe(0.0);
    }

    /// <summary>
    /// 🔒 `05` §4.1 — the grant-order tie-break alone, with the two segments' <b>insertion</b> order
    /// swapped between the rows.
    /// </summary>
    /// <remarks>
    /// The negative control for the composite above. If the pool tie-broke on anything other than
    /// grant order — the effect id, say, which is the tie-break `05` §4 uses everywhere else — the
    /// two rows would give the same answer and one of them would be wrong.
    /// </remarks>
    [Theory]
    [InlineData("EFF_Z", "EFF_A", "EFF_A")]
    [InlineData("EFF_A", "EFF_Z", "EFF_Z")]
    public void A_tie_on_expiry_is_broken_by_grant_order_and_not_by_effect_id(
        string grantedFirst, string grantedSecond, string survivor)
    {
        var pool = new WardPool();

        pool.Grant(10.0, null, grantedFirst, expiresAtTick: 5, ShippedCapPct, Thousand);
        pool.Grant(10.0, null, grantedSecond, expiresAtTick: 5, ShippedCapPct, Thousand);

        pool.Absorb(10.0, out _);

        pool.Segments.Single().SourceEffectId.ShouldBe(survivor);
    }

    /// <summary>
    /// 🔒 `05` §4.1 — <em>"<c>WardBroken</c> the moment the pool reaches 0 <b>through
    /// damage</b>"</em>, with both negative controls.
    /// </summary>
    [Fact]
    public void The_pool_reaching_zero_through_damage_is_a_break()
    {
        var pool = new WardPool();
        pool.Grant(10.0, null, "EFF_A", null, ShippedCapPct, Thousand);

        pool.Absorb(4.0, out var partial);
        partial.ShouldBeFalse("6 of the 10 is still in the pool");

        var reached = pool.Absorb(9.0, out var broken);
        broken.ShouldBeTrue();
        reached.ShouldBe(3.0, "the 6 that was left absorbed 6 of the 9");

        pool.Absorb(5.0, out var again);
        again.ShouldBeFalse(
            "the pool was already empty — it did not reach 0 through THIS damage, and `18` §6's " +
            "until: WARD_BROKEN must not terminate a second time");
    }

    /// <summary>
    /// 🔒 `05` §4.1 — <em>"segment expiry silently removes its remainder (<c>StatusExpired</c>), and
    /// does <b>not</b> fire <c>WardBroken</c>."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The distinction `18` §6's <c>until: WARD_BROKEN</c> is built on</b> (`17` §4's Ossify,
    /// which M2-06 shipped against exactly this). The two halves of the case reach the <em>same
    /// end state</em> — an empty pool — by the two different routes, which is the only way to show
    /// that the report is about the route and not about the state.
    /// </remarks>
    [Fact]
    public void Segment_expiry_empties_the_pool_and_is_not_a_break()
    {
        var pool = new WardPool();
        pool.Grant(10.0, null, "EFF_A", expiresAtTick: 5, ShippedCapPct, Thousand);
        pool.Absorb(4.0, out _);

        var dropped = pool.ExpireDue(4);
        dropped.ShouldBeEmpty("the segment expires at tick 5, and it is tick 4");
        pool.Total.ShouldBe(6.0);

        dropped = pool.ExpireDue(5);

        dropped.Count.ShouldBe(1);
        dropped[0].Amount.ShouldBe(6.0, "the REMAINDER is what is dropped, not the granted amount");
        dropped[0].SourceEffectId.ShouldBe("EFF_A");
        pool.Total.ShouldBe(0.0, "the pool is empty by exactly the route damage would have taken it");

        // The proof: the same end state, and nothing reports a break. ExpireDue has no `out bool`
        // to answer with, which is the rule made structural rather than remembered.
        pool.Absorb(1.0, out var broken);
        broken.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 `18` §2.2 — <em>"the total <b>unbroken</b> ward contributed by that effect instance is
    /// clamped at <c>sourceCapPct × Max HP</c>"</em> (<c>PK_TRANSFUSION</c>, 20%).
    /// </summary>
    /// <remarks>
    /// The word doing the work is <em>unbroken</em>: the cap is a running total over what is still
    /// in the pool, so a source whose segment has been spent may grant again. A cap applied per
    /// grant would let four 20% segments stack; a cap applied to the lifetime total would leave
    /// <c>PK_TRANSFUSION</c> dead after one big overheal.
    /// </remarks>
    [Fact]
    public void A_source_cap_clamps_the_live_total_from_one_effect_and_frees_up_as_it_is_spent()
    {
        var pool = new WardPool();

        pool.Grant(150.0, 0.20, "PK_TRANSFUSION", null, ShippedCapPct, Thousand)
            .ShouldBe(150.0);
        pool.Grant(150.0, 0.20, "PK_TRANSFUSION", null, ShippedCapPct, Thousand)
            .ShouldBe(50.0, "20% of 1000 is 200, and 150 of it is already live");
        pool.Grant(150.0, 0.20, "PK_TRANSFUSION", null, ShippedCapPct, Thousand)
            .ShouldBe(0.0);

        // A DIFFERENT source is untouched by it — the cap is per instance, not per pool.
        pool.Grant(150.0, 0.20, "PK_AEGIS", null, ShippedCapPct, Thousand).ShouldBe(150.0);

        pool.Absorb(200.0, out _);
        pool.LiveFrom("PK_TRANSFUSION").ShouldBe(0.0);

        pool.Grant(150.0, 0.20, "PK_TRANSFUSION", null, ShippedCapPct, Thousand)
            .ShouldBe(150.0, "the spent ward is no longer 'unbroken', so the instance may grant again");
    }

    /// <summary>
    /// 🔒 <c>null</c> is not 0 — `18` §2.2 authors <c>sourceCapPct</c> on two effects in the whole
    /// game, and every other <c>SHIELD</c> has none.
    /// </summary>
    /// <remarks>
    /// The negative control for the case above, and steering S6's shape: coercing the absent value
    /// to a zero would make every unadorned <c>SHIELD</c> in the game grant nothing, silently.
    /// </remarks>
    [Fact]
    public void An_absent_source_cap_is_not_a_zero_one()
    {
        var pool = new WardPool();

        pool.Grant(150.0, null, "PK_WARDED", null, ShippedCapPct, Thousand).ShouldBe(150.0);
        pool.Grant(150.0, 0.0, "PK_ZEROED", null, ShippedCapPct, Thousand).ShouldBe(0.0);
    }

    /// <summary>
    /// A grant that is clipped to nothing is not a segment — the pool holds no zero-amount entries
    /// for <see cref="WardPool.Absorb"/> to walk past.
    /// </summary>
    [Fact]
    public void A_grant_clipped_to_nothing_adds_no_segment()
    {
        var pool = new WardPool();

        pool.Grant(1000.0, null, "EFF_A", null, ShippedCapPct, Thousand).ShouldBe(1000.0);
        pool.Grant(50.0, null, "EFF_B", null, ShippedCapPct, Thousand).ShouldBe(0.0);

        pool.Segments.Count.ShouldBe(1);
        pool.Segments.Single().SourceEffectId.ShouldBe("EFF_A");
    }

    /// <summary>A NaN ward is refused rather than clipped — it compares false against every bound.</summary>
    [Fact]
    public void A_non_finite_grant_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new WardPool().Grant(double.NaN, null, "EFF_A", null, ShippedCapPct, Thousand));

    /// <summary>Absorbing against an empty pool changes nothing and is not a break.</summary>
    [Fact]
    public void An_empty_pool_absorbs_nothing()
    {
        var pool = new WardPool();

        pool.Absorb(10.0, out var broken).ShouldBe(10.0);
        broken.ShouldBeFalse();
    }
}
