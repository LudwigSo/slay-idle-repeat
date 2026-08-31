using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>The wire seam: the game as it is reached over a network, typed on the command envelope.</summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IGameHost"/> and deliberately not the same interface. This one
/// speaks <c>14</c> §2.3's envelope and answers with <em>projections</em>, which is all a client may
/// ever be told; <see cref="IGameHost"/> speaks the typed command and answers with the Core
/// aggregates, which no transport can honestly produce.
/// </para>
/// <para>
/// 🔒 <b>A rejected command is not an exception.</b> The server answers a refusal with HTTP 200 and
/// a rejection envelope — that is its own contract, and a rejection is a successful exchange whose
/// answer is no. It arrives here as <c>WireCommandResult { Accepted = false, Rejection = … }</c>.
/// The two exceptions below are for the exchange failing, never for its answer.
/// </para>
/// <para>
/// The connection-state machinery reads exactly one distinction off this port:
/// <see cref="GameApiUnavailableException"/> means the connection is lost and the attempt is worth
/// repeating, <see cref="GameApiRefusedException"/> means the server understood and said no.
/// </para>
/// </remarks>
public interface IGameApiPort
{
    /// <summary>Mints an anonymous account and the device credential that reopens it.</summary>
    /// <param name="displayName">The name to ask for, or <c>null</c> to take the server's default.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The credential, the account, and the name the server's filter actually decided.</returns>
    /// <exception cref="GameApiUnavailableException">The transport could not answer.</exception>
    /// <exception cref="GameApiRefusedException">The server refused the registration — a name its filter will not issue.</exception>
    Task<WireDeviceRegistration> RegisterDeviceAsync(string? displayName, CancellationToken ct);

    /// <summary>Opens a token family for a stored device credential.</summary>
    /// <param name="credentials">The stored device credential.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The access token every later call carries, and the refresh token that renews it.</returns>
    /// <exception cref="GameApiUnavailableException">The transport could not answer.</exception>
    /// <exception cref="GameApiRefusedException">The credential is unknown, wrong, or names an unavailable account.</exception>
    Task<WireSession> AuthenticateAsync(WireCredentials credentials, CancellationToken ct);

    /// <summary>Submits one command envelope.</summary>
    /// <param name="run">
    /// The run the command is addressed to, or <c>null</c> for the player endpoint. The two are
    /// different scopes with different sequence counters, not a convenience overload.
    /// </param>
    /// <param name="envelope">The envelope, already carrying its command id and sequence.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The outcome — accepted or rejected. A rejection is a return value, never an exception.</returns>
    /// <exception cref="GameApiUnavailableException">The transport could not answer.</exception>
    /// <exception cref="GameApiRefusedException">The server could not read the request at all.</exception>
    Task<WireCommandResult> SendCommandAsync(RunId? run, CommandEnvelope envelope, CancellationToken ct);

    /// <summary>Reads where a run stands, and what the client missed while it was away.</summary>
    /// <param name="run">The run to read.</param>
    /// <param name="sinceSequence">The sequence the client already holds. Zero asks for everything.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The projections, the hash, and the outcomes after <paramref name="sinceSequence"/>.</returns>
    /// <exception cref="GameApiUnavailableException">The transport could not answer.</exception>
    /// <exception cref="GameApiRefusedException">The run is unknown, or belongs to someone else — the two are indistinguishable by design.</exception>
    Task<WireRunState> FetchRunStateAsync(RunId run, long sinceSequence, CancellationToken ct);
}
