using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// A perk tier's authored effects, read out of <c>content/perks/perks.json</c> as the DSL shapes the
/// interpreter runs.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The reader <c>PerkCatalogue</c> deliberately is not.</b> That type stops at
/// <see cref="PerkCatalogueEntry.TierCount"/> because the draft engine only ever chooses an id and a
/// tier. Turning an owned perk into something a fight can feel needs the tier's effect tree itself,
/// which is what this reads — and it is a separate type so the draft keeps loading a catalogue
/// whose effect data it never touches.
/// </para>
/// <para>
/// Narrow rather than lossy, on <c>SetBonusCatalogue</c>'s precedent: every key an authored perk
/// effect may carry is enumerated, and a key outside the list is refused. Without that, authoring a
/// <c>duration</c> the reader did not map would load cleanly and grant a permanent effect instead —
/// the document and the game disagreeing with everything green.
/// </para>
/// <para>
/// The ops whose keys are unreachable from a perk — <c>SUMMON</c>'s archetype pair,
/// <c>STAT_CAP_OVERRIDE</c>'s cap kind and <c>RANDOM_OUTCOME</c>'s outcome table — are absent from
/// the key set on purpose. No perk authors
/// one, and admitting a key nothing writes would be a branch no data covers.
/// </para>
/// </remarks>
internal static class PerkEffects
{
    /// <summary>The keys one authored perk effect may carry.</summary>
    private static readonly string[] KnownEffectKeys =
    [
        "id", "op", "trigger", "condition", "target", "value", "valueMode", "valueScale",
        "duration", "stacking", "tags", "stat", "toStat", "charges", "statusId", "statusTag",
        "sourceCapPct",
    ];

    /// <summary>The keys one authored trigger may carry — 18 §3's fourteen parameter shapes, unioned.</summary>
    private static readonly string[] KnownTriggerKeys =
    [
        "kind", "onlyIfWon", "everyNth", "chance", "cooldown", "threshold", "once", "interval",
        "startDelay", "phase", "tileType", "category",
    ];

    /// <summary>The keys one authored comparison term may carry.</summary>
    private static readonly string[] KnownTermKeys = ["fn", "op", "value", "statusId", "category"];

    /// <summary>The keys one authored value scale may carry.</summary>
    private static readonly string[] KnownValueScaleKeys = ["fn", "per", "cap", "statusId", "category"];

    /// <summary>The keys one authored duration may carry.</summary>
    private static readonly string[] KnownDurationKeys = ["scope", "seconds", "until"];

    /// <summary>The keys one authored stacking block may carry.</summary>
    private static readonly string[] KnownStackingKeys = ["mode", "maxStacks", "refreshOnReapply"];

    /// <summary>
    /// The effects <paramref name="perkId"/> grants at <paramref name="tier"/>, in the document's order.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <param name="perkId">The perk id.</param>
    /// <param name="tier">The owned tier, numbered from 1.</param>
    /// <returns>The tier's effects.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The catalogue carries no such perk.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The perk authors no such tier.</exception>
    /// <exception cref="InvalidTunableException">An effect is authored in a shape this reader does not map.</exception>
    internal static IReadOnlyList<EffectDefinition> Of(ContentSnapshot content, string perkId, int tier)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(perkId);

        var effectsPointer = EffectsPointer(content, perkId, tier);
        var effects = content.Read(effectsPointer);

        var read = new EffectDefinition[effects.Items.Count];

        for (var i = 0; i < read.Length; i++)
        {
            read[i] = ReadEffect(content, effectsPointer + "/" + AuthoredToken.Render(i));
        }

