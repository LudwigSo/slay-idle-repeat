using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic fixtures for the four documents M3-03's tile resolvers read:
/// <c>tuning/currencies.json</c>'s <c>chapterScalars</c> and <c>inRunIncome</c> blocks,
/// <c>content/curses/curses.json</c>, and <c>content/board_events/board_events.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <c>Core.Tests</c> is hermetic, so this <em>mirrors</em> the shipped files rather than reading
/// them — the same shape <see cref="CurrenciesDocuments"/> already uses, and for its reason. The
/// shipped constants below are transcribed as of M3-03's read of the files.
/// </para>
/// <para>
/// 🔒 <b>The mirror is not the whole guarantee, and the other half is deliberate.</b> A fixture that
/// mirrors can drift from what ships, so each reader also has one test that loads the <b>real</b>
/// <c>game-data</c> tree through <c>GameDataLoader</c> and pins the reader against it — the seam
/// <c>GameDataLoaderTests</c> already established in this project. Neither is sufficient alone: the
/// hermetic tests prove the rules are right about the numbers, the real-data tests prove those are
/// the numbers we ship.
/// </para>
/// </remarks>
internal static class InRunIncomeDocuments
{
    /// <summary>The document `03` §7a's scalars and in-run income blocks live in.</summary>
    internal const string CurrenciesPath = "tuning/currencies.json";

    /// <summary>The document `19` Part E is transcribed into.</summary>
    internal const string CursesPath = "content/curses/curses.json";

    /// <summary>The document `19` Part A is transcribed into.</summary>
    internal const string BoardEventsPath = "content/board_events/board_events.json";

    /// <summary>`03` §7a — <c>G(c) = goldGrowth^(c-1)</c>'s base, as shipped.</summary>
    internal const decimal ShippedGoldGrowth = 1.55m;

    /// <summary>`03` §7a — <c>M(c) = metaGrowth^(c-1)</c>'s base, as shipped.</summary>
    internal const decimal ShippedMetaGrowth = 1.35m;

    /// <summary>`03` §7a — the only rounding mode authored, and the only one implemented.</summary>
    internal const string ShippedRoundingMode = "NEAREST_INTEGER";

    /// <summary>`03` §7a.4 — the chapter-1 cache payout, as shipped.</summary>
    internal const long ShippedBeastFeedBase = 25;

    /// <summary>`03` §7a.4 — the flat Pet Egg rate, as shipped.</summary>
    internal const decimal ShippedEggChance = 0.06m;

    /// <summary>`03` §7a.5 — how many distinct options a shrine offers, as shipped.</summary>
    internal const int ShippedOptionsOffered = 2;

    /// <summary>`03` §2 — the campfire rest's healed share of Max HP, as shipped.</summary>
    internal const decimal ShippedCampfireHeal = 0.4m;

    /// <summary>🔒 M3-05, `03` §1.1 — the Stage Gate's healed share of Max HP, as shipped.</summary>
    internal const decimal ShippedStageGateHeal = 0.15m;

    /// <summary>`03` §7a.3's three profiles, in the document's own order.</summary>
    internal static readonly (string Id, decimal Weight, long Crowns, long Stones, long Dust)[]
        ShippedTreasureProfiles =
        {
            ("COIN_HOARD", 55m, 40, 2, 0),
            ("STONE_CACHE", 30m, 18, 6, 0),
            ("DUST_TROVE", 15m, 15, 0, 10),
        };

    /// <summary>`03` §7a.5's pool of ten, in the document's own order.</summary>
    /// <remarks>
    /// ⚠️ <c>SHR_HP</c> (index 1) carries BOTH a stat raise and an immediate heal, and
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
            ("SHR_DR", "DR", 0.06m, null),
            ("SHR_THORN", "THORNS", 0.25m, null),
            ("SHR_GOLD", "GOLD_GAIN", 0.15m, null),
            ("SHR_HEAL", null, null, 0.4m),
        };

    /// <summary>
    /// `19` Part E's twelve rows, in the document's own order — id, reward prose and chapter gate.
    /// </summary>
    /// <remarks>
    /// The <c>effect</c> column is filled with a placeholder rather than transcribed: nothing in
    /// <c>Core</c> reads it (M3-11's engine will), so transcribing it here would imply something does.
    /// The <c>reward</c> column IS transcribed, because <c>CurseRewards</c>' four-row table is
    /// cross-checked against it.
    /// </remarks>
    internal static readonly (string Id, string Reward, int AvailableFrom)[] ShippedCurses =
    {
        ("CUR_SLIPPERY", "+250 Gold", 1),
        ("CUR_MARKED", "+2 Enhance Stones", 1),
        ("CUR_DIZZY", "+180 Gold", 1),
        ("CUR_FRACTURED", "+500 Gold", 1),
        ("CUR_HUNTED", "+1 gear drop per Elite", 5),
        ("CUR_UNTIMELY", "+300 Crowns", 3),
        ("CUR_FAMISHED", "+12% ATK", 3),
        ("CUR_BRITTLE_BONES", "+8% Crit Chance", 3),
        ("CUR_MISERLY", "+600 Gold", 3),
        ("CUR_BLIND", "+2 Reroll Charges", 3),
        ("CUR_LEADFOOT", "+15% Gold", 3),
        ("CUR_TITHE", "+20% gear drop chance", 3),
    };

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
    /// ⚠️ Exists so the campfire's rest can be pinned as genuinely READ rather than hardcoded — the
    /// same reason <paramref name="metaGrowth"/> exists. The heal was a C# constant until M3-03's
    /// review moved it into <c>#/inRunIncome/campfire</c> (`21` §3.1), and a test that only ever saw
    /// the shipped 0.4 could not tell the two apart.
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
        ContentValue? curses = null,
        ContentValue? cards = null) =>
        new(
            ContentVersion.FromHex(new string('a', ContentVersion.HexLength)),
            [
                // 🔒 tuning/progression.json is here because GameRules.Apply CLONES the slice through
                // Player.Rehydrate before a handler ever runs, and that reads 10 §6's Legend Level
                // range. A context missing it fails inside Apply's clone rather than in the rule
                // under test — which is a confusing failure for every test in this suite, none of
                // which is about Legend Level.
                ProgressionDocuments.Shipped.GetDocument(ProgressionDocuments.DocumentPath),
                new ContentDocument(CurrenciesPath, Currencies(
                    eggChance, metaGrowth, goldGrowth, roundingMode, treasureProfiles, shrineBuffs,
                    optionsOffered, campfireHeal, stageGateHeal)),
                new ContentDocument(CursesPath, Curses(curses)),
                new ContentDocument(BoardEventsPath, BoardEvents(cards)),
            ]);

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
        ContentValue? stageGateHeal) =>
        Obj(
            ("chapterScalars", Obj(
                ("goldGrowth", goldGrowth ?? ContentValue.Number(ShippedGoldGrowth)),
                ("metaGrowth", metaGrowth ?? ContentValue.Number(ShippedMetaGrowth)),
                ("roundingMode", roundingMode ?? ContentValue.Text(ShippedRoundingMode)))),
            ("inRunIncome", Obj(
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
                        ShippedShrineBuffs.Select(ShrineBuff))))))));

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
