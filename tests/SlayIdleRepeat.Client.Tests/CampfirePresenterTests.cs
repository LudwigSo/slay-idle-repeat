using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `03` §2 / `03` §7a.5 / `13` §1 — the Campfire / Shrine screen (S11): one screen, two arms, and
/// four absences it has to name.
/// </summary>
/// <remarks>
/// 🔴 Two of the campfire's three options are refused by the rules layer with the same wire value,
/// for two entirely different missing systems. The shrine has no choose command at all, so it picks
/// for the player. And its Cleanse arm can never fire, because a run holds no curse list. None of
/// those four is visible to a player unless this screen says it in its own words.
/// </remarks>
public sealed class CampfirePresenterTests
{
    /// <summary>An ordinary combat tile — the negative control for "this is neither arm".</summary>
    private const int EnemyTileKind = 0;

    /// <summary>The choice index the rules layer answers rest on.</summary>
    private const int RestChoiceIndex = 0;

    private static readonly PlayerId Player = new("PLAYER_camp_9b7d");
    private static readonly RunId Run = new("RUN_camp_04fe");

    // ---- the two readings of one numbering --------------------------------------------------------

    /// <summary>
    /// 🔒 Both tile kinds this screen opens on are read off the rules layer's own enum, and this
    /// holds that reading against the table the client transcribes the whole enum into.
    /// </summary>
    [Theory]
    [InlineData(CampfirePresenter.CampfireTileKind, "loc.tile.campfire.name")]
    [InlineData(CampfirePresenter.ShrineTileKind, "loc.tile.shrine.name")]
    public void The_tile_kinds_this_screen_opens_on_are_the_ones_the_shared_table_names(
        int kind, string expectedNameKey)
    {
        BoardTileKinds.NameKeyFor(kind).ShouldBe(
            expectedNameKey,
            "this screen reads two tile kinds off the rules layer's enum and the shared table " +
            "transcribes that same numbering, so this is the one place the two can be held against " +
            "each other. If this is red a kind was inserted or reordered, and either the table is " +
            "stale or the screen now opens one of its arms on the wrong tile — which for the shrine " +
            "arm means projecting a draw that will never be applied.");
    }

