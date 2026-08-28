using System.Collections.Concurrent;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Moderation;

/// <summary>
/// ⚠️ The in-process moderation store: everything it holds dies with the process. The stand-in
/// until a durable implementation of <see cref="IModerationStore"/> exists.
/// </summary>
/// <remarks>
/// <para>
/// Beside its seam rather than in the composition root, on the command ledger's precedent: what is
/// volatile about it is storage, not policy, and a process configured with no moderation database
/// still has to answer the sweep's questions somehow.
/// </para>
/// <para>
/// 🔒 It is nobody's system of record and it is not a cache either — there is nothing behind it to
/// rebuild from. A deployment running on this loses every observation and every queue entry on
/// restart, which is why the composition root announces it at startup rather than letting a
/// deployment discover it when a reviewer asks where the queue went.
/// </para>
/// <para>
/// The account source is empty unless something supplies one: nothing enumerates player rows today
/// — no port offers a scan — so a sweep over this store observes whatever
/// <see cref="SetObservableAccounts"/> was given and nothing else. That is the honest shape of a
/// skeleton, not a defect to paper over with a fabricated account list.
/// </para>
/// </remarks>
public sealed class VolatileModerationStore : IModerationStore
{
    private readonly ConcurrentDictionary<PlayerId, PlausibilityObservation> _previous = new();
    private readonly ConcurrentQueue<ReviewQueueEntry> _reviews = new();
    private readonly ConcurrentQueue<PlayerSanction> _sanctions = new();

    private volatile IReadOnlyList<PlausibilityObservation> _accounts = [];

    /// <summary>Sets what a sweep over this store will observe, stamped with the sweep's own instant.</summary>
    /// <param name="accounts">The accounts and their cumulative measures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="accounts"/> is null.</exception>
    public void SetObservableAccounts(IEnumerable<PlausibilityObservation> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        _accounts = accounts.ToArray();
    }

    /// <summary>Records a sanction. No production caller: sanctions are recorded by hand today.</summary>
    /// <param name="sanction">The sanction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sanction"/> is null.</exception>
    public void AddSanction(PlayerSanction sanction)
    {
        ArgumentNullException.ThrowIfNull(sanction);

        _sanctions.Enqueue(sanction);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PlausibilityObservation>> ObserveAccountsAsync(
        DateTimeOffset at, CancellationToken ct)
    {
        IReadOnlyList<PlausibilityObservation> observed = _accounts
            .Select(account => account with { ObservedAtUtc = at })
            .ToArray();

        return Task.FromResult(observed);
    }

    /// <inheritdoc/>
    public Task<PlausibilityObservation?> ReadPreviousObservationAsync(PlayerId player, CancellationToken ct) =>
        Task.FromResult(_previous.GetValueOrDefault(player));

    /// <inheritdoc/>
    public Task RecordObservationAsync(PlausibilityObservation observation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);

        _previous[observation.Player] = observation;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RaiseReviewAsync(ReviewQueueEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _reviews.Enqueue(entry);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReviewQueueEntry>> ReadReviewsAsync(ReviewState state, CancellationToken ct)
    {
        IReadOnlyList<ReviewQueueEntry> matching = _reviews.Where(entry => entry.State == state).ToArray();

        return Task.FromResult(matching);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PlayerSanction>> ReadSanctionsAsync(CancellationToken ct)
    {
        IReadOnlyList<PlayerSanction> all = _sanctions.ToArray();

        return Task.FromResult(all);
    }
}
