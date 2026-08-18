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
        "SimulatePendingBattleUseCase.cs",
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

    /// <summary>The read side's own file, whose absence would take the rule below green with it.</summary>
    private const string ReadSideFile = "ReadOwnStateUseCase.cs";

    /// <summary>The write side's files, across which every term the rule below bans is legitimately named.</summary>
    private static readonly string[] WriteSideFiles = ["ApplyCommandUseCase.cs", "WorldSliceStore.cs"];

    /// <summary>The doors from a stored row onto something a rule can be invoked on.</summary>
    /// <remarks>
    /// A row is inert; an aggregate is not. Naming any of these on the read side puts a rule one call
    /// away from a query, which is the failure the write/read split exists to prevent — and it is the
    /// same failure whether the call is made today or left one line from being made.
    /// </remarks>
    private static readonly string[] RuleDoors = ["Rehydrate", "GameRules", ".Apply("];

    /// <summary>
    /// 🔒 The read side names no way to turn a stored row into an aggregate, so a query cannot run a
    /// game rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a rule and not a type-system guarantee, and the difference is worth stating: a player
    /// aggregate needs a content set the read use case is never handed, but a run's row rehydrates
    /// from itself alone. Measured, not assumed — rehydrating a run inside the read path leaves every
    /// other case in this suite green.
    /// </para>
    /// <para>
    /// Both floors are by identity. The subject must be present, and every banned term must be proven
    /// findable by this same scan somewhere it is legitimate, so a term misspelled into something that
    /// can never match cannot pass as compliance.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_read_side_names_no_way_to_rehydrate_an_aggregate()
    {
        var sources = ApplicationSourceFiles();

        var readSide = sources.SingleOrDefault(f => Path.GetFileName(f) == ReadSideFile);

        var controls = WriteSideFiles
            .Select(name => sources.SingleOrDefault(f => Path.GetFileName(f) == name))
            .ToArray();

        readSide.ShouldNotBeNull(
            "the scan read " + sources.Count + " file(s) and none of them was " + ReadSideFile +
            ". A rule with no subject passes for free.");
        controls.ShouldNotContain(
            (string?)null,
            "the scan never saw one of " + string.Join(", ", WriteSideFiles) + ", so the control below " +
            "cannot prove the banned terms are findable at all and a typo among them would read as " +
            "compliance.");

        var control = string.Join("\n", controls.Select(file => File.ReadAllText(file!)));

        foreach (var door in RuleDoors)
        {
            control.ShouldContain(
                door,
                Case.Sensitive,
                "'" + door + "' is not found in " + string.Join(" or ", WriteSideFiles) + ", where it is " +
                "legitimately named. A banned term this scan cannot find anywhere bans nothing.");
        }

        var text = File.ReadAllText(readSide!);

        var offenders = RuleDoors.Where(door => text.Contains(door, StringComparison.Ordinal)).ToArray();

        offenders.ShouldBeEmpty(
            ReadSideFile + " names " + string.Join(", ", offenders) + ". A read that holds an aggregate " +
            "is a read that can invoke a rule, and the same question would then be answered once by the " +
            "command that wrote the row and again by the query that reads it.");
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
