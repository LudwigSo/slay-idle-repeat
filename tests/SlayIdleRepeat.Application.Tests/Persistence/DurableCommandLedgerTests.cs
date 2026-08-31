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
    public async Task ReadOutcomesAfterAsync_enumerates_the_scope_above_the_bound()
    {
        var (ledger, _) = Build();
        await ledger.OpenScopeAsync(RunScope, Cancel);
        await ledger.AppendAsync(RunScope, Record(1), Cancel);
        await ledger.AppendAsync(RunScope, Record(2), Cancel);
        await ledger.AppendAsync(RunScope, Record(3), Cancel);

        var missed = await ledger.ReadOutcomesAfterAsync(RunScope, 1, Cancel);

        missed.IsAvailable.ShouldBeTrue(
            "the port beneath reads by sequence, so this ledger enumerates rather than refusing — "
            + "which is what makes an incremental resume possible at all instead of costing every "
            + "reconnecting client its whole local state.");
        missed.Records.Select(record => record.Sequence).ShouldBe([2L, 3L],
            "the bound is exclusive and the order is ascending: 1 is already held, and a client "
            + "applying 3 before 2 would apply them backwards.");
        missed.Records[0].ResponseBody.ShouldBe(Record(2).ResponseBody,
            "the replayed bytes are the ones the first processing stored, never a re-rendering.");
    }

    [Fact]
    public async Task ReadOutcomesAfterAsync_hands_back_the_hole_an_expired_record_left()
    {
        var (ledger, clock) = Build();
        await ledger.OpenScopeAsync(RunScope, Cancel);
        await ledger.AppendAsync(RunScope, Record(1), Cancel);

        // Far enough that 1 has fallen out of its window and 2 has not — the state a run that was
        // played across the record lifetime genuinely reaches.
        clock.Advance(Ttl - TimeSpan.FromMinutes(1));
        await ledger.AppendAsync(RunScope, Record(2), Cancel);
        clock.Advance(TimeSpan.FromMinutes(2));

        var missed = await ledger.ReadOutcomesAfterAsync(RunScope, 0, Cancel);

        missed.IsAvailable.ShouldBeTrue(
            "the enumeration succeeded; that a record has expired out of it is a different fact "
            + "from 'this backing cannot enumerate'.");
        missed.Records.Select(record => record.Sequence).ShouldBe([2L],
            "an expired record is simply absent, leaving a HOLE the caller detects and answers with "
            + "a full resync. A ledger that padded or renumbered it would hand over a short list the "
            + "caller would take for the complete set of what was missed.");
    }

    [Fact]
    public async Task A_scope_key_this_repository_never_spelled_is_refused()
    {
        var (ledger, _) = Build();

        await Should.ThrowAsync<ArgumentException>(
            async () => await ledger.ReadLastSequenceAsync("session:PLAYER_1", Cancel));
    }
}
