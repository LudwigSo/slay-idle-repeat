namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// 🔒 The register of port implementations that carry <b>no runnable contract fixture</b>, each with
/// the observation that covers it instead and a reason a later reader can falsify.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this exists, and why it is not a hole punched through X-06.</b>
/// <c>23</c> §5 A8 demands a fixture per implementation, and
/// <c>ContractSuiteCoverageTests.Every_implementation_of_a_port_has_a_contract_fixture</c> enforces
/// it. A vendor adapter whose whole meaning is delivery to a backend — a sink POSTing to PostHog, an
/// exporter feeding a collector, a client needing a DSN — cannot honour that demand in this
/// repository's one test tier: a fixture would need the backend, and a fixture without it proves
/// buffering, not delivery, which is <c>23</c> §5 A5's hollow-fake shape wearing a green tick. Each
/// entry therefore names the thing that DOES observe the adapter — a unit suite over its wire
/// behavior, a CI infrastructure assertion, or an honest "deployment only" — so the exemption is a
/// recorded, owned decision rather than a fixture nobody noticed was missing.
/// </para>
/// <para>
/// 🔒 <b>One mechanism repo-wide (steering S4).</b> M5-05's store adapters (Postgres, Redis, S3)
/// will face the identical demand and extend THIS register rather than growing a sibling. An entry
/// is three fields, and all three are enforced: the implementation must resolve to a scanned port
/// implementation that has no fixture (<see cref="Satisfied"/> / <see cref="Unanchored"/>), and the
/// reason must be long enough to falsify (<see cref="Malformed"/>). The OTel entry's covering
/// observation is additionally pinned to the CI workflow's own text — see
/// <c>FixtureExemptionTests.The_otel_covering_observation_still_exists_in_the_ci_workflow</c> — so
/// deleting the scrape assertion un-covers the adapter loudly.
/// </para>
/// <para>
/// ⚠️ <b>The known limit</b>, stated once: what is decidable is the shape — the fixture arrived, the
/// type left, the reason is missing. An entry whose written <i>reason</i> stopped being true while
/// its predicate still holds is the next milestone kickoff's to re-read; CI is not doing it for you.
/// </para>
/// </remarks>
internal static class FixtureExemptions
{
    /// <summary>
    /// A port implementation deliberately carrying no contract fixture.
    /// </summary>
    /// <param name="Implementation">The implementation's FULL type name — full, not simple, so a
    /// decoy of the same simple name in another adapter cannot satisfy the entry.</param>
    /// <param name="CoveringObservation">What observes this adapter instead of a fixture.</param>
    /// <param name="Why">Why a fixture is impossible or dishonest here. Falsifiable.</param>
    internal sealed record FixtureExemption(string Implementation, string CoveringObservation, string Why);

    /// <summary>The exemption the OTel adapter rests on, named once so the pin and the entry agree.</summary>
    internal const string OtelScrapeJob = "otel-collector-app";

    /// <summary>The CI step the OTel entry names, verbatim from the workflow.</summary>
    internal const string OtelScrapeStep = "Assert every telemetry scrape target is up";

    /// <summary>🔒 Every exempted implementation, with its covering observation. Each entry expires by itself.</summary>
    internal static readonly FixtureExemption[] Entries =
    {
        new("SlayIdleRepeat.Adapters.Analytics.PostHog.PostHogAnalyticsSink",
            "PostHogAnalyticsSinkTests pins the wire behavior — the /batch payload shape, buffering, "
            + "the disabled drop and never-throwing Track — through a recording HttpMessageHandler.",
            "A vendor HTTP sink. A fixture would need a project key and a PostHog backend to answer, "
            + "and without one it would prove buffering rather than delivery — the hollow shape 23 §5 "
            + "A5 refuses. Delivery is observed only in deployment; locally the adapter is "
            + "config-disabled outright via PostHog:Enabled=false, so there is nothing a fixture "
            + "could reach."),

        new("SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry.OpenTelemetryTelemetry",
            "The CI compose-boot step '" + OtelScrapeStep + "' in .github/workflows/ci.yml, which "
            + "requires the scrape job '" + OtelScrapeJob + "' to be up — the road this adapter's "
            + "spans and metrics travel, asserted on every CI run even while the server emits nothing.",
            "Its meaning is export to a collector over OTLP, which no unit tier holds: without a "
            + "collector a span goes nowhere and looks identical to one that was never begun. "
            + "OpenTelemetryTelemetryTests observes the ActivitySource/Meter half in process; the "
            + "collector half is the CI infrastructure assertion this entry names."),

        new("SlayIdleRepeat.Adapters.Telemetry.Sentry.SentryTelemetry",
            "SentryTelemetryTests covers the argument paths that need no DSN; end-to-end capture is "
            + "deferred to deployment, honestly — no local or CI observation exists today.",
            "The Sentry SDK needs a DSN to send anything, and locally the adapter is disabled by the "
            + "SDK's own documented convention, Sentry:Dsn=''. A fixture against a disabled SDK "
            + "asserts a no-op; one against a live DSN is a network test this repository's tiers "
            + "forbid. An exception nobody receives looks identical to one that was captured."),
    };

