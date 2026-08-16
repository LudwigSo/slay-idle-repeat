using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The pity registry: the ten source classes with their counter keys and scopes, the hard/soft pity
/// ladders of the five classes that author one, and the rarity-floor rule — read out of
/// <c>tuning/luck.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type owns counter-key formation, and it is the only place that forms one.</b> A source
/// class does not address a counter by itself: <c>CHEST_STANDARD</c> runs three ladders at once, so
/// a counter is addressed by the pairing of the class's authored <c>counterKey</c> with the
/// guarantee it protects — <c>chest.standard:A</c>, <c>chest.standard:S</c>,
/// <c>chest.standard:SS</c>. Adding a fourth rung stays a data edit because the key is derived from
/// the data rather than declared beside it.
/// </para>
/// <para>
/// Five classes author the <c>hardPity[] + softPity</c> shape and are the ones the rarity-ladder
/// path serves. The other five author their rule in a different shape entirely — a dry-streak
/// breaker, a failure-rate mercy, a draft composition rule, a jackpot spin count, a chest-pick
/// guarantee — and asking this type for a ladder they do not have answers false rather than
/// synthesising one. Those five call the pity primitives directly; the tasks that wire them are
/// named in the exception message.
/// </para>
/// <para>
/// Not cached. Every reader re-reads the snapshot it was handed, so a content version swap cannot
/// leave a stale ladder behind a static field.
/// </para>
/// </remarks>
internal sealed class LuckTuning
{
    /// <summary>The document the pity registry lives in.</summary>
    internal const string DocumentPath = "tuning/luck.json";

    private const string RarityFloorPointer = DocumentPath + "#/rarityFloor";

    /// <summary>The ten source-class rows: id, counter key, counter scope.</summary>
    internal const string SourceClassesReference = DocumentPath + "#/sourceClasses";

    /// <summary>How a floored table is renormalised. <c>PROPORTIONAL</c> as shipped.</summary>
    internal const string RenormalisationReference = RarityFloorPointer + "/renormalisation";

    /// <summary>Whether a floored draw still advances and resets counters. <c>true</c> as shipped.</summary>
    internal const string CountersAdvanceNormallyReference = RarityFloorPointer + "/countersAdvanceNormally";

    /// <summary>The standard-chest block — the 10/40/160 ladder plus its soft-pity curve.</summary>
    internal const string ChestStandardReference = DocumentPath + "#/chestStandard";

    /// <summary>The premium-chest block — the 5/25 ladder plus its soft-pity curve.</summary>
    internal const string ChestPremiumReference = DocumentPath + "#/chestPremium";

    /// <summary>The apex-chest block — one rung, and no soft pity at that density.</summary>
    internal const string ChestApexReference = DocumentPath + "#/chestApex";

    /// <summary>The Pet Egg block — the 30/150 ladder.</summary>
    internal const string EggPetReference = DocumentPath + "#/eggPet";

    /// <summary>The Mount Crate block — the 8/30 ladder plus its soft-pity curve.</summary>
    internal const string CrateMountReference = DocumentPath + "#/crateMount";

    /// <summary>The perk-draft block — the five <c>DRAFT</c> rules' authored dials.</summary>
    internal const string DraftReference = DocumentPath + "#/draft";

    /// <summary>The chest-pick block — the one minigame that carries a counter.</summary>
    internal const string ChestPickReference = DocumentPath + "#/minigame/chestPick";

    /// <summary>The member every ladder block names its rungs under.</summary>
    internal const string HardPityMember = "hardPity";

    /// <summary>The member every ladder block names its soft-pity curve under. May be an authored null.</summary>
    internal const string SoftPityMember = "softPity";

