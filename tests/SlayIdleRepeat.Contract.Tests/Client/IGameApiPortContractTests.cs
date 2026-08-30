using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// States what <see cref="IGameApiPort"/> <em>means</em> (<c>23</c> §5 A8) — written once against
/// the interface so the HTTP adapter and the scripted fake cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The distinction every case here defends is the three-way one a caller has to make: an answer
/// (HTTP 200, accepted or refused), a connection that could not answer, and a server that refused
/// the request. Fold any two together and the reconnect machinery either retries a refusal forever
/// or gives up on a network blip.
/// </para>
/// <para>
/// ⚠️ Nothing here asserts what a command DOES. The projections a fake serves are handed to it, and
/// the ones the adapter parses were rendered by the server's own writer — so what is shared is that
/// the answer arrives with its sequence, its hash and its reason intact, not that a rule ran.
/// </para>
/// </remarks>
[ContractSuiteFor(typeof(IGameApiPort))]
public abstract class IGameApiPortContractTests
{
    /// <summary>The reason every fixture's refusing api answers with.</summary>
    /// <remarks>
    /// A transport-tier reason on purpose: it is one no domain rule decides, so a fixture cannot
    /// satisfy this suite by accidentally running a real command that happened to be illegal.
    /// </remarks>
    protected const RejectionReason ScriptedRejection = RejectionReason.RATE_LIMITED;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    /// <summary>An empty payload object — the shape an argument-less command encodes to.</summary>
    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>An api that can be reached and accepts what it is sent.</summary>
    protected abstract IGameApiPort Create();

    /// <summary>The same api, with the server refusing every command with <see cref="ScriptedRejection"/>.</summary>
    /// <remarks>
    /// A hook rather than a command chosen to be illegal: the refusal has to be reachable on both
    /// implementations, and only one of them has any rules to break.
    /// </remarks>
    protected abstract IGameApiPort CreateRefusing();

    /// <summary>The same api with nothing on the other end of the transport.</summary>
    protected abstract IGameApiPort CreateUnreachable();

    /// <summary>The one device credential the api under test authenticates.</summary>
    protected abstract WireCredentials KnownCredentials { get; }

    /// <summary>The one run the api under test knows.</summary>
    protected abstract RunId KnownRun { get; }

    /// <summary>Registration answers a credential that opens a session for the account it names.</summary>
    /// <remarks>
    /// The two halves are asserted together because separately neither says anything: a device id
    /// that authenticates nothing is a string, and a session for an account the registration did not
    /// name is a different player's game.
    /// </remarks>
    [Fact]
    public async Task Registering_a_device_answers_a_credential_that_opens_a_session_for_that_account()
    {
        var api = Create();

        var registration = await api.RegisterDeviceAsync(displayName: null, Cancel);

        registration.DeviceId.ShouldNotBeNullOrWhiteSpace("the credential is what reopens the account.");
        registration.DeviceSecret.ShouldNotBeNullOrWhiteSpace("…and the secret is what proves it.");
        registration.DisplayName.ShouldNotBeNullOrWhiteSpace(
            "an anonymous account still has a name, decided by the server's filter rather than left "
            + "blank for a screen to paper over.");

        var session = await api.AuthenticateAsync(
            new WireCredentials(registration.DeviceId, registration.DeviceSecret), Cancel);

        session.Player.ShouldBe(
            registration.Player,
            $"the credential minted for '{registration.Player}' opened a session for "
            + $"'{session.Player}'. A registration that hands back a credential for somebody else's "
            + "account is the worst possible defect on this seam and the cheapest one to miss.");
    }

    /// <summary>
    /// 🔒 A session names a renewal horizon strictly inside the access token's own lifetime.
    /// </summary>
    /// <remarks>
    /// <c>14</c> §16.5's silent renewal only works if the client is told to renew before the token
    /// stops being accepted. A horizon at or past expiry means every renewal races the expiry it was
    /// meant to prevent, and the symptom is an intermittent sign-out no player can reproduce.
    /// </remarks>
    [Fact]
    public async Task An_authenticated_session_renews_strictly_before_its_access_token_expires()
    {
        var api = Create();

        var session = await api.AuthenticateAsync(KnownCredentials, Cancel);

        session.AccessToken.ShouldNotBeNullOrWhiteSpace("every later call carries it.");
        session.RefreshToken.ShouldNotBeNullOrWhiteSpace("and the renewal needs something to rotate.");
        session.RenewAfterSeconds.ShouldBeGreaterThan(
            0, "a horizon of zero tells the client to renew immediately and forever.");
        session.RenewAfterSeconds.ShouldBeLessThan(
            session.AccessExpiresInSeconds,
            $"renew after {session.RenewAfterSeconds}s, expires in {session.AccessExpiresInSeconds}s. "
            + "A renewal that fires at or after expiry is one the player sees.");
        session.RefreshExpiresInSeconds.ShouldBeGreaterThan(
            session.AccessExpiresInSeconds,
            "the family has to outlive the token it renews, or the first renewal is also the last.");
    }

