using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `03` §7 / `13` §1 — the run Shop screen (S08): a tile that stocks nothing, says why, and can be
/// left.
/// </summary>
/// <remarks>
/// 🔴 The whole screen is four sentences and one button, so almost every case here is about telling
/// four kinds of nothing apart. A shop with no stock, a screen opened where no shop is, a refused
/// departure and a host that never answered are the same blank page unless the wording differs.
/// </remarks>
public sealed class ShopPresenterTests
{
    /// <summary>An ordinary combat tile — the negative control for "this is not a shop".</summary>
    private const int EnemyTileKind = 0;

    private static readonly PlayerId Player = new("PLAYER_shop_51ca");
    private static readonly RunId Run = new("RUN_shop_8e02");

    // ---- the transcription --------------------------------------------------------------------

    /// <summary>
    /// 🔒 The tile kind this screen opens on is a transcription of a rules-internal enum, and this
    /// is what ties it to the one table that already transcribes the whole enum.
    /// </summary>
    /// <remarks>
    /// A bare <c>6</c> in a presenter agrees with itself forever. Asking the shared table what sits
    /// at that index turns a kind inserted above the shop into a failing case here, rather than into
    /// a shop screen that opens on treasure.
    /// </remarks>
    [Fact]
    public void The_tile_kind_this_screen_opens_on_is_the_one_the_shared_table_calls_a_shop()
    {
        BoardTileKinds.NameKeyFor(ShopPresenter.ShopTileKind).ShouldBe(
            "loc.tile.shop.name",
            "the shop screen transcribes its tile kind as a number, and the only thing that can " +
            "notice the number going stale is the table that transcribes all fourteen. If this is " +
            "red a tile kind was inserted or reordered, and this screen now opens on whatever took " +
            "the shop's place.");
    }

    // ---- the absence the screen exists to state ------------------------------------------------

    /// <summary>
    /// 🔒 <b>No buy slot and no refresh control.</b> The rules layer refuses every purchase and
    /// every refresh, so an affordance for either would be a control whose only possible outcome is
    /// a refusal — and, worse, a claim that an offer exists.
    /// </summary>
    [Fact]
    public void The_screen_draws_no_buy_slot_and_no_refresh_control()
    {
        ShopPresenter.BuySlotCount.ShouldBe(
            0,
            "a buy slot asserts an offer. The run row carries no stock, no price and no visit " +
            "count, and SHOP_BUY refuses every call — so a slot drawn here is a button that can " +
            "only ever tell the player their own purchase was illegal.");

        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.RefreshOffered.ShouldBeFalse(
            "and a refresh control asserts something to refresh. SHOP_REFRESH refuses every call " +
            "for the same reason: there is no offer list to replace. The refresh economy the design " +
            "describes — one free per visit, then an ad, then unavailable — is not modelled here at " +
            "all, and half of it drawn as a live button would be the other half invented.");
    }

