using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Board screen has got with the read everything it draws depends on.</summary>
public enum BoardStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run was read and is playable.</summary>
    Ready = 2,

    /// <summary>There is no such run for this player. Named, so it can never read as "keep playing".</summary>
    RunMissing = 3,

    /// <summary>The run was read and has already finished.</summary>
    RunEnded = 4,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 5,
}

/// <summary>
/// Why the roll is not live — decided from the run's own state, never from a refusal.
/// </summary>
/// <remarks>
/// 🔒 <b>This enum is the reason this screen has a presenter at all.</b> Four of the five ways a
/// roll can be refused come back on the wire as the SAME value, so a screen that waited to be told
/// would learn only that the roll failed. Each of the four is escaped by doing something completely
/// different — resolve the tile, choose a branch, fight the battle, take a perk — so collapsing them
/// leaves the player holding a dead button and no instruction. The run row carries every fact needed
/// to tell them apart before anything is submitted, so that is where they are told apart.
/// </remarks>
public enum BoardRollBlock
{
    /// <summary>Nothing is in the way; the roll may be taken.</summary>
    None = 1,

    /// <summary>The read has not answered, so whether a roll is legal is not yet known.</summary>
    NotYetRead = 2,

    /// <summary>A tile was landed on and not resolved. Escaped by resolving it.</summary>
    TilePending = 3,

    /// <summary>Movement is paused at a junction. Escaped by choosing a branch.</summary>
    ForkOpen = 4,

    /// <summary>A battle is open. Escaped by fighting it — on a screen this task does not own.</summary>
    BattleOpen = 5,

    /// <summary>A won battle's perk draft is open. Escaped on a screen this task does not own.</summary>
    DraftOpen = 6,

    /// <summary>The run has finished. Escaped by leaving, which no screen here can do yet.</summary>
    RunEnded = 7,
}

/// <summary>What one submission from this screen did.</summary>
public enum BoardSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>
    /// The command went to the host and the rules layer refused it. Which refusal is carried by
    /// <see cref="BoardPresenter.RulesRejection"/>.
    /// </summary>
    RefusedByRules = 3,
}

/// <summary>The tile the run is standing on and has not resolved.</summary>
/// <param name="Kind">The kind, as the run's own integer.</param>
/// <param name="NameKey">Its caption key, or null when the number names no kind this build knows.</param>
/// <param name="LinearIndex">The node index it sits at — exact, unlike <see cref="BoardPresenter.Position"/>.</param>
/// <param name="Stage">The stage it belongs to, 1-3, or 0 for the boss node.</param>
public sealed record PendingTile(int Kind, string? NameKey, int LinearIndex, int Stage);

/// <summary>One branch a paused junction offers.</summary>
/// <param name="BranchIndex">What <c>CHOOSE_FORK</c> carries for it.</param>
/// <param name="CaptionKey">The caption key describing where it goes.</param>
public sealed record ForkBranch(int BranchIndex, string CaptionKey);

/// <summary>A movement paused at a junction, waiting for the player to pick an edge.</summary>
/// <param name="JunctionPosition">The junction the run is paused on.</param>
/// <param name="RemainingSteps">Steps left to spend once the edge is taken.</param>
/// <param name="Branches">The edges on offer, in the order <c>CHOOSE_FORK</c> indexes them.</param>
public sealed record ForkPrompt(int JunctionPosition, int RemainingSteps, IReadOnlyList<ForkBranch> Branches);

/// <summary>The window after a roll during which the reroll is offered.</summary>
/// <param name="Face">The face the roll reported.</param>
/// <param name="Remaining">How much of the ring is left, or null when this prompt never lapses.</param>
/// <param name="RingFraction">
/// How full the ring is drawn, from one down to zero — and one for a prompt that never lapses.
/// </param>
/// <remarks>
/// 🔒 The fraction is computed here rather than by whatever draws the ring, because dividing by the
/// window's length means knowing the window's length, and that duration is authored. A renderer
/// working it out would be a second copy of it in the one place this codebase keeps saying a copy
/// must not go.
/// </remarks>
public sealed record RerollPrompt(DieFaceReading Face, TimeSpan? Remaining, double RingFraction);

/// <summary>A face as the run reported having rolled it.</summary>
/// <param name="Sequence">Its position among the faces one roll produced — a chain reports several.</param>
/// <param name="Kind">The kind, as the rules layer's own public vocabulary spells it.</param>
/// <param name="Value">Its pips, meaningful only for a pip face.</param>
public sealed record DieFaceReading(int Sequence, string Kind, int Value);

