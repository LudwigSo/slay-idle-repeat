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
/// Every rule is <b>vacuous when its documents are absent</b>, the same discipline
/// <c>SlayIdleRepeat.Architecture.Tests</c> uses: a rule waiting for M2's content passes over an
/// empty subject set rather than being switched off, and bites the day the data lands.
/// </para>
/// <para>
/// 🔒 A rule must never treat an unauthorised value as zero. Where a leaf is
/// <see cref="ContentValueKind.Unauthorised"/> the rule <em>skips</em>: the design docs have not
/// authorised a number, so there is nothing to compare and inventing one would be the exact bug
/// the null convention exists to prevent.
/// </para>
/// <para>
/// Rules deliberately <b>not</b> stated, because the data contradicts them today and the conflict
/// is documented rather than accidental:
/// <list type="bullet">
/// <item><c>forge#/inventory/maxCapacity == maxCapacityReachableFromLadder</c> — 400 vs 320, the
/// `08` §5 versus `10` §4 / `16` A7 conflict that <c>forge.json</c>'s own <c>_doc</c> flags. The
/// weaker true form is stated instead.</item>
/// <item><c>totalRewardedPerDaySoftCap &gt;= maxInRun + maxMeta</c> — 44 vs 53; the soft cap sits
/// deliberately below the theoretical maximum.</item>
/// <item><c>hasPlus =&gt; ad rates are null</c> — <c>Plus_Lapsed</c> watches ads by design.</item>
/// </list>
/// </para>
/// </remarks>
internal static class DeclaredRules
{
    private delegate void Rule(IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues);

    /// <summary>Runs every declared rule.</summary>
    /// <param name="documents">Data documents by snapshot-relative path. Schemas excluded.</param>
    /// <param name="schemas">
    /// The parsed schema set, which <b>R35</b> needs and no other rule does — an embedded effect is
    /// validated against <c>schema/effect.schema.json</c>, and that is the one rule here whose
    /// authority is a schema rather than a second data file.
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

