using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// `03` §7 — the in-run shop tile's pricing numbers, read out of
/// <c>tuning/currencies.json#/shopTile</c>.
/// </summary>
/// <remarks>
/// <para>
/// `21` §3.1: <em>"A 📐 TUNABLE number that is not in this directory is a bug."</em> Every number
/// <see cref="Rules.Economy.ShopPricing"/> uses is authored in
/// <c>game-data/tuning/currencies.json#/shopTile</c> and reaches the rules through this type, the
/// same shape <see cref="EnergyTuning"/> already uses for `10` §3.
/// </para>
/// <para>
/// ⚠️ <b>What this deliberately does not carry.</b> The rarity-weighted <em>draw</em> for the
/// Perk slot needs the 98-perk catalogue's rarity distribution, which is M3-06/M3-07's — nothing
/// in <c>tuning/currencies.json</c> authors it, so nothing here invents one (steering S6). This
/// type prices a perk <em>by rarity</em> (`03` §7's <c>BasePrice(itemType, rarity)</c>), which is
/// all the pricing formula itself needs.
/// </para>
/// </remarks>
internal sealed class ShopTuning
{
    /// <summary>The document `03` §7's shop block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string ShopPointer = DocumentPath + "#/shopTile";
    private const string BasePricePointer = ShopPointer + "/basePrice";

    /// <summary>`03` §7 — the shop's slot count. 4 as shipped.</summary>
    internal const string SlotsReference = ShopPointer + "/slots";

    /// <summary>`03` §7 — free refreshes per shop visit before the ad-gated ones. 1 as shipped.</summary>
    internal const string FreeRefreshesPerVisitReference = ShopPointer + "/freeRefreshesPerVisit";

    /// <summary>`03` §7 (ruled `16` A7) — the per-stage price step. 0.25 as shipped.</summary>
    internal const string StagePriceStepReference = ShopPointer + "/stagePriceStep";

    /// <summary>`03` §7 (ruled `16` A7) — the per-chapter price scalar array, one entry per chapter.</summary>
    internal const string ChapterPriceScalarReference = ShopPointer + "/chapterPriceScalar";

    /// <summary>`03` §7 — the Heal slot's healed share of Max HP. 0.35 as shipped.</summary>
    internal const string HealPctMaxHpReference = ShopPointer + "/healPctMaxHp";

    /// <summary>`03` §7 — a telemetry alarm threshold, not a rule input. 0.2 as shipped.</summary>
    internal const string LeftoverGoldAlarmShareReference = ShopPointer + "/leftoverGoldAlarmShare";

    /// <summary>`03` §7 — the Perk slot's base price table, keyed by <see cref="ShopRarity"/>.</summary>
    internal const string PerkBasePriceReference = BasePricePointer + "/PERK";

    /// <summary>`03` §7.1 — the Consumable slot's base price table, keyed by consumable id.</summary>
    internal const string ConsumableBasePriceReference = BasePricePointer + "/CONSUMABLE";

    /// <summary>`03` §7 — the Run Buff slot's base price table, keyed by run buff id.</summary>
    internal const string RunBuffBasePriceReference = BasePricePointer + "/RUN_BUFF";

    /// <summary>`03` §7 — the Heal slot's base price. 150 as shipped.</summary>
    internal const string HealBasePriceReference = BasePricePointer + "/HEAL";

    /// <summary>`03` §7 — the three authored run buffs (Whetstone, Heartroot Tonic, Hawk's Eye).</summary>
    internal const string RunBuffsReference = ShopPointer + "/runBuffs";

    private static readonly ShopRarity[] PerkRarities =
    {
        ShopRarity.COMMON, ShopRarity.RARE, ShopRarity.EPIC, ShopRarity.LEGENDARY,
    };

    private readonly IReadOnlyDictionary<ShopRarity, long> _perkBasePrice;
    private readonly IReadOnlyDictionary<string, long> _consumableBasePrice;
    private readonly IReadOnlyDictionary<string, long> _runBuffBasePrice;

