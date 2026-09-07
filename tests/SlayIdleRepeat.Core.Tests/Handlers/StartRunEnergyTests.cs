using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔴 <c>START_RUN</c>'s price: a run costs Energy, it is drawn from both banks, and a player who
/// cannot cover it is refused rather than charged.
/// </summary>
/// <remarks>
/// <para>
/// Its own file because the rule is its own: <c>StartRunTests</c> is about the <c>Run</c> the
/// handler builds and <c>StartRunChapterGateTests</c> about `10` §7's ladder, and both now have to
/// pay for the runs they open — which makes them the wrong place to state what the payment IS.
/// </para>
/// <para>
/// 🔒 <b>Nothing here compares against a bare 20.</b> The price is read from the document the
/// handler reads, and <see cref="The_price_is_the_documents_and_not_a_number_in_the_handler"/> moves
/// the document to prove it: a handler carrying its own copy agrees with the shipped file today and
/// charges the wrong price the first time the economy is retuned.
/// </para>
/// </remarks>
public sealed class StartRunEnergyTests
{
    /// <summary>The chapter a brand-new account may enter — the ladder's own front door.</summary>
    private const int OpenChapter = 1;

    /// <summary>The chapter behind a clear nothing in these fixtures has earned.</summary>
    private const int LockedChapter = 2;

    /// <summary>What a run costs, read from the document rather than transcribed.</summary>
    private static readonly int Price = ProgressionDocuments.ShippedRunCost;

    // ------------------------------------------------------------------------ the charge

    /// <summary>The price comes out of the main bar, and one event says so.</summary>
    /// <remarks>
    /// The bar holds five more than the price, so the leftover is a number neither zero nor the
    /// price: a handler that emptied the bar, or one that charged twice, answers something else.
    /// </remarks>
    [Fact]
    public void A_run_draws_its_price_from_the_main_bar_and_attributes_it()
    {
        var result = Start(Row(new EnergyBanks(Price + 5, 0)), OpenChapter);

        result.Accepted.ShouldBeTrue("START_RUN was refused " + result.Rejection + ".");

        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(5, 0),
            "the bar held five more than a run's price, so five is what is left of it.");

        var charge = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();

