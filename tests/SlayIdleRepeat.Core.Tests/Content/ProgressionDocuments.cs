using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/progression.json</c> fixtures — the <c>energy</c> and <c>legendLevel</c>
/// blocks, built as a <see cref="ContentSnapshot"/> in memory.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <c>Core.Tests</c> is hermetic: no file, no adapter, no parser (see
/// <c>TestSupport/GameContexts.cs</c>). So this fixture <em>mirrors</em> the shipped
/// <c>game-data/tuning/progression.json</c> rather than reading it, and the two are pinned
/// together from the other side by
/// <c>SlayIdleRepeat.Application.Tests.Rules.Economy.EnergyTuningMatchesTuningDataTests</c>, which
/// reads the real file and asserts the same numbers at the same JSON pointers. Neither half is
/// sufficient alone: this one proves the math is right about numbers, that one proves those are the
/// numbers the game ships.
/// </para>
/// <para>
/// The shipped values are `10` §3 and `28` Part C: <c>baseMax</c> 120, <c>perLegendLevel</c> 2,
/// <c>maxCap</c> 200, <c>regenMinutesPerPoint</c> 4, <c>runCost</c> 20,
/// <c>reserveMultipleOfMax</c> 1.
/// </para>
/// </remarks>
internal static class ProgressionDocuments
{
    /// <summary>The document path the energy tunables live at.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    /// <summary>`10` §3 — 120 Energy at Legend Level 0.</summary>
    internal const int ShippedBaseMax = 120;

    /// <summary>`10` §3 — +2 Max Energy per Legend Level.</summary>
    internal const int ShippedPerLegendLevel = 2;

    /// <summary>`10` §3 — Max Energy stops growing at 200.</summary>
    internal const int ShippedMaxCap = 200;

    /// <summary>`10` §3 — one Energy per four minutes (15/hour).</summary>
    internal const int ShippedRegenMinutesPerPoint = 4;

    /// <summary>`10` §3 — a run costs 20 Energy.</summary>
    internal const int ShippedRunCost = 20;

    /// <summary>`28` C2 — the Reserve holds 1× Max Energy.</summary>
    internal const int ShippedReserveMultipleOfMax = 1;

    /// <summary>`07` §1.1 — a player starts at Legend Level 1.</summary>
    /// <remarks>
    /// <c>const</c> rather than <c>static readonly</c> so <c>[InlineData]</c> can take it: a range
    /// test that restated the two bounds as literals would keep passing after the data moved.
    /// Pinned against the shipped file by
    /// <c>SlayIdleRepeat.Application.Tests.Rules.Economy.EnergyTuningMatchesTuningDataTests</c>,
    /// which reads <c>game-data/tuning/progression.json</c> for real.
    /// </remarks>
    internal const int ShippedLegendLevelMin = 1;

    /// <summary>`07` §1.1 — the Legend Level ladder ends at 200 in v1.</summary>
    /// <inheritdoc cref="ShippedLegendLevelMin"/>
    internal const int ShippedLegendLevelMax = 200;

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
        ContentValue? legendLevelMax = null)
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

        // `07` §1.1's range only — not xpCoefficient, xpExponent or talentPointsPerLevel. LegendTuning
        // reads two leaves because the Player aggregate's invariant needs two; the level-up CURVE is
        // M4-10's, and a fixture that authored it would imply something reads it.
        var legendLevel = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["min"] = legendLevelMin ?? ContentValue.Number(ShippedLegendLevelMin),
            ["max"] = legendLevelMax ?? ContentValue.Number(ShippedLegendLevelMax),
        });

        return Document(ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["energy"] = energy,
            ["legendLevel"] = legendLevel,
        }));
    }

    /// <summary>A snapshot whose <c>tuning/progression.json</c> has the given root value.</summary>
    internal static ContentSnapshot Document(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('a', ContentVersion.HexLength)),
            [new ContentDocument(DocumentPath, root)]);
}
