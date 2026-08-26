using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// One authored condition tree, read out of any content document as the DSL shape the evaluator
/// runs: <c>{"fn": …, "op": …, "value": …}</c>, or one of the combinators <c>all · any · not</c>.
/// </summary>
/// <remarks>
/// <para>
/// One reader rather than one per document, because the conditional standing-effect bucket made a
/// gate a legal authored shape in three places at once — perk effects, set bonuses and the affix
/// pool — and three transcriptions of the same tree grammar would be three chances for a document
/// to author a gate the game reads differently.
/// </para>
/// <para>
/// Narrow on the same rule as every effect reader: the keys a comparison may carry are enumerated
/// and any other is refused, so a misspelled or borrowed key fails loudly instead of silently
/// changing what the document says. The four node shapes are told apart by which key is present,
/// exactly as the schema's <c>oneOf</c> does it — a comparison is the shape with no combinator key,
/// not a default, so a node carrying none of the four is refused rather than read as an
/// unsatisfiable comparison.
/// </para>
/// </remarks>
internal static class ConditionContentReader
{
    /// <summary>The keys one authored comparison term may carry.</summary>
    private static readonly string[] KnownTermKeys = ["fn", "op", "value", "statusId", "category"];

    /// <summary>Reads the tree at <paramref name="pointer"/>.</summary>
    /// <param name="content">The version-stamped snapshot being read.</param>
    /// <param name="pointer">Where the tree is authored.</param>
    /// <param name="what">
    /// Whose condition it is, in the reader's terms — e.g. <c>"a perk effect's condition"</c> —
    /// named in the unknown-key refusal so the failure reads as a sentence about the document.
    /// </param>
    /// <exception cref="InvalidTunableException">A node or term is authored in a shape this reader does not map.</exception>
    internal static EffectCondition Read(ContentSnapshot content, string pointer, string what)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(what);

        var node = content.Read(pointer);

        if (node.MemberNames.Contains("all", StringComparer.Ordinal))
        {
            RequireOnlyKey(node, "all", pointer, what);
            return EffectCondition.All(ReadOperands(content, pointer + "/all", what));
        }

        if (node.MemberNames.Contains("any", StringComparer.Ordinal))
        {
            RequireOnlyKey(node, "any", pointer, what);
            return EffectCondition.Any(ReadOperands(content, pointer + "/any", what));
        }

        if (node.MemberNames.Contains("not", StringComparer.Ordinal))
        {
            RequireOnlyKey(node, "not", pointer, what);
            return EffectCondition.Not(Read(content, pointer + "/not", what));
        }

