using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §6 — "Rules that are not enforced are suggestions." These are the rules that
/// keep the rules honest: a skipped, commented-out or placeholder architecture test is
/// a rule that has been quietly switched off, which is the exact failure mode the
/// enforcement section exists to prevent.
/// </summary>
public sealed class SuiteIntegrityTests
{
    /// <summary>A citation of a design-document section, or of the milestone task X-02.</summary>
    /// <remarks>
    /// The document number is captured so it can be RESOLVED against <c>game-design/</c>.
    /// Without that, the pattern accepts <c>99 §9</c> — and since <c>TreatWarningsAsErrors</c>
    /// already forces CS1591, "every rule cites its doc section" would collapse to "every
    /// summary contains a §", which is not a claim worth a test.
    /// </remarks>
    private static readonly Regex DocumentCitation = new(
        @"(\b(?<doc>\d{1,2})\b\W{0,3}§\s*\d)|(\bX-02\b)",
        RegexOptions.Compiled);

    /// <summary>The design-document numbers that exist, read off <c>game-design/NN_*.md</c>.</summary>
    private static readonly Lazy<IReadOnlyCollection<int>> DesignDocumentNumbers = new(() =>
    {
        var directory = Path.Combine(RepoLayout.RepoRoot, "game-design");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"'{directory}' does not exist, so a citation cannot be resolved against anything and this " +
                "rule would accept every string containing a §.");
        }

        var numbers = Directory.GetFiles(directory, "*.md")
            .Select(Path.GetFileName)
            .Select(name => Regex.Match(name!, @"^(?<n>\d{1,2})_"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();

        return numbers.Count > 0
            ? numbers
            : throw new InvalidOperationException(
                $"No 'NN_*.md' design document found under {directory}. Resolving a citation against an empty " +
                "set would accept nothing, or — if this rule were written the other way round — everything.");
    });

    /// <summary>Placeholders that look like a rule but assert nothing.</summary>
    private static readonly (Regex Pattern, string Reason)[] Placeholders =
    {
        (new Regex(@"Assert\s*\.\s*True\s*\(\s*true\s*\)", RegexOptions.Compiled), "Assert.True(true) asserts nothing"),
        (new Regex(@"\bSkip\s*=", RegexOptions.Compiled), "a skipped test is a disabled rule"),
        (new Regex(@"#if\s+false", RegexOptions.Compiled), "a compiled-out rule is a disabled rule"),
    };

    /// <summary>
    /// `23` §6 / M0-08 acceptance criterion 2 — the meta-rule: no test in this suite
    /// carries a non-empty <c>Skip</c>. A rule that is waiting for a later milestone must
    /// pass vacuously over an empty subject set, never be switched off.
    /// </summary>
    [Fact]
    public void No_test_in_this_suite_is_skipped()
    {
        var offenders =
            from method in TestMethods()
            from attribute in method.GetCustomAttributes<FactAttribute>(inherit: true)
            let skip = attribute.Skip
            where !string.IsNullOrWhiteSpace(skip)
            select $"{method.DeclaringType?.FullName}.{method.Name} is skipped: \"{skip}\"";

        ArchRule.Empty(
            offenders,
            "No architecture rule is skipped — rules that are not enforced are suggestions (23 §6).");
    }

