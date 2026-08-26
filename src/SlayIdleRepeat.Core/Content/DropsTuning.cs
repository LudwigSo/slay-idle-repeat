using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>What one rarity band does to an item: its stat multiplier and its affix count.</summary>
/// <param name="StatMultiplier">Its multiplier on item power. Positive.</param>
/// <param name="AffixCount">How many affixes an item of this band rolls. The bottom band rolls none.</param>
/// <remarks>
/// The band itself is not a member: the row is reached <em>by</em> its band, and carrying the key
/// inside the value is a second answer to which band this is.
/// </remarks>
internal readonly record struct RarityBandRow(double StatMultiplier, int AffixCount);

/// <summary>
/// One slot's two stats and the coefficients that scale them — or the marker that a stat is a
/// percentage and is read from the per-rarity table instead.
/// </summary>
/// <param name="Slot">The slot.</param>
/// <param name="PrimaryStat">The authored stat token for the primary stat, e.g. <c>ATK</c>.</param>
/// <param name="PrimaryCoefficient">
/// Its coefficient on item power, or <see langword="null"/> where the stat is a percentage.
/// </param>
/// <param name="SecondaryStat">The authored stat token for the secondary stat.</param>
/// <param name="SecondaryCoefficient">
/// Its coefficient on item power, or <see langword="null"/> where the stat is a percentage.
/// </param>
/// <remarks>
/// A <see langword="null"/> coefficient is not a missing value: it is the authored statement that the
/// stat is a percentage, scaling with rarity alone and never with chapter, because it feeds a capped
/// percentage that must not inflate as chapters climb.
/// </remarks>
internal readonly record struct SlotCoefficients(
    GearSlot Slot,
    string PrimaryStat,
    double? PrimaryCoefficient,
    string SecondaryStat,
    double? SecondaryCoefficient);

/// <summary>One affix in the pool: its id, what it writes, its authored range, and the slots it may appear on.</summary>
/// <param name="AffixId">The authored id, e.g. <c>AFX_CRIT_CHANCE</c>.</param>
/// <param name="Stat">
/// The stat a roll of this affix writes, or <see langword="null"/> where the pool authors an affix the
/// stat block has no slot for.
/// </param>
/// <param name="Op">
/// How it writes it — the additive-flat or additive-percent bucket. <see langword="null"/> exactly
/// where <paramref name="Stat"/> is.
/// </param>
/// <param name="Minimum">The bottom of its authored range, inclusive.</param>
/// <param name="Maximum">The top of its authored range, inclusive.</param>
/// <param name="Slots">The slots it may be rolled on. Never empty.</param>
/// <param name="Condition">
/// The context gate a roll of this affix carries onto its synthesised effect, or
/// <see langword="null"/> for an ungated affix. Appended last so the six positional arguments
/// every existing caller passes keep their meaning.
/// </param>
/// <remarks>
/// <para>
/// The rarity floor two of the fourteen carry is deliberately <em>not</em> a member. It is an
/// eligibility rule rather than a property of the affix, and the tuning reader applies it when it
/// answers which affixes a given item may draw — so the roller never has to remember to.
/// </para>
/// <para>
/// 🔒 <b>The op is authored per affix and is not uniform</b>, because aggregation multiplies the
/// running value by <c>1 + Σ percent</c>: on a stat whose base is zero — lifesteal, block,
/// penetration, damage reduction — a percent add is arithmetically inert. So a <c>+X%</c> affix on a
/// stat that <em>is</em> a fraction contributes X percentage points, and only a stat whose base is a
/// magnitude the percentage is taken of takes the percent bucket. Authored rather than derived here:
/// a table in code would be a second, frozen answer no architecture rule could see.
/// </para>
/// </remarks>
internal readonly record struct GearAffixDefinition(
    string AffixId,
    StatId? Stat,
    EffectOp? Op,
    double Minimum,
    double Maximum,
    IReadOnlyList<GearSlot> Slots,
    EffectCondition? Condition = null)
{
    /// <summary>Whether a roll of this affix contributes anything a stat block can hold.</summary>
    /// <remarks>
    /// Both halves or neither: an affix naming a stat with no op could not be applied, and one naming
    /// an op with no stat could not be aimed. The reader refuses either half alone, so this is a
    /// question about the authored pool rather than a guard against a half-read row.
    /// </remarks>
    internal bool WritesAStat => Stat is not null && Op is not null;
}