        charge.Id.ShouldBe(CurrencyId.ENERGY);
        charge.Delta.ShouldBe(
            -Price,
            "every currency movement in this game is attributed by a CurrencyChanged, and a run's " +
            "price is the one place Energy leaves a profile at all — an unattributed spend makes " +
            "the whole Energy budget unauditable.");
    }

    /// <summary>
    /// 🔒 The Reserve pays what the bar cannot, because a run is drawn from both banks.
    /// </summary>
    /// <remarks>
    /// The discriminating fixture: the bar alone is three short of the price and the Reserve covers
    /// the difference twice over. A handler deciding affordability from the main bar refuses this
    /// player outright, and <c>HomeEnergyView.Shortfall</c> — which asks <c>EnergyMath.Spend</c> the
    /// same question — would then be telling the screen the opposite of what the command does.
    /// </remarks>
    [Fact]
    public void The_reserve_pays_what_the_main_bar_cannot_cover()
    {
        var result = Start(Row(new EnergyBanks(Price - 3, 10)), OpenChapter);

        result.Accepted.ShouldBeTrue(
            "START_RUN was refused " + result.Rejection + ". The two banks hold more than the price " +
            "between them, and EnergyMath.Spend draws the bar first and the Reserve for the rest.");

        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(0, 7),
            "the bar is emptied first and the Reserve covers the three it was short by.");
    }

    // ------------------------------------------------------------------------ the refusal

    /// <summary>
    /// 🔒 A player whose two banks together fall short is refused, and nothing is spent on the way.
    /// </summary>
    /// <remarks>
    /// Six and three against a price of twenty: short in both banks together, and short by a number
    /// that is neither the price nor the bar. The lifetime run counter is asserted because it is the
    /// one thing a refusal must not move — the run seed and the minted run id are both derived from
    /// it, so a counter spent on a refused command collides the next run with this one.
    /// </remarks>
    [Fact]
    public void A_run_neither_bank_can_pay_for_is_refused_and_charges_nothing()
    {
        var banks = new EnergyBanks(6, 3);
        var state = new WorldSlice(Worlds.Rehydrated(PlayerSnapshots.With(energy: banks)), null);
        var before = state.Player.RunsStarted;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(OpenChapter, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeFalse("nine Energy does not buy a run priced at " + Price + ".");
        result.Rejection.ShouldBe(
            RejectionReason.INSUFFICIENT_ENERGY,
            "the refusal has to name the rule that fired. ILLEGAL_STATE here would mean the request " +
            "was malformed, and PREREQUISITE_NOT_CLEARED that the ladder refused it — three " +
            "different sentences the screen owes the player, all of which read as 'refused'.");

        result.NewState.Run.ShouldBeNull("a refused start opened a run.");
        result.NewState.Player.Energy.ShouldBe(
            banks, "a refused run charged the player for it anyway.");
        result.NewState.Player.RunsStarted.ShouldBe(
            before, "a refused run spent the lifetime counter the next run's seed is derived from.");
        result.Events.ShouldBeEmpty("a refused command produced an event.");
    }

    /// <summary>
    /// 🔒 The negative control on scope: a chapter the ladder has not opened is refused for the
    /// LADDER, even when the player also cannot afford it.
    /// </summary>
    /// <remarks>
    /// Both conditions are true at once — no clear and no Energy — and only one refusal can be
    /// named. The ladder's is the one the player is owed: the launch block only ever offers a stage
    /// already open, so a refill offer in front of a locked chapter sends the player to buy Energy
    /// for a run that would be refused at full banks anyway. Without this case the price could have
    /// been charged ahead of the ladder and nothing would have noticed.
    /// </remarks>
    [Fact]
    public void A_locked_chapter_is_refused_for_the_ladder_even_when_the_banks_are_empty()
    {
        var result = Start(Row(new EnergyBanks(0, 0)), LockedChapter);

        result.Accepted.ShouldBeFalse("nothing has cleared the way to chapter " + LockedChapter + ".");
        result.Rejection.ShouldBe(
            RejectionReason.PREREQUISITE_NOT_CLEARED,
            "the ladder is consulted before the price, so a player who is short of both is told " +
            "about the one they cannot fix by waiting.");
    }

    // ------------------------------------------------------------------------ the document

    /// <summary>
    /// 🔒 The price is <c>tuning/progression.json</c>'s, not a number in the handler.
    /// </summary>
    /// <remarks>
    /// One row, two documents. It holds exactly the retuned price, which is far below the shipped
    /// one — so the retuned run starts and the shipped one is refused. A handler carrying its own
    /// copy of the price answers the same way to both, and every reader believes it.
    /// </remarks>
    [Fact]
    public void The_price_is_the_documents_and_not_a_number_in_the_handler()
    {
        var row = Row(new EnergyBanks(RetunedPrice, 0));

        RetunedPrice.ShouldBeLessThan(
            Price, "the fixture only discriminates while the two documents disagree.");

        var retuned = SlayIdleRepeat.Core.GameRules.Apply(
            row, new StartRunCommand(OpenChapter, DifficultyTier.NORMAL),
            Worlds.Context with { Content = RetunedContent });

        retuned.Accepted.ShouldBeTrue(
            "START_RUN was refused " + retuned.Rejection + " against a document pricing a run at " +
            RetunedPrice + ", which is exactly what this row holds.");
        retuned.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(0, 0), "the retuned price emptied the bar exactly.");

        var shipped = Start(row, OpenChapter);

        shipped.Accepted.ShouldBeFalse(
            "the same row against the shipped document, which prices a run at " + Price + ". A " +
            "handler charging a constant accepts both and this case is about the difference.");
        shipped.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_ENERGY);
    }

    // -------------------------------------------------------------------------------- fixtures

    /// <summary>A run price nothing ships, so a handler holding the shipped one disagrees with it.</summary>
    private const int RetunedPrice = 7;

    /// <summary>The harness content with the run price — and only the run price — moved.</summary>
    private static readonly ContentSnapshot RetunedContent = ShippedHarness.WithShippedGaps(
        new ContentSnapshot(
            TuningDocuments.Shipped.Version,
            TuningDocuments.Shipped.DocumentPaths
                .Where(path => !string.Equals(path, ProgressionDocuments.DocumentPath, StringComparison.Ordinal))
                .Select(TuningDocuments.Shipped.GetDocument)
                .Append(
                    ProgressionDocuments.With(runCost: ContentValue.Number(RetunedPrice))
                        .GetDocument(ProgressionDocuments.DocumentPath))
                .ToArray()));

    private static WorldSlice Row(EnergyBanks energy) =>
        new(Worlds.Rehydrated(PlayerSnapshots.With(energy: energy)), null);

    private static CommandResult Start(WorldSlice state, int chapter) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new StartRunCommand(chapter, DifficultyTier.NORMAL), Worlds.Context);
}