        return Array.AsReadOnly(read);
    }

    /// <summary>The pointer at which a perk's tier authors its effect array.</summary>
    /// <remarks>
    /// Both walks match on the authored value rather than on array position — the id for the perk,
    /// the <c>tier</c> number for the tier — so a catalogue written out of order still answers the
    /// row the caller asked for. The index is only what turns the match back into a pointer.
    /// </remarks>
    private static string EffectsPointer(ContentSnapshot content, string perkId, int tier)
    {
        var perks = content.Read(PerkCatalogue.PerksReference);

        for (var i = 0; i < perks.Items.Count; i++)
        {
            if (!perks.Items[i].TryGetMember("id", out var id) ||
                id is not { Kind: ContentValueKind.Text } ||
                !string.Equals(id.AsText(), perkId, StringComparison.Ordinal))
            {
                continue;
            }

            var perkPointer = PerkCatalogue.PerksReference + "/" + AuthoredToken.Render(i);
            var tiers = content.Read(perkPointer + "/tiers");

            for (var t = 0; t < tiers.Items.Count; t++)
            {
                if (tiers.Items[t].TryGetMember("tier", out var number) &&
                    number is { Kind: ContentValueKind.Number } &&
                    number.AsInt32() == tier)
                {
                    return perkPointer + "/tiers/" + AuthoredToken.Render(t) + "/effects";
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                "'" + perkId + "' authors no tier " + AuthoredToken.Render(tier) +
                ". 06 §1.1 numbers a perk's internal tiers from 1.");
        }

        throw new ArgumentException(
            "06 §3 authors no perk '" + perkId + "'. A perk id reaching here that the catalogue does " +
            "not carry means a Run persisted an owned-perk id this content version no longer has — a " +
            "content rollback across a live run, not a player input.",
            nameof(perkId));
    }

    private static EffectDefinition ReadEffect(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownEffectKeys, "a perk effect");

        return new EffectDefinition
        {
            Id = content.ReadText(pointer + "/id"),
            Op = AuthoredToken.Parse<EffectOp>(content, pointer + "/op", "an effect operation"),
            Trigger = Authored(content, pointer + "/trigger") ? ReadTrigger(content, pointer + "/trigger") : null,
            Condition = Authored(content, pointer + "/condition")
                ? ReadCondition(content, pointer + "/condition")
                : null,
            Target = Authored(content, pointer + "/target")
                ? AuthoredToken.Parse<EffectTarget>(content, pointer + "/target", "an effect target")
                : null,
            Value = Authored(content, pointer + "/value") ? content.ReadDouble(pointer + "/value") : null,
            ValueMode = Authored(content, pointer + "/valueMode")
                ? AuthoredToken.Parse<ValueMode>(content, pointer + "/valueMode", "a value mode")
                : null,
            ValueScale = Authored(content, pointer + "/valueScale")
                ? ReadValueScale(content, pointer + "/valueScale")
                : null,
            Duration = Authored(content, pointer + "/duration")
                ? ReadDuration(content, pointer + "/duration")
                : null,
            Stacking = Authored(content, pointer + "/stacking")
                ? ReadStacking(content, pointer + "/stacking")
                : null,
            Tags = ReadTags(content, pointer + "/tags"),
            Stat = Authored(content, pointer + "/stat") ? ReadStatSelector(content, pointer + "/stat") : null,
            ToStat = Authored(content, pointer + "/toStat")
                ? AuthoredToken.Parse<StatId>(content, pointer + "/toStat", "one of the stats")
                : null,
            Charges = Authored(content, pointer + "/charges") ? content.ReadInt32(pointer + "/charges") : null,
            StatusId = Authored(content, pointer + "/statusId") ? content.ReadText(pointer + "/statusId") : null,
            StatusTag = Authored(content, pointer + "/statusTag")
                ? new StatusTag(content.ReadText(pointer + "/statusTag"))
                : null,
            SourceCapPct = Authored(content, pointer + "/sourceCapPct")
                ? content.ReadDouble(pointer + "/sourceCapPct")
                : null,
        };
    }

    private static EffectTrigger ReadTrigger(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownTriggerKeys, "a perk effect's trigger");

        return new EffectTrigger
        {
            Kind = AuthoredToken.Parse<TriggerKind>(content, pointer + "/kind", "a trigger kind"),
            OnlyIfWon = Flag(content, pointer + "/onlyIfWon"),
            EveryNth = Count(content, pointer + "/everyNth"),
            Chance = Amount(content, pointer + "/chance"),
            Cooldown = Amount(content, pointer + "/cooldown"),
            Threshold = Amount(content, pointer + "/threshold"),
            Once = Flag(content, pointer + "/once"),
            Interval = Amount(content, pointer + "/interval"),
            StartDelay = Amount(content, pointer + "/startDelay"),
            Phase = Count(content, pointer + "/phase"),
            TileType = Word(content, pointer + "/tileType"),
            Category = Word(content, pointer + "/category"),
        };
    }

    /// <summary>One node of a condition tree: a comparison, or one of the three combinators.</summary>
    /// <remarks>
    /// The four shapes are told apart by which key is present, exactly as the schema's <c>oneOf</c>
    /// does it — a comparison is the shape with no combinator key, not a default, so a node carrying
    /// none of the four is refused rather than read as an unsatisfiable comparison.
    /// </remarks>
    private static EffectCondition ReadCondition(ContentSnapshot content, string pointer)
    {
        var node = content.Read(pointer);

        if (node.MemberNames.Contains("all", StringComparer.Ordinal))
        {
            return EffectCondition.All(ReadOperands(content, pointer + "/all"));
        }

        if (node.MemberNames.Contains("any", StringComparer.Ordinal))
        {
            return EffectCondition.Any(ReadOperands(content, pointer + "/any"));
        }

        if (node.MemberNames.Contains("not", StringComparer.Ordinal))
        {
            return EffectCondition.Not(ReadCondition(content, pointer + "/not"));
        }

        return EffectCondition.Of(ReadTerm(content, pointer));
    }

    private static EffectCondition[] ReadOperands(ContentSnapshot content, string pointer)
    {
        var operands = content.Read(pointer);
        var read = new EffectCondition[operands.Items.Count];

        for (var i = 0; i < read.Length; i++)
        {
            read[i] = ReadCondition(content, pointer + "/" + AuthoredToken.Render(i));
        }

        return read;
    }

    /// <summary>A comparison — the leaf of a condition tree.</summary>
    /// <remarks>
    /// <c>value</c> carries three shapes and the comparator does not decide which: a boolean for the
    /// four <c>*_IS_*</c> predicates, a two-element array for <c>BETWEEN</c>, a number otherwise. The
    /// authored shape is what is read, so a boolean written against a numeric comparator reaches the
    /// evaluator as the flag it is rather than as a silently coerced 1.
    /// </remarks>
    private static ConditionTerm ReadTerm(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownTermKeys, "a perk effect's condition");

        var value = content.Read(pointer + "/value");

        return new ConditionTerm
        {
            Fn = AuthoredToken.Parse<ConditionFunction>(content, pointer + "/fn", "a condition function"),
            Comparator = ReadComparator(content, pointer + "/op"),
            Value = value.Kind == ContentValueKind.Number ? value.AsDouble(pointer + "/value") : null,
            Flag = value.Kind == ContentValueKind.Boolean ? value.AsBoolean(pointer + "/value") : null,
            RangeLow = value.Kind == ContentValueKind.Array && value.Items.Count == 2
                ? value.Items[0].AsDouble(pointer + "/value/0")
                : null,
            RangeHigh = value.Kind == ContentValueKind.Array && value.Items.Count == 2
                ? value.Items[1].AsDouble(pointer + "/value/1")
                : null,
            StatusId = Word(content, pointer + "/statusId"),
            Category = Word(content, pointer + "/category"),
        };
    }

    /// <summary>
    /// The comparator, whose JSON spelling is lower-case where every other token in the DSL is
    /// <c>SCREAMING_SNAKE</c>.
    /// </summary>
    /// <remarks>
    /// Upper-cased before the parse rather than parsed case-insensitively: the shared token reader
    /// refuses a spelling that is not exactly a member's name, precisely so two spellings of one
    /// member cannot both load, and relaxing that for this one key would relax it for every key it
    /// reads. What is authored lower-case is one closed vocabulary, and this is where the mapping is
    /// stated.
    /// </remarks>
    private static ConditionComparator ReadComparator(ContentSnapshot content, string pointer)
    {
        var authored = content.ReadText(pointer);

        return AuthoredToken.TryParse<ConditionComparator>(authored.ToUpperInvariant(), out var parsed)
            ? parsed
            : throw new InvalidTunableException(
                pointer,
                $"'{authored}' is not a comparator. The authored set is eq, neq, lt, lte, gt, gte, " +
                "between — lower-case, unlike every other token in the DSL.");
    }

    private static ValueScale ReadValueScale(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownValueScaleKeys, "a perk effect's value scale");

        return new ValueScale
        {
            Fn = AuthoredToken.Parse<ConditionFunction>(content, pointer + "/fn", "a condition function"),
            Per = content.ReadDouble(pointer + "/per"),
            Cap = Count(content, pointer + "/cap"),
            StatusId = Word(content, pointer + "/statusId"),
            Category = Word(content, pointer + "/category"),
        };
    }

    private static EffectDuration ReadDuration(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownDurationKeys, "a perk effect's duration");

        return new EffectDuration
        {
            Scope = AuthoredToken.Parse<DurationScope>(content, pointer + "/scope", "a duration scope"),
            Seconds = Amount(content, pointer + "/seconds"),
            Until = Authored(content, pointer + "/until")
                ? AuthoredToken.Parse<DurationTerminator>(content, pointer + "/until", "a duration terminator")
                : null,
        };
    }

    private static EffectStacking ReadStacking(ContentSnapshot content, string pointer)
    {
        RequireKnownKeys(content, pointer, KnownStackingKeys, "a perk effect's stacking block");

        return new EffectStacking
        {
            Mode = AuthoredToken.Parse<StackingMode>(content, pointer + "/mode", "a stacking mode"),
            MaxStacks = Count(content, pointer + "/maxStacks"),
            RefreshOnReapply = Flag(content, pointer + "/refreshOnReapply"),
        };
    }

    /// <summary>
    /// The stat key, which carries two vocabularies: one of the stats, or one of the two selectors
    /// that name a set of them.
    /// </summary>
    private static StatSelector ReadStatSelector(ContentSnapshot content, string pointer)
    {
        var token = content.ReadText(pointer);

        if (string.Equals(token, "ALL_COMBAT", StringComparison.Ordinal))
        {
            return StatSelector.AllCombat;
        }

        return string.Equals(token, "HIGHEST_PCT_BONUS", StringComparison.Ordinal)
            ? StatSelector.HighestPctBonus
            : StatSelector.Of(AuthoredToken.Parse<StatId>(content, pointer, "one of the stats"));
    }

    private static IReadOnlyList<string> ReadTags(ContentSnapshot content, string pointer)
    {
        if (!Authored(content, pointer))
        {
            return [];
        }

        var array = content.Read(pointer);
        var tags = new string[array.Items.Count];

        for (var i = 0; i < tags.Length; i++)
        {
            tags[i] = array.Items[i].AsText(pointer + "/" + AuthoredToken.Render(i));
        }

        return Array.AsReadOnly(tags);
    }

    private static bool Authored(ContentSnapshot content, string pointer) => content.IsAuthorised(pointer);

    private static double? Amount(ContentSnapshot content, string pointer) =>
        Authored(content, pointer) ? content.ReadDouble(pointer) : null;

    private static int? Count(ContentSnapshot content, string pointer) =>
        Authored(content, pointer) ? content.ReadInt32(pointer) : null;

    private static bool? Flag(ContentSnapshot content, string pointer) =>
        Authored(content, pointer) ? content.ReadBoolean(pointer) : null;

    private static string? Word(ContentSnapshot content, string pointer) =>
        Authored(content, pointer) ? content.ReadText(pointer) : null;

    /// <summary>Refuses an authored key this reader does not map.</summary>
    private static void RequireKnownKeys(
        ContentSnapshot content, string pointer, IReadOnlyList<string> known, string what)
    {
        foreach (var name in content.Read(pointer).MemberNames)
        {
            if (!known.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidTunableException(
                    pointer + "/" + name,
                    $"'{name}' is a key this reader does not map, so authoring it would change nothing " +
                    "about the effect the game builds while changing what the document says. The keys " +
                    $"{what} may carry are {string.Join(", ", known)}; teaching it a new one is a " +
                    "deliberate edit here.");
            }
        }
    }
}
