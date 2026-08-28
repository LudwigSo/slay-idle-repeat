namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// 🔒 The one repo-wide register of port implementations that carry NO in-repo contract fixture,
/// because exercising them needs live infrastructure or a vendor backend this repository has no
/// tier for. Each entry names the owner that observes the adapter instead, and expires by that
/// owner's status.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>ContractSuiteCoverageTests</c>' rule 2 demands a fixture for every
/// implementation of a port, and a fixture for a Postgres/Redis/object-store adapter would need
/// Docker in the unit tier — the tier decision (<c>$noIntegrationTier</c> in
/// <c>build/ci/test-suites.json</c>) rules that out categorically. What replaced the deleted tier is
/// the <c>compose-boot</c> CI job; an entry here says "THIS is where this adapter is exercised",
/// which is a claim a reader can falsify.
/// </para>
/// <para>
/// <b>One mechanism, three owner shapes</b> (steering S4: one mechanism per repo, not one per
/// task). M5-05 and M5-11 independently built this register under two names; this is the survivor,
/// widened so the observability adapters' owners fit without either side losing a direction.
/// <see cref="ExemptionOwnerKind"/> is the widening, and every kind carries a status check —
/// existence alone is never the check (S4's M4 amendment).
/// </para>
/// <para>
/// <b>The mechanism fails in five directions</b>, the same construction as <c>PortCatalogue</c>:
/// </para>
/// <list type="number">
///   <item><b>Malformed.</b> An entry whose owner does not have its kind's shape, whose reason is
///   too short to falsify, or that exempts one implementation twice — two entries for one type are
///   two answers to why it has no fixture, and deleting one would read as the exemption expiring
///   while the other kept it alive.</item>
///   <item><b>Unanchored.</b> An entry naming an implementation the scan does not find is a promise
///   about nothing. Full names, never simple ones: a decoy of the same simple name in another
///   adapter must not be able to excuse the type it shadows.</item>
///   <item><b>Expired.</b> An entry whose implementation now HAS a fixture fails the build — the
///   exemption was satisfied and must go (steering S4: a declared exception expires by itself,
///   including when satisfied).</item>
///   <item><b>Owner not open.</b> A probe file that is gone or no longer invoked, a workflow step
///   that no longer exists, or a vendor that is no longer disabled in the local stack. In each case
///   the entry's claim about where the adapter is observed has stopped being true.</item>
///   <item><b>Vacuous.</b> The subject set has an identity floor: every exempted adapter is named
///   one by one in the register's own tests, <em>with its expected owner kind</em>, so the register
///   cannot quietly empty and a store-backed adapter cannot be downgraded to "deployment only".</item>
/// </list>
/// <para>
/// ⚠️ <b>The known limits</b>, stated rather than smoothed over. What a probe actually asserts is
/// not decidable here — the object-store entry is the honest weak case, and its reason says so.
/// Neither is what a deployment actually observes: a <see cref="ExemptionOwnerKind.DeploymentOnly"/>
/// entry's status check is that the adapter is still disabled locally, which is why no local
/// observation can exist; it is not evidence that anybody is watching the vendor dashboard.
/// Re-read these at each kickoff.
/// </para>
/// </remarks>
internal static class StoreBackedAdapterExemptions
{
    /// <summary>What kind of owner an entry names, and therefore how its status is checked.</summary>
    internal enum ExemptionOwnerKind
    {
        /// <summary>A <c>build/ci/*.ps1</c> probe: the file must exist AND the workflow must run it.</summary>
        CiProbeScript,

        /// <summary>A named step in the workflow: its name must still appear there, verbatim.</summary>
        CiWorkflowStep,

        /// <summary>
        /// No local or CI observation exists, honestly. The owner is the local stack's own setting
        /// that disables the vendor, which is WHY none can exist; that setting must still be there.
        /// </summary>
        DeploymentOnly,
    }

    /// <summary>
    /// One exempted implementation: who, what observes it instead of a fixture, and why no in-repo
    /// fixture can.
    /// </summary>
    /// <param name="Implementation">The implementation's full type name.</param>
    /// <param name="OwnerKind">How <paramref name="Owner"/> is spelled and how its status is read.</param>
    /// <param name="Owner">
    /// The owner literal: a probe path under <c>build/ci/</c>, a workflow step name, or the local
    /// stack setting that disables the vendor — matched into the file that owns it.
    /// </param>
    /// <param name="Why">Why a fixture cannot exist in this repository — falsifiable at a kickoff.</param>
    internal sealed record StoreBackedAdapterExemption(
        string Implementation,
        ExemptionOwnerKind OwnerKind,
        string Owner,
        string Why);

