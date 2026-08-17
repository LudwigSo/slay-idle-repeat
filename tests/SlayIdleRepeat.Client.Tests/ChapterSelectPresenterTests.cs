using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Chapter Select screen's own seam: which chapters exist, which (chapter, tier) pairs a player
/// may play, and — the part that matters most — the four different reasons a pair may be refused.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A greyed-out tier looks identical whether the chapter is unwritten, whether a clear is missing
/// and which clear it is, or whether the Legend Level is short. Every refusal below is therefore
/// asserted by its <em>payload</em> — the (chapter, tier) pair it demands, or the level it demands
/// and the level the player has — rather than by a name. Three enum members carrying no payload
/// could be swapped by a wrong-branch bug and every "is it blocked?" assertion would still pass.
/// </para>
/// <para>
/// ⚠️ The gate is presentation only. The rules layer refuses a <c>START_RUN</c> for a chapter id
/// below one or an undefined tier and for nothing else; no clear ladder and no Legend Level is
/// enforced anywhere behind this screen, and no task currently owns making one so.
/// </para>
/// </remarks>
public sealed class ChapterSelectPresenterTests
{
    private static readonly PlayerId Profile = new("PLAYER_select_71a0");

    /// <summary>The chapters the fixture content authors, and the shipped content set too.</summary>
    private static readonly int[] AuthoredChapters = [1, 2];

    /// <summary>A chapter no document in this repository describes. Its data row is a later task's.</summary>
    private const int UnauthoredChapter = 3;

    private const string ReadFailureMessage = "the profile row is present and will not decode";

    /// <summary>
    /// What the shipped ladder demands of Mythic. Stated as a literal because the point of the case
    /// that uses it is that this number lives in <c>tuning/progression.json</c> rather than in code;
    /// reading it out of the content set to compare against itself would assert nothing.
    /// </summary>
    private const int ShippedMythicLegendLevel = 60;

    // ------------------------------------------------------------------- the chapter list