    /// <summary>
    /// The separator between an authored counter key and the guarantee token it protects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A colon, matching the aggregate's existing composite counter keys. It cannot occur in the
    /// left half — the schema's <c>counterKey</c> pattern is lower-case letters, digits and dots —
    /// so a key parses back unambiguously.
    /// </para>
    /// <para>
    /// ⚠️ The right half used to be a <see cref="Rarity"/>, two letters at most, and the claim rested
    /// on that. It no longer does: a guarantee can be named by an authored outcome token instead, and
    /// while every document that authors one constrains it to upper-case letters, digits and
    /// underscores, that constraint lives in a different schema from this one. So the formation point
    /// <em>enforces</em> the invariant rather than inheriting it — see
    /// <see cref="CounterKey(SourceClass, string)"/>.
    /// </para>
    /// </remarks>
    internal const char CounterKeySeparator = ':';

    /// <summary>The five classes that state their protection as a rarity ladder, and their blocks.</summary>
    private static readonly (SourceClass Source, string Reference)[] LadderBlocks =
    {
        (SourceClass.CHEST_STANDARD, ChestStandardReference),
        (SourceClass.CHEST_PREMIUM, ChestPremiumReference),
        (SourceClass.CHEST_APEX, ChestApexReference),
        (SourceClass.EGG_PET, EggPetReference),
        (SourceClass.CRATE_MOUNT, CrateMountReference),
    };

    /// <summary>
    /// The five classes that state their protection in another shape, the block that holds the real
    /// rule, and where a caller goes for it instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named rather than left as a bare "no ladder": a caller that asked for the wrong shape has to
    /// be able to tell that from a block somebody forgot to author, and the next step is what turns
    /// the refusal into something actionable.
    /// </para>
    /// <para>
    /// 🔴 <b>Two of the five stopped being a task name.</b> <c>DRAFT</c> and <c>MINIGAME</c> carried
    /// M4-01b as "the task that wires it" until M4-01b ran; a note that sends the next reader to a
    /// task that has already landed is worse than none, so those two now name the member that
    /// actually serves them. All five are still unserved <em>by the ladder path</em> — that is what
    /// this array is about, and it is why none of them was deleted.
    /// </para>
    /// </remarks>
    private static readonly (SourceClass Source, string Block, string ServedBy)[] UnservedShapes =
    {
        (SourceClass.DROP_RUN, "dropRun", "the luck façade's ResolveRunDrop serves it"),
        (SourceClass.ENHANCE, "enhance", "M4-04 wires it"),
        (SourceClass.DRAFT, "draft", "the luck façade's ResolveDraft serves it"),
        (SourceClass.WHEEL, "wheel", "M4-09 wires it"),
        (SourceClass.MINIGAME, "minigame", "the luck façade's ResolveChestPick serves it"),
    };

    private readonly IReadOnlyDictionary<SourceClass, PityLadder> _ladders;

    private LuckTuning(
        IReadOnlyList<SourceClassRow> sourceClasses,
        IReadOnlyDictionary<SourceClass, PityLadder> ladders,
        RarityFloorRule rarityFloor,
        DraftRule draft,
        ChestPickRule chestPick)
    {
        SourceClasses = sourceClasses;
        _ladders = ladders;
        RarityFloor = rarityFloor;
        Draft = draft;
        ChestPick = chestPick;
    }

    /// <summary>The ten source-class rows, in the order the document lists them.</summary>
    internal IReadOnlyList<SourceClassRow> SourceClasses { get; }

    /// <summary>The one rule for flooring a class table at a guaranteed rarity.</summary>
    internal RarityFloorRule RarityFloor { get; }

    /// <summary>
    /// The <c>DRAFT</c> class's five rules, in the shape they are authored — not a rarity ladder.
    /// </summary>
    internal DraftRule Draft { get; }

    /// <summary>The <c>MINIGAME</c> class's one guarantee: the chest pick's gold-tier rung.</summary>
    internal ChestPickRule ChestPick { get; }

    /// <summary>The authored row for one source class.</summary>
    /// <param name="source">The class to look up.</param>
    /// <returns>Its registry row.</returns>
    /// <exception cref="InvalidTunableException">The document authors no row for this class.</exception>
    internal SourceClassRow Row(SourceClass source)
    {
        foreach (var row in SourceClasses)
        {
            if (row.Id == source)
            {
                return row;
            }
        }

        throw new InvalidTunableException(
            SourceClassesReference,
            $"The registry authors no row for {source}. Every grant source must be assigned a class, " +
            "and a class with no row is a source whose counter nothing addresses.");
    }

