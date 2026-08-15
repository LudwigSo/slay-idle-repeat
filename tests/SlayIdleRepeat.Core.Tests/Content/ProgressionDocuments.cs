using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/progression.json</c> fixtures — the <c>energy</c> and <c>legendLevel</c> blocks,
/// built as a <see cref="ContentSnapshot"/> in memory.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped file rather than reading it;
/// <c>EnergyTuningMatchesTuningDataTests</c> in <c>Application.Tests</c> pins the two together by
/// reading the real file at the same JSON pointers. Neither half is sufficient alone: this proves the
/// math is right about the numbers, that one proves those are the numbers we ship.
/// </remarks>
internal static class ProgressionDocuments
{
    /// <summary>The document path the energy tunables live at.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    /// <summary>120 Energy at Legend Level 0.</summary>
    internal const int ShippedBaseMax = 120;

    /// <summary>+2 Max Energy per Legend Level.</summary>
    internal const int ShippedPerLegendLevel = 2;

    /// <summary>Max Energy stops growing at 200.</summary>
    internal const int ShippedMaxCap = 200;

    /// <summary>One Energy per four minutes (15/hour).</summary>
    internal const int ShippedRegenMinutesPerPoint = 4;

    /// <summary>A run costs 20 Energy.</summary>
    internal const int ShippedRunCost = 20;

    /// <summary>The Reserve holds 1× Max Energy.</summary>
    internal const int ShippedReserveMultipleOfMax = 1;

    /// <summary>A player starts at Legend Level 1.</summary>
    /// <remarks>
    /// <c>const</c> rather than <c>static readonly</c> so <c>[InlineData]</c> can take it: a range
    /// test that restated the two bounds as literals would keep passing after the data moved.
    /// Pinned against the shipped file by
    /// <c>SlayIdleRepeat.Application.Tests.Rules.Economy.EnergyTuningMatchesTuningDataTests</c>,
    /// which reads <c>game-data/tuning/progression.json</c> for real.
    /// </remarks>
    internal const int ShippedLegendLevelMin = 1;

    /// <summary>The Legend Level ladder ends at 200 in v1.</summary>
    /// <inheritdoc cref="ShippedLegendLevelMin"/>
    internal const int ShippedLegendLevelMax = 200;

    /// <summary><c>BaseXp(c) = baseXpCoefficient * baseXpGrowth^(c-1)</c>'s coefficient, as shipped.</summary>
    internal const int ShippedBaseXpCoefficient = 25;

    /// <summary>The Legend XP growth base, as shipped.</summary>
    internal const decimal ShippedBaseXpGrowth = 1.55m;

    /// <summary>The Victory completion multiplier, as shipped.</summary>
    internal const decimal ShippedVictoryMultiplier = 1.0m;

    /// <summary>The Stage 3 death completion multiplier, as shipped.</summary>
    internal const decimal ShippedStage3DeathMultiplier = 0.6m;

    /// <summary>The Stage 2 death completion multiplier, as shipped.</summary>
    internal const decimal ShippedStage2DeathMultiplier = 0.4m;

    /// <summary>The Stage 1 death completion multiplier, as shipped.</summary>
    internal const decimal ShippedStage1DeathMultiplier = 0.25m;

    /// <summary>The Abandon completion multiplier, as shipped.</summary>
    internal const decimal ShippedAbandonMultiplier = 0.1m;

    /// <summary>The run-end ad-double multiplier, as shipped.</summary>
    internal const decimal ShippedAdDoubleMultiplier = 2.0m;