    /// <summary>
    /// 🔒 Ascending id, over a fixture whose document paths run the other way, so a presenter
    /// listing chapters in the order the content set hands them over produces a descending list.
    /// </summary>
    [Fact]
    public void Chapters_lists_the_authored_chapters_in_ascending_id_order()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.Chapters.Select(c => c.ChapterId).ShouldBe(
            AuthoredChapters,
            "the picker is a ladder and the player reads it top to bottom. Whatever order the loaded " +
            "documents happen to arrive in is a filesystem fact, not a campaign order.");
    }

    [Fact]
    public void Chapters_carries_each_chapters_display_name_resolved_out_of_the_locale()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.Chapters.Select(c => c.DisplayName).ShouldBe(
            AuthoredChapters.Select(id => ScreenContent.EnglishValueOf(ScreenContent.ChapterNameKey(id))),
            "the chapter document carries a loc KEY, so a presenter that passed it through would put " +
            "'loc.chapter.1.name' in the picker. Held against fixture values no literal would ever " +
            "be, so the only way to satisfy this is to go through the catalogue.");
    }

    /// <summary>
    /// 🔒 Against the checkout's own content: only two chapters are authored today, and the other six
    /// the schema allows are a later task's data rows rather than an oversight.
    /// </summary>
    [Fact]
    public void Chapters_over_the_checkouts_own_content_is_exactly_the_two_chapters_authored_today()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), BootContent.Shipped);

        presenter.Chapters.Select(c => c.ChapterId).ShouldBe(
            AuthoredChapters,
            "every other case here runs against an in-memory fixture and would keep passing while the " +
            "picker drew nothing at all on a handset. A presenter that invented rows up to the " +
            "schema's cap of eight would offer six chapters with no board, no boss and no loot table.");
    }

    // -------------------------------------------- 🔒 the four refusals, told apart by payload

    [Fact]
    public async Task Availability_reports_NotAuthored_rather_than_Blocked_for_a_chapter_no_document_describes()
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.Availability(UnauthoredChapter, DifficultyTier.NORMAL).Lookup.ShouldBe(
            ChapterTierLookup.NotAuthored,
            "a chapter nobody has written is not a chapter the player can work towards. Reported as " +
            "locked it becomes a goal with no completion condition, and the day its data lands the " +
            "screen cannot tell the new chapter from the six that still do not exist.");
    }

    [Fact]
    public async Task Availability_blocks_NORMAL_on_the_PREVIOUS_chapters_NORMAL_clear()
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.NORMAL).Unmet.ShouldBe(
            new ChapterTierRequirement[] { new ClearRequirement(1, DifficultyTier.NORMAL) },
            "the ladder says PREVIOUS_CHAPTER_NORMAL, so chapter 2 waits on chapter ONE's Normal " +
            "clear. A refusal that named chapter 2 would send the player to grind the chapter they " +
            "are already looking at.");
    }

    [Fact]
    public async Task Availability_blocks_HEROIC_on_the_SAME_chapters_NORMAL_clear()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL)))),
            Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.HEROIC).Unmet.ShouldBe(
            new ChapterTierRequirement[] { new ClearRequirement(2, DifficultyTier.NORMAL) },
            "the ladder says SAME_CHAPTER_NORMAL, so Heroic waits on THIS chapter's Normal clear — " +
            "a different chapter from the one Normal itself waits on. Two rungs that both resolve to " +
            "'a Normal clear' are only told apart by which chapter they name.");
    }

    [Fact]
    public async Task Availability_blocks_MYTHIC_on_the_SAME_chapters_HEROIC_clear()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(
                legendLevel: ShippedMythicLegendLevel,
                cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL), (2, DifficultyTier.NORMAL)))),
            Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.MYTHIC).Unmet.ShouldBe(
            new ChapterTierRequirement[] { new ClearRequirement(2, DifficultyTier.HEROIC) },
            "the ladder says SAME_CHAPTER_HEROIC, and the level is already met here so the clear is " +
            "the only thing left. A Normal clear standing in for a Heroic one would open Mythic to a " +
            "player who has never played the tier below it.");
    }

    /// <summary>
    /// 🔒 The case that fails if the three rungs are ever wired to the wrong branch. Compared
    /// against each other rather than against literals: three rungs resolving to one pair would
    /// satisfy every literal assertion above by being that pair.
    /// </summary>
    [Fact]
    public async Task The_three_clear_rungs_of_one_chapter_demand_three_different_chapter_tier_pairs()
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        var demanded = new[] { DifficultyTier.NORMAL, DifficultyTier.HEROIC, DifficultyTier.MYTHIC }
            .Select(tier => presenter.Availability(2, tier).Unmet.OfType<ClearRequirement>().Single())
            .ToArray();

        demanded.Distinct().Count().ShouldBe(
            3,
            "three rungs, three demands. Any two that collapse make a whole tier reachable by " +
            "clearing something else, and the pair most likely to collapse is the previous chapter's " +
            "Normal with this chapter's — one letter apart in the authored data and one branch apart " +
            "in the code.");
    }

    [Fact]
    public async Task Availability_blocks_MYTHIC_on_Legend_Level_carrying_the_level_required_and_the_level_reached()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(
                legendLevel: ShippedMythicLegendLevel - 1,
                cleared: PlayerState.Cleared(
                    (1, DifficultyTier.NORMAL), (2, DifficultyTier.NORMAL), (2, DifficultyTier.HEROIC)))),
            Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.MYTHIC).Unmet.ShouldBe(
            new ChapterTierRequirement[]
            {
                new LegendLevelRequirement(ShippedMythicLegendLevel, ShippedMythicLegendLevel - 1),
            },
            "'your Legend Level is too low' is not actionable; 'level 60, you are 59' is. Both " +
            "numbers, because a refusal carrying only the requirement cannot tell the player whether " +
            "they are one level away or fifty.");
    }

    /// <summary>
    /// 🔒 Both blocks at once. A first-match refusal passes every case above and hides half the truth.
    /// </summary>
    [Fact]
    public async Task Availability_reports_the_missing_clear_AND_the_missing_Legend_Level_on_MYTHIC()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(legendLevel: 1)), Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.MYTHIC).Unmet.ShouldBe(
            new ChapterTierRequirement[]
            {
                new ClearRequirement(2, DifficultyTier.HEROIC),
                new LegendLevelRequirement(ShippedMythicLegendLevel, 1),
            },
            ignoreOrder: true,
            customMessage:
            "a fresh player is short of both, and a refusal that stopped at the first one sends them " +
            "away to do half the work and come back to the same locked button. The list is the point: " +
            "the requirements are independent, so the answer is every one that is unmet.");
    }

    [Fact]
    public async Task Availability_reports_Selectable_for_the_first_chapter_on_NORMAL_which_has_no_previous_chapter()
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.Availability(1, DifficultyTier.NORMAL).Lookup.ShouldBe(
            ChapterTierLookup.Selectable,
            "chapter one's Normal is where a brand-new account starts, and the rung above it names a " +
            "chapter that does not exist. A presenter that demanded chapter zero's clear would ship " +
            "a game with nothing at all to play.");
    }

    [Fact]
    public async Task Availability_reports_Selectable_once_every_authored_requirement_is_met()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(
                legendLevel: ShippedMythicLegendLevel,
                cleared: PlayerState.Cleared(
                    (1, DifficultyTier.NORMAL), (2, DifficultyTier.NORMAL), (2, DifficultyTier.HEROIC)))),
            Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.MYTHIC).Lookup.ShouldBe(
            ChapterTierLookup.Selectable,
            "the positive control the refusals above are worthless without: a gate that refused " +
            "everything would satisfy every one of them and lock the whole campaign.");
    }

    // -------------------------------------------------------- 🔒 the ladder is authored data

    /// <summary>
    /// 🔒 The case that makes "data, not code" bite. Two content sets differing in one authored
    /// number, one player, two answers.
    /// </summary>
    [Fact]
    public async Task Availability_follows_the_authored_Legend_Level_rather_than_a_number_compiled_into_the_client()
    {
        var player = PlayerRow(
            legendLevel: 10,
            cleared: PlayerState.Cleared(
                (1, DifficultyTier.NORMAL), (2, DifficultyTier.NORMAL), (2, DifficultyTier.HEROIC)));

        var againstSixty = await Started(
            RecordingGameHost.Finding(player), Authoring(AuthoredChapters, mythicLegendLevel: 60));
        var againstFive = await Started(
            RecordingGameHost.Finding(player), Authoring(AuthoredChapters, mythicLegendLevel: 5));

        againstSixty.Availability(2, DifficultyTier.MYTHIC).Lookup.ShouldBe(
            ChapterTierLookup.Blocked,
            "level 10 is short of the 60 this content set authors");
        againstFive.Availability(2, DifficultyTier.MYTHIC).Lookup.ShouldBe(
            ChapterTierLookup.Selectable,
            "and clears the 5 this one authors — the same player, the same code, one number moved in " +
            "tuning data. A presenter holding its own copy of 60 answers Blocked both times and " +
            "makes the authored ladder a comment. Balance is a data edit, and a data edit that " +
            "changes nothing is a balance lever nobody has.");
    }

    /// <summary>
    /// 🔒 The other shape of the same rule: a rung that authors <em>no</em> level demands none. The
    /// case above varies the number; this one removes it, which is the edit a presenter carrying its
    /// own copy of 60 would ignore just as completely while still answering Blocked.
    /// </summary>
    [Fact]
    public async Task Availability_demands_no_Legend_Level_when_the_authored_rung_names_none()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(
                legendLevel: 1,
                cleared: PlayerState.Cleared(
                    (1, DifficultyTier.NORMAL), (2, DifficultyTier.NORMAL), (2, DifficultyTier.HEROIC)))),
            Authoring(AuthoredChapters, mythicLegendLevel: null));

        presenter.Availability(2, DifficultyTier.MYTHIC).Unmet.ShouldBeEmpty(
            "the ladder authors requiresLegendLevel as nullable and Normal and Heroic ship it null " +
            "today, so 'no level demanded' is a shape the data really takes. A presenter that read " +
            "an unauthored hole as a level would demand zero — or, worse, treat Mythic as always " +
            "gated — and no amount of tuning could unlock it.");
    }

    /// <summary>
    /// 🔒 And the number the checkout actually authors, held against reality rather than a fixture.
    /// </summary>
    [Fact]
    public async Task Availability_over_the_checkouts_own_content_holds_a_level_59_player_out_of_MYTHIC()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(
                legendLevel: ShippedMythicLegendLevel - 1,
                cleared: PlayerState.Cleared(
                    (1, DifficultyTier.NORMAL), (2, DifficultyTier.NORMAL), (2, DifficultyTier.HEROIC)))),
            BootContent.Shipped);

        presenter.Availability(2, DifficultyTier.MYTHIC).Unmet.ShouldBe(
            new ChapterTierRequirement[]
            {
                new LegendLevelRequirement(ShippedMythicLegendLevel, ShippedMythicLegendLevel - 1),
            },
            "the fixture cases prove the number is read; this one proves the number that gets read is " +
            "the one tuning/progression.json actually carries. A ladder read out of a document the " +
            "shipped tree does not have would pass every fixture case and let a level-1 player into " +
            "Mythic on a handset.");
    }

    // -------------------------------------------------------------------- the clear history

    /// <summary>
    /// 🔒 The key shape is a format another assembly owns: <c>Player.ChapterTierKey</c> is
    /// <c>internal</c> and unreachable from here, so this is a transcription. The literal is written
    /// out in full rather than composed by the fixture helper, so a drift in that format fails here
    /// with the shape it expected rather than agreeing with a helper that drifted alongside it.
    /// </summary>
    [Fact]
    public async Task Availability_reads_a_clear_recorded_under_the_chapter_colon_TIER_key_the_rules_layer_writes()
    {
        var cleared = new Dictionary<string, long>(StringComparer.Ordinal) { ["2:NORMAL"] = 1 };
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: cleared)), Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.HEROIC).Lookup.ShouldBe(
            ChapterTierLookup.Selectable,
            "'2:NORMAL' is the exact key the rules layer stores a cleared (chapter, tier) pair under: " +
            "the chapter number, a colon, and the tier's NAME. A client that re-formed it with the " +
            "tier's numeric value, a dash, or padding would find nothing in a history full of clears " +
            "and lock a player out of everything they have already beaten.");
    }

    [Fact]
    public async Task Availability_does_not_read_a_clear_recorded_under_the_tiers_numeric_value()
    {
        var cleared = new Dictionary<string, long>(StringComparer.Ordinal) { ["2:1"] = 1 };
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: cleared)), Authoring(AuthoredChapters));

        presenter.Availability(2, DifficultyTier.HEROIC).Lookup.ShouldBe(
            ChapterTierLookup.Blocked,
            "the negative half of the case above, and the one that stops it passing on a lookup that " +
            "matches on the chapter alone. NORMAL's numeric value is 1, so a client keying on the " +
            "number would read this row as the clear it is not.");
    }

    /// <summary>
    /// The row documents <c>null</c> as "nothing cleared yet" — not as a fault, unlike its
    /// neighbours on the same snapshot. Both spellings of nothing must therefore behave the same.
    /// </summary>
    [Fact]
    public async Task A_null_clear_history_and_an_empty_one_produce_the_same_availability()
    {
        var overNull = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: null)), Authoring(AuthoredChapters));
        var overEmpty = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: PlayerState.NothingCleared())),
            Authoring(AuthoredChapters));

        var fromNull = overNull.Availability(2, DifficultyTier.NORMAL);
        var fromEmpty = overEmpty.Availability(2, DifficultyTier.NORMAL);

        fromEmpty.Unmet.ShouldBe(
            new ChapterTierRequirement[] { new ClearRequirement(1, DifficultyTier.NORMAL) },
            "the answer itself, pinned before the two are held against each other: a reader that " +
            "answered 'selectable, nothing unmet' for both spellings of nothing would agree with " +
            "itself perfectly and open the whole campaign to a brand-new account.");
        fromNull.Lookup.ShouldBe(
            fromEmpty.Lookup,
            "a player who has cleared nothing and a player whose map was written empty are the same " +
            "player. Compared member by member rather than whole, because these carry a list and a " +
            "record's synthesized equality would compare that by reference and agree with itself " +
            "whatever the contents were.");
        fromNull.Unmet.ShouldBe(
            fromEmpty.Unmet,
            "and they must be refused for the same reasons, not merely refused. A reader that treated " +
            "null as a fault would answer something different here while both still read as blocked.");
    }

    // ------------------------------------------------------------------------- the read

    [Fact]
    public void Stage_is_NotYetRead_before_StartAsync_is_called()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.Stage.ShouldBe(
            ChapterSelectStage.NotYetRead,
            "the picker draws before the read answers, and a stage that started at Ready would let a " +
            "confirm through against a Legend Level and a clear history nobody has fetched yet.");
    }

    /// <summary>
    /// 🔒 The picker's first frame is drawn before the read answers, so every tier on every row is
    /// asked about while the Legend Level and the clear history it is decided against are still
    /// unknown. Chapter one on Normal is the pair a permissive presenter opens first, and it is
    /// genuinely selectable once the read lands — so it is the pair that tells a real answer apart
    /// from a hopeful one.
    /// </summary>
    /// <remarks>
    /// Named rather than merely non-selectable: <c>Blocked</c> here would be a refusal carrying no
    /// requirement, which is the one shape this screen's refusals must never take — "the chapter is
    /// disabled" reads identically to every other cause and sends the player nowhere. Which of the
    /// three unread states it is stays legible through <c>Stage</c>.
    /// </remarks>
    [Fact]
    public async Task Availability_is_NotYetKnown_before_StartAsync_has_fetched_the_state_it_is_decided_against()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        var beforeTheRead = presenter.Availability(1, DifficultyTier.NORMAL);

        beforeTheRead.Lookup.ShouldBe(
            ChapterTierLookup.NotYetKnown,
            "answering Selectable against a Legend Level and a clear history nobody has fetched " +
            "opens every tier for exactly as long as the read takes, and a tap landing in that " +
            "window reaches a confirm that refuses for a reason the screen never showed.");
        beforeTheRead.Unmet.ShouldBeEmpty(
            "nothing is unmet, because nothing is known. A requirement invented here would be a " +
            "demand made of a player whose progress has not been looked at.");

        presenter.Availability(UnauthoredChapter, DifficultyTier.NORMAL).Lookup.ShouldBe(
            ChapterTierLookup.NotAuthored,
            "and the unread state does not swallow the one cause that never depended on the read: " +
            "whether anybody wrote the chapter is a fact about the content set alone.");

        await presenter.StartAsync(CancellationToken.None);

        presenter.Availability(1, DifficultyTier.NORMAL).Lookup.ShouldBe(
            ChapterTierLookup.Selectable,
            "the read is what changes the answer. A presenter that reported NotYetKnown forever " +
            "would satisfy the assertions above and lock the campaign shut.");
    }

    [Fact]
    public async Task StartAsync_reports_ProfileMissing_rather_than_Ready_when_no_such_player_is_stored()
    {
        var presenter = await Started(RecordingGameHost.FindingNoSuchPlayer(), Authoring(AuthoredChapters));

        presenter.Stage.ShouldBe(
            ChapterSelectStage.ProfileMissing,
            "a run started for a player with no row is a run written against nobody. Named, so it can " +
            "never read as 'a player who has cleared nothing'.");
    }

    [Fact]
    public async Task StartAsync_reports_ReadUnavailable_rather_than_throwing_when_the_read_faults()
    {
        var presenter = await Started(
            RecordingGameHost.FaultingItsRead(new InvalidOperationException(ReadFailureMessage)),
            Authoring(AuthoredChapters));

        presenter.Stage.ShouldBe(
            ChapterSelectStage.ReadUnavailable,
            "a failure is a state, never an escape: an exception here goes out through an engine " +
            "callback where nothing catches it and the player is left on a frozen picker.");
    }

    // ----------------------------------------------------------------------- the confirm

    [Fact]
    public async Task ConfirmAsync_submits_START_RUN_for_the_chosen_chapter_and_tier()
    {
        var host = RecordingGameHost.Finding(
            PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))));
        var presenter = await Started(host, Authoring(AuthoredChapters));

        var submission = await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        submission.ShouldBe(
            ChapterSelectSubmission.Submitted,
            "the verdict a legal confirm reports, stated here because every other confirm case pins " +
            "a REFUSAL — a screen that answered RefusedNotSelectable while submitting anyway would " +
            "satisfy all of them, and the caller has no other way to know the tap took.");
        host.SubmitCommand.ShouldBe(
            new StartRunCommand(2, DifficultyTier.NORMAL),
            "the chapter and the tier are what the command exists to carry: they seed the run and " +
            "decide the board the player will spend the next twenty minutes on. A screen that " +
            "submitted the defaults, or the highlighted row rather than the confirmed one, starts " +
            "the wrong run and there is no undo.");
    }

    [Fact]
    public async Task ConfirmAsync_submits_on_the_player_rather_than_against_a_named_run()
    {
        var host = RecordingGameHost.Finding(
            PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))));
        var presenter = await Started(host, Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        host.SubmitPlayer.ShouldBe(Profile, "the run belongs to the profile this screen was built for");
        host.SubmitRun.ShouldBeNull(
            "there is no run yet — that is what START_RUN is for. Addressing it to a run id would " +
            "route it at something that does not exist, and on a real host that is a sequencing key " +
            "for a stream nothing has opened.");
    }

    [Fact]
    public async Task ConfirmAsync_hands_the_host_the_cancellation_token_it_was_given()
    {
        using var cancellation = new CancellationTokenSource();
        var host = RecordingGameHost.Finding(
            PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))));
        var presenter = await Started(host, Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, cancellation.Token);

        host.SubmitToken.ShouldBe(
            cancellation.Token,
            "the token is how the app says 'the screen is going away, stop'. A presenter that " +
            "swallows it and passes None leaves a START_RUN in flight against a screen that is " +
            "already gone, and this is the one call that writes.");
    }

    [Fact]
    public async Task ConfirmAsync_submits_nothing_for_a_blocked_selection()
    {
        var host = RecordingGameHost.Finding(PlayerRow());
        var presenter = await Started(host, Authoring(AuthoredChapters));

        var submission = await presenter.ConfirmAsync(2, DifficultyTier.MYTHIC, CancellationToken.None);

        submission.ShouldBe(
            ChapterSelectSubmission.RefusedNotSelectable,
            "the refusal is the screen's answer, not silence — a confirm that appears to do nothing " +
            "reads as a broken button.");
        host.SubmitCallCount.ShouldBe(
            0,
            "and nothing may reach the host. The rules layer checks only that the chapter id is at " +
            "least one and the tier is defined, so a blocked pair sent anyway would be ACCEPTED and " +
            "the whole ladder would be decoration.");
    }

    [Fact]
    public async Task ConfirmAsync_submits_nothing_for_a_chapter_the_content_set_does_not_author()
    {
        var host = RecordingGameHost.Finding(PlayerRow());
        var presenter = await Started(host, Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(UnauthoredChapter, DifficultyTier.NORMAL, CancellationToken.None);

        host.SubmitCallCount.ShouldBe(
            0,
            "a run on a chapter with no document has no board, no boss and no loot table. The rules " +
            "layer would accept it — its only chapter check is 'at least one' — and the run would " +
            "fail at whatever first tried to read the chapter it names.");
    }

    [Fact]
    public async Task ConfirmAsync_submits_nothing_when_the_profile_could_not_be_read()
    {
        var host = RecordingGameHost.FindingNoSuchPlayer();
        var presenter = await Started(host, Authoring(AuthoredChapters));

        var submission = await presenter.ConfirmAsync(1, DifficultyTier.NORMAL, CancellationToken.None);

        submission.ShouldBe(
            ChapterSelectSubmission.RefusedProfileUnavailable,
            "told apart from a blocked pair by name, because they are fixed by different things: one " +
            "by playing, the other by getting a profile back. Chapter one on Normal is selectable " +
            "against an empty history, so a screen that only checked the ladder would start a run " +
            "for a player with no row.");
        host.SubmitCallCount.ShouldBe(0, "and nothing reaches the host either way");
    }

    // ------------------------------------------- 🔒 the refusal this screen does not decide

    /// <summary>
    /// 🔒 A submitted command is not an accepted one. The rules layer refuses a <c>START_RUN</c>
    /// while a run is already open, and a screen that read its own submission as the outcome would
    /// report a run that never started.
    /// </summary>
    /// <remarks>
    /// The pair here is selectable and the profile was read, so every check this screen makes has
    /// already passed: the only thing left that can say no is the layer behind it, and the whole
    /// question is whether its answer is looked at.
    /// </remarks>
    [Fact]
    public async Task ConfirmAsync_reports_RefusedByRules_when_the_rules_layer_refuses_the_command()
    {
        var host = RecordingGameHost
            .Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))))
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);
        var presenter = await Started(host, Authoring(AuthoredChapters));

        var submission = await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        submission.ShouldBe(
            ChapterSelectSubmission.RefusedByRules,
            "the command really was sent and really was refused, so neither refusal this screen " +
            "decides for itself describes it. Reported as Submitted — which is what discarding the " +
            "outcome amounts to — the caller latches its confirm on a run that does not exist and " +
            "the player is left on a dead screen with nothing said.");
        host.SubmitCallCount.ShouldBe(
            1,
            "and it is told apart from the two refusals above by having reached the host at all: " +
            "those two are decided before anything is sent.");
    }

    /// <summary>
    /// 🔒 The reason travels, rather than collapsing into one opaque "it failed". Two reasons in,
    /// two reasons out — a presenter that kept only a bool satisfies the case above and loses the
    /// difference between a player who is already mid-run and a build sending an impossible tier.
    /// </summary>
    [Theory]
    [InlineData(RejectionReason.ILLEGAL_STATE)]
    [InlineData(RejectionReason.RATE_LIMITED)]
    public async Task ConfirmAsync_carries_the_rules_layers_own_reason_for_the_refusal(
        RejectionReason rejection)
    {
        var host = RecordingGameHost
            .Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))))
            .RefusingCommands(rejection);
        var presenter = await Started(host, Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        presenter.RulesRejection.ShouldBe(
            rejection,
            $"{rejection} is the whole content of the refusal, and it is the only thing that could " +
            "tell the player what to do next. Stated per reason rather than as 'not null', because " +
            "a presenter that stored the first refusal it ever saw, or a constant, would pass a " +
            "single case and report the wrong cause for every later one.");
    }

    [Fact]
    public async Task ConfirmAsync_carries_no_rejection_when_the_rules_layer_accepts_the_command()
    {
        var host = RecordingGameHost.Finding(
            PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))));
        var presenter = await Started(host, Authoring(AuthoredChapters));

        var submission = await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        submission.ShouldBe(
            ChapterSelectSubmission.Submitted,
            "the positive control the case above is worthless without: a presenter that answered " +
            "RefusedByRules for everything would satisfy it and never start a run at all.");
        presenter.RulesRejection.ShouldBeNull(
            "an accepted command was refused for no reason, and a reason left standing from nowhere " +
            "would put a cause on a screen that succeeded.");
    }

    // ------------------------------------------- 🔒 the line the confirm's own press cannot say

    /// <summary>
    /// 🔒 The confirm is disabled on the press and comes back on every outcome but one, so the three
    /// outcomes are one flicker of one button. These cases pin that each produces its own sentence —
    /// and, above all, that the refusal produces one at all: it reached only the engine log before,
    /// which is a primary action failing in silence on the screen every run starts from.
    /// </summary>
    [Fact]
    public async Task ConfirmStatusText_says_nothing_before_a_confirm_has_been_answered()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.ConfirmStatusText.ShouldBeEmpty(
            "a screen nobody has confirmed on yet has nothing to report about a confirm. A line " +
            "standing there from the start would be a verdict on a press that never happened.");
    }

    [Fact]
    public async Task ConfirmStatusText_says_the_run_started_once_the_rules_layer_accepted_it()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL)))),
            Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        presenter.ConfirmStatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerStartedStatusKey),
            "this is the one outcome that latches the control for good, so the screen never changes " +
            "again — and with nothing said, the tap that actually worked is the one that looks most " +
            "like the button breaking.");
    }

    [Fact]
    public async Task ConfirmStatusText_says_the_run_was_not_started_when_the_rules_layer_refused_it()
    {
        var presenter = await Started(
            RecordingGameHost
                .Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))))
                .RefusingCommands(RejectionReason.ILLEGAL_STATE),
            Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        presenter.ConfirmStatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerRefusedStatusKey),
            "the refusal the player can act on, and the one that used to reach the log and nothing " +
            "else: the control came back live and the screen looked exactly as it had before the " +
            "press, so the only reading available was that the tap had not registered.");
    }

    /// <summary>
    /// The refusals this screen decides for itself are unreachable from a live control, and they
    /// still may not be silent: a verdict with no sentence is the hole the catch-all closes.
    /// </summary>
    [Fact]
    public async Task ConfirmStatusText_reports_a_refusal_this_screen_decided_for_itself_too()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        await presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        presenter.ConfirmStatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerRefusedStatusKey),
            "chapter two is blocked, so nothing was sent — and a verdict the switch has not been " +
            "taught must land on the refusal line rather than on the empty string, or a confirm " +
            "that did nothing is drawn as a confirm that has not happened.");
    }

    /// <summary>
    /// 🔒 The path with no verdict at all. A host that faults rather than refusing takes the failure
    /// out through the caller's catch, so nothing ever returns a <see cref="ChapterSelectSubmission"/>
    /// — and a line driven off the returned verdict would leave the single press a player can make on
    /// this screen failing in complete silence, which is the state it is worst to leave them in.
    /// </summary>
    [Fact]
    public async Task ConfirmStatusText_says_the_run_was_not_started_when_the_host_faulted_instead_of_answering()
    {
        var presenter = await Started(
            RecordingGameHost
                .Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))))
                .FaultingItsCommands(new InvalidOperationException(ReadFailureMessage)),
            Authoring(AuthoredChapters));

        await Should.ThrowAsync<InvalidOperationException>(
            () => presenter.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None));

        presenter.ConfirmStatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerRefusedStatusKey),
            "no run started, so the one true sentence is the same one a refusal gets. The failure " +
            "itself still travels to the caller, which is where an untranslated type name belongs — " +
            "what may not happen is the screen going quiet because nothing came back to read.");
    }

    /// <summary>
    /// 🔒 The case that fails if success and failure are ever wired to one key, or if the in-flight
    /// line is the same words as either of them.
    /// </summary>
    [Fact]
    public async Task The_three_things_a_confirm_can_say_stay_three_different_lines()
    {
        var accepted = await Started(
            RecordingGameHost.Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL)))),
            Authoring(AuthoredChapters));
        var refused = await Started(
            RecordingGameHost
                .Finding(PlayerRow(cleared: PlayerState.Cleared((1, DifficultyTier.NORMAL))))
                .RefusingCommands(RejectionReason.ILLEGAL_STATE),
            Authoring(AuthoredChapters));

        await accepted.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);
        await refused.ConfirmAsync(2, DifficultyTier.NORMAL, CancellationToken.None);

        new[] { accepted.StartingStatus, accepted.ConfirmStatusText, refused.ConfirmStatusText }
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(
                3,
                "still working, worked, and did not work are three different things to do next — " +
                "wait, stop, and press again. Any two of them sharing a sentence puts one instruction " +
                "on two states, and the pair most likely to collapse is the two that both end with a " +
                "screen that has stopped moving.");
        accepted.StartingStatus.ShouldNotBeEmpty(
            "and the in-flight line is the one with no state behind it to notice if it went blank: " +
            "the screen resolves it on the press and would draw an empty label without complaint.");
    }

    // ------------------------------------------------------ 🔒 the absence, pinned as a fact

    /// <summary>
    /// 🔒 No power comparison, pinned so that adding one means deleting a case that says why not.
    /// </summary>
    /// <remarks>
    /// The expected-power curve belongs to the row that owns the power model. A comparison invented
    /// on this screen would be a second answer to a question that already has one owner, and the two
    /// would disagree the first time either moved. A substring match, because
    /// <c>ParPower</c>, <c>ExpectedPower</c> and <c>PowerWarning</c> are the same mistake three ways.
    /// </remarks>
    [Theory]
    [InlineData("Power")]
    [InlineData("Expected")]
    [InlineData("Warning")]
    public void The_chapter_select_screen_exposes_no_member_comparing_the_players_power_against_a_chapter(
        string fragment)
    {
        ChapterSelectMemberNames()
            .Where(n => n.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty(
                $"a member naming '{fragment}' would be the soft power warning, and this screen does " +
                "not own it. If a later task brings the power model within reach, deleting this case " +
                "is the deliberate act that records the change.");
    }

    /// <summary>The floor under the rule above, by named member rather than by count.</summary>
    [Fact]
    public void The_absence_rule_is_stated_over_the_members_the_chapter_select_screen_actually_exposes()
    {
        var members = ChapterSelectMemberNames();

        members.ShouldContain(nameof(ChapterSelectPresenter.Chapters), "the list the absent warning would have sat beside");
        members.ShouldContain(nameof(ChapterSelectPresenter.Availability), "the answer the absent warning must not become part of");
        members.ShouldContain(nameof(ChapterSelectPresenter.ConfirmAsync), "the control the absent warning would have gated");
        members.ShouldContain(nameof(ChapterSelectPresenter.RulesRejection), "the one refusal on this screen that comes from behind it");
        members.ShouldContain(nameof(ChapterSelectPresenter.StatusText), "the one line the screen says about itself, which a power warning must not be smuggled into");
        members.ShouldContain(nameof(ChapterSelectPresenter.StartingStatus), "the line drawn while the confirm is in flight, which a power warning must not be smuggled into either");
        members.ShouldContain(nameof(ChapterSelectPresenter.ConfirmStatusText), "the line drawn after it, and the obvious place to bolt a 'your power is low' onto");
    }

    // ------------------------------------------------------------------------- the strings

    [Fact]
    public void Every_caption_the_chapter_select_screen_renders_comes_from_the_catalogue_rather_than_a_literal()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        new[]
        {
            presenter.Title, presenter.ConfirmText,
            presenter.RequiresClearCaption, presenter.RequiresLegendLevelCaption,
        }.ShouldBe(
            new[]
            {
                ScreenContent.EnglishValueOf(ScreenContent.ChapterSelectTitleKey),
                ScreenContent.EnglishValueOf(ScreenContent.ConfirmActionKey),
                ScreenContent.EnglishValueOf(ScreenContent.RequiresClearBlockKey),
                ScreenContent.EnglishValueOf(ScreenContent.RequiresLegendLevelBlockKey),
            },
            "an English literal renders identically to a resolved English string on every English " +
            "handset and ships a German build in English. Held against fixture values no reasonable " +
            "literal would ever be.");
    }

    [Theory]
    [InlineData(DifficultyTier.NORMAL, ScreenContent.TierNormalKey)]
    [InlineData(DifficultyTier.HEROIC, ScreenContent.TierHeroicKey)]
    [InlineData(DifficultyTier.MYTHIC, ScreenContent.TierMythicKey)]
    public void TierName_resolves_each_tier_through_its_own_key(DifficultyTier tier, string key)
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.TierName(tier).ShouldBe(
            ScreenContent.EnglishValueOf(key),
            $"{tier} is the tier the player is choosing, and the three names are what the picker is. " +
            "Stated per tier rather than over the set, because two keys swapped leaves every caption " +
            "resolved, every string non-blank, and a Mythic button labelled Normal.");
    }

    // ------------------------------------------------- 🔒 the line the dimmed list cannot say

    /// <summary>
    /// 🔒 Every stage short of <see cref="ChapterSelectStage.Ready"/> draws the same dimmed,
    /// non-interactive list, so opacity carries no identity at all: the four cases below pin which
    /// SENTENCE each stage produces, and the case after them pins that the four stay apart.
    /// Asserted against the catalogue's value for a named key rather than against "not empty",
    /// because one sentence shown for all three failures is exactly the defect these exist to catch.
    /// </summary>
    [Fact]
    public void StatusText_before_the_read_says_the_screen_is_still_finding_out()
    {
        var presenter = Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.StatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerLoadingStatusKey),
            "the frame before the read answers dims the whole list and disables every row, and with " +
            "no words that is indistinguishable from a campaign the player has not unlocked. This " +
            "one ends by itself, and saying so is the difference between waiting and giving up.");
    }

    [Fact]
    public async Task StatusText_is_cleared_once_the_read_lands_and_the_list_itself_is_the_answer()
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters));

        presenter.StatusText.ShouldBeEmpty(
            "the rows are live and each refusal carries its own requirement line, so there is " +
            "nothing left for a status line to add. A loading line left up over a working list is a " +
            "screen still talking about itself while the player is already reading it.");
    }

    [Fact]
    public async Task StatusText_names_a_missing_profile_rather_than_repeating_the_loading_line()
    {
        var presenter = await Started(RecordingGameHost.FindingNoSuchPlayer(), Authoring(AuthoredChapters));

        presenter.StatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerProfileMissingStatusKey),
            "this one never ends. Left saying the screen is still reading, the player waits out a " +
            "dead end for something that is never coming, and nothing on the screen ever admits it.");
    }

    [Fact]
    public async Task StatusText_names_a_read_that_did_not_answer_rather_than_a_missing_profile()
    {
        var presenter = await Started(
            RecordingGameHost.FaultingItsRead(new InvalidOperationException(ReadFailureMessage)),
            Authoring(AuthoredChapters));

        presenter.StatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.PickerUnavailableStatusKey),
            "a profile that is not there and a profile that would not be read are fixed by different " +
            "things — one by getting an account back, one by trying again later — and a single " +
            "sentence for both tells the player to do neither.");
    }

    /// <summary>
    /// 🔒 The case that fails if the three failing stages are ever wired to one key. Compared against
    /// each other rather than against literals: three stages resolving to one line would satisfy a
    /// "the line is not blank" assertion on every one of them.
    /// </summary>
    [Fact]
    public async Task The_four_stages_of_the_picker_produce_four_different_status_lines()
    {
        var lines = new[]
        {
            Screen(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters)),
            await Started(RecordingGameHost.Finding(PlayerRow()), Authoring(AuthoredChapters)),
            await Started(RecordingGameHost.FindingNoSuchPlayer(), Authoring(AuthoredChapters)),
            await Started(
                RecordingGameHost.FaultingItsRead(new InvalidOperationException(ReadFailureMessage)),
                Authoring(AuthoredChapters)),
        }.Select(presenter => presenter.StatusText).ToArray();

        lines.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            4,
            "four stages, four outcomes. Any two that collapse put one sentence on two states that " +
            "are escaped in different ways, and the pair most likely to collapse is the two dead " +
            "ends — they are the two the screen can do least about and the two a player can do most.");
        lines.Count(line => line.Length == 0).ShouldBe(
            1,
            "and exactly one of the four is silent. A screen that said nothing in two of them would " +
            "still have four distinct entries here while leaving a dead end wordless.");
    }

    // ------------------------------------------------------------------------- null guards

    [Fact]
    public void Constructor_rejects_a_null_content_snapshot()
    {
        Should.Throw<ArgumentNullException>(() => new ChapterSelectPresenter(
                  RecordingGameHost.Finding(PlayerRow()),
                  ScreenContent.Catalogue(),
                  content: null!,
                  Profile))
              .ParamName.ShouldBe(
                  "content",
                  "the chapters and the whole gating ladder are read from it. A null one fails at " +
                  "whichever lookup happens first, which on this screen is inside a refusal — so the " +
                  "player would be told a chapter is locked when the graph was never wired.");
    }

    [Fact]
    public void Constructor_rejects_a_null_game_host()
    {
        Should.Throw<ArgumentNullException>(() => new ChapterSelectPresenter(
                  gameHost: null!, ScreenContent.Catalogue(), Authoring(AuthoredChapters), Profile))
              .ParamName.ShouldBe(
                  "gameHost",
                  "it is both where the player's state comes from and where the run is started. A " +
                  "null one surfaces on the confirm, which is the one tap a player must never lose.");
    }

    // ---------------------------------------------------------------------------- fixtures

    private static ContentSnapshot Authoring(IReadOnlyList<int> chapterIds, int? mythicLegendLevel = 60) =>
        ScreenContent.Authoring(chapterIds, mythicLegendLevel);

    private static PlayerSnapshot PlayerRow(
        int legendLevel = 1, IReadOnlyDictionary<string, long>? cleared = null) =>
        PlayerState.Player(Profile, legendLevel: legendLevel, clearedChapterTiers: cleared);

    private static ChapterSelectPresenter Screen(RecordingGameHost host, ContentSnapshot content) =>
        new(host, ScreenContent.Catalogue(content), content, Profile);

    private static async Task<ChapterSelectPresenter> Started(RecordingGameHost host, ContentSnapshot content)
    {
        var presenter = Screen(host, content);

        await presenter.StartAsync(CancellationToken.None);

        return presenter;
    }

    private static IReadOnlyList<string> ChapterSelectMemberNames() =>
        typeof(ChapterSelectPresenter)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();
}
