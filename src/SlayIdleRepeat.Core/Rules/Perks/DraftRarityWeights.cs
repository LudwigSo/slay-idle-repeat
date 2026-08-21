using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// The rarity weights one draft slot is drawn under, keyed on the stage and on the battle that
/// opened the draft. Pure data-driven weighting; no luck-protection service involved.
/// </summary>
/// <remarks>
/// The elite shift is implemented literally, not derived: the design describes it as "shift one
/// band upward" but gives only two concrete operations (Commons halved, Legendary ×2), so Rare and
/// Epic are left at the stage's base weight rather than inventing a redistribution the design
/// doesn't specify. The mini-boss's own epic+ band ignores the stage entirely — a mini-boss stands
/// on the last node of stage 1 and of stage 2 and pays the same band on both.
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

    /// <summary>The mini-boss's own band: epic or better, and nothing else.</summary>
    private static readonly IReadOnlyList<(PerkRarity Rarity, double Weight)> MiniBoss = Array.AsReadOnly(
        new (PerkRarity, double)[]
        {
            (PerkRarity.Epic, 60), (PerkRarity.Legendary, 40),
        });

    /// <summary>The weight table for one draft slot, keyed on the battle that opened the draft.</summary>
    /// <param name="stage">
    /// The stage that battle belonged to — 1, 2 or 3. Read only by the stage-keyed bands; the
    /// mini-boss's band is the same on both stages a mini-boss stands on.
    /// </param>
    /// <param name="battleKind">
    /// The tile kind of the battle that opened the draft, or <see cref="TileKind.Empty"/> for
    /// the run's opening draft, which no battle caused.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="battleKind"/> is <see cref="TileKind.Boss"/>, which opens no draft at
    /// all, or <paramref name="stage"/> is not 1, 2 or 3.
    /// </exception>
    internal static IReadOnlyList<(PerkRarity Rarity, double Weight)> For(
        int stage, TileKind battleKind)
    {
        // Refused rather than answered with a stage table: the last fight of the run leaves no run
        // to spend a perk in, so a caller asking for its band is a caller that has resurrected a
        // reward the game no longer pays — and a silent fallthrough would pay it a Common-heavy one.
        if (battleKind == TileKind.Boss)
        {
            throw new ArgumentOutOfRangeException(
                nameof(battleKind), battleKind,
                "The boss opens no perk draft, so it has no rarity band. Only a mini-boss draws the " +
                "epic+ band.");
        }

        if (battleKind == TileKind.MiniBoss)
        {
            return MiniBoss;
        }

        var baseTable = stage switch
        {
            1 => Stage1,
            2 => Stage2,
            3 => Stage3,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage), stage,
                "06 §4's RarityWeights table is keyed on stage 1, 2 or 3. The mini-boss's band is " +
                "the one that carries no stage of its own."),
        };

        return battleKind == TileKind.Elite ? Shifted(baseTable) : baseTable;
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
