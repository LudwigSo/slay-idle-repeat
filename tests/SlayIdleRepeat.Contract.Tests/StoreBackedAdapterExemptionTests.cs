using Shouldly;
using Xunit;
using static SlayIdleRepeat.Contract.Tests.StoreBackedAdapterExemptions;

namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// The rules that keep <see cref="StoreBackedAdapterExemptions"/> honest, and the proofs that each
/// rule bites — driven with crafted entries, never by committing a violation.
/// </summary>
public sealed class StoreBackedAdapterExemptionTests
{
    /// <summary>
    /// 🔒 The adapters this register must always carry, named one by one and paired with the owner
    /// kind each is entitled to — an identity floor, for the reason <c>CoveredImplementations</c> is
    /// one: a count is cleared by a register that swapped members, and what has to survive is these
    /// names.
    /// </summary>
    /// <remarks>
    /// The kind is pinned beside the name because the register's owner shapes are not
    /// interchangeable. <see cref="ExemptionOwnerKind.DeploymentOnly"/> is the weakest — its status
    /// check is only that the vendor is still switched off locally — so without this pairing, a
    /// store-backed adapter whose probe became inconvenient could be quietly moved onto it and every
    /// other direction would stay green.
    /// </remarks>
    private static readonly (string Implementation, ExemptionOwnerKind Kind)[] RequiredExemptions =
    {
        ("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresPlayerRepository", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresRunStateStore", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresIdempotencyStore", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresUnitOfWork", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresMessageRepository", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Cache.Redis.RedisRunStateCache", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Cache.Redis.RedisIdempotencyCache", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Cache.Redis.RedisCommitCache", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.ObjectStore.S3.S3BattleLogStore", ExemptionOwnerKind.CiProbeScript),
        ("SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry.OpenTelemetryTelemetry", ExemptionOwnerKind.CiWorkflowStep),
        ("SlayIdleRepeat.Adapters.Telemetry.Sentry.SentryTelemetry", ExemptionOwnerKind.DeploymentOnly),
        ("SlayIdleRepeat.Adapters.Analytics.PostHog.PostHogAnalyticsSink", ExemptionOwnerKind.DeploymentOnly),
    };

    private static string RepoRoot { get; } = FindRepoRoot();

    [Fact]
    public void Every_exemption_is_well_formed() =>
        Malformed(Entries).ShouldBeEmpty();

    [Fact]
    public void Every_exemption_names_an_implementation_the_scan_finds() =>
        Unanchored(
                Entries,
                ContractSuiteCoverageTests.PortImplementations(
                    ContractSuiteCoverageTests.Ports().ToArray()))
            .ShouldBeEmpty();

    [Fact]
    public void No_exemption_outlives_the_fixture_it_excuses() =>
        Expired(Entries, ContractSuiteCoverageTests.FixtureDeclarations()).ShouldBeEmpty();

    [Fact]
    public void Every_exemptions_owner_is_still_open() =>
        OwnersNotWired(
                Entries,
                probe => File.Exists(RepoPath(probe)),
                File.ReadAllText(RepoPath(WorkflowPath)),
                File.ReadAllText(RepoPath(LocalStackPath)))
            .ShouldBeEmpty();

    /// <summary>
    /// 🔒 The OTel entry's step names an assertion; this is what notices the assertion being hollowed
    /// out while its step name survives.
    /// </summary>
    /// <remarks>
    /// The step could keep its name and stop requiring the collector's application-facing scrape job,
    /// at which point the owner is a heading with no check under it. Read from the repository source
    /// tree, not the build output, because the workflow never leaves the tree.
    /// </remarks>
    [Fact]
    public void The_otel_step_still_requires_the_scrape_job_the_entry_rests_on() =>
        File.ReadAllText(RepoPath(WorkflowPath)).ShouldContain(
            OtelScrapeJob,
            customMessage:
            $"{WorkflowPath} no longer requires the scrape job the OpenTelemetryTelemetry exemption "
            + "rests on. The step may still exist, but without this job it no longer asserts the road "
            + "this adapter's spans travel, so the owner is a name with no check behind it.");

    /// <summary>🔒 Steering S3: the register cannot quietly empty, swap members, or downgrade one.</summary>
    [Fact]
    public void The_exemption_register_carries_every_exempted_adapter_by_name_and_kind()
    {
        var carried = Entries.ToDictionary(e => e.Implementation, e => e.OwnerKind, StringComparer.Ordinal);

        foreach (var (implementation, kind) in RequiredExemptions)
        {
            carried.ShouldContainKey(
                implementation,
                $"'{implementation}' is a port implementation with no in-repo fixture. Without "
                + "its entry, Every_implementation_of_a_port_has_a_contract_fixture would fail — and "
                + "if it is NOT failing, the implementation has left the scan, which is worse. If the "
                + "adapter was genuinely deleted, delete the name here in the same commit.");

            carried[implementation].ShouldBe(
                kind,
                $"'{implementation}' is entitled to a {kind} owner and now carries a "
                + $"{carried[implementation]} one. The owner kinds are not interchangeable: moving an "
                + "adapter onto DeploymentOnly drops its status check to 'the vendor is still off "
                + "locally', which is not a check that anything exercises the adapter.");
        }
    }

    // ------------------------------------------------------------------------------- self-tests
    //
    // 🔒 Each direction driven loud on a crafted bad entry and silent on a good one (steering S1).
    // The good entry is a REAL register row, so the silent halves also exercise real data.

    private static StoreBackedAdapterExemption Probe => Entries[0];

    private static StoreBackedAdapterExemption Step =>
        Entries.First(e => e.OwnerKind == ExemptionOwnerKind.CiWorkflowStep);

