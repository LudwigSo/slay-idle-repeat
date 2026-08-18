using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>One chapter the content set authors, ready to draw.</summary>
/// <param name="ChapterId">The chapter number the chapter document declares.</param>
/// <param name="DisplayName">The chapter's name, already resolved out of the locale.</param>
public sealed record ChapterListing(int ChapterId, string DisplayName);

/// <summary>
/// One requirement a (chapter, tier) pair has not met.
/// </summary>
/// <remarks>
/// 🔒 A closed hierarchy rather than a reason enum, because the identity of a refusal is its
/// <em>payload</em>: "you must clear chapter 1 on Normal" and "you must clear chapter 2 on Normal"
/// are different instructions to the player, and three enum names carrying no chapter and no tier
/// could be swapped by a wrong-branch bug without any test noticing.
/// </remarks>
public abstract record ChapterTierRequirement;

/// <summary>A clear that has not happened yet.</summary>
/// <param name="ChapterId">The chapter that must be cleared.</param>
/// <param name="Tier">The tier it must be cleared on.</param>
public sealed record ClearRequirement(int ChapterId, DifficultyTier Tier) : ChapterTierRequirement;

/// <summary>A Legend Level that has not been reached.</summary>
/// <param name="Required">The level the authored ladder demands.</param>
/// <param name="Actual">The level the player is at.</param>
public sealed record LegendLevelRequirement(int Required, int Actual) : ChapterTierRequirement;

/// <summary>What a lookup for one (chapter, tier) pair found.</summary>
public enum ChapterTierLookup
{
    /// <summary>Every authored requirement is met; confirming it starts a run.</summary>
    Selectable = 1,

    /// <summary>The chapter exists and at least one requirement is unmet.</summary>
    Blocked = 2,

    /// <summary>
    /// The content set authors no such chapter. Distinct from <see cref="Blocked"/> on purpose: a
    /// chapter nobody has written yet is not a chapter the player can work towards.
    /// </summary>
    NotAuthored = 3,

    /// <summary>
    /// The chapter exists, but the state its requirements are decided against has not been read.
    /// </summary>
    /// <remarks>
    /// 🔒 The screen draws its first frame before the read answers, so every pair is asked about
    /// while the player's Legend Level and clear history are still unknown. Answering
    /// <see cref="Selectable"/> there would open every tier for exactly as long as the read takes,
    /// and a tap landing in that window reaches a confirm that refuses for a reason the screen never
    /// showed. Answering <see cref="Blocked"/> would be a refusal with an empty requirement list —
    /// "this is disabled", which is the one thing a refusal on this screen must never be. Which of
    /// the three unread states this is — not started, no such profile, or a read that did not
    /// answer — is carried by <see cref="ChapterSelectPresenter.Stage"/>.
    /// </remarks>
    NotYetKnown = 4,
}

/// <summary>What the screen may do with one (chapter, tier) pair, and why not when it may not.</summary>
/// <param name="Lookup">Selectable, blocked, or not authored at all.</param>
/// <param name="Unmet">
/// Every unmet requirement, not the first one found. Mythic can be blocked by a missing clear
/// <em>and</em> by Legend Level at the same time, and a screen that showed only one of them would
/// send the player to do half the work and come back to the same locked button.
/// </param>
public sealed record ChapterTierAvailability(
    ChapterTierLookup Lookup,
    IReadOnlyList<ChapterTierRequirement> Unmet);

/// <summary>What a confirmed selection did.</summary>
public enum ChapterSelectSubmission
{
    /// <summary><c>START_RUN</c> went to the host.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the pair is not selectable.</summary>
    RefusedNotSelectable = 2,

    /// <summary>Nothing was submitted, because there is no player state to start a run for.</summary>
    RefusedProfileUnavailable = 3,

    /// <summary>
    /// <c>START_RUN</c> went to the host and the rules layer refused it. Which refusal it was is
    /// carried by <see cref="ChapterSelectPresenter.RulesRejection"/>.
    /// </summary>
    /// <remarks>
    /// Told apart from the two refusals above because it is the only one this screen did not decide:
    /// the pair was selectable, the profile was read, the command was sent, and the answer came back
    /// no. Folded into either of the others it would report a screen state that is not true, and the
    /// player would be shown a ladder that is not what stopped them.
    /// </remarks>
    RefusedByRules = 4,
}

