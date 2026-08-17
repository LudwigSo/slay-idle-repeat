using Shouldly;
using SlayIdleRepeat.Core.Rules.Luck;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// M3, the bad-luck bank: tokens accrue on the outcome the player did not want and are spent on the
/// one they did — accrue per miss, redeem at the authored cost, refuse below it.
/// </summary>
/// <remarks>
/// The costs are <c>24</c>'s own: 60 / 200 / 600 Beast Marks for a chosen pet (§4.4 P2) and 12 Set
/// Tokens for a chosen SS item (§5.1). They are the sinks this primitive exists to reach, and a bank
/// that could not reach one exactly would leave the player a token short forever.
/// </remarks>
public sealed class MercyAccrualTests
{
    // ---------------------------------------------------------------- accrual

    /// <summary>A miss adds what it grants, and the bank is the running total.</summary>
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(59, 1, 60)]
    [InlineData(199, 1, 200)]
    [InlineData(11, 1, 12)]
    public void Accruing_adds_the_grant_to_the_bank(int held, int grant, int expected)
    {
        MercyAccrual.Accrue(held, grant).ShouldBe(expected);
    }

    /// <summary>A zero grant is legal and leaves the bank where it was.</summary>
    /// <remarks>
    /// Reachable rather than theoretical: a duplicate pet below ★5 grants no Beast Mark (<c>24</c>
    /// §4.4 P2 grants one only at ★5), and refusing that call would make every caller branch first.
    /// </remarks>
    [Fact]
    public void A_zero_grant_is_legal_and_leaves_the_bank_alone()
    {
        MercyAccrual.Accrue(7, 0).ShouldBe(7);
    }

    /// <summary>The bank never shrinks on an accrual.</summary>
    /// <remarks>
    /// Stated against the input rather than a literal, so an implementation answering a constant
    /// cannot satisfy it. <c>24</c> §1.1: bad luck is not a debt that expires.
    /// </remarks>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(600, 1)]
    public void Accruing_never_lowers_the_bank(int held, int grant)
    {
        MercyAccrual.Accrue(held, grant).ShouldBeGreaterThanOrEqualTo(held);
    }

    /// <summary>A bank that would exceed <see cref="int.MaxValue"/> throws rather than wrapping.</summary>
    /// <remarks>
    /// A wrapped bank reads as a negative balance, which <see cref="MercyAccrual.CanRedeem"/> would
    /// then refuse forever — a silent, permanent loss of every token the player earned.
    /// </remarks>
    [Fact]
    public void An_accrual_that_would_overflow_the_bank_is_refused()
    {
        Should.Throw<OverflowException>(() => MercyAccrual.Accrue(int.MaxValue, 1));
    }

    /// <summary>Neither argument may be negative.</summary>
    /// <remarks>
    /// The refused argument is named in each case. Both carry the same range, so a guard that only
    /// checked one of them would satisfy a bare type assertion on every row.
    /// </remarks>
    [Theory]
    [InlineData(-1, 1, "held")]
    [InlineData(1, -1, "grant")]
    [InlineData(int.MinValue, 0, "held")]
    public void A_negative_bank_or_grant_is_refused(int held, int grant, string parameter)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MercyAccrual.Accrue(held, grant))
            .ParamName.ShouldBe(parameter);
    }

    // ---------------------------------------------------------------- redemption

    /// <summary>The bank covers the cost at exactly the cost, and not one token below it.</summary>
    [Theory]
    [InlineData(60, 60, true)]
    [InlineData(59, 60, false)]
    [InlineData(200, 200, true)]
    [InlineData(199, 200, false)]
    [InlineData(600, 600, true)]
    [InlineData(599, 600, false)]
    [InlineData(12, 12, true)]
    [InlineData(11, 12, false)]
    [InlineData(0, 60, false)]
    public void CanRedeem_answers_at_the_authored_cost_and_not_below_it(int held, int cost, bool expected)
    {
        MercyAccrual.CanRedeem(held, cost).ShouldBe(
            expected,
            "24 §4.4 P2 and §5.1 author the exchange rates. An off-by-one here either strands the " +
            "player one token short of a counter their own screen shows as full, or hands them the " +
            "reward a token early and desynchronises the visible counter from the ledger.");
    }

    /// <summary>Spending takes exactly the cost and leaves the remainder.</summary>
    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(61, 60, 1)]
    [InlineData(600, 200, 400)]
    [InlineData(12, 12, 0)]
    public void Redeeming_spends_exactly_the_cost(int held, int cost, int expected)
    {
        MercyAccrual.Redeem(held, cost).ShouldBe(expected);
    }

    /// <summary>A bank that does not cover the cost is refused, not clamped to zero.</summary>
    /// <remarks>
    /// A clamped redemption would spend every token the player had and hand back the reward they
    /// could not afford — or worse, spend them for nothing. The balance is on their own screen
    /// (<c>24</c> §5.1: <em>"Set Tokens 7 / 12"</em>), so "not enough" is a state the caller can check.
    /// </remarks>
    [Theory]
    [InlineData(0, 60)]
    [InlineData(59, 60)]
    [InlineData(11, 12)]
    public void Redeeming_below_the_cost_is_refused_rather_than_clamped(int held, int cost)
    {
        Should.Throw<InvalidOperationException>(() => MercyAccrual.Redeem(held, cost));
    }

    /// <summary>A cost below 1 is not a sink: it would hand out the reward forever, for free.</summary>
    /// <inheritdoc cref="A_negative_bank_or_grant_is_refused" path="/remarks"/>
    [Theory]
    [InlineData(60, 0, "cost")]
    [InlineData(60, -1, "cost")]
    [InlineData(-1, 60, "held")]
    public void A_bank_or_cost_outside_its_stated_range_is_refused(int held, int cost, string parameter)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MercyAccrual.CanRedeem(held, cost))
            .ParamName.ShouldBe(parameter);
        Should.Throw<ArgumentOutOfRangeException>(() => MercyAccrual.Redeem(held, cost))
            .ParamName.ShouldBe(parameter);
    }

    /// <summary>
    /// Accrual and redemption meet: enough misses reach the sink exactly, which is the whole promise
    /// of M3 — <em>"a player who keeps playing will finish their set, on a schedule they can see"</em>.
    /// </summary>
    [Fact]
    public void Twelve_accruals_of_one_reach_the_set_token_sink_exactly()
    {
        var bank = Enumerable.Range(0, 12).Aggregate(0, (held, _) => MercyAccrual.Accrue(held, 1));

        MercyAccrual.CanRedeem(bank, 12).ShouldBeTrue(
            "24 §5.1: salvaging an SS yields 1 Set Token and 12 buy a chosen SS item. Twelve " +
            "salvages must reach the sink, or the deterministic spine the endgame rests on is off " +
            "by a token.");
        MercyAccrual.Redeem(bank, 12).ShouldBe(0);
    }
}
