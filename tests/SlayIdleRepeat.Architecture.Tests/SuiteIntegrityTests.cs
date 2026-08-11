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
    private static readonly Regex DocumentCitation = new(
        @"(\b\d{1,2}\b\W{0,3}§\s*\d)|(\bX-02\b)",
        RegexOptions.Compiled);

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
    /// enforces, so a future failure is diagnosable instead of mysterious. Read back from
    /// the compiler-generated XML documentation of this very assembly.
    /// </summary>
    [Fact]
    public void Every_test_in_this_suite_cites_the_document_section_it_enforces()
    {
        var summaries = XmlSummaries();

        var offenders =
            from method in TestMethods()
            let key = $"M:{method.DeclaringType?.FullName}.{method.Name}"
            let summary = summaries.TryGetValue(key, out var text) ? text : null
            where summary is null || !DocumentCitation.IsMatch(summary)
            select summary is null
                ? $"{key} has no XML summary"
                : $"{key} has a summary that cites no document section";

        ArchRule.Empty(
            offenders,
            "Every architecture rule cites the doc section it enforces (23 §6, M0-08 acceptance criterion 4).");
    }

    /// <summary>
    /// `23` §6 / M0-08 acceptance criterion 2 — the suite's own source contains no
    /// placeholder standing in for a rule. Complements the reflection check above by
    /// catching the forms reflection cannot see: a compiled-out block, or an assertion
    /// that is true by construction.
    /// </summary>
    [Fact]
    public void The_suite_contains_no_placeholder_rules()
    {
        var suiteDirectory = Path.Combine(RepoLayout.RepoRoot, "tests", "SlayIdleRepeat.Architecture.Tests");

        var offenders =
            from file in RepoLayout.SourceFiles(suiteDirectory)
            let source = SourceText.Read(file)
            from placeholder in Placeholders
            from hit in source.Hits(placeholder.Pattern)
            select $"{hit}  [{placeholder.Reason}]";

        ArchRule.Empty(
            offenders,
            "The architecture suite contains no placeholder or disabled rule (23 §6).");
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
