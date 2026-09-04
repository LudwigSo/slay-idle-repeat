using Shouldly;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Minigame screen: one tile, one of three games, and the single submission that pays it and
/// clears the tile.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Two authority arms behind one screen.</b> The timing bar is client-asserted — the tier is
/// the hit count and the command carries it — so it may only be submitted once the game is finished.
/// The chest pick and the dice duel are server-rolled: they submit a placeholder tier the handler
/// throws away, and the tier they actually got comes back on <c>MinigameResolved</c>. A screen that
/// reported its own claim on those two would tell the player they won something the server never
/// paid.
/// </para>
/// <para>
/// 🔴 <b>Every settled state offers a way back.</b> The screen is opened by the board's routing table
/// mid-run, and four of its states draw no game at all — so the one control it carries is the only
/// thing on screen. Gating it on the tile having cleared strands the player. Copied from the Event
/// screen, whose own suite found this the hard way.
/// </para>
/// <para>
/// 🔴 <b>A refused command comes back with an EMPTY state slice.</b> The host answers a rejection
/// with the player's own row and no run, so a screen that read its state back off a refused outcome
/// would blank the game the player is still looking at.
/// </para>
/// </remarks>
public sealed class MinigamePresenterTests
{
    /// <summary>An ordinary combat tile — the negative control for "this is not a minigame".</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's enum for the same reason <c>MinigamePresenter.MinigameTileKind</c>
    /// is: a literal here keeps pointing at whatever kind moves into that slot.
    /// </remarks>
    private const int EnemyTileKind = (int)TileKind.Enemy;

    /// <summary>The node the fixture run stands on.</summary>
    private const int Position = 5;

    /// <summary>The placeholder tier a server-rolled arm submits, which the handler never reads.</summary>
    private const int IgnoredClaim = 0;

    private static readonly PlayerId Player = new("PLAYER_minigame_4b19");
    private static readonly RunId Run = new("RUN_minigame_7e2c");

    // ---- the numbering ---------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The tile kind this screen opens on is read off the rules layer's own enum, and this holds
    /// that reading against the table the client transcribes the whole enum into.
    /// </summary>
    [Fact]
    public void The_tile_kind_this_screen_opens_on_is_the_one_the_shared_table_names() =>
        BoardTileKinds.NameKeyFor(MinigamePresenter.MinigameTileKind).ShouldBe(
            "loc.tile.minigame.name",
            "this screen reads one tile kind off the rules layer's enum and the shared table " +
            "transcribes that same numbering, so this is the one place the two can be held against " +
            "each other. If this is red a kind was inserted or reordered, and either the table is " +
            "stale or the screen now opens on the wrong tile.");

    /// <summary>
    /// 🔒 The three ids this suite's fixtures spell are exactly the arms the client draws.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>MinigameCatalogue</c> is internal to Core and this project cannot see it, so the ids in
    /// <see cref="MinigameContent"/> are transcribed. Without this, a rename in Core would leave every
    /// case below arming a presenter with an id the rules layer no longer knows — and the screen
    /// would settle on whatever an unknown arm settles on rather than on the arm the case named.
    /// </remarks>
    [Fact]
    public void The_ids_this_suite_arms_a_screen_with_are_the_arms_the_client_draws() =>
        MinigameArms.Built.ShouldBe(
            [MinigameContent.ChestPick, MinigameContent.TimingBar, MinigameContent.DiceDuel],
            ignoreOrder: true,
            "the fixture ids and the built-arm list have drifted apart. The list is derived from the " +
            "rules layer's own catalogue and these three are spelled by hand, so this is the one " +
            "place a rename in Core can be caught before it silently re-points every case here.");

    // ---- the read --------------------------------------------------------------------------------

