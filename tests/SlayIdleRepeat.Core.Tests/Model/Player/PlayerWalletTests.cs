using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `10` §1 / `30` §7 / `30` §11.5 — the wallet: which currencies it holds, the one seam that
/// moves them, the event every movement produces, and the invariant that a balance never goes
/// negative.
/// </summary>
public sealed class PlayerWalletTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player Player(params (CurrencyId Currency, long Balance)[] balances) =>
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet(balances)), Content)
            .Value;

    /// <summary>
    /// 🔒 The wallet holds exactly the six player-scoped currencies of `10` §1 — <b>not</b>
    /// <c>GOLD</c>, which is run-scoped (assumption A3), and <b>not</b> <c>ENERGY</c>, which is
    /// held as the two banks of `28` C.
    /// </summary>
    /// <remarks>
    /// Stated as an exact set rather than a superset. A ninth currency appended to
    /// <see cref="CurrencyId"/> and quietly adopted into every wallet would change every
    /// <c>stateHash</c> in existence, and a "contains the six" assertion would not notice.
    /// </remarks>
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

        // …and the two the enum has that this aggregate deliberately does not carry as rows.
        Enum.GetValues<CurrencyId>()
            .Except(Core.Model.Player.WalletCurrencies)
            .ShouldBe(new[] { CurrencyId.GOLD, CurrencyId.ENERGY }, ignoreOrder: true);
    }

    /// <summary>
    /// 🔒 …and the list itself cannot be rewritten through the reference it hands out. A bare array
    /// behind an <see cref="IReadOnlyList{T}"/> casts straight back to <c>CurrencyId[]</c>, so a
    /// caller could redefine what a wallet <b>is</b>, process-wide.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Apply_is_the_only_public_mutation</c> would not see it: that rule inspects setters,
    /// public fields, constructors and mutating methods, not exposed collections. The two sibling
    /// assertions in this file cover the same hole for <c>Wallet</c> and the counter maps.
    /// </remarks>
    [Fact]
    public void The_wallet_currency_list_cannot_be_rewritten_through_its_reference()
    {
        (Core.Model.Player.WalletCurrencies as CurrencyId[]).ShouldBeNull(
            "a bare array behind IReadOnlyList<T> is a public mutation path in disguise");

        Should.Throw<NotSupportedException>(
            () => ((IList<CurrencyId>)Core.Model.Player.WalletCurrencies)[0] = CurrencyId.GOLD);

        Core.Model.Player.WalletCurrencies[0].ShouldBe(CurrencyId.CROWNS);
    }

    /// <summary>A credit moves the balance and produces the `30` §7 event that attributes it.</summary>
    [Fact]
    public void A_credit_moves_the_balance_and_emits_CurrencyChanged()
    {
        var player = Player((CurrencyId.CROWNS, 10));

        var moved = player.MoveCurrency(CurrencyId.CROWNS, 60, "ftue_treasure_tile");

        player.BalanceOf(CurrencyId.CROWNS).ShouldBe(70);
        moved.ShouldBeOfType<CurrencyChanged>();
        moved.Id.ShouldBe(CurrencyId.CROWNS);
        moved.Delta.ShouldBe(60);
        moved.Reason.ShouldBe("ftue_treasure_tile");
    }

    /// <summary>A debit is the same seam with a negative delta, and it too is attributed.</summary>
    [Fact]
    public void A_debit_moves_the_balance_and_emits_CurrencyChanged()
    {
        var player = Player((CurrencyId.SOUL_SHARDS, 300));

        var moved = player.MoveCurrency(CurrencyId.SOUL_SHARDS, -300, "energy_refill_purchase");

        player.BalanceOf(CurrencyId.SOUL_SHARDS).ShouldBe(0);
        moved.Delta.ShouldBe(-300);
    }

    /// <summary>
    /// 🔒 The event leaves <c>Sequence</c> unstamped. <c>DomainEvent</c> is explicit that the
    /// ordinal belongs to <c>GameRules.Apply</c> — a mutator cannot know its position in a list the
    /// command has not finished building.
    /// </summary>
    [Fact]
    public void The_emitted_event_leaves_its_sequence_for_Apply_to_stamp()
    {
        var moved = Player().MoveCurrency(CurrencyId.HONOR, 5, "pvp_duel_win");

        moved.Sequence.ShouldBe(
            0,
            "DomainEvent's ordinal is assigned by GameRules.Apply; asserting against the constant " +
            "the producer emits would hold for whatever value that constant took.");

        // …and Apply can stamp it, because every component but Reason stays a positional `init`.
        (moved with { Sequence = 3 }).Sequence.ShouldBe(3);
    }

    /// <summary>
    /// 🔒 `30` §11.5 — <em>"a currency never goes negative"</em>. The aggregate refuses rather than
    /// clamping: a clamp would let a rule that debited without checking look like it succeeded.
    /// </summary>
    [Fact]
    public void A_movement_that_would_go_negative_is_refused()
    {
        var player = Player((CurrencyId.MERGE_DUST, 40));

        var act = () => player.MoveCurrency(CurrencyId.MERGE_DUST, -41, "forge_merge");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*30 §11.5*never goes negative*");

        player.BalanceOf(CurrencyId.MERGE_DUST).ShouldBe(40, "a refused movement changes nothing");
    }

    /// <summary>Spending the balance down to exactly zero is legal — the boundary is not off by one.</summary>
    [Fact]
    public void A_movement_down_to_exactly_zero_is_allowed()
    {
        var player = Player((CurrencyId.MERGE_DUST, 40));

        player.MoveCurrency(CurrencyId.MERGE_DUST, -40, "forge_merge");

        player.BalanceOf(CurrencyId.MERGE_DUST).ShouldBe(0);
    }

    /// <summary>
    /// An overflowing grant is refused rather than wrapping. <c>long.MaxValue + 1</c> is a large
    /// negative in the default unchecked context, which would walk straight past the guard above.
    /// </summary>
    [Fact]
    public void A_movement_that_would_overflow_is_refused()
    {
        var player = Player((CurrencyId.CROWNS, long.MaxValue));

        var act = () => player.MoveCurrency(CurrencyId.CROWNS, 1, "economy_defect");

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*overflows a 64-bit balance*");

        player.BalanceOf(CurrencyId.CROWNS).ShouldBe(long.MaxValue);
    }

    /// <summary>
    /// 🔒 <c>GOLD</c> is refused by name, with the reason. A zero would read as "the player has no
    /// Gold", which is a lie about the wrong aggregate rather than a balance.
    /// </summary>
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

    /// <summary>
    /// 🔒 <c>ENERGY</c> is refused by name too, and the message points at the method that does move
    /// it. Two ways to change one balance is the second source of truth this split exists to avoid.
    /// </summary>
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

    /// <summary>An undefined <see cref="CurrencyId"/> is an uninitialised field, not a balance.</summary>
    [Fact]
    public void An_undefined_currency_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Player().BalanceOf((CurrencyId)999))
              .Message.ShouldMatchWildcard("*not one of them*");
    }

    /// <summary>
    /// 🔒 The reason is mandatory, because it is the attribution column of `21` §8.3's
    /// <c>income_attribution.csv</c> — the report answering risk R10.
    /// </summary>
    [Fact]
    public void A_movement_with_no_reason_is_refused_by_the_event_itself()
    {
        var player = Player((CurrencyId.CROWNS, 10));

        Should.Throw<ArgumentException>(() => player.MoveCurrency(CurrencyId.CROWNS, 1, "  "))
              .Message.ShouldMatchWildcard("*income_attribution.csv*");
    }

    /// <summary>
    /// The wallet handed out is read-only, and the reference it hands out cannot be walked back to
    /// a mutable dictionary — the whole point of replacing it wholesale rather than editing it.
    /// </summary>
    [Fact]
    public void The_exposed_wallet_cannot_be_mutated_through_its_reference()
    {
        var player = Player((CurrencyId.CROWNS, 10));
        var exposed = player.Wallet;

        Should.Throw<NotSupportedException>(() => ((IDictionary<CurrencyId, long>)exposed)[CurrencyId.CROWNS] = 999);

        player.BalanceOf(CurrencyId.CROWNS).ShouldBe(10);
    }

    /// <summary>
    /// 🔒 A wallet missing a row is a corrupt row rather than a zero balance — a dropped column and
    /// a genuinely empty purse must not read the same.
    /// </summary>
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

    /// <summary>A negative balance in the persisted row is refused at the seam, per `30` §11.5.</summary>
    [Fact]
    public void A_negative_persisted_balance_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet((CurrencyId.BEAST_FEED, -1))), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Wallet[BEAST_FEED] is -1", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A persisted row carrying a <c>GOLD</c> or <c>ENERGY</c> wallet entry is refused: it is a
    /// second copy of a balance that lives somewhere else.
    /// </summary>
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

    /// <summary>A null wallet is refused; an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_wallet_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(wallet: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Wallet is null", Case.Sensitive);
    }
}
