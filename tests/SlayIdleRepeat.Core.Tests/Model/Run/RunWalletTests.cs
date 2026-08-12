using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `10` §1 / `30` §7 / `30` §11.5 — the run's wallet: the one <c>RUN</c>-scoped currency it
/// holds, the one seam that moves it, the event every movement produces, and the invariant that a
/// balance never goes negative.
/// </summary>
/// <remarks>
/// The mirror image of <c>PlayerWalletTests</c>. Milestone assumption <b>A3</b> splits `10` §1's
/// eight currencies across two aggregates: <c>GOLD</c> here, the six wallet rows and <c>ENERGY</c>
/// on <c>Player</c>. Each aggregate refuses the other's, by name and with the reason, rather than
/// answering zero — a zero reads as "the run has none", which about the wrong aggregate is a lie
/// rather than a balance.
/// </remarks>
public sealed class RunWalletTests
{
    private static Run Rich(long gold = 0) =>
        Run.Rehydrate(RunSnapshots.With(gold: gold)).Value;

    /// <summary>A credit moves the balance and produces the `30` §7 event that attributes it.</summary>
    [Fact]
    public void A_credit_moves_the_balance_and_emits_CurrencyChanged()
    {
        var run = Rich(150);

        CurrencyChanged moved = run.MoveCurrency(CurrencyId.GOLD, 60, "tile_kill_gold");

        run.Gold.ShouldBe(210);
        run.BalanceOf(CurrencyId.GOLD).ShouldBe(210);

        // 🔒 No `moved.ShouldBeOfType<CurrencyChanged>()` here. CurrencyChanged is a sealed record
        // and MoveCurrency's return type IS CurrencyChanged, so that assertion could only ever have
        // caught a null — which the three assertions below catch anyway, with a message that says
        // what was wrong (steering S1: an assertion true of every possible value is not an
        // assertion).
        moved.Id.ShouldBe(CurrencyId.GOLD);
        moved.Delta.ShouldBe(60);
        moved.Reason.ShouldBe("tile_kill_gold");
    }

    /// <summary>A debit is the same seam with a negative delta, and it too is attributed.</summary>
    [Fact]
    public void A_debit_moves_the_balance_and_emits_CurrencyChanged()
    {
        var run = Rich(300);

        var moved = run.MoveCurrency(CurrencyId.GOLD, -300, "in_run_shop_purchase");

        run.Gold.ShouldBe(0);
        moved.Delta.ShouldBe(-300);
        moved.Reason.ShouldBe("in_run_shop_purchase");
    }

    /// <summary>
    /// 🔒 The event leaves <c>Sequence</c> unstamped. <c>DomainEvent</c> is explicit that the ordinal
    /// belongs to <c>GameRules.Apply</c> — a mutator cannot know its position in a list the command
    /// has not finished building.
    /// </summary>
    [Fact]
    public void The_emitted_event_leaves_its_sequence_for_Apply_to_stamp()
    {
        var moved = Rich().MoveCurrency(CurrencyId.GOLD, 5, "tile_kill_gold");

        moved.Sequence.ShouldBe(
            0,
            "DomainEvent's ordinal is assigned by GameRules.Apply; asserting against the constant " +
            "the producer emits would hold for whatever value that constant took.");

        (moved with { Sequence = 3 }).Sequence.ShouldBe(3);
    }

    /// <summary>
    /// 🔒 `30` §11.5 — <em>"a currency never goes negative"</em>. The aggregate refuses rather than
    /// clamping: a clamp would let a rule that debited without checking look like it succeeded.
    /// </summary>
    [Fact]
    public void A_movement_that_would_go_negative_is_refused()
    {
        var run = Rich(40);

        var act = () => run.MoveCurrency(CurrencyId.GOLD, -41, "in_run_shop_purchase");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*30 §11.5*never goes negative*");

        run.Gold.ShouldBe(40, "a refused movement changes nothing");
    }

    /// <summary>Spending down to exactly zero is legal — the boundary is not off by one.</summary>
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

    /// <summary>
    /// An overflowing grant is refused rather than wrapping. <c>long.MaxValue + 1</c> is a large
    /// negative in the default unchecked context, which would walk straight past the guard above.
    /// </summary>
    [Fact]
    public void A_movement_that_would_overflow_is_refused()
    {
        var run = Rich(long.MaxValue);

        var act = () => run.MoveCurrency(CurrencyId.GOLD, 1, "economy_defect");

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*overflows a 64-bit balance*");

        run.Gold.ShouldBe(long.MaxValue);
    }