    /// <summary>What a satisfied exemption means, said once.</summary>
    internal const string SatisfiedConsequence =
        "A contract fixture now runs the shared suite against it, so this entry describes a decision "
        + "that has been reversed. Delete it in the commit that added the fixture — and note the "
        + "suite's fixture floor snaps back with it, which is the point.";

    /// <summary>What an unanchored exemption means, said once.</summary>
    internal const string UnanchoredConsequence =
        "An exemption is a promise about a real port implementation. One naming a type the scan does "
        + "not return as a port implementation exempts nothing and can never be satisfied — only "
        + "deleted by hand. If the type was renamed or moved, rename it here in the same commit; if "
        + "it stopped implementing the port, delete the entry.";

    /// <summary>
    /// Every entry whose implementation now HAS a fixture. Empty means the register holds.
    /// </summary>
    /// <remarks>
    /// Parameterised over both inputs, the <c>PortCatalogue.Expired</c> construction: the self-tests
    /// drive it with a deliberately satisfied entry without one ever being committed.
    /// </remarks>
    internal static IReadOnlyList<string> Satisfied(
        IEnumerable<FixtureExemption> entries,
        IEnumerable<ContractSuiteCoverageTests.FixtureDeclaration> fixtures)
    {
        var fixtured = fixtures
            .Select(f => f.Implementation.FullName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(entry => fixtured.Contains(entry.Implementation))
            .Select(entry => $"'{entry.Implementation}' is exempted from the fixture demand, but a "
                             + $"[ContractFixtureFor] class names it. {SatisfiedConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every entry naming a type the implementation scan does not return. Empty means the register
    /// holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Satisfied"/>.</remarks>
    internal static IReadOnlyList<string> Unanchored(
        IEnumerable<FixtureExemption> entries,
        IEnumerable<Type> scannedImplementations)
    {
        var scanned = scannedImplementations
            .Select(t => t.FullName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(entry => !scanned.Contains(entry.Implementation))
            .Select(entry => $"'{entry.Implementation}' is exempted, but the implementation scan "
                             + $"returns no port implementation of that full name. {UnanchoredConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every entry that is not well formed: a blank implementation, no covering observation, no
    /// reason worth falsifying, or a duplicated implementation. Empty means the register holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Satisfied"/>.</remarks>
    internal static IReadOnlyList<string> Malformed(IEnumerable<FixtureExemption> entries)
    {
        var all = entries.ToArray();
        var offenders = new List<string>();

        foreach (var entry in all)
        {
            if (string.IsNullOrWhiteSpace(entry.Implementation))
            {
                offenders.Add("an entry names no implementation, so it exempts nothing and expires never.");
            }

            if (string.IsNullOrWhiteSpace(entry.CoveringObservation) || entry.CoveringObservation.Length < 40)
            {
                offenders.Add(
                    $"'{entry.Implementation}' names no covering observation worth checking. The whole "
                    + "bargain of an exemption is that SOMETHING ELSE observes the adapter — a unit "
                    + "suite, a CI assertion, or an honest 'deployment only' — and the entry has to "
                    + "say which, or the next kickoff cannot verify it still exists.");
            }

            if (string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 40)
            {
                offenders.Add(
                    $"'{entry.Implementation}' carries no written reason worth falsifying. Every "
                    + "exemption here is a 23 §5 A5 argument — a fixture would need a backend, a "
                    + "key or a DSN — and the entry has to say WHICH, or it is a formula.");
            }
        }

        offenders.AddRange(
            all.GroupBy(e => e.Implementation, StringComparer.Ordinal)
               .Where(g => g.Count() > 1)
               .Select(g => $"'{g.Key}' is exempted {g.Count()} times. Two entries for one type are "
                            + "two answers to why it has no fixture, and deleting one would read as "
                            + "the exemption expiring while the other kept it alive."));

        return offenders;
    }
}
