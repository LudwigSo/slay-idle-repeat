using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Analytics.PostHog;

/// <summary>
/// <see cref="IAnalyticsSinkPort"/> over PostHog's batch capture endpoint, hand-rolled over
/// <see cref="HttpClient"/> — the port boundary is the isolation, so no vendor SDK is pinned.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Track"/> never blocks and never throws for a backend problem: events queue in a
/// bounded in-memory buffer and are POSTed as one <c>/batch</c> JSON body. A full buffer drops the
/// oldest; a failed POST drops the batch. Disabled means every event is dropped on arrival.
/// </para>
/// <para>
/// The handler is injected so the wire behaviour is testable without a network.
/// </para>
/// </remarks>
public sealed class PostHogAnalyticsSink : IAnalyticsSinkPort, IDisposable
{
    // Roughly a busy hour of buffered events; beyond it the oldest are the least valuable.
    private const int QueueCapacity = 10_000;

    private readonly PostHogOptions _options;
    private readonly HttpClient _client;
    private readonly ConcurrentQueue<CaptureEntry> _queue = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    /// <summary>Builds the sink over its configuration and the HTTP handler it posts through.</summary>
    /// <param name="options">The adapter's configuration.</param>
    /// <param name="handler">The transport. Owned by the caller.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public PostHogAnalyticsSink(PostHogOptions options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        _options = options;
        _client = new HttpClient(handler, disposeHandler: false);
    }

    /// <inheritdoc/>
    public void Track(PlayerId player, AnalyticsEvent analyticsEvent)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);

        if (!_options.Enabled)
        {
            return;
        }

        _queue.Enqueue(new CaptureEntry(analyticsEvent.Name, player.Value, analyticsEvent.Properties));

        while (_queue.Count > QueueCapacity && _queue.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Drains the queue now — the background flush's own step, callable so composition can flush at
    /// shutdown and a test can flush deterministically. Never throws for a backend problem.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    public async Task FlushAsync(CancellationToken ct)
    {
        await _flushGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var batch = new List<CaptureEntry>();

            while (_queue.TryDequeue(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await PostAsync(batch, ct).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _flushGate.Dispose();
    }

    private async Task PostAsync(IReadOnlyList<CaptureEntry> batch, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new CapturePayload(_options.ProjectKey, batch));
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _client
                .PostAsync(_options.Host.TrimEnd('/') + "/batch", content, ct)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A backend that is down is the adapter's problem at its own edge: the batch is dropped.
        }
    }

    private sealed record CapturePayload(
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("batch")] IReadOnlyList<CaptureEntry> Batch);

    private sealed record CaptureEntry(
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("distinct_id")] string DistinctId,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, string> Properties);
}
