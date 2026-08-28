using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// One processed command is one commit: the aggregate snapshots, the outcome record with the
/// sequence it consumed, and the economy rows land together or not at all — and nothing
/// loss-tolerant rides inside.
/// </summary>
public sealed class CommandCommitTests
{
    private const string PickPerkPayload = "{\"optionIndex\": 0}";

    [Fact]
    public async Task An_accepted_command_commits_the_new_snapshots_and_its_response_record_together()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();
        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-perk", PickPerkPayload), Worlds.Cancel);

        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 2, "c-roll"), Worlds.Cancel);

        world.UnitOfWork.Commits.ShouldNotBeEmpty(
            "the command happened at the moment it was committed, so a command answered 200 with "
            + "nothing committed is an answer about a write that never took place.");

        var commit = world.UnitOfWork.Commits[^1];

        commit.Outcome.ResponseBody.ShouldBe(
            reply.Body,
            "the record holds the bytes the duplicate replays, so anything but the exact answer "
            + "sent means a retry is served a different exchange than the first one was.");
        commit.Outcome.Sequence.ShouldBe(2L);
        commit.Outcome.CommandId.ShouldBe(new CommandId("c-roll"));
        commit.Scope.ShouldBe(IdempotencyScope.ForRun(world.Player, run));

        commit.State.ShouldNotBeNull(
            "an accepted command moved the aggregates, and a record committed without them is the "
            + "torn state where a replay answers with a move the stored player never made.");
        commit.State!.ActiveRun.ShouldNotBeNull();
        commit.State.ActiveRun!.Position.ShouldBe(
            (await world.RowsAsync()).Run!.Position,
            "the snapshot committed is the one the command produced, not the one it was loaded with.");
    }

    [Fact]
    public async Task An_accepted_commands_economy_rows_ride_the_same_commit_as_its_record()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();
        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-perk", PickPerkPayload), Worlds.Cancel);

        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 2, "c-roll"), Worlds.Cancel);

        world.UnitOfWork.Commits.ShouldNotBeEmpty();

        var rows = world.UnitOfWork.Commits[^1].EconomyEvents;

        rows.Count.ShouldBe(
            1,
            "a roll produces exactly one event, and the log is what the economy is later audited "
            + "from — rows appended outside the commit can be lost while the command they describe "
            + "stands.");
        rows[0].EventType.ShouldBe("DiceRolled");
        rows[0].CommandId.ShouldBe(new CommandId("c-roll"));
        rows[0].Run.ShouldBe(run, "a run command's rows name the run they happened in.");
    }

    [Fact]
    public async Task A_refused_command_commits_its_record_alone()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, CommitEnvelopes.LockUnownedItem(sequence: 1, commandId: "c-no"), Worlds.Cancel);

        Replies.Rejection(reply, "NOT_OWNED");

        var commit = world.UnitOfWork.Commits.ShouldHaveSingleItem(
            "a refusal is a decided, recorded answer, and recording it is what lets its duplicate "
            + "replay instead of being decided a second time.");

        commit.State.ShouldBeNull(
            "a refused command changed nothing, so committing a snapshot would write a state no "
            + "command produced.");
        commit.EconomyEvents.ShouldBeEmpty();
        commit.OpensScope.ShouldBeNull();
        commit.Outcome.ResponseBody.ShouldBe(reply.Body);
    }

    [Fact]
    public async Task An_accepted_opening_command_carries_its_new_run_scope_in_the_same_commit()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        var opened = Replies.Parse(reply, expectedStatus: 200)
            .GetProperty("outcome").GetProperty("runId").GetString()!;

        var commit = world.UnitOfWork.Commits.ShouldHaveSingleItem();

        commit.OpensScope.ShouldBe(
            IdempotencyScope.ForRun(world.Player, new RunId(opened)),
            "the scope and the record that names it are established by one effect: two effects "
            + "leave a committed acceptance whose run can never be addressed.");
        commit.State!.ActiveRun!.Id.Value.ShouldBe(
            opened, "the run row rides the same commit as the scope that sequences it.");
    }

    [Fact]
    public async Task A_duplicate_replays_the_stored_bytes_and_writes_nothing()
    {
        var inner = new VolatileCommandLedger();
        var scripted = new ScriptedLedger(inner);
        var world = await GatewayWorld.WithAStartingPlayerAsync(ledger: scripted);
        var scope = CommandScopes.ForPlayer(world.Player);

        await inner.AppendAsync(
            scope,
            new LedgerRecord(
                new CommandId("c-start"), 1, new StartRunCommand(1, DifficultyTier.NORMAL),
                "{\"protocolVersion\":1,\"sequence\":1}",
                CommandScopes.ForRun(world.Player, new RunId("RUN_already-opened"))),
            Worlds.Cancel);

        var appendsBefore = scripted.Appends;
        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        reply.Body.ShouldBe("{\"protocolVersion\":1,\"sequence\":1}");
        scripted.ScopeOpens.ShouldBe(
            0,
            "a replay is a read. Re-opening the scope makes the answer depend on a row this command "
            + "no longer owns, which is what turned a duplicate into a 500 at the lifetime boundary.");
        (scripted.Appends - appendsBefore).ShouldBe(0);
        world.UnitOfWork.Commits.ShouldBeEmpty("a replay decides nothing, so it commits nothing.");
    }

    [Fact]
    public async Task A_duplicate_opening_command_still_replays_when_its_run_row_is_gone()
    {
        var inner = new VolatileCommandLedger();
        var scripted = new ScriptedLedger(inner) { RefusingScopeOpens = true };
        var world = await GatewayWorld.WithAStartingPlayerAsync(ledger: scripted);

        await inner.AppendAsync(
            CommandScopes.ForPlayer(world.Player),
            new LedgerRecord(
                new CommandId("c-start"), 1, new StartRunCommand(1, DifficultyTier.NORMAL),
                "{\"protocolVersion\":1,\"sequence\":1}",
                CommandScopes.ForRun(world.Player, new RunId("RUN_expired-away"))),
            Worlds.Cancel);

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        reply.StatusCode.ShouldBe(
            200,
            "a player-scoped record outlives the run row it points at, and a duplicate that "
            + "depends on that row still existing answers a fault where the first send answered a "
            + "run.");
        reply.Body.ShouldBe("{\"protocolVersion\":1,\"sequence\":1}");
    }

    [Fact]
    public async Task The_event_fan_out_runs_after_the_commit_and_its_failure_does_not_lose_the_command()
    {
        var order = new List<string>();
        var sink = new OrderedSink(order) { Failing = true };
        var world = await GatewayWorld.WithAStartingPlayerAsync(sinks: [sink], order: order);

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        Replies.Parse(reply, expectedStatus: 200);
        world.UnitOfWork.Commits.ShouldHaveSingleItem(
            "the fan-out is a side channel: a sink that cannot take the batch has no say in whether "
            + "the command happened.");

        order.ShouldBe(
            [CommitSteps.Commit, CommitSteps.Dispatch],
            "a sink delivered before the commit describes a state that may never land, and one "
            + "delivered inside it holds a player's command open on an analytics endpoint.");
    }
}
