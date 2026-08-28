using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The write-model read behind <c>GET /run/{runId}/state?sinceSequence=N</c>: the player's own
/// persisted rows, the command path's own hash over them, and the stored outcome envelopes the
/// client missed — replayed, never recomputed.
/// </summary>
public sealed class RunStateQueryTests
{
    /// <summary>
    /// A ledger decorating the world's real one, so a case can choose exactly what the two read
    /// members answer while every envelope in it is one the gateway actually stored.
    /// </summary>
    /// <remarks>
    /// Both writing members throw: the query has no business calling either, and a fake that merely
    /// counted would let a write pass while the assertion sat in one case out of twelve.
    /// </remarks>
    private sealed class ScriptedLedger(ICommandLedgerStore inner) : ICommandLedgerStore
    {
        private bool _forgetsTheScope;
        private MissedOutcomes? _outcomes;

        internal const string WriteRefusal = "the state query wrote through the ledger";

        internal ScriptedLedger ForgettingTheScope()
        {
            _forgetsTheScope = true;

            return this;
        }

        internal ScriptedLedger AnsweringOutcomes(MissedOutcomes outcomes)
        {
            _outcomes = outcomes;

            return this;
        }

        public Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct) =>
            _forgetsTheScope
                ? Task.FromResult((long?)null)
                : inner.ReadLastSequenceAsync(scope, ct);

