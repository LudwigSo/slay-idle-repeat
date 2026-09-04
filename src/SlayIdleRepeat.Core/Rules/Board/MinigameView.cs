using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The minigame a pending Minigame tile offers, projected read-only so its screen can show what
/// each outcome tier pays before <c>MINIGAME_SUBMIT</c> resolves one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The reward preview and the reward payout are one number.</b> The rows are the chapter-scaled
/// table the handler pays from, and their Gold goes through the same run-modifier scaling the handler
/// applies at the income site — a screen that showed the unscaled figure would promise a cursed run
/// more than it is about to receive.
/// </para>
/// <para>
/// 🔒 <b>The guarantee numbers are asked of the guarantee rule, never re-derived here.</b> The
/// ordinal arithmetic is <c>misses &gt;= everyNth - 1</c> and both readings of the authored key have
/// been wrong in this repository before, so a second statement of it in a projection is a second
/// place for the counter a player is watching to disagree with the one the server keeps.
/// </para>
/// <para>
/// 🔒 <b>Read-only.</b> Projecting mutates no <c>Run</c> and moves no stream position: the
/// server-rolled draw belongs to the command.
/// </para>
/// </remarks>
public sealed class MinigameView
{
    /// <summary>The known minigame ids, read off the rules layer's own catalogue.</summary>
    /// <remarks>
    /// The catalogue is internal and must not leak, so the ids are copied out rather than the type.
    /// A client picking which arm to open reads this list rather than transcribing four literals.
    /// </remarks>
    public static IReadOnlyList<string> Ids => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether the server draws this minigame's outcome and ignores the client's claim.</summary>
    public bool IsServerRolled => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether this run has already resolved a minigame at the node it is standing on.</summary>
    /// <remarks>
    /// The legality gate is per tile instance rather than per minigame, so a screen that offered a
    /// second submission here would be offering a press the rules layer refuses.
    /// </remarks>
    public bool AlreadyResolvedHere => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Every outcome tier this minigame authors, chapter-scaled, in ascending tier order.</summary>
    public IReadOnlyList<MinigameTierRow> Rows => throw new NotImplementedException(NotBuiltYet);

    /// <summary>
    /// The chest pick's gold-tier guarantee as it stands for this player, or <c>null</c> for the
    /// three minigames that carry no counter at all.
    /// </summary>
    public MinigameGuaranteeView? Guarantee => throw new NotImplementedException(NotBuiltYet);

    /// <summary>
    /// Projects one minigame's offer, or <c>null</c> when the run's pending tile is not a Minigame.
    /// </summary>
    /// <param name="run">The run standing on the tile.</param>
    /// <param name="player">The profile whose wallet and pity counters the offer is read against.</param>
    /// <param name="content">The loaded content set the reward tables and the guarantee are read from.</param>
    /// <param name="minigameId">Which of <see cref="Ids"/> is being offered.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> names no known minigame.</exception>
    public static MinigameView? Project(
        RunSnapshot run, PlayerSnapshot player, ContentSnapshot content, string minigameId) =>
        throw new NotImplementedException(NotBuiltYet);

    private const string NotBuiltYet =
        "MinigameView is a signature-only stub: the Minigame screen's tests are written against " +
        "this surface and the projection itself lands with them.";
}

/// <summary>One outcome tier as a screen has to draw it, already scaled to the run's chapter.</summary>
/// <param name="Tier">The zero-based tier index a submission carries for this row.</param>
/// <param name="Outcome">The authored outcome token — the name the reward table gives this row.</param>
/// <param name="Gold">
/// What the run receives, <b>after</b> the run's own Gold modifiers. 🔒 The one column a screen can
/// get wrong without any document disagreeing, because the table's figure and the payout differ on
/// every run carrying a shrine buff or a curse.
/// </param>
/// <param name="Crowns">What the profile's wallet receives.</param>
/// <param name="BeastFeed">What the profile's wallet receives.</param>
/// <param name="EnhanceStones">What the profile's wallet receives.</param>
/// <param name="FixedDice">
/// How many fixed dice this row grants. 🔒 Not chapter-scaled, and the only column that is not: a
/// fixed die is one die whatever chapter it was won in.
/// </param>
public readonly record struct MinigameTierRow(
    int Tier, string Outcome, long Gold, long Crowns, long BeastFeed, long EnhanceStones, long FixedDice);

/// <summary>The chest pick's guarantee, as a player counting chests reads it.</summary>
/// <param name="PicksStood">
/// Consecutive picks that missed the top tier, as the profile's counter stands before this one.
/// </param>
/// <param name="ForcedOnPick">
/// Which pick of the current run of misses is the forced one, counted from 1. Derived from the
/// guarantee rule's own answer rather than from the authored ordinal, so the two readings of that
/// key cannot disagree here.
/// </param>
/// <param name="PicksUntilForced">
/// How many further picks must be stood before one is forced. <c>0</c> means the next pick is it.
/// </param>
public readonly record struct MinigameGuaranteeView(
    int PicksStood, int ForcedOnPick, int PicksUntilForced);
