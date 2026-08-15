using SlayIdleRepeat.Core.Content.Perks;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// 🔒 M3-06, `06` §4 — <c>RarityWeights(stage, isElite, isBoss)</c>, transcribed verbatim from the
/// spec table. Pure data-driven weighting; no <c>LuckService</c> involved (that milestone's eight
/// composition rules are <see cref="DraftCompositionRules"/>'s, not this table's).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The elite shift is implemented literally, not derived.</b> `06` §4's own text for an elite
/// battle is <em>"shift one band upward (Commons halved, Legendary ×2)"</em> — a description of the
/// net effect, followed by exactly two concrete operations. Rare and Epic are not given deltas, so
/// this reads the two stated operations and leaves Rare/Epic at the stage's base weight rather than
/// inventing a redistribution the spec does not write down (steering S6). If a future kickoff rules
/// a fuller shift, this is the one place that changes.
/// </para>
/// <para>
/// A boss battle's table ignores <paramref name="stage"/> entirely — `06` §4 authors one boss table,
/// not one per stage, and the boss node carries no stage of its own (<c>Run.PendingTileStage</c>'s
/// <c>BossStage</c> sentinel).
/// </para>
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

    /// <summary>`06` §4's weight table for one draft slot.</summary>
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