        public Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct) =>
            inner.ReadRecordAsync(scope, commandId, ct);

        public Task<MissedOutcomes> ReadOutcomesAfterAsync(string scope, long sinceSequence, CancellationToken ct) =>
            _outcomes is { } scripted
                ? Task.FromResult(scripted)
                : inner.ReadOutcomesAfterAsync(scope, sinceSequence, ct);

        public Task OpenScopeAsync(string scope, CancellationToken ct) =>
            throw new InvalidOperationException(WriteRefusal + ": OpenScopeAsync('" + scope + "').");

        public Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct) =>
            throw new InvalidOperationException(WriteRefusal + ": AppendAsync('" + scope + "').");
    }

    /// <summary>A started run whose scope has consumed sequences 1..5, and the bodies it stored for them.</summary>
    /// <param name="World">The world the rows and the envelopes live in.</param>
    /// <param name="Run">The run every case reads.</param>
    /// <param name="Bodies">The stored response body of sequence <c>n</c>, at index <c>n - 1</c>.</param>
    private sealed record FiveExchanges(GatewayWorld World, RunId Run, IReadOnlyList<string> Bodies);

    private static async Task<FiveExchanges> ARunThatHasAnsweredFiveCommandsAsync()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();
        var bodies = new List<string>();

        var opening = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("SKIP_DRAFT", 1, "c-run-1"), Worlds.Cancel);
        bodies.Add(Body(opening, 1));

        // REVIVE on a living run is refused by the domain, and a refusal is still a decided,
        // recorded, sequence-consuming answer — which is exactly what a replay has to hand back.
        for (var sequence = 2; sequence <= 5; sequence++)
        {
            var reply = await world.Gateway.SubmitRunCommandAsync(
                world.Player, run, Envelopes.Body("REVIVE", sequence, "c-run-" + sequence), Worlds.Cancel);
            bodies.Add(Body(reply, sequence));
        }

        return new FiveExchanges(world, run, bodies);
    }

    private static string Body(GatewayReply reply, long sequence)
    {
        if (reply.StatusCode != 200)
        {
            throw new InvalidOperationException(
                "The fixture command at sequence " + sequence + " answered HTTP " + reply.StatusCode +
                ", so nothing was recorded and every replay case below would read an empty ledger.");
        }

        return reply.Body;
    }

    private static RunStateQuery QueryOver(FiveExchanges fixture, ScriptedLedger ledger) =>
        new(new ReadOwnStateUseCase(fixture.World.Store), ledger);

    private static ScriptedLedger Scripted(FiveExchanges fixture) => new(fixture.World.Ledger);

    private static JsonElement Read(GatewayReply reply)
    {
        reply.StatusCode.ShouldBe(200, "the read succeeded, so the body is the state envelope: " + reply.Body);

        return JsonDocument.Parse(reply.Body).RootElement.Clone();
    }

    [Fact]
    public async Task ReadAsync_answers_the_players_own_persisted_rows()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 5, Worlds.Cancel);

        var body = Read(reply);
        var rows = await fixture.World.RowsAsync();

        body.GetProperty("protocolVersion").GetInt32().ShouldBe(
            1,
            "the answer states the version the SERVER speaks, and a client that reads this field to " +
            "decide whether it may apply the state would apply an envelope it cannot parse");
        body.GetProperty("runId").GetString().ShouldBe(fixture.Run.Value);
        body.GetProperty("sinceSequence").GetInt64().ShouldBe(5L, "the accepted value is echoed as accepted");
        body.GetProperty("sequence").GetInt64().ShouldBe(
            5L, "the run scope has consumed five commands, and the client reconciles against that number");
        body.GetProperty("run").GetProperty("position").GetInt32().ShouldBe(
            rows.Run!.Position,
            "the answer is the STORED row read through the write model — a fresh or default run would " +
            "stand somewhere else, and the client would resume at the wrong tile");
        body.GetProperty("profile").GetProperty("id").GetString().ShouldBe(fixture.World.Player.Value);
    }

    [Fact]
    public async Task ReadAsync_renders_the_run_without_its_seed()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 5, Worlds.Cancel);

        reply.Body.ShouldContain(
            "\"rngStreamPositions\"",
            Case.Sensitive,
            "the control for the claim below: the run projection really is in this text, so the " +
            "absence of the seed is an absence rather than an empty scan.");
        reply.Body.ShouldNotContain(
            "runSeed",
            Case.Sensitive,
            "a client holding the run seed can derive tomorrow's board, draft options and drops " +
            "(`02` §2) — the projection is the only shape that crosses this line, in any spelling.");
        Read(reply).GetProperty("run").TryGetProperty("runSeed", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ReadAsync_hashes_the_state_the_way_the_command_path_hashes_it()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 5, Worlds.Cancel);

        // 🔒 The expectation is the hash the COMMAND path itself wrote into the last stored envelope,
        // not one recomputed here: an expected value produced by the same call the answer is supposed
        // to have made would agree with a second hashing path just as happily as with the first.
        var lastCommandsHash = JsonDocument.Parse(fixture.Bodies[4]).RootElement
            .GetProperty("stateHash").GetString();

        lastCommandsHash.ShouldNotBeNullOrEmpty(
            "the fixture's last command must have carried a state hash, or the comparison below has " +
            "nothing to be a comparison against");

        Read(reply).GetProperty("stateHash").GetString().ShouldBe(
            lastCommandsHash,
            "the client verifies this answer against the stateHash its last command returned, and " +
            "sequence 5 changed nothing after it; a second hashing path would make the two disagree " +
            "over identical state and every reconnect would order a full resync.");

        var rows = await fixture.World.RowsAsync();
        var overTheseRows = WireProjections.HashPlayerAndRun(rows.Player, rows.Run!);

        overTheseRows.ShouldBe(
            lastCommandsHash,
            "the control on the fixture: the stored envelope's hash really is the hash of the rows " +
            "standing now, so the claim above is about this state rather than about a stale number " +
            "both sides happen to copy.");

        // The negative control: a state the player is not in hashes differently, so none of the
        // above is an equality that would hold whatever the run contained.
        WireProjections.HashPlayerAndRun(rows.Player, rows.Run! with { Gold = rows.Run!.Gold + 1 })
            .ShouldNotBe(lastCommandsHash);
    }

    [Fact]
    public async Task ReadAsync_replays_the_stored_envelopes_after_the_asked_sequence_in_ascending_order()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 2, Worlds.Cancel);

        var missed = Read(reply).GetProperty("missedOutcomes").EnumerateArray().ToArray();

        missed.Length.ShouldBe(3, "sequences 3, 4 and 5 were decided while the client was away");
        missed.Select(o => o.GetRawText()).ShouldBe(
            new[] { fixture.Bodies[2], fixture.Bodies[3], fixture.Bodies[4] },
            "byte for byte the envelopes the first processing stored, in ascending sequence order: a " +
            "recomputation would re-decide outcomes the client has already been told about, and an " +
            "unordered replay would apply them backwards.");
    }

    [Fact]
    public async Task ReadAsync_reports_nothing_missed_when_the_client_is_level_with_the_run()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 5, Worlds.Cancel);

        Read(reply).GetProperty("missedOutcomes").EnumerateArray().ToArray().ShouldBeEmpty();
        reply.Body.ShouldNotContain(
            "resyncFull",
            Case.Sensitive,
            "this is the ordinary reconnect — the client missed nothing, and a resync marker here " +
            "would throw away a correct local state on every dropped connection.");
    }

    [Fact]
    public async Task ReadAsync_asks_for_a_full_resync_when_the_ledger_cannot_enumerate_the_scope()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture).AnsweringOutcomes(MissedOutcomes.Unavailable));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 2, Worlds.Cancel);

        var body = Read(reply);
        body.GetProperty("resyncFull").GetBoolean().ShouldBeTrue(
            "a ledger that cannot produce the missed outcomes has not said there were none — " +
            "answering [] would tell the client it is up to date when three outcomes are missing.");
        body.GetProperty("missedOutcomes").EnumerateArray().ToArray().ShouldBeEmpty();
        body.GetProperty("sequence").GetInt64().ShouldBe(
            5L, "the scope is known, so the client still learns where the conversation stands");
    }

    [Fact]
    public async Task ReadAsync_asks_for_a_full_resync_when_a_missed_outcome_has_expired()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var surviving = MissedOutcomes.Of(
        [
            new ReplayedOutcome(4, fixture.Bodies[3]),
            new ReplayedOutcome(5, fixture.Bodies[4]),
        ]);
        var query = QueryOver(fixture, Scripted(fixture).AnsweringOutcomes(surviving));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 2, Worlds.Cancel);

        Read(reply).GetProperty("resyncFull").GetBoolean().ShouldBeTrue(
            "sequence 3 fell out under the 48 h TTL, so 4 and 5 do not cover 3..5 — handing them " +
            "over alone would apply two outcomes on top of a state the third never reached.");

        // The control that makes the claim about the GAP rather than about the scripting: the same
        // three sequences, contiguous, are replayed with no resync ordered.
        var whole = MissedOutcomes.Of(
        [
            new ReplayedOutcome(3, fixture.Bodies[2]),
            new ReplayedOutcome(4, fixture.Bodies[3]),
            new ReplayedOutcome(5, fixture.Bodies[4]),
        ]);
        var contiguous = await QueryOver(fixture, Scripted(fixture).AnsweringOutcomes(whole))
            .ReadAsync(fixture.World.Player, fixture.Run, 2, Worlds.Cancel);

        contiguous.Body.ShouldNotContain(
            "resyncFull",
            Case.Sensitive,
            "3, 4 and 5 cover 3..5 exactly, so nothing is missing and the client can resume " +
            "incrementally. A read that resynced whenever it had outcomes to hand over would pass " +
            "the case above for a reason that has nothing to do with the gap.");
        Read(contiguous).GetProperty("missedOutcomes").EnumerateArray().Count().ShouldBe(3);
    }

    [Fact]
    public async Task ReadAsync_asks_for_a_full_resync_when_the_client_is_ahead_of_the_run()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 6, Worlds.Cancel);

        var body = Read(reply);
        body.GetProperty("resyncFull").GetBoolean().ShouldBeTrue(
            "the client believes it has been answered at a sequence the run never reached, so its " +
            "model of the conversation is wrong and nothing incremental can repair it.");
        body.GetProperty("missedOutcomes").EnumerateArray().ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadAsync_omits_the_sequence_and_asks_for_a_full_resync_when_the_run_scope_is_gone()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture).ForgettingTheScope());

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 2, Worlds.Cancel);

        var body = Read(reply);
        body.TryGetProperty("sequence", out _).ShouldBeFalse(
            "the ledger no longer knows the scope, so there is no sequence to state — a 0 here would " +
            "read as 'the run has answered nothing yet' and invite the client to send sequence 1.");
        body.GetProperty("resyncFull").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ReadAsync_answers_without_writing_a_row_or_a_ledger_record()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var refusing = new RecordingCache(fixture.World.Cache).RefusingEveryWrite();
        var query = new RunStateQuery(
            new ReadOwnStateUseCase(new WorldSliceStore(refusing)), Scripted(fixture));

        var reply = await query.ReadAsync(fixture.World.Player, fixture.Run, 2, Worlds.Cancel);

        reply.StatusCode.ShouldBe(
            200,
            "every store beneath this read refuses to write, so a read that survives is a read that " +
            "wrote nothing: reading must not slide a run's 48 h expiry or stamp a player as active.");
        refusing.Writes.ShouldBeEmpty(
            "a refused write still records its key here, so an empty list is the stronger claim — " +
            "the query never even attempted one.");
    }

    [Fact]
    public async Task ReadAsync_answers_404_when_the_player_has_no_stored_state()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var query = QueryOver(fixture, Scripted(fixture));

        var reply = await query.ReadAsync(new PlayerId("PLAYER_never_created"), fixture.Run, 0, Worlds.Cancel);

        reply.StatusCode.ShouldBe(
            404,
            "nothing is stored for that id, and the read side answers an unknown player exactly as it " +
            "answers a run that is not theirs — a distinct code here would confirm which ids exist");
        reply.Body.ShouldBeEmpty("a refusal carries no contract shape, and a body here would say whose");
    }

    [Fact]
    public async Task ReadAsync_answers_a_strangers_run_exactly_as_it_answers_a_run_that_never_existed()
    {
        var fixture = await ARunThatHasAnsweredFiveCommandsAsync();
        var stranger = await fixture.World.SeedSecondPlayerAsync("PLAYER_wire_other");

        var opened = await fixture.World.Gateway.SubmitPlayerCommandAsync(
            stranger, Envelopes.StartRun(sequence: 1, commandId: "c-other-start"), Worlds.Cancel);
        var strangersRun = new RunId(
            Replies.Parse(opened, expectedStatus: 200)
                .GetProperty("outcome").GetProperty("runId").GetString()!);

        var query = QueryOver(fixture, Scripted(fixture));

        var foreign = await query.ReadAsync(fixture.World.Player, strangersRun, 0, Worlds.Cancel);
        var absent = await query.ReadAsync(fixture.World.Player, new RunId("RUN_nobody_opened"), 0, Worlds.Cancel);

        foreign.StatusCode.ShouldBe(404);
        foreign.ShouldBe(
            absent,
            "status and body both: any difference between 'someone else's run' and 'no such run' is " +
            "a probe that confirms a run id exists, and the read side answers the same to both.");
    }
}
