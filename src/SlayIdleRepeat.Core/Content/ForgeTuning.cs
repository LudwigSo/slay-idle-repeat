using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The three free Forge operations' numbers: merge, enhancement and salvage, read out of
/// <c>tuning/forge.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three of the five blocks, deliberately.</b> <c>reforge</c> and <c>retune</c> are the quality
/// and affix re-rolls, whose rules are authored in the luck document rather than here; they are
/// unread until the task that wires them arrives, and reading them early would put a second,
/// unexercised statement of their costs in the build. The <c>inventory</c> block belongs to
/// <see cref="InventoryTuning"/>, which owns the capacity ladder.
/// </para>
/// <para>
/// 🔒 <b>The per-level success ladder is read, and the bands are not.</b> The document authors both:
/// five endpoints as bands and fifteen values as a ladder. Reading the bands here as well would be a
/// second answer to "what is the chance at +12", and the two would eventually disagree; the
/// cross-document validator is where the ladder is checked against the endpoints it was expanded
/// from, once, for the whole content set.
/// </para>
/// <para>
/// 🔒 <b>The dust price at the top band is an authored absence, not a missing number.</b> Nothing
/// merges out of the top rung, so the document holds a deliberate <c>null</c> there and
/// <see cref="MergeDustSubstituteCost"/> refuses it rather than answering a price nobody set. A
/// default here would be a number invented for an operation that cannot happen.
/// </para>
/// </remarks>
internal sealed class ForgeTuning
{
    /// <summary>The document the three blocks live in.</summary>
    internal const string DocumentPath = "tuning/forge.json";

    private const string MergePointer = DocumentPath + "#/merge";

    private const string EnhancePointer = DocumentPath + "#/enhance";

    private const string SalvagePointer = DocumentPath + "#/salvage";

    /// <summary>How many inputs one fusion consumes. 3 as shipped.</summary>
    internal const string InputCountReference = MergePointer + "/inputCount";

    /// <summary>How many of those slots Merge Dust may fill. 1 as shipped.</summary>
    internal const string DustSubstituteMaxInputsReference = MergePointer + "/dustSubstituteMaxInputs";

    /// <summary>The dust price of a substituted slot, keyed on the INPUT band.</summary>
    internal const string DustSubstituteCostReference = MergePointer + "/dustSubstituteCost";

    /// <summary>The Crown price of a fusion, keyed on the OUTPUT band — so there is deliberately no bottom-band row.</summary>
    internal const string CrownCostReference = MergePointer + "/crownCostByOutputRarity";

    /// <summary>The enhancement level a fresh item sits at.</summary>
    internal const string MinLevelReference = EnhancePointer + "/minLevel";

    /// <summary>The enhancement level nothing goes past.</summary>
    internal const string MaxLevelReference = EnhancePointer + "/maxLevel";

    /// <summary>The additive share of base stats each level adds.</summary>
    internal const string StatBonusPerLevelReference = EnhancePointer + "/statBonusPerLevel";

    /// <summary>The stone price of each attempt, one per level from the first.</summary>
    internal const string StoneCostReference = EnhancePointer + "/stoneCostPerLevel";

    /// <summary>The unmodified success chance of each attempt, one per level from the first.</summary>
    internal const string SuccessRateReference = EnhancePointer + "/perLevelSuccessRate";

    /// <summary>The dust an unenhanced item of each band breaks down into.</summary>
    internal const string BaseDustReference = SalvagePointer + "/baseDustByRarity";

    /// <summary>The share of the base dust each enhancement level adds back.</summary>
    internal const string DustPerEnhanceLevelReference = SalvagePointer + "/dustPerEnhanceLevel";

    /// <summary>The share of the stones spent on an item that salvage returns.</summary>
    internal const string StoneRefundShareReference = SalvagePointer + "/stoneRefundShare";

    private readonly IReadOnlyDictionary<Rarity, long?> _dustSubstituteCost;

    private readonly IReadOnlyDictionary<Rarity, long?> _crownCost;

    private readonly IReadOnlyDictionary<Rarity, long?> _baseDust;

    private readonly IReadOnlyList<long> _stoneCost;

    private readonly IReadOnlyList<double> _successRate;

