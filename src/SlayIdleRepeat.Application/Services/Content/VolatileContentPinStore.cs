using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>The process-local <see cref="IContentPinStore"/> — two dictionaries and one gate.</summary>
/// <remarks>
/// <para>
/// Beside its seam rather than among the port fakes, on <see cref="VolatileCommandLedger"/>'s
/// precedent and for its reason: pins are not a port, and this is the implementation a process
/// configured with NO database actually runs on, not a double that only tests use. The composition
/// root reaches it without depending on a fakes assembly, which it must never do.
/// </para>
/// <para>
/// Pins never expire here. Expiry is storage semantics the durable backing owns; ageing rows here
/// would need a clock, and the sweep this store feeds is already pure and driven by its caller's
/// time. A restart therefore forgets every pin — which is the honest behaviour for a process whose
/// runs did not survive it either.
/// </para>
/// </remarks>
public sealed class VolatileContentPinStore : IContentPinStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Pin> _runPins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pin> _sessionPins = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<ContentVersion?> ReadRunPinAsync(RunId run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<ContentVersion?>(
                _runPins.TryGetValue(run.Value, out var pin) ? pin.Version : null);
        }
    }

    /// <inheritdoc/>
    public Task WriteRunPinAsync(RunId run, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);

        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _runPins[run.Value] = new Pin(version, atUtc);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ContentVersion?> ReadSessionPinAsync(PlayerId player, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<ContentVersion?>(
                _sessionPins.TryGetValue(player.Value, out var pin) ? pin.Version : null);
        }
    }

    /// <inheritdoc/>
    public Task WriteSessionPinAsync(
        PlayerId player, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);

        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sessionPins[player.Value] = new Pin(version, atUtc);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RetainedVersion>> ListRetainedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var latest = new Dictionary<string, RetainedVersion>(StringComparer.Ordinal);

            foreach (var pin in _runPins.Values.Concat(_sessionPins.Values))
            {
                // The newest reference wins: a version two pins name is retained on the strength of
                // whichever of them touched it last, never the first one enumeration happened to hit.
                if (!latest.TryGetValue(pin.Version.Value, out var held) ||
                    held.LastReferencedAtUtc < pin.AtUtc)
                {
                    latest[pin.Version.Value] = new RetainedVersion(pin.Version, pin.AtUtc);
                }
            }

            return Task.FromResult<IReadOnlyList<RetainedVersion>>(
                latest
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => entry.Value)
                    .ToArray());
        }
    }

    private sealed record Pin(ContentVersion Version, DateTimeOffset AtUtc);
}