/// <summary>
/// Drives the Board screen: what the run carries, what the player may do next, and the four commands
/// a board turn is made of.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it. That is not tidiness here: there is no scene-test harness
/// in this repository, so logic left in the scene is logic nothing can test, and every decision this
/// screen makes is therefore made in this file.
/// </para>
/// <para>
/// 🔴 <b>There is no board here, and that is the largest thing to know about this screen.</b> The
/// graph a run is played on — which tile sits at which node, how many nodes a fork branch has, what
/// is coming up — is generated inside the rules assembly and never leaves it. No persisted field
/// carries it and no reachable type describes it. So this screen cannot draw the track of upcoming
/// tiles the design calls for. What it draws instead is true: how far through the current stage the
/// run has walked, out of the stage's own authored length, and the name of the one tile the run is
/// actually standing on. The upcoming-tile preview, the fork's authored branch labels and icon sets,
/// and the burning-tile marks are all absent because they are unreadable, not because they were
/// forgotten.
/// </para>
/// <para>
/// ⚠️ <b>The reroll does not re-roll the face it is offered beside.</b> The design describes a
/// prompt that appears after the die settles and before movement, offering to replace the result.
/// The shipped command cannot do that: one <c>ROLL_DICE</c> answers with the face, the movement and
/// the landing together, so by the time a face is known the run has already moved. The reroll
/// command that exists spends a charge to advance the die for the NEXT roll. This screen therefore
/// offers it for what it is — see <see cref="TheRerollCannotReplaceTheFaceItIsShownBeside"/> — and
/// does not caption it as an undo it cannot perform.
/// </para>
/// <para>
/// 🔴 Absent because a later screen owns each: the battle, the perk draft, the shop, the event card,
/// the campfire choice, the minigame, the run's decision screens and its death and results. Where a
/// board turn hands off to one of them this screen stops at a named destination rather than building
/// half of a screen it does not own.
/// </para>
/// </remarks>
public sealed class BoardPresenter
{
    /// <summary>
    /// ⚠️ Deliberately not papered over, and named so it can be found. The design's reroll prompt
    /// offers to replace the face just shown; the command the rules layer actually ships cannot,
    /// because the roll, the movement and the landing are one command and are already committed by
    /// the time any face is known. What <c>USE_REROLL</c> does instead is spend a charge to advance
    /// the die so the NEXT roll differs. Reconciling the two is a dice-system decision no task owns,
    /// so the divergence is stated here rather than hidden behind a caption that would promise an
    /// undo and deliver a different mechanic.
    /// </summary>
    private const string TheRerollCannotReplaceTheFaceItIsShownBeside =
        "04's reroll prompt offers to replace the face the die just settled on. USE_REROLL cannot: " +
        "ROLL_DICE answers with the face, the movement and the landing in one command, so the run " +
        "has already moved before a face is known, and the handler's own remarks say it instead " +
        "burns a dice-stream draw so the next ROLL_DICE differs. The prompt below is therefore the " +
        "authored acceptance window, and the control inside it is offered for what the command " +
        "does rather than for what the design describes.";

    /// <summary>
    /// ⚠️ Deliberately unread, and named so it can be found. Nothing reachable from a client
    /// describes the board, so the two branches a junction offers are named by their structural
    /// position rather than by the labels the generator actually authored for them.
    /// </summary>
    private const string TheForkPreviewIsNotReachableHere =
        "03 §3.1 gives each branch a one-word label — Perilous, Sheltered, Arcane, Feral — and up " +
        "to three icons drawn from its real contents, and the generator does author them. They are " +
        "built inside the rules assembly, attached to an edge of a graph that is regenerated per " +
        "command, and deliberately not persisted; every type involved is internal. So this screen " +
        "knows a junction is open and how many steps are left, and nothing whatever about what " +
        "either branch holds. It names the two edges by the one fact it does have — that a junction " +
        "has exactly two, the first continuing the spine and the second entering the side path — " +
        "and shows no preview, rather than inventing one for the run's only real navigation choice.";

    /// <summary>
    /// ⚠️ Deliberately not computed, and named so it can be found. The reroll charge allowance is
    /// decided inside the rules layer and no client can read it, so this screen reports what has
    /// been spent and learns exhaustion from the refusal rather than predicting it.
    /// </summary>
    private const string TheRerollAllowanceIsNotReadableHere =
        "The per-stage reroll allowance is computed by a rules-internal calculator from a base " +
        "allotment plus talent, campfire, perk and token bonuses. Nothing public exposes it and no " +
        "tuning document carries it, so a remaining-charges number shown here would be a second " +
        "copy of that calculation — and it would be wrong the moment the first bonus source is " +
        "wired, which is exactly when a player starts having more than one. The screen shows the " +
        "spent count, which is a real persisted field, and reports exhaustion only once the rules " +
        "layer has answered CAP_REACHED — a reason distinct enough on the wire to be told apart.";

