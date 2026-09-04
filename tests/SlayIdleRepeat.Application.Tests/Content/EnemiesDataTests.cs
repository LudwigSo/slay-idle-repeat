using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 The shipped <c>content/enemies/enemies.json</c> against `05` §6–§6.2 and §6.4 — the
/// transcription, asserted where the real file can actually be read.
/// </summary>
/// <remarks>
/// <para>
/// <c>SlayIdleRepeat.Core.Tests</c> owns the <em>behaviour</em> of the derivation and the draw; it
/// references <c>SlayIdleRepeat.Core</c> and nothing else, so it has no JSON reader and cannot see
/// this document. This suite is where the document's ~100 numbers meet the design sections that
/// authorise them — <c>game-data/README.md</c>: <em>"a number nobody can trace to a section is a
/// number nobody will defend."</em>
/// </para>
/// <para>
/// 🔒 Every value is read with a <see cref="ContentSnapshot"/> reader, which throws on a <c>null</c>.
/// A coefficient that was quietly nulled fails here rather than reading as zero.
/// </para>
/// </remarks>
public sealed class EnemiesDataTests
{
    private const string Document = "content/enemies/enemies.json";

    /// <summary>`05` §6.1's eight shapes, in the document's own row order.</summary>
    private static readonly string[] ArchetypeOrder =
    {
        "GRUNT", "SWARM", "BRUTE", "SKIRMISHER", "WARDEN", "CASTER", "LEECH", "REAVER",
    };

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_document_is_in_the_snapshot_and_under_content_rather_than_tuning()
    {
        var snapshot = Data();

        snapshot.DocumentPaths.ShouldContain(Document);

        snapshot.DocumentPaths
            .Count(p => p.StartsWith("tuning/", StringComparison.Ordinal))
            .ShouldBe(16, "doc 21's catalogue names exactly sixteen tuning files; enemies.json is not a seventeenth");
    }