    /// <summary>
    /// 🔒 The four ways this screen comes to nothing are four different sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the shipped locale, not over a fixture, and the distinction is the whole value
    /// of the case: every fixture string in this suite is derived from its own key, so four distinct
    /// keys give four distinct values by construction and a fixture-based version could never fail
    /// whatever anyone wrote in <c>en.json</c>.
    /// </remarks>
    [Fact]
    public void The_four_ways_this_screen_comes_to_nothing_are_four_different_authored_sentences()
    {
        string[] keys =
        [
            RunDecisionContent.ShopNothingStockedBlockKey,
            RunDecisionContent.ShopNotAtAShopStatusKey,
            RunDecisionContent.ShopRefusedStatusKey,
            RunDecisionContent.ShopHostUnavailableStatusKey,
        ];

        var authored = keys.Select(key =>
        {
            RunDecisionContent.ShippedEnglish.TryGetValue(key, out var sentence).ShouldBeTrue(
                $"'{key}' is not in the shipped English locale, so one of this screen's four " +
                "outcomes has no sentence and a player meeting it is shown its key.");

            return sentence!;
        }).ToArray();

        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the four are AUTHORED the same, so the screen has stopped distinguishing an " +
            "empty shop from a missing one, from a refusal, from a host that did not answer — and " +
            "three of those four are somebody's bug and one is not: " +
            $"[{string.Join(" | ", authored)}]");
    }

    // ---- the read ------------------------------------------------------------------------------

    [Fact]
    public void A_freshly_built_presenter_has_not_read_anything()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Stage.ShouldBe(ShopStage.NotYetRead);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopLoadingStatusKey));
    }

    /// <summary>
    /// 🔒 Building the screen submits nothing. Stated as a rule rather than an observation: a screen
    /// that leaves its own tile on construction resolves the tile before the player has read the one
    /// sentence the tile exists to show them.
    /// </summary>
    [Fact]
    public void Building_the_screen_submits_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAShop());

        _ = Build(host);

        host.SubmitCallCount.ShouldBe(
            0,
            "constructing the screen reached the host. Nothing on this screen may leave the tile " +
            "except the player pressing the one control it has.");
        host.ReadCallCount.ShouldBe(
            0,
            "and constructing it read as well. The read belongs to StartAsync, where a failure has " +
            "somewhere to be reported; a read in a constructor can only throw.");
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAShop());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(Run);
        host.ReadPlayer.ShouldBe(Player);
    }

    [Fact]
    public async Task A_run_standing_on_a_shop_puts_the_screen_on_its_ready_arm()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtAShop()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(ShopStage.Ready);
        presenter.StatusText.ShouldBeEmpty();
        presenter.NothingStockedText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopNothingStockedBlockKey),
            "the one thing this screen is for is saying why there is nothing to buy, and it says it " +
            "on the arm where a shop is actually open.");
    }

    /// <summary>
    /// 🔒 A screen opened on the wrong tile is its own state. Folding it into "no run" would tell a
    /// player their run is gone when they are standing on a campfire.
    /// </summary>
    [Fact]
    public async Task A_run_standing_on_something_else_is_named_rather_than_drawn_as_a_shop()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 2, pendingTileStage: 1)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(ShopStage.NotAtAShop);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopNotAtAShopStatusKey));
    }

    [Fact]
    public async Task A_read_that_finds_no_run_says_so_and_never_reads_as_a_shop()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(ShopStage.RunMissing);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopRunMissingStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(ShopStage.ReadUnavailable);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopReadUnavailableStatusKey));
    }

    // ---- leaving -------------------------------------------------------------------------------

    [Fact]
    public async Task Leaving_submits_RESOLVE_TILE_against_this_run()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAShop())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, position: 3));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.LeaveAsync(CancellationToken.None);

        submission.ShouldBe(ShopSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<ResolveTileCommand>();
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>
    /// 🔒 A command the screen's own state forbids costs no round trip. Submitted anyway it would
    /// come back with a value shared by four other refusals, and the sentence naming the real cause
    /// — that no shop is open — would be replaced by one naming nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(StatesThatCannotLeave))]
    public async Task Leaving_from_a_state_that_has_no_shop_open_submits_nothing_at_all(
        RunSnapshot? run)
    {
        var host = run is null
            ? RecordingGameHost.Reading(new OwnStateResult(OwnStateLookup.NoSuchRun, View: null))
            : RecordingGameHost.Finding(AnyPlayer(), run);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.LeaveAsync(CancellationToken.None);

        submission.ShouldBe(ShopSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    /// <summary>A run standing on the wrong tile, and no run at all — the two shapes with no shop.</summary>
    public static TheoryData<RunSnapshot?> StatesThatCannotLeave() => new()
    {
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 2, pendingTileStage: 1),
        null,
    };

    /// <summary>The third shape: a read that never answered, so no shop was ever established.</summary>
    [Fact]
    public async Task Leaving_after_a_read_that_faulted_submits_nothing_at_all()
    {
        var host = RecordingGameHost.FaultingItsRead(new TimeoutException("no answer"));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.LeaveAsync(CancellationToken.None);

        submission.ShouldBe(ShopSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 A refusal reaches the player. The one action this screen has coming back refused in
    /// silence is the failure another screen in this milestone actually shipped.
    /// </summary>
    [Fact]
    public async Task A_departure_the_rules_layer_refuses_is_told_to_the_player()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAShop())
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.LeaveAsync(CancellationToken.None);

        submission.ShouldBe(ShopSubmission.RefusedByRules);
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopRefusedStatusKey));
    }

    /// <summary>
    /// 🔒 <b>A faulting host is not silence, and it does not read as a rules refusal.</b> A refusal
    /// is an answer about the game; a fault is the game not answering. Told the first when it was
    /// the second, a player retries a move the rules would have allowed all along.
    /// </summary>
    [Fact]
    public async Task A_host_that_faults_on_the_way_out_is_told_apart_from_a_refusal()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAShop())
            .FaultingItsCommands(new TimeoutException("the submission never completed"));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.LeaveAsync(CancellationToken.None);

        submission.ShouldBe(ShopSubmission.HostUnavailable);
        presenter.HostFaulted.ShouldBeTrue();
        presenter.RulesRejection.ShouldBeNull(
            "a faulted call carried no outcome at all, so there is no rejection to report — and " +
            "reporting one would be inventing an answer the game never gave.");
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopHostUnavailableStatusKey),
            "and the sentence is its own. A fault dressed as ILLEGAL_STATE tells the player the " +
            "shop refused to let them leave, which is a bug report against the wrong system.");
    }

    /// <summary>
    /// 🔒 <b>A fault does not outlive the submission that faulted.</b> The next attempt that actually
    /// answers is what the player is told about.
    /// </summary>
    /// <remarks>
    /// Leaving is the only control this screen has, so it is also the only one a player retries. A
    /// screen that latched "the host did not answer" and never cleared it would answer the retry with
    /// the previous attempt's sentence, and every later refusal on the way out would read as a
    /// dropped connection for the rest of the tile.
    /// </remarks>
    [Fact]
    public async Task A_fault_does_not_survive_into_the_next_answer()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAShop())
            .FaultingItsCommands(new TimeoutException("the submission never completed"), times: 1)
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.LeaveAsync(CancellationToken.None)).ShouldBe(ShopSubmission.HostUnavailable);
        (await presenter.LeaveAsync(CancellationToken.None))
            .ShouldBe(
                ShopSubmission.RefusedByRules,
                "a departure that never completed left the run standing on the tile, so the retry " +
                "has to reach the host. A screen latched shut after one fault is a run that cannot " +
                "leave.");

        presenter.HostFaulted.ShouldBeFalse("the second submission completed, so nothing faulted.");
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.ShopRefusedStatusKey),
            "the fault's sentence was printed under a refusal that did answer. The flag is settled " +
            "before the command goes out, not only when one fails.");
    }

    /// <summary>
    /// 🔒 <b>No double submit.</b> The latch is taken before the await, not after it: taken
    /// afterwards, a second press arriving while the first is in flight finds it unset and submits
    /// again. That exact shape shipped once already in this milestone.
    /// </summary>
    [Fact]
    public async Task A_second_departure_while_one_is_in_flight_never_reaches_the_host_twice()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAShop())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, position: 3));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var first = presenter.LeaveAsync(CancellationToken.None);
        var second = presenter.LeaveAsync(CancellationToken.None);

        await Task.WhenAll(first, second);

        host.SubmitCallCount.ShouldBe(
            1,
            "both presses reached the host, so a double-tap resolved the tile twice. The second " +
            "call is refused by the screen's own latch, and the latch is taken before the await " +
            "rather than after it.");
    }

    // ---- the vocabulary ------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Neither of this screen's enums has a zero member, so a default-initialised field can never
    /// read as a real state.
    /// </summary>
    [Theory]
    [InlineData(typeof(ShopStage))]
    [InlineData(typeof(ShopSubmission))]
    public void No_state_this_screen_reports_is_the_default_value_of_its_own_type(Type vocabulary)
    {
        Enum.IsDefined(vocabulary, 0).ShouldBeFalse(
            $"{vocabulary.Name} has a member valued zero, so an uninitialised field of that type " +
            "reads as that member rather than as an obviously wrong value. Every member takes an " +
            "explicit value starting at one.");
    }

    // ---- construction --------------------------------------------------------------------------

    [Fact]
    public void Every_reference_collaborator_is_required()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ShopPresenter(null!, RunDecisionContent.Catalogue(), Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new ShopPresenter(RecordingGameHost.FindingNoSuchPlayer(), null!, Player, Run));
    }

    // ---- fixture -------------------------------------------------------------------------------

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    /// <summary>A run standing on an unresolved shop tile.</summary>
    private static RunSnapshot AtAShop() =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 3,
            pendingTileKind: ShopPresenter.ShopTileKind,
            pendingTileLinearIndex: 3,
            pendingTileStage: 1);

    private static ShopPresenter Build(RecordingGameHost host) =>
        new(host, RunDecisionContent.Catalogue(), Player, Run);
}
