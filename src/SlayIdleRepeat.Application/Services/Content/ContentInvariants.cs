using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The rules no single schema can state, because they hold <em>between</em> files.
/// </summary>
/// <remarks>
/// <para>
/// A JSON Schema can express only some of these on its own (unknown ids, out-of-range values), and
/// gets duplicate ids only within one array. Orphaned references, missing icons, duplicate ids
/// across files, and every "these two files must agree" rule the design docs state live here.
/// </para>
/// <para>
/// The reference rules are derived from the schemas, not guessed from the data. A schema
/// <c>pattern</c> such as <c>^PET_[A-Z0-9_]+$</c> defines an id space; wherever that pattern
/// governs an <c>id</c> member the value is a <em>declaration</em>, and wherever the same pattern
/// governs anything else the value is a <em>reference</em> that must resolve. Inferring id spaces
/// from the shape of the strings instead would fire on lookalikes like <c>BOSS_KILL</c> and
/// <c>BOTH_ROUNDS</c>, and a rule that cries wolf is a rule somebody switches off.
/// </para>
/// </remarks>
public static partial class ContentInvariants
{
    private const string IdMemberName = "id";
    private const string IconMemberName = "icon";
    private const string LocaleDirectory = "loc/";
    private const string StringsMemberName = "strings";

    /// <summary>
    /// Perk ids <c>tuning/calibration_builds.json</c> references from its <c>draftPriority</c> rows
    /// ahead of the perk that authors them — a known, dated forward reference, not a defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>calibration_builds.json</c>'s five archetype rows were written with full
    /// <c>draftPriority</c> lists while <c>content/perks/</c> was still empty, so every string in
    /// them validated by shape alone until the first perk was authored. The perk rework rewrote
    /// four of the five rows against the catalogue that now exists; what is left is the pet
    /// archetype, whose perks belong to a system nobody has built.
    /// </para>
    /// <para>
    /// Self-expiring, entry by entry: <c>CheckIdSpaces</c> only consults this set for a value that
    /// is already otherwise-orphaned, so the day a listed id is authored, that reference resolves
    /// on its own and the entry sits here inert. A test asserts every entry is still reached by a
    /// live reference, so a name that stops being referenced — or gets authored and silently drops
    /// out of the failure list — is caught rather than rotting.
    /// </para>
    /// <para>
    /// Scoped to <c>calibration_builds.json</c>'s <c>draftPriority</c> arrays specifically, not a
    /// bare id allowlist — an orphaned <c>PK_</c> reference appearing anywhere else in the content
    /// set is exactly the authoring mistake this file exists to catch, and this exemption must not
    /// swallow it.
    /// </para>
    /// </remarks>
    private const string CalibrationBuildsDocument = "tuning/calibration_builds.json";

    /// <summary>
    /// The exemption set itself, exposed so a test can assert its self-expiry: every entry must
    /// still be an id that is BOTH referenced from <see cref="CalibrationBuildsDocument"/>'s
    /// <c>draftPriority</c> arrays AND absent from <c>content/perks/perks.json</c>.
    /// </summary>
    public static IReadOnlyCollection<string> KnownForwardPerkReferences { get; } = new HashSet<string>(
        StringComparer.Ordinal)
    {
        // The pet archetype's three pet-specific perks, and nothing else. Every other archetype row
        // now names perks the reworked catalogue actually authors; these three cannot, because no
        // pet perk is authored anywhere and the pet system itself is a later milestone's. Naming a
        // real perk in their place would quietly turn ARCH_PET into a second generic archetype,
        // which is a worse answer than a forward reference that says what it is waiting for.
        "PK_PACK_LEADER", "PK_SYMBIOSIS", "PK_ECHO",
    };

    /// <summary>Every <c>path#/pointer</c> the declared cross-file rules have looked up so far.</summary>
    /// <remarks>
    /// A rule whose reference no longer resolves is a rule that has silently stopped holding.
    /// Exposed so a test can assert every reference still resolves against the real data set.
    /// Populated by running <see cref="Check"/>.
    /// </remarks>
    public static IReadOnlyList<string> DeclaredRuleReferences => DeclaredRules.References;

