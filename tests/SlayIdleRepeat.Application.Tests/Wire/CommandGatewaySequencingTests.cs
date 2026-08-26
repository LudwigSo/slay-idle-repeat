using Shouldly;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// 14 §16.3 through the gateway: the two scopes, last + 1, the duplicate replay, and the three
/// resync reasons. Every case drives the public submits only.
/// </summary>
public sealed class CommandGatewaySequencingTests
{
    [Fact]
    public async Task START_RUN_rides_the_player_scope_and_opens_the_runs_own_scope_at_zero()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var started = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        var body = Replies.Parse(started, expectedStatus: 200);
        var runId = body.GetProperty("outcome").GetProperty("runId").GetString()!;

        // The wire allocator, not the deterministic mint: the in-process mint spells
        // RUN_<playerId>_<counter>, and a wire-issued identity must not embed the player.
        runId.ShouldStartWith("RUN_");
        runId.ShouldNotContain(
            world.Player.Value,
            customMessage: "the server allocated this id from the generator; the mint's " +
                           "RUN_<player>_<counter> shape here would mean the wire host issued nothing");

        // The allocated run's counter starts at 1: the first RUN command is expected at last+1 = 1.
        var first = await world.Gateway.SubmitRunCommandAsync(
            world.Player, new Core.Primitives.RunId(runId),
            Envelopes.Body("PICK_PERK", 1, "c-pick", "{\"optionIndex\": 0}"), Worlds.Cancel);

