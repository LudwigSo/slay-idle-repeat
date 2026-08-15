using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §6.1's chapter-scaled minigame reward tables, read out of <c>tuning/currencies.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// `21` §3.1: <em>"A 📐 TUNABLE number that is not in this directory is a bug."</em> §6.1 marks the
/// four reward tables 📐 tunable and the document's own <c>chapterScalars</c> block names the
/// formula every one of them scales by — <c>1 + adBundleScalar × (chapter − 1)</c>, `03` §7a's
/// <c>adBundleScalar</c>, authored once and shared with ad bundles, the login calendar and the shop's
/// scaled material offers. Neither the four Chapter-1 base tables nor <c>0.35</c> is a literal
/// anywhere in <c>Core</c> — this type is the one reader.
/// </para>
/// <para>
/// 🔒 <b>Read whole, not lazily per outcome.</b> The four tables are thirteen rows total (3+4+3+3)
/// and every row is five integers; reading them all at construction means a malformed document fails
/// once, at the seam, rather than on whichever minigame a player happens to resolve first.
/// </para>
/// <para>
/// ⚠️ <b><c>RerollCharges</c> is read and carried, and deliberately not applied by any caller
/// today.</b> `03` §6.1's <c>MG_DICE_DUEL</c> 2-0 row grants one, and nothing in <c>Core</c> has
/// anywhere to put it: <c>UseRerollCommand</c>/<c>ROLL_DICE</c>'s reroll-charge economy is M3-04's,
/// and <c>Run</c> carries no reroll-charge field today. Reading the number now and leaving the field
/// unspent (rather than inventing a place to put it) is the recorded assumption
/// <see cref="Handlers.MinigameSubmit"/> states at its call site.
/// </para>
/// </remarks>
internal sealed class MinigameRewardTuning
{
    /// <summary>The document `03` §6.1's reward tables live in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string RewardsPointer = DocumentPath + "#/minigameRewards";
    private const string AdBundleScalarReference = DocumentPath + "#/chapterScalars/adBundleScalar";

    /// <summary>The four `03` §6 minigame ids, in the order `03` §6.1's table lists them.</summary>
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
    /// The number of outcome tiers `03` §6.1 authors for <paramref name="minigameId"/> — the range a
    /// claimed or rolled tier index must fall inside.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is not one of `03` §6's four ids.</exception>
    internal int TierCount(string minigameId) => RowsFor(minigameId).Count;

    /// <summary>
    /// `03` §6.1's Chapter-1 base reward for <paramref name="minigameId"/>'s outcome tier
    /// <paramref name="tier"/>, scaled to <paramref name="chapterId"/> by
    /// <c>1 + adBundleScalar × (chapterId − 1)</c> and rounded to the nearest integer
    /// (<c>chapterScalars.roundingMode</c>).
    /// </summary>
    /// <param name="minigameId">One of `03` §6's four <c>MG_*</c> ids.</param>
    /// <param name="tier">A zero-based tier index, within <see cref="TierCount"/>.</param>
    /// <param name="chapterId">`02` §1's chapter number. Never below 1.</param>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is not one of `03` §6's four ids.</exception>
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
/// One chapter-scaled minigame reward: `03` §6.1's four currency columns.
/// <see cref="RerollCharges"/> is carried but unspent — see <see cref="MinigameRewardTuning"/>'s
/// remarks.
/// </summary>
internal readonly record struct MinigameReward(
    long Gold, long Crowns, long BeastFeed, long EnhanceStones, long RerollCharges);
