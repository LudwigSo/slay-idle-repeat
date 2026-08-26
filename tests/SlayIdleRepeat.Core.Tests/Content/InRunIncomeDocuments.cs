using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic fixtures for the four documents the tile resolvers read:
/// <c>tuning/currencies.json</c>'s <c>chapterScalars</c> and <c>inRunIncome</c> blocks,
/// <c>content/curses/curses.json</c>, and <c>content/board_events/board_events.json</c>.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped files rather than reading them — the
/// same shape <see cref="CurrenciesDocuments"/> already uses.
/// <para>
/// The mirror is not the whole guarantee: a fixture that mirrors can drift from what ships, so each
/// reader also has one test that loads the real <c>game-data</c> tree through
/// <c>GameDataLoader</c> and pins the reader against it. The hermetic tests prove the rules are right
/// about the numbers; the real-data tests prove those are the numbers we ship.
/// </para>
/// </remarks>
internal static class InRunIncomeDocuments
{
    /// <summary>Where the chapter scalars and in-run income blocks live.</summary>
    internal const string CurrenciesPath = "tuning/currencies.json";

    internal const string CursesPath = "content/curses/curses.json";

    internal const string BoardEventsPath = "content/board_events/board_events.json";

    /// <summary>Where the AD_REVIVE numbers live.</summary>
    internal const string AdsPath = "tuning/ads.json";

    /// <summary>The revive heal share of Max HP, as shipped.</summary>
    internal const decimal ShippedReviveHealPctMaxHp = 0.66m;

    /// <summary>The revive invulnerability window in seconds, as shipped.</summary>
    internal const int ShippedReviveInvulnerabilitySeconds = 2;

    /// <summary><c>G(c) = goldGrowth^(c-1)</c>'s base, as shipped.</summary>
    internal const decimal ShippedGoldGrowth = 1.55m;

    /// <summary><c>M(c) = metaGrowth^(c-1)</c>'s base, as shipped.</summary>
    internal const decimal ShippedMetaGrowth = 1.35m;

    /// <summary>The only rounding mode authored, and the only one implemented.</summary>
    internal const string ShippedRoundingMode = "NEAREST_INTEGER";

    /// <summary>The chapter-1 cache payout, as shipped.</summary>
    internal const long ShippedBeastFeedBase = 25;

    /// <summary>The flat Pet Egg rate, as shipped.</summary>
    internal const decimal ShippedEggChance = 0.06m;

    /// <summary>How many distinct options a shrine offers, as shipped.</summary>
    internal const int ShippedOptionsOffered = 2;

    /// <summary>The campfire rest's healed share of Max HP, as shipped.</summary>
    internal const decimal ShippedCampfireHeal = 0.4m;

    /// <summary>The Stage Gate's healed share of Max HP, as shipped.</summary>
    internal const decimal ShippedStageGateHeal = 0.15m;

    /// <summary>The draft's skip Gold reward, as shipped.</summary>
    internal const long ShippedSkipGoldReward = 60;

    /// <summary>The draft's reroll Gold cost, as shipped.</summary>
    internal const long ShippedRerollGoldCost = 60;

    /// <summary>GoldPerKill's chapter-1 base, as shipped.</summary>
    internal const long ShippedGoldPerKillBase = 40;

    /// <summary>The Elite Gold multiplier, as shipped.</summary>
    internal const long ShippedGoldPerKillEliteMultiplier = 3;

    /// <summary>The Boss Gold multiplier, as shipped.</summary>
    internal const long ShippedGoldPerKillBossMultiplier = 10;

    /// <summary>Normal-tier Boss-kill Soul Shards per chapter, as shipped.</summary>
    internal const long ShippedBossKillNormalPerChapter = 15;

    /// <summary>Heroic-tier Boss-kill Soul Shards per chapter, as shipped.</summary>
    internal const long ShippedBossKillHeroicPerChapter = 40;

    /// <summary>Mythic-tier Boss-kill Soul Shards per chapter, as shipped.</summary>
    internal const long ShippedBossKillMythicPerChapter = 100;

