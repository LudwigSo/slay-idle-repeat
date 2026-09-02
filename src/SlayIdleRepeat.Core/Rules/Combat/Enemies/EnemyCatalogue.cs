using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// <c>content/enemies/enemies.json</c>, read.
/// </summary>
/// <remarks>
/// Nothing here has a default: every value goes through <see cref="ContentSnapshot"/>'s readers,
/// which throw <c>MissingContentException</c> on an absent pointer and
/// <c>UnauthorisedTunableException</c> on a <c>null</c> — with two declared exceptions where the
/// null is the mechanism: a <c>CASTER</c> row's <c>maxStacks</c> and <c>CURSED</c>'s <c>curseId</c>.
/// Both are read through <see cref="ContentSnapshot.IsAuthorised"/> and carried as <c>null</c>, and
/// both have a <c>Require…</c> accessor that throws by name when something tries to use them.
/// </remarks>
internal sealed record EnemyCatalogue(
    EnemyDerivationConstants Derivation,
    EnemyLevelTable Levels,
    IReadOnlyList<ArchetypeRow> Archetypes,
    OnHitStatus WardenSunder,
    IReadOnlyDictionary<int, OnHitStatus> CasterBiomeStatus,
    double ElitePowerMultiplier,
    int ModifiersPerElite,
    bool NoRepeatWithPreviousEliteInRun,
    IReadOnlyList<EliteModifierRow> EliteModifiers,
    IReadOnlyDictionary<string, EnemyArchetype> EliteIdentities,
    IReadOnlyDictionary<int, ChapterEnemyPool> ChapterPools,
    int DefaultTargetPriority,
    int DeprioritisedTargetPriority,
    int ForcedTargetPriority)
{
    /// <summary>The snapshot-relative path of the document.</summary>
    internal const string Document = "content/enemies/enemies.json";

    /// <summary>The <c>0.60</c> term of <c>MaxHP</c>.</summary>
    internal const string HpPerPowerPointer = Document + "#/derivation/hpPerPower";

    /// <summary>The <c>0.045</c> term of <c>ATK</c>.</summary>
    internal const string AtkPerPowerPointer = Document + "#/derivation/atkPerPower";

    /// <summary>The <c>0.030</c> term of <c>DEF</c>.</summary>
    internal const string DefPerPowerPointer = Document + "#/derivation/defPerPower";

    /// <summary>The <c>1.00</c> term of <c>ASPD</c>.</summary>
    internal const string BaseAspdPointer = Document + "#/derivation/baseAspd";

    /// <summary>Every term's rounding, as authored.</summary>
    internal const string RoundingDecimalsPointer = Document + "#/derivation/roundingDecimals";

    /// <summary>The elite power multiplier.</summary>
    internal const string ElitePowerMultiplierPointer = Document + "#/elites/powerMultiplier";

    /// <summary>The modifiers granted per Elite.</summary>
    internal const string ModifiersPerElitePointer = Document + "#/elites/modifiersPerElite";

    /// <summary>The no-repeat rule's own switch.</summary>
    internal const string NoRepeatPointer = Document + "#/elites/noRepeatWithPreviousEliteInRun";

    /// <summary>The default target priority.</summary>
    internal const string DefaultTargetPriorityPointer = Document + "#/targetPriority/default";

    /// <summary>The deprioritising value.</summary>
    internal const string DeprioritisedTargetPriorityPointer = Document + "#/targetPriority/deprioritised";

    /// <summary>The focus-forcing value.</summary>
    internal const string ForcedTargetPriorityPointer = Document + "#/targetPriority/forced";

    /// <summary>The pointer holding one of the six per-archetype-invariant stats.</summary>
    internal static string FixedStatPointer(StatId stat) => $"{Document}#/derivation/fixedStats/{stat}";

    /// <summary>The pointer holding one tier's level bonus.</summary>
    internal static string TierBonusPointer(string tier) => $"{Document}#/enemyLevel/tierBonus/{tier}";

    /// <summary>
    /// The six stats every archetype gives the same value, named in code rather than discovered from
    /// whatever keys the file holds.
    /// </summary>
    /// <remarks>
    /// A stat that vanished from the data would otherwise be indistinguishable from a stat the
    /// derivation computes. A <see cref="List{T}"/> initialiser, not <c>[ … ]</c>, since a collection
    /// expression here synthesises a global-namespace helper that fails the build.
    /// </remarks>
    internal static IReadOnlyList<StatId> FixedStats { get; } = new List<StatId>
    {
        StatId.BLOCK, StatId.PEN, StatId.DMG_PCT, StatId.DR_PCT, StatId.HEAL_PCT, StatId.THORNS,
    };

    /// <summary>Reads the whole document.</summary>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A value is <c>null</c> where one is required.</exception>
    internal static EnemyCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fixedStats = new Dictionary<StatId, double>(FixedStats.Count);
        foreach (var stat in FixedStats)
        {
            fixedStats[stat] = content.ReadDouble(FixedStatPointer(stat));
        }

        var derivation = new EnemyDerivationConstants(
            content.ReadDouble(HpPerPowerPointer),
            content.ReadDouble(AtkPerPowerPointer),
            content.ReadDouble(DefPerPowerPointer),
            content.ReadDouble(BaseAspdPointer),
            content.ReadInt32(RoundingDecimalsPointer),
            fixedStats);

        // Authored values that would otherwise be dials nothing turns, checked against the code
        // they describe so a divergence is loud rather than silently no-oping.
        RequireAgreement(
            RoundingDecimalsPointer, derivation.RoundingDecimals, StatRounding.Decimals,
            "05 §1.1's rounding is the locked determinism rule and StatRounding is its one " +
            "implementation. This file records the rule it was written under; it cannot retune it.");

        var modifiersPerElite = content.ReadInt32(ModifiersPerElitePointer);

        RequireAgreement(
            ModifiersPerElitePointer, modifiersPerElite, 1,
            "05 §6.2 gives an Elite exactly one modifier and EliteModifierDraw.Draw returns one. A " +
            "second would need a draw that excludes the first, which 05 §6.2 does not state — so " +
            "authoring 2 here has to fail rather than be quietly ignored.");

        return new EnemyCatalogue(
            derivation,
            ReadLevels(content),
            ReadArchetypes(content),
            ReadOnHit(content, Document + "#/onHit/wardenSunder", casterRow: false),
            ReadCasterRows(content),
            content.ReadDouble(ElitePowerMultiplierPointer),
            modifiersPerElite,
            content.ReadBoolean(NoRepeatPointer),
            ReadModifiers(content),
            ReadIdentities(content),
            ReadPools(content),
            content.ReadInt32(DefaultTargetPriorityPointer),
            content.ReadInt32(DeprioritisedTargetPriorityPointer),
            content.ReadInt32(ForcedTargetPriorityPointer));
    }

    /// <summary>The row for one of the eight shapes.</summary>
    /// <exception cref="KeyNotFoundException">The catalogue holds no row for it.</exception>
    internal ArchetypeRow Archetype(EnemyArchetype archetype)
    {
        foreach (var row in Archetypes)
        {
            if (row.Id == archetype)
            {
                return row;
            }
        }

        throw new KeyNotFoundException(
            $"content/enemies/enemies.json states no 05 §6.1 row for {archetype}.");
    }

    /// <summary>
    /// The status this archetype applies on a landed hit in this chapter, or <c>null</c> where the
    /// shape applies none.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// The archetype is a <c>CASTER</c> and the chapter has no biome row.
    /// </exception>
    internal OnHitStatus? OnHitFor(EnemyArchetype archetype, int chapter)
    {
        var row = Archetype(archetype);

        return row.OnHit switch
        {
            ArchetypeOnHit.None => null,
            ArchetypeOnHit.Sunder => WardenSunder,
            ArchetypeOnHit.BiomeStatus => CasterBiomeStatus.TryGetValue(chapter, out var status)
                ? status
                : throw new KeyNotFoundException(
                    $"05 §6.1a states one CASTER row per chapter and content/enemies/enemies.json has " +
                    $"none for chapter {chapter.ToString(CultureInfo.InvariantCulture)}. A CASTER with " +
                    "no biome status is a CASTER that has quietly stopped being one."),
            _ => throw new InvalidOperationException(
                $"05 §6.1a knows no on-hit kind '{row.OnHit}' for {archetype}."),
        };
    }

    /// <summary>The authored row for one modifier.</summary>
    /// <exception cref="KeyNotFoundException">The catalogue holds no row for it.</exception>
    internal EliteModifierRow Modifier(EliteModifier modifier)
    {
        foreach (var row in EliteModifiers)
        {
            if (row.Id == modifier)
            {
                return row;
            }
        }

        throw new KeyNotFoundException(
            $"content/enemies/enemies.json states no 05 §6.2 row for {modifier}.");
    }

    /// <summary>The chapter's pool.</summary>
    /// <exception cref="KeyNotFoundException">The catalogue holds no pool for that chapter.</exception>
    internal ChapterEnemyPool Pool(int chapter) =>
        ChapterPools.TryGetValue(chapter, out var pool)
            ? pool
            : throw new KeyNotFoundException(
                $"content/enemies/enemies.json states no enemy pool for chapter " +
                $"{chapter.ToString(CultureInfo.InvariantCulture)}.");

    // ------------------------------------------------------------------ block readers

    private static EnemyLevelTable ReadLevels(ContentSnapshot content)
    {
        var rows = content.Read(Document + "#/enemyLevel/baseByChapter");
        var baseByChapter = new Dictionary<int, int>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = $"{Document}#/enemyLevel/baseByChapter/{i.ToString(CultureInfo.InvariantCulture)}";
            baseByChapter[content.ReadInt32(pointer + "/chapter")] = content.ReadInt32(pointer + "/level");
        }

        var bonuses = new List<int>(EnemyLevelTable.Ordinals.Count);
        foreach (var tier in EnemyLevelTable.Ordinals)
        {
            bonuses.Add(content.ReadInt32(TierBonusPointer(tier)));
        }

        return EnemyLevelTable.From(baseByChapter, bonuses);
    }

    private static IReadOnlyList<ArchetypeRow> ReadArchetypes(ContentSnapshot content)
    {
        var rows = content.Read(Document + "#/archetypes");
        var archetypes = new List<ArchetypeRow>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = $"{Document}#/archetypes/{i.ToString(CultureInfo.InvariantCulture)}";

            archetypes.Add(new ArchetypeRow(
                ParseArchetype(content.ReadText(pointer + "/id"), pointer + "/id"),
                content.ReadDouble(pointer + "/hpCoef"),
                content.ReadDouble(pointer + "/atkCoef"),
                content.ReadDouble(pointer + "/defCoef"),
                content.ReadDouble(pointer + "/aspdCoef"),
                content.ReadDouble(pointer + "/crit"),
                content.ReadDouble(pointer + "/critDamage"),
                content.ReadDouble(pointer + "/dodge"),
                content.ReadDouble(pointer + "/lifesteal"),
                content.ReadInt32(pointer + "/unitsPerDraw"),
                ParseOnHit(content.ReadText(pointer + "/onHit"), pointer + "/onHit")));
        }

        return archetypes;
    }

    private static IReadOnlyDictionary<int, OnHitStatus> ReadCasterRows(ContentSnapshot content)
    {
        var rows = content.Read(Document + "#/onHit/casterBiomeStatus");
        var byChapter = new Dictionary<int, OnHitStatus>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = $"{Document}#/onHit/casterBiomeStatus/{i.ToString(CultureInfo.InvariantCulture)}";
            byChapter[content.ReadInt32(pointer + "/chapter")] = ReadOnHit(content, pointer, casterRow: true);
        }

        return byChapter;
    }

    /// <remarks>
    /// <c>maxStacks</c> is the one member read through <see cref="ContentSnapshot.IsAuthorised"/>: a
    /// stack count is authored for most <c>CASTER</c> rows but not for FREEZE, which stays
    /// <c>null</c> — see <see cref="OnHitStatus.RequireMaxStacks"/>.
    /// </remarks>
    /// <param name="casterRow">
    /// True for one of the per-chapter <c>CASTER</c> rows. A <c>CASTER</c> row carries a
    /// <c>flavourName</c> and no <c>refreshOnReapply</c>; the <c>WARDEN</c> row is the mirror image.
    /// </param>
    private static OnHitStatus ReadOnHit(ContentSnapshot content, string pointer, bool casterRow)
    {
        var maxStacksPointer = pointer + "/maxStacks";

        return new OnHitStatus(
            content.ReadText(pointer + "/statusId"),
            content.ReadDouble(pointer + "/procChancePerLandedHit"),
            content.ReadDouble(pointer + "/potency"),
            ParsePotencyBasis(content.ReadText(pointer + "/potencyBasis"), pointer + "/potencyBasis"),
            content.ReadDouble(pointer + "/durationSeconds"),
            content.IsAuthorised(maxStacksPointer) ? content.ReadInt32(maxStacksPointer) : null,
            // "Refresh on reapply" is authored for the WARDEN set only; CASTER rows carry null
            // rather than a decided false.
            casterRow ? null : content.ReadBoolean(pointer + "/refreshOnReapply"),
            casterRow ? content.ReadText(pointer + "/flavourName") : null);
    }

    private static IReadOnlyList<EliteModifierRow> ReadModifiers(ContentSnapshot content)
    {
        var rows = content.Read(Document + "#/elites/modifiers");
        var modifiers = new List<EliteModifierRow>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = $"{Document}#/elites/modifiers/{i.ToString(CultureInfo.InvariantCulture)}";

            var authored = content.Read(pointer + "/parameters");
            var parameters = new Dictionary<string, double>(StringComparer.Ordinal);

            // A leading underscore marks a documentation-only member, e.g. "_doc".
            foreach (var name in authored.MemberNames.Where(n => !n.StartsWith('_')))
            {
                parameters[name] = content.ReadDouble($"{pointer}/parameters/{name}");
            }

            // CURSED is the only row that carries curseId, and carries it as null: the curse
            // catalogue does not exist yet, so nothing is coerced to a fabricated id.
            var curseIdPointer = pointer + "/curseId";
            var curseId = content.IsAuthorised(curseIdPointer) ? content.ReadText(curseIdPointer) : null;

            modifiers.Add(new EliteModifierRow(
                ParseModifier(content.ReadText(pointer + "/id"), pointer + "/id"),
                content.ReadText(pointer + "/displayName"),
                parameters,
                curseId));
        }

        return modifiers;
    }

    private static IReadOnlyDictionary<string, EnemyArchetype> ReadIdentities(ContentSnapshot content)
    {
        var rows = content.Read(Document + "#/elites/identities");
        var identities = new Dictionary<string, EnemyArchetype>(rows.Items.Count, StringComparer.Ordinal);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = $"{Document}#/elites/identities/{i.ToString(CultureInfo.InvariantCulture)}";

            identities[content.ReadText(pointer + "/id")] =
                ParseArchetype(content.ReadText(pointer + "/baseArchetype"), pointer + "/baseArchetype");
        }

        return identities;
    }

    private static IReadOnlyDictionary<int, ChapterEnemyPool> ReadPools(ContentSnapshot content)
    {
        var rows = content.Read(Document + "#/chapterPools");
        var pools = new Dictionary<int, ChapterEnemyPool>(rows.Items.Count);

        for (var i = 0; i < rows.Items.Count; i++)
        {
            var pointer = $"{Document}#/chapterPools/{i.ToString(CultureInfo.InvariantCulture)}";
            var chapter = content.ReadInt32(pointer + "/chapter");

            var weights = new List<ArchetypeWeight>();
            foreach (var archetype in Enum.GetValues<EnemyArchetype>())
            {
                weights.Add(new ArchetypeWeight(archetype, content.ReadDouble($"{pointer}/weights/{archetype}")));
            }

            var elitePool = new List<string>();
            var authored = content.Read(pointer + "/elitePool");
            for (var e = 0; e < authored.Items.Count; e++)
            {
                elitePool.Add(content.ReadText($"{pointer}/elitePool/{e.ToString(CultureInfo.InvariantCulture)}"));
            }

            pools[chapter] = ChapterEnemyPool.From(chapter, weights, elitePool);
        }

        return pools;
    }

    /// <summary>
    /// An authored number that has to agree with the code it describes, or the key is a dial
    /// nothing turns.
    /// </summary>
    /// <exception cref="ContentTypeMismatchException">The two have diverged.</exception>
    private static void RequireAgreement(string pointer, int authored, int required, string why)
    {
        if (authored == required)
        {
            return;
        }

        throw new ContentTypeMismatchException(
            pointer, ContentValueKind.Number,
            $"{authored.ToString(CultureInfo.InvariantCulture)}, but the code it describes is fixed at " +
            $"{required.ToString(CultureInfo.InvariantCulture)}. {why}");
    }

    // ------------------------------------------------------------------ vocabulary parsing

    private static EnemyArchetype ParseArchetype(string name, string pointer) =>
        EnemyArchetypes.TryParse(name, out var archetype)
            ? archetype
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 05 §6.1's eight archetypes " +
                $"({EnemyArchetypes.Names}). The table is closed: a ninth " +
                "shape is a design decision, not a data edit");

    private static ArchetypeOnHit ParseOnHit(string name, string pointer) => name switch
    {
        "NONE" => ArchetypeOnHit.None,
        "SUNDER" => ArchetypeOnHit.Sunder,
        "BIOME_STATUS" => ArchetypeOnHit.BiomeStatus,
        _ => throw new ContentTypeMismatchException(
            pointer, ContentValueKind.Text,
            $"'{name}', which is not an on-hit kind 05 §6.1a states. It names two — WARDEN's SUNDER " +
            "and CASTER's biome status — and NONE for the other six shapes"),
    };

    private static PotencyBasis ParsePotencyBasis(string name, string pointer) => name switch
    {
        "APPLIER_ATK_PCT_PER_SECOND" => PotencyBasis.ApplierAtkPctPerSecond,
        "TARGET_MAX_HP_PCT_PER_SECOND" => PotencyBasis.TargetMaxHpPctPerSecond,
        "TARGET_ASPD_PCT" => PotencyBasis.TargetAspdPct,
        "TARGET_DEF_PCT_PER_STACK" => PotencyBasis.TargetDefPctPerStack,
        "TARGET_HEALING_RECEIVED_PCT_PER_STACK" => PotencyBasis.TargetHealingReceivedPctPerStack,
        _ => throw new ContentTypeMismatchException(
            pointer, ContentValueKind.Text,
            $"'{name}', which is not a potency basis 05 §6.1a states. The five are a transcription of " +
            "the units that table already writes; a sixth would mean a unit nobody authored"),
    };

    private static EliteModifier ParseModifier(string name, string pointer) =>
        Enum.TryParse<EliteModifier>(name, ignoreCase: false, out var modifier) && Enum.IsDefined(modifier)
            ? modifier
            : throw new ContentTypeMismatchException(
                pointer, ContentValueKind.Text,
                $"'{name}', which is not one of 05 §6.2's eight Elite Modifiers " +
                $"({string.Join(", ", Enum.GetNames<EliteModifier>())})");
}