    private ShopTuning(
        int slots,
        int freeRefreshesPerVisit,
        decimal stagePriceStep,
        IReadOnlyList<decimal> chapterPriceScalar,
        IReadOnlyDictionary<ShopRarity, long> perkBasePrice,
        IReadOnlyDictionary<string, long> consumableBasePrice,
        IReadOnlyDictionary<string, long> runBuffBasePrice,
        long healBasePrice,
        decimal healPctMaxHp,
        IReadOnlyList<ShopRunBuffDefinition> runBuffs,
        decimal leftoverGoldAlarmShare)
    {
        Slots = slots;
        FreeRefreshesPerVisit = freeRefreshesPerVisit;
        StagePriceStep = stagePriceStep;
        ChapterPriceScalar = chapterPriceScalar;
        _perkBasePrice = perkBasePrice;
        _consumableBasePrice = consumableBasePrice;
        _runBuffBasePrice = runBuffBasePrice;
        HealBasePrice = healBasePrice;
        HealPctMaxHp = healPctMaxHp;
        RunBuffs = runBuffs;
        LeftoverGoldAlarmShare = leftoverGoldAlarmShare;
    }

    /// <summary>`03` §7 — the shop's slot count.</summary>
    internal int Slots { get; }

    /// <summary>`03` §7 — free refreshes per visit before the ad-gated ones.</summary>
    internal int FreeRefreshesPerVisit { get; }

    /// <summary>`03` §7 (ruled `16` A7) — the per-stage price step (0.25 means Stage 3 is +50%).</summary>
    internal decimal StagePriceStep { get; }

    /// <summary>
    /// `03` §7 (ruled `16` A7) — <c>chapterPriceScalar[c-1]</c> for chapter <c>c</c>. One entry per
    /// authored chapter.
    /// </summary>
    internal IReadOnlyList<decimal> ChapterPriceScalar { get; }

    /// <summary>`03` §7 — the Heal slot's base price, before the stage/chapter scalars.</summary>
    internal long HealBasePrice { get; }

    /// <summary>`03` §7 — the Heal slot's healed share of Max HP.</summary>
    internal decimal HealPctMaxHp { get; }

    /// <summary>`03` §7 — the three authored run buffs, in the order the document lists them.</summary>
    internal IReadOnlyList<ShopRunBuffDefinition> RunBuffs { get; }

    /// <summary>`03` §7 — a telemetry alarm threshold. Not consumed by any rule.</summary>
    internal decimal LeftoverGoldAlarmShare { get; }

    /// <summary>The Perk slot's base price for one rarity, before the stage/chapter scalars.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rarity"/> is not one of the four.</exception>
    internal long PerkBasePrice(ShopRarity rarity)
    {
        if (_perkBasePrice.TryGetValue(rarity, out var price))
        {
            return price;
        }

        throw new ArgumentOutOfRangeException(
            nameof(rarity), rarity,
            "03 §7's PERK base price table authors exactly COMMON/RARE/EPIC/LEGENDARY and this " +
            "rarity is not one of them.");
    }

    /// <summary>The Consumable slot's base price for one consumable id, before the scalars.</summary>
    /// <exception cref="ArgumentException"><paramref name="consumableId"/> is not authored.</exception>
    internal long ConsumableBasePrice(string consumableId)
    {
        ArgumentNullException.ThrowIfNull(consumableId);

        if (_consumableBasePrice.TryGetValue(consumableId, out var price))
        {
            return price;
        }

        throw new ArgumentException(
            "03 §7.1's CONSUMABLE base price table has no entry for '" + consumableId + "'.",
            nameof(consumableId));
    }

    /// <summary>The Run Buff slot's base price for one run buff id, before the scalars.</summary>
    /// <exception cref="ArgumentException"><paramref name="runBuffId"/> is not authored.</exception>
    internal long RunBuffBasePrice(string runBuffId)
    {
        ArgumentNullException.ThrowIfNull(runBuffId);

        if (_runBuffBasePrice.TryGetValue(runBuffId, out var price))
        {
            return price;
        }

        throw new ArgumentException(
            "03 §7's RUN_BUFF base price table has no entry for '" + runBuffId + "'.",
            nameof(runBuffId));
    }

