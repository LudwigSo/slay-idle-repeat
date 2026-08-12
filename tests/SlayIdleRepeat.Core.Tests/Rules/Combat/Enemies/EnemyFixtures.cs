using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary>
/// `05` §6–§6.2 and §6.4 as <c>game-data/content/enemies/enemies.json</c> authors them, restated
/// for the suite that tests the derivation's arithmetic.
/// </summary>
/// <remarks>
/// ⚠️ These are the numbers the shipped document holds, restated here because
/// <c>SlayIdleRepeat.Core.Tests</c> references <c>SlayIdleRepeat.Core</c> and nothing else — it has
/// no JSON reader and no content loader. The copy cannot be allowed to drift, so the shipped file is
/// asserted against `05` §6 <em>separately</em>, by
/// <c>SlayIdleRepeat.Application.Tests.Content.EnemiesDataTests</c>, which reads the real document.
/// What is tested here is the derivation's arithmetic and its rules; what is tested there is the
/// transcription. The same split <c>StatFixtures</c> records for <c>combat_caps.json</c>.
/// </remarks>
internal static class EnemyFixtures
{
    /// <summary>The document path `05` §6 names, as the repository holds it.</summary>
    internal const string Document = "content/enemies/enemies.json";

    /// <summary>`05` §6.1 — the eight rows, as coefficients only.</summary>
    internal static IReadOnlyList<ArchetypeRow> Archetypes { get; } = new List<ArchetypeRow>
    {
        new(EnemyArchetype.GRUNT, 1.00, 1.00, 1.00, 1.00, 0.05, 0.50, 0.02, 0.00, 1, ArchetypeOnHit.None),
        new(EnemyArchetype.SWARM, 0.35, 0.55, 0.60, 1.30, 0.05, 0.50, 0.05, 0.00, 3, ArchetypeOnHit.None),
        new(EnemyArchetype.BRUTE, 2.00, 1.35, 1.20, 0.60, 0.05, 0.75, 0.00, 0.00, 1, ArchetypeOnHit.None),
        new(EnemyArchetype.SKIRMISHER, 0.70, 0.85, 0.70, 1.70, 0.10, 0.50, 0.15, 0.00, 1, ArchetypeOnHit.None),
        new(EnemyArchetype.WARDEN, 1.60, 0.70, 2.20, 0.85, 0.03, 0.50, 0.02, 0.00, 1, ArchetypeOnHit.Sunder),
        new(EnemyArchetype.CASTER, 0.75, 1.50, 0.55, 0.70, 0.08, 0.60, 0.03, 0.00, 1, ArchetypeOnHit.BiomeStatus),
        new(EnemyArchetype.LEECH, 1.10, 0.95, 0.90, 1.10, 0.05, 0.50, 0.03, 0.25, 1, ArchetypeOnHit.None),
        new(EnemyArchetype.REAVER, 0.90, 1.20, 0.80, 1.00, 0.30, 1.20, 0.05, 0.00, 1, ArchetypeOnHit.None),
    };

    /// <summary>One row, by shape.</summary>
    internal static ArchetypeRow Row(EnemyArchetype archetype) =>
        Archetypes.Single(a => a.Id == archetype);

    /// <summary>`05` §6 — the derivation constants and the six per-archetype-invariant stats.</summary>
    internal static EnemyDerivationConstants Constants() =>
        new(0.60, 0.045, 0.030, 1.00, 4, new Dictionary<StatId, double>
        {
            [StatId.BLOCK] = 0.00,
            [StatId.PEN] = 0.00,
            [StatId.DMG_PCT] = 0.00,
            [StatId.DR_PCT] = 0.00,
            [StatId.HEAL_PCT] = 1.00,
            [StatId.THORNS] = 0.00,
        });

    /// <summary>The same constants with one of the six fixed stats removed, for a negative case.</summary>
    internal static EnemyDerivationConstants ConstantsWithout(StatId stat)
    {
        var full = Constants();
        var fixedStats = full.FixedStats.Where(s => s.Key != stat).ToDictionary(s => s.Key, s => s.Value);

        return full with { FixedStats = fixedStats };
    }

    /// <summary>`05` §6.0 — the chapter-based level table.</summary>
    internal static EnemyLevelTable Levels() =>
        EnemyLevelTable.From(
            new Dictionary<int, int>
            {
                [1] = 10, [2] = 15, [3] = 20, [4] = 30, [5] = 40, [6] = 50, [7] = 60, [8] = 80,
            },
            new List<int> { 0, 10, 20 });

