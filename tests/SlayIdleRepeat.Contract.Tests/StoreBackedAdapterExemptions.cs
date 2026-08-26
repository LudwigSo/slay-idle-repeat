namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// 🔒 The register of port implementations that carry NO in-repo contract fixture because their
/// only honest fixture is a live store — and this repository has no infrastructure test tier, by
/// decision. Each entry names the CI probe that exercises the adapter instead, and expires by that
/// owner's status.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>ContractSuiteCoverageTests</c>' rule 2 demands a fixture for every
/// implementation of a port, and a fixture for a Postgres/Redis/object-store adapter would need
/// Docker in the unit tier — the tier decision (<c>$noIntegrationTier</c> in
/// <c>build/ci/test-suites.json</c>) rules that out categorically. What replaced the deleted tier is
/// the <c>compose-boot</c> CI job's authored probes under <c>build/ci/</c>; an entry here says
/// "THIS probe is where this adapter is exercised", which is a claim a reader can falsify.
/// </para>
/// <para>
/// <b>The mechanism fails in four directions</b>, the same construction as <c>PortCatalogue</c>:
/// </para>
/// <list type="number">
///   <item><b>Stale.</b> An entry whose implementation now HAS a fixture fails the build — the
///   exemption was satisfied and must go (steering S4: a declared exception expires by itself,
///   including when satisfied).</item>
///   <item><b>Unanchored.</b> An entry naming an implementation the scan does not find is a promise
///   about nothing.</item>
///   <item><b>Orphaned.</b> An entry whose probe file is gone from <c>build/ci/</c>, or whose probe
///   the <c>compose-boot</c> job no longer invokes, has an owner that is no longer OPEN — the
///   adapter is then exercised by nothing anywhere, which is exactly the state the entry claims is
///   not so (steering S4's M4 amendment: check the owner is still OPEN, not that it exists).</item>
///   <item><b>Vacuous.</b> The subject set has an identity floor: the six store-backed adapters are
///   named one by one in the register's own tests, so the register cannot quietly empty.</item>
/// </list>
/// <para>
/// ⚠️ <b>The known limit</b>: what a probe actually asserts is not decidable here. The
/// object-store entry is the honest weak case — no production path writes a battle log until the
/// battle milestone lands a producer, so its probe verifies the adapter is constructed and its
/// drain running against the live store, not a put/get. The entry's reason says so; re-read these
/// at each kickoff.
/// </para>
/// </remarks>
internal static class StoreBackedAdapterExemptions
{
    /// <summary>
    /// One exempted implementation: who, which probe exercises it, and why no in-repo fixture can.
    /// </summary>
    /// <param name="Implementation">The implementation's full type name.</param>
    /// <param name="Probe">The probe script's repo-relative path, under <c>build/ci/</c>.</param>
    /// <param name="Why">Why a fixture cannot exist in this repository — falsifiable at a kickoff.</param>
    internal sealed record StoreBackedAdapterExemption(string Implementation, string Probe, string Why);

    /// <summary>The compose-boot workflow the probes must be wired into.</summary>
    internal const string WorkflowPath = ".github/workflows/ci.yml";

    /// <summary>🔒 Every store-backed implementation, its probe, and its reason.</summary>
    internal static readonly StoreBackedAdapterExemption[] Entries =
    {
        new("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresPlayerRepository",
            "build/ci/Invoke-CommandRoundTripProbe.ps1",
            "Its only backing is a live Postgres; a fixture would need the compose stack in the " +
            "unit tier. The probe posts a real command through the API and asserts the player row " +
            "landed — the JSONB document plus the typed columns — by querying the database in the " +
            "compose-boot job."),

        new("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresRunStateStore",
            "build/ci/Invoke-CommandRoundTripProbe.ps1",
            "Its only backing is a live Postgres. The same round-trip probe covers it: the posted " +
            "command's save upserts the run row beside the player row, and the probe asserts both."),

        new("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresIdempotencyStore",
            "build/ci/Invoke-IdempotentReplayProbe.ps1",
            "Its only backing is a live Postgres, and its one atomic effect (record plus sequence " +
            "advance) is only observable against one. The probe replays a committed command across " +
            "an API restart and demands the byte-identical stored response — which only the durable " +
            "record can produce."),

        new("SlayIdleRepeat.Adapters.Cache.Redis.RedisRunStateCache",
            "build/ci/Invoke-RedisRebuildProbe.ps1",
            "Its policy layer is unit-tested over a fake byte surface, but the vendor-speaking " +
            "class underneath needs a live Redis. The probe flushes Redis mid-session and demands " +
            "the next command still succeed — the rebuild-from-Postgres claim, made against the " +
            "real pair."),

        new("SlayIdleRepeat.Adapters.Cache.Redis.RedisIdempotencyCache",
            "build/ci/Invoke-RedisRebuildProbe.ps1",
            "Same live-Redis dependency as its sibling; the same probe replays a command whose " +
            "cached record was flushed, which must fall back to the durable record and answer " +
            "byte-identically."),

        new("SlayIdleRepeat.Adapters.ObjectStore.S3.S3BattleLogStore",
            "build/ci/Invoke-ObjectStoreProbe.ps1",
            "Its only backing is an S3-compatible store (MinIO in the stack). ⚠️ WEAKEST ENTRY, " +
            "recorded rather than smoothed over: no production path writes a battle log yet — the " +
            "battle milestone owns the first producer — so the probe asserts the adapter was " +
            "constructed against the live store and its drain is running, not a put/get. The gzip " +
            "and naming logic is unit-tested; the queued layer above it has a real in-repo fixture. " +
            "The task that lands the first battle-log producer owes this probe the real round-trip."),
    };