    /// <summary>A snapshot holding exactly the shipped energy block.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// A snapshot holding the shipped energy block with individual leaves replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> to model a deliberate <c>null</c> hole, or omit a
    /// parameter to keep the shipped value.
    /// </summary>
    internal static ContentSnapshot With(
        ContentValue? baseMax = null,
        ContentValue? perLegendLevel = null,
        ContentValue? maxCap = null,
        ContentValue? regenMinutesPerPoint = null,
        ContentValue? runCost = null,
        ContentValue? reserveMultipleOfMax = null,
        ContentValue? legendLevelMin = null,
        ContentValue? legendLevelMax = null,
        ContentValue? baseXpCoefficient = null,
        ContentValue? baseXpGrowth = null,
        ContentValue? tierMultiplier = null,
        ContentValue? sourceMultiplier = null,
        ContentValue? victoryMultiplier = null,
        ContentValue? stage3DeathMultiplier = null,
        ContentValue? stage2DeathMultiplier = null,
        ContentValue? stage1DeathMultiplier = null,
        ContentValue? abandonMultiplier = null,
        ContentValue? adDoubleMultiplier = null)
    {
        var energy = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["baseMax"] = baseMax ?? ContentValue.Number(ShippedBaseMax),
            ["perLegendLevel"] = perLegendLevel ?? ContentValue.Number(ShippedPerLegendLevel),
            ["maxCap"] = maxCap ?? ContentValue.Number(ShippedMaxCap),
            ["regenMinutesPerPoint"] = regenMinutesPerPoint ?? ContentValue.Number(ShippedRegenMinutesPerPoint),
            ["runCost"] = runCost ?? ContentValue.Number(ShippedRunCost),
            ["reserveMultipleOfMax"] = reserveMultipleOfMax ?? ContentValue.Number(ShippedReserveMultipleOfMax),
        });

        // The range only — not xpCoefficient, xpExponent or talentPointsPerLevel. LegendTuning reads
        // two leaves because the Player aggregate's invariant needs two; the level-up curve is out of
        // scope here, and a fixture that authored it would imply something reads it.
        var legendLevel = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["min"] = legendLevelMin ?? ContentValue.Number(ShippedLegendLevelMin),
            ["max"] = legendLevelMax ?? ContentValue.Number(ShippedLegendLevelMax),
        });

        var runXp = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["baseXpCoefficient"] = baseXpCoefficient ?? ContentValue.Number(ShippedBaseXpCoefficient),
            ["baseXpGrowth"] = baseXpGrowth ?? ContentValue.Number(ShippedBaseXpGrowth),
            ["tierMultiplier"] = tierMultiplier ?? ContentValue.Object(
                new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["NORMAL"] = ContentValue.Number(1.0m),
                    ["HEROIC"] = ContentValue.Number(1.6m),
                    ["MYTHIC"] = ContentValue.Number(2.5m),
                }),
            ["sourceMultiplier"] = sourceMultiplier ?? ContentValue.Object(
                new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["NORMAL_ENEMY_KILL"] = ContentValue.Number(1),
                    ["ELITE_KILL"] = ContentValue.Number(3),
                    ["BOSS_KILL"] = ContentValue.Number(15),
                    ["RUN_VICTORY_BONUS"] = ContentValue.Number(10),
                }),
        });

        var completionMultiplier = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["VICTORY"] = victoryMultiplier ?? ContentValue.Number(ShippedVictoryMultiplier),
            ["STAGE_3_DEATH"] = stage3DeathMultiplier ?? ContentValue.Number(ShippedStage3DeathMultiplier),
            ["STAGE_2_DEATH"] = stage2DeathMultiplier ?? ContentValue.Number(ShippedStage2DeathMultiplier),
            ["STAGE_1_DEATH"] = stage1DeathMultiplier ?? ContentValue.Number(ShippedStage1DeathMultiplier),
            ["ABANDON"] = abandonMultiplier ?? ContentValue.Number(ShippedAbandonMultiplier),
        });

        var adDoubleMultiplierBlock = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["value"] = adDoubleMultiplier ?? ContentValue.Number(ShippedAdDoubleMultiplier),
        });

        return Document(ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["energy"] = energy,
            ["legendLevel"] = legendLevel,
            ["runXp"] = runXp,
            ["completionMultiplier"] = completionMultiplier,
            ["adDoubleMultiplier"] = adDoubleMultiplierBlock,
        }));
    }

    /// <summary>A snapshot whose <c>tuning/progression.json</c> has the given root value.</summary>
    internal static ContentSnapshot Document(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('a', ContentVersion.HexLength)),
            [new ContentDocument(DocumentPath, root)]);
}
