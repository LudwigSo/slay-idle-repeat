using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SlayIdleRepeat.Integration.Tests;

/// <summary>
/// The local development stack, under test (M0-03).
/// </summary>
/// <remarks>
/// <para>
/// 14 §1.1 🔒 states the requirement and the way it is checked in one breath:
/// </para>
/// <para>
///   <c>Local development | `docker compose up` brings the entire stack — API,
///   Postgres, Redis, MinIO, observability — up on a laptop with no cloud account.</c><br/>
///   <c>Portability test | CI builds and boots the full stack in Docker Compose on
///   every commit. If it cannot run on a laptop, it is locked in.</c>
/// </para>
/// <para>
/// <b>Why these tests exist at all.</b> The compose-boot CI job runs this suite
/// against the live stack. Until now the suite contained zero tests, and
/// <c>dotnet test</c> exits 0 on an empty assembly — so that step was a green tick
/// over nothing, which is precisely the vacuous pass
/// <c>build/ci/Invoke-UnitTests.ps1</c> and <c>build/ci/test-suites.json</c> were
/// written to prevent. Rather than leave the suite declared <c>knownEmpty</c>
/// against a milestone that has now arrived, M0-03 puts its own acceptance
/// criteria in here, where they will keep being checked.
/// </para>
/// <para>
/// <b>Scope, deliberately narrow.</b> These assert that the stack is up and wired
/// — the API answers, the object store answers, telemetry is being scraped. They
/// assert nothing about the game, because there is no game surface yet. The real
/// end-to-end tests (14 §13: "Full run played end-to-end against a Docker Compose
/// stack in CI") arrive with the adapters in M5-05 and after. Add them alongside
/// these; do not repurpose these.
/// </para>
/// <para>
/// <b>They need the stack running.</b> That is the point, and it is why the suite
/// is excluded from the <c>unit</c> group in <c>build/ci/test-suites.json</c> and
/// runs only in the compose-boot job. Locally:
/// <c>docker compose up -d --wait</c> first. Every endpoint can be pointed
/// elsewhere with the environment variables below, for a stack brought up on
/// non-default ports via <c>.env</c>.
/// </para>
/// </remarks>
public sealed class ComposeStackSmokeTests
{
    // One HttpClient for the class: creating one per test is the classic socket
    // exhaustion bug, and these tests are the first place in the repo where
    // anyone would copy an HTTP calling pattern from.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string Endpoint(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

    /// <summary>SlayIdleRepeat.Server. Must match SIR_API_PORT in .env.example.</summary>
    private static string ApiBaseUrl => Endpoint("SIR_TEST_API_URL", "http://127.0.0.1:8080");

    /// <summary>MinIO's S3 API. Must match SIR_MINIO_API_PORT in .env.example.</summary>
    private static string ObjectStoreBaseUrl => Endpoint("SIR_TEST_MINIO_URL", "http://127.0.0.1:9000");

    /// <summary>Prometheus. Must match SIR_PROMETHEUS_PORT in .env.example.</summary>
    private static string PrometheusBaseUrl => Endpoint("SIR_TEST_PROMETHEUS_URL", "http://127.0.0.1:9090");

    private const string StackHint =
        "Bring the stack up first: `docker compose up -d --wait` from the repository root. " +
        "If it is up on non-default ports, set SIR_TEST_API_URL / SIR_TEST_MINIO_URL / SIR_TEST_PROMETHEUS_URL.";

    /// <summary>
    /// 14 §14 "boot the Docker Compose stack" — the same assertion the compose-boot
    /// job makes with build/ci/Wait-ForHttpOk.ps1, kept here so the suite that runs
    /// against the stack is never empty.
    /// </summary>
    [Fact]
    public async Task Api_answers_health_with_200_and_status_ok()
    {
        var response = await GetAsync($"{ApiBaseUrl}/health");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "GET /health is the readiness contract the compose healthcheck, the CI job and every " +
            "future deployment probe rely on. {0}", StackHint);

        var body = await response.Content.ReadAsStringAsync();

        // Parsed, not string-matched: the contract is a JSON document with a
        // `status` member, not a particular byte sequence. A test that greps for
        // the literal text would start failing the day someone adds a field.
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("status", out var status)
            .Should().BeTrue("GET /health must return a JSON object with a `status` member; got: {0}", body);
        status.GetString().Should().Be("ok");
    }

    /// <summary>
    /// 14 §7.1: "S3-compatible object storage | Battle logs for replay, ghost
    /// snapshots over a size threshold | MinIO locally and self-hosted".
    /// </summary>
    /// <remarks>
    /// Deliberately the unauthenticated liveness probe rather than an S3 call: an
    /// S3 call needs an S3 client, and 14 §1.1 🔒 says the S3 client lives only in
    /// <c>Adapters.ObjectStore.S3</c>. Referencing one from a test project would
    /// break that rule (and <c>Test-VendorPackageUniqueness.ps1</c>). The bucket
    /// contents get asserted through <c>IBattleLogStore</c> once M5-05 writes the
    /// adapter — through the port, in domain language (23 A4).
    /// </remarks>
    [Fact]
    public async Task Object_store_answers_its_liveness_probe()
    {
        var response = await GetAsync($"{ObjectStoreBaseUrl}/minio/health/live");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the object store must be reachable for battle logs and ghost snapshots (14 §7.1). {0}",
            StackHint);
    }

    /// <summary>
    /// 14 §10: "Metrics &amp; tracing | OpenTelemetry → Prometheus + Grafana".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserts the telemetry ROAD, not the traffic: every Prometheus scrape target
    /// is up, including the collector's OTLP-fed application endpoint. The server
    /// emits no telemetry of its own until M5-11, so there is nothing else to
    /// assert yet — but a broken collector or a mistyped scrape config would make
    /// M5-11 look broken instead, and this catches that here.
    /// </para>
    /// <para>
    /// When M5-11 lands, tighten this into an assertion about actual server spans
    /// and metrics rather than adding a second test beside it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Telemetry_targets_are_all_being_scraped()
    {
        var response = await GetAsync($"{PrometheusBaseUrl}/api/v1/query?query=up");
        response.StatusCode.Should().Be(HttpStatusCode.OK, StackHint);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = document.RootElement.GetProperty("data").GetProperty("result");

        var down = new List<string>();
        var jobs = new List<string>();
        foreach (var series in results.EnumerateArray())
        {
            var job = series.GetProperty("metric").GetProperty("job").GetString() ?? "(unnamed)";
            jobs.Add(job);

            // Prometheus returns the sample as [ <unix ts>, "<value as string>" ].
            var value = series.GetProperty("value")[1].GetString();
            if (value != "1")
            {
                down.Add($"{job}={value}");
            }
        }

        jobs.Should().Contain(
            "otel-collector-app",
            "the job that carries application telemetry out of the collector must be configured in " +
            "infra/prometheus/prometheus.yml — it is empty until M5-11, but it must exist and be scraped");

        down.Should().BeEmpty(
            "every Prometheus scrape target must be up; scraped jobs were [{0}]. {1}",
            string.Join(", ", jobs), StackHint);
    }

    private static async Task<HttpResponseMessage> GetAsync(string url)
    {
        try
        {
            return await Http.GetAsync(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The default failure — "connection refused" from deep inside
            // HttpClient — tells you nothing about what you were supposed to have
            // started. Say it plainly instead.
            throw new InvalidOperationException($"Could not reach {url}. {StackHint}", ex);
        }
    }
}