/// <summary>How far the Chapter Select screen has got with the read its gating depends on.</summary>
public enum ChapterSelectStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The player's Legend Level and clear history are known.</summary>
    Ready = 2,

    /// <summary>Nothing is stored for this player.</summary>
    ProfileMissing = 3,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 4,
}

/// <summary>
/// Drives the Chapter Select screen: which chapters exist, which (chapter, tier) pairs the player
/// may play, and the <c>START_RUN</c> that a confirmed choice submits.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The ladder is authored data, never a constant here.</b> Which clear each tier demands and
/// which Legend Level Mythic demands are read from <c>tuning/progression.json#/chapterGating</c> on
/// every lookup. A number transcribed into this file would be a balance decision that a tuning edit
/// could no longer move.
/// </para>
/// <para>
/// 🔒 <b>This gate is drawn here and enforced in the rules layer, from the same ladder.</b>
/// <c>StartRun.Handle</c> reads <c>tuning/progression.json#/chapterGating</c> too and refuses a
/// request that does not meet it with <c>PREREQUISITE_NOT_CLEARED</c> or
/// <c>LEGEND_LEVEL_TOO_LOW</c>, so a client that skipped this screen gains nothing. What this
/// screen owns is the <em>instruction</em>: the refusal a player can read and act on, before a
/// command is sent at all.
/// </para>
/// <para>
/// ⚠️ <b>The one asymmetry, and it is deliberate:</b> this screen lists <em>every</em> unmet
/// requirement, and the handler names one. A rejection carries no detail payload, so a Mythic
/// request blocked by a missing clear and by Legend Level at the same time comes back as the clear
/// alone — which is why a screen that showed only the first requirement would send the player to
/// do half the work and come back to the same locked button.
/// </para>
/// <para>
/// 🔒 <b>The generic ladder is the single runtime authority.</b> Each chapter document also carries
/// its own <c>unlockCondition</c>, and it is a restatement, never a second source: the chapter
/// schema pins its tier to the one tier the ladder's Normal rung names, and a declared loader rule
/// pins the chapter number and cross-checks the schema against the ladder, so a chapter authored
/// with a prerequisite the ladder cannot express fails the build. That is why nothing here reads it.
/// </para>
/// <para>
/// 🔒 There is deliberately no par-power, expected-power or power-warning member. The power model
/// belongs to the row that owns it, and a comparison invented here would be a second answer to a
/// question that already has one owner.
/// </para>
/// </remarks>
public sealed class ChapterSelectPresenter
{
    /// <summary>
    /// The screen's own strings. The chapter names are not among them: each chapter document names
    /// its own, so a chapter added later brings its name with it rather than needing a second edit.
    /// </summary>
    private const string TitleKey = "loc.chapter_select.title.name";

    private const string ConfirmActionKey = "loc.chapter_select.confirm.action";
    private const string RequiresClearBlockKey = "loc.chapter_select.requires_clear.block";
    private const string RequiresLegendLevelBlockKey = "loc.chapter_select.requires_legend_level.block";
    private const string TierNormalKey = "loc.chapter_select.tier_normal.name";
    private const string TierHeroicKey = "loc.chapter_select.tier_heroic.name";
    private const string TierMythicKey = "loc.chapter_select.tier_mythic.name";
    private const string LoadingStatusKey = "loc.chapter_select.loading.status";
    private const string ProfileMissingStatusKey = "loc.chapter_select.profile_missing.status";
    private const string UnavailableStatusKey = "loc.chapter_select.unavailable.status";
    private const string StartingStatusKey = "loc.chapter_select.starting.status";
    private const string StartedStatusKey = "loc.chapter_select.started.status";
    private const string RefusedStatusKey = "loc.chapter_select.refused.status";

    /// <summary>The status line of a screen whose list is the answer: there is nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>Where the chapter documents sit in the content set.</summary>
    private const string ChaptersDirectoryPrefix = "content/chapters/";

    private const string ChapterIdMember = "id";
    private const string ChapterDisplayNameMember = "displayName";

    /// <summary>The authored gating ladder, one rung per tier, keyed by the tier's own name.</summary>
    private const string ChapterGatingPointer = "tuning/progression.json#/chapterGating/";

    private const string RequiresClearMember = "requiresClear";
    private const string RequiresLegendLevelMember = "requiresLegendLevel";