        Replies.Parse(first, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "the run scope must open at 0 on the accepted START_RUN, or no run command can ever land");
    }

    [Fact]
    public async Task A_run_command_for_a_run_no_START_RUN_opened_is_RUN_NOT_FOUND_never_a_404()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, new Core.Primitives.RunId("RUN_nobody_opened_this"),
            Envelopes.Body("ROLL_DICE", 1, "c-x"), Worlds.Cancel);

        reply.StatusCode.ShouldBe(200, "RUN_NOT_FOUND is an in-protocol answer, never expressed as HTTP 404");
        Replies.Rejection(reply, "RUN_NOT_FOUND");
    }

    [Fact]
    public async Task A_sequence_ahead_of_expected_is_SEQUENCE_GAP_and_one_behind_with_a_fresh_id_is_SEQUENCE_STALE()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        // Consume run sequence 1.
        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);

        var ahead = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 3, "c-3"), Worlds.Cancel);
        Replies.Rejection(ahead, "SEQUENCE_GAP");

        var behind = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 1, "c-fresh"), Worlds.Cancel);
        Replies.Rejection(behind, "SEQUENCE_STALE");

        // Neither resync reason consumed anything: the expected sequence is still 2.
        var next = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 2, "c-2"), Worlds.Cancel);
        Replies.Parse(next, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_duplicate_replays_the_stored_outcome_byte_identically_without_re_executing()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);

        var roll = Envelopes.Body("ROLL_DICE", 2, "c-roll");
        var first = await world.Gateway.SubmitRunCommandAsync(world.Player, run, roll, Worlds.Cancel);
        var rowsAfterFirst = await world.RowsAsync();

        var replay = await world.Gateway.SubmitRunCommandAsync(world.Player, run, roll, Worlds.Cancel);

        replay.StatusCode.ShouldBe(200);
        replay.Body.ShouldBe(
            first.Body,
            customMessage: "the stored outcome is replayed byte-identically (14 §16.3) — 'did my roll go through' " +
            "must be a non-question");

        var rowsAfterReplay = await world.RowsAsync();
        rowsAfterReplay.Run!.RngStreamPositions.ShouldBe(
            rowsAfterFirst.Run!.RngStreamPositions,
            "a replay re-executes nothing: a second dice draw here would be a different roll under " +
            "the same commandId, which is exactly what idempotency exists to rule out");

        // Whitespace and key order are not intent: the same command spelled differently is still
        // the same exchange, because the ledger compares the DECODED command.
        var reordered = "{\"type\": \"ROLL_DICE\", \"sequence\": 2, \"commandId\": \"c-roll\", " +
                        "\"protocolVersion\": 1, \"payload\": {}}";
        var replayedAnyway = await world.Gateway.SubmitRunCommandAsync(world.Player, run, reordered, Worlds.Cancel);
        replayedAnyway.Body.ShouldBe(first.Body);
    }

    [Fact]
    public async Task A_known_commandId_with_a_different_payload_or_sequence_is_IDEMPOTENCY_CONFLICT()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-used", "{\"optionIndex\": 0}"), Worlds.Cancel);

        // Same id, same sequence, different intent.
        var differentPayload = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-used", "{\"optionIndex\": 1}"), Worlds.Cancel);
        Replies.Rejection(differentPayload, "IDEMPOTENCY_CONFLICT");

        // Same id, different sequence, same intent.
        var differentSequence = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 2, "c-used", "{\"optionIndex\": 0}"), Worlds.Cancel);
        Replies.Rejection(differentSequence, "IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task A_domain_rejection_consumes_its_sequence_is_recorded_and_replays()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        // A second START_RUN with a run already active: the domain says no (ILLEGAL_STATE), and
        // that no is a decided, RECORDED answer — 14 §16.2's "understood, decided, and recorded".
        var refusal = Envelopes.StartRun(sequence: 2, commandId: "c-again");
        var refused = await world.Gateway.SubmitPlayerCommandAsync(world.Player, refusal, Worlds.Cancel);
        var body = Replies.Rejection(refused, "ILLEGAL_STATE");
        body.GetProperty("stateHash").GetString()!.ShouldStartWith(
            "fnv1a:",
            customMessage: "a dispatched rejection hashes the UNTOUCHED state, so a divergent mirror resyncs on a no");

        // Recorded under its sequence: the next fresh command is expected at 3, not 2…
        var atTwo = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("LOCK_ITEM", 2, "c-next", "{\"itemId\": \"g1\", \"locked\": true}"),
            Worlds.Cancel);
        Replies.Rejection(atTwo, "SEQUENCE_STALE");

        // …and the refused exchange replays byte-identically for its own commandId.
        var replay = await world.Gateway.SubmitPlayerCommandAsync(world.Player, refusal, Worlds.Cancel);
        replay.Body.ShouldBe(refused.Body, customMessage: "a 200-rejection is final for that commandId — retrying it re-decides nothing");
    }

    [Fact]
    public async Task Another_players_run_answers_RUN_NOT_FOUND_and_costs_the_owner_nothing()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();
        var intruder = await world.SeedSecondPlayerAsync("PLAYER_intruder");

        // The intruder guesses the owner's runId and the small next sequence.
        var foreign = await world.Gateway.SubmitRunCommandAsync(
            intruder, run, Envelopes.Body("PICK_PERK", 1, "c-intrude", "{\"optionIndex\": 0}"), Worlds.Cancel);

        Replies.Rejection(foreign, "RUN_NOT_FOUND");
        Replies.Rejection(foreign, "RUN_NOT_FOUND").TryGetProperty("stateHash", out _).ShouldBeFalse(
            "the foreign submit read no state, so there is nothing honest to hash");

        // The owner's scope is untouched: their sequence 1 is still expected, and the intruder's
        // commandId never landed in the owner's idempotency space.
        var owners = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-intrude", "{\"optionIndex\": 0}"), Worlds.Cancel);

        Replies.Parse(owners, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "a run scope is its OWNER's: a foreign player naming the runId must consume nothing — " +
            "not a sequence number (or the owner desyncs) and not a commandId (or the owner's " +
            "genuine command conflicts with an intruder's record)");
    }

    [Fact]
    public async Task A_replayed_START_RUN_repairs_a_run_scope_whose_open_never_landed()
    {
        // The seam the record-then-open pair leaves: with a durable ledger, appending the
        // acceptance and opening the run scope are two store calls and the process can die between
        // them. No shipped command can reach that state through the volatile ledger, so the fixture
        // constructs it directly (S25): a decorator drops the first open.
        var ledger = new OpenDroppingLedger(new VolatileCommandLedger()) { DropOpens = true };
        var world = await GatewayWorld.WithAStartingPlayerAsync(ledger: ledger);

        var startRun = Envelopes.StartRun(sequence: 1, commandId: "c-start");
        var first = await world.Gateway.SubmitPlayerCommandAsync(world.Player, startRun, Worlds.Cancel);
        var runId = Replies.Parse(first, expectedStatus: 200)
            .GetProperty("outcome").GetProperty("runId").GetString()!;

        // The broken state is real: the accepted run answers RUN_NOT_FOUND.
        var beforeRepair = await world.Gateway.SubmitRunCommandAsync(
            world.Player, new Core.Primitives.RunId(runId),
            Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);
        Replies.Rejection(beforeRepair, "RUN_NOT_FOUND");

        // The client's own retry rule — same commandId after a fault — is the repair path.
        ledger.DropOpens = false;
        var replay = await world.Gateway.SubmitPlayerCommandAsync(world.Player, startRun, Worlds.Cancel);
        replay.Body.ShouldBe(first.Body, customMessage: "the replay is still the stored bytes, never a re-execution");

        var afterRepair = await world.Gateway.SubmitRunCommandAsync(
            world.Player, new Core.Primitives.RunId(runId),
            Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);
        Replies.Parse(afterRepair, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "the replayed opening acceptance re-opens its run scope, so the run it named is " +
            "reachable again instead of RUN_NOT_FOUND forever");
    }

    /// <summary>A ledger whose <c>OpenScopeAsync</c> can be made to silently fail — the durable-store crash window, constructed.</summary>
    private sealed class OpenDroppingLedger(ICommandLedgerStore inner) : ICommandLedgerStore
    {
        internal bool DropOpens { get; set; }

        public Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct) =>
            inner.ReadLastSequenceAsync(scope, ct);

        public Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct) =>
            inner.ReadRecordAsync(scope, commandId, ct);

        public Task OpenScopeAsync(string scope, CancellationToken ct) =>
            DropOpens ? Task.CompletedTask : inner.OpenScopeAsync(scope, ct);

        public Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct) =>
            inner.AppendAsync(scope, record, ct);
    }
}