/// <summary>
/// The gear generation tables, read out of <c>tuning/drops.json</c>: the rarity ladder, the
/// chapter-banded drop shares, the item-power coefficient and quality scales, the slot coefficients
/// and their percent-stat table, the fourteen affixes, and the set breakpoints.
/// </summary>
/// <remarks>
/// <para>
/// Everything a rolled item's numbers come from is here, and none of it is restated in code. The
/// design set marks the whole document tunable and expects the flat coefficients in particular to
/// move, so a constant anywhere in <c>Rules/Gear/</c> would be a second, frozen answer.
/// </para>
/// <para>
/// Not cached. Every reader re-reads the snapshot it was handed, so a content version swap cannot
/// leave a stale table behind a static field.
/// </para>
/// </remarks>
internal sealed class DropsTuning
{
    /// <summary>The document the gear tables live in.</summary>
    internal const string DocumentPath = "tuning/drops.json";

    /// <summary>The five rarity bands with their stat multipliers and affix counts.</summary>
    internal const string RaritiesReference = DocumentPath + "#/rarities";

    /// <summary>The four chapter bands and their drop shares.</summary>
    internal const string DropShareReference = DocumentPath + "#/dropShareByChapterBand";

    /// <summary>The item-power coefficient — the fraction of a chapter's power target one item is.</summary>
    internal const string ItemPowerCoefficientReference = DocumentPath + "#/itemGeneration/itemPowerCoefficient";

    /// <summary>The quality scalar's authored range and the two stat scales it drives.</summary>
    internal const string QualityReference = DocumentPath + "#/itemGeneration/quality";

    /// <summary>The six slots with their primary and secondary stats.</summary>
    internal const string SlotCoefficientsReference = DocumentPath + "#/slotCoefficients";

    /// <summary>The percent stats by rarity — the table a null coefficient points at.</summary>
    internal const string PercentStatsReference = DocumentPath + "#/percentStatsByRarity";

    /// <summary>The fourteen affixes.</summary>
    internal const string AffixesReference = DocumentPath + "#/affixPool/affixes";

    /// <summary>The 2/4/6 set breakpoints.</summary>
    internal const string SetBreakpointsReference = DocumentPath + "#/sets/breakpoints";

    /// <summary>The percentages of one chapter band must add up to this.</summary>
    internal const double ShareTotal = 100.0;

    /// <summary>How far a band's total may drift from <see cref="ShareTotal"/> before it is refused.</summary>
    /// <remarks>
    /// A tolerance rather than an equality because the shares are authored to one decimal place and
    /// summed as doubles — sixty plus twenty-seven plus ten plus 2.7 plus 0.3 does not land exactly on
    /// a hundred in binary floating point. Far tighter than any authoring mistake: a band that is a
    /// thousandth of a percent out is still refused.
    /// </remarks>
    internal const double ShareTolerance = 1e-9;

    private readonly IReadOnlyDictionary<Rarity, RarityBandRow> _bands;
    private readonly IReadOnlyList<(int From, int To, IReadOnlyList<(Rarity Rarity, double Share)> Shares)> _dropShares;
    private readonly IReadOnlyList<SlotCoefficients> _slotCoefficients;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<Rarity, double>> _percentStats;
    private readonly IReadOnlyList<(GearAffixDefinition Definition, Rarity? MinimumRarity)> _affixes;

    private DropsTuning(
        IReadOnlyDictionary<Rarity, RarityBandRow> bands,
        IReadOnlyList<(int From, int To, IReadOnlyList<(Rarity Rarity, double Share)> Shares)> dropShares,
        double itemPowerCoefficient,
        QualityScales quality,
        IReadOnlyList<SlotCoefficients> slotCoefficients,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Rarity, double>> percentStats,
        IReadOnlyList<(GearAffixDefinition Definition, Rarity? MinimumRarity)> affixes,
        IReadOnlyList<int> setBreakpoints)
    {
        _bands = bands;
        _dropShares = dropShares;
        ItemPowerCoefficient = itemPowerCoefficient;
        Quality = quality;
        _slotCoefficients = slotCoefficients;
        _percentStats = percentStats;
        _affixes = affixes;
        SetBreakpoints = setBreakpoints;
    }

    /// <summary>The fraction of a chapter's power target one item carries.</summary>
    internal double ItemPowerCoefficient { get; }

    /// <summary>The quality scalar's range and the two stat scales it drives.</summary>
    internal QualityScales Quality { get; }