    /// <summary>The first-clear grant range floor, as shipped.</summary>
    internal const long ShippedFirstClearMin = 100;

    /// <summary>The first-clear grant range ceiling, as shipped.</summary>
    internal const long ShippedFirstClearMax = 800;

    /// <summary>The three treasure profiles, in the document's own order.</summary>
    internal static readonly (string Id, decimal Weight, long Crowns, long Stones, long Dust)[]
        ShippedTreasureProfiles =
        {
            ("COIN_HOARD", 55m, 40, 2, 0),
            ("STONE_CACHE", 30m, 18, 6, 0),
            ("DUST_TROVE", 15m, 15, 0, 10),
        };

    /// <summary>The pool of ten shrine buffs, in the document's own order.</summary>
    /// <remarks>
    /// <c>SHR_HP</c> (index 1) carries BOTH a stat raise and an immediate heal, and
    /// <c>SHR_HEAL</c> (index 9) carries only the heal, with <c>stat</c> and <c>magnitude</c>
    /// authored as deliberate <c>null</c>s. Those two rows are the reason this fixture carries all
    /// ten rather than a representative two.
    /// </remarks>
    internal static readonly (string Id, string? Stat, decimal? Magnitude, decimal? Heal)[]
        ShippedShrineBuffs =
        {
            ("SHR_ATK", "ATK", 0.12m, null),
            ("SHR_HP", "MAX_HP", 0.18m, 0.18m),
            ("SHR_ASPD", "ASPD", 0.1m, null),
            ("SHR_CRIT", "CRIT", 0.08m, null),
            ("SHR_DEF", "DEF", 0.2m, null),
            ("SHR_LS", "LIFESTEAL", 0.06m, null),
            ("SHR_DR", "DR", -0.06m, null),
            ("SHR_THORN", "THORNS", 0.25m, null),
            ("SHR_GOLD", "GOLD_GAIN", 0.15m, null),
            ("SHR_HEAL", null, null, 0.4m),
        };

    /// <summary>The twelve curse rows, in the document's own order — id, reward prose, and chapter gate.</summary>
    /// <remarks>
    /// The <c>effect</c> column is filled with a placeholder rather than transcribed: nothing in
    /// <c>Core</c> reads it, so transcribing it here would imply something does. The <c>reward</c>
    /// column IS transcribed, because <c>CurseRewards</c>' four-row table is cross-checked against it.
    /// </remarks>
    internal static readonly (string Id, string Reward, int AvailableFrom)[] ShippedCurses =
    {
        ("CUR_SLIPPERY", "+250 Gold", 1),
        ("CUR_MARKED", "+2 Enhance Stones", 1),
        ("CUR_FRACTURED", "+500 Gold", 1),
        ("CUR_HUNTED", "+1 gear drop per Elite", 5),
        ("CUR_UNTIMELY", "+300 Crowns", 3),
        ("CUR_FAMISHED", "+12% ATK", 3),
        ("CUR_BRITTLE_BONES", "+8% Crit Chance", 3),
        ("CUR_MISERLY", "+600 Gold", 3),
        ("CUR_TITHE", "+20% gear drop chance", 3),
    };

    // ---------------------------------------------------------------- the gear grant path

    /// <summary>How many items an Elite kill drops, as shipped.</summary>
    internal const int ShippedEliteKillItems = 1;

    /// <summary>The fewest items a Boss kill drops, as shipped.</summary>
    internal const int ShippedBossKillItemsMin = 2;

    /// <summary>The most items a Boss kill drops, as shipped.</summary>
    internal const int ShippedBossKillItemsMax = 3;

    /// <summary>The per-kill chance an ordinary enemy drops anything, as shipped.</summary>
    internal const decimal ShippedNormalEnemyChance = 0.08m;