    /// <summary>The compose-boot workflow the probes and steps must live in.</summary>
    internal const string WorkflowPath = ".github/workflows/ci.yml";

    /// <summary>The local stack a <see cref="ExemptionOwnerKind.DeploymentOnly"/> owner is read from.</summary>
    internal const string LocalStackPath = "docker-compose.yml";

    /// <summary>
    /// The Prometheus scrape job the OTel entry's step actually requires. Pinned separately because
    /// the step could survive while the assertion inside it stopped naming the collector's app road.
    /// </summary>
    internal const string OtelScrapeJob = "otel-collector-app";

    /// <summary>🔒 Every exempted implementation, its owner and its reason.</summary>
    internal static readonly StoreBackedAdapterExemption[] Entries =
    {
        new("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresPlayerRepository",
            ExemptionOwnerKind.CiProbeScript,
            "build/ci/Invoke-CommandRoundTripProbe.ps1",
            "Its only backing is a live Postgres; a fixture would need the compose stack in the " +
            "unit tier. The probe posts a real command through the API and asserts the player row " +
            "landed — the JSONB document plus the typed columns — by querying the database in the " +
            "compose-boot job."),

        new("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresRunStateStore",
            ExemptionOwnerKind.CiProbeScript,
            "build/ci/Invoke-CommandRoundTripProbe.ps1",
            "Its only backing is a live Postgres. The same round-trip probe covers it: the posted " +
            "command's save upserts the run row beside the player row, and the probe asserts both."),

        new("SlayIdleRepeat.Adapters.Persistence.Postgres.PostgresIdempotencyStore",
            ExemptionOwnerKind.CiProbeScript,
            "build/ci/Invoke-IdempotentReplayProbe.ps1",
            "Its only backing is a live Postgres, and its one atomic effect (record plus sequence " +
            "advance) is only observable against one. The probe replays a committed command across " +
            "an API restart and demands the byte-identical stored response — which only the durable " +
            "record can produce."),

        new("SlayIdleRepeat.Adapters.Cache.Redis.RedisRunStateCache",
            ExemptionOwnerKind.CiProbeScript,
            "build/ci/Invoke-RedisRebuildProbe.ps1",
            "Its policy layer is unit-tested over a fake byte surface, but the vendor-speaking " +
            "class underneath needs a live Redis. The probe flushes Redis mid-session and demands " +
            "the next command still succeed — the rebuild-from-Postgres claim, made against the " +
            "real pair."),

        new("SlayIdleRepeat.Adapters.Cache.Redis.RedisIdempotencyCache",
            ExemptionOwnerKind.CiProbeScript,
            "build/ci/Invoke-RedisRebuildProbe.ps1",
            "Same live-Redis dependency as its sibling; the same probe replays a command whose " +
            "cached record was flushed, which must fall back to the durable record and answer " +
            "byte-identically."),

        new("SlayIdleRepeat.Adapters.ObjectStore.S3.S3BattleLogStore",
            ExemptionOwnerKind.CiProbeScript,
            "build/ci/Invoke-ObjectStoreProbe.ps1",
            "Its only backing is an S3-compatible store (MinIO in the stack). ⚠️ WEAKEST PROBE " +
            "ENTRY, recorded rather than smoothed over: no production path writes a battle log yet " +
            "— the battle milestone owns the first producer — so the probe asserts the adapter was " +
            "constructed against the live store and its drain is running, not a put/get. The gzip " +
            "and naming logic is unit-tested; the queued layer above it has a real in-repo fixture. " +
            "The task that lands the first battle-log producer owes this probe the real round-trip."),

        new("SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry.OpenTelemetryTelemetry",
            ExemptionOwnerKind.CiWorkflowStep,
            "Assert every telemetry scrape target is up",
            "Its meaning is export to a collector over OTLP, which no unit tier holds: without a " +
            "collector a span goes nowhere and looks identical to one that was never begun. " +
            "OpenTelemetryTelemetryTests observes the ActivitySource/Meter half in process; the " +
            "collector half is this compose-boot step, which requires the '" + OtelScrapeJob +
            "' scrape job to be up — the road these spans and metrics travel, asserted on every CI " +
            "run even while the server emits nothing of its own."),

        new("SlayIdleRepeat.Adapters.Telemetry.Sentry.SentryTelemetry",
            ExemptionOwnerKind.DeploymentOnly,
            "Sentry__Dsn: ''",
            "The Sentry SDK needs a DSN to send anything, and the local stack disables the adapter " +
            "by the SDK's own documented convention — the empty DSN this owner names. A fixture " +
            "against a disabled SDK asserts a no-op; one against a live DSN is a network test this " +
            "repository's tiers forbid, and no CI probe exercises Sentry today. ⚠️ Capture is " +
            "therefore observed at DEPLOYMENT only: SentryTelemetryTests covers the argument paths " +
            "that need no DSN, and an exception nobody receives looks identical to one captured. " +
            "Do not invent a probe for this; give it one only when a CI step really runs it."),

        new("SlayIdleRepeat.Adapters.Analytics.PostHog.PostHogAnalyticsSink",
            ExemptionOwnerKind.DeploymentOnly,
            "PostHog__Enabled: 'false'",
            "A vendor HTTP sink. A fixture would need a project key and a PostHog backend to " +
            "answer, and without one it would prove buffering rather than delivery — the hollow " +
            "shape 23 §5 A5 refuses. The local stack disables it outright via the setting this " +
            "owner names, so there is nothing a fixture or a probe could reach. ⚠️ Delivery is " +
            "therefore observed at DEPLOYMENT only; PostHogAnalyticsSinkTests pins the wire " +
            "behaviour — the /batch payload shape, buffering, the disabled drop and the " +
            "never-throwing Track — through a recording HttpMessageHandler, which is the half that " +
            "IS observable here."),
    };

