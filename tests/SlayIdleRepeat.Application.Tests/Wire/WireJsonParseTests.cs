using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The reading half of the wire dialect, driven against the writing half: what
/// <c>WireJson.Render</c> produces is what <c>WireJson.Parse*</c> has to be able to read back.
/// </summary>
/// <remarks>
/// 🔒 Every body here is RENDERED rather than hand-written, which is the whole point. A reader
/// tested against bodies its own author typed is a reader tested against that author's idea of the
/// dialect; tested against the renderer, a member rename on either side fails immediately instead of
/// shipping a client that cannot read its own protocol. The two exceptions are the unknown-reason
/// and malformed cases, which are about bodies this build cannot render on purpose.
/// </remarks>
public sealed class WireJsonParseTests
{
    private static readonly Lazy<(PlayerSnapshot Player, RunSnapshot Run)> LazyWorld = new(() =>
    {
        var (game, player) = Worlds.InARun();
        var slice = game.State(player);

        return (slice.Player.ToSnapshot(), slice.Run!.ToSnapshot());
    });

    private static PlayerSnapshot PlayerRow => LazyWorld.Value.Player;

    private static RunSnapshot RunRow => LazyWorld.Value.Run;

    private static string Hash => WireProjections.HashPlayerAndRun(PlayerRow, RunRow);

    [Fact]
    public void An_accepted_command_response_survives_a_render_and_a_parse()
    {
        var outcome = new AcceptedCommandOutcome(
            RunRow.Id,
            WireProjections.Of(RunRow),
            RunRow.RngStreamPositions,
            "0x00c0ffee0badf00d",
            Array.Empty<DomainEvent>());

        var rendered = WireJson.Render(
            CommandResponse.Accepted(42, outcome, WireProjections.Of(PlayerRow), Hash));

        var read = WireJson.ParseCommandResponse(rendered);

        read.Accepted.ShouldBeTrue("the envelope carries no rejection.");
        read.Sequence.ShouldBe(42, "the sequence is how a client matches an answer to its command.");
        read.StateHash.ShouldBe(Hash, "the hash the mirror checks itself against, verbatim.");
        read.RunId.ShouldBe(RunRow.Id, "the run the command acted on.");
        read.BattleSeed.ShouldBe("0x00c0ffee0badf00d", "the server-issued seed, unchanged.");
        read.Rejection.ShouldBeNull("an accepted command has no reason.");

        read.Profile.ShouldNotBeNull().Id.ShouldBe(
            PlayerRow.Id, "the profile projection came back as the player it describes.");
        read.Run.ShouldNotBeNull().Position.ShouldBe(
            RunRow.Position, "…and the run projection with the position the run is actually at, "
            + "which is the member a mirror renders the board from.");
        read.RngStreamStates.ShouldBe(
            RunRow.RngStreamPositions,
            "the draw counters ride the wire by name, and a mirror that lost them cannot predict "
            + "anything the server draws next.");
    }

    [Fact]
    public void A_rejected_command_response_survives_a_render_and_a_parse()
    {
        var rendered = WireJson.Render(
            CommandResponse.RejectedWith(43, RejectionReason.INSUFFICIENT_ENERGY, Hash));

        var read = WireJson.ParseCommandResponse(rendered);

        read.Accepted.ShouldBeFalse("the envelope is a rejection.");
        read.Rejection.ShouldBe(
            RejectionReason.INSUFFICIENT_ENERGY,
            "the reason is what a caller decides from — whether to show something, whether to retry, "
            + "whether to resync.");
        read.Sequence.ShouldBe(43, "a refusal still names the command it refused.");
        read.StateHash.ShouldBe(
            Hash, "the UNTOUCHED state's hash, which is what lets a mirror stay put after a refusal.");
        read.Profile.ShouldBeNull("nothing changed, so there is no projection to carry.");
    }