    /// <summary>The whole shipped set these resolvers read, with no leaf replaced.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// The shipped set with individual leaves replaced. Omit a parameter to keep the shipped value.
    /// </summary>
    /// <param name="eggChance">
    /// ⚠️ Exists because the cache's egg branch is a <em>probability boundary</em>: pinning which
    /// side of it a fixed seed lands on needs a rate the test chose, not the shipped 0.06 that
    /// almost never fires.
    /// </param>
    /// <param name="metaGrowth">
    /// ⚠️ Exists for the same class of reason as <c>CurrenciesDocuments</c>' <c>legendLevelMin</c>:
    /// <c>M(1)</c> is 1 whatever the growth base is, so a chapter-1 test cannot tell a real
    /// <c>MetaScalar</c> call from a hardcoded 1.
    /// </param>
    /// <param name="roundingMode">For the refusal case — the one mode this reader implements.</param>
    /// <param name="campfireHeal">
    /// Exists so the campfire's rest can be pinned as genuinely READ rather than hardcoded — the
    /// same reason <paramref name="metaGrowth"/> exists: the heal used to be a C# constant, and a
    /// test that only ever saw the shipped 0.4 could not tell the two apart.
    /// </param>
    /// <param name="cards">A purpose-built card list, for the event resolver's own tests.</param>
    internal static ContentSnapshot With(
        ContentValue? eggChance = null,
        ContentValue? metaGrowth = null,
        ContentValue? goldGrowth = null,
        ContentValue? roundingMode = null,
        ContentValue? treasureProfiles = null,
        ContentValue? shrineBuffs = null,
        ContentValue? optionsOffered = null,
        ContentValue? campfireHeal = null,
        ContentValue? stageGateHeal = null,
        ContentValue? skipGoldReward = null,
        ContentValue? rerollGoldCost = null,
        ContentValue? curses = null,
        ContentValue? cards = null,
        ContentValue? goldPerKillBase = null,
        ContentValue? goldPerKillEliteMultiplier = null,
        ContentValue? goldPerKillBossMultiplier = null,
        ContentValue? bossKillNormalPerChapter = null,
        ContentValue? bossKillHeroicPerChapter = null,
        ContentValue? bossKillMythicPerChapter = null,
        ContentValue? firstClearMin = null,
        ContentValue? firstClearMax = null,
        ContentValue? reviveHealPctMaxHp = null,
        ContentValue? reviveInvulnerabilitySeconds = null) =>
        new(
            ContentVersion.FromHex(new string('a', ContentVersion.HexLength)),
            [
                // tuning/progression.json is here because GameRules.Apply CLONES the slice through
                // Player.Rehydrate before a handler ever runs, and that reads the Legend Level range.
                // A context missing it fails inside Apply's clone rather than in the rule under test —
                // a confusing failure for every test in this suite, none of which is about Legend Level.
                ProgressionDocuments.Shipped.GetDocument(ProgressionDocuments.DocumentPath),
                new ContentDocument(CurrenciesPath, Currencies(
                    eggChance, metaGrowth, goldGrowth, roundingMode, treasureProfiles, shrineBuffs,
                    optionsOffered, campfireHeal, stageGateHeal, skipGoldReward, rerollGoldCost,
                    goldPerKillBase, goldPerKillEliteMultiplier, goldPerKillBossMultiplier,
                    bossKillNormalPerChapter, bossKillHeroicPerChapter, bossKillMythicPerChapter,
                    firstClearMin, firstClearMax)),
                new ContentDocument(CursesPath, Curses(curses)),
                new ContentDocument(BoardEventsPath, BoardEvents(cards)),
                new ContentDocument(AdsPath, Ads(reviveHealPctMaxHp, reviveInvulnerabilitySeconds)),

                // RESOLVE_TILE's Portal branch calls Rules.Board.BoardResolution.Resolve, which reads
                // a real content/chapters/ document via ChapterBoardTuning — every ResolveTileTests
                // world needs one to exist, the same chapter-1 shape ChapterDocuments.ChapterOne
                // already mirrors for Rules-level tests.
                ChapterDocuments.Document(chapterId: 1, ChapterDocuments.ChapterOnePath),

                // 🔴 The five documents the in-run GEAR GRANT reads, and they are here for exactly
                // the reason the chapter document above is: a won battle over a kill tile now banks
                // gear, so CONFIRM_BATTLE_RESULT resolves a band, mints an item and files it — and a
                // set short of one of them would fail inside a content reader rather than in the
                // rule any of these suites is about. Borrowed whole from the fixtures that own them
                // rather than re-mirrored, so there is one transcription of each number.
                LuckDocuments.Shipped.GetDocument(LuckDocuments.DocumentPath),
                GearDocuments.Shipped.GetDocument(GearDocuments.GearDocumentPath),
                GearDocuments.Shipped.GetDocument(GearDocuments.ParPowerDocumentPath),
                new ContentDocument(GearDocuments.DropsDocumentPath, DropsWithAcquisitionRates()),
                InventoryDocuments.Shipped.GetDocument(InventoryDocuments.ForgeDocumentPath),
            ]);

