using System.Net;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Adapters.Analytics.PostHog;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// The PostHog adapter's own wire behaviour, observed through a recording handler and never a
/// network: batch shape, buffering, the disabled drop, and the never-throws promise.
/// </summary>
public sealed class PostHogAnalyticsSinkTests
{
    private const string Host = "https://posthog.test";
    private const string Key = "phc_test_key";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly PlayerId Player = new("PLAYER_wire");

    private static PostHogOptions Enabled() => new() { Enabled = true, Host = Host, ProjectKey = Key };

    private static AnalyticsEvent Event(string name) =>
        new(name, new Dictionary<string, string> { ["face"] = "3", ["source"] = "rolled" });

    /// <summary>A handler that keeps every request and answers 200, so the wire is observable offline.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal List<(HttpMethod Method, Uri? Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add((request.Method, request.RequestUri, body));

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>A handler standing in for a backend that is down.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("the backend is down");
    }

    [Fact]
    public async Task A_tracked_event_flushes_as_one_authenticated_post_to_the_batch_endpoint()
    {
        var handler = new RecordingHandler();
        using var sink = new PostHogAnalyticsSink(Enabled(), handler);

        sink.Track(Player, Event("die_rolled"));
        await sink.FlushAsync(Cancel);

        var (method, uri, body) = handler.Requests.ShouldHaveSingleItem("one flush is one POST.");

        method.ShouldBe(HttpMethod.Post, "the capture endpoint takes a POST.");
        uri.ShouldNotBeNull();
        uri.GetLeftPart(UriPartial.Path).ShouldBe(
            Host + "/batch", "the configured host's batch endpoint, nothing hard-coded.");

        using var payload = JsonDocument.Parse(body);

        payload.RootElement.GetProperty("api_key").GetString().ShouldBe(
            Key, "the project key authenticates the batch.");

        var entry = payload.RootElement.GetProperty("batch").EnumerateArray()
            .ShouldHaveSingleItem("one tracked event is one batch entry.");

        entry.GetProperty("event").GetString().ShouldBe("die_rolled", "the vocabulary name, verbatim.");
        entry.GetProperty("distinct_id").GetString().ShouldBe(
            Player.Value, "the player IS the distinct id — attribution is the port's whole promise.");
        entry.GetProperty("properties").GetProperty("face").GetString().ShouldBe(
            "3", "properties travel as given, values already rendered upstream.");
        entry.GetProperty("properties").GetProperty("source").GetString().ShouldBe("rolled");
    }

    [Fact]
    public async Task Buffered_events_flush_as_one_batch_in_track_order()
    {
        var handler = new RecordingHandler();
        using var sink = new PostHogAnalyticsSink(Enabled(), handler);

        sink.Track(Player, Event("first_probe"));
        sink.Track(Player, Event("second_probe"));
        await sink.FlushAsync(Cancel);

        var (_, _, body) = handler.Requests.ShouldHaveSingleItem(
            "buffering is the point: two tracks are one POST, not two.");

        using var payload = JsonDocument.Parse(body);

        payload.RootElement.GetProperty("batch").EnumerateArray()
            .Select(entry => entry.GetProperty("event").GetString())
            .ShouldBe(
                new[] { "first_probe", "second_probe" },
                "both events, in the order they were tracked.");
    }

    [Fact]
    public async Task A_disabled_sink_drops_every_event_and_never_posts()
    {
        var handler = new RecordingHandler();
        using var sink = new PostHogAnalyticsSink(
            new PostHogOptions { Enabled = false, Host = Host, ProjectKey = Key }, handler);

        sink.Track(Player, Event("die_rolled"));
        await sink.FlushAsync(Cancel);

        handler.Requests.ShouldBeEmpty(
            "Enabled=false is the deployment's opt-out, and a sink that still posts leaks events " +
            "to a backend the operator said no to.");
    }

    [Fact]
    public async Task Track_and_flush_never_throw_when_the_backend_is_down()
    {
        using var sink = new PostHogAnalyticsSink(Enabled(), new FailingHandler());

        Should.NotThrow(
            () => sink.Track(Player, Event("die_rolled")),
            "Track never blocks and never throws — the queue absorbs the backend's state.");
        await Should.NotThrowAsync(
            () => sink.FlushAsync(Cancel),
            "a failed POST drops the batch at the adapter's edge; analytics down must never " +
            "surface as an exception.");
    }

    [Fact]
    public void Construction_refuses_null_options()
    {
        Should.Throw<ArgumentNullException>(() => new PostHogAnalyticsSink(null!, new RecordingHandler()));
    }

    [Fact]
    public void Construction_refuses_a_null_handler()
    {
        Should.Throw<ArgumentNullException>(() => new PostHogAnalyticsSink(Enabled(), null!));
    }
}