    /// <summary>`05` §6.2 — the eight modifiers, with the authored parameters.</summary>
    internal static IReadOnlyList<EliteModifierRow> Modifiers { get; } = new List<EliteModifierRow>
    {
        new(EliteModifier.ENRAGED, "loc.elite_modifier.enraged.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["atkMult"] = 1.50, ["belowHpFraction"] = 0.40 }, null),
        new(EliteModifier.ARMORED, "loc.elite_modifier.armored.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["defMult"] = 1.80, ["aspdMult"] = 0.80 }, null),
        new(EliteModifier.VAMPIRIC, "loc.elite_modifier.vampiric.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["lifesteal"] = 0.35 }, null),
        new(EliteModifier.VOLATILE, "loc.elite_modifier.volatile.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["deathExplosionHeroMaxHpPct"] = 0.15 }, null),
        new(EliteModifier.SHIELDED, "loc.elite_modifier.shielded.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["startingWardMaxHpPct"] = 0.30 }, null),
        new(EliteModifier.SWIFT, "loc.elite_modifier.swift.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["aspdMult"] = 1.60 }, null),
        new(EliteModifier.CURSED, "loc.elite_modifier.cursed.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["killWithinSeconds"] = 20.0 }, null),
        new(EliteModifier.REFLECTIVE, "loc.elite_modifier.reflective.name",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["thorns"] = 0.25 }, null),
    };

    /// <summary>`05` §6.4 — Chapter 1's pool, the row with the authored <c>REAVER</c> zero.</summary>
    internal static ChapterEnemyPool ChapterOnePool() =>
        ChapterEnemyPool.From(
            1,
            new List<ArchetypeWeight>
            {
                new(EnemyArchetype.GRUNT, 40), new(EnemyArchetype.SWARM, 20),
                new(EnemyArchetype.BRUTE, 15), new(EnemyArchetype.SKIRMISHER, 10),
                new(EnemyArchetype.WARDEN, 5), new(EnemyArchetype.CASTER, 5),
                new(EnemyArchetype.LEECH, 5), new(EnemyArchetype.REAVER, 0),
            },
            new List<string> { "EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA" });

    /// <summary>A snapshot holding a <c>content/enemies/enemies.json</c> of the shipped shape.</summary>
    /// <param name="unauthorised">
    /// A pointer suffix (below the document root) to replace with <c>null</c>, for a negative case.
    /// </param>
    internal static ContentSnapshot Snapshot(string? unauthorised = null)
    {
        var root = ContentValue.Object(
        [
            new("derivation", ContentValue.Object(
            [
                new("hpPerPower", Number("derivation/hpPerPower", 0.60m, unauthorised)),
                new("atkPerPower", ContentValue.Number(0.045m)),
                new("defPerPower", ContentValue.Number(0.030m)),
                new("baseAspd", ContentValue.Number(1.00m)),
                new("roundingDecimals", ContentValue.Number(4m)),
                new("fixedStats", ContentValue.Object(
                [
                    new("BLOCK", ContentValue.Number(0.0m)),
                    new("PEN", ContentValue.Number(0.0m)),
                    new("DMG_PCT", ContentValue.Number(0.0m)),
                    new("DR_PCT", ContentValue.Number(0.0m)),
                    new("HEAL_PCT", Number("derivation/fixedStats/HEAL_PCT", 1.0m, unauthorised)),
                    new("THORNS", ContentValue.Number(0.0m)),
                ])),
            ])),
            new("enemyLevel", ContentValue.Object(
            [
                new("baseByChapter", ContentValue.Array(
                    new List<(int Chapter, int Level)>
                    {
                        (1, 10), (2, 15), (3, 20), (4, 30), (5, 40), (6, 50), (7, 60), (8, 80),
                    }
                    .Select(r => ContentValue.Object(
                    [
                        new("chapter", ContentValue.Number(r.Chapter)),
                        new("level", ContentValue.Number(r.Level)),
                    ])).ToList())),
                new("tierBonus", ContentValue.Object(
                [
                    new("NORMAL", ContentValue.Number(0m)),
                    new("HEROIC", ContentValue.Number(10m)),
                    new("MYTHIC", ContentValue.Number(20m)),
                ])),
            ])),
            new("archetypes", ContentValue.Array(Archetypes.Select(a => ContentValue.Object(
            [
                new("id", ContentValue.Text(a.Id.ToString())),
                new("hpCoef", ContentValue.Number((decimal)a.HpCoef)),
                new("atkCoef", ContentValue.Number((decimal)a.AtkCoef)),
                new("defCoef", ContentValue.Number((decimal)a.DefCoef)),
                new("aspdCoef", ContentValue.Number((decimal)a.AspdCoef)),
                new("crit", ContentValue.Number((decimal)a.Crit)),
                new("critDamage", ContentValue.Number((decimal)a.CritDamage)),
                new("dodge", ContentValue.Number((decimal)a.Dodge)),
                new("lifesteal", ContentValue.Number((decimal)a.Lifesteal)),
                new("unitsPerDraw", ContentValue.Number(a.UnitsPerDraw)),
                new("onHit", ContentValue.Text(a.OnHit switch
                {
                    ArchetypeOnHit.Sunder => "SUNDER",
                    ArchetypeOnHit.BiomeStatus => "BIOME_STATUS",
                    _ => "NONE",
                })),
            ])).ToList())),
            new("onHit", ContentValue.Object(
            [
                new("wardenSunder", ContentValue.Object(
                [
                    new("statusId", ContentValue.Text("SUNDER")),
                    new("procChancePerLandedHit", ContentValue.Number(0.35m)),
                    new("potency", ContentValue.Number(-0.05m)),
                    new("potencyBasis", ContentValue.Text("TARGET_DEF_PCT_PER_STACK")),
                    new("durationSeconds", ContentValue.Number(6.0m)),
                    new("refreshOnReapply", ContentValue.Boolean(true)),
                    new("maxStacks", ContentValue.Number(5m)),
                ])),
                new("casterBiomeStatus", ContentValue.Array(CasterRows().ToList())),
            ])),
            new("elites", ContentValue.Object(
            [
                new("powerMultiplier", ContentValue.Number(2.2m)),
                new("modifiersPerElite", ContentValue.Number(1m)),
                new("noRepeatWithPreviousEliteInRun", ContentValue.Boolean(true)),
                new("modifiers", ContentValue.Array(Modifiers.Select(m => ContentValue.Object(
                    m.Id == EliteModifier.CURSED
                        ? new List<KeyValuePair<string, ContentValue>>
                        {
                            new("id", ContentValue.Text(m.Id.ToString())),
                            new("displayName", ContentValue.Text(m.DisplayName)),
                            new("parameters", Parameters(m)),
                            new("curseId", ContentValue.Unauthorised),
                        }
                        : new List<KeyValuePair<string, ContentValue>>
                        {
                            new("id", ContentValue.Text(m.Id.ToString())),
                            new("displayName", ContentValue.Text(m.DisplayName)),
                            new("parameters", Parameters(m)),
                        })).ToList())),
                new("identities", ContentValue.Array(Identities.Select(i => ContentValue.Object(
                [
                    new("id", ContentValue.Text(i.Key)),
                    new("baseArchetype", ContentValue.Text(i.Value.ToString())),
                ])).ToList())),
            ])),
            new("chapterPools", ContentValue.Array(PoolRows().ToList())),
            new("targetPriority", ContentValue.Object(
            [
                new("default", ContentValue.Number(0m)),
                new("deprioritised", ContentValue.Number(-1m)),
                new("forced", ContentValue.Number(1m)),
            ])),
        ]);

        return new ContentSnapshot(
            ContentVersion.FromHex(new string('6', ContentVersion.HexLength)),
            [new ContentDocument(Document, root)]);
    }

    /// <summary>`05` §6.2 — the sixteen identities, in the document's order.</summary>
    internal static IReadOnlyList<KeyValuePair<string, EnemyArchetype>> Identities { get; } =
        new List<KeyValuePair<string, EnemyArchetype>>
        {
            new("EL_THORN_SENTINEL", EnemyArchetype.WARDEN),
            new("EL_MOSSBACK_ALPHA", EnemyArchetype.BRUTE),
            new("EL_BOGFATHER", EnemyArchetype.CASTER),
            new("EL_MIRESTALKER", EnemyArchetype.SKIRMISHER),
            new("EL_BONE_CHOIR", EnemyArchetype.CASTER),
            new("EL_GRAVE_TITAN", EnemyArchetype.BRUTE),
            new("EL_MAGMA_HERALD", EnemyArchetype.REAVER),
            new("EL_ASHWING", EnemyArchetype.SKIRMISHER),
            new("EL_RIMEFANG_WARDEN", EnemyArchetype.WARDEN),
            new("EL_GLACIER_MAW", EnemyArchetype.BRUTE),
            new("EL_COGWRIGHT", EnemyArchetype.CASTER),
            new("EL_STEAMBREAKER", EnemyArchetype.BRUTE),
            new("EL_SPORELORD", EnemyArchetype.CASTER),
            new("EL_ROTVINE", EnemyArchetype.LEECH),
            new("EL_STARSCRIBE", EnemyArchetype.CASTER),
            new("EL_VOIDCALF", EnemyArchetype.LEECH),
        };

    /// <summary>`05` §6.4 — the eight weight rows, in archetype order.</summary>
    internal static IReadOnlyList<int[]> PoolWeights { get; } = new List<int[]>
    {
        new[] { 40, 20, 15, 10, 5, 5, 5, 0 },
        new[] { 25, 15, 10, 10, 5, 20, 15, 0 },
        new[] { 20, 25, 15, 5, 10, 15, 5, 5 },
        new[] { 20, 10, 20, 10, 5, 20, 5, 10 },
        new[] { 15, 10, 20, 15, 20, 10, 5, 5 },
        new[] { 15, 15, 15, 15, 20, 10, 0, 10 },
        new[] { 10, 20, 10, 10, 5, 20, 20, 5 },
        new[] { 10, 10, 15, 15, 10, 15, 10, 15 },
    };

    private static ContentValue Parameters(EliteModifierRow modifier) =>
        ContentValue.Object(modifier.Parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new KeyValuePair<string, ContentValue>(p.Key, ContentValue.Number((decimal)p.Value)))
            .ToList());

    private static ContentValue Number(string pointer, decimal value, string? unauthorised) =>
        string.Equals(pointer, unauthorised, StringComparison.Ordinal)
            ? ContentValue.Unauthorised
            : ContentValue.Number(value);

    private static IEnumerable<ContentValue> CasterRows()
    {
        var rows = new List<(int Chapter, string Biome, string Status, string Flavour, decimal Potency,
            string Basis, decimal Duration, int? MaxStacks, decimal Proc)>
        {
            (1, "Greenwood Vale", "BLEED", "loc.enemy_status.thorn_gash.name", 0.20m,
                "APPLIER_ATK_PCT_PER_SECOND", 3.0m, 1, 0.30m),
            (2, "Ashen Mire", "POISON", "loc.enemy_status.bog_rot.name", 0.015m,
                "TARGET_MAX_HP_PCT_PER_SECOND", 4.0m, 3, 0.30m),
            (3, "Sunken Crypt", "BLEED", "loc.enemy_status.bone_splinter.name", 0.30m,
                "APPLIER_ATK_PCT_PER_SECOND", 4.0m, 1, 0.35m),
            (4, "Emberpeak", "BURN", "loc.enemy_status.magma_splash.name", 0.30m,
                "APPLIER_ATK_PCT_PER_SECOND", 3.0m, 5, 0.35m),
            (5, "Frostbound Reach", "FREEZE", "loc.enemy_status.deep_chill.name", -0.50m,
                "TARGET_ASPD_PCT", 2.0m, null, 0.25m),
            (6, "Clockwork Vaults", "SUNDER", "loc.enemy_status.shear.name", -0.05m,
                "TARGET_DEF_PCT_PER_STACK", 6.0m, 5, 0.35m),
            (7, "Bloom of Decay", "SPORE", "loc.enemy_status.spore_cloud.name", -0.10m,
                "TARGET_HEALING_RECEIVED_PCT_PER_STACK", 8.0m, 4, 0.35m),
            (8, "Astral Spire", "BURN", "loc.enemy_status.starfire.name", 0.40m,
                "APPLIER_ATK_PCT_PER_SECOND", 3.0m, 5, 0.35m),
        };

        return rows.Select(r => ContentValue.Object(
        [
            new("chapter", ContentValue.Number(r.Chapter)),
            new("biome", ContentValue.Text(r.Biome)),
            new("statusId", ContentValue.Text(r.Status)),
            new("flavourName", ContentValue.Text(r.Flavour)),
            new("potency", ContentValue.Number(r.Potency)),
            new("potencyBasis", ContentValue.Text(r.Basis)),
            new("durationSeconds", ContentValue.Number(r.Duration)),
            new("maxStacks", r.MaxStacks is { } stacks
                ? ContentValue.Number(stacks)
                : ContentValue.Unauthorised),
            new("procChancePerLandedHit", ContentValue.Number(r.Proc)),
        ]));
    }

    private static IEnumerable<ContentValue> PoolRows()
    {
        var order = Enum.GetValues<EnemyArchetype>();

        for (var chapter = 1; chapter <= 8; chapter++)
        {
            var weights = PoolWeights[chapter - 1];
            var members = new List<KeyValuePair<string, ContentValue>>();

            for (var i = 0; i < order.Length; i++)
            {
                members.Add(new KeyValuePair<string, ContentValue>(
                    order[i].ToString(), ContentValue.Number(weights[i])));
            }

            var elites = Identities.Skip((chapter - 1) * 2).Take(2)
                .Select(e => ContentValue.Text(e.Key)).ToList();

            yield return ContentValue.Object(
            [
                new("chapter", ContentValue.Number(chapter)),
                new("weights", ContentValue.Object(members)),
                new("elitePool", ContentValue.Array(elites)),
            ]);
        }
    }
}