    private ForgeTuning(
        int inputCount,
        int dustSubstituteMaxInputs,
        IReadOnlyDictionary<Rarity, long?> dustSubstituteCost,
        IReadOnlyDictionary<Rarity, long?> crownCost,
        int minLevel,
        int maxLevel,
        double statBonusPerLevel,
        IReadOnlyList<long> stoneCost,
        IReadOnlyList<double> successRate,
        IReadOnlyDictionary<Rarity, long?> baseDust,
        double dustPerEnhanceLevel,
        double stoneRefundShare)
    {
        MergeInputCount = inputCount;
        MergeDustSubstituteMaxInputs = dustSubstituteMaxInputs;
        _dustSubstituteCost = dustSubstituteCost;
        _crownCost = crownCost;
        MinEnhanceLevel = minLevel;
        MaxEnhanceLevel = maxLevel;
        StatBonusPerLevel = statBonusPerLevel;
        _stoneCost = stoneCost;
        _successRate = successRate;
        _baseDust = baseDust;
        DustPerEnhanceLevel = dustPerEnhanceLevel;
        StoneRefundShare = stoneRefundShare;
    }

    /// <summary>How many inputs one fusion consumes, dust-filled slots included.</summary>
    internal int MergeInputCount { get; }

    /// <summary>How many of a fusion's slots Merge Dust may fill.</summary>
    internal int MergeDustSubstituteMaxInputs { get; }

    /// <summary>The enhancement level a fresh item sits at.</summary>
    internal int MinEnhanceLevel { get; }

    /// <summary>The enhancement level nothing goes past.</summary>
    internal int MaxEnhanceLevel { get; }

    /// <summary>The additive share of an item's base stats one enhancement level adds.</summary>
    internal double StatBonusPerLevel { get; }

    /// <summary>The share of the base dust each enhancement level on the item adds back.</summary>
    internal double DustPerEnhanceLevel { get; }

    /// <summary>The share of the stones spent on an item that salvage returns.</summary>
    internal double StoneRefundShare { get; }

    /// <summary>The Crown price of a fusion that lands on this band.</summary>
    /// <param name="outputRarity">The band the fusion produces.</param>
    /// <returns>The price.</returns>
    /// <exception cref="InvalidTunableException">The document prices no fusion onto that band.</exception>
    internal long MergeCrownCost(Rarity outputRarity) =>
        Priced(_crownCost, outputRarity, CrownCostReference, "no fusion lands on it");

    /// <summary>The Merge Dust price of substituting one input slot at this band.</summary>
    /// <param name="inputRarity">The band of the items being fused.</param>
    /// <returns>The price.</returns>
    /// <exception cref="InvalidTunableException">The document prices no substitution at that band.</exception>
    /// <exception cref="UnauthorisedTunableException">
    /// The band holds a deliberate <c>null</c>. Nothing merges out of the top rung, so there is no
    /// substitution to price and no number to answer with.
    /// </exception>
    internal long MergeDustSubstituteCost(Rarity inputRarity) =>
        Priced(_dustSubstituteCost, inputRarity, DustSubstituteCostReference, "nothing merges out of it");

    /// <summary>The dust an unenhanced item of this band breaks down into.</summary>
    /// <param name="rarity">The item's band.</param>
    /// <returns>The base dust.</returns>
    /// <exception cref="InvalidTunableException">The document values no item of that band.</exception>
    /// <exception cref="UnauthorisedTunableException">The band holds a deliberate <c>null</c>.</exception>
    internal long SalvageBaseDust(Rarity rarity) =>
        Priced(_baseDust, rarity, BaseDustReference, "it is not valued for salvage");

    /// <summary>The stones one attempt at this level costs.</summary>
    /// <param name="targetLevel">The level the attempt is reaching for, from <see cref="MinEnhanceLevel"/> + 1.</param>
    /// <returns>The cost.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No attempt reaches that level.</exception>
    internal long EnhanceStoneCost(int targetLevel) => _stoneCost[LadderIndex(targetLevel)];

    /// <summary>The unmodified success chance of one attempt at this level, before any mercy.</summary>
    /// <param name="targetLevel">The level the attempt is reaching for, from <see cref="MinEnhanceLevel"/> + 1.</param>
    /// <returns>The chance, between 0 and 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No attempt reaches that level.</exception>
    internal double EnhanceSuccessRate(int targetLevel) => _successRate[LadderIndex(targetLevel)];