    /// <summary>
    /// The hard/soft pity ladder for a class, when the document authors one in that shape.
    /// </summary>
    /// <param name="source">The class to look up.</param>
    /// <param name="ladder">The ladder, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the class authors a rarity ladder.</returns>
    internal bool TryGetLadder(SourceClass source, out PityLadder ladder) =>
        _ladders.TryGetValue(source, out ladder);

    /// <summary>The hard/soft pity ladder for a class.</summary>
    /// <param name="source">The class to look up.</param>
    /// <returns>Its ladder.</returns>
    /// <exception cref="InvalidTunableException">
    /// The class authors its protection in another shape entirely, so there is no ladder to draw
    /// against. The message names the class, the block that holds its real rule, and where that
    /// rule is served instead.
    /// </exception>
    internal PityLadder Ladder(SourceClass source)
    {
        if (_ladders.TryGetValue(source, out var ladder))
        {
            return ladder;
        }

        foreach (var unserved in UnservedShapes)
        {
            if (unserved.Source == source)
            {
                throw new InvalidTunableException(
                    DocumentPath + "#/" + unserved.Block,
                    $"{source} states its protection in the '{unserved.Block}' block, in a shape the " +
                    $"rarity-ladder path does not serve — {unserved.ServedBy}. Drawing it " +
                    "against a ladder nobody authored would be inventing odds.");
            }
        }

        throw new InvalidTunableException(
            SourceClassesReference,
            $"{source} authors no pity ladder and states no rule in any other shape either.");
    }

    /// <summary>
    /// The counter id a class's guarantee is stored under —
    /// <c>"&lt;counterKey&gt;&lt;separator&gt;&lt;rarity&gt;"</c>, e.g. <c>chest.standard:A</c>.
    /// </summary>
    /// <remarks>
    /// The one place a counter key is formed. Everything that reads or writes a pity counter goes
    /// through here, so the authored key and the stored key cannot drift apart.
    /// </remarks>
    /// <param name="source">The class whose counter is being addressed.</param>
    /// <param name="guarantee">The guarantee rarity that counter protects.</param>
    /// <returns>The counter id.</returns>
    /// <exception cref="InvalidTunableException">
    /// The class authors no counter key — its counter is not player-scoped, so it has no id in this
    /// map at all.
    /// </exception>
    internal string CounterKey(SourceClass source, Rarity guarantee) =>
        CounterKey(source, guarantee.ToString());

    /// <summary>
    /// The counter id a class's guarantee is stored under, where the guarantee is named by something
    /// other than a gear rarity band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chest pick guarantees an outcome <em>tier</em>, not a band on the gear ladder, so it has no
    /// <see cref="Rarity"/> to pair its authored key with. Rather than let that class spell its own
    /// key, the formation point widens: the rarity overload delegates here, and this stays the one
    /// place a counter id is built.
    /// </para>
    /// <para>
    /// 🔒 Widening it from a closed enum to a token means the "the separator occurs in neither half"
    /// invariant is no longer free, so it is checked here. A token carrying a
    /// <see cref="CounterKeySeparator"/> would make one authored key and one guarantee produce a
    /// counter id that parses back as a different pair — two guarantees quietly sharing a counter,
    /// which reads to the player as a pity counter that reset itself.
    /// </para>
    /// </remarks>
    /// <param name="source">The class whose counter is being addressed.</param>
    /// <param name="guarantee">The authored token naming the guarantee that counter protects.</param>
    /// <returns>The counter id.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="guarantee"/> is blank, or carries the separator.
    /// </exception>
    /// <exception cref="InvalidTunableException">
    /// The class authors no counter key — its counter is not player-scoped, so it has no id in this
    /// map at all.
    /// </exception>
    internal string CounterKey(SourceClass source, string guarantee)
    {
        ArgumentNullException.ThrowIfNull(guarantee);

        if (string.IsNullOrWhiteSpace(guarantee))
        {
            throw new ArgumentException(
                "A blank guarantee token pairs the authored key with nothing, so two different " +
                "guarantees of one class would address the same counter.",
                nameof(guarantee));
        }

        if (guarantee.IndexOf(CounterKeySeparator, StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException(
                $"'{guarantee}' carries the counter-key separator '{CounterKeySeparator}'. A key is " +
                "an authored key paired with one guarantee token, and a token holding the separator " +
                "produces an id that parses back as a different pair — so two guarantees of one " +
                "class would address the same counter and each would look, to the player, like a " +
                "pity counter that reset itself.",
                nameof(guarantee));
        }

        var row = Row(source);

        if (row.CounterKey is null)
        {
            throw new InvalidTunableException(
                SourceClassesReference,
                $"{source}'s counter is scoped {row.Scope}, so it authors no key in the player " +
                "counter map and there is no counter id to form. Inventing one would name a counter " +
                "the player profile cannot store.");
        }

        return $"{row.CounterKey}{CounterKeySeparator}{guarantee}";
    }

    /// <summary>
    /// Reads the pity registry. Throws rather than defaulting on anything missing, unauthorised,
    /// mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static LuckTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var sourceClasses = ReadSourceClasses(content);

        var ladders = new Dictionary<SourceClass, PityLadder>();
        foreach (var (source, reference) in LadderBlocks)
        {
            ladders[source] = ReadLadder(content, source, reference);
        }

        return new LuckTuning(
            sourceClasses,
            ladders,
            ReadRarityFloor(content),
            ReadDraft(content),
            ReadChestPick(content));
    }