    /// <summary>Every entry that is not well formed. Empty means the register holds.</summary>
    /// <remarks>Parameterised so the self-tests can drive each arm; same construction as <c>PortCatalogue.Malformed</c>.</remarks>
    internal static IReadOnlyList<string> Malformed(IEnumerable<StoreBackedAdapterExemption> entries)
    {
        var all = entries.ToArray();
        var offenders = new List<string>();

        foreach (var entry in all)
        {
            if (string.IsNullOrWhiteSpace(entry.Implementation))
            {
                offenders.Add($"an entry with owner '{entry.Owner}' names no implementation.");
            }

            offenders.AddRange(MalformedOwner(entry));

            if (string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 60)
            {
                offenders.Add(
                    $"'{entry.Implementation}' carries no written reason worth falsifying. Every entry " +
                    "here claims live infrastructure or a vendor backend is the only honest fixture " +
                    "AND names what its owner actually observes; without that, the next kickoff " +
                    "cannot check either half.");
            }
        }

        offenders.AddRange(
            all.GroupBy(e => e.Implementation, StringComparer.Ordinal)
               .Where(g => g.Count() > 1)
               .Select(g =>
                   $"'{g.Key}' is exempted {g.Count()} times. Two entries for one type are two " +
                   "answers to why it has no fixture, and deleting one would read as the exemption " +
                   "expiring while the other kept it alive."));

        return offenders;
    }

