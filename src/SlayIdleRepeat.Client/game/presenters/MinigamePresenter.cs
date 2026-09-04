using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Minigame screen has got with the game its tile offers.</summary>
public enum MinigameStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run is on a Minigame tile and the game is the player's to play.</summary>
    Playing = 2,

    /// <summary>A submission was accepted and the tile is cleared. What was won is on the screen.</summary>
    Resolved = 3,

    /// <summary>The run was read and is standing on some other tile. The screen is open on the wrong one.</summary>
    NotAtAMinigame = 4,

    /// <summary>
    /// This run has already resolved a minigame at the node it stands on. One submission per tile,
    /// so there is nothing left to play and nothing left to press but the way back.
    /// </summary>
    AlreadyResolved = 5,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 6,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 7,

    /// <summary>
    /// The content set cannot describe this minigame — its reward table or its authored numbers are
    /// not readable. A sentence, not a crash.
    /// </summary>
    RulesUnavailable = 8,
}

/// <summary>What one submission from this screen did.</summary>
public enum MinigameSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules layer refused it.</summary>
    RefusedByRules = 3,

    /// <summary>
    /// 🔒 The call itself did not complete. Told apart from <see cref="RefusedByRules"/> because a
    /// refusal is an answer about the game and a fault is the game not answering.
    /// </summary>
    HostUnavailable = 4,
}

/// <summary>What the one control this screen carries does when it is pressed.</summary>
/// <remarks>
/// 🔴 <b>Every state this screen can settle in answers this with something other than nothing,
/// except the one where the player still has a game to play.</b> The screen is opened by a routing
/// table mid-run, so a settled state it cannot act in and cannot leave was a run that could only be
/// left by killing the application. Copied deliberately from <see cref="EventExit"/>, whose own
/// remarks record why the faulted read is the one state that re-reads rather than handing back.
/// </remarks>
public enum MinigameExit
{
    /// <summary>Nothing, and the control is drawn out of use — the game is still being played.</summary>
    Nowhere = 1,

    /// <summary>
    /// 🔒 Reads the run again, in place. A read that never answered knows nothing about the run, and
    /// handing back would give the board a decision it has already latched.
    /// </summary>
    ReadAgain = 2,

    /// <summary>Hands back to the board, which re-reads the run and routes it wherever it now belongs.</summary>
    ToTheBoard = 3,
}

/// <summary>
/// Drives the Minigame screen: the tile offers one of three games, the player plays it, and
/// <c>MINIGAME_SUBMIT</c> pays the outcome tier and clears the tile.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The two authority arms are different screens behind one presenter.</b> The timing bar is
/// client-asserted: the tier is the hit count and the submission carries it, so it may only be sent
/// once the game is finished — a submission mid-game would claim a tier the player has not earned and
/// the rules layer would take it. The chest pick and the dice duel are server-rolled: they submit a
/// tier of zero, which the handler ignores, and the tier they actually got comes back on
/// <c>MinigameResolved</c>. No persisted field carries it, so a screen that reported its own claim
/// would tell the player they won something the server never paid.
/// </para>
/// <para>
/// 🔒 <b>Which arm a tile offers is <see cref="MinigameChoice"/>'s, and it is unenforced.</b> The id
/// arrives as a constructor argument rather than being decided here, so the one place the pick is
/// made is the one place its remarks describe what it is worth.
/// </para>
/// <para>
/// 🔒 <b>The guarantee line is the chest pick's alone.</b> The other three minigames are skill-scaled
/// and carry no pity counter at all, so a countdown drawn on them would be a promise nothing keeps.
/// </para>
/// <para>
/// The screen's strings are the <c>loc.minigame.*</c> group, authored in
/// <c>content/minigame/minigame.json</c> and named the way the sibling screens name theirs: a
/// camelCase member becomes a snake_case key with its group as the suffix
/// (<c>label.strikesLeft</c> → <c>loc.minigame.strikes_left.label</c>). The outcome captions are the
/// one derived group — an authored outcome token lower-cased into
/// <c>loc.minigame.&lt;token&gt;.outcome</c> — so a thirteenth reward row cannot ship without one.
/// </para>
/// </remarks>
public sealed class MinigamePresenter
{
    /// <summary>The tile kind a minigame is, as the run reports it.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed: the numbering is public, so a kind
    /// inserted above this one renumbers the constant with it rather than leaving a literal pointing
    /// at whatever moved into its slot.
    /// </remarks>
    public const int MinigameTileKind = (int)TileKind.Minigame;

    /// <summary>What <see cref="ResolvedTier"/> reads before any outcome has been paid.</summary>
    /// <remarks>
    /// Negative rather than zero, because zero is a real tier — the lowest one, and the one the
    /// server-rolled arms submit as a placeholder. A sentinel a game can also mean would report the
    /// worst outcome on a screen that has resolved nothing.
    /// </remarks>
    public const int NoTierYet = -1;

    /// <summary>The tier a server-rolled submission carries, which the handler never reads.</summary>
    public const int ClaimIgnoredByTheServer = 0;