    /// <summary>Reads the <c>DRAFT</c> class's five rules — none of which is a rarity ladder.</summary>
    private static DraftRule ReadDraft(ContentSnapshot content) => new(
        RequirePositive(content, DraftReference + "/legendaryPityDraftNumber"),
        new SustainAntiBrickRule(
            content.ReadBoolean(DraftReference + "/sustainAntiBrick/enabled"),
            ReadCategory(content, DraftReference + "/sustainAntiBrick/forceCategory")),
        RequirePositive(content, DraftReference + "/qualityFloor/consecutiveDraftsWithoutAboveCommon"),
        ReadPerkRarity(content, DraftReference + "/qualityFloor/forceRarityAtLeast"),
        content.ReadDouble(DraftReference + "/codexBias/neverDraftedWeightMultiplier"),
        content.ReadInt32(DraftReference + "/codexBias/maxBiasSelectedOptions"),
        content.ReadDouble(DraftReference + "/upgradeFamine/ownedUpgradeBias"),
        RequirePositive(content, DraftReference + "/upgradeFamine/consecutiveDraftsWithoutOwnedUpgrade"));

    /// <summary>Reads the chest pick's guarantee — the one <c>MINIGAME</c> rule that carries a counter.</summary>
    private static ChestPickRule ReadChestPick(ContentSnapshot content) => new(
        RequirePositive(content, ChestPickReference + "/chestCount"),
        RequirePositive(content, ChestPickReference + "/goldTierChests"),
        RequirePositive(content, ChestPickReference + "/guaranteeAfterConsecutiveMisses"));

    /// <summary>An authored count that has to be at least one to name anything at all.</summary>
    private static int RequirePositive(ContentSnapshot content, string reference)
    {
        var value = content.ReadInt32(reference);

        return value >= 1
            ? value
            : throw new InvalidTunableException(
                reference,
                $"A guarantee counts draws from one, and this document authors {Render(value)}. " +
                "There is no zeroth draw to force.");
    }

    private static PerkRarity ReadPerkRarity(ContentSnapshot content, string reference)
    {
        var authored = content.ReadText(reference);

        return TryParseName<PerkRarity>(Pascal(authored), out var rarity)
            ? rarity
            : throw new InvalidTunableException(
                reference,
                $"'{authored}' is not a perk band. The authored set is {Names<PerkRarity>()}, spelled " +
                "in the documents' upper-case form. The gear rarity ladder is a different vocabulary " +
                "over different things, and a token from one must not resolve in the other.");
    }

