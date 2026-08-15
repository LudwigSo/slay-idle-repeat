using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The ward pool's rules, stated over <see cref="WardPool"/> itself without a battle.</summary>
public sealed class WardPoolTests
{
    /// <summary>
    /// Not "uncapped": at 1.0 the ceiling and the Max HP basis are numerically the same number, which
    /// is why the grant-clip case below also carries a 0.25 row to discriminate the two.
    /// </summary>
    private const double ShippedCapPct = 1.0;

    private const double Thousand = 1000.0;

    /// <summary>A grant that would exceed the cap is clipped.</summary>
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
    /// Absorption order: soonest-expiring segment first, ties broken by grant order (oldest first),
    /// non-expiring segments last. Each rule has its own case below; this is the composite that
    /// would catch any of them being applied in the wrong sequence.
    /// </summary>
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
    /// The grant-order tie-break alone, with the two segments' insertion order swapped between rows —
    /// the negative control for the composite above. Tie-breaking on effect id instead would give
    /// both rows the same answer and one would be wrong.
    /// </summary>
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

    /// <summary><c>WardBroken</c> fires the moment the pool reaches 0 through damage, and only once.</summary>
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
    /// Segment expiry silently removes its remainder and does not fire <c>WardBroken</c>: the two
    /// halves of this case reach the same empty-pool end state by different routes, showing the
    /// report is about the route and not the state.
    /// </summary>
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
    /// The source cap is a running total over what is still <em>unbroken</em> in the pool, so a
    /// source whose segment has been spent may grant again. A cap applied to the lifetime total
    /// would instead leave a source dead after one big overheal.
    /// </summary>
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
    /// A <c>null</c> source cap is not 0: coercing the absent value to zero would make every
    /// unadorned <c>SHIELD</c> grant nothing, silently.
    /// </summary>
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
