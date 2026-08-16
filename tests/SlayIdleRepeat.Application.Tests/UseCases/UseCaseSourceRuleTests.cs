using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// 🔒 No game rule lives in this layer, proved the one way a test can prove an absence: the layer's
/// source never names a reason only a rule may decide.
/// </summary>
/// <remarks>
/// <para>
/// A use case may <b>route</b> a domain-tier refusal — it hands back whatever the domain returned —
/// but it may never <b>decide</b> one, and naming a domain-tier member is what deciding one looks
/// like in source. The transport-tier reason this layer does decide is the addressing refusal, which
/// is why finding that one is the rule's own positive control.
/// </para>
/// <para>
/// The subject is the whole layer, not the two use-case files: the store, the codec and the
/// dispatcher sit on the same side of the seam and a rule decided in any of them is a rule decided
/// above the domain just the same.
/// </para>
/// <para>
/// The scan reads text, comments included. A domain-tier reason named in a comment here is a rule
/// being explained in a layer that must not hold one, and the cheapest honest answer is not to write
/// it down.
/// </para>
/// </remarks>
public sealed class UseCaseSourceRuleTests
{
    /// <summary>The files the scan must be reading before the rule below means anything.</summary>
    /// <remarks>
    /// Named files rather than a count: a scan pointed at some other directory of the same size
    /// satisfies a count and proves nothing, and a rule that read nothing is indistinguishable from
    /// full compliance. If the layer moves, this list has to move with it — deliberately and visibly.
    /// </remarks>
    private static readonly string[] RequiredFiles =
    [
        "ApplyCommandUseCase.cs",
        "ReadOwnStateUseCase.cs",
        "WorldSliceStore.cs",
        "SliceKeys.cs",
        "SnapshotCodec.cs",
        "DomainEventDispatcher.cs",
        "IDomainEventSink.cs",
    ];

    /// <summary>The transport-tier reason this layer is expected to name — the rule's positive control.</summary>
    private const string ExpectedTransportReason = "RUN_NOT_FOUND";

    [Fact]
    public void The_application_layer_names_no_domain_tier_rejection_reason()
    {
        var sources = ApplicationSourceFiles();
        var scanned = sources.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var missing = RequiredFiles.Where(file => !scanned.Contains(file)).ToArray();

        // The subject floor comes first, and it is by name: a scan that read the wrong directory
        // passes a count, and this exact seam has shipped that failure before.
        missing.ShouldBeEmpty(
            "the source scan read " + sources.Count + " file(s) and never saw " +
            string.Join(", ", missing) + ". A rule with no subject passes for free.");

        var text = string.Join("\n", sources.Select(File.ReadAllText));

        text.ShouldContain(
            ExpectedTransportReason,
            Case.Sensitive,
            "the scan cannot see the one reason name this layer is known to contain, so the text it " +
            "is reading is not the text it thinks it is and the rule below proves nothing.");

        var offenders = RejectionReasons.DomainTier
            .Where(reason => text.Contains(reason.ToString(), StringComparison.Ordinal))
            .Select(reason => reason.ToString())
            .ToArray();

        offenders.ShouldBeEmpty(
            "this layer names " + string.Join(", ", offenders) + ". Deciding a refusal a rule " +
            "owns is a game rule living above the domain: the same situation would then be answered one " +
            "way by this layer and another way by the rules, and only one of them is the game.");
    }

    /// <summary>Every hand-written <c>.cs</c> file of the layer.</summary>
    private static IReadOnlyList<string> ApplicationSourceFiles()
    {
        var directory = Path.Combine(RepoData.RepositoryRoot, "src", "SlayIdleRepeat.Application");

        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Where(Authored).ToArray()
            : [];
    }

    /// <summary>Whether a path is authored source rather than a generated build artefact.</summary>
    private static bool Authored(string path) =>
        !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
