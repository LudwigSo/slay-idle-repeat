using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Opens — and reopens — the server session the rest of the wire machinery runs on.
/// </summary>
/// <remarks>
/// <para>
/// The client's own seam over <see cref="IGameApiPort"/> rather than a port, for the reason
/// <see cref="IContentDistributionClient"/> already states about itself. One mechanism with two
/// callers: the boot opens the session once, and the pump reopens it on the ladder.
/// </para>
/// <para>
/// 🔒 <b>The two failures the port distinguishes are not folded together.</b> An unreachable server
/// is recorded on the connection and never rethrown, because that is the state the ladder exists
/// for; a refusal escapes, because retrying it unchanged cannot help.
/// </para>
/// </remarks>
public sealed class SessionOpener
{
    /// <summary>Wires the opener over the seam, the credential it holds and the ladder it reports to.</summary>
    /// <param name="api">The wire seam.</param>
    /// <param name="credentials">The in-memory device credential.</param>
    /// <param name="connection">The ladder every outcome is reported to.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public SessionOpener(
        IGameApiPort api, EphemeralDeviceCredentials credentials, ReconnectManager connection)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(connection);
    }

    /// <summary>Whether a session is open right now.</summary>
    public bool IsOpen => throw new NotImplementedException();

    /// <summary>The account the open session belongs to, or <c>null</c> while none is open.</summary>
    public PlayerId? Account => throw new NotImplementedException();

    /// <summary>
    /// Registers a device when none is held, otherwise reopens the family. Reports the outcome to
    /// the connection either way.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="GameApiRefusedException">The server understood and said no.</exception>
    public Task OpenAsync(CancellationToken ct) => throw new NotImplementedException();
}
