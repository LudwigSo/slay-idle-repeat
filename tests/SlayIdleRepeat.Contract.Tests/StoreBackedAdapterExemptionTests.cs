using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// The rules that keep <see cref="StoreBackedAdapterExemptions"/> honest, and the proofs that each
/// rule bites — driven with crafted entries, never by committing a violation.
/// </summary>
public sealed class StoreBackedAdapterExemptionTests
{
    /// <summary>
    /// 🔒 The store-backed adapters this register must always carry, named one by one — an identity
    /// floor, for the reason <c>CoveredImplementations</c> is one: a count is cleared by a register
    /// that swapped members, and what has to survive is these six names.
    /// </summary>
    private static readonly string[] RequiredExemptions =
    {
        "SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresPlayerRepository",
        "SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresRunStateStore",
        "SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresIdempotencyStore",
        "SlayIdleRepeat.Adapters.Cache.Redis.RedisRunStateCache",
        "SlayIdleRepeat.Adapters.Cache.Redis.RedisIdempotencyCache",
        "SlayIdleRepeat.Adapters.ObjectStore.S3.S3BattleLogStore",
    };

    private static string RepoRoot { get; } = FindRepoRoot();

    [Fact]
    public void Every_exemption_is_well_formed() =>
        StoreBackedAdapterExemptions.Malformed(StoreBackedAdapterExemptions.Entries).ShouldBeEmpty();

    [Fact]
    public void Every_exemption_names_an_implementation_the_scan_finds() =>
        StoreBackedAdapterExemptions.Unanchored(
                StoreBackedAdapterExemptions.Entries,
                ContractSuiteCoverageTests.PortImplementations(
                    ContractSuiteCoverageTests.Ports().ToArray()))
            .ShouldBeEmpty();

    [Fact]
    public void No_exemption_outlives_the_fixture_it_excuses() =>
        StoreBackedAdapterExemptions.Expired(
                StoreBackedAdapterExemptions.Entries,
                ContractSuiteCoverageTests.FixtureDeclarations())
            .ShouldBeEmpty();

    [Fact]
    public void Every_exemptions_probe_exists_and_is_wired_into_the_compose_boot_job() =>
        StoreBackedAdapterExemptions.OwnersNotWired(
                StoreBackedAdapterExemptions.Entries,
                probe => File.Exists(Path.Combine(RepoRoot, probe.Replace('/', Path.DirectorySeparatorChar))),
                File.ReadAllText(Path.Combine(RepoRoot, StoreBackedAdapterExemptions.WorkflowPath
                    .Replace('/', Path.DirectorySeparatorChar))))
            .ShouldBeEmpty();

    /// <summary>🔒 Steering S3: the register cannot quietly empty or swap members.</summary>
    [Fact]
    public void The_exemption_register_carries_every_store_backed_adapter_by_name()
    {
        var carried = StoreBackedAdapterExemptions.Entries
            .Select(e => e.Implementation)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var required in RequiredExemptions)
        {
            carried.ShouldContain(
                required,
                $"'{required}' is a store-backed port implementation with no in-repo fixture. Without "
                + "its entry, Every_implementation_of_a_port_has_a_contract_fixture would fail — and "
                + "if it is NOT failing, the implementation has left the scan, which is worse. If the "
                + "adapter was genuinely deleted, delete the name here in the same commit.");
        }
    }

    // ------------------------------------------------------------------------------- self-tests
    //
    // 🔒 Each direction driven loud on a crafted bad entry and silent on a good one (steering S1).
    // The good entry is a REAL register row, so the silent halves also exercise real data.

    private static StoreBackedAdapterExemptions.StoreBackedAdapterExemption Good =>
        StoreBackedAdapterExemptions.Entries[0];

    [Fact]
    public void The_malformed_direction_fires_on_each_defect_and_is_silent_on_a_real_entry()
    {
        StoreBackedAdapterExemptions.Malformed(new[] { Good }).ShouldBeEmpty(
            "the first real entry is well formed, which is the arrangement the rule permits.");

        StoreBackedAdapterExemptions.Malformed(new[] { Good with { Implementation = " " } })
            .ShouldContain(o => o.Contains("names no implementation", StringComparison.Ordinal));

        StoreBackedAdapterExemptions.Malformed(new[] { Good with { Probe = "tools/probe.ps1" } })
            .ShouldHaveSingleItem()
            .ShouldContain("not a pwsh script under build/ci/", Case.Sensitive);

        StoreBackedAdapterExemptions.Malformed(new[] { Good with { Probe = "build/ci/probe.sh" } })
            .ShouldHaveSingleItem()
            .ShouldContain("not a pwsh script under build/ci/", Case.Sensitive);

        StoreBackedAdapterExemptions.Malformed(new[] { Good with { Why = "needs a database" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);
    }

    [Fact]
    public void The_unanchored_direction_fires_on_a_name_the_scan_does_not_find()
    {
        var scanned = new[] { typeof(Adapters.Ambient.System.SystemClock) };
        var anchored = Good with { Implementation = typeof(Adapters.Ambient.System.SystemClock).FullName! };

        StoreBackedAdapterExemptions.Unanchored(new[] { anchored }, scanned).ShouldBeEmpty(
            "the entry names exactly the type the scan returned.");

        StoreBackedAdapterExemptions.Unanchored(
                new[] { anchored with { Implementation = "SlayIdleRepeat.Adapters.Gone.DeletedStore" } },
                scanned)
            .ShouldHaveSingleItem()
            .ShouldContain("the implementation scan does not find it", Case.Sensitive);
    }

    [Fact]
    public void The_expiry_direction_fires_the_moment_an_exempted_implementation_gains_a_fixture()
    {
        var fixtureFor = new ContractSuiteCoverageTests.FixtureDeclaration(
            typeof(Shared.SystemClockContractTests), typeof(Adapters.Ambient.System.SystemClock));
        var exempted = Good with { Implementation = typeof(Adapters.Ambient.System.SystemClock).FullName! };

        StoreBackedAdapterExemptions.Expired(new[] { Good }, new[] { fixtureFor }).ShouldBeEmpty(
            "the fixture covers a different implementation, so nothing here has expired.");

        StoreBackedAdapterExemptions.Expired(new[] { exempted }, new[] { fixtureFor })
            .ShouldHaveSingleItem()
            .ShouldContain("a [ContractFixtureFor] fixture for it now exists", Case.Sensitive);
    }

    [Fact]
    public void The_owner_direction_fires_on_a_missing_probe_and_on_an_unwired_one()
    {
        var wired = "steps:\n  - run: ./" + Good.Probe + " -SomeArg";

        StoreBackedAdapterExemptions.OwnersNotWired(new[] { Good }, _ => true, wired).ShouldBeEmpty(
            "the probe exists and the workflow invokes it, which is an OPEN owner.");

        StoreBackedAdapterExemptions.OwnersNotWired(new[] { Good }, _ => false, wired)
            .ShouldHaveSingleItem()
            .ShouldContain("no such file exists", Case.Sensitive);

        StoreBackedAdapterExemptions.OwnersNotWired(
                new[] { Good }, _ => true, "steps:\n  - run: ./build/ci/Invoke-SomeOtherProbe.ps1")
            .ShouldHaveSingleItem()
            .ShouldContain("not invoked anywhere", Case.Sensitive);
    }

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
            "No SlayIdleRepeat.sln above " + AppContext.BaseDirectory + " — the owner-wiring rule "
            + "reads build/ci/ and the workflow from the repository root and must fail loudly "
            + "rather than report every probe missing.");
    }
}
