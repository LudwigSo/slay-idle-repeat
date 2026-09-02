using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IPlayerRepository"/> <em>means</em> (<c>23</c> §4.2, §5 A8): the
/// system-of-record row behind every command, whose stored document IS the codec bytes.
/// </summary>
/// <remarks>
/// Round-trips are asserted on the canonical stored bytes, never on record equality: the snapshots
/// hold collections, whose record equality is reference equality, so a "same" comparison there
/// would pass a store that returned a differently-populated profile of the same shape.
/// </remarks>
[ContractSuiteFor(typeof(IPlayerRepository))]
public abstract class IPlayerRepositoryContractTests
{
    /// <summary>A repository under test, over a backing store of its own.</summary>
    protected abstract IPlayerRepository Create();

    [Fact]
    public async Task A_player_that_was_never_stored_reads_back_as_null()
    {
        var repository = Create();

        (await repository.GetAsync(new PlayerId("PLAYER_never-stored"), PersistenceWorlds.Cancel))
            .ShouldBeNull(
                "no row exists for this id, and null is how this port says so — answering with a "
                + "blank profile would hand out a fresh account and overwrite the real one on the "
                + "next commit.");
    }

    [Fact]
    public async Task A_created_profile_reads_back_byte_identical()
    {
        var repository = Create();
        var profile = PersistenceWorlds.FreshProfile();

        await repository.CreateAnonymousAsync(profile, PersistenceWorlds.Cancel);
        var read = await repository.GetAsync(profile.Player.Id, PersistenceWorlds.Cancel);

        read.ShouldNotBeNull();
        PersistenceWorlds.CanonicalBytes(read).ShouldBe(
            PersistenceWorlds.CanonicalBytes(profile),
            "the stored document is the exact codec bytes — a store that reads back anything else "
            + "is rehydrating a player the domain never produced.");
    }

    [Fact]
    public async Task Creation_returns_the_created_players_id()
    {
        var repository = Create();
        var profile = PersistenceWorlds.FreshProfile();

        (await repository.CreateAnonymousAsync(profile, PersistenceWorlds.Cancel))
            .ShouldBe(profile.Player.Id);
    }

    [Fact]
    public async Task Creating_an_id_that_already_has_a_row_is_refused_and_the_row_survives()
    {
        var repository = Create();
        var original = PersistenceWorlds.FreshProfile("Original");
        await repository.CreateAnonymousAsync(original, PersistenceWorlds.Cancel);

        var usurper = new PlayerProfile(
            PersistenceWorlds.FreshProfile("Usurper").Player with { Id = original.Player.Id },
            ActiveRun: null);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await repository.CreateAnonymousAsync(usurper, PersistenceWorlds.Cancel),
            "server-minted ids never collide, so a second creation under one id is a miswired "
            + "caller — merging or overwriting here would hand one player another's account.");

        var read = await repository.GetAsync(original.Player.Id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull();
        PersistenceWorlds.CanonicalBytes(read).ShouldBe(
            PersistenceWorlds.CanonicalBytes(original),
            "the refused creation must leave the original row exactly as it was.");
    }

    [Fact]
    public async Task Saving_replaces_the_players_row()
    {
        var repository = Create();
        var before = PersistenceWorlds.ProfileInARun();
        await repository.CreateAnonymousAsync(before, PersistenceWorlds.Cancel);

        var after = before with { ActiveRun = null };
        await repository.SaveAsync(after, PersistenceWorlds.Cancel);

        var read = await repository.GetAsync(before.Player.Id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull();
        PersistenceWorlds.CanonicalBytes(read).ShouldBe(
            PersistenceWorlds.CanonicalBytes(after),
            "the second save wins whole — a store that merged the two rows would resurrect the run "
            + "the domain just closed.");
    }

    [Fact]
    public async Task Saving_a_player_with_no_row_stores_one()
    {
        var repository = Create();
        var profile = PersistenceWorlds.FreshProfile();

        await repository.SaveAsync(profile, PersistenceWorlds.Cancel);

        (await repository.GetAsync(profile.Player.Id, PersistenceWorlds.Cancel)).ShouldNotBeNull(
            "a save is the commit of a player's current state wherever that row came from — a "
            + "deployment may seed rows outside the creation door (a support restore, the CI "
            + "probe), and a store that refused them would make every later command of that "
            + "player fail.");
    }

    [Fact]
    public async Task A_profile_in_a_run_round_trips_its_run()
    {
        var repository = Create();
        var profile = PersistenceWorlds.ProfileInARun();

        await repository.CreateAnonymousAsync(profile, PersistenceWorlds.Cancel);
        var read = await repository.GetAsync(profile.Player.Id, PersistenceWorlds.Cancel);

        read.ShouldNotBeNull();
        read.ActiveRun.ShouldNotBeNull(
            "the profile committed with a run and read back without one — the pair moves together "
            + "or a crash-shaped bug becomes a player whose run silently vanished.");
        PersistenceWorlds.CanonicalBytes(read.ActiveRun).ShouldBe(
            PersistenceWorlds.CanonicalBytes(profile.ActiveRun!));
    }

    [Fact]
    public async Task A_default_player_id_is_an_argument_fault_everywhere_it_can_arrive()
    {
        var repository = Create();
        var headless = new PlayerProfile(
            PersistenceWorlds.FreshProfile().Player with { Id = default }, ActiveRun: null);

        await Should.ThrowAsync<ArgumentException>(
            async () => await repository.GetAsync(default, PersistenceWorlds.Cancel),
            "default(PlayerId) carries no text; a store that keyed on it would pool every such "
            + "caller's state into one row.");

        await Should.ThrowAsync<ArgumentException>(
            async () => await repository.SaveAsync(headless, PersistenceWorlds.Cancel));

        await Should.ThrowAsync<ArgumentException>(
            async () => await repository.CreateAnonymousAsync(headless, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task A_null_profile_is_a_null_argument_fault()
    {
        var repository = Create();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await repository.SaveAsync(null!, PersistenceWorlds.Cancel));

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await repository.CreateAnonymousAsync(null!, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_every_method()
    {
        var repository = Create();
        var profile = PersistenceWorlds.FreshProfile();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await repository.GetAsync(profile.Player.Id, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await repository.SaveAsync(profile, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await repository.CreateAnonymousAsync(profile, source.Token));
    }
}
