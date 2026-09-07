using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Opens the game DAY: one <c>BEGIN_SESSION</c> per launch, sent once the profile is open and
/// before any screen reads it.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>It exists because nothing else sent that command, and a run now costs Energy.</b>
/// <c>Player.CreateStarting</c> opens both Energy banks at zero — deliberately, since every balance
/// must be attributed by a <c>CurrencyChanged</c> — and the daily free refill is
/// <c>BEGIN_SESSION</c>'s grant. With <c>START_RUN</c> charging the authored run cost and nothing in
/// the build submitting <c>BEGIN_SESSION</c>, a freshly installed profile had to wait out
/// <c>runCost x regenMinutesPerPoint</c> of regeneration before it could start its first run. That
/// is steering S25's exact shape: a documented seam with no production caller.
/// </para>
/// <para>
/// 🔒 <b>Not a transport session.</b> <see cref="SessionOpener"/> opens the account session the wire
/// runs on; this is the game command that advances the login calendar and grants the day's Energy.
/// The two are separate on purpose — the local arm composes no wire session at all and still has a
/// game day.
/// </para>
/// <para>
/// 🔒 <b>Never fatal, and never retried.</b> The handler is idempotent per game day, so a launch
/// whose call did not land loses that day's refill and nothing else — the game is entirely playable
/// without it, and a boot that stopped here would trade a missing grant for an unstartable app. The
/// outcome is recorded rather than thrown so a launch can say which of the three things happened.
/// </para>
/// <para>
/// 🔴 <b>It is BOUNDED, and the bound is the boot's own.</b> The boot budgets every server stage it
/// runs at <c>BootPresenter.ServerStageDeadline</c> apiece, so that a slow or unreachable server
/// costs a player a moment on the splash rather than a minute. This command is awaited on that same
/// path — between the boot finishing and the home screen appearing — so an unbounded one would have
/// held the player on a splash reading <em>Ready</em> for as long as the transport allowed, which on
/// the shipped HTTP adapter is its whole request timeout. Expiry is free here in a way it is not
/// elsewhere: the day is idempotent, so an attempt that was cut short either landed server-side
/// anyway (and the profile the home screen then reads carries the grant) or did not, and the next
/// launch opens the day instead. Nothing is lost either way; only this launch's grant is delayed.
/// </para>
/// <para>
/// ⚠️ It is the one command that carries a content hash (<c>ContentVersionCheck</c> says so in as
/// many words), so the hash it sends is the version of the content set this client actually loaded —
/// never a constant, and never the one it was built against.
/// </para>
/// </remarks>
public sealed class GameDayOpener
{
    private readonly IGameHost _host;
    private readonly string _clientVersion;
    private readonly string _contentHash;
    private readonly TimeSpan _deadline;

    /// <summary>Builds the opener over the host the game is played through.</summary>
    /// <param name="host">Where the command is submitted.</param>
    /// <param name="clientVersion">The build the player is running, as the platform reports it.</param>
    /// <param name="contentVersion">The version of the content set this client loaded.</param>
    /// <param name="deadline">
    /// How long this call may hold the launch. Passed in rather than named here: the number is the
    /// boot's, because what it protects is the boot's promise about how long a splash lasts.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadline"/> is not positive.</exception>
    public GameDayOpener(
        IGameHost host, string clientVersion, ContentVersion contentVersion, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clientVersion);
        ArgumentNullException.ThrowIfNull(contentVersion);

        if (deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline), deadline,
                "a deadline at or below zero cancels the call before it is made, which loses the " +
                "day's grant on every launch. Pass the boot's own server-stage bound.");
        }

        _host = host;
        _clientVersion = clientVersion;
        _contentHash = contentVersion.Value;
        _deadline = deadline;
    }

    /// <summary>Whether the day was opened. False until <see cref="OpenAsync"/> has answered.</summary>
    public bool Opened { get; private set; }

    /// <summary>Why the command was refused, or <c>null</c> when it was not.</summary>
    public RejectionReason? Refusal { get; private set; }

    /// <summary>What went wrong when the call itself did not complete, or <c>null</c>.</summary>
    /// <remarks>
    /// Held apart from <see cref="Refusal"/> because they are different failures with different
    /// owners: a refusal is the domain answering, and a fault is the host not answering at all.
    /// </remarks>
    public string? Failure { get; private set; }

    /// <summary>Sends this launch's <c>BEGIN_SESSION</c> for one profile.</summary>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Whether the day was opened.</returns>
    public async Task<bool> OpenAsync(PlayerId player, CancellationToken ct)
    {
        Opened = false;
        Refusal = null;
        Failure = null;

        // 🔒 The caller's token is LINKED in rather than replaced: a shutdown during a slow launch
        // still cancels at once, and the deadline is what tells a closing window apart from a server
        // that is simply not answering — both arrive below as the same exception.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);

        bounded.CancelAfter(_deadline);

        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's submit is
            // an async method, so its failure arrives as a faulted task and a try around the call
            // alone would never see it.
            var outcome = await _host
                .SubmitAsync(
                    player, run: null, new BeginSessionCommand(_clientVersion, _contentHash), bounded.Token)
                .ConfigureAwait(false);

            Opened = outcome.Accepted;
            Refusal = outcome.Accepted ? null : outcome.Rejection;
        }
        catch (Exception failure)
        {
            Failure = $"{failure.GetType().Name}: {failure.Message}";
        }

        return Opened;
    }
}
