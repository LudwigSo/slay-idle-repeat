using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Application.Tests.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The one local profile: created once, found again by every later launch, and built out of authored
/// values rather than plausible ones.
/// </summary>
public sealed class InProcessGameHostProfileTests
{
    /// <summary>The authored starting Legend Level, read where the game reads it.</summary>
    private const string AuthoredMinimumLegendLevel = "tuning/progression.json#/legendLevel/min";

    // ═══════════════════════════════════════════════════════ creating it, exactly once

    [Fact]
    public async Task OpenProfileAsync_creates_a_profile_the_read_side_finds()
    {
        var host = Hosts.Over(new InMemoryLocalCache());

        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var read = await host.ReadOwnStateAsync(player, null, Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.Found,
            "the host answered with an identity nothing is stored for, so the very next command would " +
            "fail on the load rather than being applied.");
        read.View!.Run.ShouldBeNull("a profile that has never played stands in no run.");
    }

    [Fact]
    public async Task OpenProfileAsync_returns_the_same_profile_a_second_time_and_writes_nothing()
    {
        var cache = new RecordingCache(new InMemoryLocalCache());
        var host = Hosts.Over(cache);

        var first = await host.OpenProfileAsync(Worlds.Cancel);
        var writesToCreateIt = cache.Writes.Count;

        var second = await host.OpenProfileAsync(Worlds.Cancel);

        writesToCreateIt.ShouldBeGreaterThan(
            0,
            "creating the profile stored nothing, so 'the second call wrote nothing' below would be " +
            "true of a host that never writes at all.");

        second.ShouldBe(
            first,
            "the second call minted a second profile. One local profile is the whole design here: a " +
            "fresh id on relaunch is a player's account silently replaced.");

        cache.Writes.Count.ShouldBe(
            writesToCreateIt,
            "opening an existing profile wrote to the store. Reading is not an event, and rewriting " +
            "the starting row over a played one would reset the account.");
    }

    /// <summary>
    /// The row goes down before anything that names it. A crash between the two writes then leaves an
    /// unreferenced row and the next launch mints a fresh profile — a leak. The other order leaves a
    /// name for a row that is not there, and every command afterwards fails on the load forever.
    /// </summary>
    [Fact]
    public async Task OpenProfileAsync_commits_the_profile_row_before_anything_that_names_it()
    {
        var cache = new RecordingCache(new InMemoryLocalCache());
        var host = Hosts.Over(cache);

        var player = await host.OpenProfileAsync(Worlds.Cancel);

        cache.Writes.Count.ShouldBeGreaterThan(
            1,
            "creating the profile wrote the row and nothing else, so the next launch has no way to " +
            "find it again — and 'the row went first' would be true of a host that wrote only one key.");

        cache.Writes[0].ShouldBe(
            SliceKeys.ForPlayer(player),
            "something was stored ahead of the player's own row. Whatever points at a profile has to " +
            "be written after the profile exists, or an interrupted first launch is a permanently " +
            "bricked one rather than a leaked row.");
    }

    [Fact]
    public async Task OpenProfileAsync_returns_the_stored_profile_to_a_fresh_host_over_the_same_cache()
    {
        var store = new InMemoryLocalCache();

        var created = await Hosts.Over(store).OpenProfileAsync(Worlds.Cancel);

        // A generator of its own, so a host that minted instead of reading answers a different id —
        // the port's own guarantee is that two generators never share a sequence.
        var relaunched = Hosts.Over(store.Reopen(), ids: new CountingIdGenerator());

        var reopened = await relaunched.OpenProfileAsync(Worlds.Cancel);

        // The control for the comparison: this second generator really does mint something else, so
        // "the same id came back" is evidence of a read rather than of two generators agreeing.
        var elsewhere = await Hosts.Over(new InMemoryLocalCache(), ids: new CountingIdGenerator())
            .OpenProfileAsync(Worlds.Cancel);

        elsewhere.ShouldNotBe(
            created,
            "two hosts over two empty caches minted the same identity, so the assertion below cannot " +
            "tell a profile that was read back from one that was minted again.");

        reopened.ShouldBe(
            created,
            "the next launch of the app did not find the profile the last one created. The pointer and " +
            "the row are both in the cache the fresh host was handed.");
    }

    // ═══════════════════════════════════════════════════════ what the starting row holds

