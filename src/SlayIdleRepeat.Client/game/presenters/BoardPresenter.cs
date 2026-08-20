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

    /// <summary>
    /// The run has finished. Escaped by leaving, which the run-end screen now does: an accepted
    /// <c>END_RUN</c> frees the board and brings the starting menu back, so this state is what a board
    /// would draw on the way out rather than a state a player is left sitting in.
    /// </summary>
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
/// ⚠️ <b>A roll is one tap and is final.</b> There is no reroll and no acceptance window: the die
/// is an ordinary 1..6, <c>ROLL_DICE</c> answers with the number, the movement and the landing
/// together, and the board decides what the landing means. What the screen shows afterwards is the
/// number that was rolled.
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

    private const string HpLabelKey = "loc.board.hp.label";
    private const string GoldLabelKey = "loc.board.gold.label";
    private const string StageLabelKey = "loc.board.stage.label";
    private const string RolledLabelKey = "loc.board.rolled.label";
    private const string StandingOnLabelKey = "loc.board.standing_on.label";
    private const string RollActionKey = "loc.board.roll.action";
    private const string ResolveActionKey = "loc.board.resolve.action";
    private const string AbandonActionKey = "loc.board.abandon.action";
    private const string AbandonConfirmActionKey = "loc.board.abandon_confirm.action";
    private const string ForkNameKey = "loc.board.fork.name";
    private const string ForkContinueActionKey = "loc.board.fork_continue.action";
    private const string ForkBranchActionKey = "loc.board.fork_branch.action";
    private const string LoadingStatusKey = "loc.board.loading.status";
    private const string RunMissingStatusKey = "loc.board.run_missing.status";
    private const string RunEndedStatusKey = "loc.board.run_ended.status";
    private const string UnavailableStatusKey = "loc.board.unavailable.status";
    private const string RefusedStatusKey = "loc.board.refused.status";
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
    private readonly PlayerId _player;
    private readonly RunId _run;

    private RunSnapshot? _snapshot;

    /// <summary>Builds the screen over the host, the strings, the content set and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the chapter's stage lengths are read from.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <remarks>
    /// ⚠️ No clock. This screen used to take one to measure the reroll's 4-second acceptance ring
    /// against; with no reroll there is no window, so nothing here is time-dependent.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public BoardPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player,
        RunId run)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;
        _run = run;
    }

    /// <summary>How far the read this screen depends on has got.</summary>
    public BoardStage Stage { get; private set; } = BoardStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>The hero's current hit points, as the run carries them.</summary>
    public int CurrentHp { get; private set; }

    /// <summary>
    /// Whether this run has reached its end and has NOT been closed yet — the moment S13/S14 owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Told apart from <see cref="BoardStage.RunEnded"/>, which is a run already CLOSED.</b> A
    /// run whose hero is dead or whose Boss is dead is over, but nothing has been banked until
    /// <c>END_RUN</c> runs — and <c>END_RUN</c> is submitted from the run-end screen this flags the way
    /// to. A board that could not tell the two apart would either hand a player to a results screen for
    /// a payout already taken, or leave them on the board with a dead hero and a roll button.
    /// </para>
    /// <para>
    /// ⚠️ <b>It says the run is over, never WHY.</b> Which of <c>02</c> §6's outcomes this is — and what it
    /// pays — is <c>RunEndView</c>'s, projected on the screen that draws it. A second derivation here
    /// would be a second answer to "was this a victory", and the two would part company the first time
    /// either moved.
    /// </para>
    /// </remarks>
    public bool RunAwaitingResults { get; private set; }

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

    /// <summary>The number the last accepted roll came up, or null until one has been accepted.</summary>
    /// <remarks>
    /// One number, not a list: a roll is one draw off an ordinary die. It survives commands that
    /// roll nothing — acknowledging a tile or choosing a branch does not blank "what you rolled".
    /// </remarks>
    public int? LastRolledPips { get; private set; }

    /// <summary>Why the roll is not live, decided from the run's own state.</summary>
    public BoardRollBlock RollBlock =>
        Stage switch
        {
            BoardStage.NotYetRead => BoardRollBlock.NotYetRead,
            BoardStage.RunEnded => BoardRollBlock.RunEnded,
            BoardStage.Ready => BlockFromSnapshot(),
            _ => BoardRollBlock.NotYetRead,
        };

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

    /// <summary>The tile acknowledgement's caption, resolved.</summary>
    public string ResolveText => _strings.Resolve(ResolveActionKey);

    /// <summary>
    /// The abandon control's caption, resolved — and it changes to the confirmation once armed.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Two presses, not a modal.</b> Abandoning throws the run away and pays a tenth of what it
    /// banked, so a single mis-tap must not be able to do it — and `13` §11 forbids a full-screen
    /// blocking dialog during a run. A control that states the consequence on itself and needs a
    /// second press is the strongest confirmation this build can give without inventing a mechanism.
    /// </remarks>
    public string AbandonText =>
        _strings.Resolve(AbandonArmed ? AbandonConfirmActionKey : AbandonActionKey);

    /// <summary>Whether the abandon control is showing its confirmation rather than its offer.</summary>
    public bool AbandonArmed { get; private set; }

    /// <summary>
    /// 🔒 Whether the run can be abandoned right now — <b>true from every state a live run can stand
    /// in</b>.
    /// </summary>
    /// <remarks>
    /// It is not gated on <see cref="RollBlock"/>, and that is the whole point: `16` D39 makes
    /// <c>ABANDON_RUN</c> legal mid-battle, mid-draft, at a paused junction and on an unresolved
    /// tile, and this control is the only surface that reaches it. A player standing on a tile whose
    /// screen this build has not written — an Event, a Minigame — has no other way out at all, so a
    /// control gated on the same block that stranded them would strand them again.
    /// </remarks>
    public bool AbandonOffered => Stage == BoardStage.Ready;

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
    /// ⚠️ One sentence for every refusal. The exhausted reroll used to get its own — it was the
    /// only refusal on this screen a player could plan around — and it is gone with the reroll. The
    /// reason's identity is still carried by <see cref="RulesRejection"/> for the log.
    /// </remarks>
    public string RejectionText =>
        RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

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

    /// <summary>
    /// Arms the abandon control, or — once armed — submits <c>ABANDON_RUN</c>.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// One method for both presses rather than an Arm and an Abandon, because the caller is one
    /// control: two methods would leave the screen deciding which press it is on, which is the state
    /// this presenter already holds.
    /// </remarks>
    public async Task<BoardSubmission> AbandonRunAsync(CancellationToken ct)
    {
        if (!AbandonOffered)
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        if (!AbandonArmed)
        {
            AbandonArmed = true;

            return BoardSubmission.RefusedNotAvailable;
        }

        AbandonArmed = false;

        return await SubmitAsync(new AbandonRunCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disarms the abandon control — every other action on this screen calls it.
    /// </summary>
    /// <remarks>
    /// 🔒 An armed confirmation that survived a roll would sit there through the rest of the run,
    /// one stray press from ending it. Rolling, choosing a fork or resolving a tile all say the
    /// player has moved on.
    /// </remarks>
    public void CancelAbandon() => AbandonArmed = false;

    /// <summary>Submits <c>ROLL_DICE</c>, and nothing at all when something is in the way.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> RollAsync(CancellationToken ct)
    {
        CancelAbandon();

        if (RollBlock != BoardRollBlock.None)
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new RollDiceCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>Submits <c>CHOOSE_FORK</c> for one of the branches on offer.</summary>
    /// <param name="branchIndex">The chosen branch's index, as the fork prompt lists it.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> ChooseForkAsync(int branchIndex, CancellationToken ct)
    {
        CancelAbandon();

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

    /// <summary>
    /// Whether the pending tile is one that is left by FIGHTING it rather than by resolving it.
    /// </summary>
    /// <remarks>
    /// 🔒 Asked of the battle screen rather than answered here, so the three tile numbers have one
    /// home — the same way this screen asks <c>ShopPresenter</c> and <c>CampfirePresenter</c> about
    /// theirs.
    /// </remarks>
    public bool PendingTileOpensAFight =>
        PendingTile is { } tile && BattleReplayPresenter.OpensAFight(tile.Kind);

    /// <summary>
    /// Acts on the tile the run is standing on, with the command that tile is actually left by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS BRANCH IS THE FIX FOR A RUN THAT COULD NOT FIGHT.</b> It submitted
    /// <c>RESOLVE_TILE</c> unconditionally, and <c>Handlers.ResolveTile</c> says of Enemy, Elite and Boss
    /// that they are *"acknowledged and not cleared"* — so on a fight tile the command was ACCEPTED,
    /// cleared nothing, and left the board redrawing the identical state. No rejection, no error, no
    /// fight: the run was stuck on that tile permanently. Found by playing an exported build, on an
    /// enemy tile at position 20, and confirmed by grep: <c>START_BATTLE</c> had no caller anywhere in
    /// the client.
    /// </para>
    /// <para>
    /// 🔒 <c>START_BATTLE</c> is the whole fix, because everything after it already worked: it moves
    /// the run to <c>RunPhase.BattlePending</c>, which is the state the board already watches for and
    /// already opens the replay screen on. Nothing new was needed downstream — only the command that
    /// gets a run into a fight.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public async Task<BoardSubmission> ResolvePendingTileAsync(CancellationToken ct)
    {
        CancelAbandon();

        if (Stage != BoardStage.Ready || PendingTile is null)
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        return PendingTileOpensAFight
            ? await SubmitAsync(new StartBattleCommand(), ct).ConfigureAwait(false)
            : await SubmitAsync(new ResolveTileCommand(), ct).ConfigureAwait(false);
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
        var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

        RulesRejection = outcome.Rejection;

        if (!outcome.Accepted)
        {
            return BoardSubmission.RefusedByRules;
        }

        // 🔒 The state comes back with the outcome rather than being read again. A second read would
        // be a second round trip on every tap, and — worse — a window in which the screen draws a
        // run the command has already moved past.
        CarryRoll(outcome.Events);

        if (outcome.State.Run?.ToSnapshot() is { } moved)
        {
            Carry(moved);
        }

        return BoardSubmission.Submitted;
    }

    /// <remarks>
    /// The number is kept only when a command actually reported one, so acknowledging a tile or
    /// choosing a branch does not blank "what you rolled". Replacing rather than appending: the
    /// caption is what you just rolled, and a history would answer a question nobody asked.
    /// </remarks>
    private void CarryRoll(IReadOnlyList<DomainEvent> events)
    {
        if (events.OfType<DiceRolled>().LastOrDefault() is { } rolled)
        {
            LastRolledPips = rolled.Pips;
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

        // Read off the two facts that end a run, and only while it is still open: a hero at zero hit
        // points (02 §6) or a dead Boss. A closed run is excluded because its rewards are already
        // banked — END_RUN has run — and a results screen opened over one would show a tally for a
        // payout the player has had.
        RunAwaitingResults = run.Phase != RunPhase.Ended && (run.CurrentHp == 0 || run.BossDefeated);
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