    /// <summary>
    /// The ring that runs around the reroll control once a roll lands.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Authored, not chosen here.</b> The design set fixes this duration and fixes what
    /// happens when it lapses: the roll is accepted. It is not a number this task picked, and it is
    /// not an economy tunable, so it is stated here rather than in the tuning tree. The case that
    /// pins it names the section it comes from.
    /// </remarks>
    private static readonly TimeSpan RerollRingDuration = TimeSpan.FromSeconds(4);

    private const string HpLabelKey = "loc.board.hp.label";
    private const string GoldLabelKey = "loc.board.gold.label";
    private const string StageLabelKey = "loc.board.stage.label";
    private const string RolledLabelKey = "loc.board.rolled.label";
    private const string StandingOnLabelKey = "loc.board.standing_on.label";
    private const string RollActionKey = "loc.board.roll.action";
    private const string RerollActionKey = "loc.board.reroll.action";
    private const string ResolveActionKey = "loc.board.resolve.action";
    private const string DiePanelActionKey = "loc.board.die_panel.action";
    private const string ForkNameKey = "loc.board.fork.name";
    private const string ForkContinueActionKey = "loc.board.fork_continue.action";
    private const string ForkBranchActionKey = "loc.board.fork_branch.action";
    private const string LoadingStatusKey = "loc.board.loading.status";
    private const string RunMissingStatusKey = "loc.board.run_missing.status";
    private const string RunEndedStatusKey = "loc.board.run_ended.status";
    private const string UnavailableStatusKey = "loc.board.unavailable.status";
    private const string RefusedStatusKey = "loc.board.refused.status";
    private const string RerollExhaustedStatusKey = "loc.board.reroll_exhausted.status";
    private const string BlockedTileStatusKey = "loc.board.blocked_tile.status";
    private const string BlockedForkStatusKey = "loc.board.blocked_fork.status";
    private const string BlockedBattleStatusKey = "loc.board.blocked_battle.status";
    private const string BlockedDraftStatusKey = "loc.board.blocked_draft.status";

    /// <summary>The status line of a screen whose board is the answer: there is nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>Where the chapter documents sit, so this run's stage lengths can be found.</summary>
    private const string ChaptersDirectoryPrefix = "content/chapters/";

    private const string ChapterIdMember = "id";
    private const string StageLengthsMember = "stageLengths";

    /// <summary>
    /// A junction has exactly two outgoing edges, and this is that count transcribed.
    /// </summary>
    /// <remarks>
    /// 🔒 It is a structural invariant of the rules layer rather than a guess: a graph that marks a
    /// node as a junction and does not give it exactly two outgoing edges is refused at
    /// construction. The client cannot ask, so it transcribes — and the fail-safe is real, because
    /// an index outside the edge list is refused rather than silently taken.
    /// </remarks>
    private const int BranchesPerJunction = 2;

    /// <summary>The edge that keeps to the spine — always the first a junction offers.</summary>
    private const int ContinueBranchIndex = 0;

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly IClockPort _clock;
    private readonly PlayerId _player;
    private readonly RunId _run;
    private readonly bool _ringLapses;

    private RunSnapshot? _snapshot;
    private DateTimeOffset _promptOpenedAt;
    private DieFaceReading? _promptFace;
    private bool _promptOpen;

    /// <summary>When the ring was covered, or null while it is running.</summary>
    private DateTimeOffset? _suspendedAt;

    /// <summary>The faces the most recent command reported — empty when it reported none.</summary>
    /// <remarks>
    /// Distinct from <see cref="LastRolledFaces"/> on purpose: this one is scoped to one command
    /// and is what a roll opens its prompt from, while that one is what the die panel shows and
    /// therefore outlives commands that roll nothing.
    /// </remarks>
    private IReadOnlyList<DieFaceReading> _facesFromLastCommand = [];

