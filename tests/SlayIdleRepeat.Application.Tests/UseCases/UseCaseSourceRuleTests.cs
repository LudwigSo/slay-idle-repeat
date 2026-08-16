using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// 🔒 No game rule lives in the use-case layer, proved the one way a test can prove an absence: the
/// layer's source never names a reason only a rule may decide.
/// </summary>
/// <remarks>
/// <para>
/// A use case may <b>route</b> a domain-tier refusal — it hands back whatever the domain returned —
/// but it may never <b>decide</b> one, and naming a domain-tier member is what deciding one looks
/// like in source. The transport-tier reason this layer does decide is the addressing refusal, which
/// is why finding that one is the rule's own positive control.
/// </para>
/// <para>
/// The scan reads text, comments included. A domain-tier reason named in a comment here is a rule
/// being explained in a layer that must not hold one, and the cheapest honest answer is not to write
/// it down.
/// </para>
/// </remarks>
public sealed class UseCaseSourceRuleTests
{
    /// <summary>How many source files the layer must have before this rule means anything.</summary>
    /// <remarks>Two: the write side and the read side. A scan that reads fewer has lost its subject.</remarks>
    private const int FileFloor = 2;

    /// <summary>The transport-tier reason this layer is expected to name — the rule's positive control.</summary>
    private const string ExpectedTransportReason = "RUN_NOT_FOUND";

    [Fact]
    public void The_use_case_layer_names_no_domain_tier_rejection_reason()
    {
        var sources = UseCaseSourceFiles();

        // The floor comes first: a scan that read nothing is indistinguishable from full compliance,
        // and this exact seam has shipped that failure before.
        sources.Count.ShouldBeGreaterThanOrEqualTo(
            FileFloor,
            "the use-case source scan found " + sources.Count + " file(s). A rule with no subject " +
            "passes for free — if the layer moved, move this scan with it.");

        var text = string.Join("\n", sources.Select(File.ReadAllText));

        text.ShouldContain(
            ExpectedTransportReason,
            Case.Sensitive,
            "the scan cannot see a reason name it is known to contain, so the text it is reading is not " +
            "the text it thinks it is and the rule below proves nothing.");

        var offenders = RejectionReasons.DomainTier
            .Where(reason => text.Contains(reason.ToString(), StringComparison.Ordinal))
            .Select(reason => reason.ToString())
            .ToArray();

        offenders.ShouldBeEmpty(
            "the use-case layer names " + string.Join(", ", offenders) + ". Deciding a refusal a rule " +
            "owns is a game rule living above the domain: the same situation would then be answered one " +
            "way by this layer and another way by the rules, and only one of them is the game.");
    }

    /// <summary>Every <c>.cs</c> file of the use-case layer.</summary>
    private static IReadOnlyList<string> UseCaseSourceFiles()
    {
        var directory = Path.Combine(
            RepoData.RepositoryRoot, "src", "SlayIdleRepeat.Application", "UseCases");

        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            : [];
    }
}