    /// <summary>
    /// The host persists exactly the starting row the shared factory built, field for field, and
    /// loses nothing on the way through the store and back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Not a drift guard, and it is worth saying which it is. It was written as one, against a
    /// second starting row transcribed inside the domain harness; that transcription is gone —
    /// <c>Player.CreateStarting</c> builds both — so there is no longer a version of this that fails
    /// by the two rows disagreeing. What it still discriminates is the round trip: the host reaches
    /// the reference row through a commit, an encode, a decode and a read, and any field that path
    /// dropped or rewrote shows up here.
    /// </para>
    /// <para>
    /// Both rows are built at one instant, so the clock-derived fields are equal by construction
    /// rather than normalised away; only the identity, which is a generated value, is aligned.
    /// Compared as canonical bytes: these rows carry dictionaries, which record equality compares by
    /// reference.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OpenProfileAsync_writes_the_starting_row_the_domain_harness_builds()
    {
        var clock = new AdjustableClock();
        clock.Set(Worlds.Start);

        var host = Hosts.Over(new InMemoryLocalCache(), clock: clock);

        var player = await host.OpenProfileAsync(Worlds.Cancel);
        var stored = (await host.ReadOwnStateAsync(player, null, Worlds.Cancel)).View!.Player;

        var harness = new InMemoryGame(Worlds.Content, Worlds.Seed, new VirtualClock(clock.UtcNow));
        var reference = harness.State(harness.CreatePlayer()).Player.ToSnapshot();

        // The teeth: without these, the comparison below would also hold if the writer collapsed
        // every row to the same bytes. Two shapes, because a scalar and a collection reach the
        // comparison down different paths — and it is the collections this row is compared by bytes
        // for in the first place.
        CanonicalStateWriter.HashMetaCommandState(reference with { LegendXp = reference.LegendXp + 1 })
            .ShouldNotBe(
                CanonicalStateWriter.HashMetaCommandState(reference),
                "one changed scalar produced identical bytes, so the comparison below cannot see a " +
                "difference either.");

        CanonicalStateWriter.HashMetaCommandState(
                reference with
                {
                    FeatCounters = new Dictionary<string, long>(StringComparer.Ordinal)
                    {
                        ["FEAT_NOTHING_HAS_EVER_COUNTED"] = 1L,
                    },
                })
            .ShouldNotBe(
                CanonicalStateWriter.HashMetaCommandState(reference),
                "an entry added to a counter dictionary produced identical bytes. Every collection " +
                "on this row would then be invisible to the comparison below, which is exactly the " +
                "half a starting row is most likely to drift on.");

        CanonicalStateWriter.HashMetaCommandState(
                stored with { Id = reference.Id, DisplayName = reference.DisplayName })
            .ShouldBe(
                CanonicalStateWriter.HashMetaCommandState(reference),
                "the profile the host creates and the one the domain harness creates differ in some " +
                "field. Everything but the generated identity is authored, derived from the clock, or " +
                "the identity element, so there is nothing here the two are entitled to disagree on.");
    }

    [Fact]
    public async Task OpenProfileAsync_names_the_profile_after_the_identity_it_minted()
    {
        var host = Hosts.Over(new InMemoryLocalCache());

        var player = await host.OpenProfileAsync(Worlds.Cancel);
        var stored = (await host.ReadOwnStateAsync(player, null, Worlds.Cancel)).View!.Player;

        stored.DisplayName.ShouldBe(
            player.Value,
            "the display name is the one field the comparison against the domain harness normalises " +
            "away with the identity, so it is pinned here instead. Nothing has asked the player for a " +
            "name yet, and inventing one would be a value with no author.");
    }

    // ═══════════════════════════════════════════════════════════════ the authored floor

    [Fact]
    public async Task OpenProfileAsync_starts_the_profile_at_the_authored_Legend_Level_minimum()
    {
        var host = Hosts.Over(new InMemoryLocalCache());

        var player = await host.OpenProfileAsync(Worlds.Cancel);
        var stored = (await host.ReadOwnStateAsync(player, null, Worlds.Cancel)).View!.Player;

        stored.LegendLevel.ShouldBe(
            Worlds.Content.ReadInt32(AuthoredMinimumLegendLevel),
            "the starting level is authored, not chosen here. A row below the authored minimum does " +
            "not rehydrate at all, and one above it is a level the player was given for nothing.");
    }

    /// <summary>
    /// …and it follows the tuning rather than agreeing with it by coincidence: the same host over a
    /// content set whose authored minimum has moved starts the profile at the moved value.
    /// </summary>
    [Fact]
    public async Task OpenProfileAsync_follows_the_authored_minimum_when_the_tuning_moves()
    {
        var moved = ContentLoader
            .Load(RepoData.SourceWithEdit("tuning/progression.json", "\"min\": 1,", "\"min\": 3,"))
            .Require();

        moved.ReadInt32(AuthoredMinimumLegendLevel).ShouldBe(
            3,
            "the edit did not take, so this case would be re-asserting the shipped value and a " +
            "hard-coded starting level would satisfy it.");

        Worlds.Content.ReadInt32(AuthoredMinimumLegendLevel).ShouldNotBe(
            3, "…and the moved value has to differ from the shipped one, or nothing here discriminates.");

        var host = Hosts.Over(new InMemoryLocalCache(), content: moved);

        var player = await host.OpenProfileAsync(Worlds.Cancel);
        var stored = (await host.ReadOwnStateAsync(player, null, Worlds.Cancel)).View!.Player;

        stored.LegendLevel.ShouldBe(
            moved.ReadInt32(AuthoredMinimumLegendLevel),
            "the host wrote a starting level the content set does not author. Read off the moved set " +
            "rather than restated, so this stays the same assertion the case above makes and not a " +
            "second transcription of the number the edit happened to pick.");
    }
}
