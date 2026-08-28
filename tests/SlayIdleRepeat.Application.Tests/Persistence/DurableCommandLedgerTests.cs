using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Persistence;

/// <summary>
/// The durable <c>ICommandLedgerStore</c>: the gateway's seam carried by the idempotency port,
/// asserted against the same semantics the gateway's own suite pinned on the volatile placeholder.
/// </summary>
public sealed class DurableCommandLedgerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    // Properties, not fields: a static initializer would turn any CommandScopes fault into a
    // TypeInitializationException naming this class instead of the spelling that threw.
    private static string PlayerScope => CommandScopes.ForPlayer(new PlayerId("PLAYER_1"));
    private static string RunScope => CommandScopes.ForRun(new PlayerId("PLAYER_1"), new RunId("RUN_9"));

    private static (DurableCommandLedger Ledger, AdjustableClock Clock) Build()
    {
        var clock = new AdjustableClock();

        return (new DurableCommandLedger(new InMemoryIdempotencyStore(clock), Ttl), clock);
    }

    private static LedgerRecord Record(long sequence, string? opensScope = null) =>
        new(new CommandId("CMD_" + sequence), sequence,
            new StartRunCommand(1, DifficultyTier.NORMAL),
            """{"protocolVersion":1,"sequence":""" + sequence + "}",
            opensScope);

    [Fact]
    public async Task A_scope_never_opened_has_no_last_sequence()
    {
        var (ledger, _) = Build();

        (await ledger.ReadLastSequenceAsync(RunScope, Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task Opening_a_scope_starts_it_at_zero_and_reopening_resets_nothing()
    {
        var (ledger, _) = Build();
        await ledger.OpenScopeAsync(RunScope, Cancel);
        (await ledger.ReadLastSequenceAsync(RunScope, Cancel)).ShouldBe(0L);

        await ledger.AppendAsync(RunScope, Record(1), Cancel);
        await ledger.OpenScopeAsync(RunScope, Cancel);

        (await ledger.ReadLastSequenceAsync(RunScope, Cancel)).ShouldBe(1L,
            "the replay path re-opens on every replayed opening acceptance, and a reset here would "
            + "zero a live run's counter on a retried START_RUN.");
    }

    [Fact]
    public async Task An_appended_record_reads_back_with_the_command_decoded_equal()
    {
        var (ledger, _) = Build();
        var record = Record(1, opensScope: RunScope);

        await ledger.AppendAsync(PlayerScope, record, Cancel);
        var read = await ledger.ReadRecordAsync(PlayerScope, record.CommandId, Cancel);

        read.ShouldNotBeNull();
        read.Command.ShouldBe(record.Command,
            "the stored (type, payload) pair must decode through the one codec to a command equal "
            + "to the appended one — that equality IS the duplicate-versus-conflict decision.");
        read.Sequence.ShouldBe(1L);
        read.ResponseBody.ShouldBe(record.ResponseBody,
            "a duplicate replays these bytes, never a recomputation.");
        read.OpensRunScope.ShouldBe(RunScope,
            "the open-repair marker survives storage, or a crash between append and open bricks "
            + "the new run.");
    }

    [Fact]
    public async Task Appending_advances_the_scopes_last_sequence()
    {
        var (ledger, _) = Build();

        await ledger.AppendAsync(PlayerScope, Record(1), Cancel);

        (await ledger.ReadLastSequenceAsync(PlayerScope, Cancel)).ShouldBe(1L);
    }

    [Fact]
    public async Task A_record_is_invisible_in_the_other_scope()
    {
        var (ledger, _) = Build();
        var record = Record(1);

        await ledger.AppendAsync(PlayerScope, record, Cancel);

        (await ledger.ReadRecordAsync(RunScope, record.CommandId, Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_command_id_reads_back_as_null()
    {
        var (ledger, _) = Build();
        await ledger.AppendAsync(PlayerScope, Record(1), Cancel);

        (await ledger.ReadRecordAsync(PlayerScope, new CommandId("CMD_unknown"), Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task A_record_expires_on_the_configured_window()
    {
        var (ledger, clock) = Build();
        var record = Record(1);
        await ledger.AppendAsync(PlayerScope, record, Cancel);

        clock.Advance(Ttl + TimeSpan.FromMinutes(1));

        (await ledger.ReadRecordAsync(PlayerScope, record.CommandId, Cancel)).ShouldBeNull(
            "the ledger hands the store its configured 48 h window — a ledger that pinned its own "
            + "number would drift from the run TTL it is defined to equal.");
    }

    [Fact]
    public async Task ReadOutcomesAfterAsync_says_it_cannot_enumerate_this_scope_by_sequence()
    {
        var (ledger, _) = Build();
        await ledger.AppendAsync(RunScope, Record(1), Cancel);
        await ledger.AppendAsync(RunScope, Record(2), Cancel);

        var missed = await ledger.ReadOutcomesAfterAsync(RunScope, 0, Cancel);

        missed.IsAvailable.ShouldBeFalse(
            "the port beneath keys records on (scope, commandId) and offers no by-sequence read, so "
            + "the honest answer is 'I cannot enumerate this' — an empty list would read as 'nothing "
            + "was missed' and a reconnecting client would resume on top of two outcomes it never saw.");
        missed.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scope_key_this_repository_never_spelled_is_refused()
    {
        var (ledger, _) = Build();

        await Should.ThrowAsync<ArgumentException>(
            async () => await ledger.ReadLastSequenceAsync("session:PLAYER_1", Cancel));
    }
}
