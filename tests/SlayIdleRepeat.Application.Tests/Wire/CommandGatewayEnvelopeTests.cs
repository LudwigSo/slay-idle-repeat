using Shouldly;
using SlayIdleRepeat.Application.Tests.UseCases;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The envelope's outer contract through the gateway: the 400 boundary, the 14 §16.1 skew window,
/// and the decode rejections — everything answered before any state is read, so every rejection
/// here must also carry no <c>stateHash</c> and consume no sequence number.
/// </summary>
public sealed class CommandGatewayEnvelopeTests
{
    [Fact]
    public async Task A_body_that_is_not_JSON_is_HTTP_400_with_no_contract_body()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(world.Player, "not json {", Worlds.Cancel);

        reply.StatusCode.ShouldBe(400, "14 §16.2: a body that is not a parseable envelope is the one 400");
        reply.Body.ShouldBeEmpty("the 400 carries no contract shape — the client bug goes to telemetry");
    }

    [Theory]
    [InlineData("{\"commandId\": \"c-1\", \"sequence\": 1, \"type\": \"ROLL_DICE\"}", "protocolVersion missing")]
    [InlineData("{\"protocolVersion\": 1, \"sequence\": 1, \"type\": \"ROLL_DICE\"}", "commandId missing")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"type\": \"ROLL_DICE\"}", "sequence missing")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"sequence\": 1}", "type missing")]
    [InlineData("{\"protocolVersion\": \"1\", \"commandId\": \"c-1\", \"sequence\": 1, \"type\": \"ROLL_DICE\"}", "protocolVersion not an integer")]
    [InlineData("{\"protocolVersion\": 1, \"commandId\": \"c 1\", \"sequence\": 1, \"type\": \"ROLL_DICE\"}", "commandId carries whitespace")]
    [InlineData("[1, 2, 3]", "the body is not an object")]
    public async Task A_body_missing_a_required_envelope_field_is_HTTP_400(string body, string why)
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(world.Player, body, Worlds.Cancel);

        reply.StatusCode.ShouldBe(400, $"the 400 boundary is JSON parse + required-field presence: {why}");
    }

    [Fact]
    public async Task A_version_outside_the_skew_window_is_a_200_rejection_naming_the_window_rule()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-v2", protocolVersion: 2), Worlds.Cancel);

        var body = Replies.Rejection(reply, "PROTOCOL_VERSION_UNSUPPORTED");
        body.GetProperty("sequence").GetInt64().ShouldBe(1, "the rejection echoes the request's sequence");
        body.TryGetProperty("stateHash", out _).ShouldBeFalse(
            "no state was read for a version refusal, so there is nothing honest to hash");
    }

    [Fact]
    public async Task The_previous_version_is_inside_the_skew_window_and_dispatches()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-v0", protocolVersion: 0), Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        body.TryGetProperty("rejected", out _).ShouldBeFalse(
            "14 §16.1 accepts {N, N−1}; N−1 must ride the same pipeline as N, not a tolerated error path");
        body.GetProperty("protocolVersion").GetInt32().ShouldBe(
            1, "the response speaks the server's own version, never an echo of the client's");
    }

    [Theory]
    [InlineData("NOT_A_COMMAND")]
    [InlineData("USE_REROLL")]
    [InlineData("DICE_FORGE_CHOOSE")]
    public async Task A_type_with_no_registry_row_is_UNKNOWN_COMMAND_TYPE_including_the_retired_names(string type)
    {
        // The two retired names are the load-bearing half of this case: D41 retires a wire name
        // rather than reusing it, so an old client's send must answer as unknown — never decode to
        // a different command, and never 400 (the envelope itself is perfectly well-formed).
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body(type, sequence: 1, commandId: "c-u"), Worlds.Cancel);

        Replies.Rejection(reply, "UNKNOWN_COMMAND_TYPE");
    }

    [Theory]
    [InlineData("{\"chapterId\": 1, \"tier\": \"NOT_A_TIER\"}", "an enum value the registry does not declare")]
    [InlineData("{\"chapterId\": 1, \"tier\": \"NORMAL\", \"extra\": true}", "a payload member the command does not declare")]
    [InlineData("5", "a payload that is not an object")]
    public async Task A_payload_the_commands_schema_refuses_is_MALFORMED_COMMAND(string payload, string why)
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("START_RUN", 1, "c-m", payload), Worlds.Cancel);

        Replies.Rejection(reply, "MALFORMED_COMMAND");
        why.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_command_on_the_wrong_endpoint_is_MALFORMED_COMMAND_in_both_directions()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        // A meta command on the run endpoint: the registry lists BEGIN_SESSION under
        // POST /player/command, so the run endpoint's schema has no such command.
        var metaOnRun = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run,
            Envelopes.Body("BEGIN_SESSION", 1, "c-r1", "{\"clientVersion\": \"1.0\", \"contentHash\": \"h\"}"),
            Worlds.Cancel);
        Replies.Rejection(metaOnRun, "MALFORMED_COMMAND");

        // A run command on the player endpoint — except START_RUN, whose exception is the whole
        // point of the routing rule.
        var runOnPlayer = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("ROLL_DICE", 2, "c-p1"), Worlds.Cancel);
        Replies.Rejection(runOnPlayer, "MALFORMED_COMMAND");

        // START_RUN on the run endpoint: the registry submits it on the player endpoint, since the
        // run id in this route is the id START_RUN exists to create.
        var startOnRun = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.StartRun(sequence: 1, commandId: "c-r2"), Worlds.Cancel);
        Replies.Rejection(startOnRun, "MALFORMED_COMMAND");
    }

    [Fact]
    public async Task A_pre_dispatch_rejection_consumes_no_sequence_number()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        // Burn the same sequence on three pre-dispatch refusals…
        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(1, "c-a", protocolVersion: 9), Worlds.Cancel);
        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("NOT_A_COMMAND", 1, "c-b"), Worlds.Cancel);
        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("ROLL_DICE", 1, "c-c"), Worlds.Cancel);

        // …and sequence 1 is still the expected next: the refused exchanges recorded nothing.
        var accepted = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-d"), Worlds.Cancel);

        var body = Replies.Parse(accepted, expectedStatus: 200);
        body.TryGetProperty("rejected", out _).ShouldBeFalse(
            "a client refused for version, unknown type or routing retries the fixed envelope at the " +
            "same sequence; if the refusals had consumed it, every such repair would SEQUENCE_STALE");
    }
}
