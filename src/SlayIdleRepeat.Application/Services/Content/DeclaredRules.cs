using System.Globalization;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The named cross-file rules the design documents state and no schema can hold: two files that
/// must agree, a value that must be derivable from another, an id that must be classified
/// somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// Every rule is vacuous when its documents are absent: a rule waiting for later content passes
/// over an empty subject set rather than being switched off, and bites the day the data lands.
/// </para>
/// <para>
/// A rule must never treat an unauthorised (null) value as zero — where a leaf is unauthorised the
/// rule skips, since the design docs have not authorised a number and inventing one would be the
/// exact bug the null convention exists to prevent.
/// </para>
/// <para>
/// Rules deliberately <b>not</b> stated, because the data contradicts them today and the conflict
/// is documented rather than accidental:
/// <list type="bullet">
/// <item><c>totalRewardedPerDaySoftCap &gt;= maxInRun + maxMeta</c> — the soft cap sits
/// deliberately below the theoretical maximum.</item>
/// <item><c>hasPlus =&gt; ad rates are null</c> — <c>Plus_Lapsed</c> watches ads by design.</item>
/// </list>
/// </para>
/// </remarks>
internal static class DeclaredRules
{
    private delegate void Rule(IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues);

    /// <summary>A rule whose authority is a schema rather than a second data file.</summary>
    private delegate void SchemaAwareRule(
        IReadOnlyDictionary<string, ContentValue> documents,
        IReadOnlyDictionary<string, ContentValue> schemas,
        List<ContentIssue> issues);

    /// <summary>Runs every declared rule.</summary>
    /// <param name="documents">Data documents by snapshot-relative path. Schemas excluded.</param>
    /// <param name="schemas">
    /// The parsed schema set, which the <see cref="SchemaAwareRules"/> need and the rest do not — an
    /// embedded effect is validated against <c>schema/effect.schema.json</c>, and a chapter's unlock
    /// gate is cross-checked against <c>schema/chapter.schema.json</c>.
    /// </param>
    /// <param name="issues">Findings are appended here.</param>
    internal static void Check(
        IReadOnlyDictionary<string, ContentValue> documents,
        IReadOnlyDictionary<string, ContentValue> schemas,
        List<ContentIssue> issues)
    {
        foreach (var rule in Rules)
        {
            rule(documents, issues);
        }

        foreach (var rule in SchemaAwareRules)
        {
            rule(documents, schemas, issues);
        }
    }

    /// <summary>
    /// The rules that read the schema set. Listed rather than called one by one, so a third of them
    /// is an entry here rather than another line in <see cref="Check"/>.
    /// </summary>
    private static readonly SchemaAwareRule[] SchemaAwareRules =
    [
        // Every effect embedded in an owning content file validates against the one file that
        // states the effect partition — an owning schema can neither $ref it nor restate it.
        EmbeddedEffectsValidateAgainstTheEffectSchema,

        // A chapter's own unlockCondition is exactly the ladder's Normal rung restated, and
        // chapter.schema.json permits exactly the tier that rung names. The gate is authored twice
        // on purpose; this is what keeps the second copy from saying anything the first cannot.
        ChapterUnlockConditionsAreExactlyTheLaddersNormalRung,
    ];

    /// <summary>The <c>schema/</c> path of the effect vocabulary — the embedded-effect rule's authority.</summary>
    internal const string EffectSchemaPath = "schema/effect.schema.json";

    /// <summary>
    /// The one universal key — the member whose presence makes an object an effect.
    /// <c>effect.schema.json</c> requires it on all seventeen of its branches, which is what lets
    /// the rule find an embedded effect without knowing what its owner calls the list.
    /// </summary>
    private const string OpMemberName = "op";

    /// <summary>
    /// The other universal key, required alongside <see cref="OpMemberName"/> — required together
    /// because <c>op</c> alone collides with the condition vocabulary's own comparator, also
    /// spelled <c>op</c>. A walk keyed on <c>op</c> alone would find a perk's nested
    /// <c>condition</c> block too and report it against the top-level effect <c>oneOf</c>, which no
    /// condition node can ever satisfy. <c>id</c> is required on every effect branch and never on a
    /// condition, duration or stacking shape, so the pair together is unambiguous.
    /// </summary>
    private const string IdMemberName = "id";

    /// <summary>
    /// Every embedded effect the rule has validated so far, as <c>path#/pointer</c> — the
    /// subject-set floor a test asserts against.
    /// </summary>
    /// <remarks>
    /// The subject set is discovered structurally rather than from a list of content types, so
    /// nothing in the rule itself says how many effects it <em>ought</em> to have seen. Without
    /// this, a walk that silently matched nothing would pass exactly as loudly as one that validated
    /// everything. Populated by running <see cref="Check"/>.
    /// </remarks>
    internal static IReadOnlyList<string> ValidatedEmbeddedEffects =>
        ValidatedEffects.Keys.OrderBy(e => e, StringComparer.Ordinal).ToArray();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ValidatedEffects =
        new(StringComparer.Ordinal);