    [Fact]
    public void A_run_state_survives_a_render_and_a_parse_with_its_missed_outcomes_and_resync_marker()
    {
        var missed = new[]
        {
            JsonDocument.Parse(
                WireJson.Render(CommandResponse.RejectedWith(7, RejectionReason.COOLDOWN_ACTIVE, Hash)))
                .RootElement.Clone(),
            JsonDocument.Parse(
                WireJson.Render(
                    CommandResponse.Accepted(
                        8,
                        new AcceptedCommandOutcome(
                            RunRow.Id, WireProjections.Of(RunRow), null, null, Array.Empty<DomainEvent>()),
                        WireProjections.Of(PlayerRow),
                        Hash)))
                .RootElement.Clone(),
        };

        var rendered = WireJson.Render(
            new RunStateResponse(
                WireProtocol.PROTOCOL_VERSION,
                RunRow.Id,
                Sequence: 8,
                SinceSequence: 6,
                WireProjections.Of(PlayerRow),
                WireProjections.Of(RunRow),
                Hash,
                missed,
                ResyncFull: true));

        var read = WireJson.ParseRunState(rendered);

        read.RunId.ShouldBe(RunRow.Id, "the run that was read.");
        read.Sequence.ShouldBe(8, "the scope's last consumed sequence.");
        read.SinceSequence.ShouldBe(6, "echoed exactly as it was accepted.");
        read.StateHash.ShouldBe(Hash, "the hash over the two rows.");
        read.ResyncFull.ShouldBeTrue("the marker was present, and its presence is the whole signal.");
        read.Profile.Id.ShouldBe(PlayerRow.Id, "the profile projection.");
        read.Run.Id.ShouldBe(RunRow.Id, "the run projection.");

        read.MissedOutcomes.Select(o => (o.Sequence, o.Accepted)).ShouldBe(
            new[] { (7L, false), (8L, true) },
            "the embedded envelopes are command responses and are read by the same reader, in order. "
            + "A client replays these to catch up, so losing one or reordering two is a client that "
            + "animates the wrong thing.");
    }

    /// <summary>An absent <c>resyncFull</c> means false — the renderer omits it rather than spelling it.</summary>
    [Fact]
    public void A_run_state_without_the_resync_marker_reads_as_no_full_resync()
    {
        var rendered = WireJson.Render(
            new RunStateResponse(
                WireProtocol.PROTOCOL_VERSION,
                RunRow.Id,
                Sequence: null,
                SinceSequence: 0,
                WireProjections.Of(PlayerRow),
                WireProjections.Of(RunRow),
                Hash,
                Array.Empty<JsonElement>(),
                ResyncFull: null));

        var read = WireJson.ParseRunState(rendered);

        read.ResyncFull.ShouldBeFalse(
            "the marker is never written false; it is omitted. A reader that treated absence as "
            + "unknown — or as true — would resync the whole run on every ordinary poll.");
        read.Sequence.ShouldBeNull(
            "an omitted sequence means the ledger no longer knows the scope, which is not the same "
            + "as zero: zero invites the client to send sequence 1.");
        read.MissedOutcomes.ShouldBeEmpty("nothing was missed.");
    }

    /// <summary>
    /// 🔒 A <c>reason</c> this build has no member for is a generic rejection, not a parse failure.
    /// </summary>
    /// <remarks>
    /// Hand-written because it cannot be rendered: the value is one the enum does not have, which is
    /// exactly the position an older client is in when the server adds a reason. A reader that threw
    /// here would make every reason the server ever adds a breaking change for every shipped build.
    /// </remarks>
    [Fact]
    public void An_unknown_rejection_reason_reads_as_a_refusal_with_no_reason()
    {
        var read = WireJson.ParseCommandResponse(
            """
            {"protocolVersion":1,"sequence":9,"rejected":true,
             "reason":"A_REASON_FROM_A_LATER_PROTOCOL","stateHash":"fnv1a:0123456789abcdef"}
            """);

        read.Accepted.ShouldBeFalse("the server refused the command, whatever it called the refusal.");
        read.Rejection.ShouldBeNull(
            "an unknown value is treated as a generic rejection and resynced from — that is "
            + "RejectionReason's own contract, and the only reading that lets the catalogue grow.");
        read.Sequence.ShouldBe(9, "the rest of the envelope is still readable.");
        read.StateHash.ShouldBe("fnv1a:0123456789abcdef", "…including the untouched state's hash.");
    }

    /// <summary>
    /// A numeric <c>reason</c> is unknown too: the wire spelling is the NAME, and accepting the
    /// number would silently invent a reason out of an ordinal.
    /// </summary>
    [Fact]
    public void A_numeric_rejection_reason_is_not_read_as_the_member_with_that_value()
    {
        var read = WireJson.ParseCommandResponse(
            """{"protocolVersion":1,"sequence":10,"rejected":true,"reason":"8"}""");

        read.Rejection.ShouldBeNull(
            "'8' is RATE_LIMITED's ordinal, and Enum.TryParse would have accepted it. The wire spells "
            + "a reason by name, so a number is a value this build does not recognise — reading it as "
            + "a member would let a renumbering on either side rewrite what a refusal means.");
    }

