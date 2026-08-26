using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// The rules that keep <see cref="FixtureExemptions"/> honest — an exemption expires by itself, in
/// every direction a rule can see (steering S4).
/// </summary>
/// <remarks>
/// Three register directions — satisfied, unanchored, malformed — plus the one entry-specific pin:
/// the OTel exemption names a CI assertion as its covering observation, and that assertion's own
/// text is what makes the entry falsifiable rather than a sentence. Each predicate is parameterised,
/// so each direction is also driven against a deliberately bad entry here.
/// </remarks>
public sealed class FixtureExemptionTests
{
    /// <summary>
    /// 🔒 Steering S4, the satisfied direction: no exemption outlives the fixture it excuses. The
    /// moment a <c>[ContractFixtureFor]</c> names an exempted implementation, the entry is wrong,
    /// this fails, and the suite's fixture floor snaps back to its full demand.
    /// </summary>
    [Fact]
    public void No_fixture_exemption_outlives_the_fixture_it_excuses()
    {
        Empty(
            FixtureExemptions.Satisfied(
                FixtureExemptions.Entries, ContractSuiteCoverageTests.FixtureDeclarations()),
            "No FixtureExemptions entry excuses an implementation that now has a fixture (23 §5 A8, steering S4).");
    }

    /// <summary>
    /// 🔒 Steering S4, the unanchored direction: every exemption names a type the implementation
    /// scan actually returns — the same scan
    /// <c>Every_implementation_of_a_port_has_a_contract_fixture</c> consults, so the two cannot
    /// disagree about what an implementation is.
    /// </summary>
    [Fact]
    public void Every_fixture_exemption_names_a_scanned_port_implementation()
    {
        Empty(
            FixtureExemptions.Unanchored(FixtureExemptions.Entries, ScannedImplementations()),
            "Every FixtureExemptions entry names a real, scanned port implementation (23 §6, steering S4).");
    }

    /// <summary>
    /// `23` §6 — every entry carries a covering observation and a reason worth falsifying, and no
    /// implementation is exempted twice.
    /// </summary>
    [Fact]
    public void Every_fixture_exemption_is_well_formed()
    {
        Empty(
            FixtureExemptions.Malformed(FixtureExemptions.Entries),
            "Every FixtureExemptions entry is well formed: covering observation, written reason (23 §6).");
    }

    /// <summary>
    /// 🔒 The OTel entry's covering observation, pinned to the workflow's own text. The exemption
    /// says "CI asserts the collector road is up"; this is what notices the road being deleted.
    /// </summary>
    /// <remarks>
    /// Both halves by literal string: the step name, so a renamed step sends the reader here rather
    /// than leaving the entry pointing at prose that no longer exists, and the scrape job id, which
    /// is the thing the step actually requires ("must exist even while it is empty"). Read from the
    /// repository source tree, not the build output, because the workflow never leaves the tree.
    /// </remarks>
    [Fact]
    public void The_otel_covering_observation_still_exists_in_the_ci_workflow()
    {
        var workflowPath = Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml");

        File.Exists(workflowPath).ShouldBeTrue(
            $"'{workflowPath}' does not exist. The OpenTelemetryTelemetry fixture exemption names a "
            + "step in that workflow as its covering observation; if the workflow moved, re-point "
            + "this rule and the entry together.");

        var workflow = File.ReadAllText(workflowPath);

        workflow.ShouldContain(
            FixtureExemptions.OtelScrapeStep,
            customMessage:
            "the CI workflow no longer carries the step the OpenTelemetryTelemetry exemption names "
            + "as its covering observation. The adapter is then observed by NOTHING — no fixture, no "
            + "infrastructure assertion — which is the state the register exists to forbid. Restore "
            + "the step, or give the entry a covering observation that exists.");

        workflow.ShouldContain(
            FixtureExemptions.OtelScrapeJob,
            customMessage:
            "the CI workflow no longer requires the scrape job the OpenTelemetryTelemetry exemption "
            + "rests on. The step may still exist, but without this job it no longer asserts the "
            + "road this adapter's spans travel, so the covering observation is a name without a "
            + "check behind it.");
    }

