using Shouldly;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Commands;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The commands a player issued that the server has not answered: how they are numbered, why a retry
/// must not renumber them, and why an acknowledgement out of order is refused rather than absorbed.
/// </summary>
/// <remarks>
/// 🔴 The whole idempotency protocol rests on one property of this class: the id and the sequence are
/// assigned once and reused on every attempt. A queue that reminted either would turn a dropped
/// answer into a duplicated command — the roll charged twice, the purchase made twice — and no test
/// downstream of here could tell, because both attempts would look perfectly well-formed.
/// </remarks>
public sealed class CommandQueueTests
{
    /// <summary>A command that needs no arrangement, because no case here is about what it does.</summary>
    private static GameCommand AnyCommand() => new RollDiceCommand();

    // ---- numbering ------------------------------------------------------------------------------

    [Fact]
    public void Enqueue_takes_its_command_id_from_the_generator_and_numbers_from_one()
    {
        var ids = CountingIdGenerator.Counting();
        var queue = new CommandQueue(ids);

        var first = queue.Enqueue(AnyCommand(), NetWorlds.Run);

        first.Id.Value.ShouldBe(
            CountingIdGenerator.CommandIdNumber(1),
            "the id must come from IIdGeneratorPort and from nowhere else. Guid.NewGuid and " +
            "System.Random are banned outright in this codebase, and an id from an ambient source is " +
            "also an id no test can pin — which is why this asserts the generator's own value rather " +
            "than merely that the id is non-empty.");
        first.Sequence.ShouldBe(
            1,
            "the first command in a scope takes sequence 1. Starting at zero would send the server a " +
            "sequence it reserves for 'nothing consumed yet'.");
        ids.Minted.ShouldBe(1, "one command, one mint.");
    }

    [Fact]
    public void Enqueue_preserves_submission_order_and_advances_the_sequence()
    {
        var queue = new CommandQueue(CountingIdGenerator.Counting());

        var first = queue.Enqueue(AnyCommand(), NetWorlds.Run);
        var second = queue.Enqueue(AnyCommand(), NetWorlds.Run);

        queue.Pending.Select(pending => pending.Id).ShouldBe(
            [first.Id, second.Id],
            "the queue's order IS the order the player issued the commands in, and the server applies " +
            "them in the order it receives them. A queue that reordered would apply a purchase before " +
            "the roll that paid for it.");
        second.Sequence.ShouldBe(
            2,
            "each command in a scope takes the next number. A repeated sequence is SEQUENCE_STALE at " +
            "the server and the second command is silently discarded.");
        queue.PendingCount.ShouldBe(2);
    }

    /// <summary>
    /// 🔒 The run endpoint and the player endpoint are separate scopes with separate counters.
    /// </summary>
    [Fact]
    public void Enqueue_counts_the_run_scope_and_the_player_scope_apart()
    {
        var queue = new CommandQueue(CountingIdGenerator.Counting());

        queue.Enqueue(AnyCommand(), NetWorlds.Run);
        queue.Enqueue(AnyCommand(), NetWorlds.Run);
        var player = queue.Enqueue(AnyCommand(), run: null);

        player.Sequence.ShouldBe(
            1,
            "the player endpoint keeps its own counter, so the first command sent to it is its first " +
            "regardless of how much run traffic has gone past. One shared counter here would number a " +
            "player command from the run's history and the server would answer SEQUENCE_GAP — a " +
            "refusal a player would see as a control that simply does nothing.");
    }

    // ---- 🔒 a retry is the same command, not a new one -------------------------------------------

    /// <summary>
    /// 🔒 A retry resends the SAME command id and the SAME sequence — the whole idempotency contract.
    /// </summary>
    [Fact]
    public void The_head_keeps_its_id_and_sequence_across_repeated_reads()
    {
        var ids = CountingIdGenerator.Counting();
        var queue = new CommandQueue(ids);
        var queued = queue.Enqueue(AnyCommand(), NetWorlds.Run);

        var firstAttempt = queue.Head;
        var retry = queue.Head;

        retry!.Id.ShouldBe(
            queued.Id,
            "a retry is the SAME command reaching the server a second time, and the command id is how " +
            "the server recognises it as one: it replays the first attempt's outcome instead of " +
            "applying the command again. A fresh id here is a duplicated command — the roll charged " +
            "twice — and nothing downstream could tell, because both requests are well-formed.");
        retry.Sequence.ShouldBe(
            firstAttempt!.Sequence,
            "the sequence is the other half of the same contract. A renumbered retry arrives as a NEW " +
            "command at a new position, so even a server that deduplicated on the id would see a gap " +
            "where the original sequence should have been.");
        ids.Minted.ShouldBe(
            1,
            "the generator was asked once. Without this the case above passes on a queue that reminted " +
            "the id and happened to be handed an equal one — counting the mints is what distinguishes " +
            "'reused' from 'coincidentally equal'.");
    }

    // ---- acknowledging ---------------------------------------------------------------------------

    [Fact]
    public void Acknowledge_drops_the_head_and_leaves_the_rest_in_order()
    {
        var queue = new CommandQueue(CountingIdGenerator.Counting());
        var first = queue.Enqueue(AnyCommand(), NetWorlds.Run);
        var second = queue.Enqueue(AnyCommand(), NetWorlds.Run);

        queue.Acknowledge(first.Id);

        queue.PendingCount.ShouldBe(1);
        queue.Head!.Id.ShouldBe(
            second.Id,
            "acknowledging the answered command hands the next one to the drain. If the queue kept the " +
            "answered command the drain would resend it forever and nothing behind it would ever go.");
    }

    /// <summary>
    /// 🔒 Refused loudly rather than searched for, because a silent reorder hides the bug behind a
    /// wrong game state.
    /// </summary>
    [Fact]
    public void Acknowledge_refuses_an_id_that_is_not_the_head()
    {
        var queue = new CommandQueue(CountingIdGenerator.Counting());
        queue.Enqueue(AnyCommand(), NetWorlds.Run);
        var second = queue.Enqueue(AnyCommand(), NetWorlds.Run);

        var refusal = Should.Throw<InvalidOperationException>(() => queue.Acknowledge(second.Id));

        refusal.Message.ShouldContain(
            second.Id.Value,
            Case.Insensitive,
            "the message has to name the id that was offered, because the caller's whole problem is " +
            "that it thinks it holds an answer to a different command from the one at the front — and " +
            "a refusal that does not say which id it refused sends a reader to the wrong end of the " +
            "queue.");
        queue.PendingCount.ShouldBe(
            2,
            "nothing is dropped on a refusal. A queue that removed the out-of-order command anyway " +
            "would leave the older one behind the newer, and the two would reach the server reversed — " +
            "which is a wrong game state rather than a crash, and therefore far harder to find.");
    }

    [Fact]
    public void Acknowledge_refuses_when_nothing_is_queued()
    {
        var queue = new CommandQueue(CountingIdGenerator.Counting());
        var queued = queue.Enqueue(AnyCommand(), NetWorlds.Run);
        queue.Acknowledge(queued.Id);

        Should.Throw<InvalidOperationException>(
            () => queue.Acknowledge(queued.Id),
            "a second acknowledgement of the same command means an answer arrived twice, or a command " +
            "was dropped without being sent. Absorbing it silently would let a double-drain look " +
            "healthy right up until the command it skipped was needed.");
    }
}
