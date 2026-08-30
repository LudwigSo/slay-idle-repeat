using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Client.Game.Net;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The seam that opens the server session: what it does with a device nobody has registered yet,
/// and — the part the whole reconnect ladder hangs off — which of the two wire failures it swallows.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The distinction is not observable from either caller.</b> A boot that reached Ready and a
/// pump that recorded a fault are each satisfied by an opener that rethrew the wrong failure and a
/// caller that caught it, so which rule fired has to be pinned here: an unreachable server is
/// recorded on the ladder and never rethrown, because that is the state the ladder exists for; a
/// refusal escapes and leaves the ladder alone, because retrying it unchanged cannot help and
/// backing off would be waiting for a network that is already there.
/// </para>
/// <para>
/// The account is asserted as a value rather than as "something": both of the callers' cases expect
/// no account, so an opener that never opened one at all would satisfy every one of them.
/// </para>
/// </remarks>
public sealed class SessionOpenerTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 6, 11, 8, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The leading run of the minted secret, so a holder printing a TRUNCATED secret is caught too.
    /// </summary>
    private const int LeadingRunLength = 13;

    [Fact]
    public async Task OpenAsync_registers_a_device_and_signs_in_with_the_credential_it_was_issued()
    {
        var api = RecordingGameApi.Reachable();
        var (opener, _, _) = Opener(api);

        await opener.OpenAsync(CancellationToken.None);

        api.RegisterAttempts.ShouldBe(
            1,
            "nothing is held on a first cold start, so the session begins by minting an anonymous " +
            "account. An opener that signed in without one presents a credential no server issued.");
        api.Presented.ShouldHaveSingleItem().DeviceId.ShouldBe(
            RecordingGameApi.MintedDeviceId,
            "and the sign-in presents what the registration answered with, not something assembled " +
            "beside it. A credential the server did not issue is refused, and a refusal is fatal to " +
            "the boot — so an opener that lost the registration's answer turns a working server into " +
            "a start that cannot happen.");
        opener.IsOpen.ShouldBeTrue(
            "the pump reopens the session whenever this says it is shut, so an opener that never " +
            "admits to being open signs in again on every single frame that a retry is due.");
        opener.Account.ShouldBe(
            NetWorlds.Player,
            "and the account is the server's answer, stated as the value rather than as 'not null': " +
            "the boot's own cases all expect no account, so nothing else in the suite can tell an " +
            "opener that opened one from an opener that never does.");
    }

    /// <summary>
    /// 🔒 Reopening reuses the device, because a second registration is a second account.
    /// </summary>
    /// <remarks>
    /// Two calls because that is what the behaviour is — the pump's whole job is to call this again
    /// — and the second one is what the assertion is about.
    /// </remarks>
    [Fact]
    public async Task Opening_again_reuses_the_device_it_registered_rather_than_minting_another()
    {
        var api = RecordingGameApi.Reachable();
        var (opener, _, _) = Opener(api);

        await opener.OpenAsync(CancellationToken.None);
        await opener.OpenAsync(CancellationToken.None);

        api.RegisterAttempts.ShouldBe(
            1,
            "every registration mints a NEW anonymous account, so an opener that registers on each " +
            "reopen abandons the player's account every time the network blinks — and the ladder " +
            "reopens after every single failure.");
        opener.Account.ShouldBe(
            NetWorlds.Player,
            "and it is still the account the first open produced. An opener that reopened onto a " +
            "different one would hand every screen after boot an identity that changed underneath it.");
    }

    /// <summary>
    /// 🔒 <b>Two callers, one opener, and never two registrations in flight over it.</b>
    /// </summary>
    /// <remarks>
    /// The boot opens the session and the pump reopens it, and on the server arm both are driven
    /// from the same frame loop over the same opener — so an open still waiting on the server is the
    /// ordinary state for the second caller to arrive in. Two in flight is not merely wasteful:
    /// neither holds a credential yet, so both register, and every registration mints a NEW
    /// anonymous account — the second silently replacing the one the first just opened.
    /// </remarks>
    [Fact]
    public async Task A_second_open_joins_the_one_in_flight_rather_than_registering_again()
    {
        var gated = RecordingGameApi.Reachable().Gated();
        var (opener, _, _) = Opener(gated);

        var first = opener.OpenAsync(CancellationToken.None);
        var second = opener.OpenAsync(CancellationToken.None);

        first.IsCompleted.ShouldBeFalse(
            "the arrangement has to actually leave an open in flight, or everything below is about a " +
            "second call that simply followed a finished first one.");
        gated.RegisterAttempts.ShouldBe(
            1,
            "the second caller found an open already under way and joined it. A second registration " +
            "mints a second anonymous account and abandons the first — and on this arm that account " +
            "is the only one the session has.");
        second.ShouldBeSameAs(
            first,
            "and it joined rather than returned early: a caller handed a completed task walks on into " +
            "a boot stage that believes a session is open while the sign-in is still out.");

        var settled = RecordingGameApi.Reachable();
        var (reopener, _, _) = Opener(settled);

        await reopener.OpenAsync(CancellationToken.None);
        await reopener.OpenAsync(CancellationToken.None);

        settled.AuthenticateAttempts.ShouldBe(
            2,
            "the control, and the half that stops this becoming 'open once, ever': an open that has " +
            "finished holds nothing back, because reopening after a loss is the pump's whole job.");
    }

    /// <summary>
    /// 🔒 <b>An unreachable server is recorded on the ladder and never rethrown.</b>
    /// </summary>
    [Fact]
    public async Task An_unreachable_server_is_recorded_on_the_ladder_rather_than_thrown()
    {
        var api = RecordingGameApi.Reachable().UnreachableFor(int.MaxValue);
        var (opener, connection, _) = Opener(api);

        await Should.NotThrowAsync(
            () => opener.OpenAsync(CancellationToken.None),
            "a server that cannot be reached is the ordinary state of a handset in a lift, and it is " +
            "the state the ladder exists for. Rethrowing makes the boot fail on a network outage and " +
            "makes the pump file one as an error nobody can act on.");

        connection.ConsecutiveFailures.ShouldBe(
            1,
            "and swallowing is not the same as ignoring. Nothing else feeds this ladder on this arm — " +
            "no command is queued and no run is followed — so an opener that catches the failure and " +
            "says nothing leaves the ladder with no input at all, which is a connection that is never " +
            "due for a retry and an overlay that reports one that was never attempted.");
        connection.State.ShouldBe(
            ConnectionState.Waiting,
            "the connection is known to be down before the pill threshold has elapsed, which is what " +
            "makes a blink of failure cost the player nothing on screen.");
        opener.IsOpen.ShouldBeFalse(
            "an attempt that never reached the server opened nothing. An opener reporting otherwise " +
            "stops the pump ever retrying.");
        opener.Account.ShouldBeNull(
            "and there is no account, because no server answered with one.");
    }

    /// <summary>
    /// 🔒 …and a refusal escapes, leaving the ladder exactly where it was.
    /// </summary>
    /// <remarks>
    /// The two halves are one claim: the port draws this distinction and the whole backoff hangs off
    /// it. Folding a refusal into the unreachable arm would back off from a server that is up and
    /// answering, forever, over a request it will refuse identically every time.
    /// </remarks>
    [Fact]
    public async Task A_refusal_escapes_and_starts_no_backoff()
    {
        var api = RecordingGameApi.Reachable().RefusingFor(int.MaxValue, statusCode: 403);
        var (opener, connection, _) = Opener(api);

        var refusal = await Should.ThrowAsync<GameApiRefusedException>(
            () => opener.OpenAsync(CancellationToken.None),
            "the server understood the sign-in and said no. Swallowed here it becomes a build whose " +
            "every later call is refused with nothing anywhere naming why — and the boot walks on " +
            "into it instead of failing where the cause is still legible.");

        refusal.StatusCode.ShouldBe(
            403,
            "carried rather than flattened: the status is what says whether an account service " +
            "rejected the credential or refused the whole client.");
        connection.ConsecutiveFailures.ShouldBe(
            0,
            "and a refusal is not a connection loss. It is also the control for the case above — an " +
            "opener that recorded every failure on the ladder would satisfy that one and mean " +
            "nothing, while backing off from a server that is up.");
        connection.State.ShouldBe(
            ConnectionState.Connected,
            "so nothing is drawn about the network, which is correct: the network is fine.");
    }

    /// <summary>
    /// 🔒 The issued credential is held for the process, and the holder never prints the secret.
    /// </summary>
    /// <remarks>
    /// The device secret is the anonymous account's root credential and the only sanctioned home for
    /// it is a platform keystore this repository does not have. Held in memory is the stated
    /// consequence of that; printed into a log line is not, and a holder that formats itself the
    /// obvious way puts it in every exception message and debugger watch that ever renders one.
    /// </remarks>
    [Fact]
    public async Task The_issued_credential_is_held_and_the_holder_never_prints_the_secret()
    {
        var api = RecordingGameApi.Reachable();
        var (opener, _, credentials) = Opener(api);

        await opener.OpenAsync(CancellationToken.None);

        credentials.IsHeld.ShouldBeTrue(
            "the credential the registration issued is never re-readable, so an opener that dropped " +
            "it can only ever reopen by minting another account.");
        credentials.Held.ShouldNotBeNull(
            "and it has to be reachable, or the reopen has nothing to present.")
                   .DeviceId.ShouldBe(
                       RecordingGameApi.MintedDeviceId,
                       "stated as the device the server minted rather than as 'some device', because " +
                       "a holder carrying anything else presents a credential that is refused.");
        credentials.ToString().ShouldNotContain(
            RecordingGameApi.MintedDeviceSecret[..LeadingRunLength],
            Case.Sensitive,
            "the leading run rather than the whole value, so a holder printing a truncated secret is " +
            "caught too — a prefix is worth little to an attacker and everything to a compliance " +
            "review, and both are worse than a line that never carried one.");
    }

    private static (SessionOpener Opener, ReconnectManager Connection, EphemeralDeviceCredentials Credentials)
        Opener(RecordingGameApi api)
    {
        var clock = new ManualClock(StartedAt);
        var connection = new ReconnectManager(
            api, new StateMirror(), new CommandQueue(CountingIdGenerator.Counting()), clock);
        var credentials = new EphemeralDeviceCredentials();

        return (new SessionOpener(api, credentials, connection), connection, credentials);
    }
}