    /// <summary>The piece counts a set bonus fires at, ascending.</summary>
    internal IReadOnlyList<int> SetBreakpoints { get; }

    /// <summary>How many rarity bands the ladder authors.</summary>
    internal int BandCount => _bands.Count;

    /// <summary>How many affixes the pool authors.</summary>
    internal int AffixCount => _affixes.Count;

    /// <summary>How many chapter bands the drop table authors.</summary>
    internal int ChapterBandCount => _dropShares.Count;

    /// <summary>One rarity band's row.</summary>
    /// <param name="rarity">The band to look up.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="InvalidTunableException">The document authors no row for this band.</exception>
    internal RarityBandRow Band(Rarity rarity) =>
        _bands.TryGetValue(rarity, out var band)
            ? band
            : throw new InvalidTunableException(
                RaritiesReference,
                $"The ladder authors no row for {rarity}, so there is no stat multiplier to scale an " +
                "item of that band by and no affix count to roll it. A band with no row is a rarity " +
                "the drop table can produce and the generator cannot build.");

    /// <summary>The drop shares that apply to one chapter, as percentages.</summary>
    /// <param name="chapter">The chapter, from 1.</param>
    /// <returns>Each band's share, in the order the document authors them.</returns>
    /// <exception cref="InvalidTunableException">No authored band covers this chapter.</exception>
    internal IReadOnlyList<(Rarity Rarity, double Share)> SharesFor(int chapter)
    {
        foreach (var band in _dropShares)
        {
            if (chapter >= band.From && chapter <= band.To)
            {
                return band.Shares;
            }
        }

        throw new InvalidTunableException(
            DropShareReference,
            $"No chapter band covers chapter {AuthoredToken.Render(chapter)}. The bands are the whole " +
            "of the drop table; a chapter outside all of them has no odds at all, and extending them " +
            "here would mean choosing that chapter's legendary rate on the author's behalf.");
    }

    /// <summary>One slot's stats and coefficients.</summary>
    /// <param name="slot">The slot to look up.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="InvalidTunableException">The document authors no row for this slot.</exception>
    internal SlotCoefficients Coefficients(GearSlot slot)
    {
        foreach (var row in _slotCoefficients)
        {
            if (row.Slot == slot)
            {
                return row;
            }
        }

        throw new InvalidTunableException(
            SlotCoefficientsReference,
            $"The coefficient table authors no row for {slot}, so an item in that slot has no stats " +
            "to derive at all.");
    }

    /// <summary>The value of a percent stat at one rarity, before quality is applied.</summary>
    /// <param name="stat">The authored stat token, e.g. <c>CRIT</c>.</param>
    /// <param name="rarity">The band.</param>
    /// <returns>The authored percentage, as a fraction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stat"/> is null.</exception>
    /// <exception cref="InvalidTunableException">The table authors no cell for this pairing.</exception>
    internal double PercentStat(string stat, Rarity rarity)
    {
        ArgumentNullException.ThrowIfNull(stat);

        if (_percentStats.TryGetValue(stat, out var byRarity) &&
            byRarity.TryGetValue(rarity, out var value))
        {
            return value;
        }

        throw new InvalidTunableException(
            PercentStatsReference,
            $"'{stat}' has no authored value at {rarity}. A slot row states a null coefficient " +
            "precisely to say 'read this table instead', so a missing cell is a stat the item is " +
            "supposed to have and nothing can compute.");
    }

    /// <summary>
    /// The affixes an item of this slot and band may draw, in the pool's authored order.
    /// </summary>
    /// <remarks>
    /// Both restrictions are applied here rather than at the roll: the slot restriction is what keeps
    /// lifesteal off boots, and the rarity floor is what keeps the reroll-charge affix off anything
    /// below its authored band. A roller that had to remember either would eventually forget one.
    /// <para>
    /// 🔒 <b>Wrapped rather than handed back as the live <c>List&lt;T&gt;</c></b>, on
    /// <c>Rules.Inventory.InventorySorting</c>' precedent and for its reason: an
    /// <c>IReadOnlyList&lt;T&gt;</c> that is really a <c>List&lt;T&gt;</c> can be cast back and
    /// written through, and this reader is shared by every mint, merge and session-floor grant of the
    /// command that built it — one caller adding a row would change what a later item may roll.
    /// </para>
    /// <para>
    /// ⚠️ <b>The per-call filter is deliberate and is not a candidate for precomputation.</b>
    /// <see cref="Read"/> runs on every command that can produce an item, so materialising all
    /// (slot, band) combinations there would allocate the whole grid on commands that mint nothing,
    /// in place of one list per item that is actually rolled.
    /// </para>
    /// </remarks>
    /// <param name="slot">The slot the item is worn in.</param>
    /// <param name="rarity">The item's band.</param>
    /// <returns>The eligible affixes, read-only. May be empty.</returns>
    internal IReadOnlyList<GearAffixDefinition> EligibleAffixes(GearSlot slot, Rarity rarity)
    {
        var eligible = new List<GearAffixDefinition>(_affixes.Count);

        foreach (var row in _affixes)
        {
            if (row.MinimumRarity is { } floor && rarity < floor)
            {
                continue;
            }

            foreach (var allowed in row.Definition.Slots)
            {
                if (allowed == slot)
                {
                    eligible.Add(row.Definition);
                    break;
                }
            }
        }

        return eligible.AsReadOnly();
    }