    /// <summary>
    /// Every embedded effect that has validated against <c>schema/effect.schema.json</c>, as
    /// <c>path#/pointer</c> — the subject-set floor a test asserts against.
    /// </summary>
    /// <remarks>
    /// The subjects are discovered structurally rather than from a list of content types, so the
    /// rule cannot say how many effects it ought to have seen; a walk that matched nothing would
    /// pass exactly as loudly as one that validated every boss mechanic in the repository.
    /// </remarks>
    public static IReadOnlyList<string> ValidatedEmbeddedEffects => DeclaredRules.ValidatedEmbeddedEffects;

    /// <summary>The effect vocabulary schema — the authority the embedded-effect rule validates against.</summary>
    public static string EffectSchemaPath => DeclaredRules.EffectSchemaPath;

    /// <summary>
    /// Every cross-file rule, over the merged, schema-valid document set and the schema set the
    /// vocabulary rules read.
    /// </summary>
    /// <remarks>
    /// One overload, deliberately: a shorter form with no schema set would make the embedded-effect
    /// rule validate against nothing, manufacturing a <c>MissingSchema</c> finding for perfectly
    /// valid content. Nothing calls a shorter form; the loader always passes its schemas.
    /// </remarks>
    /// <param name="documents">Data documents by snapshot-relative path. Schemas excluded.</param>
    /// <param name="patternBindings">
    /// Where each schema <c>pattern</c> governed a string, per document — the trace the reference
    /// rules are derived from.
    /// </param>
    /// <param name="schemas">
    /// The parsed schema set. Only the embedded-effect rule reads it, and only for
    /// <c>schema/effect.schema.json</c>: an effect is embedded in the content that owns it, so the
    /// one file that states the effect op-to-key partition has to be applied to those embedded
    /// copies from here — an owning schema can neither <c>$ref</c> it nor restate it.
    /// </param>
    /// <param name="options">Which gates to run.</param>
    public static IReadOnlyList<ContentIssue> Check(
        IReadOnlyDictionary<string, ContentValue> documents,
        IReadOnlyDictionary<string, IReadOnlyList<PatternBinding>> patternBindings,
        IReadOnlyDictionary<string, ContentValue> schemas,
        ContentLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(patternBindings);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<ContentIssue>();

        CheckIdSpaces(patternBindings, issues);
        CheckIdUniqueness(documents, issues);
        CheckIcons(documents, issues);
        CheckLocaleParity(documents, issues);
        CheckLocaleKeyReferences(documents, issues);
        DeclaredRules.Check(documents, schemas, issues);

        if (options.ShippingBuild)
        {
            CheckTranslationSentinels(documents, issues);
        }

        return issues;
    }

    /// <summary>The sentinel a German value carries until a human has translated it.</summary>
    public const string TranslationSentinel = "##TODO_DE##";

    /// <summary>
    /// Fails a shipping build while any locale value still carries the untranslated sentinel — it
    /// only runs for <see cref="ContentLoadOptions.ShippingBuild"/> because every DE value is a
    /// sentinel today, and running it always would fail every dev build by design.
    /// </summary>
    private static void CheckTranslationSentinels(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        foreach (var (path, root) in documents
                     .Where(d => d.Key.StartsWith(LocaleDirectory, StringComparison.Ordinal))
                     .OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (!root.TryGetMember(StringsMemberName, out var strings))
            {
                continue;
            }

            foreach (var key in strings!.MemberNames)
            {
                strings.TryGetMember(key, out var value);

                if (value!.Kind == ContentValueKind.Text &&
                    value.AsText().Contains(TranslationSentinel, StringComparison.Ordinal))
                {
                    issues.Add(new ContentIssue(
                        ContentIssueCode.LocalisationMismatch,
                        $"{path}#/{StringsMemberName}/{key}",
                        $"still carries the {TranslationSentinel} sentinel, so no human has " +
                        "translated it. 16 D20: nothing machine-translated reaches a player, and a " +
                        "build that ships must fail while any sentinel remains."));
                }
            }
        }
    }