    /// <summary>
    /// `23` §6 — the teeth of the three register directions, driven against crafted entries so each
    /// is shown to bite without a violation ever being committed.
    /// </summary>
    [Fact]
    public void The_exemption_directions_fire_on_a_deliberately_bad_entry_and_are_silent_on_a_good_one()
    {
        var scanned = ScannedImplementations().ToArray();
        var fixtures = ContractSuiteCoverageTests.FixtureDeclarations().ToArray();

        var good = new FixtureExemptions.FixtureExemption(
            "SlayIdleRepeat.Adapters.Analytics.PostHog.PostHogAnalyticsSink",
            "a covering observation long enough to be worth checking at the next kickoff.",
            "a reason long enough to be worth falsifying at the next kickoff, naming its backend.");

        FixtureExemptions.Satisfied(new[] { good }, fixtures).ShouldBeEmpty(
            "no fixture names PostHogAnalyticsSink, which is the arrangement the entry records.");
        FixtureExemptions.Unanchored(new[] { good }, scanned).ShouldBeEmpty(
            "PostHogAnalyticsSink is a scanned port implementation, which is what an entry must name.");
        FixtureExemptions.Malformed(new[] { good }).ShouldBeEmpty();

        // Satisfied: an entry excusing an implementation that has a fixture today. SystemClock is in
        // the build with SystemClockContractTests over it, so this is exactly the state the rule
        // exists to catch — caught without committing it.
        FixtureExemptions.Satisfied(
                new[] { good with { Implementation = "SlayIdleRepeat.Adapters.Ambient.System.SystemClock" } },
                fixtures)
            .ShouldHaveSingleItem()
            .ShouldContain("a [ContractFixtureFor] class names it", Case.Sensitive);

        // Unanchored, both shapes: a name nothing declares, and — the half a simple-name register
        // would miss — the right SIMPLE name under the wrong namespace.
        FixtureExemptions.Unanchored(new[] { good with { Implementation = "No.Such.Adapter" } }, scanned)
            .ShouldHaveSingleItem()
            .ShouldContain("no port implementation of that full name", Case.Sensitive);

        FixtureExemptions.Unanchored(
                new[] { good with { Implementation = "SlayIdleRepeat.Adapters.InMemory.PostHogAnalyticsSink" } },
                scanned)
            .ShouldHaveSingleItem()
            .ShouldContain("no port implementation of that full name", Case.Sensitive);

        // Malformed, one crafted entry per branch.
        FixtureExemptions.Malformed(new[] { good with { Implementation = " " } })
            .ShouldContain(o => o.Contains("names no implementation", StringComparison.Ordinal));

        FixtureExemptions.Malformed(new[] { good with { CoveringObservation = "CI" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no covering observation worth checking", Case.Sensitive);

        FixtureExemptions.Malformed(new[] { good with { Why = "vendor" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);

        FixtureExemptions.Malformed(new[] { good, good })
            .ShouldHaveSingleItem()
            .ShouldContain("exempted 2 times", Case.Sensitive);
    }

    /// <summary>Every concrete port implementation the coverage scan returns.</summary>
    private static IEnumerable<Type> ScannedImplementations() =>
        ContractSuiteCoverageTests.PortImplementations(ContractSuiteCoverageTests.Ports().ToArray());

    /// <summary>The directory holding <c>SlayIdleRepeat.sln</c>, walked up from the test output.</summary>
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find SlayIdleRepeat.sln in any ancestor of {AppContext.BaseDirectory}.");
    }

    private static void Empty(IEnumerable<string> offenders, string rule)
    {
        var list = offenders.ToArray();

        if (list.Length > 0)
        {
            throw new ContractCoverageViolationException(rule, list);
        }
    }
}