    /// <summary>The three clears the authored ladder can name.</summary>
    private const string PreviousChapterNormal = "PREVIOUS_CHAPTER_NORMAL";

    private const string SameChapterNormal = "SAME_CHAPTER_NORMAL";
    private const string SameChapterHeroic = "SAME_CHAPTER_HEROIC";

    /// <summary>The lowest chapter number the campaign has, so the rung below it demands nothing.</summary>
    private const int FirstChapterId = 1;

    /// <summary>Separates the chapter from the tier in one clear-history key.</summary>
    private const char ClearedKeySeparator = ':';

    private static readonly IReadOnlyList<ChapterTierRequirement> NothingUnmet = [];

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;
    private readonly HashSet<int> _authoredChapterIds;

    private int _legendLevel;
    private IReadOnlyDictionary<string, long>? _clearedChapterTiers;
    /// <summary>
    /// Whether the last confirm started a run — null until one has been answered.
    /// </summary>
    /// <remarks>
    /// Deliberately a yes/no rather than the verdict itself: the line it decides has exactly two
    /// things to say, and a confirm whose host <em>threw</em> has no verdict to carry. Recording the
    /// nearest named refusal there would put a cause on the screen that nothing decided.
    /// </remarks>
    private bool? _lastConfirmStartedARun;

    /// <summary>Builds the screen over the host, the strings, the content set and the profile.</summary>
    /// <param name="gameHost">The seam the player's state is read through and the run is started through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the chapters and the gating ladder are read from.</param>
    /// <param name="player">The profile this screen is about.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ChapterSelectPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;