    /// <summary>Every effect embedded in an owning content file validates against <c>schema/effect.schema.json</c>.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is a cross-file rule and not a <c>$ref</c>:</b> the effect shape is a closed
    /// <c>oneOf</c> partition written in exactly one file. An owning content schema cannot reach it
    /// two ways — <see cref="JsonSchemaValidator"/> resolves same-document pointers only, so it
    /// cannot reference the partition across files, and a duplicate-vocabulary test fails any schema
    /// outside that file restating it, so it cannot restate it either. Together those leave an
    /// embedded effect validated only as "an object with an id and an op" unless something walks the
    /// embedded effects and runs the real schema over each one — which is what this rule does.
    /// </para>
    /// <para>
    /// The subject set is structural, not a list of content types: any array whose items are objects
    /// declaring an <c>op</c> is one, wherever it sits, so new content types are covered on the day
    /// they land rather than on the day somebody remembers to add them here.
    /// </para>
    /// <para>
    /// An embedded effect with no effect schema to check it against is a finding, not a skip. Every
    /// other rule here is vacuous when its documents are absent, because an absent document means
    /// there is nothing to disagree about — that reading does not transfer here, since the subject
    /// is present and it is the authority that is missing.
    /// </para>
    /// </remarks>
    private static void EmbeddedEffectsValidateAgainstTheEffectSchema(
        IReadOnlyDictionary<string, ContentValue> documents,
        IReadOnlyDictionary<string, ContentValue> schemas,
        List<ContentIssue> issues)
    {
        var embedded = new List<(string Location, ContentValue Effect)>();

        // content/ only: every effect owner lives under content/, and the walk's signature is a
        // bare `op` member, so sweeping tuning/ and loc/ too would let a future tuning key
        // innocently called "op" fail with an unrelated oneOf message.
        foreach (var (path, root) in documents
                     .Where(d => d.Key.StartsWith(ContentLayout.ContentDirectory, StringComparison.Ordinal))
                     .OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            CollectEmbeddedEffects(root, path, string.Empty, embedded);
        }

        if (embedded.Count == 0)
        {
            return;
        }

        if (!schemas.TryGetValue(EffectSchemaPath, out var effectSchema))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.MissingSchema, EffectSchemaPath,
                $"is absent, and {embedded.Count.ToString(CultureInfo.InvariantCulture)} embedded " +
                "effect(s) in the content set have nothing to be validated against. 18 §1's op-to-key " +
                "partition is written in that one file and an owning schema may neither $ref it " +
                "(same-document pointers only) nor restate it (the duplicate-vocabulary rule), so " +
                "without it every embedded effect ships unchecked."));

            return;
        }

        foreach (var (location, effect) in embedded)
        {
            issues.AddRange(JsonSchemaValidator.Validate(effect, effectSchema!, location));

            ValidatedEffects.TryAdd(location, 0);
        }
    }

    /// <summary>
    /// Finds every embedded effect: any object that declares both <c>id</c> and <c>op</c>, wherever
    /// it sits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The signature is the <c>id</c>+<c>op</c> pair, not the member name the list is spelled under
    /// and not <c>op</c> alone. Different owners spell their effect list differently (<c>effects</c>,
    /// <c>aura</c>, etc.), so a name-keyed walk would let a whole content type ship unvalidated while
    /// the floor test stayed comfortably non-empty.
    /// </para>
    /// <para>
    /// <c>op</c> alone is not enough: the condition vocabulary's own comparator is also spelled
    /// <c>op</c>, so an effect with a non-null <c>condition</c> nests a second object with an
    /// <c>op</c> member several keys down. A walk keyed on <c>op</c> alone would find that node too
    /// and report it against the top-level effect <c>oneOf</c>, which no condition node can ever
    /// satisfy. <c>id</c> is the key that tells the two apart: every effect branch requires it and no
    /// condition, duration or stacking shape ever carries one.
    /// </para>
    /// <para>
    /// No double-counting: <c>effect.schema.json</c> is <c>additionalProperties: false</c> on every
    /// branch and no branch nests a second <c>id</c>+<c>op</c> pair at its own top level, so nothing
    /// shaped like an effect sits inside one.
    /// </para>
    /// </remarks>
    private static void CollectEmbeddedEffects(
        ContentValue value, string documentPath, string pointer, List<(string, ContentValue)> found)
    {
        if (value.Kind == ContentValueKind.Array)
        {
            for (var i = 0; i < value.Items.Count; i++)
            {
                CollectEmbeddedEffects(
                    value.Items[i], documentPath, $"{pointer}/{i.ToString(CultureInfo.InvariantCulture)}", found);
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        if (value.TryGetMember(OpMemberName, out _) && value.TryGetMember(IdMemberName, out _))
        {
            found.Add(($"{documentPath}#{pointer}", value));
        }

        foreach (var name in value.MemberNames)
        {
            value.TryGetMember(name, out var member);

            CollectEmbeddedEffects(member!, documentPath, $"{pointer}/{name}", found);
        }
    }

    private static readonly IReadOnlyList<Rule> Rules =
    [
        // forge.json holds the forge-screen view of numbers luck.json owns.
        Mirrors("24 §6.1 (reforge is one set of numbers, viewed twice)",
            "tuning/forge.json#/reforge/costByRarity", "tuning/luck.json#/reforge/costByRarity"),
        Mirrors("24 §6.1", "tuning/forge.json#/reforge/currency", "tuning/luck.json#/reforge/currency"),
        Mirrors("24 §6.2", "tuning/forge.json#/retune/costByRarity", "tuning/luck.json#/retune/costByRarity"),
        Mirrors("24 §6.2", "tuning/forge.json#/retune/currency", "tuning/luck.json#/retune/currency"),
        Mirrors("24 §6.2", "tuning/forge.json#/retune/lockCostMultiplier",
            "tuning/luck.json#/retune/lockCostMultiplier"),

        // 🔒 The M4 retro ruling of 2026-08-17: capacity is FLAT and 10 §4's ladder is DEFERRED.
        // These two replace the old Derives rule (ceiling EQUALS baseCapacity + maxPurchases x
        // slotsPerPurchase), which was M4 kickoff decision 4's derivation and is now superseded.
        // The ladder must stay entirely out of reach — its reach STRICTLY ABOVE the ceiling, so not
        // one rung of it is buyable — and the ceiling must be the base. The old equality is the
        // exact state the first of the two refuses.
        StaysBelow("08 §5 / 10 §4 (the deferred ladder's reach stays above the flat ceiling)",
            "tuning/forge.json#/inventory/maxCapacity",
            d => Number(d, "tuning/forge.json#/inventory/baseCapacity") is { } capacity &&
                 Number(d, "tuning/currencies.json#/crowns/inventoryExpansionMaxPurchases") is { } purchases &&
                 Number(d, "tuning/currencies.json#/crowns/inventoryExpansionSlotsPerPurchase") is { } slots
                ? capacity + (purchases * slots)
                : null),
        Mirrors("08 §5 (capacity is flat: the ceiling IS the base)",
            "tuning/forge.json#/inventory/maxCapacity",
            "tuning/forge.json#/inventory/baseCapacity"),
        Mirrors("08 §5 / 10 §4 (one expansion step, authored twice)",
            "tuning/forge.json#/inventory/expansionStep",
            "tuning/currencies.json#/crowns/inventoryExpansionSlotsPerPurchase"),
        CountEquals("10 §4 (one ladder price per purchase step)",
            "tuning/currencies.json#/crowns/inventoryExpansionLadder",
            d => Number(d, "tuning/currencies.json#/crowns/inventoryExpansionMaxPurchases")),

        // Every currency named anywhere is a wallet currency.
        ValuesResolve("10 §1 (the wallet is the closed currency vocabulary)",
            "tuning/dungeons.json#/dungeons", "currency", WalletIds),
        ValuesResolve("10 §1", "tuning/currencies.json#/shop/dailyTab/staples", "currency", WalletIds),
        ValuesResolve("10 §1", "tuning/currencies.json#/shop/dailyTab/rotatingPool", "currency", WalletIds),
        KeysResolveIn("10 §1", "tuning/currencies.json#/shop/materialsTab", WalletIds),

        // Every grant source has a source class.
        KeysResolveIn("24 §3 (every container is classified in luck.json)",
            "tuning/drops.json#/containerContents", SourceClassIds),
        ItemsResolveIn("24 §5 (Focus applies only to classified gear sources)",
            "tuning/luck.json#/focus/appliesToClasses", SourceClassIds),
        ValuesResolve("24 §3", "tuning/currencies.json#/loginCalendar/days", "chest", SourceClassIds),

        // Every ad placement named anywhere is in the catalogue.
        ValueResolves("12 §4 (the ad-placement catalogue is closed)",
            "tuning/dungeons.json#/entries/adPlacementId", AdPlacementIds),
        KeysResolveIn("12 §4", "tuning/ads.json#/placementRewardValues", AdPlacementIds),
        KeysResolveIn("12 §5", "tuning/ads.json#/rewardScaling/baseValue", AdPlacementIds),

        // The three ad-behaviour groups partition the catalogue exactly.
        AdPlacementGroupsPartitionTheCatalogue,

        // A global cap that is not the sum of its parts is unenforceable.
        SumOfEquals("12 §1 (the in-run global cap is the sum of its placements)",
            "tuning/ads.json#/inRunPlacements", "cap", "tuning/ads.json#/globalCaps/maxInRunImpressionsPerRun"),
        SumOfEquals("12 §1 (the meta global cap is the sum of its placements)",
            "tuning/ads.json#/metaPlacements", "cap", "tuning/ads.json#/globalCaps/maxMetaImpressionsPerDay"),

        // Every material bundle is worth the same in Crowns.
        AdBundlesShareOneCrownEquivalence,
        Mirrors("12 §5 (one chapter scalar, authored twice)",
            "tuning/ads.json#/rewardScaling/chapterScalar", "tuning/currencies.json#/chapterScalars/adBundleScalar"),

        // The fairness contract and the assertion that grades it are one number.
        FairnessContractMatchesAssertionA3,

        // The behavioural profile vocabulary is shared by three files.
        ProfileNamesAgreeAcrossFiles,

        // Expected power never goes down, and every ladder resolves.
        ExpectedProgressionIsWellFormed,

        // The par table is its own default fill.
        ParPowerTableMatchesItsDefaultFill,

        // K_POWER is defined as Chapter 1 Normal par.
        Mirrors("29 §2.5.1 (K_POWER := Chapter 1 Normal par)",
            "tuning/calibration_builds.json#/referenceParBuild/targetPower",
            "tuning/par_power.json#/defaultFill/chapterPowerTargetBase"),
        MirrorsFieldsOf("29 §2.2 (the reference opponent promoted to a live actor)",
            "tuning/power_model.json#/referenceOpponent", "tuning/calibration_builds.json#/standardDummy"),

        // The dummy's output per second is derived from its own stat block.
        Derives("29 §2.5 (atk x aspd x (1 + crit x critDamage))",
            "tuning/calibration_builds.json#/measurementProtocol/dummyOutputPerSecond",
            d => Number(d, "tuning/calibration_builds.json#/standardDummy/atk") is { } atk &&
                 Number(d, "tuning/calibration_builds.json#/standardDummy/aspd") is { } aspd &&
                 Number(d, "tuning/calibration_builds.json#/standardDummy/crit") is { } crit &&
                 Number(d, "tuning/calibration_builds.json#/standardDummy/critDamage") is { } critDamage
                ? atk * aspd * (1m + (crit * critDamage))
                : null),

        // A null slot coefficient means "read percentStatsByRarity instead".
        PercentStatTableMirrorsNullSlotCoefficients,

        // Drop bands tile every chapter exactly once.
        DropBandsTileEveryChapterExactlyOnce,

        // A soft pity that sits above its hard pity protects nothing.
        SoftPitySitsBelowItsHardPity,

        // The dungeon's tiles, payouts and entry budget are one shape.
        DungeonStructureIsInternallyClosed,

        // progression.json#/unlocks is the single source of truth for gates.
        Mirrors("10 §7 (the dungeon gate is authored once)",
            "tuning/dungeons.json#/entries/unlockLegendLevel", "tuning/progression.json#/unlocks/DUNGEONS"),
        Mirrors("27 §1 (the guild gate is authored once)",
            "tuning/guilds.json#/structure/unlockLegendLevel", "tuning/progression.json#/unlocks/GUILDS"),
        Mirrors("07 §3.2 (the mount-slot gate is authored once)",
            "tuning/beasts.json#/mounts/equipSlotUnlockLegendLevel", "tuning/progression.json#/unlocks/MOUNT_SLOT"),
        Mirrors("10 §7 (the Mythic gate is authored once)",
            "tuning/progression.json#/chapterGating/MYTHIC/requiresLegendLevel",
            "tuning/progression.json#/unlocks/MYTHIC_TIER"),
        ValuesResolve("27 §3 (a quest gate names an unlock)",
            "tuning/guilds.json#/quests/pool", "gatedOn", d => Keys(d, "tuning/progression.json#/unlocks")),

        // The energy grants agree with the systems that own them.
        Mirrors("12 §4 (the ad energy grant is authored once)",
            "tuning/progression.json#/energy/sources/AD_ENERGY/amount",
            "tuning/ads.json#/placementRewardValues/AD_ENERGY/energy"),
        Mirrors("19 Part B (the daily-quest energy grant is authored once)",
            "tuning/progression.json#/energy/sources/DAILY_QUEST/amount",
            "tuning/currencies.json#/dailyQuests/rewardPerQuest/energy"),
        Mirrors("25 §4 (the dungeon energy cost is authored once)",
            "tuning/progression.json#/energy/dungeonCost", "tuning/dungeons.json#/structure/energyCost"),

        // The Soul-Shard container prices are authored three times.
        Mirrors("10 §2 / 07 §2.3 (the pet-egg price)",
            "tuning/currencies.json#/soulShards/sinks/PET_EGG",
            "tuning/beasts.json#/containerPrices/petEggSoulShards"),
        Mirrors("10 §2 / 07 §3.2 (the mount-crate price)",
            "tuning/currencies.json#/soulShards/sinks/MOUNT_CRATE",
            "tuning/beasts.json#/containerPrices/mountCrateSoulShards"),

        // Ladders the docs call ordered and no keyword can express.
        StrictlyAscending("08 §4.2 (enhance stone costs)", "tuning/forge.json#/enhance/stoneCostPerLevel"),
        StrictlyAscending("10 §4 (the inventory ladder)",
            "tuning/currencies.json#/crowns/inventoryExpansionLadder"),
        StrictlyAscending("03 §7 (the chapter price scalar)", "tuning/currencies.json#/shopTile/chapterPriceScalar"),
        StrictlyAscending("29 §6 (the checkpoint days)", "tuning/expected_progression.json#/checkpointDays"),

        // The enhance ladder is self-consistent.
        EnhanceLadderIsSelfConsistent,

        // One chapter growth rate, authored twice.
        Mirrors("02 §5.1a (XP and gold grow at the same rate)",
            "tuning/progression.json#/runXp/baseXpGrowth", "tuning/currencies.json#/chapterScalars/goldGrowth"),

        // The event calendar's two shares are a partition.
        SharesSumTo("26 §3 (every major event is one archetype or the other)", 1m,
            "tuning/events.json#/calendar/chapterEventShare",
            "tuning/events.json#/calendar/collectionEventShare"),

        // One daily reset, or the day boundary fragments.
        Mirrors("26 §3 / 25 §4 (one daily reset time)",
            "tuning/events.json#/calendar/startEndUtc", "tuning/dungeons.json#/entries/refreshUtc"),
        Mirrors("27 §3 (one daily reset time)",
            "tuning/guilds.json#/quests/drawUtc", "tuning/dungeons.json#/entries/refreshUtc"),

        // The daily shop draw has to be satisfiable.
        ShopDailyDrawIsSatisfiable,

        // The guild quest pool has to be drawable.
        GuildQuestPoolIsDrawable,

        // The two mitigation dials are one pair of numbers, written twice: combat_caps.json is
        // what the simulator loads, power_model.json is what the sweep reads, and if the pair ever
        // diverges the power model predicts a mitigation the fight does not produce. Stated over
        // the two blocks rather than as scalar mirrors so a dial added to one side and not the
        // other is caught too.
        Mirrors("05 §4 / 29 §2.3 (one pair of mitigation dials, authored twice)",
            "content/combat_caps.json#/mitigation", "tuning/power_model.json#/mitigation"),

        // A chapter's enemy pool is a weight table over the eight archetypes; weights per row sum
        // to 100. No JSON Schema keyword can add up an object's values, so the total is stated
        // here. Two authored zero-weight rows are deliberately NOT checked by this total — a row
        // that moved points between two other archetypes would still sum to 100 and silently erase
        // that zero. Those are pinned by name in EnemiesDataTests instead.
        ChapterPoolWeightsSumToOneHundred,

        // Each chapter's elitePool is exactly its two biome elites, and elites come only from it —
        // an identity in none is an elite nothing can ever draw, and one in two is a biome leak.
        EliteIdentitiesAreEachInExactlyOneChapterPool,

        // Every chapter that has an enemy pool needs a base level, or the chapter derives level-0
        // enemies and the mitigation denominator reads an attacker that never grows. On the
        // shipped shape this is belt-and-braces rather than coverage — the schema already pins
        // both arrays to the same fixed chapter range — but it bites the day either array's bounds
        // are relaxed.
        EveryChapterWithAPoolHasABaseEnemyLevel,

        // The pool weights are authored twice: enemies.json#/chapterPools is the producer-side
        // transcription, and each chapter file's own enemyPool is what the board actually draws
        // from. Vacuous for chapters whose file has not landed yet — the only moment a pair can
        // start to disagree.
        ChapterFilesAgreeWithTheProducerSideEnemyPool,
    ];

    // ------------------------------------------------------------------- rules with a body

    private static void AdPlacementGroupsPartitionTheCatalogue(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var catalogue = AdPlacementIds(documents);
        if (catalogue.Count == 0)
        {
            return;
        }

        var grouped = new List<string>();
        foreach (var group in (string[])["HIGH", "MEDIUM", "LOW"])
        {
            var list = Find(documents, $"tuning/sim_profiles.json#/adPlacementGroups/{group}");
            if (list is null)
            {
                return;
            }

            grouped.AddRange(list.Items.Where(i => i.Kind == ContentValueKind.Text).Select(i => i.AsText()));
        }

        foreach (var duplicate in grouped.GroupBy(g => g, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.DuplicateId, "tuning/sim_profiles.json#/adPlacementGroups",
                $"21 §5.4: '{duplicate.Key}' is in {duplicate.Count()} behaviour groups; every placement " +
                "belongs to exactly one."));
        }

        foreach (var orphan in grouped.Where(g => !catalogue.Contains(g)).Distinct(StringComparer.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/sim_profiles.json#/adPlacementGroups",
                $"21 §5.4: '{orphan}' is not an ad placement in 12 §4's catalogue."));
        }

        foreach (var missing in catalogue.Except(grouped, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/sim_profiles.json#/adPlacementGroups",
                $"21 §5.4: the placement '{missing}' is in no behaviour group, so no simulated profile " +
                "ever watches it."));
        }
    }

    private static void AdBundlesShareOneCrownEquivalence(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var invariant = Number(documents, "tuning/ads.json#/rewardScaling/crownEquivalenceInvariant");
        if (invariant is null)
        {
            return;
        }

        var bundles = new (string Placement, string Currency)[]
        {
            ("AD_ENHANCE_STONES", "ENHANCE_STONES"),
            ("AD_MERGE_DUST", "MERGE_DUST"),
            ("AD_FEED_BUNDLE", "BEAST_FEED"),
        };

        foreach (var (placement, currency) in bundles)
        {
            var amount = Number(documents, $"tuning/ads.json#/rewardScaling/baseValue/{placement}");
            var rate = Number(documents, $"tuning/currencies.json#/shop/materialsTab/{currency}/crownsPerUnit");

            if (amount is null || rate is null || amount * rate == invariant)
            {
                continue;
            }

            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, $"tuning/ads.json#/rewardScaling/baseValue/{placement}",
                $"12 §5: {amount} x {rate} Crowns/unit = {amount * rate}, but every ad bundle is worth " +
                $"{invariant} Crowns. If the shop rate moved, this must move with it."));
        }

        var crowns = Number(documents, "tuning/ads.json#/rewardScaling/baseValue/AD_CROWNS");
        if (crowns is not null && crowns != invariant)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/ads.json#/rewardScaling/baseValue/AD_CROWNS",
                $"12 §5: the Crown bundle pays {crowns} but the bundle equivalence is {invariant}."));
        }
    }

    private static void FairnessContractMatchesAssertionA3(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var gap = Number(documents, "tuning/ads.json#/fairnessContract/maxFreeVsPlusPowerGap");
        var minRatio = Number(documents, "tuning/sim_thresholds.json#/coreEconomy/A3/minRatio");

        if (gap is not null && minRatio is not null && 1m - gap != minRatio)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/sim_thresholds.json#/coreEconomy/A3/minRatio",
                $"12 §3: A3 grades the fairness contract, so its minimum ratio must be 1 - " +
                $"maxFreeVsPlusPowerGap ({1m - gap}), not {minRatio}."));
        }

        var measuredAt = Number(documents, "tuning/ads.json#/fairnessContract/measuredAtDay");
        var assertedAt = Number(documents, "tuning/sim_thresholds.json#/coreEconomy/A3/day");

        if (measuredAt is not null && assertedAt is not null && measuredAt != assertedAt)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/sim_thresholds.json#/coreEconomy/A3/day",
                $"12 §3: the contract is measured on day {measuredAt} but A3 grades day {assertedAt}."));
        }
    }

    private static void ProfileNamesAgreeAcrossFiles(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var declared = FieldValues(documents, "tuning/sim_profiles.json#/profiles", "name");
        if (declared.Count == 0)
        {
            return;
        }

        foreach (var used in FieldValues(documents, "tuning/expected_progression.json#/profiles", "profile")
                     .Where(p => !declared.Contains(p)))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/expected_progression.json#/profiles",
                $"21 §5.4: '{used}' is not one of the {declared.Count} behavioural profiles."));
        }

        foreach (var assertion in (string[])["coreEconomy/A1", "coreEconomy/A7", "coreEconomy/A8", "behaviourAndFairness/A15"])
        {
            var reference = $"tuning/sim_thresholds.json#/{assertion}/profile";
            var value = Find(documents, reference);

            if (value is { Kind: ContentValueKind.Text } && !declared.Contains(value.AsText()))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, reference,
                    $"21 §11: grades the profile '{value.AsText()}', which sim_profiles.json does not define."));
            }
        }

        var count = Number(documents, "tuning/sim_thresholds.json#/inherited/E6/profileCount");
        if (count is not null && count != declared.Count)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/sim_thresholds.json#/inherited/E6/profileCount",
                $"21 §11: asserts {count} profiles but sim_profiles.json defines {declared.Count}."));
        }
    }

    private static void ExpectedProgressionIsWellFormed(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var checkpoints = Find(documents, "tuning/expected_progression.json#/checkpointDays");
        var ladders = Find(documents, "tuning/expected_progression.json#/toleranceLadders");
        var profiles = Find(documents, "tuning/expected_progression.json#/profiles");

        if (checkpoints is null || ladders is null || profiles is null)
        {
            return;
        }

        var canonical = 0;

        for (var i = 0; i < profiles.Items.Count; i++)
        {
            var profile = profiles.Items[i];
            var location = $"tuning/expected_progression.json#/profiles/{i}";

            if (profile.TryGetMember("toleranceLadder", out var ladder) &&
                ladder!.Kind == ContentValueKind.Text &&
                !ladders.TryGetMember(ladder.AsText(), out _))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, $"{location}/toleranceLadder",
                    $"29 §6: '{ladder.AsText()}' is not a declared tolerance ladder."));
            }

            if (profile.TryGetMember("canonical", out var flag) &&
                flag!.Kind == ContentValueKind.Boolean && flag.AsBoolean())
            {
                canonical++;
            }

            if (!profile.TryGetMember("expectedPower", out var power) || power!.Kind != ContentValueKind.Array)
            {
                continue;
            }

            if (power.Items.Count != checkpoints.Items.Count)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, $"{location}/expectedPower",
                    $"29 §6: has {power.Items.Count} entries against {checkpoints.Items.Count} checkpoint days."));
            }

            for (var j = 1; j < power.Items.Count; j++)
            {
                if (power.Items[j].Kind == ContentValueKind.Number &&
                    power.Items[j - 1].Kind == ContentValueKind.Number &&
                    power.Items[j].AsNumber() <= power.Items[j - 1].AsNumber())
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OutOfRange, $"{location}/expectedPower/{j}",
                        "29 §6: a profile's expected power never goes down."));
                }
            }
        }

        if (canonical != 1)
        {
            issues.Add(new ContentIssue(
                canonical > 1 ? ContentIssueCode.DuplicateId : ContentIssueCode.OrphanedReference,
                "tuning/expected_progression.json#/profiles",
                $"29 §6: {canonical} profiles are marked canonical; exactly one is the product owner's intent."));
        }
    }

    private static void ParPowerTableMatchesItsDefaultFill(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var table = Find(documents, "tuning/par_power.json#/parPower");
        var basePower = Number(documents, "tuning/par_power.json#/defaultFill/chapterPowerTargetBase");
        var growth = Number(documents, "tuning/par_power.json#/defaultFill/chapterPowerTargetGrowth");

        if (table is null || basePower is null || growth is null)
        {
            return;
        }

        for (var i = 0; i < table.Items.Count; i++)
        {
            var row = table.Items[i];
            if (!row.TryGetMember("chapter", out var chapter) || chapter!.Kind != ContentValueKind.Number ||
                !row.TryGetMember("NORMAL", out var normal) || normal!.Kind != ContentValueKind.Number)
            {
                continue;
            }

            var expected = basePower.Value;
            for (var step = 1; step < chapter.AsInt32(); step++)
            {
                expected *= growth.Value;
            }

            if (normal.AsNumber() != expected)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, $"tuning/par_power.json#/parPower/{i}/NORMAL",
                    $"29 §4: chapter {chapter.AsInt32()} par is {normal.AsNumber()}, but the default fill " +
                    $"({basePower} x {growth}^(c-1)) gives {expected}."));
            }

            foreach (var tier in (string[])["HEROIC", "MYTHIC"])
            {
                var multiplier = Number(documents, $"tuning/par_power.json#/defaultFill/tierMultiplier/{tier}");
                if (multiplier is null || !row.TryGetMember(tier, out var value) ||
                    value!.Kind != ContentValueKind.Number || value.AsNumber() == normal.AsNumber() * multiplier)
                {
                    continue;
                }

                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, $"tuning/par_power.json#/parPower/{i}/{tier}",
                    $"29 §4: is {value.AsNumber()} but NORMAL x {multiplier} gives {normal.AsNumber() * multiplier}."));
            }
        }
    }

    private static void PercentStatTableMirrorsNullSlotCoefficients(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var slots = Find(documents, "tuning/drops.json#/slotCoefficients");
        var table = Find(documents, "tuning/drops.json#/percentStatsByRarity");

        if (slots is null || table is null)
        {
            return;
        }

        // A null coefficient means "read percentStatsByRarity instead" — the null is the
        // mechanism, so this rule must read it as data, never coerce it, and never skip it.
        var unauthorised = new HashSet<string>(StringComparer.Ordinal);
        var authored = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slot in slots.Items)
        {
            foreach (var (statMember, coefficientMember) in
                     (( string, string )[])[("primaryStat", "primaryCoef"), ("secondaryStat", "secondaryCoef")])
            {
                if (!slot.TryGetMember(statMember, out var stat) || stat!.Kind != ContentValueKind.Text ||
                    !slot.TryGetMember(coefficientMember, out var coefficient))
                {
                    continue;
                }

                _ = coefficient!.IsUnauthorised ? unauthorised.Add(stat.AsText()) : authored.Add(stat.AsText());
            }
        }

        var rows = table.MemberNames.Where(n => !n.StartsWith('_')).ToHashSet(StringComparer.Ordinal);

        foreach (var stat in unauthorised.Except(rows).OrderBy(s => s, StringComparer.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.UnknownId, "tuning/drops.json#/percentStatsByRarity",
                $"08 §3.0a: '{stat}' has a null slot coefficient, which means 'read this table instead' — " +
                "and the table has no row for it."));
        }

        foreach (var stat in rows.Except(unauthorised).OrderBy(s => s, StringComparer.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.UnknownId, $"tuning/drops.json#/percentStatsByRarity/{stat}",
                authored.Contains(stat)
                    ? $"08 §3.0a: '{stat}' has an authored slot coefficient, so it must not also have a " +
                      "percent-stat row — the two are alternatives."
                    : $"08 §3.0a: '{stat}' has a percent-stat row that no gear slot claims."));
        }
    }

    private static void DropBandsTileEveryChapterExactlyOnce(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var bands = Find(documents, "tuning/drops.json#/dropShareByChapterBand");
        var chapters = Find(documents, "tuning/par_power.json#/parPower");

        if (bands is null || chapters is null)
        {
            return;
        }

        var covered = new Dictionary<int, int>();

        foreach (var band in bands.Items)
        {
            if (!band.TryGetMember("chapterFrom", out var from) || from!.Kind != ContentValueKind.Number ||
                !band.TryGetMember("chapterTo", out var to) || to!.Kind != ContentValueKind.Number)
            {
                continue;
            }

            for (var chapter = from.AsInt32(); chapter <= to.AsInt32(); chapter++)
            {
                covered[chapter] = covered.GetValueOrDefault(chapter) + 1;
            }
        }

        foreach (var chapter in chapters.Items
                     .Where(c => c.TryGetMember("chapter", out var value) && value!.Kind == ContentValueKind.Number)
                     .Select(c => { c.TryGetMember("chapter", out var value); return value!.AsInt32(); }))
        {
            var count = covered.GetValueOrDefault(chapter);
            covered.Remove(chapter);

            if (count == 1)
            {
                continue;
            }

            issues.Add(new ContentIssue(
                count == 0 ? ContentIssueCode.OrphanedReference : ContentIssueCode.DuplicateId,
                "tuning/drops.json#/dropShareByChapterBand",
                count == 0
                    ? $"08 §6: chapter {chapter} is covered by no drop band, so nothing drops there."
                    : $"08 §6: chapter {chapter} is covered by {count} drop bands."));
        }

        foreach (var stray in covered.Keys.OrderBy(c => c))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/drops.json#/dropShareByChapterBand",
                $"08 §6: a band covers chapter {stray}, which the par table does not have."));
        }
    }

    private static void SoftPitySitsBelowItsHardPity(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        foreach (var block in (string[])["chestStandard", "chestPremium", "chestApex", "eggPet", "crateMount"])
        {
            var softPity = Find(documents, $"tuning/luck.json#/{block}/softPity");
            var hardPity = Find(documents, $"tuning/luck.json#/{block}/hardPity");

            // A null softPity means none is authorised here. Skipping is correct; reading it as
            // "threshold 0" would invent a guarantee.
            if (softPity is null || softPity.IsUnauthorised || hardPity is null ||
                !softPity.TryGetMember("target", out var target) ||
                !softPity.TryGetMember("missThreshold", out var threshold) ||
                threshold!.Kind != ContentValueKind.Number)
            {
                continue;
            }

            var protecting = hardPity.Items
                .Where(step => step.TryGetMember("guaranteeRarityAtLeast", out var rarity) &&
                               rarity!.Equals(target!) &&
                               step.TryGetMember("everyNth", out var n) && n!.Kind == ContentValueKind.Number)
                .Select(step => { step.TryGetMember("everyNth", out var n); return n!.AsNumber(); })
                .ToArray();

            if (protecting.Length == 0 || threshold.AsNumber() < protecting.Max())
            {
                continue;
            }

            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, $"tuning/luck.json#/{block}/softPity/missThreshold",
                $"24 §4: soft pity starts at {threshold.AsNumber()} misses but the hard pity it protects " +
                $"fires at {protecting.Max()}. A soft pity above its hard pity protects nobody."));
        }
    }

    private static void DungeonStructureIsInternallyClosed(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var composition = Find(documents, "tuning/dungeons.json#/structure/tileComposition");
        var nodes = Number(documents, "tuning/dungeons.json#/structure/nodesBeforeGuardian");

        if (composition is not null && nodes is not null)
        {
            var total = composition.MemberNames
                .Select(name => { composition.TryGetMember(name, out var v); return v!; })
                .Where(v => v.Kind == ContentValueKind.Number)
                .Sum(v => v.AsNumber());

            if (total != nodes)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, "tuning/dungeons.json#/structure/tileComposition",
                    $"25 §3: the tiles sum to {total} but the run has {nodes} nodes before the Guardian."));
            }
        }

        var perEnemy = Number(documents, "tuning/dungeons.json#/payoutDistribution/perEnemyKill");
        var kills = Number(documents, "tuning/dungeons.json#/payoutDistribution/enemyKills");
        var perCache = Number(documents, "tuning/dungeons.json#/payoutDistribution/perCache");
        var caches = Number(documents, "tuning/dungeons.json#/payoutDistribution/caches");
        var guardian = Number(documents, "tuning/dungeons.json#/payoutDistribution/guardianKill");

        if (perEnemy is not null && kills is not null && perCache is not null &&
            caches is not null && guardian is not null)
        {
            var share = (perEnemy.Value * kills.Value) + (perCache.Value * caches.Value) + guardian.Value;
            if (share != 1m)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, "tuning/dungeons.json#/payoutDistribution",
                    $"25 §4: the payout shares total {share}; a dungeon pays exactly its tier yield."));
            }
        }

        var perDungeon = Number(documents, "tuning/dungeons.json#/entries/perDungeonPerDay");
        var totalPerDay = Number(documents, "tuning/dungeons.json#/entries/totalPerDay");
        var dungeons = Find(documents, "tuning/dungeons.json#/dungeons");

        if (perDungeon is not null && totalPerDay is not null && dungeons is not null &&
            perDungeon * dungeons.Items.Count != totalPerDay)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, "tuning/dungeons.json#/entries/totalPerDay",
                $"25 §4: {perDungeon} entries x {dungeons.Items.Count} dungeons is " +
                $"{perDungeon * dungeons.Items.Count}, not {totalPerDay}."));
        }

        var neverPays = Find(documents, "tuning/dungeons.json#/neverPays");
        if (dungeons is null || neverPays is null)
        {
            return;
        }

        var forbidden = neverPays.Items.Where(i => i.Kind == ContentValueKind.Text)
                                 .Select(i => i.AsText()).ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < dungeons.Items.Count; i++)
        {
            if (dungeons.Items[i].TryGetMember("currency", out var currency) &&
                currency!.Kind == ContentValueKind.Text && forbidden.Contains(currency.AsText()))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, $"tuning/dungeons.json#/dungeons/{i}/currency",
                    $"25 §4.5: pays {currency.AsText()}, which this file also lists under neverPays."));
            }
        }
    }

    private static void EnhanceLadderIsSelfConsistent(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var minLevel = Number(documents, "tuning/forge.json#/enhance/minLevel");
        var maxLevel = Number(documents, "tuning/forge.json#/enhance/maxLevel");
        var perLevel = Number(documents, "tuning/forge.json#/enhance/statBonusPerLevel");
        var total = Number(documents, "tuning/forge.json#/enhance/totalMultiplierAtMax");
        var costs = Find(documents, "tuning/forge.json#/enhance/stoneCostPerLevel");

        if (perLevel is not null && maxLevel is not null && total is not null &&
            1m + (perLevel.Value * maxLevel.Value) != total)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/totalMultiplierAtMax",
                $"08 §4.2: is {total} but 1 + {perLevel} x {maxLevel} gives {1m + (perLevel * maxLevel)}."));
        }

        if (costs is not null && minLevel is not null && maxLevel is not null &&
            costs.Items.Count != maxLevel - minLevel)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/stoneCostPerLevel",
                $"08 §4.2: has {costs.Items.Count} costs for {maxLevel - minLevel} enhancement levels."));
        }

        var bands = Find(documents, "tuning/forge.json#/enhance/successRateBands");
        if (bands is null || minLevel is null || maxLevel is null)
        {
            return;
        }

        var next = minLevel.Value + 1m;
        for (var i = 0; i < bands.Items.Count; i++)
        {
            if (!bands.Items[i].TryGetMember("fromLevel", out var from) || from!.Kind != ContentValueKind.Number ||
                !bands.Items[i].TryGetMember("toLevel", out var to) || to!.Kind != ContentValueKind.Number)
            {
                continue;
            }

            if (from.AsNumber() != next)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, $"tuning/forge.json#/enhance/successRateBands/{i}/fromLevel",
                    $"08 §4.2: starts at {from.AsNumber()}, leaving level {next} with no success rate."));
            }

            next = to.AsNumber() + 1m;
        }

        if (next - 1m != maxLevel)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/successRateBands",
                $"08 §4.2: the bands end at level {next - 1m}, not at maxLevel {maxLevel}."));
        }

        SuccessLadderExpandsItsBands(documents, bands, minLevel.Value, issues);
    }

    /// <summary>
    /// The per-level success ladder against the band endpoints it was expanded from: each band's
    /// levels carry its start rate, its end rate, and an even step between them.
    /// </summary>
    /// <remarks>
    /// 🔒 The reason this rule exists rather than the ladder simply being authored. The two are
    /// separately authored statements of one thing — five endpoints and fifteen values — and the
    /// second was expanded from the first by reading the arrow in <c>08</c> §4.2's bands as the
    /// document's own ramp notation. Nothing else in the build would notice the two drifting, and a
    /// ladder that had drifted would read as a deliberate re-tune rather than as a transcription
    /// slip.
    /// </remarks>
    private static void SuccessLadderExpandsItsBands(
        IReadOnlyDictionary<string, ContentValue> documents,
        ContentValue bands,
        decimal minLevel,
        List<ContentIssue> issues)
    {
        const string reference = "tuning/forge.json#/enhance/perLevelSuccessRate";

        var ladder = Find(documents, reference);

        if (ladder is null || ladder.Kind != ContentValueKind.Array)
        {
            return;
        }

        foreach (var band in bands.Items)
        {
            if (!band.TryGetMember("fromLevel", out var from) || from!.Kind != ContentValueKind.Number ||
                !band.TryGetMember("toLevel", out var to) || to!.Kind != ContentValueKind.Number ||
                !band.TryGetMember("startRate", out var start) || start!.Kind != ContentValueKind.Number ||
                !band.TryGetMember("endRate", out var end) || end!.Kind != ContentValueKind.Number)
            {
                continue;
            }

            var first = (int)from.AsNumber();
            var last = (int)to.AsNumber();
            var steps = last - first;

            for (var level = first; level <= last; level++)
            {
                var index = level - (int)minLevel - 1;

                if (index < 0 || index >= ladder.Items.Count ||
                    ladder.Items[index].Kind != ContentValueKind.Number)
                {
                    continue;
                }

                var expected = steps == 0
                    ? start.AsNumber()
                    : start.AsNumber() +
                      ((end.AsNumber() - start.AsNumber()) * (level - first) / steps);

                if (ladder.Items[index].AsNumber() != expected)
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OutOfRange,
                        reference + "/" + index.ToString(CultureInfo.InvariantCulture),
                        $"08 §4.2: level {level} carries {ladder.Items[index].AsNumber()}, but its " +
                        $"band ramps {start.AsNumber()} to {end.AsNumber()} over {steps + 1} levels, " +
                        $"which puts {expected} here. The ladder is the bands' own endpoints spread " +
                        "evenly; the two must not drift."));
                }
            }
        }
    }

    private static void ShopDailyDrawIsSatisfiable(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var pool = Find(documents, "tuning/currencies.json#/shop/dailyTab/rotatingPool");
        var perDay = Number(documents, "tuning/currencies.json#/shop/dailyTab/rotatingOffersPerDay");
        var alwaysIncluded = Find(documents, "tuning/currencies.json#/shop/dailyTab/alwaysIncluded");

        if (pool is null || perDay is null)
        {
            return;
        }

        if (perDay > pool.Items.Count)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, "tuning/currencies.json#/shop/dailyTab/rotatingOffersPerDay",
                $"10 §4.2: draws {perDay} offers without replacement from a pool of {pool.Items.Count}."));
        }

        var ids = FieldValues(documents, "tuning/currencies.json#/shop/dailyTab/rotatingPool", "id");

        foreach (var required in (alwaysIncluded?.Items ?? [])
                     .Where(i => i.Kind == ContentValueKind.Text).Select(i => i.AsText())
                     .Where(i => !ids.Contains(i)))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/currencies.json#/shop/dailyTab/alwaysIncluded",
                $"10 §4.2: '{required}' is always drawn but is not in the rotating pool."));
        }

        var maxSoulShard = Number(documents, "tuning/currencies.json#/shop/dailyTab/maxSoulShardPricedOffersPerDay");
        if (maxSoulShard is not null && maxSoulShard > perDay)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange,
                "tuning/currencies.json#/shop/dailyTab/maxSoulShardPricedOffersPerDay",
                $"10 §4.2: caps Soul-Shard offers at {maxSoulShard} of a {perDay}-offer block."));
        }
    }

    private static void GuildQuestPoolIsDrawable(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var pool = FieldValues(documents, "tuning/guilds.json#/quests/pool", "id");
        if (pool.Count == 0)
        {
            return;
        }

        var runsOnly = Find(documents, "tuning/guilds.json#/quests/runsOnlySatisfiable");
        var perDay = Number(documents, "tuning/guilds.json#/quests/perDay");

        foreach (var quest in (runsOnly?.Items ?? []).Where(i => i.Kind == ContentValueKind.Text)
                     .Select(i => i.AsText()).Where(q => !pool.Contains(q)))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, "tuning/guilds.json#/quests/runsOnlySatisfiable",
                $"27 §3.1: '{quest}' is not in the quest pool."));
        }

        if (perDay is not null && perDay > pool.Count)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OutOfRange, "tuning/guilds.json#/quests/perDay",
                $"27 §3.1: draws {perDay} quests without replacement from a pool of {pool.Count}."));
        }

        Mirrors("27 §3.1 (targets are authored for a full guild)",
            "tuning/guilds.json#/quests/targetsAuthoredForActiveMembers",
            "tuning/guilds.json#/structure/baseMemberCap")(documents, issues);
    }

    private const string EnemiesDocument = "content/enemies/enemies.json";

    /// <summary>Weights per chapter-pool row sum to 100.</summary>
    private static void ChapterPoolWeightsSumToOneHundred(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var pools = Find(documents, EnemiesDocument + "#/chapterPools");
        if (pools is null || pools.Kind != ContentValueKind.Array)
        {
            return;
        }

        for (var i = 0; i < pools.Items.Count; i++)
        {
            var reference = $"{EnemiesDocument}#/chapterPools/{i.ToString(CultureInfo.InvariantCulture)}/weights";
            var weights = Find(documents, reference);

            if (weights is not { Kind: ContentValueKind.Object })
            {
                continue;
            }

            var total = 0m;
            foreach (var name in weights.MemberNames.Where(n => !n.StartsWith('_')))
            {
                weights.TryGetMember(name, out var weight);

                // An unauthorised weight is skipped, not read as zero: a null weight is a hole and
                // the row it sits in cannot be said to sum to anything.
                if (weight!.Kind == ContentValueKind.Number)
                {
                    total += weight.AsNumber();
                }
            }

            if (total != 100m)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, reference,
                    $"05 §6.4: the weights total {total}, not 100. A pool whose row does not sum to " +
                    "100 still draws — it just draws at shares nobody authored."));
            }
        }
    }

    /// <summary>Each chapter's <c>elitePool</c> is exactly its two biome elites.</summary>
    private static void EliteIdentitiesAreEachInExactlyOneChapterPool(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var identities = FieldValues(documents, EnemiesDocument + "#/elites/identities", "id");
        var pools = Find(documents, EnemiesDocument + "#/chapterPools");

        if (identities.Count == 0 || pools is null || pools.Kind != ContentValueKind.Array)
        {
            return;
        }

        var pooled = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < pools.Items.Count; i++)
        {
            var reference = $"{EnemiesDocument}#/chapterPools/{i.ToString(CultureInfo.InvariantCulture)}/elitePool";
            var pool = Find(documents, reference);

            foreach (var id in (pool?.Items ?? []).Where(e => e.Kind == ContentValueKind.Text).Select(e => e.AsText()))
            {
                pooled[id] = pooled.GetValueOrDefault(id) + 1;
            }
        }

        foreach (var id in identities.Except(pooled.Keys, StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, EnemiesDocument + "#/elites/identities",
                $"05 §6.2: '{id}' is in no chapter's elitePool, and elites come only from an " +
                "elitePool — so it is an elite the game can never present."));
        }

        foreach (var (id, count) in pooled.Where(p => p.Value > 1).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.DuplicateId, EnemiesDocument + "#/chapterPools",
                $"05 §6.2: '{id}' is in {count.ToString(CultureInfo.InvariantCulture)} chapters' " +
                "elitePools. Each chapter's pool is exactly its own two biome elites."));
        }
    }

    /// <summary>Every chapter that fields enemies has a <c>BaseEnemyLevel</c>.</summary>
    private static void EveryChapterWithAPoolHasABaseEnemyLevel(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var levels = Find(documents, EnemiesDocument + "#/enemyLevel/baseByChapter");
        var pools = Find(documents, EnemiesDocument + "#/chapterPools");

        if (levels is null || levels.Kind != ContentValueKind.Array ||
            pools is null || pools.Kind != ContentValueKind.Array)
        {
            return;
        }

        var levelled = levels.Items
            .Where(r => r.TryGetMember("chapter", out var c) && c!.Kind == ContentValueKind.Number)
            .Select(r => { r.TryGetMember("chapter", out var c); return c!.AsInt32(); })
            .ToHashSet();

        foreach (var chapter in pools.Items
                     .Where(r => r.TryGetMember("chapter", out var c) && c!.Kind == ContentValueKind.Number)
                     .Select(r => { r.TryGetMember("chapter", out var c); return c!.AsInt32(); })
                     .Where(c => !levelled.Contains(c))
                     .OrderBy(c => c))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, EnemiesDocument + "#/enemyLevel/baseByChapter",
                $"05 §6.0: chapter {chapter.ToString(CultureInfo.InvariantCulture)} fields enemies but " +
                "has no BaseEnemyLevel, so every enemy in it would be level 0 — which 05 §4's " +
                "mitigation denominator reads as an attacker that never grows."));
        }
    }

    /// <summary>A chapter file's <c>enemyPool</c> is the same weight table <c>enemies.json</c> transcribes for it.</summary>
    /// <remarks>
    /// Keyed on the chapter file's own <c>id</c> rather than on its path, since the file names are
    /// author-chosen. A chapter file whose <c>id</c> matches no <c>chapterPools</c> row is left
    /// alone here — the id space and range rules already own that.
    /// </remarks>
    private static void ChapterFilesAgreeWithTheProducerSideEnemyPool(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var pools = Find(documents, EnemiesDocument + "#/chapterPools");
        if (pools is null || pools.Kind != ContentValueKind.Array)
        {
            return;
        }

        var byChapter = new Dictionary<int, string>();
        for (var i = 0; i < pools.Items.Count; i++)
        {
            if (pools.Items[i].TryGetMember("chapter", out var chapter) &&
                chapter!.Kind == ContentValueKind.Number)
            {
                byChapter[chapter.AsInt32()] =
                    $"{EnemiesDocument}#/chapterPools/{i.ToString(CultureInfo.InvariantCulture)}/weights";
            }
        }

        foreach (var (path, root) in documents
                     .Where(d => d.Key.StartsWith(ContentLayout.ContentDirectory + "chapters/", StringComparison.Ordinal))
                     .OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (!root.TryGetMember("id", out var id) || id!.Kind != ContentValueKind.Number ||
                !byChapter.TryGetValue(id.AsInt32(), out var producer))
            {
                continue;
            }

            Mirrors(
                "05 §6.4 (one enemy-pool weight table, authored on the chapter and transcribed in enemies.json)",
                $"{path}#/enemyPool", producer)(documents, issues);
        }
    }

    // ------------------------------------------------------- the chapter/tier unlock gate

    /// <summary>Where the chapter documents sit.</summary>
    private const string ChaptersDirectory = ContentLayout.ContentDirectory + "chapters/";

    /// <summary>The <c>schema/</c> path of the chapter shape — the cross-check arm's authority.</summary>
    private const string ChapterSchemaPath = ContentLayout.SchemaDirectory + "chapter.schema.json";

    /// <summary>The Normal rung of the authored ladder, and the member holding the clear it demands.</summary>
    private const string NormalRungClearReference = "tuning/progression.json#/chapterGating/NORMAL/requiresClear";

    /// <summary>
    /// The one clear token a chapter's own <c>unlockCondition</c> can restate, and the tier that
    /// token names. Written as a pair so the translation is stated rather than assumed: the rule
    /// reads the token first and only then knows which tier the schema may permit.
    /// </summary>
    private const string PreviousChapterNormalToken = "PREVIOUS_CHAPTER_NORMAL";

    private const string PreviousChapterNormalTier = "NORMAL";

    /// <summary>The lowest chapter number the campaign has, so nothing is authored before it.</summary>
    private const int FirstChapterId = 1;

    private const string UnlockConditionMember = "unlockCondition";
    private const string ClearChapterMember = "clearChapter";
    private const string TierMember = "tier";

    /// <summary>A chapter's <c>unlockCondition</c> is the ladder's Normal rung restated, and nothing else.</summary>
    /// <remarks>
    /// <para>
    /// The gate is authored twice on purpose: generically in
    /// <c>tuning/progression.json#/chapterGating</c>, which is the single runtime authority, and
    /// per chapter in the chapter's own document, so a chapter file reads as a whole. Three arms
    /// keep the second copy from ever saying something the first cannot.
    /// </para>
    /// <para>
    /// <b>The token.</b> The Normal rung must still name the one clear this rule knows how to
    /// translate. Any other token is a <em>finding</em> rather than a reason to fall silent — a
    /// rule that descoped itself here would pass over every chapter on the day the ladder moved,
    /// and the data set would validate more cleanly than before.
    /// </para>
    /// <para>
    /// <b>The chapters.</b> Chapter 1 authors <c>null</c>, because the token names no chapter before
    /// the first one; chapter <c>c</c> authors exactly the clear of chapter <c>c-1</c> on the tier
    /// the token names. Reported at the member that is wrong rather than at the block, because
    /// several rules here emit the same code and the pointer is what says which one fired.
    /// </para>
    /// <para>
    /// <b>The schema.</b> <c>chapter.schema.json</c> must permit exactly that one tier — an
    /// <c>enum</c> beside or instead of the <c>const</c> is two answers to which tiers a chapter
    /// may name, and the wider of them is the one an author will discover. This arm is what stops
    /// the schema's <c>const</c> and the ladder from drifting apart.
    /// </para>
    /// <para>
    /// The schema arm reads the schema set directly instead of through <see cref="Find"/>: a schema
    /// is not part of the snapshot, so recording its pointer as a data reference would name
    /// something the shipped data set can never resolve.
    /// </para>
    /// </remarks>
    private static void ChapterUnlockConditionsAreExactlyTheLaddersNormalRung(
        IReadOnlyDictionary<string, ContentValue> documents,
        IReadOnlyDictionary<string, ContentValue> schemas,
        List<ContentIssue> issues)
    {
        var token = Find(documents, NormalRungClearReference);
        if (token is null)
        {
            return;
        }

        if (token.Kind != ContentValueKind.Text ||
            !string.Equals(token.AsText(), PreviousChapterNormalToken, StringComparison.Ordinal))
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OrphanedReference, NormalRungClearReference,
                $"10 §7: the Normal rung demands {token}, and a chapter's own unlockCondition can " +
                $"only ever restate '{PreviousChapterNormalToken}'. Reported rather than skipped: " +
                "a rule that stopped knowing how to translate the ladder would leave every " +
                "chapter's authored gate unchecked, silently."));
            return;
        }

        CheckEachChapterRestatesTheNormalRung(documents, issues);
        CheckTheChapterSchemaPermitsOnlyTheLaddersTier(schemas, issues);
    }

    private static void CheckEachChapterRestatesTheNormalRung(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        foreach (var (path, root) in documents
                     .Where(d => d.Key.StartsWith(ChaptersDirectory, StringComparison.Ordinal))
                     .OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            // Keyed on the document's own id rather than on its path: the file names are
            // author-chosen, and the id is what the ladder counts in.
            if (!root.TryGetMember("id", out var id) || id!.Kind != ContentValueKind.Number)
            {
                continue;
            }

            var chapter = id.AsInt32();
            var block = $"{path}#/{UnlockConditionMember}";
            var authored = Find(documents, block);

            if (chapter <= FirstChapterId)
            {
                if (authored is not null && !authored.IsUnauthorised)
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OrphanedReference, block,
                        $"10 §7: chapter {ChapterNumber(chapter)} authors an unlock condition, and " +
                        $"'{PreviousChapterNormalToken}' names no chapter before the first one. " +
                        "The first chapter carries null, which is the ladder demanding nothing of " +
                        "it — an authored pair here is a prerequisite no player can ever meet."));
                }

                continue;
            }

            if (authored is null || authored.Kind != ContentValueKind.Object)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, block,
                    $"10 §7: chapter {ChapterNumber(chapter)} authors no unlock condition, and the ladder " +
                    $"unlocks it by clearing chapter {ChapterNumber(chapter - 1)} on " +
                    $"{PreviousChapterNormalTier}. Every chapter after the first restates that " +
                    "clear, so its own document says what gates it."));
                continue;
            }

            // Both members are required by the schema, and a load whose schema validation is dirty
            // never reaches a declared rule — so on a well-formed object both of these resolve.
            var clear = Find(documents, $"{block}/{ClearChapterMember}");
            if (clear is null || clear.Kind != ContentValueKind.Number || clear.AsInt32() != chapter - 1)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, $"{block}/{ClearChapterMember}",
                    $"10 §7: chapter {ChapterNumber(chapter)} names {clear?.ToString() ?? "no chapter"} as " +
                    $"its prerequisite, and the ladder unlocks it by clearing chapter " +
                    $"{ChapterNumber(chapter - 1)}. The generic rung is the runtime authority, so a chapter " +
                    "naming any other one describes a gate nothing enforces."));
            }

            var tier = Find(documents, $"{block}/{TierMember}");
            if (tier is null || tier.Kind != ContentValueKind.Text ||
                !string.Equals(tier.AsText(), PreviousChapterNormalTier, StringComparison.Ordinal))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, $"{block}/{TierMember}",
                    $"10 §7: chapter {ChapterNumber(chapter)} asks for a clear on " +
                    $"{tier?.ToString() ?? "no tier"}, and '{PreviousChapterNormalToken}' is a " +
                    $"clear on {PreviousChapterNormalTier}. The Normal rung is the only rung a " +
                    "chapter's own document speaks to."));
            }
        }
    }

    private static void CheckTheChapterSchemaPermitsOnlyTheLaddersTier(
        IReadOnlyDictionary<string, ContentValue> schemas, List<ContentIssue> issues)
    {
        if (!schemas.TryGetValue(ChapterSchemaPath, out var schema))
        {
            return;
        }

        var pointer =
            $"{ChapterSchemaPath}#/properties/{UnlockConditionMember}/properties/{TierMember}";

        ContentValue? constraint = schema;
        foreach (var segment in (string[])["properties", UnlockConditionMember, "properties", TierMember])
        {
            if (constraint.Kind != ContentValueKind.Object ||
                !constraint.TryGetMember(segment, out var member))
            {
                constraint = null;
                break;
            }

            constraint = member!;
        }

        var pinned = constraint is { Kind: ContentValueKind.Object } &&
                     constraint.TryGetMember("const", out var value) &&
                     value!.Kind == ContentValueKind.Text
            ? value.AsText()
            : null;

        var enumerated = constraint is { Kind: ContentValueKind.Object } &&
                         constraint.TryGetMember("enum", out _);

        if (!enumerated && string.Equals(pinned, PreviousChapterNormalTier, StringComparison.Ordinal))
        {
            return;
        }

        var found =
            constraint is null ? "does not constrain the member at all"
            : enumerated ? "enumerates a set of tiers"
            : pinned is null ? "pins no single tier"
            : $"is locked to '{pinned}'";

        issues.Add(new ContentIssue(
            ContentIssueCode.OrphanedReference, pointer,
            $"10 §7: {ChapterSchemaPath}'s unlockCondition.tier {found}, and the ladder's Normal " +
            $"rung ('{PreviousChapterNormalToken}') names exactly one — {PreviousChapterNormalTier}. " +
            "The schema is what stops a chapter authoring a prerequisite the runtime ladder is " +
            "unable to express, so it must permit that tier and no other."));
    }

    /// <summary>A chapter number in a message, formatted the way every other rule here formats one.</summary>
    private static string ChapterNumber(int chapter) => chapter.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------ vocabularies

    private static IReadOnlySet<string> WalletIds(IReadOnlyDictionary<string, ContentValue> documents) =>
        FieldValues(documents, "tuning/currencies.json#/wallet", "id");

    private static IReadOnlySet<string> SourceClassIds(IReadOnlyDictionary<string, ContentValue> documents)
    {
        var classes = FieldValues(documents, "tuning/luck.json#/sourceClasses", "id").ToHashSet(StringComparer.Ordinal);

        // The guild chest is classified as "does not apply", an explicit classification rather
        // than a hole — without the sentinel it would either fake a class it does not have or fail
        // the very rule that exists to catch holes.
        if (classes.Count > 0)
        {
            classes.Add("CHEST_NONE");
        }

        return classes;
    }

    private static IReadOnlySet<string> AdPlacementIds(IReadOnlyDictionary<string, ContentValue> documents)
    {
        var placements = FieldValues(documents, "tuning/ads.json#/inRunPlacements", "id")
            .Concat(FieldValues(documents, "tuning/ads.json#/metaPlacements", "id"))
            .ToHashSet(StringComparer.Ordinal);

        return placements;
    }

    private static IReadOnlySet<string> Keys(IReadOnlyDictionary<string, ContentValue> documents, string reference) =>
        Find(documents, reference) is { Kind: ContentValueKind.Object } value
            ? value.MemberNames.Where(n => !n.StartsWith('_')).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlySet<string> FieldValues(
        IReadOnlyDictionary<string, ContentValue> documents, string arrayReference, string field)
    {
        var array = Find(documents, arrayReference);
        if (array is null || array.Kind != ContentValueKind.Array)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return array.Items
            .Where(i => i.TryGetMember(field, out var value) && value!.Kind == ContentValueKind.Text)
            .Select(i => { i.TryGetMember(field, out var value); return value!.AsText(); })
            .ToHashSet(StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------------------ helpers

    /// <summary>Every <c>path#/pointer</c> these rules have ever looked up, in ordinal order.</summary>
    /// <remarks>
    /// <see cref="Find"/> returns <c>null</c> both for an absent document (fine — the rule is
    /// vacuous) and for a typo'd pointer in a present document (which silently disables the rule),
    /// and nothing can tell them apart — so a test asserts every reference here resolves against
    /// the shipped data. Recorded at lookup rather than restated in a list, so references composed
    /// at run time are covered too.
    /// </remarks>
    internal static IReadOnlyList<string> References =>
        Referenced.Keys.OrderBy(r => r, StringComparer.Ordinal).ToArray();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Referenced =
        new(StringComparer.Ordinal);

    /// <summary>Resolves a <c>path#/pointer</c> reference, or null when anything on the way is absent.</summary>
    internal static ContentValue? Find(IReadOnlyDictionary<string, ContentValue> documents, string reference)
    {
        Referenced.TryAdd(reference, 0);

        if (!ContentReference.TryParse(reference, out var parsed) ||
            !documents.TryGetValue(parsed!.DocumentPath, out var current))
        {
            return null;
        }

        foreach (var segment in parsed.Segments)
        {
            if (current.Kind == ContentValueKind.Object)
            {
                if (!current.TryGetMember(segment, out var member))
                {
                    return null;
                }

                current = member!;
                continue;
            }

            if (current.Kind == ContentValueKind.Array &&
                int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                index < current.Items.Count)
            {
                current = current.Items[index];
                continue;
            }

            return null;
        }

        return current;
    }

    /// <summary>Reads a number, or null when it is absent or unauthorised.</summary>
    internal static decimal? Number(IReadOnlyDictionary<string, ContentValue> documents, string reference) =>
        Find(documents, reference) is { Kind: ContentValueKind.Number } value ? value.AsNumber() : null;

    /// <summary>Two references that the design docs say hold the same value.</summary>
    private static Rule Mirrors(string citation, string left, string right) =>
        (documents, issues) =>
        {
            var a = Find(documents, left);
            var b = Find(documents, right);

            // Vacuous when either side is absent, and skipped when either is unauthorised: there is
            // no number to disagree about.
            if (a is null || b is null || a.IsUnauthorised || b.IsUnauthorised)
            {
                return;
            }

            if (a.Kind == ContentValueKind.Object && b.Kind == ContentValueKind.Object)
            {
                foreach (var name in a.MemberNames.Union(b.MemberNames, StringComparer.Ordinal)
                             .Where(n => !n.StartsWith('_'))
                             .OrderBy(n => n, StringComparer.Ordinal))
                {
                    var inLeft = a.TryGetMember(name, out var leftMember);
                    var inRight = b.TryGetMember(name, out var rightMember);

                    if (inLeft && inRight)
                    {
                        if (!leftMember!.IsUnauthorised && !rightMember!.IsUnauthorised &&
                            !leftMember.Equals(rightMember))
                        {
                            issues.Add(new ContentIssue(
                                ContentIssueCode.OrphanedReference, $"{left}/{name}",
                                $"{citation}: holds {leftMember} but {right}/{name} holds {rightMember}. " +
                                "These are two views of one number and must agree."));
                        }

                        continue;
                    }

                    issues.Add(new ContentIssue(
                        ContentIssueCode.OrphanedReference, inLeft ? $"{left}/{name}" : $"{right}/{name}",
                        $"{citation}: '{name}' is on one side of this mirrored table and not the other."));
                }

                return;
            }

            if (!a.Equals(b))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, left,
                    $"{citation}: holds {a} but {right} holds {b}. These are two views of one value " +
                    "and must agree."));
            }
        };

    /// <summary>
    /// Every field of one object must equal the same-named field of another. Asymmetric on purpose:
    /// the standard dummy is the reference opponent <em>promoted to a live actor</em>, so it carries
    /// a full stat block and the reference carries only the seven fields the model reads.
    /// </summary>
    private static Rule MirrorsFieldsOf(string citation, string subset, string superset) =>
        (documents, issues) =>
        {
            var a = Find(documents, subset);
            var b = Find(documents, superset);

            if (a is not { Kind: ContentValueKind.Object } || b is not { Kind: ContentValueKind.Object })
            {
                return;
            }

            foreach (var name in a.MemberNames.Where(n => !n.StartsWith('_')))
            {
                a.TryGetMember(name, out var left);

                if (!b.TryGetMember(name, out var right))
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OrphanedReference, $"{subset}/{name}",
                        $"{citation}: '{name}' has no counterpart at {superset}."));
                    continue;
                }

                if (!left!.IsUnauthorised && !right!.IsUnauthorised && !left.Equals(right))
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OrphanedReference, $"{subset}/{name}",
                        $"{citation}: holds {left} but {superset}/{name} holds {right}."));
                }
            }
        };

    /// <summary>A number that must equal an arithmetic combination of others.</summary>
    private static Rule Derives(
        string citation, string reference, Func<IReadOnlyDictionary<string, ContentValue>, decimal?> expected) =>
        (documents, issues) =>
        {
            var actual = Find(documents, reference);
            if (actual is null || actual.IsUnauthorised || actual.Kind != ContentValueKind.Number)
            {
                return;
            }

            if (expected(documents) is { } target && actual.AsNumber() != target)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, reference,
                    $"{citation}: holds {actual.AsNumber()} but the values it is derived from give {target}."));
            }
        };

    /// <summary>
    /// A number the design docs say must stay <b>strictly below</b> an arithmetic combination of
    /// others — a bound that is deliberately never met, unlike <see cref="Derives"/>'s equality.
    /// </summary>
    /// <remarks>
    /// Written for the inventory ceiling and the deferred expansion ladder: the ladder's reach has to
    /// stay out of reach, so "equals" is exactly the state that must be refused. An equality rule
    /// cannot express that, and a rule that only checked "not equal" would pass a ceiling raised well
    /// past the ladder.
    /// </remarks>
    private static Rule StaysBelow(
        string citation, string reference, Func<IReadOnlyDictionary<string, ContentValue>, decimal?> bound) =>
        (documents, issues) =>
        {
            var actual = Find(documents, reference);
            if (actual is null || actual.IsUnauthorised || actual.Kind != ContentValueKind.Number)
            {
                return;
            }

            if (bound(documents) is { } target && actual.AsNumber() >= target)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, reference,
                    $"{citation}: holds {actual.AsNumber()}, which is not below the {target} the " +
                    "values it is bounded by give."));
            }
        };

    /// <summary>An array of numbers the design docs say never decreases.</summary>
    private static Rule StrictlyAscending(string citation, string reference) =>
        (documents, issues) =>
        {
            var array = Find(documents, reference);
            if (array is null || array.Kind != ContentValueKind.Array)
            {
                return;
            }

            for (var i = 1; i < array.Items.Count; i++)
            {
                if (array.Items[i - 1].Kind == ContentValueKind.Number &&
                    array.Items[i].Kind == ContentValueKind.Number &&
                    array.Items[i].AsNumber() <= array.Items[i - 1].AsNumber())
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OutOfRange, $"{reference}/{i}",
                        $"{citation}: {array.Items[i].AsNumber()} does not exceed the previous entry " +
                        $"{array.Items[i - 1].AsNumber()}; this ladder is strictly ascending."));
                }
            }
        };

    /// <summary>An array whose length is fixed by a number authored elsewhere.</summary>
    private static Rule CountEquals(
        string citation, string reference, Func<IReadOnlyDictionary<string, ContentValue>, decimal?> expected) =>
        (documents, issues) =>
        {
            var array = Find(documents, reference);
            if (array is null || array.Kind != ContentValueKind.Array || expected(documents) is not { } target)
            {
                return;
            }

            if (array.Items.Count != target)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, reference,
                    $"{citation}: has {array.Items.Count} entries where {target} are authored elsewhere."));
            }
        };

    /// <summary>Every value of one field, across an array, must be in a vocabulary owned elsewhere.</summary>
    private static Rule ValuesResolve(
        string citation, string arrayReference, string field,
        Func<IReadOnlyDictionary<string, ContentValue>, IReadOnlySet<string>> vocabulary) =>
        (documents, issues) =>
        {
            var array = Find(documents, arrayReference);
            var known = vocabulary(documents);

            if (array is null || array.Kind != ContentValueKind.Array || known.Count == 0)
            {
                return;
            }

            for (var i = 0; i < array.Items.Count; i++)
            {
                // A null here means "the docs authorise no value", not "an unknown value".
                if (array.Items[i].TryGetMember(field, out var value) &&
                    value!.Kind == ContentValueKind.Text && !known.Contains(value.AsText()))
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.OrphanedReference, $"{arrayReference}/{i}/{field}",
                        $"{citation}: '{value.AsText()}' is not in the vocabulary that owns it."));
                }
            }
        };

    /// <summary>One field's value must be in a vocabulary owned elsewhere.</summary>
    private static Rule ValueResolves(
        string citation, string reference,
        Func<IReadOnlyDictionary<string, ContentValue>, IReadOnlySet<string>> vocabulary) =>
        (documents, issues) =>
        {
            var value = Find(documents, reference);
            var known = vocabulary(documents);

            if (value is { Kind: ContentValueKind.Text } && known.Count > 0 && !known.Contains(value.AsText()))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, reference,
                    $"{citation}: '{value.AsText()}' is not in the vocabulary that owns it."));
            }
        };

    /// <summary>Every member name of an object must be in a vocabulary owned elsewhere.</summary>
    private static Rule KeysResolveIn(
        string citation, string reference,
        Func<IReadOnlyDictionary<string, ContentValue>, IReadOnlySet<string>> vocabulary) =>
        (documents, issues) =>
        {
            var value = Find(documents, reference);
            var known = vocabulary(documents);

            if (value is not { Kind: ContentValueKind.Object } || known.Count == 0)
            {
                return;
            }

            foreach (var name in value.MemberNames.Where(n => !n.StartsWith('_') && !known.Contains(n)))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, $"{reference}/{name}",
                    $"{citation}: '{name}' is not in the vocabulary that owns it."));
            }
        };

    /// <summary>Every string item of an array must be in a vocabulary owned elsewhere.</summary>
    private static Rule ItemsResolveIn(
        string citation, string reference,
        Func<IReadOnlyDictionary<string, ContentValue>, IReadOnlySet<string>> vocabulary) =>
        (documents, issues) =>
        {
            var value = Find(documents, reference);
            var known = vocabulary(documents);

            if (value is not { Kind: ContentValueKind.Array } || known.Count == 0)
            {
                return;
            }

            foreach (var item in value.Items.Where(i => i.Kind == ContentValueKind.Text)
                         .Select(i => i.AsText()).Where(i => !known.Contains(i)))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, reference,
                    $"{citation}: '{item}' is not in the vocabulary that owns it."));
            }
        };

    /// <summary>Several references that must add up to a stated whole.</summary>
    private static Rule SharesSumTo(string citation, decimal whole, params string[] references) =>
        (documents, issues) =>
        {
            var parts = references.Select(r => Number(documents, r)).ToArray();
            if (parts.Any(p => p is null))
            {
                return;
            }

            var sum = parts.Sum(p => p!.Value);
            if (sum != whole)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, references[0],
                    $"{citation}: the shares total {sum}, not {whole}."));
            }
        };

    /// <summary>A global total that must be the sum of one field across an array.</summary>
    private static Rule SumOfEquals(string citation, string arrayReference, string field, string totalReference) =>
        (documents, issues) =>
        {
            var array = Find(documents, arrayReference);
            var total = Number(documents, totalReference);

            if (array is null || array.Kind != ContentValueKind.Array || total is null)
            {
                return;
            }

            var sum = array.Items
                .Where(i => i.TryGetMember(field, out var v) && v!.Kind == ContentValueKind.Number)
                .Sum(i => { i.TryGetMember(field, out var v); return v!.AsNumber(); });

            if (sum != total)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OutOfRange, totalReference,
                    $"{citation}: is {total} but the parts sum to {sum}. A cap that is not the sum of " +
                    "its parts cannot be enforced from either side."));
            }
        };
}
