using Shouldly;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The volatile ledger's replay read: which stored outcomes a reconnecting client missed, and the
/// difference between "none" and "this backing cannot say".
/// </summary>
public sealed class CommandLedgerReplayTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static string Scope => CommandScopes.ForRun(new PlayerId("PLAYER_1"), new RunId("RUN_9"));

    private static string OtherScope => CommandScopes.ForPlayer(new PlayerId("PLAYER_1"));

    private static LedgerRecord Record(long sequence) =>
        new(new CommandId("CMD_" + sequence), sequence,
            new StartRunCommand(1, DifficultyTier.NORMAL),
            """{"protocolVersion":1,"sequence":""" + sequence + "}");

    private static async Task<VolatileCommandLedger> WithFiveRecordsAsync()
    {
        var ledger = new VolatileCommandLedger();
        await ledger.OpenScopeAsync(Scope, Cancel);

        // Appended out of order on purpose: the ascending order below is the ledger's, not the
        // insertion order's, and a client replaying 5 before 3 would apply them backwards.
        foreach (var sequence in new long[] { 3, 1, 5, 2, 4 })
        {
            await ledger.AppendAsync(Scope, Record(sequence), Cancel);
        }

        return ledger;
    }

    [Fact]
    public async Task ReadOutcomesAfterAsync_returns_the_records_after_the_asked_sequence_in_ascending_order()
    {
        var ledger = await WithFiveRecordsAsync();

        var missed = await ledger.ReadOutcomesAfterAsync(Scope, 2, Cancel);

        missed.IsAvailable.ShouldBeTrue();
        missed.Records.Select(r => r.Sequence).ShouldBe(
            new[] { 3L, 4L, 5L },
            "the client resumes by applying these in order; a set that included 1 and 2 would re-apply " +
            "outcomes it has already acted on.");
        missed.Records.Select(r => r.ResponseBody).ShouldBe(
            new[] { Record(3).ResponseBody, Record(4).ResponseBody, Record(5).ResponseBody },
            "the stored bytes ride out unchanged — a replay is the first processing's answer, never a " +
            "second rendering of it.");
    }

    [Fact]
    public async Task ReadOutcomesAfterAsync_returns_every_record_from_sequence_zero()
    {
        var ledger = await WithFiveRecordsAsync();

        var missed = await ledger.ReadOutcomesAfterAsync(Scope, 0, Cancel);

        missed.Records.Select(r => r.Sequence).ShouldBe(
            new[] { 1L, 2L, 3L, 4L, 5L },
            "a client that has been answered nothing has missed everything the scope decided.");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public async Task ReadOutcomesAfterAsync_is_available_and_empty_when_nothing_was_missed(long sinceSequence)
    {
        var ledger = await WithFiveRecordsAsync();

        var missed = await ledger.ReadOutcomesAfterAsync(Scope, sinceSequence, Cancel);

        missed.IsAvailable.ShouldBeTrue(
            "this ledger CAN enumerate the scope and found nothing after " + sinceSequence +
            " — 'unavailable' would tell the caller to order a full resync on the commonest " +
            "reconnect there is.");
        missed.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadOutcomesAfterAsync_answers_a_scope_it_holds_nothing_for_as_available_and_empty()
    {
        var ledger = await WithFiveRecordsAsync();

        var missed = await ledger.ReadOutcomesAfterAsync(OtherScope, 0, Cancel);

        missed.IsAvailable.ShouldBeTrue(
            "a scope this ledger holds no records for is a scope it answers about — the caller " +
            "decides what an unknown run means from the last sequence, not from a replay that " +
            "claims it cannot look.");
        missed.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_replay_and_an_unavailable_one_are_different_answers()
    {
        var ledger = await WithFiveRecordsAsync();

        var nothingMissed = await ledger.ReadOutcomesAfterAsync(Scope, 5, Cancel);

        nothingMissed.Records.ShouldBeEmpty();
        MissedOutcomes.Unavailable.Records.ShouldBeEmpty();

        // Both sides pinned absolutely rather than merely "different": a pair that had swapped
        // meanings would satisfy an inequality and order a full resync on every clean reconnect.
        nothingMissed.IsAvailable.ShouldBeTrue(
            "the record list cannot tell these two apart, so IsAvailable has to — and this one means " +
            "'this ledger looked, and you are up to date'.");
        MissedOutcomes.Unavailable.IsAvailable.ShouldBeFalse(
            "and this one means 'this ledger cannot look', which costs the client its whole local " +
            "state. Reading the flag the wrong way round is the one mistake that is silent in both " +
            "directions.");
    }
}