        EmbeddedEffectsValidateAgainstTheEffectSchema(documents, schemas, issues);
    }

    /// <summary>
    /// 🔒 The <c>schema/</c> path of `18` §1's effect vocabulary — <b>R35</b>'s authority.
    /// </summary>
    internal const string EffectSchemaPath = "schema/effect.schema.json";

    /// <summary>
    /// 🔒 `18` §1's one universal key — the member whose presence makes an object an effect.
    /// <c>effect.schema.json</c> requires it on all seventeen of its branches, which is what lets
    /// <b>R35</b> find an embedded effect without knowing what its owner calls the list.
    /// </summary>
    private const string OpMemberName = "op";

    /// <summary>
    /// 🔒 `18` §1's OTHER universal key, required alongside <see cref="OpMemberName"/> on every one
    /// of the seventeen branches — required together because <c>op</c> alone collides with `18` §4's
    /// OWN vocabulary. A condition's comparison node is <c>{"fn", "op", "value"}</c> (the comparator
    /// is spelled <c>op</c> there too), so a walk keyed on <c>op</c> alone finds a perk's own
    /// <c>condition</c> block and reports it against the top-level effect <c>oneOf</c> — which no
    /// condition node can ever satisfy, since none of the seventeen branches is shaped like one. M3-07
    /// hit this on its first perk with a non-null <c>condition</c>; no earlier content (bosses.json)
    /// ever authored one, which is why nothing caught it sooner. Both keys together are still exactly
    /// `18` §1's universal pair and still nothing a condition node, a <c>duration</c> block or a
    /// <c>stacking</c> block can accidentally satisfy.
    /// </summary>
    private const string IdMemberName = "id";

    /// <summary>
    /// 🔒 Every embedded effect <b>R35</b> has validated so far, as
    /// <c>path#/pointer</c> — the subject-set floor a test asserts against (steering S3).
    /// </summary>
    /// <remarks>
    /// The rule's subject set is discovered structurally rather than from a list of content types,
    /// so nothing in the rule itself says how many effects it <em>ought</em> to have seen. Without
    /// this, a walk that silently matched nothing would pass exactly as loudly as one that validated
    /// every boss mechanic in the repository — which is the vacuous pass the whole file is written
    /// against. Populated by running <see cref="Check"/>, on <see cref="References"/>' pattern.
    /// </remarks>
    internal static IReadOnlyList<string> ValidatedEmbeddedEffects =>
        ValidatedEffects.Keys.OrderBy(e => e, StringComparer.Ordinal).ToArray();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ValidatedEffects =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 🔒 <b>R35 · `14` §6 / `18` §1</b> — every effect <b>embedded</b> in an owning content file
    /// validates against <c>schema/effect.schema.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ═══ 🔒 <b>WHY THIS IS A CROSS-FILE RULE AND NOT A <c>$ref</c></b> ═══
    /// </para>
    /// <para>
    /// `18` §1's effect shape is a closed <c>oneOf</c> partition of the 44 ops into seventeen
    /// key-shapes, and it is written in exactly one file. An owning content schema — a boss script's,
    /// and M3's perk, talent, pet, mount and curse schemas after it — cannot reach it two ways:
    /// <list type="number">
    ///   <item><see cref="JsonSchemaValidator"/> resolves <b>same-document</b> pointers only, and
    ///   refuses a <c>$ref</c> that does not start with <c>#/</c> — <em>"a cross-file <c>$ref</c>
    ///   would make the schema set a graph nobody can review file by file"</em>. So it cannot
    ///   <b>reference</b> the partition.</item>
    ///   <item><c>EffectSchemaTests.No_other_schema_restates_the_effect_vocabulary</c> fails any
    ///   schema outside that file naming three or more op tokens. So it cannot <b>restate</b>
    ///   it either.</item>
    /// </list>
    /// <para>
    /// Both constraints are right, and together they leave an embedded effect validated only as
    /// <em>an object with an id and an op</em> — which would ship a boss mechanic whose op-specific
    /// keys nobody checked. This rule closes that: it walks the embedded effects and runs the real
    /// schema over each one, so the partition stays in one file and still governs every effect in
    /// the repository. `14` §6's guarantee is delivered by the pair.
    /// </para>
    /// </para>
    /// <para>
    /// 🔒 <b>The subject set is structural, not a list of content types.</b> Any <c>effects</c> array
    /// whose items are objects declaring an <c>op</c> is one, wherever it sits — so M3's perks and
    /// talents are covered on the day they land rather than on the day somebody remembers to add
    /// them here. `18` §1: an effect <em>"is always embedded in the perk, talent, pet, mount, curse
    /// or boss script that owns it"</em>, and they all spell that list the same way.
    /// </para>
    /// <para>
    /// ⚠️ <b>An embedded effect with no effect schema to check it against is a finding, not a
    /// skip.</b> Every other rule here is vacuous when its documents are absent, because an absent
    /// document means there is nothing to disagree about. That reading does not transfer: the
    /// subject is present and it is the <em>authority</em> that is missing, so skipping would report
    /// success over unvalidated content — the one outcome this rule exists to prevent.
    /// </para>
    /// </remarks>
    private static void EmbeddedEffectsValidateAgainstTheEffectSchema(
        IReadOnlyDictionary<string, ContentValue> documents,
        IReadOnlyDictionary<string, ContentValue> schemas,
        List<ContentIssue> issues)
    {
        var embedded = new List<(string Location, ContentValue Effect)>();

        // 🔒 content/ only. `18` §1's list of owners — perk, talent, pet, mount, curse, boss script
        // — lives entirely under content/, and the walk's signature is a bare `op` member, so
        // sweeping tuning/ and loc/ too would let a future tuning key innocently called "op" fail
        // with a oneOf message about an effect vocabulary it has nothing to do with.
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
            // ⚠️ Recorded after the call rather than before it. Both orders produce the SAME set —
            // Validate returns findings and does not throw — so this buys nothing mechanically and
            // is not load-bearing; it is written this way so the set reads as what it is named.
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
    /// 🔴 <b>The signature is the <c>id</c>+<c>op</c> PAIR, NOT the member name the list is spelled
    /// under, and NOT <c>op</c> alone.</b> Keying on <c>effects</c> looked equivalent and is not: `18`
    /// §7.7 spells a pet's list <c>aura</c>, and a curse or a mount catalogue may well spell it
    /// something else again — so a name-keyed walk would let a whole content type ship unvalidated
    /// while <see cref="ValidatedEmbeddedEffects"/> stayed comfortably non-empty, which is the one
    /// failure the floor test cannot see. <c>id</c> and <c>op</c> together are what `18` §1 makes
    /// universal and what <c>effect.schema.json</c> requires of every one of its seventeen branches,
    /// so the pair is the signature that actually means <em>this is an effect</em>.
    /// </para>
    /// <para>
    /// 🔴 <b><c>op</c> ALONE IS NOT ENOUGH, and M3-07 is the commit that found out why.</b> `18` §4's
    /// own condition vocabulary spells its comparator <c>op</c> too — a comparison node is
    /// <c>{"fn", "op", "value", …}</c> — so a perk (or talent, pet, mount, curse, boss script) whose
    /// effect carries a non-null <c>condition</c> nests a SECOND object with an <c>op</c> member
    /// several keys down, and a walk keyed on <c>op</c> alone finds it too and reports it against the
    /// top-level effect <c>oneOf</c>, which no condition node can ever satisfy — none of the
    /// seventeen branches is shaped like one. Every earlier embedder (bosses.json) happened to author
    /// no <c>condition</c> at all, so nothing exposed this until M3-07's perks — several of 06 §3's
    /// rows are literally conditional damage bonuses ("+X% damage to enemies below 30% health") and
    /// could not be authored honestly without one. <c>id</c> is the key that tells the two apart: every
    /// effect branch requires it and no condition, duration or stacking shape ever carries one.
    /// </para>
    /// <para>
    /// ⚠️ No double-counting: an effect is added when it is reached, and the walk then descends into
    /// it — but <c>effect.schema.json</c> is <c>additionalProperties: false</c> on every branch and no
    /// branch nests a SECOND <c>id</c>+<c>op</c> pair at its own top level, so there is nothing
    /// shaped like an effect inside one to double-count. (A <c>condition</c> block CAN nest a bare
    /// <c>op</c>, which is exactly the collision above — it just never nests an <c>id</c> beside it.)
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
        // ── R1/R2 · `24` §6.1-6.2 — forge.json holds the forge-screen view of numbers luck.json owns.
        Mirrors("24 §6.1 (reforge is one set of numbers, viewed twice)",
            "tuning/forge.json#/reforge/costByRarity", "tuning/luck.json#/reforge/costByRarity"),
        Mirrors("24 §6.1", "tuning/forge.json#/reforge/currency", "tuning/luck.json#/reforge/currency"),
        Mirrors("24 §6.2", "tuning/forge.json#/retune/costByRarity", "tuning/luck.json#/retune/costByRarity"),
        Mirrors("24 §6.2", "tuning/forge.json#/retune/currency", "tuning/luck.json#/retune/currency"),
        Mirrors("24 §6.2", "tuning/forge.json#/retune/lockCostMultiplier",
            "tuning/luck.json#/retune/lockCostMultiplier"),

        // ── R3 · `10` §4 — the inventory ladder and the capacity it reaches are one fact.
        Derives("10 §4 (baseCapacity + maxPurchases x slotsPerPurchase)",
            "tuning/forge.json#/inventory/maxCapacityReachableFromLadder",
            d => Number(d, "tuning/forge.json#/inventory/baseCapacity") is { } capacity &&
                 Number(d, "tuning/currencies.json#/crowns/inventoryExpansionMaxPurchases") is { } purchases &&
                 Number(d, "tuning/currencies.json#/crowns/inventoryExpansionSlotsPerPurchase") is { } slots
                ? capacity + (purchases * slots)
                : null),
        Mirrors("08 §5 / 10 §4 (one expansion step, authored twice)",
            "tuning/forge.json#/inventory/expansionStep",
            "tuning/currencies.json#/crowns/inventoryExpansionSlotsPerPurchase"),
        CountEquals("10 §4 (one ladder price per purchase step)",
            "tuning/currencies.json#/crowns/inventoryExpansionLadder",
            d => Number(d, "tuning/currencies.json#/crowns/inventoryExpansionMaxPurchases")),

        // ── R4 · `10` §1 — every currency named anywhere is a wallet currency.
        ValuesResolve("10 §1 (the wallet is the closed currency vocabulary)",
            "tuning/dungeons.json#/dungeons", "currency", WalletIds),
        ValuesResolve("10 §1", "tuning/currencies.json#/shop/dailyTab/staples", "currency", WalletIds),
        ValuesResolve("10 §1", "tuning/currencies.json#/shop/dailyTab/rotatingPool", "currency", WalletIds),
        KeysResolveIn("10 §1", "tuning/currencies.json#/shop/materialsTab", WalletIds),

        // ── R5 · `24` §3 — "the validator fails the build if a grant source has no class".
        KeysResolveIn("24 §3 (every container is classified in luck.json)",
            "tuning/drops.json#/containerContents", SourceClassIds),
        ItemsResolveIn("24 §5 (Focus applies only to classified gear sources)",
            "tuning/luck.json#/focus/appliesToClasses", SourceClassIds),
        ValuesResolve("24 §3", "tuning/currencies.json#/loginCalendar/days", "chest", SourceClassIds),

        // ── R6 · `12` §4 — every ad placement named anywhere is in the catalogue.
        ValueResolves("12 §4 (the ad-placement catalogue is closed)",
            "tuning/dungeons.json#/entries/adPlacementId", AdPlacementIds),
        KeysResolveIn("12 §4", "tuning/ads.json#/placementRewardValues", AdPlacementIds),
        KeysResolveIn("12 §5", "tuning/ads.json#/rewardScaling/baseValue", AdPlacementIds),

        // ── R7 · `21` §5.4 — the three ad-behaviour groups partition the catalogue exactly.
        AdPlacementGroupsPartitionTheCatalogue,

        // ── R8 · `12` §1 — a global cap that is not the sum of its parts is unenforceable.
        SumOfEquals("12 §1 (the in-run global cap is the sum of its placements)",
            "tuning/ads.json#/inRunPlacements", "cap", "tuning/ads.json#/globalCaps/maxInRunImpressionsPerRun"),
        SumOfEquals("12 §1 (the meta global cap is the sum of its placements)",
            "tuning/ads.json#/metaPlacements", "cap", "tuning/ads.json#/globalCaps/maxMetaImpressionsPerDay"),

        // ── R9 · `12` §5 — every material bundle is worth the same in Crowns.
        AdBundlesShareOneCrownEquivalence,
        Mirrors("12 §5 (one chapter scalar, authored twice)",
            "tuning/ads.json#/rewardScaling/chapterScalar", "tuning/currencies.json#/chapterScalars/adBundleScalar"),

        // ── R10 · `12` §3 — the fairness contract and the assertion that grades it are one number.
        FairnessContractMatchesAssertionA3,

        // ── R11 · `21` §5.4 — the profile vocabulary is shared by three files.
        ProfileNamesAgreeAcrossFiles,

        // ── R12 · `29` §6 — expected power never goes down, and every ladder resolves.
        ExpectedProgressionIsWellFormed,

        // ── R13 · `29` §2-5 — the par table is its own default fill.
        ParPowerTableMatchesItsDefaultFill,

        // ── R14 · `29` §2.5 — K_POWER is defined as Chapter 1 Normal par.
        Mirrors("29 §2.5.1 (K_POWER := Chapter 1 Normal par)",
            "tuning/calibration_builds.json#/referenceParBuild/targetPower",
            "tuning/par_power.json#/defaultFill/chapterPowerTargetBase"),
        MirrorsFieldsOf("29 §2.2 (the reference opponent promoted to a live actor)",
            "tuning/power_model.json#/referenceOpponent", "tuning/calibration_builds.json#/standardDummy"),

        // ── R15 · `29` §2.5 — the dummy's output per second is derived from its own stat block.
        Derives("29 §2.5 (atk x aspd x (1 + crit x critDamage))",
            "tuning/calibration_builds.json#/measurementProtocol/dummyOutputPerSecond",
            d => Number(d, "tuning/calibration_builds.json#/standardDummy/atk") is { } atk &&
                 Number(d, "tuning/calibration_builds.json#/standardDummy/aspd") is { } aspd &&
                 Number(d, "tuning/calibration_builds.json#/standardDummy/crit") is { } crit &&
                 Number(d, "tuning/calibration_builds.json#/standardDummy/critDamage") is { } critDamage
                ? atk * aspd * (1m + (crit * critDamage))
                : null),

        // ── R16 · `08` §3.0a — a null slot coefficient means "read percentStatsByRarity instead".
        PercentStatTableMirrorsNullSlotCoefficients,

        // ── R17 · `08` §6 — drop bands tile every chapter exactly once.
        DropBandsTileEveryChapterExactlyOnce,

        // ── R18 · `24` §4 — a soft pity that sits above its hard pity protects nothing.
        SoftPitySitsBelowItsHardPity,

        // ── R19 · `25` §3-4 — the dungeon's tiles, payouts and entry budget are one shape.
        DungeonStructureIsInternallyClosed,

        // ── R20 · `10` §7 — progression.json#/unlocks is the single source of truth for gates.
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

        // ── R21 · `10` §2 — the energy grants agree with the systems that own them.
        Mirrors("12 §4 (the ad energy grant is authored once)",
            "tuning/progression.json#/energy/sources/AD_ENERGY/amount",
            "tuning/ads.json#/placementRewardValues/AD_ENERGY/energy"),
        Mirrors("19 Part B (the daily-quest energy grant is authored once)",
            "tuning/progression.json#/energy/sources/DAILY_QUEST/amount",
            "tuning/currencies.json#/dailyQuests/rewardPerQuest/energy"),
        Mirrors("25 §4 (the dungeon energy cost is authored once)",
            "tuning/progression.json#/energy/dungeonCost", "tuning/dungeons.json#/structure/energyCost"),

        // ── R22 · `10` §2 — the Soul-Shard container prices are authored three times.
        Mirrors("10 §2 / 07 §2.3 (the pet-egg price)",
            "tuning/currencies.json#/soulShards/sinks/PET_EGG",
            "tuning/beasts.json#/containerPrices/petEggSoulShards"),
        Mirrors("10 §2 / 07 §3.2 (the mount-crate price)",
            "tuning/currencies.json#/soulShards/sinks/MOUNT_CRATE",
            "tuning/beasts.json#/containerPrices/mountCrateSoulShards"),

        // ── R23 · the ladders the docs call ordered and no keyword can.
        StrictlyAscending("08 §4.2 (enhance stone costs)", "tuning/forge.json#/enhance/stoneCostPerLevel"),
        StrictlyAscending("10 §4 (the inventory ladder)",
            "tuning/currencies.json#/crowns/inventoryExpansionLadder"),
        StrictlyAscending("03 §7 (the chapter price scalar)", "tuning/currencies.json#/shopTile/chapterPriceScalar"),
        StrictlyAscending("29 §6 (the checkpoint days)", "tuning/expected_progression.json#/checkpointDays"),

        // ── R24 · `08` §4.2 — the enhance ladder is self-consistent.
        EnhanceLadderIsSelfConsistent,

        // ── R25 · `02` §5.1a / `10` §4 — one chapter growth rate, authored twice.
        Mirrors("02 §5.1a (XP and gold grow at the same rate)",
            "tuning/progression.json#/runXp/baseXpGrowth", "tuning/currencies.json#/chapterScalars/goldGrowth"),

        // ── R26 · `26` §3 — the event calendar's two shares are a partition.
        SharesSumTo("26 §3 (every major event is one archetype or the other)", 1m,
            "tuning/events.json#/calendar/chapterEventShare",
            "tuning/events.json#/calendar/collectionEventShare"),

        // ── R27 · `26`/`25`/`27` — one daily reset, or the day boundary fragments.
        Mirrors("26 §3 / 25 §4 (one daily reset time)",
            "tuning/events.json#/calendar/startEndUtc", "tuning/dungeons.json#/entries/refreshUtc"),
        Mirrors("27 §3 (one daily reset time)",
            "tuning/guilds.json#/quests/drawUtc", "tuning/dungeons.json#/entries/refreshUtc"),

        // ── R28 · `10` §4.2 — the daily shop draw has to be satisfiable.
        ShopDailyDrawIsSatisfiable,

        // ── R29 · `27` §3.1 — the guild quest pool has to be drawable.
        GuildQuestPoolIsDrawable,

        // ── R30 · `05` §4 / `29` §2.3 — the two mitigation dials are one pair of numbers, written
        // twice. `05` §4 states them for the simulator ("expose them in data") and `29` §2.3 uses
        // the same formula for MitigationVsReference, which is what grades the simulator. If the
        // pair ever diverges, the power model predicts a mitigation the fight does not produce and
        // `05` §9's assertion A10 — the closed form tracking EmpiricalPower within ±12% — becomes
        // unfalsifiable rather than false. Stated as a rule rather than solved by deleting one copy:
        // combat_caps.json is what the simulator loads, power_model.json is what `21` sweeps, and
        // neither file may reach into the other's directory.
        // Stated over the two BLOCKS rather than as two scalar mirrors, so that a third dial added
        // to one side and not the other is caught as well as a value that drifts. Mirrors ignores
        // `_`-prefixed members, so combat_caps.json's `_doc` is not compared against nothing.
        Mirrors("05 §4 / 29 §2.3 (one pair of mitigation dials, authored twice)",
            "content/combat_caps.json#/mitigation", "tuning/power_model.json#/mitigation"),

        // ── R31 · `05` §6.4 — a chapter's enemy pool is a weight table over the eight archetypes,
        // and 05 §6.4 states "Weights per row sum to 100". No JSON Schema keyword can add up an
        // object's values, so the total is stated here. ⚠️ The two authored ZEROS — Chapter 1's
        // REAVER and Chapter 6's LEECH — are NOT checked by the total: a row that moved five points
        // from GRUNT to REAVER still sums to 100 and would erase "no 30%-crit spikes in the tutorial
        // chapter" in silence. They are pinned by name in EnemiesDataTests instead, which is where
        // the rest of the transcription is asserted.
        ChapterPoolWeightsSumToOneHundred,

        // ── R32 · `05` §6.2/§6.4 — each chapter's elitePool is exactly its two biome elites, and
        // elites come only from it. Every identity therefore belongs to exactly one pool: an
        // identity in none is an elite nothing can ever draw, and one in two is a biome leak.
        EliteIdentitiesAreEachInExactlyOneChapterPool,

        // ── R33 · `05` §6.0 — EnemyLevel(c, t) needs a base level for every chapter that has a
        // pool, or the chapter derives level-0 enemies and 05 §4's mitigation denominator reads an
        // attacker that never grows.
        // ⚠️ Stated honestly: on the SHIPPED shape this is belt-and-braces rather than coverage.
        // enemies.schema.json pins both arrays to 8 rows with `chapter` 1..8 and required, and
        // ContentInvariants treats `chapter` as an identity member, so the two sets are already
        // forced to be exactly {1..8}. It bites the day either array's bounds are relaxed — which is
        // exactly what an eighth-chapter-plus content pack would do — and it costs one pass.
        EveryChapterWithAPoolHasABaseEnemyLevel,

        // ── R34 · `05` §6.4 / `03` §4 — the pool weights are authored TWICE.
        // enemies.json#/chapterPools is M2-11's producer-side transcription; each chapter's own
        // `enemyPool` field (chapter.schema.json) is what the board actually draws from. M3-14
        // landed chapters 1-2 (content/chapters/CH_01_GREENWOOD_VALE.json,
        // CH_02_ASHEN_MIRE.json), so this rule is armed for those two today and stays vacuous for
        // chapters 3-8 until M11-02 lands their rows — the only moment each pair can start to
        // disagree.
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

        // 🔒 A null coefficient means "read percentStatsByRarity instead" — the null is the
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

            // 🔒 A null softPity means 24 §4.2 does not authorise one here (Apex needs none at that
            // density). Skipping is correct; reading it as "threshold 0" would invent a guarantee.
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

    /// <summary>The document `05` §6's tables live in.</summary>
    private const string EnemiesDocument = "content/enemies/enemies.json";

    /// <summary>`05` §6.4 — <em>"Weights per row sum to 100."</em></summary>
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

                // 🔒 An unauthorised weight is skipped, not read as zero — and it would then fail
                // the total, which is the right way round: a null weight is a hole and the row it
                // sits in cannot be said to sum to anything.
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

    /// <summary>`05` §6.2 — each chapter's <c>elitePool</c> is exactly its two biome elites.</summary>
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

    /// <summary>`05` §6.0 — every chapter that fields enemies has a <c>BaseEnemyLevel</c>.</summary>
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

    /// <summary>
    /// `05` §6.4 / `03` §4 — a chapter file's <c>enemyPool</c> is the same weight table
    /// <c>enemies.json</c> transcribes for that chapter.
    /// </summary>
    /// <remarks>
    /// Keyed on the chapter file's own <c>id</c> rather than on its path, because the file names are
    /// M3-14's to choose. A chapter file whose <c>id</c> matches no <c>chapterPools</c> row is left
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

    // ------------------------------------------------------------------------ vocabularies

    private static IReadOnlySet<string> WalletIds(IReadOnlyDictionary<string, ContentValue> documents) =>
        FieldValues(documents, "tuning/currencies.json#/wallet", "id");

    private static IReadOnlySet<string> SourceClassIds(IReadOnlyDictionary<string, ContentValue> documents)
    {
        var classes = FieldValues(documents, "tuning/luck.json#/sourceClasses", "id").ToHashSet(StringComparer.Ordinal);

        // 🔒 `27` §8 / `24` §3: the guild chest is classified as "24 does not apply", which is an
        // explicit classification and not a hole. Without the sentinel a guild chest would either
        // have to fake a class it does not have or fail the very rule that exists to catch holes.
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

    /// <summary>
    /// 🔒 Every <c>path#/pointer</c> these rules have ever looked up, in ordinal order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Find"/> returns <c>null</c> for two very different facts: "the document is absent"
    /// — correct, and the reason a rule waiting on M2's content is vacuous rather than switched off
    /// — and "the pointer is a typo in a document that is present", which disables the rule in
    /// silence. Nothing could tell them apart, and
    /// <c>The_shipped_data_set_validates_with_no_issues</c> passes <em>hardest</em> when every rule
    /// is dead.
    /// </para>
    /// <para>
    /// Recorded at lookup rather than restated in a list, so a reference composed at run time
    /// (<c>tuning/luck.json#/{block}/softPity</c>, <c>…/tierMultiplier/{tier}</c>) is covered too —
    /// a hand-maintained list would miss exactly those. The set only ever grows to the literals in
    /// this file, and a test asserts every one of them resolves against the shipped data.
    /// </para>
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
                // 🔒 A null here means "the docs authorise no value", not "an unknown value".
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