    // ---- the two arms never overlap ---------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A campfire draws no shrine rows and a shrine draws no campfire options.</b> An option
    /// card on a shrine is a control whose command the rules layer refuses; a buff row on a campfire
    /// describes a draw that never happened.
    /// </summary>
    [Fact]
    public async Task A_campfire_offers_its_three_options_and_no_shrine_rows()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtACampfire()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(CampfireStage.Campfire);
        presenter.Options.Select(row => row.Option).ShouldBe(
            [CampfireOption.Rest, CampfireOption.UpgradePerk, CampfireOption.RerollCharges]);
        presenter.ShrineRows.ShouldBeEmpty(
            "a campfire drew no shrine buffs, so any row here was invented rather than projected.");
    }

    [Fact]
    public async Task A_shrine_offers_the_two_rows_it_drew_and_no_campfire_options()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAShrine()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(CampfireStage.Shrine);
        presenter.ShrineRowsAvailable.ShouldBeTrue();
        presenter.ShrineRows.Count.ShouldBe(
            2,
            "the buff pool is authored to offer exactly two distinct options, and both are shown: " +
            "the player did not choose between them, so hiding the one that will not be taken would " +
            "hide half of what the shrine actually did.");
        presenter.ShrineRows.Count(row => row.IsTaken).ShouldBe(
            1,
            "and exactly one is the row the resolver will apply. Marking none leaves the screen " +
            "implying a choice; marking both claims two buffs are granted when only one is.");
        presenter.Options.ShouldBeEmpty(
            "a shrine offers no campfire options. A rest card here would submit a command the rules " +
            "layer refuses because the pending tile is not a campfire.");
    }

    /// <summary>
    /// 🔒 The shrine's rows are named through the keys the buff pool itself authors, so the screen
    /// carries no second copy of the ten names.
    /// </summary>
    /// <remarks>
    /// 🔒 The claim is about <c>Name</c>, so <c>Name</c> is what is asserted. Checking only that the
    /// row's ID maps to a known key leaves a screen that set the name to the id — <c>SHR_ATK</c> on
    /// the card, translated in no locale — passing a case whose title says the opposite. The row
    /// count is asserted first for the same reason: a claim made about every member of an empty list
    /// holds.
    /// </remarks>
    [Fact]
    public async Task A_shrine_row_is_named_through_the_key_the_buff_pool_authors()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAShrine()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.ShrineRows.Count.ShouldBe(2, "with no rows there is nothing below to be about.");

        foreach (var row in presenter.ShrineRows)
        {
            var key = "loc.shrine." +
                      row.BuffId.Replace("SHR_", "", StringComparison.Ordinal).ToLowerInvariant() +
                      ".name";

            RunDecisionContent.ShrineBuffNameKeys.ShouldContain(
                key, "'" + row.BuffId + "' is not one of the pool's ten authored rows.");
            row.Name.ShouldBe(
                RunDecisionContent.EnglishValueOf(key),
                "'" + row.BuffId + "' was drawn but not resolved through the key the pool authors " +
                "for it, so the card shows something the locale table never answered.");
        }
    }

    [Fact]
    public async Task A_run_standing_on_neither_is_named_rather_than_drawn_as_one_of_them()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 2, pendingTileStage: 1)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(CampfireStage.NotAtEither);
        presenter.Options.ShouldBeEmpty();
        presenter.ShrineRows.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireNotAtACampfireStatusKey));
    }

    // ---- the read ---------------------------------------------------------------------------------

    [Fact]
    public void A_freshly_built_presenter_has_not_read_anything()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Stage.ShouldBe(CampfireStage.NotYetRead);
        presenter.Options.ShouldBeEmpty();
        presenter.ShrineRows.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireLoadingStatusKey));
    }

    /// <summary>
    /// 🔒 Building the screen resolves nothing. A shrine resolves itself the moment the tile is
    /// resolved, so a screen that submitted on construction would apply a buff the player never saw.
    /// </summary>
    [Fact]
    public void Building_the_screen_submits_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAShrine());

        _ = Build(host);

        host.SubmitCallCount.ShouldBe(0);
        host.ReadCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtACampfire());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(Run);
        host.ReadPlayer.ShouldBe(Player);
    }

    /// <summary>
    /// 🔒 <b>The cold start reads the run ONCE, on either arm.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Pinned because nothing else here can see a second one. Every other case about the read
    /// asks which arm the screen settled on, and a screen that read twice settles on exactly the
    /// same arm — so a duplicated read is invisible to the whole suite while costing a real round
    /// trip. Both arms are named because they are two branches of one settle and either could grow
    /// a read of its own.
    /// </remarks>
    [Theory]
    [InlineData(CampfirePresenter.CampfireTileKind)]
    [InlineData(CampfirePresenter.ShrineTileKind)]
    public async Task A_cold_start_reads_the_run_exactly_once(int tileKind)
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), OnTile(tileKind));
        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadCallCount.ShouldBe(
            1,
            "opening this screen cost more than one read of the same run. Which arm it is, the " +
            "options it offers and the rows the shrine drew all come out of one answer, so a second " +
            "call is a second round trip that changes nothing a player sees.");
    }

    [Fact]
    public async Task A_read_that_finds_no_run_says_so()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(CampfireStage.RunMissing);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireRunMissingStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(CampfireStage.ReadUnavailable);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireReadUnavailableStatusKey));
    }

    /// <summary>
    /// 🔒 A shrine whose draw cannot be projected is a sentence, not two blank rows and not a crash.
    /// </summary>
    [Fact]
    public async Task A_shrine_whose_draw_cannot_be_projected_says_so_rather_than_drawing_nothing()
    {
        // The strings-only fixture carries no shrine buff pool, which is the shape a content set
        // stripped of a document the projection depends on actually has.
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtAShrine()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            CampfireStage.Shrine,
            "the run really is standing on a shrine — the failure is the content set's.");
        presenter.ShrineRowsAvailable.ShouldBeFalse();
        presenter.ShrineRows.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireShrineUnavailableStatusKey));
    }

    // ---- S2: the two refused options never share a sentence ---------------------------------------

    /// <summary>
    /// 🔒 <b>The two refused campfire options carry their own reasons.</b> Both come back on the
    /// wire as the same value, so the sentence is the only thing between "no perk-tier upgrade is
    /// tracked anywhere" and "no reroll charge exists to grant".
    /// </summary>
    [Fact]
    public async Task The_two_refused_options_do_not_share_one_sentence()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AtACampfire()));

        await presenter.StartAsync(CancellationToken.None);

        var rest = presenter.Options.Single(row => row.Option == CampfireOption.Rest);
        var upgrade = presenter.Options.Single(row => row.Option == CampfireOption.UpgradePerk);
        var charges = presenter.Options.Single(row => row.Option == CampfireOption.RerollCharges);

        rest.Available.ShouldBeTrue("resting heals the authored share of Max HP and clears the tile.");
        rest.BlockText.ShouldBeEmpty("and an option that works has nothing to explain.");

        upgrade.Available.ShouldBeFalse();
        charges.Available.ShouldBeFalse();
        upgrade.BlockText.ShouldNotBeNullOrWhiteSpace();
        charges.BlockText.ShouldNotBeNullOrWhiteSpace();
        upgrade.BlockText.ShouldNotBe(
            charges.BlockText,
            "the two unavailable options are unavailable for two completely different reasons — one " +
            "system does not track perk tiers outside the draft, the other has no reroll charge to " +
            "grant at all. One sentence for the pair tells a player the game has one hole where it " +
            "has two.");
    }

    /// <summary>
    /// 🔒 The four absences this screen names are four different sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the shipped locale, never over the fixture: fixture values are derived from
    /// their own keys, so distinct keys give distinct values by construction and a fixture-based
    /// version of this case could never fail whatever anyone wrote in <c>en.json</c>.
    /// </remarks>
    [Fact]
    public void The_four_absences_this_screen_names_are_four_different_authored_sentences()
    {
        string[] keys =
        [
            RunDecisionContent.CampfireUpgradePerkBlockKey,
            RunDecisionContent.CampfireRerollChargesBlockKey,
            RunDecisionContent.CampfireShrineChoiceBlockKey,
            RunDecisionContent.CampfireShrineCleanseBlockKey,
        ];

        var authored = keys.Select(key =>
        {
            RunDecisionContent.ShippedEnglish.TryGetValue(key, out var sentence).ShouldBeTrue(
                $"'{key}' is not in the shipped English locale, so one of this screen's four " +
                "absences has no sentence and a player meeting it is shown its key.");

            return sentence!;
        }).ToArray();

        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the four are AUTHORED the same. A perk tier that cannot be raised, a reroll " +
            "charge that does not exist, a shrine choice that was never given a command and a " +
            "Cleanse that cannot fire are four unrelated holes with four different fixes: " +
            $"[{string.Join(" | ", authored)}]");
    }

    /// <summary>
    /// 🔒 The shrine names both of its own facts, and names them apart from each other.
    /// </summary>
    [Fact]
    public async Task The_shrine_names_that_the_choice_is_not_the_players_and_that_cleanse_cannot_fire()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAShrine()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.ShrineChoiceBlockText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireShrineChoiceBlockKey),
            "there is no shrine choose command in the frozen vocabulary, so the first row is simply " +
            "taken and only its immediate-heal half is applied. A screen that showed two rows and " +
            "said nothing would read as a choice the player forgot to make.");
        presenter.ShrineCleanseBlockText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireShrineCleanseBlockKey),
            "and the Cleanse arm cannot fire at all, because a run carries no curse list. A shrine " +
            "that silently never cleanses looks exactly like one that rolled badly.");
    }

    // ---- the actions --------------------------------------------------------------------------------

    [Fact]
    public async Task Resting_submits_CAMPFIRE_CHOOSE_carrying_the_rest_index()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtACampfire())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, currentHp: 90));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None);

        submission.ShouldBe(CampfireSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<CampfireChooseCommand>().ChoiceIndex.ShouldBe(RestChoiceIndex);
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>
    /// 🔒 An option this screen draws as unavailable is refused here, without a round trip. The
    /// rules layer would refuse it too — with a value four other things share — and the sentence
    /// naming the real missing system would be replaced by one naming nothing.
    /// </summary>
    [Theory]
    [InlineData(CampfireOption.UpgradePerk)]
    [InlineData(CampfireOption.RerollCharges)]
    public async Task An_option_drawn_as_unavailable_never_reaches_the_host(CampfireOption option)
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtACampfire());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ChooseAsync(option, CancellationToken.None);

        submission.ShouldBe(CampfireSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Continuing_from_a_shrine_submits_RESOLVE_TILE()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAShrine())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ContinueAsync(CancellationToken.None);

        submission.ShouldBe(CampfireSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<ResolveTileCommand>();
    }

    /// <summary>
    /// 🔒 Each arm's action belongs to that arm. Resolving a campfire's tile accepts and does NOT
    /// clear it, so a Continue offered on a campfire would look like it had done nothing; and a rest
    /// submitted on a shrine is refused for a reason the player cannot see.
    /// </summary>
    [Fact]
    public async Task Neither_arms_action_is_offered_on_the_other_arm()
    {
        var campfireHost = RecordingGameHost.Finding(AnyPlayer(), AtACampfire());
        var campfire = Build(campfireHost);

        await campfire.StartAsync(CancellationToken.None);

        (await campfire.ContinueAsync(CancellationToken.None))
            .ShouldBe(CampfireSubmission.RefusedNotAvailable);
        campfireHost.SubmitCallCount.ShouldBe(0);

        var shrineHost = RecordingGameHost.Finding(AnyPlayer(), AtAShrine());
        var shrine = Build(shrineHost, BootContent.Shipped);

        await shrine.StartAsync(CancellationToken.None);

        (await shrine.ChooseAsync(CampfireOption.Rest, CancellationToken.None))
            .ShouldBe(CampfireSubmission.RefusedNotAvailable);
        shrineHost.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_rest_the_rules_layer_refuses_is_told_to_the_player()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtACampfire())
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None);

        submission.ShouldBe(CampfireSubmission.RefusedByRules);
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireRefusedStatusKey));
    }

    /// <summary>
    /// 🔒 <b>A faulting host is not silence, and it does not read as a rules refusal.</b>
    /// </summary>
    [Fact]
    public async Task A_host_that_faults_on_a_rest_is_told_apart_from_a_refusal()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtACampfire())
            .FaultingItsCommands(new TimeoutException("the submission never completed"));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None);

        submission.ShouldBe(CampfireSubmission.HostUnavailable);
        presenter.HostFaulted.ShouldBeTrue();
        presenter.RulesRejection.ShouldBeNull();
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireHostUnavailableStatusKey));
    }

    /// <summary>
    /// 🔒 <b>A fault does not outlive the submission that faulted.</b> The next attempt that actually
    /// answers is what the player is told about.
    /// </summary>
    /// <remarks>
    /// A faulted rest healed nothing and left the campfire pending, so the player presses it again. A
    /// screen that latched "the host did not answer" and never cleared it would answer that second
    /// press — refused, and refused for a reason worth reading — with the first press's sentence.
    /// </remarks>
    [Fact]
    public async Task A_fault_does_not_survive_into_the_next_answer()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtACampfire())
            .FaultingItsCommands(new TimeoutException("the submission never completed"), times: 1)
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None))
            .ShouldBe(CampfireSubmission.HostUnavailable);
        (await presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None))
            .ShouldBe(
                CampfireSubmission.RefusedByRules,
                "a rest that never completed healed nothing and left the tile pending, so the retry " +
                "has to reach the host.");

        presenter.HostFaulted.ShouldBeFalse("the second submission completed, so nothing faulted.");
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.CampfireRefusedStatusKey),
            "the fault's sentence was printed under a refusal that did answer. The flag is settled " +
            "before the command goes out, not only when one fails.");
    }

    /// <summary>
    /// 🔒 <b>No double submit.</b> The latch is taken before the await, not after it — taken
    /// afterwards, a double-tap on rest heals twice off one campfire.
    /// </summary>
    [Fact]
    public async Task A_second_rest_while_one_is_in_flight_never_reaches_the_host_twice()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtACampfire())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, currentHp: 90));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var first = presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None);
        var second = presenter.ChooseAsync(CampfireOption.Rest, CancellationToken.None);

        await Task.WhenAll(first, second);

        host.SubmitCallCount.ShouldBe(
            1,
            "both presses reached the host, so a double-tap rested twice on one campfire. The " +
            "second call is refused by the screen's own latch, and the latch is taken before the " +
            "await rather than after it.");
    }

    // ---- the vocabulary -------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 None of this screen's three enums has a zero member, so a default-initialised field can
    /// never read as a real state — and <c>CampfireOption</c> in particular, whose members sit
    /// beside wire indices that DO start at zero.
    /// </summary>
    [Theory]
    [InlineData(typeof(CampfireStage))]
    [InlineData(typeof(CampfireSubmission))]
    [InlineData(typeof(CampfireOption))]
    public void No_state_this_screen_reports_is_the_default_value_of_its_own_type(Type vocabulary)
    {
        Enum.IsDefined(vocabulary, 0).ShouldBeFalse(
            $"{vocabulary.Name} has a member valued zero, so an uninitialised field of that type " +
            "reads as that member rather than as an obviously wrong value. Every member takes an " +
            "explicit value starting at one — which for CampfireOption also keeps the enum from " +
            "being mistaken for the wire index, since rest travels as zero.");
    }

    // ---- construction -----------------------------------------------------------------------------------

    [Fact]
    public void Every_reference_collaborator_is_required()
    {
        var content = RunDecisionContent.Strings();

        Should.Throw<ArgumentNullException>(() =>
            new CampfirePresenter(null!, RunDecisionContent.Catalogue(content), content, Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new CampfirePresenter(
                RecordingGameHost.FindingNoSuchPlayer(), null!, content, Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new CampfirePresenter(
                RecordingGameHost.FindingNoSuchPlayer(),
                RunDecisionContent.Catalogue(content),
                null!,
                Player,
                Run));
    }

    // ---- fixture -----------------------------------------------------------------------------------------

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    private static RunSnapshot AtACampfire() =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 5, currentHp: 40,
            pendingTileKind: CampfirePresenter.CampfireTileKind,
            pendingTileLinearIndex: 5,
            pendingTileStage: 1);

    private static RunSnapshot AtAShrine() =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 5, currentHp: 40,
            pendingTileKind: CampfirePresenter.ShrineTileKind,
            pendingTileLinearIndex: 5,
            pendingTileStage: 1);

    /// <summary>Either arm's run, for the cases whose claim holds on both.</summary>
    private static RunSnapshot OnTile(int tileKind) =>
        tileKind == CampfirePresenter.ShrineTileKind ? AtAShrine() : AtACampfire();

    private static CampfirePresenter Build(RecordingGameHost host, ContentSnapshot? content = null)
    {
        var strings = RunDecisionContent.Strings();

        return new CampfirePresenter(
            host, RunDecisionContent.Catalogue(strings), content ?? strings, Player, Run);
    }
}
