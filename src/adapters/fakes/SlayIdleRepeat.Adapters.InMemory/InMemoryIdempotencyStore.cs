using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IIdempotencyStore"/> — record lifetimes are real here, against the injected clock.</summary>
/// <remarks>
/// One lock per store keeps <see cref="RecordAsync"/>'s record-plus-advance genuinely atomic even
/// under the parallel callers a fake can meet in tests. Run-scoped records carry their handed
/// per-record lifetime, the approximation the port's own remarks declare for this fake.
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private sealed class ScopeState
    {
        public long LastSequence { get; set; }

        public ConcurrentDictionary<string, (RecordedCommandOutcome Outcome, DateTimeOffset ExpiresAtUtc)>
            Records { get; } = new(StringComparer.Ordinal);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, ScopeState> _scopes = new(StringComparer.Ordinal);
    private readonly IClockPort _clock;

    /// <summary>Builds a store whose record lifetimes are measured on <paramref name="clock"/>.</summary>
    /// <param name="clock">The clock expiry is measured against — an adjustable one in tests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public InMemoryIdempotencyStore(IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<RecordedCommandOutcome?>(
                _scopes.TryGetValue(KeyOf(scope), out var state) &&
                state.Records.TryGetValue(commandId.Value, out var entry) &&
                entry.ExpiresAtUtc > _clock.UtcNow
                    ? entry.Outcome
                    : null);
        }
    }

    /// <inheritdoc/>
    public Task RecordAsync(
        IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl), ttl, "A record born expired turns its command's next retry into a double-apply.");
        }

        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // The scope is created if absent (a player's lifetime domain exists the moment the
            // player does), and the record and the advance land under one gate — the atomicity the
            // port's contract makes the store's own.
            var state = StateOf(KeyOf(scope));
            state.Records[outcome.CommandId.Value] = (outcome, _clock.UtcNow + ttl);
            state.LastSequence = Math.Max(state.LastSequence, outcome.Sequence);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(
                _scopes.TryGetValue(KeyOf(scope), out var state) ? state.LastSequence : (long?)null);
        }
    }

    /// <inheritdoc/>
    public Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // Never a reset: opening an existing scope leaves its counter and records untouched.
            StateOf(KeyOf(scope));
        }

        return Task.CompletedTask;
    }

    private ScopeState StateOf(string key)
    {
        if (!_scopes.TryGetValue(key, out var state))
        {
            state = new ScopeState();
            _scopes[key] = state;
        }

        return state;
    }

    private static string KeyOf(IdempotencyScope scope) =>
        scope.Kind == IdempotencyScopeKind.Run
            ? "run|" + scope.Player.Value + "|" + scope.Run!.Value.Value
            : "player|" + scope.Player.Value;
}
