using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>The chapter-scaled minigame reward tables, read out of <c>tuning/currencies.json</c>.</summary>
/// <remarks>
/// <para>
/// Read whole at construction, not lazily per outcome: the four tables are thirteen rows total, and
/// reading them all up front means a malformed document fails once, at the seam, rather than on
/// whichever minigame a player happens to resolve first.
/// </para>
/// <para>
/// <see cref="MinigameReward.RerollCharges"/> is read and carried, and deliberately not applied by
/// any caller today: the reroll-charge economy that would spend it does not exist yet, and
/// <c>Run</c> carries no reroll-charge field. Reading the number now and leaving it unspent is a
/// recorded assumption rather than an oversight.
/// </para>
/// </remarks>
internal sealed class MinigameRewardTuning
{
    /// <summary>The document the reward tables live in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string RewardsPointer = DocumentPath + "#/minigameRewards";
    private const string AdBundleScalarReference = DocumentPath + "#/chapterScalars/adBundleScalar";

    /// <summary>The four minigame ids, in the order the reward table lists them.</summary>
    private static readonly string[] MinigameIds =
    {
        MinigameCatalogue.ChestPick,
        MinigameCatalogue.TimingBar,
        MinigameCatalogue.DiceDuel,
        MinigameCatalogue.MemoryRune,
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyList<MinigameRewardRow>> _rows;
    private readonly double _adBundleScalar;

    private MinigameRewardTuning(
        IReadOnlyDictionary<string, IReadOnlyList<MinigameRewardRow>> rows, double adBundleScalar)
    {
        _rows = rows;
        _adBundleScalar = adBundleScalar;
    }

    /// <summary>
    /// The number of outcome tiers authored for <paramref name="minigameId"/> — the range a claimed
    /// or rolled tier index must fall inside.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is not one of the four known ids.</exception>
    internal int TierCount(string minigameId) => RowsFor(minigameId).Count;

    /// <summary>
    /// The authored <c>outcome</c> token of one tier — the name the design documents give it.
    /// </summary>
    /// <remarks>
    /// Read rather than restated because the chest pick's pity counter is keyed by the guarantee it
    /// protects, and that guarantee is an outcome <em>tier</em> rather than a rarity band. Taking the
    /// token from the document is what stops a counter id being spelled by hand somewhere.
    /// </remarks>
    /// <param name="minigameId">One of the four known <c>MG_*</c> ids.</param>
    /// <param name="tier">A zero-based tier index, within <see cref="TierCount"/>.</param>
    /// <returns>The authored outcome token.</returns>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is not one of the four known ids.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tier"/> is outside <see cref="TierCount"/>.</exception>
    internal string OutcomeName(string minigameId, int tier) =>
        throw new NotImplementedException(
            "M4-01b Phase 3 owns this body — Read must carry the authored 'outcome' token beside the " +
            "reward columns it already reads.");

    /// <summary>
    /// The Chapter-1 base reward for <paramref name="minigameId"/>'s outcome tier
    /// <paramref name="tier"/>, scaled to <paramref name="chapterId"/> by
    /// <c>1 + adBundleScalar × (chapterId − 1)</c> and rounded to the nearest integer.
    /// </summary>
    /// <param name="minigameId">One of the four known <c>MG_*</c> ids.</param>
    /// <param name="tier">A zero-based tier index, within <see cref="TierCount"/>.</param>
    /// <param name="chapterId">The chapter number. Never below 1.</param>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is not one of the four known ids.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tier"/> is outside <see cref="TierCount"/>, or <paramref name="chapterId"/> is below 1.
    /// </exception>
    internal MinigameReward RewardFor(string minigameId, int tier, int chapterId)
    {
        var rows = RowsFor(minigameId);

        if (tier < 0 || tier >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                "'" + minigameId + "' authors " + Text(rows.Count) + " outcome tier(s) (03 §6.1); " +
                Text(tier) + " is not one of them. This is a defect in the caller — the legality " +
                "check that validates a claimed or rolled tier belongs before this is ever reached.");
        }

        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId),
                chapterId,
                "02 §1 runs chapters from 1; " + Text(chapterId) + " is below the floor the scaling " +
                "formula 1 + adBundleScalar * (chapter - 1) is defined over.");
        }

        var scalar = 1.0 + (_adBundleScalar * (chapterId - 1));
        var row = rows[tier];

        return new MinigameReward(
            Scale(row.Gold, scalar),
            Scale(row.Crowns, scalar),
            Scale(row.BeastFeed, scalar),
            Scale(row.EnhanceStones, scalar),
            row.RerollCharges);
    }

    /// <summary>Reads all four tables. Throws rather than defaulting on anything malformed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    internal static MinigameRewardTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var adBundleScalar = content.ReadDouble(AdBundleScalarReference);
        var rows = new Dictionary<string, IReadOnlyList<MinigameRewardRow>>(
            MinigameIds.Length, StringComparer.Ordinal);

        foreach (var minigameId in MinigameIds)
        {
            var arrayReference = RewardsPointer + "/" + minigameId;
            var items = content.Read(arrayReference).Items;
            var tableRows = new MinigameRewardRow[items.Count];

            for (var i = 0; i < items.Count; i++)
            {
                var rowReference = arrayReference + "/" + i.ToString(CultureInfo.InvariantCulture);
                tableRows[i] = new MinigameRewardRow(
                    content.ReadInt64(rowReference + "/gold"),
                    content.ReadInt64(rowReference + "/crowns"),
                    content.ReadInt64(rowReference + "/beastFeed"),
                    content.ReadInt64(rowReference + "/enhanceStones"),
                    content.ReadInt64(rowReference + "/rerollCharges"));
            }

            if (tableRows.Length == 0)
            {
                throw new InvalidTunableException(
                    arrayReference,
                    "'" + minigameId + "' authors zero outcome tiers. 03 §6.1 gives every one of the " +
                    "four minigames at least one reward row.");
            }

            rows[minigameId] = Array.AsReadOnly(tableRows);
        }

        return new MinigameRewardTuning(rows, adBundleScalar);
    }

    private IReadOnlyList<MinigameRewardRow> RowsFor(string minigameId)
    {
        ArgumentNullException.ThrowIfNull(minigameId);

        return _rows.TryGetValue(minigameId, out var rows)
            ? rows
            : throw new ArgumentException(
                "'" + minigameId + "' is not one of 03 §6's four minigame ids (" +
                string.Join(", ", MinigameIds) + ").",
                nameof(minigameId));
    }

    private static long Scale(long chapter1Base, double scalar) =>
        (long)Math.Round(chapter1Base * scalar, MidpointRounding.AwayFromZero);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private readonly record struct MinigameRewardRow(
        long Gold, long Crowns, long BeastFeed, long EnhanceStones, long RerollCharges);
}

/// <summary>
/// One chapter-scaled minigame reward. <see cref="RerollCharges"/> is carried but unspent — see
/// <see cref="MinigameRewardTuning"/>'s remarks.
/// </summary>
internal readonly record struct MinigameReward(
    long Gold, long Crowns, long BeastFeed, long EnhanceStones, long RerollCharges);
