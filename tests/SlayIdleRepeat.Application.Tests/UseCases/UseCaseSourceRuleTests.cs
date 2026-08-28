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

    /// <summary>The read side's own files, whose absence would take the rules below green with them.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Two files since M7-06b, and the second one is why this is a list.</b> The rule was
    /// written when the layer had one query, and "the read side" was spelled as that file's name — so
    /// a second query arrived governed by nothing, which is exactly the shape of drift the subject
    /// floors in this file exist to catch. A read use case added without an entry here is a read the
    /// two rules below do not see.
    /// </para>
    /// <para>
    /// 🔒 <b>Three since M5-07, and the third one does not end in <c>UseCase.cs</c>.</b> The query
    /// surface's own read assembles the wire answer rather than orchestrating a use case, so
    /// <c>ReadSide</c>'s directory sweep — which classified files by that suffix alone — would never
    /// have asked about it: a read that names <c>Rehydrate</c> or a run's phase would have slipped in
    /// governed by nothing at all, which is the precise gap the second entry was added to close. The
    /// sweep now claims <c>Query.cs</c> too, so this entry is re-derived from the directory rather
    /// than trusted: deleting the line below fails, exactly as deleting a use case's line does.
    /// </para>
    /// </remarks>
    private static readonly string[] ReadSideFiles =
        ["ReadOwnStateUseCase.cs", "SimulatePendingBattleUseCase.cs", "RunStateQuery.cs"];

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

        var readSide = ReadSide(sources);

        var controls = WriteSideFiles
            .Select(name => sources.SingleOrDefault(f => Path.GetFileName(f) == name))
            .ToArray();

        readSide.ShouldNotContain(
            (string?)null,
            "the scan read " + sources.Count + " file(s) and one of " + string.Join(", ", ReadSideFiles) +
            " was not among them. A rule with no subject passes for free.");
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

        var offenders = readSide
            .SelectMany(file => Named(file!, RuleDoors))
            .ToArray();

        offenders.ShouldBeEmpty(
            "on the read side: " + string.Join("; ", offenders) + ".A read that holds an aggregate " +
            "is a read that can invoke a rule, and the same question would then be answered once by the " +
            "command that wrote the row and again by the query that reads it.");
    }

    /// <summary>
    /// The domain state vocabulary a read may carry but must not branch on — the run's phase.
    /// </summary>
    /// <remarks>
    /// A phase is the domain's own account of where a run stands, and every question worth asking of
    /// it — is there a battle open, is a draft owed, is the run over — is a rule. A read that names it
    /// is a read that has an opinion about one.
    /// </remarks>
    private static readonly string[] DomainStateVocabulary = ["RunPhase"];

    /// <summary>The write-side file across which the term above is legitimately named.</summary>
    /// <remarks>
    /// <c>WorldSliceStore</c> routes an ended run to the archive, which is a storage decision keyed on
    /// a phase rather than a query forming an opinion about one. It is named here as the control that
    /// proves the scan can find the term at all.
    /// </remarks>
    private const string PhaseControlFile = "WorldSliceStore.cs";

    /// <summary>
    /// 🔒 The read side decides nothing about a run's phase: it hands rows out, and every question
    /// whose answer is a phase is asked of <c>Core</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The failure this is stated over is one this layer shipped.</b> The pending-battle read
    /// chose its "no open battle" answer by testing the phase itself, while the rules assembly
    /// decided the same question over four facts — so the two disagreed, and a run in the battle
    /// phase carrying no pending tile was reported as having a fight and then threw out of a read.
    /// Naming the phase is what deciding a rule looks like in source here, exactly as naming a
    /// domain-tier reason is above.
    /// </para>
    /// <para>
    /// Both floors are by identity, as above: the subject files must be present, and the banned term
    /// must be proven findable by this same scan where it is legitimate.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_read_side_decides_nothing_about_a_runs_phase()
    {
        var sources = ApplicationSourceFiles();
        var readSide = ReadSide(sources);

        readSide.ShouldNotContain(
            (string?)null,
            "the scan read " + sources.Count + " file(s) and one of " + string.Join(", ", ReadSideFiles) +
            " was not among them. A rule with no subject passes for free.");

        var control = sources.SingleOrDefault(f => Path.GetFileName(f) == PhaseControlFile);

        control.ShouldNotBeNull(
            "the scan never saw " + PhaseControlFile + ", so it cannot prove the banned term is " +
            "findable at all and a typo among the terms would read as compliance.");

        var controlText = File.ReadAllText(control!);

        foreach (var term in DomainStateVocabulary)
        {
            controlText.ShouldContain(
                term,
                Case.Sensitive,
                "'" + term + "' is not found in " + PhaseControlFile + ", where it is legitimately " +
                "named. A banned term this scan cannot find anywhere bans nothing.");
        }

        var offenders = readSide
            .SelectMany(file => Named(file!, DomainStateVocabulary))
            .ToArray();

        offenders.ShouldBeEmpty(
            "on the read side: " + string.Join("; ", offenders) + ".Deciding what a phase means is " +
            "a game rule, and a query that holds one answers the same question the domain answers — " +
            "differently, the first time either side grows a clause the other does not.");
    }

    /// <summary>The file-name suffixes that say a file is a use case or a query, and so must be classified.</summary>
    private static readonly string[] ClassifiableSuffixes = ["UseCase.cs", "Query.cs"];

    /// <summary>The read side's files, in <see cref="ReadSideFiles"/> order, <c>null</c> where absent.</summary>
    /// <remarks>
    /// 🔒 <b>Every use case and every query in the layer is classified first, and that is the
    /// load-bearing half.</b> The floors in this file are stated over <see cref="ReadSideFiles"/>, so
    /// they are only as wide as that array — and an array is shrunk by deleting a line, which no
    /// floor over the array itself can see (steering S3). The classification is taken from the
    /// DIRECTORY instead: a file whose name says it is one of these and which is in neither list is a
    /// file the read-side rules do not govern and nobody said so, whether it was just written or just
    /// dropped from the list.
    /// </remarks>
    private static IReadOnlyList<string?> ReadSide(IReadOnlyList<string> sources)
    {
        var classified = ReadSideFiles.Concat(WriteSideFiles).ToHashSet(StringComparer.Ordinal);

        var unclassified = sources
            .Select(Path.GetFileName)
            .Where(name => name is not null &&
                           ClassifiableSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
            .Where(name => !classified.Contains(name!))
            .ToArray();

        unclassified.ShouldBeEmpty(
            "the layer holds " + string.Join(", ", unclassified) + ", which is named in neither the " +
            "read-side nor the write-side list. Every use case and every query is one or the other: an " +
            "unclassified one is a read the two rules below never look at, and deleting a name from " +
            "the read-side list is exactly how one gets there without anybody choosing to.");

        return ReadSideFiles
            .Select(name => sources.SingleOrDefault(f => Path.GetFileName(f) == name))
            .ToArray();
    }

    /// <summary>Which of <paramref name="terms"/> the file's text names, tagged with the file.</summary>
    private static IEnumerable<string> Named(string file, IReadOnlyList<string> terms)
    {
        var text = File.ReadAllText(file);

        return terms
            .Where(term => text.Contains(term, StringComparison.Ordinal))
            .Select(term => Path.GetFileName(file) + " names " + term);
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
