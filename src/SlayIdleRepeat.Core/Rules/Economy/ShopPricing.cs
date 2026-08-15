using System.Globalization;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>The four shop slots, each drawn from a distinct pool.</summary>
internal enum ShopItemKind
{
    /// <summary>Slot 1 — a rarity-weighted, rarity-priced perk.</summary>
    PERK,

    /// <summary>Slot 2 — one of the four consumables.</summary>
    CONSUMABLE,

    /// <summary>Slot 3 — a flat stat bonus for the rest of the run.</summary>
    RUN_BUFF,

    /// <summary>Slot 4 — always available; restores a share of Max HP.</summary>
    HEAL,
}

/// <summary>
/// The shop pricing formula:
/// <c>Price = BasePrice(itemType, rarity) * (1 + 0.25 * stageIndex) * chapterPriceScalar</c>.
/// </summary>
/// <remarks>
/// Gold income and shop prices are both tier-invariant: there is no <c>DifficultyTier</c> parameter
/// here at all, deliberately, since a parameter accepted and ignored would tell a future caller
/// this rule reads the tier when it must not. Pure and static: everything it needs is either a
/// parameter or read off <see cref="ShopTuning"/>, which is itself a pure content read. It performs
/// no I/O, holds no state, and does not know what a <c>Run</c> is. Rounded to the nearest whole
/// Gold, ties to even — the platform-independent form of "nearest" the rest of this codebase
/// already uses, so a tie is not left to the default.
/// </remarks>
internal static class ShopPricing
{
    /// <summary>Stage 1/2/3 map to stageIndex 0/1/2.</summary>
    internal const int MinStageIndex = 0;

    /// <inheritdoc cref="MinStageIndex"/>
    internal const int MaxStageIndex = 2;

    /// <summary>
    /// The pricing formula, for one slot.
    /// </summary>
    /// <param name="kind">Which of the four pools this slot draws from.</param>
    /// <param name="key">
    /// The priced item's id inside its pool: a consumable id for <see cref="ShopItemKind.CONSUMABLE"/>,
    /// a run buff id for <see cref="ShopItemKind.RUN_BUFF"/>. Ignored for
    /// <see cref="ShopItemKind.PERK"/> (priced by <paramref name="rarity"/> alone) and
    /// <see cref="ShopItemKind.HEAL"/> (one flat base price).
    /// </param>
    /// <param name="rarity">The perk's rarity. Required for <see cref="ShopItemKind.PERK"/>; ignored otherwise.</param>
    /// <param name="stageIndex">Stage 1/2/3, as 0/1/2. Stage 1 pays the base price.</param>
    /// <param name="chapterId">The run's chapter, 1-based.</param>
    /// <param name="tuning">The shop's tuning.</param>
    /// <returns>The price in run-local Gold, rounded to the nearest whole Gold (ties to even).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is undefined, <paramref name="stageIndex"/> is outside
    /// <see cref="MinStageIndex"/>..<see cref="MaxStageIndex"/>, <paramref name="chapterId"/> is
    /// below 1 or beyond <paramref name="tuning"/>'s authored chapters, or
    /// <see cref="ShopItemKind.PERK"/> was asked for with no <paramref name="rarity"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="ShopItemKind.CONSUMABLE"/> or <see cref="ShopItemKind.RUN_BUFF"/> was asked for
    /// with a null or unauthored <paramref name="key"/>.
    /// </exception>
    internal static long Price(
        ShopItemKind kind,
        string? key,
        ShopRarity? rarity,
        int stageIndex,
        int chapterId,
        ShopTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        RequireStageIndex(stageIndex);
        var chapterScalar = ChapterPriceScalarFor(chapterId, tuning);

        var basePrice = BasePriceOf(kind, key, rarity, tuning);

        var stageMultiplier = 1m + (tuning.StagePriceStep * stageIndex);
        var raw = basePrice * stageMultiplier * chapterScalar;

        return decimal.ToInt64(Math.Round(raw, 0, MidpointRounding.ToEven));
    }

    private static long BasePriceOf(ShopItemKind kind, string? key, ShopRarity? rarity, ShopTuning tuning) =>
        kind switch
        {
            ShopItemKind.PERK => tuning.PerkBasePrice(
                rarity ?? throw new ArgumentOutOfRangeException(
                    nameof(rarity), rarity,
                    "03 §7's Perk slot is priced by rarity; a PERK price with no rarity names " +
                    "nothing to look up.")),
            ShopItemKind.CONSUMABLE => tuning.ConsumableBasePrice(RequireKey(key, nameof(key))),
            ShopItemKind.RUN_BUFF => tuning.RunBuffBasePrice(RequireKey(key, nameof(key))),
            ShopItemKind.HEAL => tuning.HealBasePrice,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "03 §7 authors exactly four slot kinds."),
        };

    private static string RequireKey(string? key, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                "This slot kind is priced by an id inside its pool, and none was given.",
                parameterName);
        }

        return key;
    }

    private static void RequireStageIndex(int stageIndex)
    {
        if (stageIndex is < MinStageIndex or > MaxStageIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageIndex), stageIndex,
                "03 §1.1 runs three stages per chapter, mapped to stageIndex 0/1/2 (Stage 1 pays " +
                "the base price). " + stageIndex.ToString(CultureInfo.InvariantCulture) +
                " is outside that range.");
        }
    }

    private static decimal ChapterPriceScalarFor(int chapterId, ShopTuning tuning)
    {
        if (chapterId < 1 || chapterId > tuning.ChapterPriceScalar.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId), chapterId,
                "tuning/currencies.json#/shopTile/chapterPriceScalar authors " +
                tuning.ChapterPriceScalar.Count.ToString(CultureInfo.InvariantCulture) +
                " chapter(s) (indices 1.." +
                tuning.ChapterPriceScalar.Count.ToString(CultureInfo.InvariantCulture) +
                "); chapter " + chapterId.ToString(CultureInfo.InvariantCulture) +
                " has no authored scalar.");
        }

        return tuning.ChapterPriceScalar[chapterId - 1];
    }
}