    [Fact]
    public void A_freshly_built_presenter_has_not_read_anything()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Stage.ShouldBe(MinigameStage.NotYetRead);
        presenter.ResolvedTier.ShouldBe(
            MinigamePresenter.NoTierYet,
            "zero is a real tier — the lowest one — so a screen reporting it before anything has " +
            "resolved is showing the worst outcome to a player who has not played.");
        presenter.StatusText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.LoadingStatusKey));
    }

    /// <summary>🔒 Building the screen submits nothing and reads nothing.</summary>
    [Fact]
    public void Building_the_screen_submits_nothing_and_reads_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAMinigame());

        _ = Build(host);

        host.SubmitCallCount.ShouldBe(0);
        host.ReadCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAMinigame());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(Run);
        host.ReadPlayer.ShouldBe(Player);
        host.ReadCallCount.ShouldBe(
            1,
            "opening this screen cost more than one read of the same run. The reward rows, the " +
            "guarantee and the tile's own state all come out of one answer.");
    }

    /// <summary>🔒 Opening the screen submits nothing — the player has not played yet.</summary>
    /// <remarks>
    /// 🔴 Worse here than on most screens: <c>MINIGAME_SUBMIT</c> is accepted at a Minigame tile
    /// whatever the claimed tier, and it CLEARS the tile as its last step. A screen that submitted on
    /// opening would resolve the game on the player's behalf at whatever tier it happened to send —
    /// which is exactly the placeholder behaviour this screen replaces.
    /// </remarks>
    [Fact]
    public async Task Opening_the_screen_never_submits_anything_by_itself()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAMinigame());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.SubmitCallCount.ShouldBe(
            0,
            "the screen resolved the tile before the player touched anything. MINIGAME_SUBMIT " +
            "clears the tile as its last step, so this is the run being played for them.");
        presenter.Stage.ShouldBe(MinigameStage.Playing);
    }

    [Fact]
    public async Task A_read_that_finds_no_run_says_so()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.RunMissing);
        presenter.StatusText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.RunMissingStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.ReadUnavailable);
        presenter.StatusText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.ReadUnavailableStatusKey));
    }

    /// <summary>🔒 A run standing on some other tile is NAMED, and nothing is submitted on it.</summary>
    [Fact]
    public async Task A_run_standing_on_another_tile_is_named_rather_than_played()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), OnAnotherTile());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.NotAtAMinigame);
        presenter.StatusText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.NotAtAMinigameStatusKey));
        host.SubmitCallCount.ShouldBe(
            0,
            "MINIGAME_SUBMIT does not check what tile the run is standing on — it checks the run's " +
            "POSITION against its resolution map — so a submission sent because this screen happened " +
            "to be open would pay a minigame reward on an enemy tile and clear the fight's tile with it.");
    }

    /// <summary>
    /// 🔒 A content set that cannot describe the game is a sentence, not a blank screen and not a crash.
    /// </summary>
    [Fact]
    public async Task A_game_the_content_set_cannot_describe_says_so_rather_than_drawing_nothing()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()),
            MinigameContent.WithoutTheAuthoredNumbers());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.RulesUnavailable);
        presenter.StatusText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.RulesUnavailableStatusKey));
    }

    /// <summary>
    /// 🔒 <b>A tile whose minigame this run already resolved refuses, and never reaches the host.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <c>MINIGAME_SUBMIT</c> refuses a second submission at the run's position with
    /// <c>ILLEGAL_STATE</c> — the same wire value an unknown id and an out-of-range tier travel as —
    /// so the player would be shown a sentence that names none of the three. Answered here instead.
    /// </remarks>
    [Fact]
    public async Task A_minigame_already_resolved_at_this_tile_refuses_without_a_round_trip()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AlreadyPlayedHere());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.AlreadyResolved);
        presenter.StatusText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.AlreadyResolvedStatusKey));

        (await presenter.SubmitAsync(CancellationToken.None))
            .ShouldBe(MinigameSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            0,
            "the press went to the host on a tile the rules layer has already closed. It comes back " +
            "as ILLEGAL_STATE, which is the same value an unknown minigame id gets — so the sentence " +
            "the player is shown stops being about anything.");
    }

    // ---- the reward ladder -----------------------------------------------------------------------

    /// <summary>
    /// 🔒 The screen draws the arm's own reward ladder, chapter-scaled by the projection.
    /// </summary>
    /// <remarks>
    /// 🔴 The row count is per arm and the three built arms do not share one — a screen drawing a
    /// fixed number of rows shows the timing bar's fourth outcome on a chest pick that has three.
    /// </remarks>
    [Theory]
    [InlineData("MG_CHEST_PICK", 3)]
    [InlineData("MG_TIMING_BAR", 4)]
    [InlineData("MG_DICE_DUEL", 3)]
    public async Task The_reward_ladder_is_the_arms_own(string minigameId, int expectedRows)
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()), minigameId: minigameId);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Rows.Count.ShouldBe(
            expectedRows,
            minigameId + " pays " + expectedRows + " outcome tiers and the screen listed " +
            presenter.Rows.Count + ". A screen drawing a fixed number of rows offers one arm's " +
            "outcomes on another arm's game.");

        for (var tier = 0; tier < presenter.Rows.Count; tier++)
        {
            presenter.Rows[tier].Tier.ShouldBe(tier);
            presenter.OutcomeText(presenter.Rows[tier].Outcome).ShouldBe(
                MinigameContent.EnglishValueOf(
                    MinigameContent.OutcomeKeyFor(presenter.Rows[tier].Outcome)),
                "the outcome caption is resolved through the catalogue off the authored token. A " +
                "screen printing the token itself tells the player they won '" +
                presenter.Rows[tier].Outcome + "'.");
        }
    }

    /// <summary>
    /// 🔒 <b>The guarantee line is the chest pick's, and it names BOTH numbers.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Either number alone is unreadable: "you have missed three" says nothing about when the
    /// guarantee lands, and "one more chest" says nothing about the streak being counted — and a
    /// player watching a pity counter is watching precisely the pair.
    /// </remarks>
    [Fact]
    public async Task The_chest_picks_guarantee_line_names_both_counter_numbers()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()),
            minigameId: MinigameContent.ChestPick);

        await presenter.StartAsync(CancellationToken.None);

        var guarantee = MinigameView.Project(
            AtAMinigame(), AnyPlayer(), MinigameContent.Playable(), MinigameContent.ChestPick)
                !.Guarantee.ShouldNotBeNull(
                    "with no guarantee projected there are no numbers for the line to name, and the " +
                    "assertions below hold over whatever the screen said.");

        presenter.GuaranteeText.ShouldContain(
            guarantee.PicksUntilForced.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive,
            "the line does not say how many more picks the guarantee is away, which is the number a " +
            "player is counting.");
        presenter.GuaranteeText.ShouldContain(
            guarantee.ForcedOnPick.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive,
            "…and it does not say which pick is the forced one, so the countdown is a number with " +
            "nothing to count towards.");
        presenter.GuaranteeText.ShouldContain(
            MinigameContent.EnglishValueOf(MinigameContent.GuaranteeLabelKey),
            Case.Sensitive,
            "the line's own caption comes from the catalogue rather than from a literal this screen " +
            "spells.");
    }

    /// <summary>…and the arms with no counter draw no line at all.</summary>
    /// <remarks>
    /// 🔒 A countdown on a skill-scaled game is a promise nothing in the rules layer keeps: those
    /// three advance no counter, so the number would never move.
    /// </remarks>
    [Theory]
    [InlineData("MG_TIMING_BAR")]
    [InlineData("MG_DICE_DUEL")]
    public async Task An_arm_with_no_pity_counter_draws_no_guarantee_line(string minigameId)
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()), minigameId: minigameId);

        await presenter.StartAsync(CancellationToken.None);

        presenter.GuaranteeText.ShouldBeEmpty(
            minigameId + " drew a guarantee countdown. Only the chest pick carries a counter, so on " +
            "this arm the number would never move and the promise is one nothing keeps.");
    }

    // ---- the client-asserted arm -----------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The timing bar cannot be submitted before it is finished.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The tier IS the hit count, and every tier of the table is a legal claim — so an early
    /// submission is accepted, pays a tier the player did not earn and clears the tile. Nothing in
    /// the rules layer refuses it, which is why the refusal has to be here.
    /// </remarks>
    [Fact]
    public async Task The_timing_bar_is_not_submitted_until_it_has_been_played_out()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAMinigame());
        var presenter = Build(host, minigameId: MinigameContent.TimingBar);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Finished.ShouldBeFalse("nothing has been struck yet.");

        (await presenter.SubmitAsync(CancellationToken.None))
            .ShouldBe(MinigameSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            0,
            "the command went out mid-game. Every tier is a legal claim, so the rules layer would " +
            "accept it, pay a tier nobody played for and clear the tile — the refusal has to be here.");
        presenter.Stage.ShouldBe(MinigameStage.Playing);
    }

    /// <summary>
    /// 🔒 <b>Once finished, the timing bar submits its own hit count as the tier.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Two hit counts, one of them zero and one of them not, so a screen submitting a constant
    /// cannot satisfy both — and zero is the tier a screen that forgot the game entirely would send.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task A_finished_timing_bar_submits_the_tier_it_was_played_to(int hits)
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAMinigame())
            .AcceptingInto(OffTheTile());

        var presenter = Build(host, minigameId: MinigameContent.TimingBar);

        await presenter.StartAsync(CancellationToken.None);

        PlayTo(presenter, hits);

        presenter.Finished.ShouldBeTrue();
        presenter.Hits.ShouldBe(hits);

        (await presenter.SubmitAsync(CancellationToken.None)).ShouldBe(MinigameSubmission.Submitted);

        var submitted = host.SubmitCommand.ShouldBeOfType<MinigameSubmitCommand>();

        submitted.MinigameId.ShouldBe(MinigameContent.TimingBar);
        submitted.Result.ShouldBe(
            hits,
            "the player scored " + hits + " and the command claimed " + submitted.Result +
            ". The tier IS the hit count on a client-asserted arm, and every tier is legal — so a " +
            "wrong claim is paid rather than refused.");
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>The timing bar's own tier is what the screen then reports as resolved.</summary>
    [Fact]
    public async Task A_finished_timing_bar_reports_the_tier_it_claimed()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAMinigame())
            .AcceptingInto(OffTheTile());

        var presenter = Build(host, minigameId: MinigameContent.TimingBar);

        await presenter.StartAsync(CancellationToken.None);

        PlayTo(presenter, 2);

        (await presenter.SubmitAsync(CancellationToken.None)).ShouldBe(MinigameSubmission.Submitted);

        presenter.ResolvedTier.ShouldBe(2);
        presenter.ResolvedOutcome.ShouldBe(
            presenter.Rows[2].Outcome,
            "the outcome shown is the one the tier that was claimed pays, read off the ladder the " +
            "screen already drew rather than restated.");
    }

    // ---- the server-rolled arms ------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A server-rolled arm submits a placeholder and reads its tier back off the event.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 No persisted field carries a rolled tier, so <c>MinigameResolved</c> is the only honest
    /// source. The event names a tier the placeholder does NOT, so a screen reporting its own claim
    /// tells the player they won the bottom outcome whatever they were paid.
    /// </remarks>
    [Theory]
    [InlineData("MG_CHEST_PICK", 2, "GOLD")]
    [InlineData("MG_DICE_DUEL", 1, "WIN_2_1")]
    public async Task A_server_rolled_arm_reports_the_tier_the_server_actually_paid(
        string minigameId, int rolledTier, string rolledOutcome)
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAMinigame())
            .AcceptingInto(OffTheTile())
            .Emitting(new MinigameResolved(0, minigameId, rolledTier, rolledOutcome));

        var presenter = Build(host, minigameId: minigameId);

        await presenter.StartAsync(CancellationToken.None);

        presenter.IsServerRolled.ShouldBeTrue(
            minigameId + " is drawn by the server, and a screen that thought otherwise would submit " +
            "a claimed tier on a game the server rolls.");

        (await presenter.SubmitAsync(CancellationToken.None)).ShouldBe(MinigameSubmission.Submitted);

        var submitted = host.SubmitCommand.ShouldBeOfType<MinigameSubmitCommand>();

        submitted.MinigameId.ShouldBe(minigameId);
        submitted.Result.ShouldBe(
            IgnoredClaim,
            "a server-rolled arm has no tier to claim, so the command carries the placeholder the " +
            "handler throws away. Sending anything else reads as a claim the client is entitled to " +
            "make.");

        presenter.ResolvedTier.ShouldBe(
            rolledTier,
            "the server paid tier " + rolledTier + " and the screen reported " +
            presenter.ResolvedTier + ". No persisted field carries a rolled tier, so the resolution " +
            "event is the only honest source — a screen echoing its own placeholder would show the " +
            "bottom outcome whatever the player was paid.");
        presenter.ResolvedOutcome.ShouldBe(rolledOutcome);
    }

    /// <summary>
    /// 🔒 An accepted submission with no resolution event leaves the screen claiming nothing.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control for the case above. A screen that fell back to its own placeholder
    /// when the event was missing would report the bottom tier as a win — and the missing-event case
    /// is a real one: an older server, or a handler that stopped emitting.
    /// </remarks>
    [Fact]
    public async Task A_server_rolled_arm_with_no_resolution_event_claims_no_tier()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAMinigame())
            .AcceptingInto(OffTheTile());

        var presenter = Build(host, minigameId: MinigameContent.ChestPick);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.SubmitAsync(CancellationToken.None)).ShouldBe(MinigameSubmission.Submitted);

        presenter.ResolvedTier.ShouldBe(
            MinigamePresenter.NoTierYet,
            "the outcome carried no resolution event, so nothing said which tier was paid — and " +
            "reporting the submitted placeholder would show the bottom outcome as the result.");
        presenter.ResolvedOutcome.ShouldBeEmpty();
    }

    // ---- S24: the tile is cleared, or the player stays --------------------------------------------

    /// <summary>
    /// 🔒 An acceptance that lands with no pending tile resolves the screen and lets the player out.
    /// </summary>
    [Fact]
    public async Task An_acceptance_that_clears_the_tile_resolves_and_lets_the_player_leave()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()).AcceptingInto(OffTheTile()),
            minigameId: MinigameContent.ChestPick);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.SubmitAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.Resolved);
        presenter.CanLeave.ShouldBeTrue(
            "MINIGAME_SUBMIT clears the pending tile as its last step, and the run came back with " +
            "none — so Continue may hand back to the board.");
    }

    /// <summary>
    /// 🔒 …and an acceptance that leaves the tile PENDING gives neither.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control, and the one that keeps the board out of a loop: the decision latch
    /// logs "halted" when the same decision re-opens with the tile unchanged, so a screen that read
    /// "accepted" as "done" would hand back and be sent straight here again.
    /// </remarks>
    [Fact]
    public async Task An_acceptance_that_leaves_the_tile_pending_neither_resolves_nor_lets_the_player_leave()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()).AcceptingInto(AtAMinigame()),
            minigameId: MinigameContent.ChestPick);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.SubmitAsync(CancellationToken.None);

        presenter.Stage.ShouldNotBe(
            MinigameStage.Resolved,
            "the command was accepted but the tile it was supposed to clear is still pending, so " +
            "there is nothing resolved to report.");
        presenter.CanLeave.ShouldBeFalse(
            "handing back with the tile still pending re-opens this same decision, and the board " +
            "logs that as halted rather than drawing anything new.");
    }

    // ---- the refusals -----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A refused submission keeps the game the screen was already showing.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The trap.</b> A refused command answers with the player's row and NO run, so a screen
    /// that re-read its state off a refused outcome empties its reward ladder and leaves the player
    /// looking at nothing on a tile that is still pending.
    /// </remarks>
    [Fact]
    public async Task A_refused_submission_keeps_the_ladder_the_screen_was_already_showing()
    {
        var presenter = Build(
            RecordingGameHost
                .Finding(AnyPlayer(), AtAMinigame())
                .RefusingCommands(RejectionReason.ILLEGAL_STATE),
            minigameId: MinigameContent.DiceDuel);

        await presenter.StartAsync(CancellationToken.None);

        var rows = presenter.Rows.Count;

        rows.ShouldBeGreaterThan(0, "with no ladder drawn there is nothing for the refusal to lose.");

        (await presenter.SubmitAsync(CancellationToken.None))
            .ShouldBe(MinigameSubmission.RefusedByRules);

        presenter.Stage.ShouldBe(
            MinigameStage.Playing,
            "the submission was refused, so the tile is unresolved and the game is still the " +
            "player's to play.");
        presenter.Rows.Count.ShouldBe(
            rows,
            "🔴 the refused outcome carried an empty state slice, and the screen took it as the " +
            "truth: the reward ladder the player is still looking at was blanked by a command that " +
            "changed nothing.");
        presenter.CanLeave.ShouldBeFalse();
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.RefusedStatusKey));
    }

    /// <summary>🔒 A faulting host is not silence, and it does not read as a rules refusal.</summary>
    [Fact]
    public async Task A_host_that_faults_on_a_submission_is_told_apart_from_a_refusal()
    {
        var presenter = Build(
            RecordingGameHost
                .Finding(AnyPlayer(), AtAMinigame())
                .FaultingItsCommands(new TimeoutException("the submission never completed")),
            minigameId: MinigameContent.ChestPick);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.SubmitAsync(CancellationToken.None))
            .ShouldBe(MinigameSubmission.HostUnavailable);

        presenter.HostFaulted.ShouldBeTrue();
        presenter.RulesRejection.ShouldBeNull(
            "a faulted call carried no outcome, so reporting a rejection would be inventing an " +
            "answer the game never gave.");
        presenter.RejectionText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.HostUnavailableStatusKey));
        presenter.Stage.ShouldBe(
            MinigameStage.Playing, "nothing was resolved, so the game is still there to play.");
        presenter.CanLeave.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 <b>No double submit.</b> The latch is taken before the await, not after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Worse here than on most screens: the second command is refused by the rules layer as a
    /// duplicate at the same position, which reaches the player as <c>ILLEGAL_STATE</c> on a screen
    /// that has just paid them — so a double-tap on a won game reads as the game refusing the win.
    /// </para>
    /// <para>
    /// 🔴 <b>The submission is genuinely PAUSED, and that is the whole case.</b> Against a host that
    /// answers synchronously the first call runs to completion before it returns, so the second press
    /// meets a screen that has already settled and is turned away by the stage guard — which a screen
    /// with the latch after its await, or with no latch at all, passes just as happily. Measured on
    /// the sibling screens: with the latch moved after the await, the identically-shaped case still
    /// reported one submission.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_second_press_while_one_is_in_flight_never_reaches_the_host_twice()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAMinigame())
            .AcceptingInto(OffTheTile())
            .PausingItsCommands();

        var presenter = Build(host, minigameId: MinigameContent.ChestPick);

        await presenter.StartAsync(CancellationToken.None);

        var first = presenter.SubmitAsync(CancellationToken.None);

        // The first command is outstanding at this line: the tile is still pending and the screen is
        // still Playing, so the latch is the only thing that can refuse the second press. Released
        // before anything is awaited, because a screen with no latch sends the second command and
        // then waits on the paused host — and this case has to FAIL on that rather than hang the
        // suite waiting for an answer nobody is going to give.
        var second = presenter.SubmitAsync(CancellationToken.None);

        host.ReleaseSubmissions();

        await Task.WhenAll(first, second);

        host.SubmittedCommands.OfType<MinigameSubmitCommand>().Count().ShouldBe(
            1,
            "both presses reached the host, so a double-tap sent two submissions for one tile. The " +
            "second is refused by the rules layer as a duplicate — ILLEGAL_STATE, on a screen that " +
            "has just paid the player.");
        (await second).ShouldBe(
            MinigameSubmission.RefusedNotAvailable,
            "the second press arrived while the first was still in flight, so nothing was sent for " +
            "it — and a screen reporting anything else is reporting on a command it did not make.");
        (await first).ShouldBe(
            MinigameSubmission.Submitted,
            "the press that WAS sent still has to come back as sent once the host answers. A latch " +
            "that swallowed its own submission would leave a tile resolved and a screen that never " +
            "heard about it.");
    }

    // ---- the way out ------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 <b>No state this screen settles in leaves the player with nothing to press.</b>
    /// </summary>
    /// <remarks>
    /// The screen is opened by the board's routing table in the middle of a run, and in each of these
    /// four states it draws no playable game at all — so the one control it carries is the only thing
    /// on it. Gated on the tile having cleared, that control is drawn out of use here, and the run
    /// could then be left only by killing the application. The Event screen's own suite found this
    /// after the screen shipped; the same four states exist here.
    /// </remarks>
    [Fact]
    public async Task Every_settled_state_that_cannot_be_acted_on_still_offers_the_way_back()
    {
        var missing = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        var elsewhere = Build(RecordingGameHost.Finding(AnyPlayer(), OnAnotherTile()));

        var alreadyPlayed = Build(RecordingGameHost.Finding(AnyPlayer(), AlreadyPlayedHere()));

        var undescribable = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()),
            MinigameContent.WithoutTheAuthoredNumbers());

        await missing.StartAsync(CancellationToken.None);
        await elsewhere.StartAsync(CancellationToken.None);
        await alreadyPlayed.StartAsync(CancellationToken.None);
        await undescribable.StartAsync(CancellationToken.None);

        missing.Exit.ShouldBe(MinigameExit.ToTheBoard, "there is no run here to stand on.");
        elsewhere.Exit.ShouldBe(
            MinigameExit.ToTheBoard,
            "the run holds a DIFFERENT pending tile, so this screen cannot clear it and the board is " +
            "what routes the run to the screen that can.");
        elsewhere.CanLeave.ShouldBeFalse(
            "the tile is still pending — the way out here is not the tile having cleared, which is " +
            "the whole reason the control needs a second reason to be live.");
        alreadyPlayed.Exit.ShouldBe(
            MinigameExit.ToTheBoard,
            "the tile's one submission has been spent and the tile is STILL pending, so this screen " +
            "can do nothing at all with it and the board is the only surface still carrying an offer.");
        alreadyPlayed.CanLeave.ShouldBeFalse(
            "the run still reports a pending Minigame tile, so the ordinary way out is shut — which " +
            "is exactly the state that strands a player when Continue is gated on it alone.");
        undescribable.Exit.ShouldBe(
            MinigameExit.ToTheBoard,
            "a game this content set cannot describe is a tile that cannot be played, and giving the " +
            "run up is offered on the board rather than here.");
    }

    /// <summary>
    /// 🔒 <b>A read that never answered asks again, and does NOT hand back.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The difference is not cosmetic. This state knows nothing about the run, so handing back
    /// would give the board a minigame tile it has already latched a handover for — which it refuses
    /// as halted, leaving a live board whose own control has nothing legal to send for that tile.
    /// </remarks>
    [Fact]
    public async Task A_read_that_never_answered_is_asked_again_rather_than_handed_back()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.ReadUnavailable);
        presenter.Exit.ShouldBe(
            MinigameExit.ReadAgain,
            "the press is the retry. Handing back on a read this screen never got an answer to " +
            "would hand the board a decision it has latched, and the board's own press cannot " +
            "resolve a minigame tile.");
    }

    /// <summary>A game still being played offers no way out, and a resolved one offers the ordinary one.</summary>
    [Fact]
    public async Task A_game_still_being_played_is_the_one_state_with_no_way_out()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()).AcceptingInto(OffTheTile()),
            minigameId: MinigameContent.DiceDuel);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(MinigameStage.Playing);
        presenter.Exit.ShouldBe(
            MinigameExit.Nowhere,
            "the game is still the player's to play, and a screen that could be left here would " +
            "leave the tile pending behind it.");

        (await presenter.SubmitAsync(CancellationToken.None)).ShouldBe(MinigameSubmission.Submitted);

        presenter.Exit.ShouldBe(
            MinigameExit.ToTheBoard, "the tile has cleared, which is the ordinary way off this screen.");
    }

    // ---- reduced motion ---------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The screen takes the reduced-motion flag, defaults it off, and passes it to the game.
    /// </summary>
    /// <remarks>
    /// 🔴 Both arms. A screen that ignored the flag draws a cursor that moves for a player who asked
    /// for nothing to move; a screen that ignored the default leaves everyone stepping.
    /// </remarks>
    [Fact]
    public async Task Reduced_motion_is_taken_from_the_composition_and_defaults_off()
    {
        var timed = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()),
            minigameId: MinigameContent.TimingBar);

        var stepped = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()),
            minigameId: MinigameContent.TimingBar,
            reducedMotion: true);

        await timed.StartAsync(CancellationToken.None);
        await stepped.StartAsync(CancellationToken.None);

        timed.ReducedMotion.ShouldBeFalse("the flag defaults off, as it does on the other two screens.");
        stepped.ReducedMotion.ShouldBeTrue();

        timed.Advance(MinigameContent.SweepSeconds / 4);
        stepped.Advance(MinigameContent.SweepSeconds / 4);

        timed.Cursor.ShouldBeGreaterThan(
            0.0, "a timed game's cursor moves with the clock, or there is nothing to aim at.");
        stepped.Cursor.ShouldBe(
            0.0,
            1e-9,
            "the same elapsed time moved a reduced-motion cursor. That is the animation the player " +
            "asked to be rid of, and the screen passed the flag nowhere.");

        stepped.Step();

        stepped.Cursor.ShouldBeGreaterThan(
            0.0, "the step is the only way a reduced-motion player aims, and it moved nothing.");
    }

    // ---- the vocabulary ---------------------------------------------------------------------------

    /// <summary>
    /// 🔒 None of this screen's enums has a zero member, so a default-initialised field can never
    /// read as a real state.
    /// </summary>
    [Theory]
    [InlineData(typeof(MinigameStage))]
    [InlineData(typeof(MinigameSubmission))]
    [InlineData(typeof(MinigameExit))]
    public void No_state_this_screen_reports_is_the_default_value_of_its_own_type(Type vocabulary) =>
        Enum.IsDefined(vocabulary, 0).ShouldBeFalse(
            $"{vocabulary.Name} has a member valued zero, so an uninitialised field of that type " +
            "reads as that member rather than as an obviously wrong value. Every member takes an " +
            "explicit value starting at one.");

    // ---- the strings ------------------------------------------------------------------------------

    /// <summary>🔒 Every caption this screen owns comes out of the catalogue, not out of a literal.</summary>
    /// <remarks>
    /// The fixture values are the key with a marker in front, so nothing a presenter spelled by hand
    /// can match one by accident. The status lines are asserted by the case for each arm they belong
    /// to; these are on screen whatever the stage.
    /// </remarks>
    [Fact]
    public void Every_caption_this_screen_owns_is_resolved_through_the_catalogue()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Title.ShouldBe(MinigameContent.EnglishValueOf(MinigameContent.TitleNameKey));
        presenter.RewardsLabel.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.RewardsLabelKey));
        presenter.HitsLabel.ShouldBe(MinigameContent.EnglishValueOf(MinigameContent.HitsLabelKey));
        presenter.StrikesLeftLabel.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.StrikesLeftLabelKey));
        presenter.ResultLabel.ShouldBe(MinigameContent.EnglishValueOf(MinigameContent.ResultLabelKey));
        presenter.StrikeText.ShouldBe(MinigameContent.EnglishValueOf(MinigameContent.StrikeActionKey));
        presenter.StepText.ShouldBe(MinigameContent.EnglishValueOf(MinigameContent.StepActionKey));
        presenter.PickChestText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.PickChestActionKey));
        presenter.RollText.ShouldBe(MinigameContent.EnglishValueOf(MinigameContent.RollActionKey));
        presenter.ContinueText.ShouldBe(
            MinigameContent.EnglishValueOf(MinigameContent.ContinueActionKey));
    }

    /// <summary>
    /// 🔒 Each arm names itself and states its own rule, and no two arms share either.
    /// </summary>
    /// <remarks>
    /// 🔴 The distinctness is the claim: a screen resolving one key for every arm passes an
    /// "it came from the catalogue" assertion and titles the dice duel "Three Chests".
    /// </remarks>
    [Theory]
    [InlineData("MG_CHEST_PICK", MinigameContent.ChestPickNameKey, MinigameContent.ChestPickRuleKey)]
    [InlineData("MG_TIMING_BAR", MinigameContent.TimingBarNameKey, MinigameContent.TimingBarRuleKey)]
    [InlineData("MG_DICE_DUEL", MinigameContent.DiceDuelNameKey, MinigameContent.DiceDuelRuleKey)]
    public void Each_arm_names_itself_and_states_its_own_rule(
        string minigameId, string nameKey, string ruleKey)
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAMinigame()), minigameId: minigameId);

        presenter.MinigameId.ShouldBe(minigameId);
        presenter.ArmName.ShouldBe(
            MinigameContent.EnglishValueOf(nameKey),
            minigameId + " is titled with another arm's name, so the player is told they are playing " +
            "a different game from the one on the screen.");
        presenter.RuleText.ShouldBe(
            MinigameContent.EnglishValueOf(ruleKey),
            minigameId + " states another arm's rule, so the only instruction the player gets " +
            "describes a game they are not playing.");
    }

    // ---- construction -----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Every reference collaborator is required, and refused at the ctor rather than at first use.
    /// </summary>
    [Fact]
    public void Every_reference_collaborator_is_required()
    {
        var content = MinigameContent.Playable();
        var strings = MinigameContent.Catalogue(MinigameContent.StringsOnly());
        var host = RecordingGameHost.FindingNoSuchPlayer();

        Should.Throw<ArgumentNullException>(() => new MinigamePresenter(
            null!, strings, content, Player, Run, MinigameContent.ChestPick));
        Should.Throw<ArgumentNullException>(() => new MinigamePresenter(
            host, null!, content, Player, Run, MinigameContent.ChestPick));
        Should.Throw<ArgumentNullException>(() => new MinigamePresenter(
            host, strings, null!, Player, Run, MinigameContent.ChestPick));
        Should.Throw<ArgumentException>(() => new MinigamePresenter(
            host, strings, content, Player, Run, "  "));
    }

    // ---- fixture ----------------------------------------------------------------------------------

    /// <summary>Plays a timing bar out to exactly the given hit count.</summary>
    /// <remarks>
    /// Aimed by advancing the clock rather than by setting the cursor, because there is no seam that
    /// sets it. Each strike is aimed at an ABSOLUTE time one whole sweep on from the last, so the
    /// cursor is back where it was and the clock never runs backwards.
    /// </remarks>
    private static void PlayTo(MinigamePresenter presenter, int hits)
    {
        var elapsed = 0.0;

        for (var strike = 0; strike < MinigameContent.Strikes; strike++)
        {
            var target = (strike * MinigameContent.SweepSeconds) +
                         (strike < hits
                             ? MinigameContent.SweepSeconds / 4
                             : MinigameContent.SweepSeconds / 2);

            presenter.Advance(target - elapsed);
            elapsed = target;

            presenter.Strike().ShouldBe(
                strike < hits,
                "strike " + strike + " landed at cursor " + presenter.Cursor + ", which is not " +
                "where this arrangement aimed it — so the hit count below is not the one the case " +
                "is about.");
        }
    }

    private static PlayerSnapshot AnyPlayer() => PlayerState.Rehydratable(Player);

    /// <summary>A run standing on an unresolved Minigame tile.</summary>
    private static RunSnapshot AtAMinigame() =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: Position,
            pendingTileKind: MinigamePresenter.MinigameTileKind,
            pendingTileLinearIndex: Position,
            pendingTileStage: 1);

    /// <summary>…and one whose minigame at this node has already been resolved.</summary>
    private static RunSnapshot AlreadyPlayedHere() => AtAMinigame() with
    {
        ResolvedMinigames = new Dictionary<int, string> { [Position] = MinigameContent.DiceDuel },
    };

    private static RunSnapshot OnAnotherTile() =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: Position,
            pendingTileKind: EnemyTileKind,
            pendingTileLinearIndex: Position,
            pendingTileStage: 1);

    /// <summary>A run whose tile has cleared — what an accepted submission hands back.</summary>
    private static RunSnapshot OffTheTile() =>
        PlayerState.Run(Run, Player, RunPhase.InProgress, position: Position);

    private static MinigamePresenter Build(
        RecordingGameHost host,
        ContentSnapshot? content = null,
        string minigameId = MinigameContent.ChestPick,
        bool reducedMotion = false)
    {
        var playable = content ?? MinigameContent.Playable();

        return new MinigamePresenter(
            host,
            MinigameContent.Catalogue(MinigameContent.StringsOnly()),
            playable,
            Player,
            Run,
            minigameId,
            reducedMotion);
    }
}
