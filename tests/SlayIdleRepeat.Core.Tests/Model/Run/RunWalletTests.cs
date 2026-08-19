using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The run wallet's invariants — the mirror of <c>PlayerWalletTests</c>: each aggregate refuses
/// the other's currencies by name rather than answering zero. Gold movement behaviour is covered
/// at the <c>Apply</c> seam by the tile/shop handler tests.
/// </summary>
public sealed class RunWalletTests
{
    private static Run Rich(long gold = 0) =>
        Run.Rehydrate(RunSnapshots.With(gold: gold)).Value;

    /// <summary>Refused, not clamped: a clamp would let a rule that debited without checking look like it succeeded.</summary>
    [Fact]
    public void A_movement_that_would_go_negative_is_refused()
    {
        var run = Rich(40);

        var act = () => run.MoveCurrency(CurrencyId.GOLD, -41, "in_run_shop_purchase");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*30 §11.5*never goes negative*");

        run.Gold.ShouldBe(40, "a refused movement changes nothing");
    }

    [Fact]
    public void A_movement_down_to_exactly_zero_is_allowed()
    {
        var run = Rich(40);

        run.MoveCurrency(CurrencyId.GOLD, -40, "in_run_shop_purchase");

        run.Gold.ShouldBe(0);
    }

    /// <summary>A zero delta is permitted — <c>CurrencyChanged</c>'s own remarks make that a decision.</summary>
    [Fact]
    public void A_zero_delta_is_permitted()
    {
        var run = Rich(7);

        run.MoveCurrency(CurrencyId.GOLD, 0, "tile_kill_gold").Delta.ShouldBe(0);

        run.Gold.ShouldBe(7);
    }

    /// <summary><c>long.MaxValue + 1</c> is a large negative in the unchecked context, which would walk past the negative guard.</summary>
    [Fact]
    public void A_movement_that_would_overflow_is_refused()
    {
        var run = Rich(long.MaxValue);

        var act = () => run.MoveCurrency(CurrencyId.GOLD, 1, "economy_defect");

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*overflows a 64-bit balance*");

        run.Gold.ShouldBe(long.MaxValue);
    }

    public static TheoryData<CurrencyId> PlayerScopedCurrencies
    {
        get
        {
            var currencies = new TheoryData<CurrencyId>();
            foreach (var currency in Enum.GetValues<CurrencyId>().Where(c => c != CurrencyId.GOLD))
            {
                currencies.Add(currency);
            }

            return currencies;
        }
    }

    /// <summary>
    /// Derived from the whole of <see cref="CurrencyId"/> minus <c>GOLD</c>, so a currency appended
    /// to the enum is refused here on the commit that adds it rather than silently becoming
    /// spendable out of a run's purse.
    /// </summary>
    [Theory]
    [MemberData(nameof(PlayerScopedCurrencies))]
    public void A_player_scoped_currency_is_refused_and_the_message_points_at_Player(CurrencyId currency)
    {
        var run = Rich();

        Should.Throw<ArgumentOutOfRangeException>(() => run.BalanceOf(currency))
              .Message.ShouldMatchWildcard("*player-scoped*Player*");

        Should.Throw<ArgumentOutOfRangeException>(
                  () => run.MoveCurrency(currency, 1, "economy_defect"))
              .Message.ShouldMatchWildcard("*player-scoped*Player*");
    }

    /// <summary>Both doors: a reader refusing what the mutator accepted would still let the purse move.</summary>
    [Fact]
    public void An_undefined_currency_is_refused()
    {
        var run = Rich(10);

        Should.Throw<ArgumentOutOfRangeException>(() => run.BalanceOf((CurrencyId)999))
              .Message.ShouldMatchWildcard("*not one of them*");

        Should.Throw<ArgumentOutOfRangeException>(() => run.MoveCurrency((CurrencyId)999, 1, "economy_defect"))
              .Message.ShouldMatchWildcard("*not one of them*");

        run.Gold.ShouldBe(10, "a refused movement changes nothing");
    }

    /// <summary>The reason is the attribution column downstream reports key on.</summary>
    [Fact]
    public void A_movement_with_no_reason_is_refused_by_the_event_itself()
    {
        var run = Rich(10);

        Should.Throw<ArgumentException>(() => run.MoveCurrency(CurrencyId.GOLD, 1, "  "))
              .Message.ShouldMatchWildcard("*income_attribution.csv*");

        run.Gold.ShouldBe(10, "the event is built before the field is written, so nothing moved");
    }
}