    /// <summary>One affix's definition.</summary>
    /// <param name="affixId">The authored id.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="affixId"/> is null.</exception>
    /// <exception cref="InvalidTunableException">The pool authors no affix with this id.</exception>
    internal GearAffixDefinition Affix(string affixId)
    {
        ArgumentNullException.ThrowIfNull(affixId);

        foreach (var row in _affixes)
        {
            if (string.Equals(row.Definition.AffixId, affixId, StringComparison.Ordinal))
            {
                return row.Definition;
            }
        }

        throw new InvalidTunableException(
            AffixesReference,
            $"The pool authors no affix '{affixId}'. An item carrying an affix the pool does not " +
            "declare has a stat nothing can price, re-roll or display.");
    }

    /// <summary>Reads the gear tables. Throws rather than defaulting on anything missing or unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The tables.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static DropsTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new DropsTuning(
            ReadBands(content),
            ReadDropShares(content),
            ReadItemPowerCoefficient(content),
            QualityScales.Read(content),
            ReadSlotCoefficients(content),
            ReadPercentStats(content),
            ReadAffixes(content),
            ReadSetBreakpoints(content));
    }

    // The drop bands and the affix rows are held as named tuples rather than as record types of
    // their own. Both are private storage shapes with no behaviour, and a declared type for either
    // would carry a rarity in its own constructor signature — which the luck-routing rule reads as
    // a producer of a grant outcome and would then need an exemption row apiece, widening a rule that
    // exists to be narrow.

    private static IReadOnlyDictionary<Rarity, RarityBandRow> ReadBands(ContentSnapshot content)
    {
        var array = RequireArray(content, RaritiesReference, "the rarity ladder");
        var bands = new Dictionary<Rarity, RarityBandRow>(array.Items.Count);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = RaritiesReference + "/" + AuthoredToken.Render(i);

            var multiplierReference = pointer + "/statMultiplier";
            var multiplier = content.ReadDouble(multiplierReference);

            if (!double.IsFinite(multiplier) || multiplier <= 0.0)
            {
                throw new InvalidTunableException(
                    multiplierReference,
                    "A band's stat multiplier scales item power, so it is a positive finite number. " +
                    $"This document authors {AuthoredToken.Render(multiplier)}, which would make " +
                    "every item of the band worthless or negative.");
            }

            var countReference = pointer + "/affixCount";
            var count = content.ReadInt32(countReference);

            if (count < 0)
            {
                throw new InvalidTunableException(
                    countReference,
                    "A band rolls a whole number of affixes, and this document authors " +
                    AuthoredToken.Render(count) + ".");
            }

            var band = AuthoredToken.Parse<Rarity>(
                content, pointer + "/id", "a band on the rarity ladder");

            if (!bands.TryAdd(band, new RarityBandRow(multiplier, count)))
            {
                throw new InvalidTunableException(
                    pointer + "/id",
                    $"{band} is authored twice, so which multiplier an item of that band gets depends " +
                    "on which row a reader reaches first.");
            }
        }

        return bands;
    }

    private static IReadOnlyList<(int From, int To, IReadOnlyList<(Rarity Rarity, double Share)> Shares)>
        ReadDropShares(ContentSnapshot content)
    {
        var array = RequireArray(content, DropShareReference, "the chapter-banded drop table");
        var bands = new (int From, int To, IReadOnlyList<(Rarity Rarity, double Share)> Shares)[array.Items.Count];

        for (var i = 0; i < bands.Length; i++)
        {
            var pointer = DropShareReference + "/" + AuthoredToken.Render(i);

            var from = content.ReadInt32(pointer + "/chapterFrom");
            var to = content.ReadInt32(pointer + "/chapterTo");

            if (from < 1 || to < from)
            {
                throw new InvalidTunableException(
                    pointer,
                    $"The band runs from chapter {AuthoredToken.Render(from)} to " +
                    $"{AuthoredToken.Render(to)}, which covers no chapter at all.");
            }

            bands[i] = (from, to, ReadShares(content, pointer + "/share"));
        }

        return Array.AsReadOnly(bands);
    }

    /// <summary>
    /// One band's shares, refusing a band that does not add up.
    /// </summary>
    /// <remarks>
    /// The total is checked because it is the one property of the table nothing else can catch: a
    /// band summing to ninety would still draw, silently at the wrong odds, and the disclosure page
    /// those odds are published on would be wrong with every test green.
    /// </remarks>
    private static IReadOnlyList<(Rarity Rarity, double Share)> ReadShares(
        ContentSnapshot content, string reference)
    {
        var value = content.Read(reference);

        if (value.Kind != ContentValueKind.Object || value.MemberNames.Count == 0)
        {
            throw new InvalidTunableException(
                reference, "A band's shares are a non-empty map from rarity to percentage.");
        }

        var shares = new List<(Rarity, double)>(value.MemberNames.Count);
        var total = 0.0;

        foreach (var name in value.MemberNames)
        {
            // The document's own comment convention, skipped here exactly as the percent table skips
            // it: a `_doc` beside a band's shares is prose, not a rarity nobody declared.
            if (name.StartsWith('_'))
            {
                continue;
            }

            var memberReference = reference + "/" + name;

            if (!AuthoredToken.TryParse<Rarity>(name, out var rarity))
            {
                throw new InvalidTunableException(
                    memberReference,
                    $"'{name}' is not a band on the rarity ladder, which is " +
                    $"{AuthoredToken.Names<Rarity>()}.");
            }

            var share = content.ReadDouble(memberReference);

            if (!double.IsFinite(share) || share < 0.0)
            {
                throw new InvalidTunableException(
                    memberReference,
                    "A share is a finite, non-negative percentage, and this document authors " +
                    AuthoredToken.Render(share) + ".");
            }

            shares.Add((rarity, share));
            total += share;
        }

        if (Math.Abs(total - ShareTotal) > ShareTolerance)
        {
            throw new InvalidTunableException(
                reference,
                $"The band's shares add up to {AuthoredToken.Render(total)}, not " +
                $"{AuthoredToken.Render(ShareTotal)}. A band that does not add up still draws — at " +
                "odds nobody authored, and different from the ones the disclosure page publishes.");
        }

        return shares.AsReadOnly();
    }

    private static double ReadItemPowerCoefficient(ContentSnapshot content)
    {
        var coefficient = content.ReadDouble(ItemPowerCoefficientReference);

        if (!double.IsFinite(coefficient) || coefficient <= 0.0)
        {
            throw new InvalidTunableException(
                ItemPowerCoefficientReference,
                "The item-power coefficient is the positive fraction of a chapter's power target one " +
                $"item carries, and this document authors {AuthoredToken.Render(coefficient)}.");
        }

        return coefficient;
    }

    private static IReadOnlyList<SlotCoefficients> ReadSlotCoefficients(ContentSnapshot content)
    {
        var array = RequireArray(content, SlotCoefficientsReference, "the slot coefficient table");
        var rows = new SlotCoefficients[array.Items.Count];

        for (var i = 0; i < rows.Length; i++)
        {
            var pointer = SlotCoefficientsReference + "/" + AuthoredToken.Render(i);

            rows[i] = new SlotCoefficients(
                AuthoredToken.Parse<GearSlot>(content, pointer + "/slot", "an equipment slot"),
                content.ReadText(pointer + "/primaryStat"),
                ReadCoefficient(content, pointer + "/primaryCoef"),
                content.ReadText(pointer + "/secondaryStat"),
                ReadCoefficient(content, pointer + "/secondaryCoef"));
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>
    /// A flat coefficient, or <see langword="null"/> for the authored statement that the stat is a
    /// percentage read from the per-rarity table.
    /// </summary>
    private static double? ReadCoefficient(ContentSnapshot content, string reference)
    {
        if (content.Read(reference).IsUnauthorised)
        {
            return null;
        }

        var coefficient = content.ReadDouble(reference);

        if (!double.IsFinite(coefficient) || coefficient <= 0.0)
        {
            throw new InvalidTunableException(
                reference,
                "A flat stat coefficient is a positive finite multiplier on item power, and this " +
                $"document authors {AuthoredToken.Render(coefficient)}. A stat that scales with " +
                "rarity alone is authored as null, which means 'read the percent table' rather than " +
                "'no coefficient'.");
        }

        return coefficient;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<Rarity, double>> ReadPercentStats(
        ContentSnapshot content)
    {
        var value = content.Read(PercentStatsReference);

        if (value.Kind != ContentValueKind.Object)
        {
            throw new InvalidTunableException(
                PercentStatsReference, "The percent-stat table is a map from stat to a map by rarity.");
        }

        var table = new Dictionary<string, IReadOnlyDictionary<Rarity, double>>(StringComparer.Ordinal);

        foreach (var stat in value.MemberNames)
        {
            if (stat.StartsWith('_'))
            {
                continue;
            }

            var statReference = PercentStatsReference + "/" + stat;
            var row = content.Read(statReference);

            if (row.Kind != ContentValueKind.Object || row.MemberNames.Count == 0)
            {
                throw new InvalidTunableException(
                    statReference, "A percent stat authors one value per rarity band.");
            }

            var byRarity = new Dictionary<Rarity, double>();

            foreach (var name in row.MemberNames)
            {
                var cellReference = statReference + "/" + name;

                if (!AuthoredToken.TryParse<Rarity>(name, out var rarity))
                {
                    throw new InvalidTunableException(
                        cellReference,
                        $"'{name}' is not a band on the rarity ladder, which is " +
                        $"{AuthoredToken.Names<Rarity>()}.");
                }

                var cell = content.ReadDouble(cellReference);

                if (!double.IsFinite(cell) || cell < 0.0)
                {
                    throw new InvalidTunableException(
                        cellReference,
                        "A percent stat is a finite, non-negative fraction, and this document authors " +
                        AuthoredToken.Render(cell) + ".");
                }

                byRarity[rarity] = cell;
            }

            table[stat] = byRarity;
        }

        return table;
    }

    private static IReadOnlyList<(GearAffixDefinition Definition, Rarity? MinimumRarity)>
        ReadAffixes(ContentSnapshot content)
    {
        var array = RequireArray(content, AffixesReference, "the affix pool");
        var affixes = new (GearAffixDefinition Definition, Rarity? MinimumRarity)[array.Items.Count];

        for (var i = 0; i < affixes.Length; i++)
        {
            var pointer = AffixesReference + "/" + AuthoredToken.Render(i);

            var minimum = content.ReadDouble(pointer + "/min");
            var maximum = content.ReadDouble(pointer + "/max");

            // Sign-free since the damage-reduction affix re-signed: it adds flat onto DR_PCT's
            // base 1.0, so its authored range is negative. Order and finiteness stay required.
            if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
            {
                throw new InvalidTunableException(
                    pointer,
                    $"The affix range runs from {AuthoredToken.Render(minimum)} to " +
                    $"{AuthoredToken.Render(maximum)}, which is not a range a roll can land inside.");
            }

            var (stat, op) = ReadContribution(content, pointer);

            affixes[i] = (
                new GearAffixDefinition(
                    content.ReadText(pointer + "/id"),
                    stat,
                    op,
                    minimum,
                    maximum,
                    ReadSlots(content, pointer + "/slots"),

                    // The context gate the bucket rides on, carried onto the synthesised effect
                    // untouched. The damage-vs-Elites affix is target-gated; the other twelve are
                    // ungated.
                    content.IsAuthorised(pointer + "/condition")
                        ? RequireContextGate(
                            ConditionContentReader.Read(content, pointer + "/condition", "an affix's condition"),
                            pointer + "/condition")
                        : null),
                ReadMinimumRarity(content, pointer + "/minRarity"));
        }

        return Array.AsReadOnly(affixes);
    }

    /// <summary>
    /// What one affix row writes: the stat and the bucket, or a matched pair of nulls where the pool
    /// authors an affix nothing can apply yet.
    /// </summary>
    /// <remarks>
    /// Half a pair is refused rather than tolerated. A stat with no op cannot be applied and an op
    /// with no stat cannot be aimed, so either alone is a row that reads as authored and contributes
    /// nothing — the shape a null exists to keep visible, wearing the clothes of a complete one.
    /// The op set is narrowed to the two additive buckets here as well as in the schema, because this
    /// is the layer that would otherwise hand the aggregation an op an affix has no business carrying.
    /// </remarks>
    private static (StatId? Stat, EffectOp? Op) ReadContribution(ContentSnapshot content, string pointer)
    {
        var statReference = pointer + "/stat";
        var opReference = pointer + "/op";

        var statAuthored = !content.Read(statReference).IsUnauthorised;
        var opAuthored = !content.Read(opReference).IsUnauthorised;

        if (statAuthored != opAuthored)
        {
            throw new InvalidTunableException(
                statAuthored ? opReference : statReference,
                "An affix names a stat and the bucket it writes it into, or neither. This row authors " +
                (statAuthored ? "a stat with no op" : "an op with no stat") +
                ", which reads as an authored contribution and applies nothing — the exact shape the " +
                "null is there to keep visible.");
        }

        if (!statAuthored)
        {
            return (null, null);
        }

        var op = AuthoredToken.Parse<EffectOp>(
            content, opReference, "the flat or the percent additive bucket");

        if (op is not (EffectOp.STAT_ADD_FLAT or EffectOp.STAT_ADD_PCT))
        {
            throw new InvalidTunableException(
                opReference,
                $"An affix is a standing stat modifier, so it writes through {EffectOp.STAT_ADD_FLAT} " +
                $"or {EffectOp.STAT_ADD_PCT} and nothing else. This document authors {op}, which would " +
                "let a rolled affix do something no affix is described as doing.");
        }

        return (AuthoredToken.Parse<StatId>(content, statReference, "one of the stats"), op);
    }

    /// <summary>
    /// A gear gate must read a contextual subject; an ambient one is refused where it is authored.
    /// </summary>
    /// <remarks>
    /// An ambient condition on a standing gear grant would evaluate in battle and then throw out of
    /// the strict aggregation the first time a hero screen composes the build -- so the pool refuses
    /// it at load, exactly as the set-bonus reader does.
    /// </remarks>
    private static EffectCondition RequireContextGate(EffectCondition condition, string reference)
    {
        var subjects = ConditionSubjects.Of(condition);

        return subjects.ReadsTarget || subjects.ReadsAttacker
            ? condition
            : throw new InvalidTunableException(
                reference,
                "The tree reads neither the current target nor the attacker, so it is not a " +
                "context gate: an ambient condition on standing gear evaluates in battle and then " +
                "throws out of the hero screen's strict aggregation. Gate gear on a contextual " +
                "subject, or leave it ungated.");
    }

    private static IReadOnlyList<GearSlot> ReadSlots(ContentSnapshot content, string reference)
    {
        var array = RequireArray(content, reference, "an affix's slot restriction");
        var slots = new GearSlot[array.Items.Count];

        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = AuthoredToken.Parse<GearSlot>(
                content, reference + "/" + AuthoredToken.Render(i), "an equipment slot");
        }

        return Array.AsReadOnly(slots);
    }

    /// <summary>An affix's rarity floor, or <see langword="null"/> where it authors none.</summary>
    /// <remarks>
    /// Absent rather than unauthorised: the schema makes the floor optional, so a row without one is
    /// an affix with no floor rather than a hole somebody left.
    /// </remarks>
    private static Rarity? ReadMinimumRarity(ContentSnapshot content, string reference) =>
        content.TryRead(reference, out var value) && value is { IsUnauthorised: false }
            ? AuthoredToken.Parse<Rarity>(content, reference, "a band on the rarity ladder")
            : null;

    private static IReadOnlyList<int> ReadSetBreakpoints(ContentSnapshot content)
    {
        var array = RequireArray(content, SetBreakpointsReference, "the set breakpoints");
        var breakpoints = new int[array.Items.Count];

        for (var i = 0; i < breakpoints.Length; i++)
        {
            var pointer = SetBreakpointsReference + "/" + AuthoredToken.Render(i);
            var pieces = content.ReadInt32(pointer);

            if (pieces < 1 || (i > 0 && pieces <= breakpoints[i - 1]))
            {
                throw new InvalidTunableException(
                    pointer,
                    "The breakpoints are ascending piece counts, and this document authors " +
                    $"{AuthoredToken.Render(pieces)} at position {AuthoredToken.Render(i)}. A " +
                    "non-ascending ladder would fire a later tier before an earlier one.");
            }

            breakpoints[i] = pieces;
        }

        return Array.AsReadOnly(breakpoints);
    }

    private static ContentValue RequireArray(ContentSnapshot content, string reference, string what)
    {
        var value = content.Read(reference);

        return value.Kind == ContentValueKind.Array && value.Items.Count > 0
            ? value
            : throw new InvalidTunableException(
                reference, $"This document authors {value} for {what}, which is not a non-empty array.");
    }
}

/// <summary>The quality scalar's authored range and the two asymmetric scales it drives.</summary>
/// <param name="Minimum">The bottom of the quality range.</param>
/// <param name="Maximum">The top of the quality range.</param>
/// <param name="PrimaryBase">The primary stat's multiplier at the bottom of the range.</param>
/// <param name="PrimarySpan">How far the primary stat's multiplier climbs across the range.</param>
/// <param name="SecondaryBase">The secondary stat's multiplier at the bottom of the range.</param>
/// <param name="SecondarySpan">How far the secondary stat's multiplier climbs across the range.</param>
/// <remarks>
/// The two scales are deliberately asymmetric — the secondary spans further than the primary — so a
/// high-quality item leans hardest into its secondary stat. That asymmetry is the whole reason two
/// items of the same base, band and chapter are not the same item, so it is read rather than assumed.
/// </remarks>
internal readonly record struct QualityScales(
    double Minimum,
    double Maximum,
    double PrimaryBase,
    double PrimarySpan,
    double SecondaryBase,
    double SecondarySpan)
{
    /// <summary>The bottom of the unit interval a quality scalar must lie in.</summary>
    internal const double UnitFloor = 0.0;

    /// <summary>The top of it.</summary>
    internal const double UnitCeiling = 1.0;

    /// <summary>The primary stat's multiplier at a given quality.</summary>
    /// <param name="quality">The quality scalar.</param>
    /// <returns>The multiplier.</returns>
    internal double Primary(double quality) => PrimaryBase + (PrimarySpan * quality);

    /// <summary>The secondary stat's multiplier at a given quality.</summary>
    /// <param name="quality">The quality scalar.</param>
    /// <returns>The multiplier.</returns>
    internal double Secondary(double quality) => SecondaryBase + (SecondarySpan * quality);

    /// <summary>Reads the quality block.</summary>
    /// <param name="content">The snapshot being read.</param>
    /// <returns>The scales.</returns>
    /// <exception cref="InvalidTunableException">The range is empty, or a number is not finite.</exception>
    internal static QualityScales Read(ContentSnapshot content)
    {
        var minimum = Finite(content, DropsTuning.QualityReference + "/min");
        var maximum = Finite(content, DropsTuning.QualityReference + "/max");

        if (maximum <= minimum)
        {
            throw new InvalidTunableException(
                DropsTuning.QualityReference,
                $"The quality range runs from {AuthoredToken.Render(minimum)} to " +
                $"{AuthoredToken.Render(maximum)}, so every item would roll the same quality and the " +
                "quality bar would be decoration.");
        }

        // Refused here rather than left to the mint, because this is the layer that can still name
        // the pointer: a wider range reads cleanly and then throws once per drop, from a call site
        // that no longer has the document. The bounds are restated rather than read off the gear
        // instance, which sits in a layer this one may not name.
        if (minimum < UnitFloor || maximum > UnitCeiling)
        {
            throw new InvalidTunableException(
                DropsTuning.QualityReference,
                $"Quality is the one scalar q in [{AuthoredToken.Render(UnitFloor)}, " +
                $"{AuthoredToken.Render(UnitCeiling)}] — the UI shows it directly as a percentage " +
                $"— and this document authors {AuthoredToken.Render(minimum)} to " +
                $"{AuthoredToken.Render(maximum)}. A range outside it mints items no gear instance " +
                "will accept, one exception per drop.");
        }

        return new QualityScales(
            minimum,
            maximum,
            Finite(content, DropsTuning.QualityReference + "/primaryScale/base"),
            Finite(content, DropsTuning.QualityReference + "/primaryScale/span"),
            Finite(content, DropsTuning.QualityReference + "/secondaryScale/base"),
            Finite(content, DropsTuning.QualityReference + "/secondaryScale/span"));
    }

    private static double Finite(ContentSnapshot content, string reference)
    {
        var value = content.ReadDouble(reference);

        return double.IsFinite(value)
            ? value
            : throw new InvalidTunableException(
                reference,
                "Every number in the quality block multiplies a stat, so a non-finite one produces an " +
                "item whose stats cannot be persisted at all.");
    }
}
