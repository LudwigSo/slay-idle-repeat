using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IMessageRepository"/> — expiry is real here, against the injected clock.</summary>
/// <remarks>
/// <para>
/// Rows are held as <see cref="PlayerMessage"/> values rather than encoded bytes, unlike this
/// project's other stores: a message has no canonical codec, because it is never hashed into a
/// state hash and never rehydrated into an aggregate. What makes holding the value safe is that
/// <see cref="PlayerMessage"/> copies its own parameters and attachments on construction, so a
/// caller cannot write into a stored row through the list it handed over.
/// </para>
/// <para>
/// Expiry is enforced at read — an expired message answers as absent from
/// <see cref="GetActiveAsync"/> — and it is still returned by <see cref="DequeueExpiringAsync"/>,
/// because that is exactly the job's subject: a message the player can no longer see and whose
/// reward is still owed.
/// </para>
/// </remarks>
public sealed class InMemoryMessageRepository : IMessageRepository
{
    private readonly ConcurrentDictionary<string, PlayerMessage> _rows = new(StringComparer.Ordinal);

    private readonly IClockPort _clock;

    /// <summary>Builds a store whose expiry is measured on <paramref name="clock"/>.</summary>
    /// <param name="clock">The clock expiry is measured against — an adjustable one in tests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public InMemoryMessageRepository(IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PlayerMessage>> GetActiveAsync(PlayerId id, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        var now = _clock.UtcNow;

        IReadOnlyList<PlayerMessage> live = _rows.Values
            .Where(m => m.Player == id && !IsDue(m, now))
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id.Value, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(live);
    }

    /// <inheritdoc/>
    public Task AppendAsync(PlayerMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        RequireId(message.Player);
        RequireMessageId(message.Id);
        ct.ThrowIfCancellationRequested();

        // TryAdd, never an assignment: the port's append is idempotent on the message id, so a
        // retried half-delivered segment send must leave the half that landed exactly as it was —
        // including its claimed stamp, which an overwrite would clear and pay a second time.
        _rows.TryAdd(message.Id.Value, message);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkClaimedAsync(PlayerId id, IReadOnlyList<MessageId> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        var now = _clock.UtcNow;

        foreach (var messageId in ids)
        {
            RequireMessageId(messageId);

            // The first stamp wins and an unowned or unknown id is ignored, both per the port: the
            // timestamp records when the reward was actually paid, and a claim that succeeded must
            // not fail here over an id the caller was already told this player does not hold.
            // TryUpdate rather than AddOrUpdate, so no path here can create a row — a stamp for an
            // unknown id would be a claimed message nobody sent.
            if (_rows.TryGetValue(messageId.Value, out var stored) &&
                stored.Player == id &&
                stored.ClaimedAtUtc is null)
            {
                _rows.TryUpdate(messageId.Value, stored with { ClaimedAtUtc = now }, stored);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PlayerMessage>> DequeueExpiringAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit,
                "A batch of no messages answers 'nothing is due' for an inbox that is overdue, and " +
                "the job would report a clean sweep for ever.");
        }

        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PlayerMessage> due = _rows.Values
            .Where(m => IsDue(m, asOfUtc))
            .OrderBy(m => m.ExpiresAtUtc)
            .ThenBy(m => m.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return Task.FromResult(due);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(IReadOnlyList<MessageId> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();

        foreach (var id in ids)
        {
            RequireMessageId(id);
            _rows.TryRemove(id.Value, out _);
        }

        return Task.CompletedTask;
    }

    private static bool IsDue(PlayerMessage message, DateTimeOffset asOfUtc) =>
        message.ExpiresAtUtc is { } expiry && expiry <= asOfUtc;

    private static void RequireId(PlayerId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "default(PlayerId) carries no text — an inbox keyed on it would pool every such " +
                "caller's messages into one player's.",
                nameof(id));
        }
    }

    private static void RequireMessageId(MessageId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "default(MessageId) carries no text, and a grant is idempotent ON that value — so " +
                "every blank-id message would be treated as the same one already paid.",
                nameof(id));
        }
    }
}
