using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Rules.Luck;

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
/// more than it is about to receive. That is why the scaling is asked of the list-taking overload
/// the handler's own aggregate one delegates to, rather than restated here or reached by rehydrating
/// a <c>Run</c> inside a projection.
/// </para>
/// <para>
/// 🔒 <b>The guarantee numbers are asked of the guarantee rule, never re-derived here.</b> The
/// ordinal arithmetic is <c>misses &gt;= everyNth - 1</c> and both readings of the authored key have
/// been wrong in this repository before, so a second statement of it in a projection is a second
/// place for the counter a player is watching to disagree with the one the server keeps.
/// </para>
/// <para>
/// 🔒 <b>Read-only.</b> Projecting mutates no <c>Run</c> and moves no stream position: the
/// server-rolled draw belongs to the command, and a view that opened the minigame stream would leave
/// the pick resolving against a different roll than the screen was showing.
/// </para>
/// <para>
/// Which minigame a Minigame tile offers is not carried by the run at all, so the id is an argument
/// rather than something this view can read. That the choice is therefore the client's, and
/// unenforced, is documented where the client makes it.
/// </para>
/// </remarks>
public sealed class MinigameView
{
    /// <summary>The known minigame ids, read off the rules layer's own catalogue.</summary>
    /// <remarks>
    /// The catalogue is internal and must not leak, so the ids are copied out rather than the type.
    /// A client picking which arm to open reads this list rather than transcribing four literals.
    /// </remarks>
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[]
    {
        MinigameCatalogue.ChestPick,
        MinigameCatalogue.TimingBar,
        MinigameCatalogue.DiceDuel,
        MinigameCatalogue.MemoryRune,
    });

    private MinigameView(
        bool isServerRolled,
        bool alreadyResolvedHere,
        IReadOnlyList<MinigameTierRow> rows,
        MinigameGuaranteeView? guarantee)
    {
        IsServerRolled = isServerRolled;
        AlreadyResolvedHere = alreadyResolvedHere;
        Rows = rows;
        Guarantee = guarantee;
    }

    /// <summary>Whether the server draws this minigame's outcome and ignores the client's claim.</summary>
    public bool IsServerRolled { get; }

    /// <summary>Whether this run has already resolved a minigame at the node it is standing on.</summary>
    /// <remarks>
    /// The legality gate is per tile instance rather than per minigame, so a screen that offered a
    /// second submission here would be offering a press the rules layer refuses.
    /// </remarks>
    public bool AlreadyResolvedHere { get; }

    /// <summary>Every outcome tier this minigame authors, chapter-scaled, in ascending tier order.</summary>
    public IReadOnlyList<MinigameTierRow> Rows { get; }

    /// <summary>
    /// The chest pick's gold-tier guarantee as it stands for this player, or <c>null</c> for the
    /// three minigames that carry no counter at all.
    /// </summary>
    public MinigameGuaranteeView? Guarantee { get; }

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
        RunSnapshot run, PlayerSnapshot player, ContentSnapshot content, string minigameId)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(minigameId);

        if (!MinigameCatalogue.IsKnown(minigameId))
        {
            throw new ArgumentException(UnknownId(minigameId), nameof(minigameId));
        }

        if (run.PendingTileKind != (int)TileKind.Minigame)
        {
            return null;
        }

        var tuning = MinigameRewardTuning.Read(content);

        return new MinigameView(
            MinigameCatalogue.IsServerRolled(minigameId),
            run.ResolvedMinigames.ContainsKey(run.Position),
            RowsOf(run, content, tuning, minigameId),
            GuaranteeOf(player, content, tuning, minigameId));
    }

    /// <summary>Every authored tier of one minigame, chapter-scaled and Gold-modifier scaled.</summary>
    private static IReadOnlyList<MinigameTierRow> RowsOf(
        RunSnapshot run, ContentSnapshot content, MinigameRewardTuning tuning, string minigameId)
    {
        var shrineBuffs = run.ShrineBuffs ?? Array.Empty<string>();
        var curses = run.Curses ?? Array.Empty<string>();
        var rows = new MinigameTierRow[tuning.TierCount(minigameId)];

        for (var tier = 0; tier < rows.Length; tier++)
        {
            var reward = tuning.RewardFor(minigameId, tier, run.ChapterId);

            rows[tier] = new MinigameTierRow(
                tier,
                tuning.OutcomeName(minigameId, tier),
                RunModifierTotals.ScaleGoldIncome(shrineBuffs, curses, content, reward.Gold),
                reward.Crowns,
                reward.BeastFeed,
                reward.EnhanceStones,
                reward.FixedDice);
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>
    /// The chest pick's guarantee as this profile's counter leaves it, or <c>null</c> for the three
    /// minigames that carry none.
    /// </summary>
    /// <remarks>
    /// The counter id is composed through <c>LuckTuning</c>, which is the one place a counter id may
    /// be formed, out of the reward table's own token for the tier the guarantee protects. A key
    /// spelled by hand here would read an absent counter on any content set that retuned either half
    /// and report a player mid-streak as one starting clean.
    /// </remarks>
    private static MinigameGuaranteeView? GuaranteeOf(
        PlayerSnapshot player, ContentSnapshot content, MinigameRewardTuning tuning, string minigameId)
    {
        if (!string.Equals(minigameId, MinigameCatalogue.ChestPick, StringComparison.Ordinal))
        {
            return null;
        }

        var luck = LuckTuning.Read(content);
        var rule = luck.ChestPick;
        var topTier = ChestPickGuarantee.TopTier(tuning.TierCount(MinigameCatalogue.ChestPick));
        var counterKey = luck.CounterKey(
            SourceClass.MINIGAME, tuning.OutcomeName(MinigameCatalogue.ChestPick, topTier));

        var picksStood = player.PityCounters is { } counters &&
                         counters.TryGetValue(counterKey, out var stood)
            ? stood
            : 0;

        var untilForced = PicksUntilForced(rule, picksStood);

        // The forced pick's own ordinal within this streak, counted from 1: the picks already stood,
        // the ones still to stand, and the forced one itself.
        return new MinigameGuaranteeView(picksStood, picksStood + untilForced + 1, untilForced);
    }

    /// <summary>
    /// How many further picks must be stood before one is forced — asked of the guarantee rule, one
    /// candidate pick at a time, rather than restated as arithmetic.
    /// </summary>
    private static int PicksUntilForced(ChestPickRule rule, int picksStood)
    {
        for (var further = 0; further <= rule.GuaranteeOnNthPick; further++)
        {
            if (ChestPickGuarantee.GuaranteeFires(rule, picksStood + further))
            {
                return further;
            }
        }

        // Unreachable while the rule's own ordinal is positive, which its reader enforces: the
        // guarantee fires at the latest on the N-th pick of any streak. Refused rather than defaulted
        // to zero, which would draw "the next chest is guaranteed" over a rule that says otherwise.
        throw new InvalidTunableException(
            LuckTuning.ChestPickReference,
            "The chest-pick guarantee never fires within its own authored ordinal, so there is no " +
            "countdown to show. A screen defaulting to zero here would promise the player that the " +
            "next chest is the forced one.");
    }

    private static string UnknownId(string minigameId) =>
        "'" + minigameId + "' is not one of the minigames the rules layer knows (" +
        string.Join(", ", Ids) + "). A tile offering an id MINIGAME_SUBMIT refuses is a screen the " +
        "player cannot leave by playing it.";
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