    /// <summary>
    /// Reads the shop block. Throws rather than defaulting on anything missing, unauthorised,
    /// mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static ShopTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var slots = content.ReadInt32(SlotsReference);
        if (slots < 1)
        {
            throw new InvalidTunableException(
                SlotsReference,
                "A shop with no slots offers nothing. 03 §7 authors 4; this document authors " +
                Render(slots) + ".");
        }

        var freeRefreshes = content.ReadInt32(FreeRefreshesPerVisitReference);
        if (freeRefreshes < 0)
        {
            throw new InvalidTunableException(
                FreeRefreshesPerVisitReference,
                "A refresh count is never negative. 03 §7 authors 1 free refresh per visit; this " +
                "document authors " + Render(freeRefreshes) + ".");
        }

        var stagePriceStep = content.ReadNumber(StagePriceStepReference);
        if (stagePriceStep < 0m)
        {
            throw new InvalidTunableException(
                StagePriceStepReference,
                "A negative stage price step would make a later stage CHEAPER than Stage 1, which " +
                "03 §7's formula does not describe. This document authors " + Render(stagePriceStep) + ".");
        }

        var chapterPriceScalar = ReadChapterPriceScalar(content);
        var (perkBasePrice, consumableBasePrice, runBuffBasePrice) = ReadBasePrices(content);
        var healBasePrice = content.ReadInt64(HealBasePriceReference);

        if (healBasePrice < 0)
        {
            throw new InvalidTunableException(
                HealBasePriceReference,
                "A price is never negative. This document authors " + Render(healBasePrice) + ".");
        }

        var healPctMaxHp = content.ReadNumber(HealPctMaxHpReference);
        if (healPctMaxHp is <= 0m or > 1m)
        {
            throw new InvalidTunableException(
                HealPctMaxHpReference,
                "A heal that restores nothing or more than a full bar is not a percentage of Max " +
                "HP. 03 §7 authors 0.35; this document authors " + Render(healPctMaxHp) + ".");
        }

        var runBuffs = ReadRunBuffs(content);
        var leftoverGoldAlarmShare = content.ReadNumber(LeftoverGoldAlarmShareReference);

