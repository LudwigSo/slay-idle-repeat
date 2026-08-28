using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IUnitOfWork"/> <em>means</em>: one processed command is one commit, and
/// what it carries lands whole or not at all.
/// </summary>
/// <remarks>
/// The atomicity cases are the ones a fake can genuinely state, because a fake can be told to fail
/// one half on purpose — which is a question no store-backed adapter can be asked without the store
/// itself. What the fake cannot state is that a real transaction rolls back on a crash mid-write;
/// that half belongs to the probe named in this port's exemption entries.
/// </remarks>
[ContractSuiteFor(typeof(IUnitOfWork))]
public abstract class IUnitOfWorkContractTests
{
    /// <summary>The lifetime the cases that are not about lifetimes use.</summary>
    protected static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private static readonly PlayerId Player = new("PLAYER_uow-suite");

    private static readonly RunId Run = new("RUN_uow-suite");

    /// <summary>A unit of work under test, over stores of its own.</summary>
    protected abstract IUnitOfWork Create();

    /// <summary>Whether the snapshots of the last commit are readable back.</summary>
    protected abstract Task<PlayerProfile?> StoredProfileAsync(PlayerId player);

    /// <summary>The outcome record stored under a scope and command id, or <c>null</c>.</summary>
    protected abstract Task<RecordedCommandOutcome?> StoredOutcomeAsync(
        IdempotencyScope scope, CommandId commandId);

    /// <summary>The scope's counter, or <c>null</c> when the scope was never opened.</summary>
    protected abstract Task<long?> LastSequenceAsync(IdempotencyScope scope);

    /// <summary>Every economy-log row this unit of work has committed, in commit order.</summary>
    protected abstract Task<IReadOnlyList<EconomyEventRecord>> StoredEconomyEventsAsync();

    /// <summary>Tells the unit of work under test to fail the record half of its next commit.</summary>
    protected abstract void FailTheRecordHalf();

    /// <summary>Tells the unit of work under test to fail the snapshot half of its next commit.</summary>
    protected abstract void FailTheSnapshotHalf();

    [Fact]
    public async Task An_accepted_commands_snapshots_record_and_counter_all_land()
    {
        var unitOfWork = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        var profile = PersistenceWorlds.ProfileInARun();
        var commit = Commit(scope, profile, sequence: 1);

        await unitOfWork.CommitAsync(commit, PersistenceWorlds.Cancel);

        var stored = await StoredProfileAsync(profile.Player.Id);
        stored.ShouldNotBeNull("the snapshots are half of what makes the command have happened at all.");

        // Canonical bytes, never record equality: the snapshots hold collections, whose synthesized
        // equality is reference equality, so "same" would pass a differently-populated profile.
        PersistenceWorlds.CanonicalBytes(stored).ShouldBe(
            PersistenceWorlds.CanonicalBytes(profile),
            "the state committed is the state the command produced. A store that lands a profile of "
            + "the right shape and the wrong contents is the torn write nothing downstream can see.");
        (await StoredOutcomeAsync(scope, commit.Outcome.CommandId)).ShouldBe(
            commit.Outcome,
            "the record holds the bytes a duplicate replays, and every field of it is what an "
            + "IDEMPOTENCY_CONFLICT is decided on.");
        (await LastSequenceAsync(scope)).ShouldBe(
            1L,
            "a counter still behind the record it was committed with is the torn state that lets a "
            + "retry pass the sequence gate and double-apply a committed command.");
        (await StoredEconomyEventsAsync()).ShouldBe(
            commit.EconomyEvents,
            "the economy log is what the currency ledger is later audited from. Rows appended "
            + "outside the commit can be lost while the command that produced them still stands.");
    }

    [Fact]
    public async Task A_refusals_commit_records_the_outcome_and_stores_no_snapshot()
    {
        var unitOfWork = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        var commit = Commit(scope, profile: null, sequence: 1);

        await unitOfWork.CommitAsync(commit, PersistenceWorlds.Cancel);

        (await StoredOutcomeAsync(scope, commit.Outcome.CommandId)).ShouldNotBeNull(
            "a refusal is a decided answer, and recording it is what lets its duplicate replay.");
        (await StoredProfileAsync(Player)).ShouldBeNull(
            "a refused command changed nothing, so a snapshot written here is a state no command "
            + "ever produced.");
        (await StoredEconomyEventsAsync()).ShouldBeEmpty(
            "nothing moved, so there is nothing for the economy log to have recorded.");
    }