    /// <summary>
    /// <see cref="GearDocuments"/>' <c>tuning/drops.json</c> plus the acquisition rates, which that
    /// fixture deliberately does not carry.
    /// </summary>
    /// <remarks>
    /// The rates are the one block of that document nothing under <c>Rules/Gear/</c> reads — they
    /// are the HANDLER's, which is why they are transcribed at the handler suite's fixture rather
    /// than at the generator's. <c>treasureTileChance</c> is an authored <c>null</c> here as it is
    /// in the shipped file, so a reader that started taking it would fail here too.
    /// </remarks>
    private static ContentValue DropsWithAcquisitionRates()
    {
        var drops = GearDocuments.Shipped.GetDocument(GearDocuments.DropsDocumentPath).Root;

        var members = drops.MemberNames
            .Select(name => (Name: name, Value: Member(drops, name)))
            .Append((
                Name: "acquisitionRates",
                Value: Obj(
                    ("eliteKillItems", ContentValue.Number(ShippedEliteKillItems)),
                    ("bossKillItemsMin", ContentValue.Number(ShippedBossKillItemsMin)),
                    ("bossKillItemsMax", ContentValue.Number(ShippedBossKillItemsMax)),
                    ("normalEnemyChance", ContentValue.Number(ShippedNormalEnemyChance)),
                    ("treasureTileChance", ContentValue.Unauthorised))))
            .ToArray();

        return Obj(members);
    }

    /// <summary>One member of an object value, or a failure naming the member that went missing.</summary>
    private static ContentValue Member(ContentValue root, string name) =>
        root.TryGetMember(name, out var value) && value is not null
            ? value
            : throw new InvalidOperationException(
                "'" + name + "' is a member this value enumerated and then did not hand over.");

    private static ContentValue Ads(ContentValue? healPctMaxHp, ContentValue? invulnerabilitySeconds) =>
        Obj(("placementRewardValues", Obj(
            ("AD_REVIVE", Obj(
                ("healPctMaxHp", healPctMaxHp ?? ContentValue.Number(ShippedReviveHealPctMaxHp)),
                ("invulnerabilitySeconds", invulnerabilitySeconds ?? ContentValue.Number(ShippedReviveInvulnerabilitySeconds)))))));