    /// <summary>The stones already spent getting an item to a level, which salvage refunds a share of.</summary>
    /// <param name="enhanceLevel">The level it stands at.</param>
    /// <returns>The total spent, counting only successful attempts.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The level is below the floor or above the ceiling.</exception>
    /// <remarks>
    /// 🔴 <b>Successful attempts only, and that is the honest reading rather than a convenience.</b>
    /// Failures consume stones too, but nothing on the item records how many it ate — the mercy
    /// counter is reset by every success, so it counts the current streak and not the item's history.
    /// Refunding a share of what the item's <em>level</em> cost is therefore the only figure the
    /// stored state can support; refunding a share of what the player actually spent would need a
    /// lifetime-spend field nobody has authored.
    /// </remarks>
    internal long EnhanceStonesInvested(int enhanceLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(enhanceLevel, MinEnhanceLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(enhanceLevel, MaxEnhanceLevel);

        var total = 0L;

        for (var level = MinEnhanceLevel + 1; level <= enhanceLevel; level++)
        {
            total += EnhanceStoneCost(level);
        }

        return total;
    }

    /// <summary>Reads the merge, enhance and salvage blocks. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The three blocks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer this reader needs holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static ForgeTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var inputCount = content.ReadInt32(InputCountReference);
        if (inputCount < 2)
        {
            throw new InvalidTunableException(
                InputCountReference,
                "A fusion of fewer than two items is a rename, not a fusion. 08 §4.1 authors 3; this " +
                "document authors " + Text(inputCount) + ".");
        }

        var dustSlots = content.ReadInt32(DustSubstituteMaxInputsReference);
        if (dustSlots < 0 || dustSlots >= inputCount)
        {
            throw new InvalidTunableException(
                DustSubstituteMaxInputsReference,
                "Dust may fill " + Text(dustSlots) + " of " + Text(inputCount) + " slots. Filling " +
                "every slot would mint an item out of dust alone, with no input to take a quality or " +
                "a chapter of origin from — 08 §4.1 substitutes ONE.");
        }

        var minLevel = content.ReadInt32(MinLevelReference);
        var maxLevel = content.ReadInt32(MaxLevelReference);
        if (maxLevel <= minLevel)
        {
            throw new InvalidTunableException(
                MaxLevelReference,
                "The ceiling is " + Text(maxLevel) + " and the floor " + Text(minLevel) + ", so " +
                "nothing can be enhanced at all. 08 §4.2 authors +0 to +15.");
        }

        var levels = maxLevel - minLevel;

        return new ForgeTuning(
            inputCount,
            dustSlots,
            ReadPrices(content, DustSubstituteCostReference),
            ReadPrices(content, CrownCostReference),
            minLevel,
            maxLevel,
            RequireUnitShare(content, StatBonusPerLevelReference),
            ReadStoneCosts(content, levels),
            ReadSuccessRates(content, levels),
            ReadPrices(content, BaseDustReference),
            RequireNonNegative(content, DustPerEnhanceLevelReference),
            RequireUnitShare(content, StoneRefundShareReference));
    }

    /// <summary>
    /// One band-keyed price map. An absent band is absent; a band holding an authored <c>null</c> is
    /// recorded as an authored absence rather than dropped, so the two can be told apart when they
    /// are asked for.
    /// </summary>
    private static IReadOnlyDictionary<Rarity, long?> ReadPrices(ContentSnapshot content, string reference)
    {
        var map = content.Read(reference);

        if (map.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(reference);
        }

        if (map.Kind != ContentValueKind.Object)
        {
            throw new InvalidTunableException(
                reference, "08 §4 authors this as a band-keyed price map. This document authors " + map + ".");
        }

        var prices = new Dictionary<Rarity, long?>(RarityLadder.Count);

        foreach (var rarity in RarityLadder)
        {
            var name = rarity.ToString();

            if (!map.TryGetMember(name, out var value) || value is null)
            {
                continue;
            }

            var pointer = reference + "/" + name;

            if (value.IsUnauthorised)
            {
                prices[rarity] = null;
                continue;
            }

            var price = value.AsInt64(pointer);

            if (price < 0)
            {
                throw new InvalidTunableException(
                    pointer, "A price of " + Text(price) + " would pay the player to perform the operation.");
            }

            prices[rarity] = price;
        }

        if (prices.Count == 0)
        {
            throw new InvalidTunableException(
                reference,
                "This map prices no band at all, so every operation it governs is unreachable. 08 §4 " +
                "authors a row per band the operation is legal at.");
        }

        return prices;
    }

