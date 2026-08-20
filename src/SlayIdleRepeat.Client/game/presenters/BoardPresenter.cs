using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

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

/// <summary>One entry of the fixed-dice tray: a number the run holds, and how many of it.</summary>
/// <param name="Pips">The number written on the die, 1-6, and what <c>USE_FIXED_DIE</c> carries.</param>
/// <param name="Count">How many dice showing that number the run holds — at least one.</param>
/// <remarks>
/// 🔒 A count rather than one entry per die, because that is what a run persists and two dice
/// showing a 3 are genuinely the same holding twice. A list would put an order into the tray that
/// nothing means, and the player would be choosing between identical controls.
/// </remarks>
public sealed record HeldFixedDie(int Pips, int Count);

/// <summary>The tile the run is standing on and has not resolved.</summary>
/// <param name="Kind">The kind, as the run's own integer.</param>
/// <param name="NameKey">Its caption key, or null when the number names no kind this build knows.</param>
/// <param name="LinearIndex">The node index it sits at — exact, unlike <see cref="BoardPresenter.Position"/>.</param>
/// <param name="Stage">The stage it belongs to, 1-3, or 0 for the boss node.</param>
public sealed record PendingTile(int Kind, string? NameKey, int LinearIndex, int Stage);

/// <summary>One branch a paused junction offers, with the preview the generator authored for it.</summary>
/// <param name="BranchIndex">What <c>CHOOSE_FORK</c> carries for it.</param>
/// <param name="CaptionKey">The caption key describing where it goes.</param>
/// <param name="Label">
/// The bias label this branch's tiles were drawn under, or null for the edge that keeps to the
/// spine — which was drawn under no bias, so naming one for it would be inventing a preview.
/// </param>
/// <param name="Icons">
/// The branch's own first tiles in walk order, at most three, or empty for the spine edge.
/// </param>
/// <remarks>
/// 🔒 The label and the icons are two INDEPENDENT reads of the same branch, and neither is derived
/// from the other: the label is the bias the draw ran under and the icons are what the draw actually
/// produced. A screen showing icons computed from the label would be showing the intention rather
/// than the board, and the two differ every time the weighted draw does not go the label's way.
/// </remarks>
public sealed record ForkBranch(
    int BranchIndex,
    string CaptionKey,
    ForkLabel? Label = null,
    IReadOnlyList<TileKind>? Icons = null);

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
/// 🔒 <b>The board is here, whole, and that is the largest thing to know about this screen.</b>
/// <see cref="BoardView.Project"/> regenerates the run's graph from its seed and hands back every
/// node of the track with the tile that sits on it, plus each junction's real branch preview. `16`
/// D42 makes the whole of it visible at all times — no fog, no preview range, no reveal distance —
/// because a die that only answers a number is only an interesting decision if the player can see
/// what the numbers reach, and that is the entire reason a FIXED die is worth choosing a number for.
/// So this screen draws the track, not a progress strip: the three paragraphs that used to stand
/// here saying the board was unreadable are gone with the thing they described.
/// </para>
/// <para>
/// 🔴 What is still absent from the track: the burning-tile marks of a Chapter 4 hazard (`03`
/// §4.1), which no projected node carries, and any mark for a node already walked — a run records
/// where it IS, not where it has been, so a visited node is indistinguishable from one ahead.
/// </para>
/// <para>
/// ⚠️ <b>A roll is one tap and is final.</b> There is no reroll and no acceptance window: the die
/// is an ordinary 1..6, <c>ROLL_DICE</c> answers with the number, the movement and the landing
/// together, and the board decides what the landing means. What the screen shows afterwards is the
/// number that was rolled.
/// </para>
/// <para>
/// 🔒 <b>There are TWO ways to move, and this screen offers both.</b> A fixed die (`04` §6) is
/// spent instead of rolling and moves the hero exactly its own number. It is not a reroll — nothing
/// is re-drawn — so it is offered as its own control rather than as something done to a roll, and it
/// is gated by exactly the same <see cref="RollBlock"/>: the rules layer refuses both movement
/// commands from the same states, so a screen that offered one where the other was refused would be
/// promising a way out of a state the game has none of.
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
    private const string ForkPerilousLabelKey = "loc.board.fork_perilous.label";
    private const string ForkShelteredLabelKey = "loc.board.fork_sheltered.label";
    private const string ForkArcaneLabelKey = "loc.board.fork_arcane.label";
    private const string ForkFeralLabelKey = "loc.board.fork_feral.label";
    private const string FixedDiceHeldLabelKey = "loc.board.dice_held.label";
    private const string FixedDiceChooseLabelKey = "loc.board.dice_choose.label";
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
    private BoardView? _board;

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

    /// <summary>Every node of the run's board in walk order, boss last — the whole track.</summary>
    /// <remarks>
    /// 🔒 The WHOLE track, never a window on it (`16` D42). Empty only when the board could not be
    /// projected at all, which is a chapter this build does not ship rather than a run mid-move.
    /// ⚠️ The spine, so a fork's branch nodes are not in this list: they hang off a junction and
    /// share their linear index with the spine node level with them, so laying them out in one row
    /// would put two tiles on one step. <see cref="Fork"/> is where a branch's own tiles are read.
    /// </remarks>
    public IReadOnlyList<BoardTrackNode> Track => _board?.Spine ?? [];

    /// <summary>The node the run is standing on, or null while it is at the trailhead.</summary>
    public BoardTrackNode? StandingOn => _board?.StandingOn;

    /// <summary>
    /// How far along the track the run stands, or null while it stands on no node of this board.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Exact, and no longer a guess withheld.</b> This used to read the pending tile, because
    /// that was the only field carrying a linear index — so between resolving one tile and landing on
    /// the next it had to answer null rather than place the token at a plausible node. The projected
    /// board answers it from the run's actual position for every node, spine or branch, so the window
    /// in which the screen did not know where the player was is closed rather than merely narrowed.
    /// </remarks>
    public int? TrackIndex => StandingOn?.LinearIndex;

    /// <summary>The stage the run is in, 1-3, or null while it is on no stage of this chapter.</summary>
    /// <remarks>
    /// Read off the node the run stands on, so it is known between tiles as well. The boss node
    /// belongs to no stage and reports 0, which is why the range is stated rather than assumed.
    /// </remarks>
    public int? StageNumber => StandingOn is { Stage: >= 1 and <= 3 } node ? node.Stage : null;

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
    /// <remarks>
    /// ⚠️ Read from the chapter document rather than counted off <see cref="Track"/>, and kept for
    /// the caption rather than for the track: it is the authored length, so a generated board of a
    /// different length is a disagreement worth being able to see rather than one to paper over.
    /// </remarks>
    public int? StageLength { get; private set; }

    /// <summary>The tile the run is standing on and has not resolved, or null when none is pending.</summary>
    public PendingTile? PendingTile { get; private set; }

    /// <summary>
    /// The fixed dice the run owns, ascending by number — one entry per distinct number, carrying how
    /// many of it are held (`04` §6.3).
    /// </summary>
    /// <remarks>
    /// 🔒 Ordered here rather than in the scene. The run persists a multiset, whose enumeration
    /// order is the dictionary's and therefore an accident of insertion — so a tray drawn straight
    /// off it would re-order itself whenever a die was granted or spent, and the control under the
    /// player's thumb would not be the one they were looking at. Uncapped, so this list has no
    /// authored maximum length; the scene lays it out as a row that wraps.
    /// </remarks>
    public IReadOnlyList<HeldFixedDie> FixedDice { get; private set; } = [];

    /// <summary>How many fixed dice have been granted whose number the player has not named yet.</summary>
    /// <remarks>
    /// ⚠️ A count, not a queue: every owed choice is the same offer, so there is nothing to tell one
    /// from another. It blocks nothing (`04` §6.2) — the roll stays live with one outstanding.
    /// </remarks>
    public int PendingFixedDieChoices { get; private set; }

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

    /// <summary>The heading over the tray of dice the run owns, resolved.</summary>
    public string FixedDiceLabel => _strings.Resolve(FixedDiceHeldLabelKey);

    /// <summary>The prompt asking the player to name a granted die's number, resolved.</summary>
    public string FixedDieChoiceLabel => _strings.Resolve(FixedDiceChooseLabelKey);

    /// <summary>Whether the tray has anything in it to draw.</summary>
    public bool FixedDiceOffered => Stage == BoardStage.Ready && FixedDice.Count > 0;

    /// <summary>Whether the screen should be asking the player to name a number.</summary>
    public bool FixedDieChoiceOffered => Stage == BoardStage.Ready && PendingFixedDieChoices > 0;

    /// <summary>One branch's bias label, resolved — empty for the edge that keeps to the spine.</summary>
    /// <param name="branch">The branch to label.</param>
    /// <exception cref="ArgumentNullException"><paramref name="branch"/> is null.</exception>
    public string BranchLabelText(ForkBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        // Empty rather than a fallback word for the spine edge: it was drawn under no bias, so there
        // is no label the generator authored for it and inventing one would be a preview of nothing.
        return branch.Label is { } label ? _strings.Resolve(LabelKeyFor(label)) : NothingLeftToSay;
    }

    /// <summary>A tile kind's name, resolved — empty for a number this build knows no name for.</summary>
    /// <param name="tile">The tile kind to name.</param>
    public string TileName(TileKind tile) =>
        BoardTileKinds.NameKeyFor((int)tile) is { } key ? _strings.Resolve(key) : NothingLeftToSay;

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
    /// Spends one fixed die, moving the hero exactly its own number instead of rolling.
    /// </summary>
    /// <param name="pips">The number on the die to spend.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// 🔒 Gated on <see cref="RollBlock"/>, the same gate the roll takes, because the rules layer
    /// refuses both movement commands from the same three states. It is ALSO checked against the
    /// tray, so a number the run does not hold is never submitted: that refusal comes back as the
    /// same wire value as the four the block already tells apart, and spending a command to learn
    /// something the screen already knows would leave the player reading the generic sentence.
    /// </remarks>
    public async Task<BoardSubmission> UseFixedDieAsync(int pips, CancellationToken ct)
    {
        CancelAbandon();

        if (RollBlock != BoardRollBlock.None || !FixedDice.Any(held => held.Pips == pips))
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new UseFixedDieCommand(pips), ct).ConfigureAwait(false);
    }

    /// <summary>Names the number on one granted fixed die.</summary>
    /// <param name="pips">The number the player chose, 1-6.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// ⚠️ NOT gated on <see cref="RollBlock"/>, and that is the difference between this and every
    /// other control here: a grant can land while a tile is unresolved or a battle is open, and
    /// naming a number moves nothing. Refusing it until the board was clear would leave the player
    /// holding a reward they cannot open, in the states they most want to open it.
    /// </remarks>
    public async Task<BoardSubmission> ChooseFixedDieAsync(int pips, CancellationToken ct)
    {
        CancelAbandon();

        if (Stage != BoardStage.Ready || PendingFixedDieChoices <= 0 || !Die.IsPips(pips))
        {
            return BoardSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ChooseFixedDieCommand(pips), ct).ConfigureAwait(false);
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

        // Projected BEFORE the fork and the stage shape are settled, because both read it.
        _board = ProjectBoard(run);

        Fork = run.PendingForkJunctionPosition is { } junction && run.PendingForkRemainingSteps is { } steps
            ? new ForkPrompt(junction, steps, Branches())
            : null;

        FixedDice = run.FixedDice is { Count: > 0 } held
            ? held.OrderBy(entry => entry.Key)
                  .Select(entry => new HeldFixedDie(entry.Key, entry.Value))
                  .ToArray()
            : [];

        PendingFixedDieChoices = run.PendingFixedDieChoices;

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

    /// <summary>
    /// Regenerates the run's board out of its seed, or null when this build cannot draw one.
    /// </summary>
    /// <remarks>
    /// 🔒 <b><see cref="MissingContentException"/> and nothing else is caught.</b> It means one
    /// thing — the run names a chapter this content set does not carry — and it is reachable from a
    /// saved run whose chapter was removed, so a screen that let it through would fail to open a
    /// board the player can otherwise still abandon. Every OTHER failure is a malformed chapter
    /// document, and swallowing those would draw a boardless board over a content bug that every
    /// content gate in CI is built to make loud.
    /// </remarks>
    private BoardView? ProjectBoard(RunSnapshot run)
    {
        try
        {
            return BoardView.Project(run, _content);
        }
        catch (MissingContentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The two edges a junction offers, the second carrying the branch's authored preview.
    /// </summary>
    /// <remarks>
    /// 🔒 The preview is the generator's own: <see cref="BoardFork.BranchLabel"/> is the bias the
    /// branch's tiles were drawn under and <see cref="BoardFork.BranchIcons"/> are the tiles that
    /// draw actually produced. The spine edge carries neither, because it was drawn under no bias —
    /// the board offers a preview of the side path and of nothing else, so that is what is shown.
    /// ⚠️ Both edges still fall back to their structural caption when the junction is not one this
    /// board knows, which is the state a fork paused at a position off the projected graph leaves.
    /// </remarks>
    private IReadOnlyList<ForkBranch> Branches()
    {
        var preview = _board?.PendingFork;

        return
        [
            new ForkBranch(ContinueBranchIndex, ForkContinueActionKey),
            new ForkBranch(
                ContinueBranchIndex + 1,
                ForkBranchActionKey,
                preview?.BranchLabel,
                preview?.BranchIcons ?? []),
        ];
    }

    /// <summary>The loc key for one fork bias label.</summary>
    /// <remarks>
    /// A total switch over the four rather than a name-derived key, so adding a fifth label upstream
    /// is a compile-time hole here instead of a key that resolves to nothing at run time.
    /// </remarks>
    private static string LabelKeyFor(ForkLabel label) => label switch
    {
        ForkLabel.Perilous => ForkPerilousLabelKey,
        ForkLabel.Sheltered => ForkShelteredLabelKey,
        ForkLabel.Arcane => ForkArcaneLabelKey,
        ForkLabel.Feral => ForkFeralLabelKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(label), label, "03 §3.1 gives a fork four bias labels and this is none of them."),
    };

    /// <summary>Reads the chapter's authored stage lengths, which is where "stage 2 of 3" comes from.</summary>
    private void ReadStageShape(int chapterId)
    {
        StageCount = null;
        StageLength = null;

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

            StageLength = stageLengths[stage - 1].AsInt32();

            return;
        }
    }
}