    /// <summary>
    /// `23` §6 / M0-08 acceptance criterion 4 — every rule names the document section it
    /// enforces, and the document it names exists. Read back from the compiler-generated XML
    /// documentation of this very assembly, then resolved against `game-design/`.
    /// </summary>
    /// <remarks>
    /// The resolution step is what gives the rule content. A citation is only useful if it
    /// leads somewhere: `99` §9 satisfies the shape and leads nowhere, and a reader chasing
    /// a failure would spend their time looking for a document that has never existed.
    /// </remarks>
    [Fact]
    public void Every_test_in_this_suite_cites_the_document_section_it_enforces()
    {
        var summaries = XmlSummaries();
        var offenders = new List<string>();

        foreach (var method in TestMethods())
        {
            var key = $"M:{method.DeclaringType?.FullName}.{method.Name}";

            if (!summaries.TryGetValue(key, out var summary))
            {
                offenders.Add($"{key} has no XML summary");
                continue;
            }

            var matches = DocumentCitation.Matches(summary);
            if (matches.Count == 0)
            {
                offenders.Add($"{key} has a summary that cites no document section");
                continue;
            }

            var cited = matches
                .Where(m => m.Groups["doc"].Success)
                .Select(m => int.Parse(m.Groups["doc"].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Distinct()
                .ToArray();

            // An X-02-only citation is legitimate — X-02 is a milestone task, not a document.
            if (cited.Length == 0)
            {
                continue;
            }

            if (cited.All(n => !DesignDocumentNumbers.Value.Contains(n)))
            {
                offenders.Add(
                    $"{key} cites document(s) {string.Join(", ", cited.OrderBy(n => n))}, none of which exists under " +
                    $"game-design/. Known: {string.Join(", ", DesignDocumentNumbers.Value.OrderBy(n => n))}.");
            }
        }

        ArchRule.Empty(
            offenders,
            "Every architecture rule cites a doc section, in a document that exists (23 §6, M0-08 acceptance criterion 4).");
    }

    /// <summary>
    /// `23` §6 / M0-08 acceptance criterion 2 — the suite's own source contains no
    /// placeholder standing in for a rule, and the grep that establishes that has actually
    /// read every file the rules live in. Complements the reflection check above by catching
    /// the forms reflection cannot see: a compiled-out block, or an assertion that is true by
    /// construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The scanned-file set is tied to the REFLECTED type set, not just to a directory
    /// path. The path is a literal, and <c>RepoLayout.SourceFiles</c> used to answer a
    /// non-existent directory with an empty list — so renaming or relocating this suite left
    /// the rule grepping nothing, forever, with no failure. What it would then stop seeing is
    /// exactly the thing it exists for: a <c>#if false</c> around
    /// <c>The_whole_game_is_playable_from_Core_alone</c>, which `30` §6 calls the
    /// load-bearing test in the codebase. A compiled-out method does not exist at runtime
    /// either, so <c>No_test_in_this_suite_is_skipped</c> cannot see it — the grep is the
    /// only mechanism that can, and a grep over zero files is not one.
    /// </para>
    /// <para>
    /// So: every class in this assembly that declares a <c>[Fact]</c> must appear among the
    /// scanned filenames. A rule file that moved out from under the grep now fails here
    /// rather than going quiet.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_suite_contains_no_placeholder_rules()
    {
        var suiteDirectory = Path.Combine(RepoLayout.RepoRoot, "tests", "SlayIdleRepeat.Architecture.Tests");

        // Throws rather than returning empty when the directory has moved — see RepoLayout.
        var files = RepoLayout.SourceFiles(suiteDirectory);

        var scannedNames = files
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();

        offenders.AddRange(
            TestMethods()
                .Select(m => m.DeclaringType)
                .Where(t => t is not null)
                .Select(t => t!.Name)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !scannedNames.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name =>
                    $"{name} declares [Fact] rules but no '{name}.cs' was found under " +
                    $"{RepoLayout.Relative(suiteDirectory)}. The placeholder grep is not reading it, so a #if false " +
                    "or an Assert.True(true) in it would be invisible to this suite."));

        offenders.AddRange(
            from file in files
            let source = SourceText.Read(file)
            from placeholder in Placeholders
            from hit in source.Hits(placeholder.Pattern)
            select $"{hit}  [{placeholder.Reason}]");

        ArchRule.Empty(
            offenders,
            "The architecture suite contains no placeholder or disabled rule, and the grep read every rule file (23 §6).");
    }

    /// <summary>Every <c>[Fact]</c> method in this assembly.</summary>
    private static IEnumerable<MethodInfo> TestMethods() =>
        typeof(SuiteIntegrityTests).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any());

    /// <summary>The compiler-generated XML documentation of this assembly, keyed by member id.</summary>
    private static Dictionary<string, string> XmlSummaries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SlayIdleRepeat.Architecture.Tests.xml");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The XML documentation file is missing from {AppContext.BaseDirectory}. " +
                "GenerateDocumentationFile must stay true — it is what proves every rule cites its doc section.");
        }

        return XDocument.Load(path)
            .Descendants("member")
            .Where(m => m.Element("summary") is not null)
            .ToDictionary(
                m => (string?)m.Attribute("name") ?? string.Empty,
                m => m.Element("summary")!.Value,
                StringComparer.Ordinal);
    }
}