        return EffectCondition.Of(ReadTerm(content, pointer, what));
    }

    /// <summary>
    /// Reads the tree at <paramref name="pointer"/> as a <b>gear gate</b> — a condition on a
    /// standing gear grant, which must read exactly one contextual subject.
    /// </summary>
    /// <remarks>
    /// The two refusals are standing-bucket policy, which is why they live on this entry point and
    /// not in <see cref="Read"/>: a trigger-time context genuinely carries both subjects, so a
    /// both-subject tree is a legal authored shape for a triggered effect. A standing gear grant is
    /// different on both sides — an <b>ambient</b> tree would evaluate in battle and then throw out
    /// of the hero screen's strict aggregation, and a <b>both-subject</b> tree could never hold,
    /// because the per-pair re-aggregation hands each side exactly one subject (the source a
    /// target, the defender an attacker): the inverted-range shape, wearing a gate's clothes.
    /// </remarks>
    /// <param name="content">The version-stamped snapshot being read.</param>
    /// <param name="pointer">Where the tree is authored.</param>
    /// <param name="what">Whose condition it is, in the reader's terms.</param>
    /// <exception cref="InvalidTunableException">The tree is malformed, ambient, or reads both subjects.</exception>
    internal static EffectCondition ReadContextGate(ContentSnapshot content, string pointer, string what)
    {
        var tree = Read(content, pointer, what);

        return ConditionSubjects.Of(tree) switch
        {
            { ReadsTarget: false, ReadsAttacker: false } => throw new InvalidTunableException(
                pointer,
                "The tree reads neither the current target nor the attacker, so it is not a " +
                "context gate: an ambient condition on standing gear evaluates in battle and then " +
                "throws out of the hero screen's strict aggregation. Gate gear on a contextual " +
                "subject, or leave it ungated."),

            { ReadsTarget: true, ReadsAttacker: true } => throw new InvalidTunableException(
                pointer,
                "The tree reads both the current target and the attacker, and no evaluation " +
                "context of the standing bucket ever carries both — the gate could never hold, so " +
                "the effect it gates could never fire."),

            _ => tree,
        };
    }

    private static EffectCondition[] ReadOperands(ContentSnapshot content, string pointer, string what)
    {
        var operands = content.Read(pointer);
        var read = new EffectCondition[operands.Items.Count];

        for (var i = 0; i < read.Length; i++)
        {
            read[i] = Read(content, pointer + "/" + AuthoredToken.Render(i), what);
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
    private static ConditionTerm ReadTerm(ContentSnapshot content, string pointer, string what)
    {
        RequireKnownKeys(content, pointer, what);

        var value = content.Read(pointer + "/value");

        // Refused at read time rather than left for the evaluator: the bucket moved a gated
        // tree's first evaluation from the hero screen to the first pair context mid-fight, so a
        // malformed value that used to fail at composition would fail out of an attack resolution.
        if (value.Kind is not (ContentValueKind.Number or ContentValueKind.Boolean) &&
            !(value.Kind == ContentValueKind.Array && value.Items.Count == 2))
        {
            throw new InvalidTunableException(
                pointer + "/value",
                "A comparison's value is a number, a boolean, or a two-element array (the 'between' " +
                "form) — nothing else has an authored reading, and loading one anyway would defer " +
                "the failure to the middle of a battle.");
        }

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

        // The exact-lower-case check keeps the one-spelling-per-member rule the shared token
        // reader enforces everywhere else: without it, upper-casing before the parse would let
        // every casing of one member load.
        return string.Equals(authored, authored.ToLowerInvariant(), StringComparison.Ordinal) &&
               AuthoredToken.TryParse<ConditionComparator>(authored.ToUpperInvariant(), out var parsed)
            ? parsed
            : throw new InvalidTunableException(
                pointer,
                $"'{authored}' is not a comparator. The authored set is eq, neq, lt, lte, gt, gte, " +
                "between — lower-case, unlike every other token in the DSL.");
    }

    /// <summary>Refuses a sibling key beside a combinator, which would otherwise be silently dropped.</summary>
    private static void RequireOnlyKey(ContentValue node, string key, string pointer, string what)
    {
        foreach (var name in node.MemberNames)
        {
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                throw new InvalidTunableException(
                    pointer + "/" + name,
                    $"'{name}' is a key this reader does not map beside '{key}', so authoring it " +
                    $"would change nothing about the effect the game builds. A combinator node of " +
                    $"{what} carries its one combinator key and nothing else.");
            }
        }
    }

    private static string? Word(ContentSnapshot content, string pointer) =>
        content.IsAuthorised(pointer) ? content.ReadText(pointer) : null;

    /// <summary>Refuses an authored key this reader does not map.</summary>
    private static void RequireKnownKeys(ContentSnapshot content, string pointer, string what)
    {
        foreach (var name in content.Read(pointer).MemberNames)
        {
            if (!KnownTermKeys.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidTunableException(
                    pointer + "/" + name,
                    $"'{name}' is a key this reader does not map, so authoring it would change nothing " +
                    "about the effect the game builds while changing what the document says. The keys " +
                    $"{what} may carry are {string.Join(", ", KnownTermKeys)}; teaching it a new one is " +
                    "a deliberate edit here.");
            }
        }
    }
}