    /// <summary>Builds the screen over the host, the strings, the content set, the run and one arm.</summary>
    /// <param name="gameHost">The seam the run is read through and its command is submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the reward rows and the authored numbers come from.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <param name="minigameId">Which arm this tile offers — <see cref="MinigameChoice"/>'s answer.</param>
    /// <param name="reducedMotion">
    /// Whether the timing bar is played by stepping rather than by timing. Defaulted false and
    /// nothing detects it yet, matching the two other screens that take the same flag; the settings
    /// screen that would remember it is unbuilt.
    /// </param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is blank.</exception>
    public MinigamePresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player,
        RunId run,
        string minigameId,
        bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(minigameId);

        throw new NotImplementedException(NotBuiltYet);
    }

    /// <summary>Which arm this tile offers.</summary>
    public string MinigameId => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether the timing bar is being played by stepping rather than by timing.</summary>
    public bool ReducedMotion => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How far the screen has got with the game.</summary>
    public MinigameStage Stage => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether the tile has cleared, so Continue may return to the board.</summary>
    public bool CanLeave => throw new NotImplementedException(NotBuiltYet);

    /// <summary>What the one control this screen carries does when it is pressed.</summary>
    public MinigameExit Exit => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether this arm's outcome is the server's to draw.</summary>
    public bool IsServerRolled => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Every outcome tier this arm pays, chapter-scaled, in ascending tier order.</summary>
    public IReadOnlyList<MinigameTierRow> Rows => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Where the timing bar's cursor stands, 0 to 1. Meaningless on the other two arms.</summary>
    public double Cursor => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Half the timing bar's scoring window, so the scene draws the window it is judged by.</summary>
    public double HitWindowHalfWidth => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How many timing-bar strikes have scored.</summary>
    public int Hits => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How many timing-bar strikes are left.</summary>
    public int StrikesLeft => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether the timing bar has been played out, so a submission carries a settled tier.</summary>
    public bool Finished => throw new NotImplementedException(NotBuiltYet);

    /// <summary>
    /// The tier that was actually paid, or <see cref="NoTierYet"/> before anything resolved.
    /// </summary>
    /// <remarks>
    /// 🔒 On the server-rolled arms this comes off the resolution event and nowhere else. The claim
    /// the command carried is not an answer, and no persisted field records the draw.
    /// </remarks>
    public int ResolvedTier => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The authored outcome token that was paid, empty before anything resolved.</summary>
    public string ResolvedOutcome => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => throw new NotImplementedException(NotBuiltYet);

    /// <summary>This arm's own name, resolved.</summary>
    public string ArmName => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How this arm is played, in one sentence, resolved.</summary>
    public string RuleText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The heading the reward rows are listed under, resolved.</summary>
    public string RewardsLabel => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The caption on the hit count, resolved.</summary>
    public string HitsLabel => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The caption on the strikes remaining, resolved.</summary>
    public string StrikesLeftLabel => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The caption on the guarantee line, resolved.</summary>
    public string GuaranteeLabel => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The heading the resolved outcome is shown under, resolved.</summary>
    public string ResultLabel => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The timing bar's strike action, resolved.</summary>
    public string StrikeText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The reduced-motion step action, resolved.</summary>
    public string StepText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The chest pick's action, resolved.</summary>
    public string PickChestText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The dice duel's action, resolved.</summary>
    public string RollText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The one action that leaves this screen, resolved.</summary>
    public string ContinueText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>
    /// The chest pick's guarantee in words, naming both counter numbers. Empty on the other arms.
    /// </summary>
    /// <remarks>
    /// 🔒 Both numbers, because either alone is unreadable: "you have missed three" says nothing
    /// about when the guarantee lands, and "one more chest" says nothing about the streak it is
    /// counting — and a player watching a pity counter is watching precisely the pair.
    /// </remarks>
    public string GuaranteeText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The line saying what the screen is doing while it has something to say, resolved.</summary>
    public string StatusText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    public string RejectionText => throw new NotImplementedException(NotBuiltYet);

    /// <summary>One authored outcome token as a player reads it, resolved.</summary>
    /// <param name="outcome">The token, as the reward table authors it.</param>
    public string OutcomeText(string outcome) => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Moves the timing bar's clock forward. Does nothing on the other two arms.</summary>
    /// <param name="delta">Seconds since the last frame. Never negative.</param>
    public void Advance(double delta) => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Moves the timing bar's cursor one authored step — the reduced-motion way to aim.</summary>
    public void Step() => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Takes one timing-bar strike, and answers whether it scored.</summary>
    public bool Strike() => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Reads the run and settles the screen on the game it is standing on.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct) => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Submits <c>MINIGAME_SUBMIT</c> for this arm.</summary>
    /// <remarks>
    /// 🔒 Refused by this screen while the timing bar is unfinished: the tier is the hit count, and a
    /// game submitted early claims a tier the player has not played for.
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public Task<MinigameSubmission> SubmitAsync(CancellationToken ct) =>
        throw new NotImplementedException(NotBuiltYet);

    private const string NotBuiltYet =
        "MinigamePresenter is a signature-only stub: the read, the three arms and the submission " +
        "land with the tests written against this surface.";
}
