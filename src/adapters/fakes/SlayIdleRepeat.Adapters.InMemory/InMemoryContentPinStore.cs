using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IContentPinStore"/> — two dictionaries and one gate.</summary>
/// <remarks>
/// Pins never expire here. Expiry is storage semantics the durable backing owns; a fake that aged
/// its own rows would need a clock, and the sweep this store feeds is already pure and time-driven
/// by its caller.
/// </remarks>
public sealed class InMemoryContentPinStore : IContentPinStore
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
