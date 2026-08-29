using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// ⚠️ VOLATILE — the unit of work a process configured with no database runs on, over the same
/// in-process world store and command ledger everything else on such a process uses.
/// </summary>
/// <remarks>
/// A composition-root type for <see cref="PlaceholderVolatileWorldStore"/>'s reason: it is not an
/// adapter over anything, it is the arrangement this process happens to be wired into. Atomicity
/// here is a property of a single-threaded in-process write rather than of a transaction, which is
/// exactly why a process that must not lose a command is configured with a database.
/// </remarks>
public sealed class VolatileUnitOfWork : IUnitOfWork
{
    private readonly WorldSliceStore _store;
    private readonly VolatileCommandLedger _ledger;

    /// <summary>Builds the volatile unit of work over the process's own store and ledger.</summary>
    /// <param name="store">Where the snapshots land.</param>
    /// <param name="ledger">Where the outcome record and the sequence advance land.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public VolatileUnitOfWork(WorldSliceStore store, VolatileCommandLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ledger);

        _store = store;
        _ledger = ledger;
    }

    /// <inheritdoc/>
    public async Task CommitAsync(CommandCommit commit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commit);

        if (commit.Claim is not null)
        {
            // Unreachable rather than unsupported: a process with no database has no message store,
            // so the claim command never loads an inbox and never produces a MailClaimed to build
            // one from. Stated loudly because the alternative is dropping a stamp for a reward the
            // snapshot above just paid — silently, on the one arrangement with no transaction.
            throw new InvalidOperationException(
                "A commit reached the volatile unit of work carrying an inbox claim. This process " +
                "is configured with no database and therefore no message store, so nothing here " +
                "can stamp it and nothing should have produced it.");
        }

        if (commit.State is { } profile)
        {
            await _store.SaveAsync(new StoredSlice(profile.Player, profile.ActiveRun), ct)
                .ConfigureAwait(false);
        }

        await _ledger
            .AppendAsync(CommandScopes.KeyOf(commit.Scope), LedgerRecord.From(commit.Outcome), ct)
            .ConfigureAwait(false);

        if (commit.OpensScope is { } opened)
        {
            await _ledger.OpenScopeAsync(CommandScopes.KeyOf(opened), ct).ConfigureAwait(false);
        }

        // The economy rows are dropped, and that is the arrangement rather than an omission: this
        // process has no economy log to append them to, and inventing an in-memory one would be a
        // ledger nobody can audit that a restart silently empties.
    }
}
