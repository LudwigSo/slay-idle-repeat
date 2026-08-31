using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>The whole client-facing surface of the game: one profile, one write, one read.</summary>
/// <remarks>
/// <para>
/// Typed on <see cref="GameCommand"/> rather than on a wire envelope, which is what makes this a
/// seam and not a transport: an implementation over HTTPS wraps the envelope and parses it into the
/// typed command before it reaches here, and everything above this interface stays unchanged when
/// that swap happens. It is also why this is a DIFFERENT port from <see cref="IGameApiPort"/>: that
/// one carries the envelope over the wire and returns projections, and no transport can honestly
/// answer here, because <see cref="ApplyCommandOutcome.State"/> is the Core aggregates — run seed
/// included — and the seed never leaves the server.
/// </para>
/// <para>
/// A port, under <c>Ports/</c>: an application-owned interface an adapter conforms to, which is
/// what makes an implementation outside this assembly legal at all. The alternative was an
/// interface no gate watches — no two-implementation rule, no shared contract suite, no fixture per
/// implementation — and a seam nothing checks is a seam whose meaning is whatever its first
/// implementation happened to do.
/// </para>
/// <para>
/// It returns the use-case results unchanged rather than a shape of its own. A second description of
/// the same state is a second thing to keep in step, and the persisted rows are already the answer.
/// </para>
/// </remarks>
public interface IGameHost
{
    /// <summary>The profile this installation plays, creating it on first use.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The player the local profile names. The same identity on every later call.</returns>
    Task<PlayerId> OpenProfileAsync(CancellationToken ct);

    /// <summary>Applies one command to one player, optionally addressed to one run.</summary>
    /// <param name="player">Whose state the command is applied to.</param>
    /// <param name="run">The run the command was addressed to, or <c>null</c> when it was addressed to the player.</param>
    /// <param name="command">What the player intends.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Whether it was accepted, the resulting state, its events, and any delivery failures.</returns>
    Task<ApplyCommandOutcome> SubmitAsync(PlayerId player, RunId? run, GameCommand command, CancellationToken ct);

    /// <summary>Reads a player's own state, and optionally one named run of theirs.</summary>
    /// <param name="player">Whose state to read.</param>
    /// <param name="run">A run of theirs — current or finished — or <c>null</c> for the player alone.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What was found, and the view when there is one.</returns>
    Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct);
}
