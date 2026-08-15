using SlayIdleRepeat.Core.Content.Perks;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// <c>RarityWeights(stage, isElite, isBoss)</c>, transcribed verbatim from the design table. Pure
/// data-driven weighting; no luck-protection service involved.
/// </summary>
/// <remarks>
/// The elite shift is implemented literally, not derived: the design describes it as "shift one
/// band upward" but gives only two concrete operations (Commons halved, Legendary ×2), so Rare and
/// Epic are left at the stage's base weight rather than inventing a redistribution the design
/// doesn't specify. A boss battle's table ignores <paramref name="stage"/> entirely, since the boss
/// node carries no stage of its own.
/// </remarks>
internal static class DraftRarityWeights
{
    private static readonly IReadOnlyList<(PerkRarity Rarity, double Weight)> Stage1 = Array.AsReadOnly(
        new (PerkRarity, double)[]
        {
            (PerkRarity.Common, 62), (PerkRarity.Rare, 30), (PerkRarity.Epic, 7), (PerkRarity.Legendary, 1),
        });

    private static readonly IReadOnlyList<(PerkRarity Rarity, double Weight)> Stage2 = Array.AsReadOnly(
        new (PerkRarity, double)[]
        {
            (PerkRarity.Common, 48), (PerkRarity.Rare, 36), (PerkRarity.Epic, 13), (PerkRarity.Legendary, 3),
        });

    private static readonly IReadOnlyList<(PerkRarity Rarity, double Weight)> Stage3 = Array.AsReadOnly(
        new (PerkRarity, double)[]
        {
            (PerkRarity.Common, 34), (PerkRarity.Rare, 40), (PerkRarity.Epic, 20), (PerkRarity.Legendary, 6),
        });

    private static readonly IReadOnlyList<(PerkRarity Rarity, double Weight)> Boss = Array.AsReadOnly(
        new (PerkRarity, double)[]
        {
            (PerkRarity.Epic, 55), (PerkRarity.Legendary, 45),
        });

    /// <summary>The weight table for one draft slot.</summary>
    /// <param name="stage">1, 2 or 3. Ignored when <paramref name="isBoss"/> is true.</param>
    /// <param name="isElite">Whether the just-won battle was an Elite tile.</param>
    /// <param name="isBoss">Whether the just-won battle was the Boss tile. Wins over <paramref name="isElite"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="isBoss"/> is false and <paramref name="stage"/> is not 1, 2 or 3.
    /// </exception>
    internal static IReadOnlyList<(PerkRarity Rarity, double Weight)> For(int stage, bool isElite, bool isBoss)
    {
        if (isBoss)
        {
            return Boss;
        }

        var baseTable = stage switch
        {
            1 => Stage1,
            2 => Stage2,
            3 => Stage3,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage), stage,
                "06 §4's RarityWeights table is keyed on stage 1, 2 or 3 for a non-boss draw. " +
                "A boss battle's own weights are keyed on isBoss instead, and the boss node carries " +
                "no stage of its own."),
        };

        return isElite ? Shifted(baseTable) : baseTable;
    }

    private static IReadOnlyList<(PerkRarity Rarity, double Weight)> Shifted(
        IReadOnlyList<(PerkRarity Rarity, double Weight)> baseTable)
    {
        var shifted = new (PerkRarity Rarity, double Weight)[baseTable.Count];

        for (var i = 0; i < baseTable.Count; i++)
        {
            var (rarity, weight) = baseTable[i];

            shifted[i] = rarity switch
            {
                PerkRarity.Common => (rarity, weight / 2.0),
                PerkRarity.Legendary => (rarity, weight * 2.0),
                _ => (rarity, weight),
            };
        }

        return Array.AsReadOnly(shifted);
    }
}
