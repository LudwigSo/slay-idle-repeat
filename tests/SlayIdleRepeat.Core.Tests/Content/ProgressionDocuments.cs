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

    /// <summary><c>LegendXpForLevel(L) = 120 * L^1.05</c>'s coefficient, as shipped.</summary>
    /// <inheritdoc cref="ShippedLegendLevelMin"/>
    internal const int ShippedLegendXpCoefficient = 120;

    /// <summary>📐 The Legend XP exponent — the single long-term pacing dial. 1.05 as shipped.</summary>
    /// <inheritdoc cref="ShippedLegendLevelMin"/>
    internal const decimal ShippedLegendXpExponent = 1.05m;

    /// <summary>Talent Points granted per Legend Level. 1 as shipped.</summary>
    /// <inheritdoc cref="ShippedLegendLevelMin"/>
    internal const int ShippedTalentPointsPerLevel = 1;

    /// <summary>The Legend Level unlock ladder, as shipped — id → level.</summary>
    /// <remarks>
    /// The nine keyed rows 07 §1.1's table authors plus the three other documents' rows the shipped
    /// file carries. Pinned against the real file by the Application suite, like every other constant
    /// here.
    /// </remarks>
    internal static IReadOnlyList<(string Unlock, int Level)> ShippedUnlocks { get; } =
    [
        ("PET_SLOT_1", 5),
        ("FORGE", 8),
        ("DUNGEONS", 8),
        ("PVP", 10),
        ("EVENTS", 12),
        ("PET_SLOT_2", 15),
        ("GUILDS", 15),
        ("MOUNT_SLOT", 20),
        ("PET_SLOT_3", 30),
        ("TALENT_BRANCH_FORTUNE", 40),
        ("MYTHIC_TIER", 60),
        ("CODEX_MASTERY", 100),
    ];

    /// <summary>
    /// The prose member <c>#/unlocks</c> carries beside its rungs, as shipped.
    /// </summary>
    /// <remarks>
    /// Every authored block in <c>game-data/</c> carries one, and the ladder's is what makes
    /// <c>UnlockTuning.Read</c>'s skip of it a live branch rather than dead code.
    /// </remarks>
    internal const string ShippedUnlocksDocMember = "_doc";

    /// <summary>The prose the shipped ladder's <c>_doc</c> member holds.</summary>
    /// <inheritdoc cref="ShippedUnlocksDocMember"/>
    internal const string ShippedUnlocksDoc = "07 §1.1 — Legend Level unlock ladder.";

    /// <summary>The member name every authored block carries its prose under.</summary>
    internal const string ChapterGatingDocMember = "_doc";

    /// <summary>Where `10` §7's chapter/tier ladder is authored.</summary>
    internal const string ChapterGatingPointer = DocumentPath + "#/chapterGating";

    /// <summary>The clear the Normal rung demands, as shipped — the chapter before this one, on Normal.</summary>
    internal const string ShippedNormalRequiresClear = "PREVIOUS_CHAPTER_NORMAL";

    /// <summary>The clear the Heroic rung demands, as shipped — this same chapter, on Normal.</summary>
    internal const string ShippedHeroicRequiresClear = "SAME_CHAPTER_NORMAL";

    /// <summary>The clear the Mythic rung demands, as shipped — this same chapter, on Heroic.</summary>
    internal const string ShippedMythicRequiresClear = "SAME_CHAPTER_HEROIC";

    /// <summary>The Legend Level the Mythic rung demands. 60 as shipped, and the only rung carrying one.</summary>
    /// <remarks>
    /// A fixture constant mirroring the shipped file, exactly like <see cref="ShippedBaseMax"/> —
    /// <b>not</b> a number any rule or gate assertion may read. The gate reads the level out of the
    /// content snapshot and <c>StartRunChapterGateTests</c> reads it back out of the same snapshot,
    /// so nothing compares the ladder against a C# literal. Pinned against the real file by
    /// <c>Application.Tests</c>' <c>ChapterGatingMatchesTuningDataTests</c>, which transcribes all
    /// three rungs by hand — this project cannot see <c>game-data</c>, so without that case every
    /// gate assertion here would stay green over a ladder the game had stopped shipping.
    /// </remarks>
    internal const int ShippedMythicRequiresLegendLevel = 60;

    /// <summary>The prose the shipped <c>chapterGating</c> block carries beside its three rungs.</summary>
    internal const string ShippedChapterGatingDoc = "10 §7 — no level gate on Normal chapters.";

    /// <summary>One authored rung: the clear it demands, and the Legend Level it demands.</summary>
    /// <param name="requiresClear">A clear token, or <see cref="ContentValue.Unauthorised"/> for "no clear".</param>
    /// <param name="requiresLegendLevel">A level, or <see cref="ContentValue.Unauthorised"/> for "no level".</param>
    internal static ContentValue Rung(ContentValue requiresClear, ContentValue requiresLegendLevel) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["requiresClear"] = requiresClear,
            ["requiresLegendLevel"] = requiresLegendLevel,
        });

    /// <summary>
    /// A <c>chapterGating</c> block holding exactly the rungs given, under the <c>_doc</c> member the
    /// shipped file carries.
    /// </summary>
    /// <remarks>
    /// Takes the rungs rather than defaulting them, so a fixture that <em>omits</em> one is as easy
    /// to write as one that replaces it — the floor case has to be expressible or it cannot be
    /// tested at all.
    /// </remarks>
    internal static ContentValue ChapterGating(params (string Tier, ContentValue Rung)[] rungs) =>
        ContentValue.Object(
            new[]
            {
                new KeyValuePair<string, ContentValue>(
                    ChapterGatingDocMember, ContentValue.Text(ShippedChapterGatingDoc)),
            }.Concat(rungs.Select(row =>
                new KeyValuePair<string, ContentValue>(row.Tier, row.Rung))));

    /// <summary>The shipped ladder: `10` §7's three rungs, with the prose member beside them.</summary>
    /// <remarks>
    /// Expression-bodied rather than an initialised static, for the reason
    /// <c>PlayerSnapshots.EmptyInventory</c> records: static initialisers run in DECLARATION order,
    /// and <see cref="Shipped"/> is declared below this and reads it.
    /// </remarks>
    internal static ContentValue ShippedChapterGating =>
        ChapterGating(
            ("NORMAL", Rung(ContentValue.Text(ShippedNormalRequiresClear), ContentValue.Unauthorised)),
            ("HEROIC", Rung(ContentValue.Text(ShippedHeroicRequiresClear), ContentValue.Unauthorised)),
            ("MYTHIC", Rung(
                ContentValue.Text(ShippedMythicRequiresClear),
                ContentValue.Number(ShippedMythicRequiresLegendLevel))));

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
        ContentValue? legendXpCoefficient = null,
        ContentValue? legendXpExponent = null,
        ContentValue? talentPointsPerLevel = null,
        ContentValue? unlocks = null,
        ContentValue? baseXpCoefficient = null,
        ContentValue? baseXpGrowth = null,
        ContentValue? tierMultiplier = null,
        ContentValue? sourceMultiplier = null,
        ContentValue? victoryMultiplier = null,
        ContentValue? stage3DeathMultiplier = null,
        ContentValue? stage2DeathMultiplier = null,
        ContentValue? stage1DeathMultiplier = null,
        ContentValue? abandonMultiplier = null,
        ContentValue? adDoubleMultiplier = null,

        // 🔴 APPENDED LAST, and every call site passes by name. A parameter inserted
        // mid-signature merges textually clean and silently re-binds every positional argument
        // after it.
        ContentValue? chapterGating = null)
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
            ["xpCoefficient"] = legendXpCoefficient ?? ContentValue.Number(ShippedLegendXpCoefficient),
            ["xpExponent"] = legendXpExponent ?? ContentValue.Number(ShippedLegendXpExponent),
            ["talentPointsPerLevel"] =
                talentPointsPerLevel ?? ContentValue.Number(ShippedTalentPointsPerLevel),
        });

        // 🔴 The `_doc` prose member is authored HERE, in the default ladder, because the shipped
        // game-data/tuning/progression.json#/unlocks carries one and this fixture mirrors that file.
        // Without it, UnlockTuning.Read's skip of the member is exercised by nothing: the reader
        // could stop skipping it, and the whole hermetic suite would still be green while the real
        // file's prose row was read as a rung and refused as a level outside the range.
        var unlockLadder = unlocks ?? ContentValue.Object(
            new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                [ShippedUnlocksDocMember] = ContentValue.Text(ShippedUnlocksDoc),
            }.Concat(ShippedUnlocks.Select(row =>
                new KeyValuePair<string, ContentValue>(row.Unlock, ContentValue.Number(row.Level)))));

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
            ["unlocks"] = unlockLadder,
            ["runXp"] = runXp,
            ["completionMultiplier"] = completionMultiplier,
            ["adDoubleMultiplier"] = adDoubleMultiplierBlock,
            ["chapterGating"] = chapterGating ?? ShippedChapterGating,
        }));
    }

    /// <summary>The shipped document with its <c>chapterGating</c> block removed outright.</summary>
    /// <remarks>
    /// <see cref="With"/> cannot express this: its optional parameters read <c>null</c> as "keep the
    /// shipped value", which is what makes them readable. "The block is not there at all" is a
    /// different failure from "the block holds a deliberate null" and from "the block is the wrong
    /// kind", and a reader whose whole job is telling those three apart needs all three doors.
    /// </remarks>
    internal static ContentSnapshot WithoutChapterGating()
    {
        var root = Shipped.GetDocument(DocumentPath).Root;

        return Document(ContentValue.Object(
            root.MemberNames
                .Where(name => !string.Equals(name, "chapterGating", StringComparison.Ordinal))
                .Select(name =>
                {
                    root.TryGetMember(name, out var member);
                    return new KeyValuePair<string, ContentValue>(name, member!);
                })));
    }

    /// <summary>A snapshot whose <c>tuning/progression.json</c> has the given root value.</summary>
    internal static ContentSnapshot Document(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('a', ContentVersion.HexLength)),
            [new ContentDocument(DocumentPath, root)]);
}