    private static PerkCategory ReadCategory(ContentSnapshot content, string reference)
    {
        var authored = content.ReadText(reference);

        return TryParseName<PerkCategory>(Pascal(authored), out var category)
            ? category
            : throw new InvalidTunableException(
                reference,
                $"'{authored}' is not a perk category. The authored set is {Names<PerkCategory>()}, " +
                "spelled in the documents' SCREAMING_SNAKE form.");
    }

    /// <summary>
    /// An authored <c>SCREAMING_SNAKE</c> token as the enum's PascalCase name.
    /// </summary>
    /// <remarks>
    /// The perk vocabularies are declared in PascalCase and authored in upper snake, unlike
    /// <see cref="Rarity"/>, whose two spellings coincide. Converting rather than declaring a second
    /// table keeps the document's tokens the only list.
    /// </remarks>
    private static string Pascal(string authored)
    {
        if (string.IsNullOrEmpty(authored))
        {
            return authored;
        }

        var word = new StringBuilder(authored.Length);
        var startOfWord = true;

        foreach (var character in authored)
        {
            if (character == '_')
            {
                startOfWord = true;
                continue;
            }

            word.Append(startOfWord ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
            startOfWord = false;
        }

        return word.ToString();
    }

    private static IReadOnlyList<SourceClassRow> ReadSourceClasses(ContentSnapshot content)
    {
        var array = content.Read(SourceClassesReference);

        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                SourceClassesReference,
                "The registry is a non-empty array, one row per grant source class. This document " +
                "authors " + array + ".");
        }

        var rows = new SourceClassRow[array.Items.Count];

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = SourceClassesReference + "/" + Render(i);

            var id = content.ReadText(pointer + "/id");
            if (!TryParseName<SourceClass>(id, out var source))
            {
                throw new InvalidTunableException(
                    pointer + "/id",
                    $"'{id}' is not a grant source class. Adding a new grant source means assigning " +
                    $"it one of {Names<SourceClass>()}; a row naming a class Core does not declare " +
                    "is a source with no class, and skipping it silently is exactly the unprotected " +
                    "grant the founding rule forbids.");
            }

            var scopeName = content.ReadText(pointer + "/counterScope");
            if (!TryParseName<CounterScope>(scopeName, out var scope))
            {
                throw new InvalidTunableException(
                    pointer + "/counterScope",
                    $"'{scopeName}' is not a place a counter is kept. The authored scopes are " +
                    $"{Names<CounterScope>()}.");
            }

            rows[i] = new SourceClassRow(source, ReadCounterKey(content, pointer), scope);
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>The authored counter key, or <see langword="null"/> for a deliberate hole.</summary>
    /// <remarks>
    /// The one leaf in this document where an unauthorised <c>null</c> is a statement rather than a
    /// gap: a class whose counter is not player-scoped has no id in the player counter map at all.
    /// </remarks>
    private static string? ReadCounterKey(ContentSnapshot content, string rowPointer)
    {
        var reference = rowPointer + "/counterKey";
        var value = content.Read(reference);

        if (value.IsUnauthorised)
        {
            return null;
        }

        var key = value.AsText(reference);

        return string.IsNullOrWhiteSpace(key)
            ? throw new InvalidTunableException(
                reference,
                "A blank counter key addresses every counter and none. Author the key, or author " +
                "the row's counter as unauthorised because it is not player-scoped.")
            : key;
    }