        return new ShopTuning(
            slots,
            freeRefreshes,
            stagePriceStep,
            chapterPriceScalar,
            perkBasePrice,
            consumableBasePrice,
            runBuffBasePrice,
            healBasePrice,
            healPctMaxHp,
            runBuffs,
            leftoverGoldAlarmShare);
    }

    private static IReadOnlyList<decimal> ReadChapterPriceScalar(ContentSnapshot content)
    {
        var array = content.Read(ChapterPriceScalarReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                ChapterPriceScalarReference,
                "03 §7 authors chapterPriceScalar as a non-empty array, one entry per chapter. " +
                "This document authors " + array + ".");
        }

        var scalars = new decimal[array.Items.Count];
        for (var i = 0; i < array.Items.Count; i++)
        {
            var value = array.Items[i].AsNumber(ChapterPriceScalarReference + "/" + Render(i));
            if (value <= 0m)
            {
                throw new InvalidTunableException(
                    ChapterPriceScalarReference + "/" + Render(i),
                    "A chapter price scalar of zero or below would make every price in that " +
                    "chapter zero or negative. This document authors " + Render(value) + ".");
            }

            scalars[i] = value;
        }

        return Array.AsReadOnly(scalars);
    }

    private static (
        IReadOnlyDictionary<ShopRarity, long> Perk,
        IReadOnlyDictionary<string, long> Consumable,
        IReadOnlyDictionary<string, long> RunBuff) ReadBasePrices(ContentSnapshot content)
    {
        var perk = new Dictionary<ShopRarity, long>();
        foreach (var rarity in PerkRarities)
        {
            var reference = PerkBasePriceReference + "/" + rarity;
            var price = content.ReadInt64(reference);
            RequireNonNegativePrice(reference, price);
            perk[rarity] = price;
        }

        var consumable = ReadStringKeyedPrices(content, ConsumableBasePriceReference);
        var runBuff = ReadStringKeyedPrices(content, RunBuffBasePriceReference);

        return (perk, consumable, runBuff);
    }

    private static IReadOnlyDictionary<string, long> ReadStringKeyedPrices(
        ContentSnapshot content, string tablePointer)
    {
        var table = content.Read(tablePointer);
        if (table.Kind != ContentValueKind.Object || table.MemberNames.Count == 0)
        {
            throw new InvalidTunableException(
                tablePointer,
                "03 §7's base price tables are non-empty objects keyed by id. This document " +
                "authors " + table + ".");
        }

        var prices = new Dictionary<string, long>(table.MemberNames.Count, StringComparer.Ordinal);
        foreach (var id in table.MemberNames)
        {
            var reference = tablePointer + "/" + id;
            var price = content.ReadInt64(reference);
            RequireNonNegativePrice(reference, price);
            prices[id] = price;
        }

        return prices;
    }

    private static void RequireNonNegativePrice(string reference, long price)
    {
        if (price < 0)
        {
            throw new InvalidTunableException(
                reference, "A price is never negative. This document authors " + Render(price) + ".");
        }
    }

    private static IReadOnlyList<ShopRunBuffDefinition> ReadRunBuffs(ContentSnapshot content)
    {
        var array = content.Read(RunBuffsReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                RunBuffsReference,
                "03 §7 authors three run buffs. This document authors " + array + ".");
        }

        var buffs = new ShopRunBuffDefinition[array.Items.Count];
        for (var i = 0; i < array.Items.Count; i++)
        {
            var entry = array.Items[i];
            var pointer = RunBuffsReference + "/" + Render(i);

            var id = Member(entry, "id", pointer).AsText(pointer + "/id");
            var displayName = Member(entry, "displayName", pointer).AsText(pointer + "/displayName");
            var stat = Member(entry, "stat", pointer).AsText(pointer + "/stat");
            var baseValue = Member(entry, "base", pointer).AsNumber(pointer + "/base");
            var chapterGrowth = Member(entry, "chapterGrowth", pointer).AsNumber(pointer + "/chapterGrowth");

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidTunableException(pointer + "/id", "A run buff id must not be blank.");
            }

            buffs[i] = new ShopRunBuffDefinition(id, displayName, stat, baseValue, chapterGrowth);
        }

        return Array.AsReadOnly(buffs);
    }

    private static ContentValue Member(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null
            ? value
            : throw new MissingContentException(pointer + "/" + name, "'" + name + "' resolves to nothing");

    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Render(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Render(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// `03` §7's four Perk rarities, for shop <b>pricing</b> only.
/// </summary>
/// <remarks>
/// ⚠️ Not the perk catalogue's own rarity type — that is M3-06/M3-07's, and does not exist yet
/// (steering S6). This enum carries no more than `03` §7's pricing table needs: which of four
/// buckets a priced perk falls in. When the real catalogue lands, aligning the two — or replacing
/// this one — is that milestone's call, not a decision frozen here.
/// </remarks>
internal enum ShopRarity
{
    /// <summary>`03` §7 — the cheapest tier.</summary>
    COMMON,

    /// <summary>`03` §7.</summary>
    RARE,

    /// <summary>`03` §7.</summary>
    EPIC,

    /// <summary>`03` §7 — the most expensive tier.</summary>
    LEGENDARY,
}

/// <summary>One authored run buff (`03` §7): a flat stat bonus for the rest of the run.</summary>
/// <param name="Id">The shop base-price table's key, e.g. <c>WHETSTONE</c>.</param>
/// <param name="DisplayName">The localisation key.</param>
/// <param name="Stat">The stat it raises, e.g. <c>ATK</c>, <c>MAX_HP</c>, <c>CRIT</c>.</param>
/// <param name="Base">The chapter-1 magnitude.</param>
/// <param name="ChapterGrowth">
/// The per-chapter growth base: the buff's chapter-<c>c</c> magnitude is
/// <c>Base * ChapterGrowth^(c-1)</c>. <c>1.0</c> means the magnitude is chapter-invariant.
/// </param>
internal readonly record struct ShopRunBuffDefinition(
    string Id, string DisplayName, string Stat, decimal Base, decimal ChapterGrowth);