    /// <summary>An accepted command answers with its own sequence and the hash of what it produced.</summary>
    /// <remarks>
    /// The sequence is what tells a client which of its in-flight commands was answered, and the
    /// hash is what the state mirror checks itself against. An implementation that echoed neither
    /// would still look like it worked, right up to the first resync.
    /// </remarks>
    [Fact]
    public async Task A_submitted_command_answers_with_its_own_sequence_and_a_state_hash()
    {
        var api = Create();
        await api.AuthenticateAsync(KnownCredentials, Cancel);

        var envelope = Envelope("CMD_accepted_probe", sequence: 4);
        var result = await api.SendCommandAsync(run: null, envelope, Cancel);

        result.Accepted.ShouldBeTrue("nothing about this command is refusable on either fixture.");
        result.Sequence.ShouldBe(
            envelope.Sequence,
            $"sent sequence {envelope.Sequence} and was answered {result.Sequence}. A client matches "
            + "answers to commands by this number, so an implementation that renumbered would deliver "
            + "every outcome to the wrong command.");
        result.StateHash.ShouldNotBeNullOrWhiteSpace(
            "an accepted command carries the hash of the state it produced; without it the mirror "
            + "cannot tell whether it is still in step with the server.");
        result.Profile.ShouldNotBeNull("and the profile it produced, which is what the client renders.");
    }

    /// <summary>
    /// 🔒 A refused command is a returned outcome carrying its reason — never an exception.
    /// </summary>
    /// <remarks>
    /// The server's own contract: a rejection rides HTTP 200 because the exchange succeeded and the
    /// answer was no. An implementation that threw would put "you are being rate limited" behind the
    /// same catch block as "the network is down", and the retry that is right for one is wrong for
    /// the other.
    /// </remarks>
    [Fact]
    public async Task A_refused_command_is_an_answer_carrying_its_reason_rather_than_an_exception()
    {
        var api = CreateRefusing();
        await api.AuthenticateAsync(KnownCredentials, Cancel);

        var envelope = Envelope("CMD_refused_probe", sequence: 5);

        // Awaited rather than wrapped: an implementation that threw fails this case with its own
        // exception, which is exactly the clause — a refusal is a value, not a failure.
        var result = await api.SendCommandAsync(run: null, envelope, Cancel);

        result.Accepted.ShouldBeFalse("the server refused it.");
        result.Rejection.ShouldBe(
            ScriptedRejection,
            $"the refusal arrived as {result.Rejection}. A caller decides what to show and whether to "
            + "retry from this value alone, so an implementation that dropped it leaves every "
            + "refusal looking the same.");
        result.Sequence.ShouldBe(
            envelope.Sequence, "a refusal still names the command it refused.");
    }

    /// <summary>The same command id sent twice is answered once — the second call replays the first.</summary>
    /// <remarks>
    /// This is the guarantee the whole resend-after-a-drop design rests on: a client that did not
    /// hear the answer resends the identical envelope, and the command must not run again. Asserted
    /// on the scalars a caller acts on rather than by comparing whole results, because a replayed
    /// body parses into an equal-but-distinct projection and record equality over its dictionaries
    /// is reference equality.
    /// </remarks>
    [Fact]
    public async Task The_same_command_id_submitted_twice_answers_identically()
    {
        var api = Create();
        await api.AuthenticateAsync(KnownCredentials, Cancel);

        var envelope = Envelope("CMD_idempotent_probe", sequence: 6);

        var first = await api.SendCommandAsync(run: null, envelope, Cancel);
        var second = await api.SendCommandAsync(run: null, envelope, Cancel);

        second.Accepted.ShouldBe(first.Accepted, "the same command id has one outcome, not two.");
        second.Sequence.ShouldBe(first.Sequence, "…answered against the same sequence.");
        second.StateHash.ShouldBe(
            first.StateHash,
            $"the replay answered hash '{second.StateHash}' where the first answer was "
            + $"'{first.StateHash}'. A different hash means the command was applied a second time, "
            + "which is exactly what a resend after a dropped connection would do to a player's "
            + "currency.");
    }

    /// <summary>A state read names the run it read and echoes the sequence it was asked from.</summary>
    /// <remarks>
    /// The echo is not decoration: the reconnect flow asks from a sequence and reconciles the answer
    /// against it, so an implementation that answered from somewhere else would silently skip or
    /// replay whatever fell between.
    /// </remarks>
    [Fact]
    public async Task A_state_read_names_the_run_and_echoes_the_sequence_it_was_asked_from()
    {
        var api = Create();
        await api.AuthenticateAsync(KnownCredentials, Cancel);

        var state = await api.FetchRunStateAsync(KnownRun, sinceSequence: 0, Cancel);

        state.RunId.ShouldBe(KnownRun, "a read of one run answers about that run.");
        state.SinceSequence.ShouldBe(
            0, "the sequence the client asked from, echoed exactly as it was accepted.");
        state.StateHash.ShouldNotBeNullOrWhiteSpace(
            "the mirror checks this answer against the one its last command returned.");
        state.MissedOutcomes.ShouldNotBeNull(
            "nothing missed is an empty list, not a null a caller has to test for.");
    }

    /// <summary>
    /// 🔒 A transport that cannot answer at all is <see cref="GameApiUnavailableException"/>, which is
    /// the one failure the reconnect machinery treats as "keep trying".
    /// </summary>
    /// <remarks>
    /// Asserted on the session call because it needs no prior state: an api nothing could reach has
    /// not authenticated either, and a case that had to sign in first would be asserting two things
    /// at once.
    /// </remarks>
    [Fact]
    public async Task A_transport_that_cannot_answer_throws_GameApiUnavailable()
    {
        var api = CreateUnreachable();

        await Should.ThrowAsync<GameApiUnavailableException>(
            () => api.AuthenticateAsync(KnownCredentials, Cancel),
            "nothing answered, so nothing was decided. Reported as a refusal this would look like a "
            + "credential problem and stop the client retrying; reported as a rejection it would "
            + "reach a player as a message about their account.");
    }

    private static CommandEnvelope Envelope(string commandId, long sequence) =>
        new(
            WireProtocol.PROTOCOL_VERSION,
            new CommandId(commandId),
            sequence,
            WireCommandCodec.WireNameOf(new SkipDraftCommand()),
            EmptyPayload);
}
