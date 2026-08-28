using System.Threading.Channels;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>The write-behind layer over a battle-log store: puts enqueue, a drain uploads, a full queue drops and counts.</summary>
/// <remarks>
/// <para>
/// Battle logs are written asynchronously and their loss never blocks progress: <see cref="PutAsync"/>
/// returns the moment the log is queued (or dropped), whatever the store underneath is doing. Reads
/// go straight through — a log still in the queue is simply not readable yet, which is the port's
/// stated acceptance-versus-readability latitude.
/// </para>
/// <para>
/// Vendor-free on purpose: it decorates any <see cref="IBattleLogStore"/>, so its policy is
/// unit-testable over the in-memory fake and reusable by the sibling backing when that lands.
/// </para>
/// <para>
/// An upload that throws drops that one log and counts it — the same accounting as a full queue,
/// because to the caller the two are the same event: a log that will never be readable.
/// </para>
/// </remarks>
public sealed class QueuedBattleLogStore : IBattleLogStore
{
    private readonly IBattleLogStore _inner;
    private readonly BattleLogLossCounter _losses;
    private readonly Channel<(BattleLogId Id, byte[] Log)> _queue;

    /// <summary>Builds the queue over a store.</summary>
    /// <param name="inner">The store uploads land in.</param>
    /// <param name="capacity">How many logs may wait. Positive. When full, a put drops its log and counts it.</param>
    /// <param name="losses">Where drops are counted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="losses"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is zero or negative.</exception>
    public QueuedBattleLogStore(IBattleLogStore inner, int capacity, BattleLogLossCounter losses)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(losses);

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity), capacity, "A queue that can hold nothing drops every log it is for.");
        }

        _inner = inner;
        _losses = losses;
        // The default Wait mode is never reached: puts go through TryWrite, which answers false on
        // a full queue instead of blocking — so a full queue costs the LOG (counted), never the
        // command's latency. DropWrite would swallow the same log with TryWrite answering true,
        // leaving the loss uncounted.
        _queue = Channel.CreateBounded<(BattleLogId, byte[])>(new BoundedChannelOptions(capacity));
    }

    /// <inheritdoc/>
    public Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        if (!_queue.Writer.TryWrite((id, log.ToArray())))
        {
            _losses.Increment();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct)
    {
        RequireId(id);

        return _inner.GetAsync(id, ct);
    }

    /// <summary>Uploads everything queued right now and returns how many logs landed. The deterministic drain tests and fixtures use.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<int> DrainPendingAsync(CancellationToken ct)
    {
        var uploaded = 0;

        while (_queue.Reader.TryRead(out var entry))
        {
            if (await TryUploadAsync(entry.Id, entry.Log, ct).ConfigureAwait(false))
            {
                uploaded++;
            }
        }

        return uploaded;
    }

    /// <summary>Drains until cancelled — the hosted drain service's loop. Completes without throwing on the stop signal.</summary>
    /// <param name="ct">Stop signal.</param>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var entry))
                {
                    await TryUploadAsync(entry.Id, entry.Log, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The stop signal: whatever is still queued is process-lifetime state, and losing it on
            // shutdown is the same accounting as a full queue.
            while (_queue.Reader.TryRead(out _))
            {
                _losses.Increment();
            }
        }
    }

    private async Task<bool> TryUploadAsync(BattleLogId id, byte[] log, CancellationToken ct)
    {
        try
        {
            await _inner.PutAsync(id, log, ct).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Only the caller's own stop signal ends the drain. A foreign cancellation — an SDK's
            // client-side timeout arrives as TaskCanceledException with OUR token untouched — is an
            // ordinary failed upload, or one hiccup would kill the loop and drop every later log
            // for the life of the process.
            _losses.Increment();
            throw;
        }
        catch (Exception)
        {
            _losses.Increment();

            return false;
        }
    }

    private static void RequireId(BattleLogId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "default(BattleLogId) carries no text — a store that keyed on it would file every " +
                "such log under one name.",
                nameof(id));
        }
    }
}