    private static PityLadder ReadLadder(ContentSnapshot content, SourceClass source, string block)
    {
        var hardPityReference = block + "/" + HardPityMember;
        var array = content.Read(hardPityReference);

        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                hardPityReference,
                $"{source} authors a rarity ladder, and a hard guarantee is mandatory on every one " +
                "of them. This document authors " + array + ".");
        }

        var rungs = new HardPityStep[array.Items.Count];

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = hardPityReference + "/" + Render(i);

            var everyNthReference = pointer + "/everyNth";
            var everyNth = content.ReadInt32(everyNthReference);
            if (everyNth < 1)
            {
                throw new InvalidTunableException(
                    everyNthReference,
                    "A rung forces the N-th draw since its counter last reset, and this document " +
                    $"authors {Render(everyNth)}. There is no zeroth draw to force — an N of zero " +
                    "would force every single draw of the class.");
            }

            var guaranteeReference = pointer + "/guaranteeRarityAtLeast";
            var guaranteeName = content.ReadText(guaranteeReference);
            if (!TryParseName<Rarity>(guaranteeName, out var guarantee))
            {
                throw new InvalidTunableException(
                    guaranteeReference,
                    $"'{guaranteeName}' is not a band on the gear rarity ladder, which is " +
                    $"{Names<Rarity>()}. The perk band ladder is a different vocabulary over " +
                    "different things, and a token from one must not resolve in the other.");
            }

            rungs[i] = new HardPityStep(everyNth, guarantee);
        }

        return new PityLadder(source, Array.AsReadOnly(rungs), ReadSoftPity(content, source, block));
    }

    private static SoftPityCurve? ReadSoftPity(ContentSnapshot content, SourceClass source, string block)
    {
        var curveReference = block + "/" + SoftPityMember;

        if (content.Read(curveReference).IsUnauthorised)
        {
            return null;
        }

        var target = content.ReadText(curveReference + "/target");
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidTunableException(
                curveReference + "/target",
                $"{source}'s curve raises nothing. A curve with no target is authored as no curve.");
        }

        var thresholdReference = curveReference + "/missThreshold";
        var threshold = content.ReadInt32(thresholdReference);
        if (threshold < 0)
        {
            throw new InvalidTunableException(
                thresholdReference,
                $"A ramp stays flat for a number of misses, and this document authors " +
                $"{Render(threshold)}. A negative threshold ramps a counter that has not moved yet.");
        }

        var slopeReference = curveReference + "/slope";
        var slope = content.ReadDouble(slopeReference);
        if (!double.IsFinite(slope) || slope <= 0.0)
        {
            throw new InvalidTunableException(
                slopeReference,
                $"A ramp's slope is a positive finite number, and this document authors " +
                $"{Render(slope)}. A slope of zero is a curve that does not ramp, which is authored " +
                "as no curve at all rather than as a flat one.");
        }

        return new SoftPityCurve(target, threshold, slope);
    }

    private static RarityFloorRule ReadRarityFloor(ContentSnapshot content)
    {
        var authored = content.ReadText(RenormalisationReference);

        if (!TryParseName<RarityFloorRenormalisation>(authored, out var renormalisation))
        {
            throw new InvalidTunableException(
                RenormalisationReference,
                $"'{authored}' is not a renormalisation this engine implements. The authored set is " +
                $"{Names<RarityFloorRenormalisation>()}, deliberately a one-member enum in both the " +
                "schema and here, so widening it is an edit in both places rather than a token that " +
                "slipped through a string comparison.");
        }

        return new RarityFloorRule(
            renormalisation, content.ReadBoolean(CountersAdvanceNormallyReference));
    }

    /// <summary>
    /// Reads an authored token as a member of a closed vocabulary, case-sensitively and by name only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Enum.TryParse</c> accepts three spellings a name is not, and each one is refused here
    /// before it is parsed rather than after, because all three land on a member
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> then reports as real:
    /// </para>
    /// <list type="bullet">
    /// <item>the underlying wire value — an authored <c>"3"</c> would load as a real band, a wire
    /// value leaking into a place the documents spell with a name;</item>
    /// <item>a comma-separated list, which is combined bitwise even for a non-flags enum — an
    /// authored <c>"C, B"</c> is <c>1 | 2</c> and would load, silently, as <c>A</c>;</item>
    /// <item>surrounding whitespace, which is trimmed away — an authored <c>" SS"</c> is not the
    /// token the schema's enum lists, and accepting it would let two spellings of one band exist.</item>
    /// </list>
    /// </remarks>
    /// <typeparam name="TEnum">The closed vocabulary.</typeparam>
    /// <param name="authored">The token the document spells.</param>
    /// <param name="parsed">The member, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the token is exactly one member's name.</returns>
    private static bool TryParseName<TEnum>(string authored, out TEnum parsed)
        where TEnum : struct, Enum =>
        AuthoredToken.TryParse(authored, out parsed);

    /// <summary>A closed vocabulary's names, for a failure message that shows the whole table.</summary>
    private static string Names<TEnum>()
        where TEnum : struct, Enum =>
        AuthoredToken.Names<TEnum>();

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The invariant rendering.</returns>
    internal static string Render(int value) => AuthoredToken.Render(value);

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The invariant rendering.</returns>
    internal static string Render(double value) => AuthoredToken.Render(value);
}