        Chapters = ReadChapters(content, strings);
        _authoredChapterIds = [.. Chapters.Select(c => c.ChapterId)];
    }

    /// <summary>Every chapter the content set authors, in ascending id order.</summary>
    public IReadOnlyList<ChapterListing> Chapters { get; }

    /// <summary>How far the read the gating depends on has got.</summary>
    public ChapterSelectStage Stage { get; private set; } = ChapterSelectStage.NotYetRead;

    /// <summary>
    /// Why the rules layer refused the last <c>START_RUN</c> that reached it, or null when the last
    /// one was accepted and when none has been sent.
    /// </summary>
    /// <remarks>
    /// 🔒 The reason is carried across rather than collapsed into the verdict. A run already open, a
    /// chapter id below one, an undefined tier and either half of the ladder are all refused, and
    /// they are the difference between "you are already playing", "this build sent nonsense" and
    /// "this screen was drawing a ladder that had already moved" — a single flag saying the command
    /// failed would leave the player and whoever reads the logs with the same blank.
    /// </remarks>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>
    /// The run the last accepted <c>START_RUN</c> opened, or null while none has been accepted.
    /// </summary>
    /// <remarks>
    /// 🔒 Taken from the state the command answered with rather than from a read that follows it.
    /// A second read would be a second round trip on the one tap in the game that must not stall,
    /// and — worse — the run it found would be whichever run the player is in, which is the same
    /// run only until it is not.
    /// </remarks>
    public RunId? StartedRun { get; private set; }

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleKey);

    /// <summary>The confirm control's caption, resolved.</summary>
    public string ConfirmText => _strings.Resolve(ConfirmActionKey);

    /// <summary>The caption shown against an unmet <see cref="ClearRequirement"/>, resolved.</summary>
    public string RequiresClearCaption => _strings.Resolve(RequiresClearBlockKey);

    /// <summary>The caption shown against an unmet <see cref="LegendLevelRequirement"/>, resolved.</summary>
    public string RequiresLegendLevelCaption => _strings.Resolve(RequiresLegendLevelBlockKey);

    /// <summary>
    /// The line saying what the screen is doing while its list is not yet an answer, resolved —
    /// never a raw key, and empty once it is.
    /// </summary>
    /// <remarks>
    /// 🔒 One sentence per stage, and the three that are not <see cref="ChapterSelectStage.Ready"/>
    /// say three different things. The list is dimmed and non-interactive in all three, so without
    /// words a player cannot tell a read that is still running from one that found no profile or did
    /// not answer at all — and only the first of those ever ends, which makes the other two a wait
    /// with no end that nothing on the screen admits to. What is <em>not</em> here is a retry, a
    /// reload or a panel: the failure screen and its ladder belong to the row that owns them, and one
    /// honest line is the whole of what this screen may say.
    /// </remarks>
    public string StatusText => KeyFor(Stage) is { } key ? _strings.Resolve(key) : NothingLeftToSay;

    /// <summary>
    /// The line shown from the moment a confirm is pressed until the host has answered it, resolved.
    /// </summary>
    /// <remarks>
    /// 🔒 A caption rather than a state, because the screen holds the in-flight window and this class
    /// cannot: the control is taken out of use on the press, before <see cref="ConfirmAsync"/> is
    /// called at all, and a flag settled inside the call would be read one redraw too late. What the
    /// player would otherwise see is the confirm greying out and nothing else — which is the same
    /// thing they see when no chapter is chosen, so the one moment the screen is genuinely working
    /// is drawn exactly like the one moment it has nothing to do.
    /// </remarks>
    public string StartingStatus => _strings.Resolve(StartingStatusKey);

    /// <summary>
    /// The line about the last confirm this screen answered, resolved — and empty until one has been.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The three outcomes are told apart, because they are escaped by doing three different
    /// things and only one of them is success. A run the rules layer refused leaves the control live
    /// and is worth pressing again; a run that started leaves it latched forever, and the player has
    /// no other way to learn that the tap they made was the one that worked.
    /// </para>
    /// <para>
    /// ⚠️ Every refusal shares one sentence, and that IS a collapse — deliberately. The identity of
    /// the refusal is carried by <see cref="RulesRejection"/> for the log; a sentence per
    /// <see cref="RejectionReason"/> would be twenty authored strings, most of them for reasons this
    /// screen cannot reach.
    /// </para>
    /// <para>
    /// 🔒 And the collapse holds even now that a ladder refusal is reachable, because a sentence is
    /// the weaker of the two surfaces this screen has. A refusal re-reads the state before it
    /// returns, so the row the player is looking at redraws as blocked and its requirement lines
    /// name the very chapter, tier and Legend Level the handler refused on — the whole instruction,
    /// not the one requirement a wire value has room for. A sentence per reason would restate the
    /// worse half of that and would have to be kept true against a ladder it does not read.
    /// </para>
    /// </remarks>
    public string ConfirmStatusText => _lastConfirmStartedARun switch
    {
        null => NothingLeftToSay,
        true => _strings.Resolve(StartedStatusKey),
        _ => _strings.Resolve(RefusedStatusKey),
    };

    /// <summary>A difficulty tier's name, resolved.</summary>
    /// <remarks>
    /// A tier the game does not define is answered with its own value rather than with the last
    /// arm's caption. The enum has no zero member, so <c>default(DifficultyTier)</c> is such a
    /// value, and a catch-all that named it "Mythic" would put a real tier's name on a tier nobody
    /// chose — a plausible answer to a question with no answer, which is worse than a visibly wrong
    /// one.
    /// </remarks>
    /// <param name="tier">The tier to name.</param>
    public string TierName(DifficultyTier tier) => tier switch
    {
        DifficultyTier.NORMAL => _strings.Resolve(TierNormalKey),
        DifficultyTier.HEROIC => _strings.Resolve(TierHeroicKey),
        DifficultyTier.MYTHIC => _strings.Resolve(TierMythicKey),
        _ => tier.ToString(),
    };

    /// <summary>What the player may do with one (chapter, tier) pair.</summary>
    /// <param name="chapterId">The chapter asked about.</param>
    /// <param name="tier">The tier asked about.</param>
    public ChapterTierAvailability Availability(int chapterId, DifficultyTier tier)
    {
        // Asked before the state check, because whether a chapter was ever written is a fact about
        // the content set alone and stays true whatever the read does or does not answer.
        if (!_authoredChapterIds.Contains(chapterId))
        {
            return new ChapterTierAvailability(ChapterTierLookup.NotAuthored, NothingUnmet);
        }

        if (Stage != ChapterSelectStage.Ready)
        {
            return new ChapterTierAvailability(ChapterTierLookup.NotYetKnown, NothingUnmet);
        }

        var unmet = UnmetRequirements(chapterId, tier);

        return new ChapterTierAvailability(
            unmet.Count == 0 ? ChapterTierLookup.Selectable : ChapterTierLookup.Blocked, unmet);
    }

    /// <summary>Reads the player's own state, which is what the gating is decided against.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct) => ReadOwnStateAsync(ct);

    private async Task ReadOwnStateAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await _gameHost.ReadOwnStateAsync(_player, run: null, ct).ConfigureAwait(false);

            if (state.Lookup != OwnStateLookup.Found || state.View is not { } view)
            {
                Stage = ChapterSelectStage.ProfileMissing;
                return;
            }

            _legendLevel = view.Player.LegendLevel;
            _clearedChapterTiers = view.Player.ClearedChapterTiers;
            Stage = ChapterSelectStage.Ready;
        }
        catch (Exception)
        {
            // The stage is the whole answer this screen has room for. It picks which sentence the
            // player reads; the failure's own identity goes to the log, because an untranslated
            // type name beside a line that was just translated helps nobody reading it.
            Stage = ChapterSelectStage.ReadUnavailable;
        }
    }

    /// <summary>Submits <c>START_RUN</c> for a selectable pair, and nothing at all for any other.</summary>
    /// <remarks>
    /// A submitted command is not an accepted one. The rules layer checks the same ladder this screen
    /// drew, and more besides — it refuses a second run while one is open — so a screen that reported
    /// the tap as taken would latch its confirm on a run that never started.
    /// </remarks>
    /// <param name="chapterId">The chosen chapter.</param>
    /// <param name="tier">The chosen tier.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<ChapterSelectSubmission> ConfirmAsync(
        int chapterId, DifficultyTier tier, CancellationToken ct)
    {
        try
        {
            // Recorded on every path rather than at each return, so a verdict added later cannot be
            // the one the screen has no sentence for.
            var submission = await SubmitAsync(chapterId, tier, ct).ConfigureAwait(false);

            _lastConfirmStartedARun = submission == ChapterSelectSubmission.Submitted;

            return submission;
        }
        catch (Exception)
        {
            // 🔒 A confirm whose host threw started no run either, and that is the whole of what the
            // screen may say about it. Recorded before the failure travels on, because the caller
            // logs the exception and redraws — and without this the one press a player can make
            // would fail in complete silence, which is the state it is worst to leave them in.
            _lastConfirmStartedARun = false;

            throw;
        }
    }

    private async Task<ChapterSelectSubmission> SubmitAsync(
        int chapterId, DifficultyTier tier, CancellationToken ct)
    {
        if (Stage != ChapterSelectStage.Ready)
        {
            return ChapterSelectSubmission.RefusedProfileUnavailable;
        }

        if (Availability(chapterId, tier).Lookup != ChapterTierLookup.Selectable)
        {
            return ChapterSelectSubmission.RefusedNotSelectable;
        }

        var outcome = await _gameHost
            .SubmitAsync(_player, run: null, new StartRunCommand(chapterId, tier), ct)
            .ConfigureAwait(false);

        RulesRejection = outcome.Rejection;

        if (!outcome.Accepted)
        {
            // The pair was selectable, so the ladder this screen drew and the ladder the rules layer
            // answered against disagreed — and the only thing this screen owns that can be wrong is
            // the state it read once, before the tap. Re-read it here, because nothing else ever
            // will: the confirm comes back live over whatever the rows are still drawing, and a
            // refusal the player cannot see the cause of is a refusal they can only answer by
            // pressing the same button again.
            await ReadOwnStateAsync(ct).ConfigureAwait(false);

            return ChapterSelectSubmission.RefusedByRules;
        }

        StartedRun = outcome.State.Run?.Id;

        return ChapterSelectSubmission.Submitted;
    }

    /// <summary>The status key each stage is drawn from, or null for the stage that needs none.</summary>
    /// <remarks>
    /// Ready is the null: the chapters and their requirement lines are the answer by then, and a
    /// fourth sentence over a live list would be a screen still talking about itself. The catch-all
    /// is the failure line rather than that null, so a stage this switch has not been taught is
    /// reported as a read that did not answer instead of falling silently into "everything is fine".
    /// </remarks>
    private static string? KeyFor(ChapterSelectStage stage) => stage switch
    {
        ChapterSelectStage.NotYetRead => LoadingStatusKey,
        ChapterSelectStage.Ready => null,
        ChapterSelectStage.ProfileMissing => ProfileMissingStatusKey,
        _ => UnavailableStatusKey,
    };

    private static IReadOnlyList<ChapterListing> ReadChapters(
        ContentSnapshot content, LocaleStringCatalogue strings)
    {
        var listings = new List<ChapterListing>();

        foreach (var path in content.DocumentPaths)
        {
            if (!path.StartsWith(ChaptersDirectoryPrefix, StringComparison.Ordinal) ||
                !content.TryGetDocument(path, out var document))
            {
                continue;
            }

            var root = document!.Root;

            if (root.TryGetMember(ChapterIdMember, out var id) &&
                id!.Kind == ContentValueKind.Number &&
                root.TryGetMember(ChapterDisplayNameMember, out var displayName) &&
                displayName!.Kind == ContentValueKind.Text)
            {
                listings.Add(new ChapterListing(id.AsInt32(), strings.Resolve(displayName.AsText())));
            }
        }

        // The campaign's own order, not the content set's: document paths sort by file name, and a
        // chapter renamed on disk would otherwise move in the picker.
        listings.Sort((left, right) => left.ChapterId.CompareTo(right.ChapterId));

        return listings;
    }

    private IReadOnlyList<ChapterTierRequirement> UnmetRequirements(int chapterId, DifficultyTier tier)
    {
        var rung = Rung(tier);
        var unmet = new List<ChapterTierRequirement>();

        if (RequiredClear(rung, chapterId) is { } clear && !HasCleared(clear))
        {
            unmet.Add(clear);
        }

        if (RequiredLegendLevel(rung) is { } required && _legendLevel < required)
        {
            unmet.Add(new LegendLevelRequirement(required, _legendLevel));
        }

        return unmet.Count == 0 ? NothingUnmet : unmet;
    }

    /// <summary>The authored rung for one tier, or null when the ladder does not describe it.</summary>
    /// <remarks>
    /// The tier's own name is the member name the ladder is keyed by, which is what lets a tier
    /// added to the enum find its rung without a second table here to keep in step.
    /// </remarks>
    private ContentValue? Rung(DifficultyTier tier) =>
        _content.TryRead($"{ChapterGatingPointer}{tier}", out var rung) &&
        rung!.Kind == ContentValueKind.Object
            ? rung
            : null;

    private static ClearRequirement? RequiredClear(ContentValue? rung, int chapterId)
    {
        if (rung is null ||
            !rung.TryGetMember(RequiresClearMember, out var required) ||
            required!.Kind != ContentValueKind.Text)
        {
            return null;
        }

        return required.AsText() switch
        {
            // The first chapter has no chapter before it, so this rung resolves to nothing at all
            // rather than to a clear of a chapter that was never written.
            PreviousChapterNormal => chapterId > FirstChapterId
                ? new ClearRequirement(chapterId - 1, DifficultyTier.NORMAL)
                : null,
            SameChapterNormal => new ClearRequirement(chapterId, DifficultyTier.NORMAL),
            SameChapterHeroic => new ClearRequirement(chapterId, DifficultyTier.HEROIC),

            // A clear this screen cannot name is a clear it cannot ask the player for, and a
            // refusal carrying no requirement is the "it is disabled" this screen exists to avoid.
            // Note which way that fails: an unrecognised token OPENS the rung. The vocabulary is
            // therefore closed by an enum in progression.schema.json, so a typo is a content
            // failure rather than an unlocked tier, and this arm is the honest answer to data that
            // got past it rather than the thing standing between a typo and a shipped bug.
            _ => null,
        };
    }

    private static int? RequiredLegendLevel(ContentValue? rung) =>
        rung is not null &&
        rung.TryGetMember(RequiresLegendLevelMember, out var required) &&
        required!.Kind == ContentValueKind.Number
            ? required.AsInt32()
            : null;

    /// <summary>Whether the clear history records one (chapter, tier) pair as beaten.</summary>
    /// <remarks>
    /// 🔒 The key is re-formed here rather than asked for: the rules layer's own key builder is
    /// internal to its assembly and unreachable from a client. This is therefore a transcription of
    /// a format another assembly owns — the chapter number, a colon, and the tier's NAME — and it
    /// will break loudly here, in a case that pins the literal shape, if that format ever moves.
    /// </remarks>
    private bool HasCleared(ClearRequirement requirement) =>
        _clearedChapterTiers is { } history &&
        history.ContainsKey($"{requirement.ChapterId}{ClearedKeySeparator}{requirement.Tier}");
}