    /// <summary>
    /// 🔒 Every player-scoped currency is refused here by name, and the message points at the
    /// aggregate that does hold it — the mirror image of <c>Player</c>'s refusal of <c>GOLD</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated over the whole of <see cref="CurrencyId"/> minus <c>GOLD</c> rather than over a
    /// transcribed list of seven, so a ninth currency appended to the enum is refused here on the
    /// commit that adds it rather than silently becoming spendable out of a run's purse. The count is
    /// asserted so the theory cannot quietly shrink to nothing (steering S3).
    /// </remarks>
    [Fact]
    public void Every_player_scoped_currency_is_refused_and_the_message_points_at_Player()
    {
        var playerScoped = Enum.GetValues<CurrencyId>().Where(c => c != CurrencyId.GOLD).ToArray();

        playerScoped.Length.ShouldBe(
            7,
            "10 §1 fixes eight currencies and assumption A3 scopes exactly one of them to the run. " +
            "If this shrank, the loop below is refusing fewer currencies than the split has.");

        var run = Rich();

        foreach (var currency in playerScoped)
        {
            Should.Throw<ArgumentOutOfRangeException>(() => run.BalanceOf(currency))
                  .Message.ShouldMatchWildcard("*player-scoped*Player*");

            Should.Throw<ArgumentOutOfRangeException>(
                      () => run.MoveCurrency(currency, 1, "economy_defect"))
                  .Message.ShouldMatchWildcard("*player-scoped*Player*");
        }
    }

    /// <summary>An undefined <see cref="CurrencyId"/> is an uninitialised field, not a balance.</summary>
    /// <remarks>
    /// Both doors, because they are two guards: a reader that refused an undefined id while the
    /// mutator accepted it would let a run's purse be moved under a currency the game does not have.
    /// </remarks>
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

    /// <summary>
    /// 🔒 The reason is mandatory, because it is the attribution column of `21` §8.3's
    /// <c>income_attribution.csv</c> — the report answering risk R10.
    /// </summary>
    [Fact]
    public void A_movement_with_no_reason_is_refused_by_the_event_itself()
    {
        var run = Rich(10);

        Should.Throw<ArgumentException>(() => run.MoveCurrency(CurrencyId.GOLD, 1, "  "))
              .Message.ShouldMatchWildcard("*income_attribution.csv*");

        run.Gold.ShouldBe(10, "the event is built before the field is written, so nothing moved");
    }

    /// <summary>
    /// 🔒 <see cref="Run.MoveCurrency"/> is the <b>only</b> member outside the constructor that can
    /// change the balance: there is no setter, no <c>SetGold</c>, no <c>AddGold</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the type-shape half of the currency rule, and it is not a duplicate of
    /// <c>DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged</c>: that rule is an IL
    /// scan asking whether a write <em>emits</em>, and it would be perfectly happy with a second
    /// mutator that emitted too. What the design forbids is a <b>second seam</b>, because the
    /// argument for one write site is that a reader can find every Gold movement by finding one
    /// method. The set is read off the type rather than transcribed, and floored, so the assertion
    /// cannot pass by finding nothing.
    /// </remarks>
    [Fact]
    public void MoveCurrency_is_the_only_member_that_can_change_the_balance()
    {
        var members = typeof(Run)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            // The property's own get accessor compiles to `get_Gold`, which would match the filter
            // below for the getter this case is asserting IS the only Gold-named member.
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .ToArray();

        members.ShouldContain(
            nameof(Run.MoveCurrency),
            "if this is absent the reflection below is looking at the wrong type and the rest of " +
            "this case is asserting over nothing.");

        members.Where(name => name.Contains("Gold", StringComparison.OrdinalIgnoreCase))
               .ShouldBe(
                   new[] { nameof(Run.Gold) },
                   ignoreOrder: true,
                   "the getter is the only Gold-named member. A SetGold/AddGold beside " +
                   "MoveCurrency would be a second place a run's purse changes, and the reason " +
                   "MoveCurrency exists is that there is exactly one.");

        // 🔒 …and the same over the OTHER name the balance goes by. A filter on "Gold" alone is
        // blind to a second seam called AdjustWallet or SetWallet — which is the more likely name
        // for one, because the field it would write is `_wallet`. The two filters together are the
        // claim; either on its own leaves the obvious rename through.
        members.Where(name => name.Contains("Wallet", StringComparison.OrdinalIgnoreCase))
               .ShouldBe(
                   new[] { "_wallet" },
                   ignoreOrder: true,
                   "the backing field is the only Wallet-named member. Its NAME is what puts this " +
                   "run's Gold inside DomainPurityTests.CurrencyFields()'s subject set at all (a " +
                   "bare long matches neither half of the type predicate), so a Wallet-named " +
                   "method beside it is both a second seam and a second thing that rule watches.");
    }
}