    /// <summary>Every entry that is not well formed. Empty means the register holds.</summary>
    /// <remarks>Parameterised so the self-tests can drive each arm; same construction as <c>PortCatalogue.Malformed</c>.</remarks>
    internal static IReadOnlyList<string> Malformed(IEnumerable<StoreBackedAdapterExemption> entries)
    {
        var offenders = new List<string>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Implementation))
            {
                offenders.Add($"an entry with probe '{entry.Probe}' names no implementation.");
            }

            if (!(entry.Probe ?? string.Empty).StartsWith("build/ci/", StringComparison.Ordinal) ||
                !(entry.Probe ?? string.Empty).EndsWith(".ps1", StringComparison.Ordinal))
            {
                offenders.Add(
                    $"'{entry.Implementation}' names probe '{entry.Probe}', which is not a pwsh script " +
                    "under build/ci/. The compose-boot job runs authored scripts from that folder and " +
                    "nowhere else, so an owner spelled differently is an owner the job cannot run.");
            }

            if (string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 60)
            {
                offenders.Add(
                    $"'{entry.Implementation}' carries no written reason worth falsifying. Every entry " +
                    "here claims a live store is the only honest fixture AND names what the probe " +
                    "actually asserts; without that, the next kickoff cannot check either half.");
            }
        }

        return offenders;
    }

    /// <summary>Every entry naming an implementation the scan does not find. Empty means the register holds.</summary>
    internal static IReadOnlyList<string> Unanchored(
        IEnumerable<StoreBackedAdapterExemption> entries,
        IEnumerable<Type> scannedImplementations)
    {
        var found = scannedImplementations.Select(t => t.FullName).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(entry => !found.Contains(entry.Implementation))
            .Select(entry =>
                $"'{entry.Implementation}' is exempted from the fixture demand, but the implementation " +
                "scan does not find it. An exemption for a type that is not there exempts nothing and " +
                "can only rot; if the adapter was renamed or deleted, move or delete this entry in the " +
                "same commit.")
            .ToArray();
    }

    /// <summary>Every entry whose implementation now HAS a fixture. Empty means no exemption outlived its need.</summary>
    internal static IReadOnlyList<string> Expired(
        IEnumerable<StoreBackedAdapterExemption> entries,
        IEnumerable<ContractSuiteCoverageTests.FixtureDeclaration> fixtures)
    {
        var covered = fixtures.Select(f => f.Implementation.FullName).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(entry => covered.Contains(entry.Implementation))
            .Select(entry =>
                $"'{entry.Implementation}' is exempted from the fixture demand, and a " +
                "[ContractFixtureFor] fixture for it now exists. The exemption is satisfied: delete " +
                "this entry in the commit that added the fixture — an exemption kept past its expiry " +
                "is the register saying a fixture is impossible while the fixture is in the build " +
                "(steering S4).")
            .ToArray();
    }

    /// <summary>
    /// Every entry whose owning probe is no longer OPEN: the script file is gone, or the
    /// compose-boot workflow no longer invokes it. Empty means every entry's owner still runs.
    /// </summary>
    /// <remarks>
    /// Steering S4's M4 amendment: existence is not an expiry. A probe file that survives while its
    /// workflow step is deleted is an owner that EXISTS and never RUNS — so both halves are checked,
    /// and the workflow text is matched on the script's own path spelling, which is how the steps
    /// invoke it.
    /// </remarks>
    internal static IReadOnlyList<string> OwnersNotWired(
        IEnumerable<StoreBackedAdapterExemption> entries,
        Func<string, bool> probeFileExists,
        string workflowText)
    {
        ArgumentNullException.ThrowIfNull(probeFileExists);
        ArgumentNullException.ThrowIfNull(workflowText);

        var offenders = new List<string>();

        foreach (var entry in entries)
        {
            if (!probeFileExists(entry.Probe))
            {
                offenders.Add(
                    $"'{entry.Implementation}' is owned by probe '{entry.Probe}', and no such file " +
                    "exists. The adapter is then exercised by nothing anywhere: restore the probe, or " +
                    "delete the adapter and this entry together.");
                continue;
            }

            if (!workflowText.Contains(entry.Probe, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"'{entry.Implementation}' is owned by probe '{entry.Probe}', which exists but is " +
                    $"not invoked anywhere in {WorkflowPath}. An owner that exists and never runs is " +
                    "not an owner; wire the step back into the compose-boot job, or retire the entry " +
                    "with the adapter.");
            }
        }

        return offenders;
    }

    /// <summary>The exempted full names, for the coverage rule's own quantifier.</summary>
    internal static IReadOnlyList<string> ExemptedImplementations(
        IEnumerable<StoreBackedAdapterExemption> entries) =>
        entries.Select(e => e.Implementation).ToArray();
}
