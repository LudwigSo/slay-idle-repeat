using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Dice Forge screen: the six faces of the run's own die, the upgrades the rules layer offers,
/// and the one command that installs one.
/// </summary>
public sealed class DiceForgePresenterTests
{
    private static readonly PlayerId Player = new("PLAYER_1");
    private static readonly RunId Run = new("RUN_1");

    /// <summary>
    /// 🔒 The tile kind this screen opens on is read off the rules layer's own enum, and this holds
    /// it against the shared transcription — the one place the two can disagree.
    /// </summary>
    [Fact]
    public void The_tile_kind_this_screen_opens_on_is_the_one_the_shared_table_calls_a_dice_forge()
    {
        BoardTileKinds.NameKeyFor(DiceForgePresenter.DiceForgeTileKind).ShouldBe(
            "loc.tile.dice_forge.name",
            "if this is red a tile kind was inserted or reordered, and either the table is stale or " +
            "this screen now opens on whatever took the forge's place.");
    }

    // ---- the die ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_run_standing_on_a_forge_draws_all_six_faces_of_its_own_die()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtAForge()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(DiceForgeStage.Ready);
        presenter.Faces.Select(face => face.FaceIndex).ShouldBe(new[] { 1, 2, 3, 4, 5, 6 });
        presenter.Faces.Select(face => face.Current).ShouldBe(
            new[] { "1", "2", "3", "4", "5", "6" },
            "an untouched run rolls the starting die, and each face shows its pips.");
        presenter.Faces.ShouldAllBe(face => face.Upgradeable);
    }

    /// <summary>
    /// 🔒 …and a face an earlier forge already turned into a Surge is drawn AS a Surge and marked
    /// unupgradeable, rather than omitted.
    /// </summary>
    /// <remarks>
    /// The negative control for the case above, and the assertion that the screen shows the RUN's
    /// die rather than the starting one: a presenter reading <c>DieComposer.StartingDie</c> would
    /// satisfy the first case completely and fail this one.
    /// </remarks>
    [Fact]
    public async Task A_face_an_earlier_forge_changed_is_drawn_as_it_now_stands()
    {
        // 🔒 The code is written as a LITERAL, not built through DieFaceCodec — which is internal to
        // Core and correctly so. That makes this case a real check on the persisted encoding: a
        // change to the layout that forgot the rows already written turns it red, which is exactly
        // what a wire value's test is for.
        // Surge is DieFaceKind 3 (the enum starts at Pip = 1), tier 0, no pips: 3 × 1000.
        const int SurgeAtTierZero = 3000;

        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                AtAForge(dieFaceUpgrades: new Dictionary<int, int> { [2] = SurgeAtTierZero })));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Faces[1].Current.ShouldBe("Surge");
        presenter.Faces[1].Upgradeable.ShouldBeFalse(
            "04 §1 makes only a Pip face a legal source, so a face already upgraded cannot be a " +
            "second one's target.");
        presenter.Faces[0].Upgradeable.ShouldBeTrue("…and the rest of the die is unaffected.");
    }

    // ---- the menu --------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The options are the rules layer's, not this screen's — a screen with a list of its own
    /// would offer an option the handler refuses or miss one it allows.
    /// </summary>
    [Fact]
    public async Task The_offered_options_are_the_rules_layers_own_menu()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtAForge()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options.Count.ShouldBe(DiceForgeMenu.Offered.Count);
        presenter.Options.Select(option => option.OptionIndex).ShouldBe(
            Enumerable.Range(0, DiceForgeMenu.Offered.Count),
            "the index the command carries is the option's position in that menu.");
        presenter.Options.Count(option => option.NeedsPipCount).ShouldBe(
            1, "exactly one option is relative to the face it upgrades.");
    }

    [Fact]
    public async Task Every_option_is_named_rather_than_drawn_blank()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtAForge()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options.ShouldAllBe(option => option.Name.Length > 0);
        presenter.Options.Select(option => option.Name).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(presenter.Options.Count, "two options sharing a caption is one option.");
    }

    // ---- the command -----------------------------------------------------------------------------

    [Fact]
    public async Task Forging_submits_DICE_FORGE_CHOOSE_carrying_the_face_and_the_option()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAForge())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ForgeAsync(3, 0, higherPipValue: 6, CancellationToken.None);

        submission.ShouldBe(DiceForgeSubmission.Submitted);

        var command = host.SubmitCommand.ShouldBeOfType<DiceForgeChooseCommand>();

        command.FaceIndex.ShouldBe(3);
        command.OptionIndex.ShouldBe(0);
        command.HigherPipValue.ShouldBe(6);
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>A command the screen's own state forbids costs no round trip.</summary>
    [Fact]
    public async Task Forging_before_the_read_lands_never_reaches_the_host()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAForge());
        var presenter = Build(host);

        var submission = await presenter.ForgeAsync(1, 0, 6, CancellationToken.None);

        submission.ShouldBe(DiceForgeSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_forge_the_rules_layer_refuses_is_told_to_the_player()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAForge())
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ForgeAsync(1, 0, 6, CancellationToken.None);

        submission.ShouldBe(DiceForgeSubmission.RefusedByRules);
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 🔒 A host that never answered is told apart from a refusal: one says the game refused you,
    /// the other says the game did not answer, and a player told the first retries nothing.
    /// </summary>
    [Fact]
    public async Task A_host_that_does_not_answer_is_told_apart_from_a_refusal()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAForge())
            .FaultingItsCommands(new TimeoutException("no answer"));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ForgeAsync(1, 0, 6, CancellationToken.None);

        submission.ShouldBe(DiceForgeSubmission.HostUnavailable);
        presenter.HostFaulted.ShouldBeTrue();
        presenter.RulesRejection.ShouldBeNull(
            "a faulted call carried no outcome, so reporting a rejection would be inventing an " +
            "answer the game never gave.");
    }

    // ---- the states ------------------------------------------------------------------------------

    [Fact]
    public async Task A_run_standing_on_something_else_is_named_rather_than_drawn_as_a_forge()
    {
        var elsewhere = PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            pendingTileKind: (int)TileKind.Shop, pendingTileLinearIndex: 3, pendingTileStage: 1);

        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), elsewhere));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(DiceForgeStage.NotAtAForge);
        presenter.Faces.ShouldBeEmpty();
        presenter.Options.ShouldBeEmpty();
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_read_that_never_answered_is_its_own_state()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(DiceForgeStage.ReadUnavailable);
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_reference_collaborator_is_required()
    {
        Should.Throw<ArgumentNullException>(() =>
            new DiceForgePresenter(null!, RunDecisionContent.Catalogue(), Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new DiceForgePresenter(RecordingGameHost.FindingNoSuchPlayer(), null!, Player, Run));
    }

    // ---- fixture ---------------------------------------------------------------------------------

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    private static RunSnapshot AtAForge(IReadOnlyDictionary<int, int>? dieFaceUpgrades = null) =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 7,
            pendingTileKind: DiceForgePresenter.DiceForgeTileKind,
            pendingTileLinearIndex: 7,
            pendingTileStage: 1,
            dieFaceUpgrades: dieFaceUpgrades);

    private static DiceForgePresenter Build(RecordingGameHost host) =>
        new(host, RunDecisionContent.Catalogue(), Player, Run);
}
