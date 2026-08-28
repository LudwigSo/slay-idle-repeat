using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IRunStateStore"/> <em>means</em> (<c>23</c> §4.2, §5 A8): the run row,
/// with a sliding lifetime restamped on every save.
/// </summary>
/// <remarks>
/// Expiry itself is deliberately not a case here: it is only observable against a store that
/// expires on a clock a test controls, so the in-memory fake pins it in its own fixture tests and
/// the live pair is exercised by the compose-boot probes. What IS every implementation's to honour
/// — the round-trip, the replace, the delete, the refused non-positive lifetime — is below.
/// </remarks>
[ContractSuiteFor(typeof(IRunStateStore))]
public abstract class IRunStateStoreContractTests
{
    /// <summary>A generous lifetime for the cases that are not about lifetimes.</summary>
    protected static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    /// <summary>A store under test, over a backing of its own.</summary>
    protected abstract IRunStateStore Create();

    [Fact]
    public async Task A_run_that_was_never_stored_reads_back_as_null()
    {
        var store = Create();

        (await store.GetAsync(new RunId("RUN_never-stored"), PersistenceWorlds.Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task A_saved_run_reads_back_byte_identical()
    {
        var store = Create();
        var run = PersistenceWorlds.ARun();

        await store.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);
        var read = await store.GetAsync(run.Id, PersistenceWorlds.Cancel);

        read.ShouldNotBeNull();
        PersistenceWorlds.CanonicalBytes(read).ShouldBe(
            PersistenceWorlds.CanonicalBytes(run),
            "the stored document is the exact codec bytes — anything else is a run the domain "
            + "never produced.");
    }

    [Fact]
    public async Task A_second_save_of_the_same_run_wins()
    {
        var store = Create();
        var run = PersistenceWorlds.ARun();
        await store.SaveAsync(run with { Gold = 1 }, Ttl, PersistenceWorlds.Cancel);

        await store.SaveAsync(run with { Gold = 2 }, Ttl, PersistenceWorlds.Cancel);

        var read = await store.GetAsync(run.Id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull();
        read.Gold.ShouldBe(2, "each accepted command commits the whole row over the last one.");
    }

    [Fact]
    public async Task Deleting_a_run_makes_it_a_miss_again()
    {
        var store = Create();
        var run = PersistenceWorlds.ARun();
        await store.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        await store.DeleteAsync(run.Id, PersistenceWorlds.Cancel);

        (await store.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_an_absent_run_is_a_no_op()
    {
        var store = Create();
        var run = PersistenceWorlds.ARun();
        await store.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        await store.DeleteAsync(new RunId("RUN_never-stored"), PersistenceWorlds.Cancel);

        (await store.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldNotBeNull(
            "deleting a run that is not there must not disturb the one that is.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_lifetime_is_refused(int hours)
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await store.SaveAsync(
                PersistenceWorlds.ARun(), TimeSpan.FromHours(hours), PersistenceWorlds.Cancel),
            "a row born expired is a delete spelled confusingly, and letting it through would make "
            + "a misconfigured lifetime read as data loss.");
    }

    [Fact]
    public async Task A_default_run_id_is_an_argument_fault_everywhere_it_can_arrive()
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.GetAsync(default, PersistenceWorlds.Cancel),
            "default(RunId) carries no text; a store that keyed on it would pool every such "
            + "caller's state into one row.");

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.SaveAsync(
                PersistenceWorlds.ARun() with { Id = default }, Ttl, PersistenceWorlds.Cancel));

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.DeleteAsync(default, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task A_null_run_is_a_null_argument_fault()
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await store.SaveAsync(null!, Ttl, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_every_method()
    {
        var store = Create();
        var run = PersistenceWorlds.ARun();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.GetAsync(run.Id, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.SaveAsync(run, Ttl, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.DeleteAsync(run.Id, source.Token));
    }
}