    [Theory]
    [InlineData("", "an empty body is not JSON at all")]
    [InlineData("not json", "a body that is not JSON")]
    [InlineData("[1,2,3]", "a JSON array where an envelope belongs")]
    [InlineData("{}", "an envelope with no sequence")]
    [InlineData("""{"sequence":"twelve"}""", "a sequence that is not a number")]
    [InlineData("""{"sequence":12}""", "an accepted envelope with no outcome")]
    public void A_body_that_is_not_a_command_response_throws_WireParseException(string body, string why)
    {
        Should.Throw<WireParseException>(
            () => WireJson.ParseCommandResponse(body),
            why + " must fail loudly. Defaulting the missing member would hand a mirror an empty "
            + "state it cannot tell from a real one, and the client would then render it.");
    }

    [Theory]
    [InlineData("{}", "a run state with no run id")]
    [InlineData("""{"runId":"RUN_x","sinceSequence":0}""", "a run state with no profile")]
    [InlineData("""{"runId":"RUN_x","sinceSequence":0,"profile":{},"run":{},"stateHash":"h","missedOutcomes":{}}""",
        "missed outcomes that are not an array")]
    public void A_body_that_is_not_a_run_state_throws_WireParseException(string body, string why)
    {
        Should.Throw<WireParseException>(
            () => WireJson.ParseRunState(body),
            why + " must fail loudly rather than resolve to a half-filled answer the reconnect flow "
            + "would then reconcile against.");
    }

    /// <summary>
    /// ⚠️ The auth bodies are read case-sensitively as the handler literally spells them, because
    /// the handler serialises anonymous objects at DEFAULT options rather than through the camelCase
    /// policy the response envelopes use.
    /// </summary>
    [Fact]
    public void A_session_body_reads_the_literal_member_names_the_auth_handler_writes()
    {
        var session = WireJson.ParseSession(
            """
            {"playerId":"PLAYER_auth","accessToken":"ACCESS","accessExpiresInSeconds":3600,
             "renewAfterSeconds":2700,"refreshToken":"REFRESH","refreshExpiresInSeconds":2592000}
            """);

        session.Player.ShouldBe(new PlayerId("PLAYER_auth"));
        session.AccessToken.ShouldBe("ACCESS");
        session.AccessExpiresInSeconds.ShouldBe(3600);
        session.RenewAfterSeconds.ShouldBe(2700);
        session.RefreshToken.ShouldBe("REFRESH");
        session.RefreshExpiresInSeconds.ShouldBe(2592000);
    }

    [Fact]
    public void A_session_body_spelled_the_response_dialects_way_is_not_a_session()
    {
        Should.Throw<WireParseException>(
            () => WireJson.ParseSession(
                """{"PlayerId":"PLAYER_auth","AccessToken":"ACCESS","AccessExpiresInSeconds":3600}"""),
            "these members are matched case-sensitively against the spellings the auth handler "
            + "actually writes. A reader that matched loosely would go on working if the handler's "
            + "own dialect drifted, which is the moment somebody needs to be told.");
    }

    [Fact]
    public void A_device_registration_body_reads_the_literal_member_names_the_auth_handler_writes()
    {
        var registration = WireJson.ParseDeviceRegistration(
            """
            {"deviceId":"DEVICE_x","deviceSecret":"SECRET_x","playerId":"PLAYER_x",
             "displayName":"Someone"}
            """);

        registration.DeviceId.ShouldBe("DEVICE_x");
        registration.DeviceSecret.ShouldBe("SECRET_x");
        registration.Player.ShouldBe(new PlayerId("PLAYER_x"));
        registration.DisplayName.ShouldBe("Someone");
    }

    [Fact]
    public void A_device_registration_missing_its_secret_throws_WireParseException()
    {
        Should.Throw<WireParseException>(
            () => WireJson.ParseDeviceRegistration(
                """{"deviceId":"DEVICE_x","playerId":"PLAYER_x","displayName":"Someone"}"""),
            "the secret is the whole point of a registration: without it the account can never be "
            + "reopened, and a default would store an empty credential that fails much later.");
    }
}
