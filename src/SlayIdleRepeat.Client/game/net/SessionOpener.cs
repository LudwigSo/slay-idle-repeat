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
    private readonly IGameApiPort _api;
    private readonly EphemeralDeviceCredentials _credentials;
    private readonly ReconnectManager _connection;

    private Task? _opening;

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

        _api = api;
        _credentials = credentials;
        _connection = connection;
    }

    /// <summary>Whether a session is open right now.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The account the open session belongs to, or <c>null</c> while none is open.</summary>
    public PlayerId? Account { get; private set; }

    /// <summary>
    /// Registers a device when none is held, otherwise reopens the family. Reports the outcome to
    /// the connection either way.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>One open at a time.</b> The boot opens the session and the pump reopens it, both from
    /// the same frame loop over this same opener, so a second call arriving while one is still out
    /// is ordinary rather than exceptional — and neither would be holding a credential yet, so both
    /// would register and the second would abandon the account the first had just minted. The
    /// second caller joins the open already under way instead. A settled one holds nothing back:
    /// reopening after a loss is what the ladder is for.
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="GameApiRefusedException">The server understood and said no.</exception>
    public Task OpenAsync(CancellationToken ct) =>
        _opening is { IsCompleted: false } inFlight ? inFlight : _opening = OpenOnceAsync(ct);

    private async Task OpenOnceAsync(CancellationToken ct)
    {
        try
        {
            if (!_credentials.IsHeld)
            {
                // Once per process, because every registration mints a NEW anonymous account: a
                // reopen that registered again would abandon the player's account on every blink.
                _credentials.Adopt(await _api.RegisterDeviceAsync(null, ct).ConfigureAwait(false));
            }

            var session = await _api.AuthenticateAsync(_credentials.Held!, ct).ConfigureAwait(false);

            Account = session.Player;
            IsOpen = true;

            _connection.RecordReached();
        }
        catch (GameApiUnavailableException failure)
        {
            IsOpen = false;

            _connection.RecordLost(failure);
        }
        catch (GameApiRefusedException refusal)
        {
            // Recorded and rethrown, not swallowed: the caller still has to see the refusal, and the
            // ladder still must not back off from a server that is up. What the record buys is the
            // hold — the pump reopens on every frame a session is shut, so a refusal nothing noted
            // is a sign-in attempt per frame for the life of the process.
            IsOpen = false;

            _connection.RecordRefused(refusal);

            throw;
        }
    }
}