/// <summary>Where a source class's counter is kept.</summary>
/// <remarks>
/// Only <see cref="PLAYER"/> counters live in the player-scoped counter map. The other two are
/// authored so that the registry can say <em>where</em> a counter lives rather than leaving a null
/// counter key to be read as "no counter at all".
/// </remarks>
internal enum CounterScope
{
    /// <summary>On the player profile, forever. The map the luck service takes as an argument.</summary>
    PLAYER,

    /// <summary>On the gear instance, and inherited by merge outputs.</summary>
    GEAR_INSTANCE,

    /// <summary>On the run, and gone when the run ends.</summary>
    RUN,
}

/// <summary>How the surviving weights of a floored table are rescaled.</summary>
/// <remarks>
/// A one-member enum, matching the schema's one-member enum: widening it is a deliberate edit in
/// both places rather than a value that slipped through a string comparison.
/// </remarks>
internal enum RarityFloorRenormalisation
{
    /// <summary>
    /// Zero every weight below the floor and rescale the survivors to sum to one, preserving their
    /// relative ratios. One rule for every source class.
    /// </summary>
    PROPORTIONAL,
}

/// <summary>One authored source-class row.</summary>
/// <param name="Id">The class.</param>
/// <param name="CounterKey">
/// The authored counter key, e.g. <c>chest.standard</c> — or <see langword="null"/> when the class's
/// counter is not player-scoped and therefore has no id in the player counter map.
/// </param>
/// <param name="Scope">Where the counter lives.</param>
internal readonly record struct SourceClassRow(SourceClass Id, string? CounterKey, CounterScope Scope);

/// <summary>One rung of a hard-pity ladder.</summary>
/// <param name="EveryNth">
/// The draw, counted from the last reset of this rung's counter, that is forced to satisfy the
/// guarantee. The N-th draw is the forced one, not the (N+1)-th.
/// </param>
/// <param name="GuaranteeRarityAtLeast">The rarity the forced draw must reach or beat.</param>
internal readonly record struct HardPityStep(int EveryNth, Rarity GuaranteeRarityAtLeast);

/// <summary>One authored soft-pity curve.</summary>
/// <param name="Target">
/// The rarity whose weight the curve raises, as the document spells it. Text rather than
/// <see cref="Rarity"/> because the schema states <c>softPity</c> once and shares that definition
/// across every block that authors a curve, including the wheel's — whose target is a jackpot
/// segment and not a rarity band at all. The reader keeps the authored token verbatim rather than
/// narrowing it here, and the rules refuse a token that is not a band where they go to read a
/// counter against it.
/// </param>
/// <param name="MissThreshold">The number of misses the curve stays flat for.</param>
/// <param name="Slope">The per-miss multiplier growth past the threshold.</param>
internal readonly record struct SoftPityCurve(string Target, int MissThreshold, double Slope);

/// <summary>One class's rarity-ladder protection: its hard-pity rungs and its soft-pity curve.</summary>
/// <param name="Source">The class this ladder belongs to.</param>
/// <param name="HardPity">
/// The rungs, in the order the document lists them. Always at least one — a class that authors this
/// shape authors a hard pity, which is mandatory everywhere.
/// </param>
/// <param name="SoftPity">
/// The curve, or <see langword="null"/> where the document authors an explicit null: two of the five
/// classes are dense enough not to need one.
/// </param>
internal readonly record struct PityLadder(
    SourceClass Source, IReadOnlyList<HardPityStep> HardPity, SoftPityCurve? SoftPity);