    /// <summary>A snapshot whose <c>tuning/currencies.json</c> has an arbitrary root value.</summary>
    /// <remarks>For the "the block is not an array / not an object" refusals, which
    /// <see cref="With"/>'s leaf-replacement shape cannot express.</remarks>
    internal static ContentSnapshot CurrenciesRoot(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('b', ContentVersion.HexLength)),
            [new ContentDocument(CurrenciesPath, root)]);

    /// <summary>A snapshot whose <c>content/curses/curses.json</c> has an arbitrary root value.</summary>
    internal static ContentSnapshot CursesRoot(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('b', ContentVersion.HexLength)),
            [new ContentDocument(CursesPath, root)]);

    /// <summary>A snapshot whose <c>content/board_events/board_events.json</c> has an arbitrary root.</summary>
    internal static ContentSnapshot BoardEventsRoot(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('b', ContentVersion.HexLength)),
            [new ContentDocument(BoardEventsPath, root)]);

    private static ContentValue Currencies(
        ContentValue? eggChance,
        ContentValue? metaGrowth,
        ContentValue? goldGrowth,
        ContentValue? roundingMode,
        ContentValue? treasureProfiles,
        ContentValue? shrineBuffs,
        ContentValue? optionsOffered,
        ContentValue? campfireHeal,
        ContentValue? stageGateHeal,
        ContentValue? skipGoldReward,
        ContentValue? rerollGoldCost,
        ContentValue? goldPerKillBase = null,
        ContentValue? goldPerKillEliteMultiplier = null,
        ContentValue? goldPerKillBossMultiplier = null,
        ContentValue? bossKillNormalPerChapter = null,
        ContentValue? bossKillHeroicPerChapter = null,
        ContentValue? bossKillMythicPerChapter = null,
        ContentValue? firstClearMin = null,
        ContentValue? firstClearMax = null) =>
        Obj(
            ("chapterScalars", Obj(
                ("goldGrowth", goldGrowth ?? ContentValue.Number(ShippedGoldGrowth)),
                ("metaGrowth", metaGrowth ?? ContentValue.Number(ShippedMetaGrowth)),
                ("roundingMode", roundingMode ?? ContentValue.Text(ShippedRoundingMode)))),
            ("boardGeneration", Obj(
                ("forkBiasPlusMultiplier", ContentValue.Number((decimal)TuningDocuments.ShippedForkBiasPlusMultiplier)),
                ("forkBiasMinusMultiplier", ContentValue.Number((decimal)TuningDocuments.ShippedForkBiasMinusMultiplier)))),
            ("inRunIncome", Obj(
                ("goldPerKill", Obj(
                    ("base", goldPerKillBase ?? ContentValue.Number(ShippedGoldPerKillBase)),
                    ("eliteMultiplier", goldPerKillEliteMultiplier ?? ContentValue.Number(ShippedGoldPerKillEliteMultiplier)),
                    ("bossMultiplier", goldPerKillBossMultiplier ?? ContentValue.Number(ShippedGoldPerKillBossMultiplier)))),
                ("treasureProfiles", Obj(
                    ("profiles", treasureProfiles ?? ContentValue.Array(
                        ShippedTreasureProfiles.Select(p => Obj(
                            ("id", ContentValue.Text(p.Id)),
                            ("weight", ContentValue.Number(p.Weight)),
                            ("crowns", ContentValue.Number(p.Crowns)),
                            ("enhanceStones", ContentValue.Number(p.Stones)),
                            ("mergeDust", ContentValue.Number(p.Dust)))))))),
                ("cache", Obj(
                    ("beastFeedBase", ContentValue.Number(ShippedBeastFeedBase)),
                    ("eggChance", eggChance ?? ContentValue.Number(ShippedEggChance)))),
                ("campfire", Obj(
                    ("healPctMaxHp", campfireHeal ?? ContentValue.Number(ShippedCampfireHeal)))),
                ("stageGate", Obj(
                    ("healPctMaxHp", stageGateHeal ?? ContentValue.Number(ShippedStageGateHeal)))),
                ("shrineBuffPool", Obj(
                    ("optionsOffered", optionsOffered ?? ContentValue.Number(ShippedOptionsOffered)),
                    ("buffs", shrineBuffs ?? ContentValue.Array(
                        ShippedShrineBuffs.Select(ShrineBuff))))))),
            // The shop block, shared with CurrenciesDocuments rather than transcribed again: this
            // fixture replaces the WHOLE of currencies.json, so without it every handler that reads
            // a price throws — which is exactly how the shop tile's own suite discovered it.
            ("shopTile", CurrenciesDocuments.ShopTile()),
            ("draftEconomy", Obj(
                ("skipGoldReward", skipGoldReward ?? ContentValue.Number(ShippedSkipGoldReward)),
                ("rerollGoldCost", rerollGoldCost ?? ContentValue.Number(ShippedRerollGoldCost)))),
            // 🔴 The Crown ladder and the flat Soul Shard sink are here for InventoryTuning, which
            // spans forge.json and this document — a grant has to know the stock's capacity to know
            // whether it stores the item or holds it. Read off InventoryDocuments' own constants
            // rather than re-transcribed, so the two fixtures cannot disagree about a price.
            ("crowns", Obj(
                ("inventoryExpansionLadder",
                    InventoryDocuments.Ladder([.. InventoryDocuments.ShippedLadder])),
                ("inventoryExpansionMaxPurchases",
                    ContentValue.Number(InventoryDocuments.ShippedMaxPurchases)),
                ("inventoryExpansionSlotsPerPurchase",
                    ContentValue.Number(InventoryDocuments.ShippedSlotsPerPurchase)))),
            ("soulShards", Obj(
                ("sources", Obj(
                    ("BOSS_KILL_NORMAL_PER_CHAPTER", bossKillNormalPerChapter ?? ContentValue.Number(ShippedBossKillNormalPerChapter)),
                    ("BOSS_KILL_HEROIC_PER_CHAPTER", bossKillHeroicPerChapter ?? ContentValue.Number(ShippedBossKillHeroicPerChapter)),
                    ("BOSS_KILL_MYTHIC_PER_CHAPTER", bossKillMythicPerChapter ?? ContentValue.Number(ShippedBossKillMythicPerChapter)),
                    ("FIRST_CLEAR_MIN", firstClearMin ?? ContentValue.Number(ShippedFirstClearMin)),
                    ("FIRST_CLEAR_MAX", firstClearMax ?? ContentValue.Number(ShippedFirstClearMax)))),
                ("sinks", Obj(
                    ("INVENTORY_EXPANSION_FLAT",
                        ContentValue.Number(InventoryDocuments.ShippedFlatSoulShardPrice)))))));

    private static ContentValue ShrineBuff((string Id, string? Stat, decimal? Magnitude, decimal? Heal) buff)
    {
        var members = new List<(string, ContentValue)>
        {
            ("id", ContentValue.Text(buff.Id)),
            ("displayName", ContentValue.Text("loc.shrine." + buff.Id.ToLowerInvariant() + ".name")),

            // 🔒 An authored NULL, not an omitted member — SHR_HEAL carries "stat": null explicitly,
            // and game-data/README.md's whole point is that the two are different facts.
            ("stat", buff.Stat is null ? ContentValue.Unauthorised : ContentValue.Text(buff.Stat)),
            ("magnitude", buff.Magnitude is null
                ? ContentValue.Unauthorised
                : ContentValue.Number(buff.Magnitude.Value)),
        };

        // ⚠️ …whereas immediateHealPctMaxHp is genuinely ABSENT on the eight rows that do not heal,
        // which is the other of the two shapes ShrineTuning.OptionalNumber has to answer null for.
        if (buff.Heal is { } heal)
        {
            members.Add(("immediateHealPctMaxHp", ContentValue.Number(heal)));
        }

        return Obj(members.ToArray());
    }

    private static ContentValue Curses(ContentValue? curses) =>
        Obj(("curses", curses ?? ContentValue.Array(ShippedCurses.Select(c => Obj(
            ("id", ContentValue.Text(c.Id)),
            ("displayName", ContentValue.Text("loc.curse." + c.Id.ToLowerInvariant() + ".name")),
            ("effect", ContentValue.Text("(effect prose — nothing in Core reads it; M3-11's)")),
            ("reward", ContentValue.Text(c.Reward)),
            ("availableFromChapter", ContentValue.Number(c.AvailableFrom)))))));

    private static ContentValue BoardEvents(ContentValue? cards) =>
        Obj(("cards", cards ?? ContentValue.Array(FixtureCards.All)));

    internal static ContentValue Obj(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(m => new KeyValuePair<string, ContentValue>(m.Name, m.Value)));
}