    private static IEnumerable<string> MalformedOwner(StoreBackedAdapterExemption entry)
    {
        var owner = entry.Owner ?? string.Empty;

        switch (entry.OwnerKind)
        {
            case ExemptionOwnerKind.CiProbeScript
                when !owner.StartsWith("build/ci/", StringComparison.Ordinal) ||
                     !owner.EndsWith(".ps1", StringComparison.Ordinal):
                yield return
                    $"'{entry.Implementation}' names probe '{owner}', which is not a pwsh script " +
                    "under build/ci/. The compose-boot job runs authored scripts from that folder and " +
                    "nowhere else, so an owner spelled differently is an owner the job cannot run.";
                break;

            case ExemptionOwnerKind.CiWorkflowStep when owner.Trim().Length < 20:
                yield return
                    $"'{entry.Implementation}' names workflow step '{owner}'. A step owner is the " +
                    $"step's name copied verbatim out of {WorkflowPath}; a fragment matches text " +
                    "that is not a step and turns the status check into a coincidence.";
                break;

            case ExemptionOwnerKind.DeploymentOnly
                when !owner.Contains("__", StringComparison.Ordinal):
                yield return
                    $"'{entry.Implementation}' is exempted as deployment-only, and '{owner}' is not " +
                    $"a configuration setting from {LocalStackPath}. A deployment-only entry has no " +
                    "CI owner to check, so the ONE thing holding it honest is the local setting that " +
                    "disables the vendor — spelled `Key__Sub: value`, exactly as the stack spells it.";
                break;
        }
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
                "(steering S4). Note the suite's fixture floor snaps back with it, which is the point.")
            .ToArray();
    }

    /// <summary>
    /// Every entry whose owner is no longer OPEN: a probe script that is gone or that the workflow
    /// no longer invokes, a workflow step that no longer exists, or a vendor the local stack no
    /// longer disables. Empty means every entry's owner still holds.
    /// </summary>
    /// <remarks>
    /// Steering S4's M4 amendment: existence is not an expiry. A probe file that survives while its
    /// workflow step is deleted is an owner that EXISTS and never RUNS — so both halves are checked,
    /// and the workflow text is matched on the script's own path spelling, which is how the steps
    /// invoke it. A deployment-only owner has no CI status to read at all, so what is checked is the
    /// premise instead: the vendor is still switched off locally, which is the entire reason no
    /// local observation can exist. Turn it on and this fires, which is correct — the entry then
    /// needs re-reading, not keeping.
    /// </remarks>
    internal static IReadOnlyList<string> OwnersNotWired(
        IEnumerable<StoreBackedAdapterExemption> entries,
        Func<string, bool> probeFileExists,
        string workflowText,
        string localStackText)
    {
        ArgumentNullException.ThrowIfNull(probeFileExists);
        ArgumentNullException.ThrowIfNull(workflowText);
        ArgumentNullException.ThrowIfNull(localStackText);

        var offenders = new List<string>();

        foreach (var entry in entries)
        {
            switch (entry.OwnerKind)
            {
                case ExemptionOwnerKind.CiProbeScript when !probeFileExists(entry.Owner):
                    offenders.Add(
                        $"'{entry.Implementation}' is owned by probe '{entry.Owner}', and no such file " +
                        "exists. The adapter is then exercised by nothing anywhere: restore the probe, or " +
                        "delete the adapter and this entry together.");
                    break;

                case ExemptionOwnerKind.CiProbeScript
                    when !workflowText.Contains(entry.Owner, StringComparison.Ordinal):
                    offenders.Add(
                        $"'{entry.Implementation}' is owned by probe '{entry.Owner}', which exists but is " +
                        $"not invoked anywhere in {WorkflowPath}. An owner that exists and never runs is " +
                        "not an owner; wire the step back into the compose-boot job, or retire the entry " +
                        "with the adapter.");
                    break;

                case ExemptionOwnerKind.CiWorkflowStep
                    when !workflowText.Contains(entry.Owner, StringComparison.Ordinal):
                    offenders.Add(
                        $"'{entry.Implementation}' is owned by the workflow step '{entry.Owner}', and " +
                        $"{WorkflowPath} no longer carries it. The adapter is then observed by NOTHING " +
                        "— no fixture, no infrastructure assertion — which is the state this register " +
                        "exists to forbid. Restore the step, or give the entry an owner that exists.");
                    break;

                case ExemptionOwnerKind.DeploymentOnly
                    when !localStackText.Contains(entry.Owner, StringComparison.Ordinal):
                    offenders.Add(
                        $"'{entry.Implementation}' is exempted as observable only at deployment because " +
                        $"{LocalStackPath} disables it with '{entry.Owner}', and that setting is no " +
                        "longer there. Either the vendor is now live locally — in which case something " +
                        "CAN observe it and this entry owes a real owner — or the setting was renamed " +
                        "and this entry must be renamed with it.");
                    break;
            }
        }

        return offenders;
    }

    /// <summary>The exempted full names, for the coverage rule's own quantifier.</summary>
    internal static IReadOnlyList<string> ExemptedImplementations(
        IEnumerable<StoreBackedAdapterExemption> entries) =>
        entries.Select(e => e.Implementation).ToArray();
}