    private static StoreBackedAdapterExemption Deployed =>
        Entries.First(e => e.OwnerKind == ExemptionOwnerKind.DeploymentOnly);

    [Fact]
    public void The_malformed_direction_fires_on_each_defect_and_is_silent_on_a_real_entry()
    {
        Malformed(new[] { Probe, Step, Deployed }).ShouldBeEmpty(
            "one real entry of each owner kind is well formed, which is the arrangement the rule permits.");

        Malformed(new[] { Probe with { Implementation = " " } })
            .ShouldContain(o => o.Contains("names no implementation", StringComparison.Ordinal));

        Malformed(new[] { Probe with { Owner = "tools/probe.ps1" } })
            .ShouldHaveSingleItem()
            .ShouldContain("not a pwsh script under build/ci/", Case.Sensitive);

        Malformed(new[] { Probe with { Owner = "build/ci/probe.sh" } })
            .ShouldHaveSingleItem()
            .ShouldContain("not a pwsh script under build/ci/", Case.Sensitive);

        Malformed(new[] { Step with { Owner = "Assert" } })
            .ShouldHaveSingleItem()
            .ShouldContain("copied verbatim out of", Case.Sensitive);

        Malformed(new[] { Deployed with { Owner = "disabled" } })
            .ShouldHaveSingleItem()
            .ShouldContain("is not a configuration setting from", Case.Sensitive);

        Malformed(new[] { Probe with { Why = "needs a database" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);

        // 🔒 One type, two answers: deleting either would read as the exemption expiring while the
        // other kept it alive.
        Malformed(new[] { Probe, Probe })
            .ShouldHaveSingleItem()
            .ShouldContain("exempted 2 times", Case.Sensitive);
    }

    [Fact]
    public void The_unanchored_direction_fires_on_a_name_the_scan_does_not_find()
    {
        var scanned = new[] { typeof(Adapters.Ambient.System.SystemClock) };
        var anchored = Probe with { Implementation = typeof(Adapters.Ambient.System.SystemClock).FullName! };

        Unanchored(new[] { anchored }, scanned).ShouldBeEmpty(
            "the entry names exactly the type the scan returned.");

        Unanchored(
                new[] { anchored with { Implementation = "SlayIdleRepeat.Adapters.Gone.DeletedStore" } },
                scanned)
            .ShouldHaveSingleItem()
            .ShouldContain("the implementation scan does not find it", Case.Sensitive);

        // 🔒 The shape a simple-name register would miss: the right SIMPLE name under the wrong
        // namespace. A decoy cannot excuse the type it shadows.
        Unanchored(
                new[] { anchored with { Implementation = "SlayIdleRepeat.Adapters.InMemory.SystemClock" } },
                scanned)
            .ShouldHaveSingleItem()
            .ShouldContain("the implementation scan does not find it", Case.Sensitive);
    }

    [Fact]
    public void The_expiry_direction_fires_the_moment_an_exempted_implementation_gains_a_fixture()
    {
        var fixtureFor = new ContractSuiteCoverageTests.FixtureDeclaration(
            typeof(Shared.SystemClockContractTests), typeof(Adapters.Ambient.System.SystemClock));
        var exempted = Probe with { Implementation = typeof(Adapters.Ambient.System.SystemClock).FullName! };

        Expired(new[] { Probe }, new[] { fixtureFor }).ShouldBeEmpty(
            "the fixture covers a different implementation, so nothing here has expired.");

        Expired(new[] { exempted }, new[] { fixtureFor })
            .ShouldHaveSingleItem()
            .ShouldContain("a [ContractFixtureFor] fixture for it now exists", Case.Sensitive);
    }

    [Fact]
    public void The_owner_direction_fires_on_every_shape_of_owner_that_stopped_holding()
    {
        var workflow = "steps:\n  - run: ./" + Probe.Owner + " -SomeArg\n  - name: " + Step.Owner;
        var stack = "    environment:\n      " + Deployed.Owner;

        OwnersNotWired(new[] { Probe, Step, Deployed }, _ => true, workflow, stack).ShouldBeEmpty(
            "the probe exists and is invoked, the step is present, and the vendor is still disabled "
            + "in the local stack — three OPEN owners.");

        OwnersNotWired(new[] { Probe }, _ => false, workflow, stack)
            .ShouldHaveSingleItem()
            .ShouldContain("no such file exists", Case.Sensitive);

        OwnersNotWired(
                new[] { Probe }, _ => true, "steps:\n  - run: ./build/ci/Invoke-SomeOtherProbe.ps1", stack)
            .ShouldHaveSingleItem()
            .ShouldContain("not invoked anywhere", Case.Sensitive);

        OwnersNotWired(new[] { Step }, _ => true, "steps:\n  - name: Something else", stack)
            .ShouldHaveSingleItem()
            .ShouldContain("no longer carries it", Case.Sensitive);

        // 🔒 The widened arm: a deployment-only entry claims nothing local CAN observe the adapter
        // BECAUSE the stack switches it off. Switch it on and the claim is false, loudly.
        OwnersNotWired(new[] { Deployed }, _ => true, workflow, "    environment:\n      PostHog__Enabled: 'true'")
            .ShouldHaveSingleItem()
            .ShouldContain("that setting is no longer there", Case.Sensitive);
    }

    private static string RepoPath(string repoRelative) =>
        Path.Combine(RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SlayIdleRepeat.sln")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new InvalidOperationException(
            "No SlayIdleRepeat.sln above " + AppContext.BaseDirectory + " — the owner-status rule "
            + "reads build/ci/, the workflow and the compose file from the repository root and must "
            + "fail loudly rather than report every owner missing.");
    }
}