    /// <summary>Duplicate ids, over every id-bearing collection.</summary>
    /// <remarks>
    /// <para>
    /// <c>uniqueItems</c> cannot carry this: it compares <em>whole objects</em>, so
    /// <c>{"id":"CROWNS","scope":"GATE"}</c> beside <c>{"id":"CROWNS","scope":"META"}</c> passes
    /// every schema in the repository while declaring the same currency twice.
    /// </para>
    /// <para>
    /// Unauthorised ids are exempt, not duplicates. Several collections are authored with most ids
    /// <c>null</c> because the design docs have not named them yet. Treating those nulls as equal
    /// values would report duplicate ids on data that is exactly as the design docs left it.
    /// </para>
    /// </remarks>
    private static void CheckIdUniqueness(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        foreach (var (path, root) in documents)
        {
            WalkForDuplicateIds(root, path, string.Empty, issues);
        }
    }

    /// <summary>
    /// The member names that identify an entry. Not a guess: these are the identity fields the
    /// shipped collections actually use.
    /// </summary>
    private static readonly string[] IdentityMemberNames = ["id", "name", "profile", "chapter", "day", "slot"];

    private static void WalkForDuplicateIds(
        ContentValue value, string documentPath, string pointer, List<ContentIssue> issues)
    {
        if (value.Kind == ContentValueKind.Array)
        {
            var entries = value.Items.Where(i => i.Kind == ContentValueKind.Object).ToArray();

            if (entries.Length > 1)
            {
                foreach (var field in IdentityMemberNames.Where(f => entries.All(e => e.TryGetMember(f, out _))))
                {
                    // Grouped by VALUE, not ToString(): identity fields include numeric ones
                    // (chapter, day, slot), whose ContentValue.ToString() is scale-preserving
                    // while ContentValue.Equals is scale-independent — {"chapter": 3} beside
                    // {"chapter": 3.0} would declare the same chapter twice and slip through a
                    // ToString()-keyed group. It also fails for objects, whose ToString() emits
                    // member names only, so two structurally different objects in an identity slot
                    // would report as duplicates of each other.
                    var duplicates = entries
                        .Select(e => { e.TryGetMember(field, out var id); return id!; })
                        .Where(id => !id.IsUnauthorised)
                        .GroupBy(id => id)
                        .Where(g => g.Count() > 1);

                    foreach (var duplicate in duplicates)
                    {
                        issues.Add(new ContentIssue(
                            ContentIssueCode.DuplicateId, $"{documentPath}#{pointer}",
                            $"declares {field} {duplicate.Key} {duplicate.Count()} times. uniqueItems " +
                            "compares whole objects and cannot see this."));
                    }
                }
            }

            for (var i = 0; i < value.Items.Count; i++)
            {
                WalkForDuplicateIds(value.Items[i], documentPath, $"{pointer}/{i}", issues);
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        foreach (var name in value.MemberNames)
        {
            value.TryGetMember(name, out var member);
            WalkForDuplicateIds(member!, documentPath, $"{pointer}/{name}", issues);
        }
    }

    /// <summary>Duplicate ids and orphaned references, over the id spaces the schemas define.</summary>
    private static void CheckIdSpaces(
        IReadOnlyDictionary<string, IReadOnlyList<PatternBinding>> patternBindings,
        List<ContentIssue> issues)
    {
        var all = patternBindings
            .SelectMany(entry => entry.Value)
            .ToArray();

        foreach (var space in all.GroupBy(b => b.Pattern, StringComparer.Ordinal).Where(g => IsIdNamespace(g.Key)))
        {
            var declarations = space
                .Where(b => string.Equals(b.MemberName, IdMemberName, StringComparison.Ordinal))
                .ToArray();

            if (declarations.Length == 0)
            {
                // Not an id space — just a validated string shape (a loc key, a colour, a date).
                continue;
            }

            foreach (var duplicate in declarations
                         .GroupBy(d => d.Value, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.DuplicateId,
                    string.Join(", ", duplicate.Select(d => d.Location).OrderBy(l => l, StringComparer.Ordinal)),
                    $"the id '{duplicate.Key}' is declared {duplicate.Count()} times."));
            }

            var declared = declarations.Select(d => d.Value).ToHashSet(StringComparer.Ordinal);

            foreach (var reference in space.Where(b =>
                         !string.Equals(b.MemberName, IdMemberName, StringComparison.Ordinal) &&
                         !declared.Contains(b.Value)))
            {
                if (reference.Location.StartsWith(CalibrationBuildsDocument, StringComparison.Ordinal) &&
                    reference.Location.Contains("/draftPriority/", StringComparison.Ordinal) &&
                    KnownForwardPerkReferences.Contains(reference.Value))
                {
                    // Known, dated forward reference — see KnownForwardPerkReferences' remarks.
                    continue;
                }

                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, reference.Location,
                    $"references '{reference.Value}', which matches the id pattern " +
                    $"{reference.Pattern} but is declared nowhere in the content set."));
            }
        }
    }

    /// <summary>True when a schema <c>pattern</c> pins a literal prefix, e.g. <c>^WID_[A-Z0-9_]+$</c>.</summary>
    /// <remarks>
    /// A prefix is what makes a pattern a <em>namespace</em> rather than a <em>shape</em>.
    /// <c>^[A-Z][A-Z0-9_]*$</c> describes "an uppercase constant" and is shared by unrelated
    /// vocabularies (stat names, currency ids, category names) — reading it as one id space would
    /// demand they all resolve against each other, which is nonsense. Two literal characters is the
    /// smallest prefix that can plausibly name a family.
    /// </remarks>
    private static bool IsIdNamespace(string pattern)
    {
        if (!pattern.StartsWith('^'))
        {
            return false;
        }

        var literal = 0;
        foreach (var character in pattern.AsSpan(1))
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                literal++;
                continue;
            }

            break;
        }

        return literal >= 2;
    }

    /// <summary>
    /// Missing icons. There is no icon manifest in the data yet, so the rule is stated against the
    /// collection itself: where a collection's entries carry icons, an entry without one is an
    /// entry that will render as a blank square.
    /// </summary>
    private static void CheckIcons(IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        foreach (var (path, root) in documents)
        {
            WalkCollections(root, path, string.Empty, issues);
        }
    }

    private static void WalkCollections(
        ContentValue value, string documentPath, string pointer, List<ContentIssue> issues)
    {
        if (value.Kind == ContentValueKind.Array)
        {
            var entries = value.Items
                .Where(i => i.Kind == ContentValueKind.Object && i.TryGetMember(IdMemberName, out _))
                .ToArray();

            if (entries.Any(HasIcon))
            {
                for (var i = 0; i < value.Items.Count; i++)
                {
                    var item = value.Items[i];
                    if (item.Kind == ContentValueKind.Object &&
                        item.TryGetMember(IdMemberName, out var id) &&
                        !HasIcon(item))
                    {
                        issues.Add(new ContentIssue(
                            ContentIssueCode.MissingIcon, $"{documentPath}#{pointer}/{i}",
                            $"'{id}' declares no icon, but its siblings in this collection do. " +
                            "14 §6 fails the build on a missing icon."));
                    }
                }
            }

            for (var i = 0; i < value.Items.Count; i++)
            {
                WalkCollections(value.Items[i], documentPath, $"{pointer}/{i}", issues);
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        foreach (var name in value.MemberNames)
        {
            value.TryGetMember(name, out var member);
            WalkCollections(member!, documentPath, $"{pointer}/{name}", issues);
        }
    }

    private static bool HasIcon(ContentValue entry) =>
        entry.TryGetMember(IconMemberName, out var icon) &&
        icon!.Kind == ContentValueKind.Text &&
        icon.AsText().Length > 0;

    /// <summary>
    /// <c>en.json</c> and <c>de.json</c> must carry an identical key set — a key in one and not the
    /// other is a string that will render as its own key in front of a player.
    /// </summary>
    private static void CheckLocaleParity(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var locales = documents
            .Where(d => d.Key.StartsWith(LocaleDirectory, StringComparison.Ordinal))
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .ToArray();

        if (locales.Length < 2)
        {
            return;
        }

        var reference = Keys(locales[0].Value);

        foreach (var (path, root) in locales.Skip(1))
        {
            var keys = Keys(root);

            foreach (var missing in reference.Except(keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.LocalisationMismatch, $"{path}#/{StringsMemberName}",
                    $"'{missing}' is in {locales[0].Key} and absent here. It would render as its own key."));
            }

            foreach (var extra in keys.Except(reference, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.LocalisationMismatch, $"{path}#/{StringsMemberName}/{extra}",
                    $"'{extra}' is here and absent from {locales[0].Key}."));
            }
        }

        static IReadOnlyList<string> Keys(ContentValue root) =>
            root.TryGetMember(StringsMemberName, out var strings) ? strings!.MemberNames : [];
    }

    /// <summary>
    /// Every <c>loc.*</c> key the content names must exist in <b>every</b> locale, and every string
    /// the locales carry must be named by something.
    /// </summary>
    /// <remarks>
    /// A <c>displayName</c> that resolves in <c>en</c> and not in <c>de</c> renders as its own key
    /// in front of a German player — the same visible defect as a missing icon, which is why it
    /// carries the same code. The reverse direction matters too: an unreferenced string is a
    /// translation somebody will pay for twice.
    /// </remarks>
    private static void CheckLocaleKeyReferences(
        IReadOnlyDictionary<string, ContentValue> documents, List<ContentIssue> issues)
    {
        var locales = documents
            .Where(d => d.Key.StartsWith(LocaleDirectory, StringComparison.Ordinal))
            .ToDictionary(
                d => d.Key,
                d => d.Value.TryGetMember(StringsMemberName, out var strings)
                    ? strings!.MemberNames.ToHashSet(StringComparer.Ordinal)
                    : [],
                StringComparer.Ordinal);

        if (locales.Count == 0)
        {
            return;
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, root) in documents.Where(d => !d.Key.StartsWith(LocaleDirectory, StringComparison.Ordinal)))
        {
            WalkLocaleKeys(root, path, string.Empty, locales, referenced, issues);
        }

        foreach (var (path, keys) in locales.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            foreach (var orphan in keys.Except(referenced).OrderBy(k => k, StringComparer.Ordinal))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanedReference, $"{path}#/{StringsMemberName}/{orphan}",
                    "is a string nothing in the content set names. Either the data that used it was " +
                    "removed, or the string was written ahead of the data that will."));
            }
        }
    }

    private static void WalkLocaleKeys(
        ContentValue value,
        string documentPath,
        string pointer,
        IReadOnlyDictionary<string, HashSet<string>> locales,
        HashSet<string> referenced,
        List<ContentIssue> issues)
    {
        if (value.Kind == ContentValueKind.Array)
        {
            for (var i = 0; i < value.Items.Count; i++)
            {
                WalkLocaleKeys(value.Items[i], documentPath, $"{pointer}/{i}", locales, referenced, issues);
            }

            return;
        }

        if (value.Kind == ContentValueKind.Text)
        {
            var key = value.AsText();
            if (!LocaleKey().IsMatch(key))
            {
                return;
            }

            referenced.Add(key);

            foreach (var path in locales.Where(l => !l.Value.Contains(key))
                         .Select(l => l.Key)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.MissingIcon, $"{documentPath}#{pointer}",
                    $"names the string '{key}', which {path} does not carry. It would render as its " +
                    "own key in front of a player."));
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        foreach (var name in value.MemberNames.Where(n => !string.Equals(n, "_doc", StringComparison.Ordinal)))
        {
            value.TryGetMember(name, out var member);
            WalkLocaleKeys(member!, documentPath, $"{pointer}/{name}", locales, referenced, issues);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^loc(\.[a-z0-9_]+)+$")]
    private static partial System.Text.RegularExpressions.Regex LocaleKey();
}