    /// <summary>Builds the screen over the host, the strings, the content set, the clock and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the chapter's stage lengths are read from.</param>
    /// <param name="clock">What the reroll ring is measured against — injected, so a case can drive it.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <param name="rerollRingLapses">
    /// Whether the ring expires on its own. False is the accessibility setting that removes every
    /// soft timer and lets each prompt wait indefinitely; the screen that would set it is not built,
    /// so it arrives as a constructor argument rather than being read from a store that does not exist.
    /// </param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public BoardPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        IClockPort clock,
        PlayerId player,
        RunId run,
        bool rerollRingLapses = true)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _clock = clock;
        _player = player;
        _run = run;
        _ringLapses = rerollRingLapses;
    }

    /// <summary>How far the read this screen depends on has got.</summary>
    public BoardStage Stage { get; private set; } = BoardStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>The hero's current hit points, as the run carries them.</summary>
    public int CurrentHp { get; private set; }

    /// <summary>The hero's maximum hit points for this run, as the run carries them.</summary>
    public int MaxHp { get; private set; }

    /// <summary>The run's Gold balance, as the run carries it.</summary>
    public long Gold { get; private set; }

    /// <summary>
    /// The node the run stands on, as the run carries it.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the node's own identity, not its distance along the track. The two agree for every
    /// node of the spine and for the boss, and diverge for the two to four nodes of a fork branch,
    /// because a branch node shares its distance with the spine node level with it. Drawing a track
    /// straight off this value therefore misplaces the token inside a branch —
    /// <see cref="TrackIndex"/> is what a track is drawn from.
    /// </remarks>
    public int Position { get; private set; }

    /// <summary>
    /// How far along the track the run stands, or null when that cannot be said exactly.
    /// </summary>
    /// <remarks>
    /// 🔒 Null rather than a best guess. The exact distance is carried only by a pending tile, so
    /// between resolving one tile and landing on the next there is a window in which the run's
    /// distance is knowable for a spine node and not for a branch node — and the client cannot tell
    /// which it is standing on. A token drawn at a plausible position for those few nodes would be a
    /// board quietly lying about where the player is, so the track holds its last exact position
    /// instead.
    /// </remarks>
    public int? TrackIndex => PendingTile?.LinearIndex;

    /// <summary>The stage the run is in, 1-3, or null while that is not known.</summary>
    /// <remarks>
    /// Known only from a pending tile, for the same reason <see cref="TrackIndex"/> is. The boss
    /// node reports its own stage value, which is why a pending tile is asked for its kind as well.
    /// </remarks>
    public int? StageNumber => PendingTile is { Stage: >= 1 and <= 3 } tile ? tile.Stage : null;

    /// <summary>
    /// How many stages this run's chapter has, read from the chapter's own authored stage lengths.
    /// </summary>
    /// <remarks>
    /// Read rather than transcribed: the count is the length of the chapter document's stage-length
    /// array, so a chapter shipped with a different shape is described by its own document instead
    /// of by a constant here. Null when this run's chapter is not in the content set at all.
    /// </remarks>
    public int? StageCount { get; private set; }

    /// <summary>How long the current stage is, in nodes, or null when that is not known.</summary>
    public int? StageLength { get; private set; }

    /// <summary>
    /// Where the token sits <em>within</em> the current stage, or null when it cannot be placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Here rather than in the scene, and the first version of it was wrong in the scene.</b>
    /// The run's index is measured from the start of the chapter and runs continuously across every
    /// stage, so a stage after the first begins part-way along it and the offset to subtract is the
    /// sum of the lengths of the stages BEFORE it. Those lengths differ — the shipped chapters
    /// author twelve, then fourteen, then sixteen — so multiplying any one of them by the stage
    /// number lands on the wrong node everywhere except stage one, confidently and silently.
    /// </para>
    /// <para>
    /// 🔒 Null rather than a clamp when the offset does not land inside the stage. A token drawn at
    /// a plausible pip is a board quietly lying about where the player is, which is worse than a
    /// board that draws no token at all.
    /// </para>
    /// </remarks>
    public int? StageTrackIndex { get; private set; }

    /// <summary>The tile the run is standing on and has not resolved, or null when none is pending.</summary>
    public PendingTile? PendingTile { get; private set; }

    /// <summary>The junction the run is paused at, or null when movement is not paused.</summary>
    public ForkPrompt? Fork { get; private set; }

    /// <summary>Reroll charges spent since this stage began, as the run carries them.</summary>
    /// <remarks>
    /// ⚠️ The spent count and not the remaining one — see
    /// <see cref="TheRerollAllowanceIsNotReadableHere"/>.
    /// </remarks>
    public int RerollChargesSpent { get; private set; }

    /// <summary>Whether the last reroll was refused for having no charge left.</summary>
    public bool RerollExhausted { get; private set; }

    /// <summary>The faces the last accepted roll reported, in the order it produced them.</summary>
    /// <remarks>
    /// A list because one roll can produce several: a chain face rolls again immediately, and every
    /// face it draws on the way is reported. Empty until a roll has been accepted.
    /// </remarks>
    public IReadOnlyList<DieFaceReading> LastRolledFaces { get; private set; } = [];

    /// <summary>Why the roll is not live, decided from the run's own state.</summary>
    public BoardRollBlock RollBlock =>
        Stage switch
        {
            BoardStage.NotYetRead => BoardRollBlock.NotYetRead,
            BoardStage.RunEnded => BoardRollBlock.RunEnded,
            BoardStage.Ready => BlockFromSnapshot(),
            _ => BoardRollBlock.NotYetRead,
        };

    /// <summary>The open reroll prompt, or null when none is open.</summary>
    /// <remarks>
    /// The clock is read once and both values are derived from that one reading. Two readings would
    /// disagree the moment the clock moves between them, and a ring whose fraction and remaining
    /// time describe two different instants is a ring that stutters.
    /// </remarks>
    public RerollPrompt? Prompt
    {
        get
        {
            if (!_promptOpen || _promptFace is not { } face)
            {
                return null;
            }

            if (!_ringLapses)
            {
                return new RerollPrompt(face, Remaining: null, RingFraction: 1);
            }

            var left = RingRemaining();

            return new RerollPrompt(face, left, left / RerollRingDuration);
        }
    }

    /// <summary>The HP caption, resolved.</summary>
    public string HpLabel => _strings.Resolve(HpLabelKey);

    /// <summary>The Gold caption, resolved.</summary>
    public string GoldLabel => _strings.Resolve(GoldLabelKey);

    /// <summary>The stage caption, resolved.</summary>
    public string StageLabel => _strings.Resolve(StageLabelKey);

    /// <summary>The caption over the face last rolled, resolved.</summary>
    public string RolledLabel => _strings.Resolve(RolledLabelKey);

    /// <summary>The caption over the tile being stood on, resolved.</summary>
    public string StandingOnLabel => _strings.Resolve(StandingOnLabelKey);

    /// <summary>The roll control's caption, resolved.</summary>
    public string RollText => _strings.Resolve(RollActionKey);

    /// <summary>The reroll control's caption, resolved.</summary>
    public string RerollText => _strings.Resolve(RerollActionKey);

    /// <summary>The tile acknowledgement's caption, resolved.</summary>
    public string ResolveText => _strings.Resolve(ResolveActionKey);

    /// <summary>The die panel control's caption, resolved.</summary>
    public string DiePanelText => _strings.Resolve(DiePanelActionKey);

    /// <summary>The fork prompt's heading, resolved.</summary>
    public string ForkTitle => _strings.Resolve(ForkNameKey);

    /// <summary>The pending tile's name, resolved — empty when nothing is pending.</summary>
    /// <remarks>
    /// Empty is also the honest answer for a tile kind this build has no name for: the number is
    /// carried on <see cref="PendingTile"/> for the log, and no other tile's caption is borrowed
    /// for it.
    /// </remarks>
    public string PendingTileName =>
        PendingTile?.NameKey is { } key ? _strings.Resolve(key) : NothingLeftToSay;

    /// <summary>One branch's caption, resolved.</summary>
    /// <param name="branch">The branch to caption.</param>
    /// <exception cref="ArgumentNullException"><paramref name="branch"/> is null.</exception>
    public string BranchText(ForkBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return _strings.Resolve(branch.CaptionKey);
    }

    /// <summary>
    /// The line saying what the screen is doing while its board is not yet an answer, resolved.
    /// </summary>
    public string StatusText => Stage switch
    {
        BoardStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        BoardStage.Ready => NothingLeftToSay,
        BoardStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        BoardStage.RunEnded => _strings.Resolve(RunEndedStatusKey),
        _ => _strings.Resolve(UnavailableStatusKey),
    };

    /// <summary>
    /// The line saying why the roll is not live, resolved — and empty when it is, or when the
    /// screen has already said the same thing in <see cref="StatusText"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 Four sentences for the four blocks that share one wire value, because each is escaped by
    /// doing a different thing. The two that do NOT get a sentence here are the two the status line
    /// above already covers — a read that has not answered, and a run that has ended — and repeating
    /// either would put two lines on screen saying one thing.
    /// </remarks>
    public string BlockText => RollBlock switch
    {
        BoardRollBlock.TilePending => _strings.Resolve(BlockedTileStatusKey),
        BoardRollBlock.ForkOpen => _strings.Resolve(BlockedForkStatusKey),
        BoardRollBlock.BattleOpen => _strings.Resolve(BlockedBattleStatusKey),
        BoardRollBlock.DraftOpen => _strings.Resolve(BlockedDraftStatusKey),
        _ => NothingLeftToSay,
    };

    /// <summary>
    /// The line about the last command the rules layer answered, resolved — empty until one has been
    /// refused.
    /// </summary>
    /// <remarks>
    /// The exhausted reroll gets its own sentence because it is the one refusal on this screen a
    /// player can plan around, and because it is the one that arrives on the wire distinctly enough
    /// to be recognised. Everything else shares one sentence; the identity is carried by
    /// <see cref="RulesRejection"/> for the log.
    /// </remarks>
    public string RejectionText => RulesRejection switch
    {
        null => NothingLeftToSay,

        // Paired with the reason that set it, not merely with the flag. The flag alone would let
        // the reroll's sentence be printed under a refusal of something else entirely, which is the
        // collapse this screen exists to avoid, reintroduced one layer out.
        RejectionReason.CAP_REACHED when RerollExhausted =>
            _strings.Resolve(RerollExhaustedStatusKey),

        _ => _strings.Resolve(RefusedStatusKey),
    };

    /// <summary>Reads the run this screen is about and settles everything drawn from it.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);

            Settle(state);
        }
        catch (Exception)
        {
            // The stage is the whole answer this screen has room for. It picks which sentence the
            // player reads; the failure's own identity goes to the log, because an untranslated type
            // name beside a line that was just translated helps nobody reading it.
            Stage = BoardStage.ReadUnavailable;
        }
    }

    /// <summary>Submits <c>ROLL_DICE</c>, and nothing at all when something is in the way.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> RollAsync(CancellationToken ct)
    {
        if (RollBlock != BoardRollBlock.None)
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        // Closed before the command rather than after it: the prompt is about the roll that has
        // just been accepted, and leaving it open across a new one would put an old face beside a
        // new result.
        ClosePromptWithoutRerolling();

        var outcome = await SubmitAsync(new RollDiceCommand(), ct).ConfigureAwait(false);

        if (outcome != BoardSubmission.Submitted)
        {
            return outcome;
        }

        // Opened from the faces THIS command reported, never from the screen-wide list: that list
        // deliberately survives commands that report none, so opening from it would caption a new
        // prompt with an old roll's face.
        if (_facesFromLastCommand.Count > 0)
        {
            OpenPrompt(_facesFromLastCommand[^1]);
        }

        return outcome;
    }

    /// <summary>
    /// Submits <c>USE_REROLL</c> — which advances the die for the next roll rather than replacing
    /// the face this prompt is shown beside.
    /// </summary>
    /// <remarks>
    /// See <see cref="TheRerollCannotReplaceTheFaceItIsShownBeside"/>. Refused outright unless a
    /// prompt is open, so the charge cannot be spent from a screen that is not offering it.
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> UseRerollAsync(CancellationToken ct)
    {
        if (!_promptOpen || Stage != BoardStage.Ready)
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        var outcome = await SubmitAsync(new UseRerollCommand(), ct).ConfigureAwait(false);

        // Recorded before the prompt closes, so the sentence the player reads survives the window
        // that produced it. Only this reason latches it: any other refusal is the generic sentence.
        RerollExhausted = RulesRejection == RejectionReason.CAP_REACHED;

        ClosePromptWithoutRerolling();

        return outcome;
    }

    /// <summary>Submits <c>CHOOSE_FORK</c> for one of the branches on offer.</summary>
    /// <param name="branchIndex">The chosen branch's index, as the fork prompt lists it.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> ChooseForkAsync(int branchIndex, CancellationToken ct)
    {
        // Checked against the prompt actually on offer rather than against a bare range: a screen
        // that submitted an index for a fork that is not open would spend a command on a refusal
        // whose reason is shared with four other things.
        if (Stage != BoardStage.Ready ||
            Fork is not { } fork ||
            !fork.Branches.Any(branch => branch.BranchIndex == branchIndex))
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ChooseForkCommand(branchIndex), ct).ConfigureAwait(false);
    }

    /// <summary>Submits <c>RESOLVE_TILE</c> for the tile the run is standing on.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> ResolveTileAsync(CancellationToken ct)
    {
        if (Stage != BoardStage.Ready || PendingTile is null)
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ResolveTileCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lets the ring advance, and closes the prompt by accepting the roll once it has lapsed.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A lapse ACCEPTS the roll.</b> It does not reroll, and it does not spend a charge — the
    /// design is explicit that letting the timer run out and tapping anywhere else are the same
    /// thing. A ring that spent a charge on expiry would take the run's scarcest resource from a
    /// player who did nothing, which is the opposite of what a countdown on a free choice means.
    /// </remarks>
    /// <returns>True when this call was the one that closed the prompt.</returns>
    public bool TickRerollPrompt()
    {
        if (!_promptOpen || !_ringLapses || RingRemaining() > TimeSpan.Zero)
        {
            return false;
        }

        ClosePromptWithoutRerolling();

        return true;
    }

    /// <summary>Closes the prompt by accepting the roll — what a tap outside the control does.</summary>
    /// <returns>True when a prompt was open to close.</returns>
    public bool AcceptRoll()
    {
        if (!_promptOpen)
        {
            return false;
        }

        ClosePromptWithoutRerolling();

        return true;
    }

    /// <summary>Stops the ring while something is covering it, so the window is not spent unseen.</summary>
    /// <remarks>
    /// 🔒 The window is a deadline the player is answering. Opening the die panel over it — which is
    /// a thing the board offers, and a reasonable thing to do before deciding — would otherwise
    /// spend that answer on the act of looking something up. Suspending and resuming rather than
    /// simply not ticking, because the clock keeps moving either way: without this the ring would
    /// be found already lapsed the moment the panel closed.
    /// </remarks>
    /// <returns>True when there was a running ring to stop.</returns>
    public bool SuspendRerollPrompt()
    {
        if (!_promptOpen || _suspendedAt is not null)
        {
            return false;
        }

        _suspendedAt = _clock.UtcNow;

        return true;
    }

    /// <summary>Starts the ring again, having lost none of it to the interruption.</summary>
    /// <returns>True when there was a suspended ring to start.</returns>
    public bool ResumeRerollPrompt()
    {
        if (_suspendedAt is not { } suspended)
        {
            return false;
        }

        // The window is moved forward by exactly as long as it was covered, so the player gets back
        // the ring they had rather than whatever is left of it.
        _promptOpenedAt += _clock.UtcNow - suspended;
        _suspendedAt = null;

        return true;
    }

    private TimeSpan RingRemaining()
    {
        // A suspended ring is frozen at the instant it was covered, so reading it while the panel is
        // up neither advances it nor reports a window that is quietly draining.
        var elapsed = (_suspendedAt ?? _clock.UtcNow) - _promptOpenedAt;
        var left = RerollRingDuration - elapsed;

        // Clamped at zero rather than allowed to go negative, so "how much ring is left" is never a
        // number a renderer would have to sanitise into an angle of its own.
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void OpenPrompt(DieFaceReading face)
    {
        _promptFace = face;
        _promptOpenedAt = _clock.UtcNow;
        _promptOpen = true;
        _suspendedAt = null;
    }

    private void ClosePromptWithoutRerolling()
    {
        _promptOpen = false;
        _promptFace = null;
        _suspendedAt = null;
    }

    private BoardRollBlock BlockFromSnapshot()
    {
        if (_snapshot is not { } run)
        {
            return BoardRollBlock.NotYetRead;
        }

        // Ordered the way the rules layer orders its own refusals, so the cause this screen names is
        // the cause that would actually have been hit. A run can carry more than one of these at
        // once — a battle opens with a tile still pending — and naming the wrong one would send the
        // player to do something that is not what is blocking them.
        if (run.Phase == RunPhase.Ended)
        {
            return BoardRollBlock.RunEnded;
        }

        if (run.Phase == RunPhase.BattlePending)
        {
            return BoardRollBlock.BattleOpen;
        }

        if (run.DraftPending)
        {
            return BoardRollBlock.DraftOpen;
        }

        if (run.PendingForkJunctionPosition is not null)
        {
            return BoardRollBlock.ForkOpen;
        }

        return run.PendingTileKind != BoardTileKinds.NoPendingTile
            ? BoardRollBlock.TilePending
            : BoardRollBlock.None;
    }

    private async Task<BoardSubmission> SubmitAsync(GameCommand command, CancellationToken ct)
    {
        // Cleared before the command rather than after it. Cleared afterwards it would survive every
        // path that returns early — which is every REFUSAL — and the reroll's sentence would then be
        // printed under the next unrelated refusal.
        RerollExhausted = false;
        _facesFromLastCommand = [];

        var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

        RulesRejection = outcome.Rejection;

        if (!outcome.Accepted)
        {
            return BoardSubmission.RefusedByRules;
        }

        // 🔒 The state comes back with the outcome rather than being read again. A second read would
        // be a second round trip on every tap, and — worse — a window in which the screen draws a
        // run the command has already moved past.
        CarryFaces(outcome.Events);

        if (outcome.State.Run?.ToSnapshot() is { } moved)
        {
            Carry(moved);
        }

        return BoardSubmission.Submitted;
    }

    /// <remarks>
    /// Only the faces one command produced, replacing rather than appending: the caption is "what
    /// you just rolled", and a list that grew across a run would answer a question nobody asked.
    /// </remarks>
    private void CarryFaces(IReadOnlyList<DomainEvent> events)
    {
        _facesFromLastCommand =
            [.. events.OfType<DiceRolled>()
                      .Select(e => new DieFaceReading(e.Sequence, e.Face.Kind.ToString(), e.Face.Value))];

        // The screen-wide list keeps the last faces that EXIST, so acknowledging a tile or choosing
        // a branch does not blank the panel's "last rolled". The per-command list above is what a
        // roll opens its prompt from, so a roll that somehow reported nothing opens no prompt rather
        // than one captioned with the previous roll's face.
        if (_facesFromLastCommand.Count > 0)
        {
            LastRolledFaces = _facesFromLastCommand;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Run is not { } run)
        {
            Stage = BoardStage.RunMissing;
            return;
        }

        Carry(run);
    }

    private void Carry(RunSnapshot run)
    {
        _snapshot = run;

        CurrentHp = run.CurrentHp;
        MaxHp = run.MaxHp;
        Gold = run.Gold;
        Position = run.Position;
        RerollChargesSpent = run.RerollChargesSpentThisStage;

        PendingTile = run.PendingTileKind == BoardTileKinds.NoPendingTile
            ? null
            : new PendingTile(
                run.PendingTileKind,
                BoardTileKinds.NameKeyFor(run.PendingTileKind),
                run.PendingTileLinearIndex,
                run.PendingTileStage);

        Fork = run.PendingForkJunctionPosition is { } junction && run.PendingForkRemainingSteps is { } steps
            ? new ForkPrompt(junction, steps, Branches())
            : null;

        ReadStageShape(run.ChapterId);

        // Settled last, because the two above are what a finished run still has to be able to draw:
        // the screen reports an ended run rather than blanking, and the roll is refused by the block
        // this stage produces rather than by an absent board.
        Stage = run.Phase == RunPhase.Ended ? BoardStage.RunEnded : BoardStage.Ready;
    }

    /// <remarks>See <see cref="TheForkPreviewIsNotReachableHere"/> for why these carry no preview.</remarks>
    private static IReadOnlyList<ForkBranch> Branches() =>
    [
        new ForkBranch(ContinueBranchIndex, ForkContinueActionKey),
        new ForkBranch(ContinueBranchIndex + 1, ForkBranchActionKey),
    ];

    /// <summary>Reads the chapter's authored stage lengths, which is where "stage 2 of 3" comes from.</summary>
    private void ReadStageShape(int chapterId)
    {
        StageCount = null;
        StageLength = null;
        StageTrackIndex = null;

        foreach (var path in _content.DocumentPaths)
        {
            if (!path.StartsWith(ChaptersDirectoryPrefix, StringComparison.Ordinal) ||
                !_content.TryGetDocument(path, out var document))
            {
                continue;
            }

            var root = document!.Root;

            if (!root.TryGetMember(ChapterIdMember, out var id) ||
                id!.Kind != ContentValueKind.Number ||
                id.AsInt32() != chapterId ||
                !root.TryGetMember(StageLengthsMember, out var lengths) ||
                lengths!.Kind != ContentValueKind.Array)
            {
                continue;
            }

            var stageLengths = lengths.Items;

            StageCount = stageLengths.Count;

            if (StageNumber is not { } stage || stage > stageLengths.Count)
            {
                return;
            }

            var length = stageLengths[stage - 1].AsInt32();

            StageLength = length;

            // The real sum of the stages before this one, taken from the chapter's own array. Every
            // earlier length is added; none is assumed equal to any other.
            var preceding = 0;

            for (var earlier = 0; earlier < stage - 1; earlier++)
            {
                preceding += stageLengths[earlier].AsInt32();
            }

            var within = (TrackIndex ?? -1) - preceding;

            StageTrackIndex = within >= 0 && within < length ? within : null;

            return;
        }
    }
}