    [Fact]
    public async Task A_commit_whose_record_half_fails_leaves_no_snapshot_behind()
    {
        var unitOfWork = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        var profile = PersistenceWorlds.ProfileInARun();
        FailTheRecordHalf();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await unitOfWork.CommitAsync(
                Commit(scope, profile, sequence: 1), PersistenceWorlds.Cancel));

        (await StoredProfileAsync(profile.Player.Id)).ShouldBeNull(
            "a snapshot standing without its record is a move the player made that no retry can "
            + "find, so the retry decides it a second time against state it already changed.");
        (await LastSequenceAsync(scope)).ShouldBeNull(
            "…and the counter must not have moved either.");
        (await StoredEconomyEventsAsync()).ShouldBeEmpty(
            "…nor may the log keep rows describing currency the stored player never gained.");
    }

    /// <summary>
    /// The other direction, which is not the same rule read backwards: the halves are written in
    /// some order, and only whichever one goes second is protected by an implementation that
    /// abandons on the first fault rather than by a real boundary.
    /// </summary>
    [Fact]
    public async Task A_commit_whose_snapshot_half_fails_records_no_outcome_and_moves_no_counter()
    {
        var unitOfWork = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        var profile = PersistenceWorlds.ProfileInARun();
        var commit = Commit(scope, profile, sequence: 1);
        FailTheSnapshotHalf();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await unitOfWork.CommitAsync(commit, PersistenceWorlds.Cancel));

        (await StoredOutcomeAsync(scope, commit.Outcome.CommandId)).ShouldBeNull(
            "a record standing without its snapshot replays an acceptance to a client whose stored "
            + "player never made the move — the answer and the state disagree forever after.");
        (await LastSequenceAsync(scope)).ShouldBeNull(
            "…and a counter advanced past a command that did not happen refuses the client's honest "
            + "retry as out of sequence.");
        (await StoredEconomyEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_opening_commit_leaves_the_new_scope_open_at_zero()
    {
        var unitOfWork = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        var opened = IdempotencyScope.ForRun(Player, Run);
        var profile = PersistenceWorlds.ProfileInARun();

        await unitOfWork.CommitAsync(
            Commit(scope, profile, sequence: 1) with { OpensScope = opened }, PersistenceWorlds.Cancel);

        (await LastSequenceAsync(opened)).ShouldBe(
            0L,
            "the scope and the record naming it are one effect. Opened separately, a crash between "
            + "them leaves a committed acceptance whose run can never be addressed — and 0 rather "
            + "than null is what makes the run's first command the expected sequence 1.");
    }

    [Fact]
    public async Task An_already_cancelled_token_is_observed()
    {
        var unitOfWork = Create();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await unitOfWork.CommitAsync(
                Commit(IdempotencyScope.ForPlayer(Player), PersistenceWorlds.FreshProfile(), sequence: 1),
                source.Token));
    }

    private static CommandCommit Commit(IdempotencyScope scope, PlayerProfile? profile, long sequence) =>
        new(
            scope,
            new RecordedCommandOutcome(
                new CommandId("CMD_uow-" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                sequence,
                "BEGIN_SESSION",
                """{"ClientVersion":"0.1.0","ContentHash":"sha256:abc"}""",
                """{"protocolVersion":1,"sequence":1}"""),
            Ttl,
            profile,
            profile is null ? Array.Empty<EconomyEventRecord>() : Rows(profile),
            OpensScope: null);

    private static IReadOnlyList<EconomyEventRecord> Rows(PlayerProfile profile) =>
    [
        new(profile.Player.Id, profile.ActiveRun?.Id, new CommandId("CMD_uow-1"), 0,
            new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.Zero), "CurrencyChanged",
            """{"type":"CurrencyChanged","currency":"GOLD","delta":1}"""),
    ];
}