/// <summary>The standing anti-brick guarantee, as the document authors it.</summary>
/// <remarks>
/// Keyless on purpose: no design document authors a number for it, so the block states only whether
/// the rule is on and which category it forces. "By the end of Stage 2" is prose, and the resolver
/// carries it as a named constant rather than a dial nobody authored.
/// </remarks>
/// <param name="Enabled">Whether the guarantee stands.</param>
/// <param name="ForceCategory">The category one option is forced into when it fires.</param>
internal readonly record struct SustainAntiBrickRule(bool Enabled, PerkCategory ForceCategory);

/// <summary>The five <c>DRAFT</c> rules' authored dials.</summary>
/// <param name="LegendaryPityDraftNumber">
/// The draft ordinal that is forced to carry a Legendary — the authored number names the forced
/// draft directly, so it is the rung's own N and takes no correction.
/// </param>
/// <param name="SustainAntiBrick">The standing anti-brick guarantee.</param>
/// <param name="ConsecutiveDraftsWithoutAboveCommon">
/// How many all-Common drafts pass before the <em>next</em> one is floored — so the forced draft is
/// this number plus one.
/// </param>
/// <param name="QualityFloorRarityAtLeast">The band that floor forces.</param>
/// <param name="NeverDraftedWeightMultiplier">The Codex bias's fresh-pool weight multiplier.</param>
/// <param name="MaxBiasSelectedOptions">How many of a draft's options may be bias-selected.</param>
/// <param name="OwnedUpgradeBias">
/// The per-option chance of drawing from the owned-but-not-maxed pool. Authored inside the famine
/// block because the famine exists to catch the runs this roll misses.
/// </param>
/// <param name="ConsecutiveDraftsWithoutOwnedUpgrade">
/// How many upgrade-free drafts pass before the <em>next</em> one is forced — the forced draft is
/// this number plus one, exactly as the quality floor's is.
/// </param>
internal readonly record struct DraftRule(
    int LegendaryPityDraftNumber,
    SustainAntiBrickRule SustainAntiBrick,
    int ConsecutiveDraftsWithoutAboveCommon,
    PerkRarity QualityFloorRarityAtLeast,
    double NeverDraftedWeightMultiplier,
    int MaxBiasSelectedOptions,
    double OwnedUpgradeBias,
    int ConsecutiveDraftsWithoutOwnedUpgrade);

/// <summary>The chest-pick minigame's authored guarantee.</summary>
/// <param name="ChestCount">
/// How many chests are offered. The reward table authors the same number as its row count, and the
/// resolver reconciles the two rather than trusting either alone.
/// </param>
/// <param name="GoldTierChests">
/// How many of them are the top tier. Descriptive, and deliberately not branched on: the guarantee
/// forces the single highest tier, which satisfies the rule for any positive count of gold chests.
/// </param>
/// <param name="GuaranteeAfterConsecutiveMisses">
/// The chest pick, counted from the last time the guarantee was satisfied, that is forced onto the
/// top tier. The N-th pick is the forced one.
/// </param>
internal readonly record struct ChestPickRule(
    int ChestCount, int GoldTierChests, int GuaranteeAfterConsecutiveMisses);

/// <summary>The one rule for drawing a class table against a rarity floor.</summary>
/// <remarks>
/// Read and carried but not branched on, deliberately: <see cref="RarityFloorRenormalisation"/> has
/// one member and the schema pins <see cref="CountersAdvanceNormally"/> as a constant, so there is
/// no authorable value the engine could be ignoring. What the reader does do is <em>refuse</em> a
/// renormalisation token it does not implement instead of falling back to the one it does — a
/// second member is therefore a code edit, and the branch has to land in the same commit.
/// </remarks>
/// <param name="Renormalisation">How the surviving weights are rescaled.</param>
/// <param name="CountersAdvanceNormally">
/// Whether a floored draw still advances and resets counters exactly as an unfloored one does.
/// <c>true</c> as shipped, and the reason a forced draw is expressible as a floored one.
/// </param>
internal readonly record struct RarityFloorRule(
    RarityFloorRenormalisation Renormalisation, bool CountersAdvanceNormally);