    private static IReadOnlyList<long> ReadStoneCosts(ContentSnapshot content, int levels)
    {
        var array = RequireLadder(content, StoneCostReference, levels);
        var costs = new long[levels];

        for (var i = 0; i < levels; i++)
        {
            var pointer = StoneCostReference + "/" + Text(i);
            costs[i] = array.Items[i].AsInt64(pointer);

            if (costs[i] <= 0)
            {
                throw new InvalidTunableException(
                    pointer, "An attempt that costs nothing is an attempt with no sink behind it.");
            }
        }

        return Array.AsReadOnly(costs);
    }

    /// <summary>
    /// The per-level success ladder. Refused when it is an authored <c>null</c>, which is what the
    /// document held while the interpolation between the band endpoints was unstated: a reader that
    /// filled that hole would be choosing the shape of the ramp, which is the one thing the
    /// <c>null</c> existed to stop.
    /// </summary>
    private static IReadOnlyList<double> ReadSuccessRates(ContentSnapshot content, int levels)
    {
        var array = RequireLadder(content, SuccessRateReference, levels);
        var rates = new double[levels];

        for (var i = 0; i < levels; i++)
        {
            var pointer = SuccessRateReference + "/" + Text(i);
            rates[i] = array.Items[i].AsDouble(pointer);

            if (!double.IsFinite(rates[i]) || rates[i] is <= 0.0 or > 1.0)
            {
                throw new InvalidTunableException(
                    pointer,
                    "A success chance is a probability in (0,1]. A zero would make the level " +
                    "unreachable however many attempts a player paid for; this document authors " +
                    Text(rates[i]) + ".");
            }
        }

        return Array.AsReadOnly(rates);
    }

    private static ContentValue RequireLadder(ContentSnapshot content, string reference, int levels)
    {
        var array = content.Read(reference);

        // The authored null, told apart from a ladder of the wrong shape: one is a decision nobody
        // has taken and the other is a document that is wrong. Collapsing them would report an
        // undecided number as a malformed one, and the reader would be the place a design decision
        // went to be misfiled.
        if (array.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(reference);
        }

        if (array.Kind != ContentValueKind.Array || array.Items.Count != levels)
        {
            throw new InvalidTunableException(
                reference,
                "08 §4.2 authors one entry per enhancement level, which is " + Text(levels) + " of " +
                "them. This document authors " + array + ".");
        }

        return array;
    }

    private static double RequireUnitShare(ContentSnapshot content, string reference)
    {
        var share = content.ReadDouble(reference);

        return double.IsFinite(share) && share is > 0.0 and <= 1.0
            ? share
            : throw new InvalidTunableException(
                reference, "This is a share in (0,1]. This document authors " + Text(share) + ".");
    }

    private static double RequireNonNegative(ContentSnapshot content, string reference)
    {
        var value = content.ReadDouble(reference);

        return double.IsFinite(value) && value >= 0.0
            ? value
            : throw new InvalidTunableException(
                reference, "This is a non-negative share. This document authors " + Text(value) + ".");
    }

    /// <summary>
    /// The price for a band, telling an authored absence apart from a band the map never mentions.
    /// Neither is coerced to a number.
    /// </summary>
    private static long Priced(
        IReadOnlyDictionary<Rarity, long?> prices, Rarity rarity, string reference, string because)
    {
        if (!prices.TryGetValue(rarity, out var price))
        {
            throw new InvalidTunableException(
                reference,
                "'" + rarity + "' carries no row, because " + because + ". Answering a price here " +
                "would let an operation the design does not have happen at a number nobody chose.");
        }

        // The authored null, refused in the vocabulary the content reader already has for one: the
        // number is absent on purpose, and a default here would be a price invented for an operation
        // that cannot happen.
        return price ?? throw new UnauthorisedTunableException(reference + "/" + rarity);
    }

    private int LadderIndex(int targetLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLevel, MinEnhanceLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetLevel, MaxEnhanceLevel);

        return targetLevel - MinEnhanceLevel - 1;
    }

    /// <summary>The bands, in ladder order, so a price map is read in a fixed order rather than the document's.</summary>
    private static IReadOnlyList<Rarity> RarityLadder { get; } =
        Array.AsReadOnly(new[] { Rarity.C, Rarity.B, Rarity.A, Rarity.S, Rarity.SS });

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
