using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The wallet's invariants: which currencies it holds and the refusals no command can reach —
/// handlers check balances before they debit, so <c>Apply</c> cannot express these inputs.
/// Movement behaviour itself is covered at the <c>Apply</c> seam by the handler tests.
/// </summary>
public sealed class PlayerWalletTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player Player(params (CurrencyId Currency, long Balance)[] balances) =>
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet(balances)), Content)
            .Value;

    /// <summary>
    /// An exact set, not a superset: a ninth currency quietly adopted into every wallet would
    /// change every <c>stateHash</c> in existence, and "contains the six" would not notice.
    /// GOLD is run-scoped; ENERGY is held as two banks.
    /// </summary>
    [Fact]
    public void The_wallet_covers_every_player_scoped_currency_and_nothing_else()
    {
        Core.Model.Player.WalletCurrencies.ShouldBe(new[]
        {
            CurrencyId.CROWNS,
            CurrencyId.SOUL_SHARDS,
            CurrencyId.ENHANCE_STONES,
            CurrencyId.MERGE_DUST,
            CurrencyId.BEAST_FEED,
            CurrencyId.HONOR,
        });

        Enum.GetValues<CurrencyId>()
            .Except(Core.Model.Player.WalletCurrencies)
            .ShouldBe(new[] { CurrencyId.GOLD, CurrencyId.ENERGY }, ignoreOrder: true);
    }

    /// <summary>A bare array behind <see cref="IReadOnlyList{T}"/> would let a caller redefine what a wallet is, process-wide.</summary>
    [Fact]
    public void The_wallet_currency_list_cannot_be_rewritten_through_its_reference()
    {
        Should.Throw<NotSupportedException>(
            () => ((IList<CurrencyId>)Core.Model.Player.WalletCurrencies)[0] = CurrencyId.GOLD);

        Core.Model.Player.WalletCurrencies[0].ShouldBe(CurrencyId.CROWNS);
    }

    /// <summary>Refused, not clamped: a clamp would let a rule that debited without checking look like it succeeded.</summary>
    [Fact]
    public void A_movement_that_would_go_negative_is_refused()
    {
        var player = Player((CurrencyId.MERGE_DUST, 40));

        var act = () => player.MoveCurrency(CurrencyId.MERGE_DUST, -41, "forge_merge");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*30 §11.5*never goes negative*");

        player.BalanceOf(CurrencyId.MERGE_DUST).ShouldBe(40, "a refused movement changes nothing");
    }

    [Fact]
    public void A_movement_down_to_exactly_zero_is_allowed()
    {
        var player = Player((CurrencyId.MERGE_DUST, 40));

        player.MoveCurrency(CurrencyId.MERGE_DUST, -40, "forge_merge");

        player.BalanceOf(CurrencyId.MERGE_DUST).ShouldBe(0);
    }

    /// <summary><c>long.MaxValue + 1</c> is a large negative in the unchecked context, which would walk past the negative guard.</summary>
    [Fact]
    public void A_movement_that_would_overflow_is_refused()
    {
        var player = Player((CurrencyId.CROWNS, long.MaxValue));

        var act = () => player.MoveCurrency(CurrencyId.CROWNS, 1, "economy_defect");

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*overflows a 64-bit balance*");

        player.BalanceOf(CurrencyId.CROWNS).ShouldBe(long.MaxValue);
    }

    /// <summary>Refused by name rather than answering zero, which would read as "the player has no Gold".</summary>
    [Fact]
    public void GOLD_is_refused_because_it_belongs_to_the_Run_aggregate()
    {
        var player = Player();

        Should.Throw<ArgumentOutOfRangeException>(() => player.BalanceOf(CurrencyId.GOLD))
              .Message.ShouldMatchWildcard("*GOLD is RUN-scoped*Run aggregate*");

        Should.Throw<ArgumentOutOfRangeException>(
                  () => player.MoveCurrency(CurrencyId.GOLD, 150, "ftue_first_kill"))
              .Message.ShouldMatchWildcard("*GOLD is RUN-scoped*");
    }

    /// <summary>Two ways to change one balance is the second source of truth the bank split exists to avoid.</summary>
    [Fact]
    public void ENERGY_is_refused_from_the_wallet_seam_and_points_at_the_one_that_moves_it()
    {
        var player = Player();

        Should.Throw<ArgumentOutOfRangeException>(() => player.BalanceOf(CurrencyId.ENERGY))
              .Message.ShouldMatchWildcard("*two banks*Player.SetEnergy*");

        Should.Throw<ArgumentOutOfRangeException>(
                  () => player.MoveCurrency(CurrencyId.ENERGY, 40, "ad_energy"))
              .Message.ShouldMatchWildcard("*Player.SetEnergy*");
    }

    [Fact]
    public void An_undefined_currency_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Player().BalanceOf((CurrencyId)999))
              .Message.ShouldMatchWildcard("*not one of them*");
    }

    /// <summary>The reason is the attribution column downstream reports key on.</summary>
    [Fact]
    public void A_movement_with_no_reason_is_refused_by_the_event_itself()
    {
        var player = Player((CurrencyId.CROWNS, 10));

        Should.Throw<ArgumentException>(() => player.MoveCurrency(CurrencyId.CROWNS, 1, "  "))
              .Message.ShouldMatchWildcard("*income_attribution.csv*");
    }

    [Fact]
    public void The_exposed_wallet_cannot_be_mutated_through_its_reference()
    {
        var player = Player((CurrencyId.CROWNS, 10));
        var exposed = player.Wallet;

        Should.Throw<NotSupportedException>(() => ((IDictionary<CurrencyId, long>)exposed)[CurrencyId.CROWNS] = 999);

        player.BalanceOf(CurrencyId.CROWNS).ShouldBe(10);
    }

    /// <summary>A dropped column and a genuinely empty purse must not read the same.</summary>
    [Fact]
    public void A_wallet_missing_a_currency_is_refused()
    {
        var short6 = PlayerSnapshots.Wallet()
            .Where(entry => entry.Key != CurrencyId.HONOR)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(wallet: short6), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Wallet has no row for HONOR", Case.Sensitive);
    }

    [Fact]
    public void A_negative_persisted_balance_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet((CurrencyId.BEAST_FEED, -1))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Wallet[BEAST_FEED] is -1", Case.Sensitive);
    }

    /// <summary>A persisted GOLD/ENERGY entry is a second copy of a balance that lives elsewhere.</summary>
    [Theory]
    [InlineData(CurrencyId.GOLD)]
    [InlineData(CurrencyId.ENERGY)]
    public void A_wallet_carrying_a_non_player_scoped_currency_is_refused(CurrencyId currency)
    {
        var wallet = PlayerSnapshots.Wallet().ToDictionary(e => e.Key, e => e.Value);
        wallet[currency] = 1;

        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(wallet: wallet), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("second source of truth", Case.Sensitive);
    }

    [Fact]
    public void A_null_wallet_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(wallet: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Wallet is null", Case.Sensitive);
    }
}