    /// <summary>
    /// The file has a schema, and the schema is what closes `05` §6's five 📐 baseline entries: the
    /// audit read <c>game-data/schema/</c> and never <c>content/</c>, so a data file with no schema
    /// would close nothing and be unvalidated besides.
    /// </summary>
    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(Document).ShouldBe("schema/enemies.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/enemies.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/enemies.schema.json");
    }

    // ------------------------------------------------------------------ 05 §6, the derivation

    /// <summary>`05` §6's <c>EnemyStats(power, archetype)</c> constants, term by term.</summary>
    [Theory]
    [InlineData("hpPerPower", 0.60)]
    [InlineData("atkPerPower", 0.045)]
    [InlineData("defPerPower", 0.030)]
    [InlineData("baseAspd", 1.00)]
    public void The_derivation_constants_are_the_numbers_05_section_6_states(string term, double expected) =>
        Data().ReadDouble($"{Document}#/derivation/{term}").ShouldBe(expected);

    /// <summary>`05` §6 — <em>"every term rounded to 4 dp (§1.1)"</em>.</summary>
    [Fact]
    public void The_rounding_is_the_four_decimal_places_05_section_1_1_locks() =>
        Data().ReadInt32($"{Document}#/derivation/roundingDecimals").ShouldBe(4);

    /// <summary>
    /// `05` §6 — the six stats every archetype shares. <c>DMG_PCT</c> and <c>DR_PCT</c> are 1.00
    /// under `16` D46: they are multiplier stats consumed bare, so 0 here would zero every enemy's
    /// damage and delete every hit an enemy takes.
    /// </summary>
    [Theory]
    [InlineData("BLOCK", 0.00)]
    [InlineData("PEN", 0.00)]
    [InlineData("DMG_PCT", 1.00)]
    [InlineData("DR_PCT", 1.00)]
    [InlineData("HEAL_PCT", 1.00)]
    [InlineData("THORNS", 0.00)]
    public void The_six_invariant_stats_are_05_section_6s_values(string stat, double expected) =>
        Data().ReadDouble($"{Document}#/derivation/fixedStats/{stat}").ShouldBe(expected);

    // ------------------------------------------------------------------ 05 §6.0, the level table

    /// <summary>`05` §6.0 — Ch1 10 · Ch2 15 · Ch3 20 · Ch4 30 · Ch5 40 · Ch6 50 · Ch7 60 · Ch8 80.</summary>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 15)]
    [InlineData(3, 20)]
    [InlineData(4, 30)]
    [InlineData(5, 40)]
    [InlineData(6, 50)]
    [InlineData(7, 60)]
    [InlineData(8, 80)]
    public void BaseEnemyLevel_is_05_section_6_0s_chapter_table(int chapter, int level)
    {
        var data = Data();
        var index = chapter - 1;

        data.ReadInt32($"{Document}#/enemyLevel/baseByChapter/{index}/chapter").ShouldBe(chapter);
        data.ReadInt32($"{Document}#/enemyLevel/baseByChapter/{index}/level").ShouldBe(level);
    }

    /// <summary>`05` §6.0 — Normal +0 · Heroic +10 · Mythic +20.</summary>
    [Theory]
    [InlineData("NORMAL", 0)]
    [InlineData("HEROIC", 10)]
    [InlineData("MYTHIC", 20)]
    public void TierLevelBonus_is_05_section_6_0s_tier_table(string tier, int bonus) =>
        Data().ReadInt32($"{Document}#/enemyLevel/tierBonus/{tier}").ShouldBe(bonus);

    // ------------------------------------------------------------------ 05 §6.1, the archetypes

    /// <summary>
    /// `05` §6.1's table, every cell. Ten numbers per row, eight rows — the transcription this task
    /// exists for.
    /// </summary>
    [Theory]
    [InlineData("GRUNT", 1.00, 1.00, 1.00, 1.00, 0.05, 0.50, 0.02, 0.00, 1, "NONE")]
    [InlineData("SWARM", 0.35, 0.55, 0.60, 1.30, 0.05, 0.50, 0.05, 0.00, 3, "NONE")]
    [InlineData("BRUTE", 2.00, 1.35, 1.20, 0.60, 0.05, 0.75, 0.00, 0.00, 1, "NONE")]
    [InlineData("SKIRMISHER", 0.70, 0.85, 0.70, 1.70, 0.10, 0.50, 0.15, 0.00, 1, "NONE")]
    [InlineData("WARDEN", 1.60, 0.70, 2.20, 0.85, 0.03, 0.50, 0.02, 0.00, 1, "SUNDER")]
    [InlineData("CASTER", 0.75, 1.50, 0.55, 0.70, 0.08, 0.60, 0.03, 0.00, 1, "BIOME_STATUS")]
    [InlineData("LEECH", 1.10, 0.95, 0.90, 1.10, 0.05, 0.50, 0.03, 0.25, 1, "NONE")]
    [InlineData("REAVER", 0.90, 1.20, 0.80, 1.00, 0.30, 1.20, 0.05, 0.00, 1, "NONE")]
    public void Each_archetype_row_is_05_section_6_1s_row(
        string id, double hp, double atk, double def, double aspd,
        double crit, double critDamage, double dodge, double lifesteal, int units, string onHit)
    {
        var data = Data();
        var row = $"{Document}#/archetypes/{Array.IndexOf(ArchetypeOrder, id)}";

        data.ReadText($"{row}/id").ShouldBe(id);
        data.ReadDouble($"{row}/hpCoef").ShouldBe(hp);
        data.ReadDouble($"{row}/atkCoef").ShouldBe(atk);
        data.ReadDouble($"{row}/defCoef").ShouldBe(def);
        data.ReadDouble($"{row}/aspdCoef").ShouldBe(aspd);
        data.ReadDouble($"{row}/crit").ShouldBe(crit);
        data.ReadDouble($"{row}/critDamage").ShouldBe(critDamage);
        data.ReadDouble($"{row}/dodge").ShouldBe(dodge);
        data.ReadDouble($"{row}/lifesteal").ShouldBe(lifesteal);
        data.ReadInt32($"{row}/unitsPerDraw").ShouldBe(units);
        data.ReadText($"{row}/onHit").ShouldBe(onHit);
    }

    /// <summary>
    /// 🔒 S3 — the floor under the theory above. Every case indexes <see cref="ArchetypeOrder"/>; if
    /// the document's rows were reordered or a row were dropped, each case would silently assert a
    /// different row or read a missing one.
    /// </summary>
    [Fact]
    public void The_eight_archetype_rows_are_in_the_order_this_suite_indexes_them_by()
    {
        var data = Data();

        for (var i = 0; i < ArchetypeOrder.Length; i++)
        {
            data.ReadText($"{Document}#/archetypes/{i}/id").ShouldBe(ArchetypeOrder[i]);
        }

        Should.Throw<MissingContentException>(
            () => data.ReadText($"{Document}#/archetypes/{ArchetypeOrder.Length}/id"),
            "05 §6.1's table is closed at eight rows");
    }

    // ------------------------------------------------------------------ 05 §6.1a, the on-hit tables

    /// <summary>`05` §6.1a — <c>WARDEN</c>'s <c>SUNDER</c>, the one parameter set for the whole game.</summary>
    [Fact]
    public void The_WARDEN_SUNDER_parameter_set_is_05_section_6_1as()
    {
        var data = Data();
        var sunder = $"{Document}#/onHit/wardenSunder";

        data.ReadText($"{sunder}/statusId").ShouldBe("SUNDER");
        data.ReadDouble($"{sunder}/procChancePerLandedHit").ShouldBe(0.35);
        data.ReadDouble($"{sunder}/potency").ShouldBe(-0.05, "05 §6.1a — −5% DEF per stack");
        data.ReadText($"{sunder}/potencyBasis").ShouldBe("TARGET_DEF_PCT_PER_STACK");
        data.ReadDouble($"{sunder}/durationSeconds").ShouldBe(6.0);
        data.ReadBoolean($"{sunder}/refreshOnReapply").ShouldBeTrue("05 §6.1a — refresh on reapply");
        data.ReadInt32($"{sunder}/maxStacks").ShouldBe(5);
    }

    /// <summary>`05` §6.1a — one <c>CASTER</c> row per chapter, every column.</summary>
    [Theory]
    [InlineData(1, "BLEED", "loc.enemy_status.thorn_gash.name", 0.20, "APPLIER_ATK_PCT_PER_SECOND", 3.0, 0.30)]
    [InlineData(2, "POISON", "loc.enemy_status.bog_rot.name", 0.20, "APPLIER_ATK_PCT_PER_SECOND", 4.0, 0.30)]
    [InlineData(3, "BLEED", "loc.enemy_status.bone_splinter.name", 0.30, "APPLIER_ATK_PCT_PER_SECOND", 4.0, 0.35)]
    [InlineData(4, "BURN", "loc.enemy_status.magma_splash.name", 0.30, "APPLIER_ATK_PCT_PER_SECOND", 3.0, 0.35)]
    [InlineData(5, "FREEZE", "loc.enemy_status.deep_chill.name", -0.50, "TARGET_ASPD_PCT", 2.0, 0.25)]
    [InlineData(6, "SUNDER", "loc.enemy_status.shear.name", -0.05, "TARGET_DEF_PCT_PER_STACK", 6.0, 0.35)]
    [InlineData(7, "SPORE", "loc.enemy_status.spore_cloud.name", -0.10, "TARGET_HEALING_RECEIVED_PCT_PER_STACK", 8.0, 0.35)]
    [InlineData(8, "BURN", "loc.enemy_status.starfire.name", 0.40, "APPLIER_ATK_PCT_PER_SECOND", 3.0, 0.35)]
    public void Each_CASTER_biome_row_is_05_section_6_1as_row(
        int chapter, string status, string flavour, double potency, string basis, double duration, double proc)
    {
        var data = Data();
        var row = $"{Document}#/onHit/casterBiomeStatus/{chapter - 1}";

        data.ReadInt32($"{row}/chapter").ShouldBe(chapter);
        data.ReadText($"{row}/statusId").ShouldBe(status);
        data.ReadText($"{row}/flavourName").ShouldBe(flavour);
        data.ReadDouble($"{row}/potency").ShouldBe(potency);
        data.ReadText($"{row}/potencyBasis").ShouldBe(basis);
        data.ReadDouble($"{row}/durationSeconds").ShouldBe(duration);
        data.ReadDouble($"{row}/procChancePerLandedHit").ShouldBe(proc);
    }

    /// <summary>
    /// 🔒 `05` §6.1a's stack counts, including the one hole. It states a count for five rows, `05`'s
    /// status table fixes <c>BLEED</c> as non-stacking, and nothing anywhere states one for
    /// <c>FREEZE</c> — so chapter 5 is <c>null</c> and stays <c>null</c> (`16` R6).
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    [InlineData(4, 5)]
    [InlineData(5, null)]
    [InlineData(6, 5)]
    [InlineData(7, 4)]
    [InlineData(8, 5)]
    public void The_CASTER_stack_counts_are_authored_where_05_states_one_and_null_where_it_does_not(
        int chapter, int? maxStacks)
    {
        var data = Data();
        var pointer = $"{Document}#/onHit/casterBiomeStatus/{chapter - 1}/maxStacks";

        if (maxStacks is { } expected)
        {
            data.ReadInt32(pointer).ShouldBe(expected);
            return;
        }

        data.IsAuthorised(pointer).ShouldBeFalse(
            "05 §6.1a states no stack count for FREEZE and neither does 05's status table");
        Should.Throw<UnauthorisedTunableException>(() => data.ReadInt32(pointer));
    }

    // ------------------------------------------------------------------ 05 §6.2, the elites

    [Fact]
    public void The_elite_treatment_is_05_section_6_2s()
    {
        var data = Data();

        data.ReadInt32($"{Document}#/elites/modifiersPerElite")
            .ShouldBe(1, "05 §6.2 — plus ONE Elite Modifier");
        data.ReadBoolean($"{Document}#/elites/noRepeatWithPreviousEliteInRun")
            .ShouldBeTrue("05 §6.2 — no Elite may draw the same modifier as the immediately preceding one");
    }

    /// <summary>`05` §6.2 — the eight modifiers and every number the section states for them.</summary>
    [Theory]
    [InlineData(0, "ENRAGED", "atkMult", 1.50)]
    [InlineData(0, "ENRAGED", "belowHpFraction", 0.40)]
    [InlineData(1, "ARMORED", "defMult", 1.80)]
    [InlineData(1, "ARMORED", "aspdMult", 0.80)]
    [InlineData(2, "VAMPIRIC", "lifesteal", 0.35)]
    [InlineData(3, "VOLATILE", "deathExplosionHeroMaxHpPct", 0.15)]
    [InlineData(4, "SHIELDED", "startingWardMaxHpPct", 0.30)]
    [InlineData(5, "SWIFT", "aspdMult", 1.60)]
    [InlineData(6, "CURSED", "killWithinSeconds", 20.0)]
    [InlineData(7, "REFLECTIVE", "thorns", 0.25)]
    public void Each_elite_modifier_carries_05_section_6_2s_numbers(
        int index, string id, string parameter, double expected)
    {
        var data = Data();

        data.ReadText($"{Document}#/elites/modifiers/{index}/id").ShouldBe(id);
        data.ReadDouble($"{Document}#/elites/modifiers/{index}/parameters/{parameter}").ShouldBe(expected);
    }

    /// <summary>
    /// 🔒 `05` §6.2 names no curse for <c>CURSED</c> and <c>content/curses/</c> is empty, so the id
    /// is <c>null</c> — and it is the only row that carries the key at all, so the hole is greppable.
    /// </summary>
    [Fact]
    public void CURSEDs_curse_id_is_the_one_authored_hole_in_the_elite_block()
    {
        var data = Data();

        data.IsAuthorised($"{Document}#/elites/modifiers/6/curseId").ShouldBeFalse();
        Should.Throw<UnauthorisedTunableException>(
            () => data.ReadText($"{Document}#/elites/modifiers/6/curseId"));

        for (var i = 0; i < 8; i++)
        {
            if (i == 6)
            {
                continue;
            }

            Should.Throw<MissingContentException>(
                () => data.ReadText($"{Document}#/elites/modifiers/{i}/curseId"),
                "only CURSED carries the key, because only CURSED applies a curse");
        }
    }

    /// <summary>🔒 `05` §6.2 — the sixteen identities, the single source of truth for the mapping.</summary>
    [Theory]
    [InlineData(0, "EL_THORN_SENTINEL", "WARDEN")]
    [InlineData(1, "EL_MOSSBACK_ALPHA", "BRUTE")]
    [InlineData(2, "EL_BOGFATHER", "CASTER")]
    [InlineData(3, "EL_MIRESTALKER", "SKIRMISHER")]
    [InlineData(4, "EL_BONE_CHOIR", "CASTER")]
    [InlineData(5, "EL_GRAVE_TITAN", "BRUTE")]
    [InlineData(6, "EL_MAGMA_HERALD", "REAVER")]
    [InlineData(7, "EL_ASHWING", "SKIRMISHER")]
    [InlineData(8, "EL_RIMEFANG_WARDEN", "WARDEN")]
    [InlineData(9, "EL_GLACIER_MAW", "BRUTE")]
    [InlineData(10, "EL_COGWRIGHT", "CASTER")]
    [InlineData(11, "EL_STEAMBREAKER", "BRUTE")]
    [InlineData(12, "EL_SPORELORD", "CASTER")]
    [InlineData(13, "EL_ROTVINE", "LEECH")]
    [InlineData(14, "EL_STARSCRIBE", "CASTER")]
    [InlineData(15, "EL_VOIDCALF", "LEECH")]
    public void Each_elite_identity_maps_to_05_section_6_2s_base_archetype(
        int index, string id, string archetype)
    {
        var data = Data();

        data.ReadText($"{Document}#/elites/identities/{index}/id").ShouldBe(id);
        data.ReadText($"{Document}#/elites/identities/{index}/baseArchetype").ShouldBe(archetype);
    }

    /// <summary>🔒 S3 — exactly sixteen, so the theory above cannot silently cover a shorter list.</summary>
    [Fact]
    public void There_are_exactly_sixteen_elite_identities()
    {
        var data = Data();

        data.ReadText($"{Document}#/elites/identities/15/id").ShouldBe("EL_VOIDCALF");
        Should.Throw<MissingContentException>(() => data.ReadText($"{Document}#/elites/identities/16/id"));

        data.ReadText($"{Document}#/elites/modifiers/7/id").ShouldBe("REFLECTIVE");
        Should.Throw<MissingContentException>(() => data.ReadText($"{Document}#/elites/modifiers/8/id"));
    }

    // ------------------------------------------------------------------ 05 §6.4, the pools

    /// <summary>`05` §6.4's weight table, every cell of every row.</summary>
    /// <remarks>
    /// Chapter 1 departs from the table in one cell by ruling (2026-09-03): its LEECH weight is 0,
    /// because the chapter ships no LEECH model (assets/GREENWOOD_VALE_CAST.md), and the five points
    /// went to GRUNT so the row still sums to 100. Every other row is the table's.
    /// </remarks>
    [Theory]
    [InlineData(1, 45, 20, 15, 10, 5, 5, 0, 0)]
    [InlineData(2, 25, 15, 10, 10, 5, 20, 15, 0)]
    [InlineData(3, 20, 25, 15, 5, 10, 15, 5, 5)]
    [InlineData(4, 20, 10, 20, 10, 5, 20, 5, 10)]
    [InlineData(5, 15, 10, 20, 15, 20, 10, 5, 5)]
    [InlineData(6, 15, 15, 15, 15, 20, 10, 0, 10)]
    [InlineData(7, 10, 20, 10, 10, 5, 20, 20, 5)]
    [InlineData(8, 10, 10, 15, 15, 10, 15, 10, 15)]
    public void Each_chapter_pool_row_is_05_section_6_4s_row(
        int chapter, int grunt, int swarm, int brute, int skirmisher,
        int warden, int caster, int leech, int reaver)
    {
        var data = Data();
        var row = $"{Document}#/chapterPools/{chapter - 1}";
        var expected = new[] { grunt, swarm, brute, skirmisher, warden, caster, leech, reaver };

        data.ReadInt32($"{row}/chapter").ShouldBe(chapter);

        for (var i = 0; i < ArchetypeOrder.Length; i++)
        {
            data.ReadInt32($"{row}/weights/{ArchetypeOrder[i]}")
                .ShouldBe(expected[i], $"05 §6.4 — chapter {chapter}'s {ArchetypeOrder[i]} weight");
        }

        expected.Sum().ShouldBe(100, "05 §6.4 — weights per row sum to 100");
    }

    /// <summary>
    /// 🔒 <c>05</c> §6.2's elite power multiplier, which is authored per chapter rather than once
    /// globally. It has to be, because an Elite and a MINI-BOSS are one code path
    /// (<c>run-minibosses</c> D7) and a mini-boss is unskippable: at §6.2's 2.2, chapter 1's stage-1
    /// <c>WARDEN</c> mini-boss reached <c>DEF 201</c> against a hero with no gear, which is a 197 s
    /// time-to-kill against a 90 s fight cap. Chapters 2-8 carry the section's 2.2 verbatim, and the
    /// row is required on all eight so one chapter's elites cannot be freed by an omission.
    /// </summary>
    [Theory]
    [InlineData(1, 1.4)]
    [InlineData(2, 2.2)]
    [InlineData(3, 2.2)]
    [InlineData(4, 2.2)]
    [InlineData(5, 2.2)]
    [InlineData(6, 2.2)]
    [InlineData(7, 2.2)]
    [InlineData(8, 2.2)]
    public void Each_chapter_pool_row_authors_its_own_elite_power_multiplier(
        int chapter, double expected)
    {
        Data().ReadDouble($"{Document}#/chapterPools/{chapter - 1}/elitePowerMultiplier")
            .ShouldBe(expected, $"05 §6.2 — chapter {chapter}'s elite power multiplier");
    }

    /// <summary>
    /// 🔒 The authored zeros, pinned by name and separately from the totals. `05` §6.4 states them as
    /// intent — <em>"no 30%-crit spikes in the tutorial chapter"</em> and <em>"machines do not
    /// drink"</em> — and a row that moved five points from <c>GRUNT</c> to <c>REAVER</c> still sums
    /// to 100. A test that only checked the total would let either be edited away in silence.
    /// </summary>
    [Fact]
    public void Chapter_1_has_no_REAVER_and_no_LEECH_and_chapter_6_no_LEECH()
    {
        var data = Data();

        data.ReadInt32($"{Document}#/chapterPools/0/weights/REAVER")
            .ShouldBe(0, "05 §6.4 — no 30%-crit spikes in the tutorial chapter");

        data.ReadInt32($"{Document}#/chapterPools/5/weights/LEECH")
            .ShouldBe(0, "05 §6.4 — machines do not drink");

        data.ReadInt32($"{Document}#/chapterPools/0/weights/LEECH")
            .ShouldBe(0, "chapter 1 fields only the archetypes it has a 3D model for; LEECH has none (assets/GREENWOOD_VALE_CAST.md)");

        // And the whole zero set is pinned, so this rule cannot pass by the table having collapsed —
        // and so that a NEW zero somewhere else has to be argued for rather than appearing.
        // ⚠️ There are FOUR: 05 §6.4's shape-intent prose calls out Chapter 1's REAVER and
        // Chapter 6's LEECH; its table also gives Chapter 2 a REAVER weight of 0 and says nothing
        // about it; and Chapter 1's LEECH went to 0 on 2026-09-03 because the chapter ships no LEECH
        // model (the six modelled archetypes are the pool). Recorded here rather than quietly asserted
        // away — the prose zeros are pinned individually above; this is the complete set the table holds.
        var zeros = new List<string>();
        for (var chapter = 1; chapter <= 8; chapter++)
        {
            foreach (var archetype in ArchetypeOrder)
            {
                if (data.ReadInt32($"{Document}#/chapterPools/{chapter - 1}/weights/{archetype}") == 0)
                {
                    zeros.Add($"ch{chapter}:{archetype}");
                }
            }
        }

        zeros.ShouldBe(new[] { "ch1:REAVER", "ch1:LEECH", "ch2:REAVER", "ch6:LEECH" }, ignoreOrder: true);
    }

    /// <summary>`05` §6.2/§6.4 — each chapter's <c>elitePool</c> is exactly its two biome elites.</summary>
    [Theory]
    [InlineData(1, "EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA")]
    [InlineData(2, "EL_BOGFATHER", "EL_MIRESTALKER")]
    [InlineData(3, "EL_BONE_CHOIR", "EL_GRAVE_TITAN")]
    [InlineData(4, "EL_MAGMA_HERALD", "EL_ASHWING")]
    [InlineData(5, "EL_RIMEFANG_WARDEN", "EL_GLACIER_MAW")]
    [InlineData(6, "EL_COGWRIGHT", "EL_STEAMBREAKER")]
    [InlineData(7, "EL_SPORELORD", "EL_ROTVINE")]
    [InlineData(8, "EL_STARSCRIBE", "EL_VOIDCALF")]
    public void Each_chapters_elite_pool_is_its_two_biome_elites(int chapter, string first, string second)
    {
        var data = Data();
        var row = $"{Document}#/chapterPools/{chapter - 1}/elitePool";

        data.ReadText($"{row}/0").ShouldBe(first);
        data.ReadText($"{row}/1").ShouldBe(second);
        Should.Throw<MissingContentException>(() => data.ReadText($"{row}/2"),
            "05 §6.2 — exactly its two biome elites");
    }

    // ------------------------------------------------------------------ 05 §3.2, targetPriority

    [Fact]
    public void The_target_priority_field_is_declared_with_05_section_3_2s_three_values()
    {
        var data = Data();

        data.ReadInt32($"{Document}#/targetPriority/default").ShouldBe(0);
        data.ReadInt32($"{Document}#/targetPriority/deprioritised").ShouldBe(-1);
        data.ReadInt32($"{Document}#/targetPriority/forced").ShouldBe(1);
    }

    // ------------------------------------------------------------------ the declared rules bite

    /// <summary>
    /// 🔒 `05` §6.4's sum-to-100 rule is enforced, not merely true today. A single edit fails the
    /// content build.
    /// </summary>
    [Fact]
    public void A_pool_row_that_stops_summing_to_one_hundred_fails_the_build()
    {
        var edited = RepoData.SourceWithEdit(
            Document,
            "\"GRUNT\": 45, \"SWARM\": 20",
            "\"GRUNT\": 46, \"SWARM\": 20");

        var issues = ContentLoader.Load(edited).Issues;

        issues.ShouldContain(
            i => i.Location == $"{Document}#/chapterPools/0/weights" && i.Message.Contains("101"),
            "the declared rule names the row whose weights stopped summing to 100");
    }

    /// <summary>
    /// 🔒 `05` §6.2 — an identity in no chapter's <c>elitePool</c> is an elite the game can never
    /// present, and one in two pools is a biome leak. The edit below causes both at once.
    /// </summary>
    /// <remarks>
    /// ⚠️ The obvious edit — repeating an id inside one pool — cannot be used: the schema's
    /// <c>uniqueItems</c> rejects it first, and the declared rule would never run. Moving a
    /// <em>different</em> chapter's elite in is what exercises the rule.
    /// </remarks>
    [Fact]
    public void An_elite_identity_in_no_chapters_pool_or_in_two_of_them_fails_the_build()
    {
        var edited = RepoData.SourceWithEdit(
            Document,
            "[\"EL_STARSCRIBE\", \"EL_VOIDCALF\"]",
            "[\"EL_STARSCRIBE\", \"EL_THORN_SENTINEL\"]");

        var issues = ContentLoader.Load(edited).Issues;

        issues.ShouldContain(
            i => i.Location == $"{Document}#/elites/identities" && i.Message.Contains("EL_VOIDCALF"),
            "the orphaned identity is named, not merely counted");

        issues.ShouldContain(
            i => i.Location == $"{Document}#/chapterPools" && i.Message.Contains("EL_THORN_SENTINEL"),
            "and the elite that is now in two chapters' pools is named too");
    }

    /// <summary>
    /// 🔒 `05` §6.0 — a chapter that fields enemies and has no <c>BaseEnemyLevel</c> would derive
    /// level-0 enemies, which `05` §4's mitigation denominator reads as an attacker that never grows.
    /// </summary>
    /// <remarks>
    /// ⚠️ The obvious edits are blocked by the schema — <c>baseByChapter</c> is <c>minItems</c>/
    /// <c>maxItems</c> 8 and <c>chapter</c> is 1..8 — so the edit below re-points chapter 8's row at
    /// chapter 7 instead. Still eight rows, still valid chapters, still unique; chapter 8 then has a
    /// pool and no level, which is exactly the state the rule exists to catch.
    /// </remarks>
    [Fact]
    public void A_chapter_that_fields_enemies_with_no_base_level_fails_the_build()
    {
        var edited = RepoData.SourceWithEdit(
            Document,
            "{ \"chapter\": 8, \"level\": 80 }",
            "{ \"chapter\": 7, \"level\": 80 }");

        var issues = ContentLoader.Load(edited).Issues;

        issues.ShouldContain(
            i => i.Location == $"{Document}#/enemyLevel/baseByChapter" && i.Message.Contains("chapter 8"),
            "the declared rule names the chapter that would field level-0 enemies");
    }
}
