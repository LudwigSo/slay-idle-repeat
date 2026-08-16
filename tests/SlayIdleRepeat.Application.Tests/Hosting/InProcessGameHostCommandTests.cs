using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The write seam one layer out: the host resolves everything ambient a command is applied under, and
/// what it commits is what the next launch reads.
/// </summary>
public sealed class InProcessGameHostCommandTests
{
    private static BeginSessionCommand BeginSession => new("1.0.0", "content");

    private static StartRunCommand StartRun => new(Worlds.Chapter, DifficultyTier.NORMAL);

    // ═════════════════════════════════════════════════ the per-command seed, both kinds

    /// <summary>
    /// A meta command reaches its handler with a seed. Pinned through the one command that actually
    /// reads it: the same command applied without one is a loud defect, so acceptance here is
    /// evidence rather than an absence of evidence.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_gives_a_meta_command_the_seed_its_handler_draws_from()
    {
        var cache = new InMemoryLocalCache();
        var host = Hosts.Over(cache);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var slice = await new WorldSliceStore(cache.Reopen())
            .LoadAsync(player, Worlds.Content, Worlds.Cancel);

        // The control, before the command is spent: this command draws on the day's first call only,
        // so once it has been sent the slice no longer demands a seed at all.
        var denied = Should.Throw<InvalidOperationException>(
            () => GameRules.Apply(
                slice,
                BeginSession,
                new GameContext(
                    slice.Player.LastAppliedAtUtc,
                    CommandSeed: null,
                    Worlds.Content,
                    LocalHostAmbience.NoSubscriptionResolved(),
                    LocalHostAmbience.NoRemoteConfigResolved())),
            "this command draws on the day's first call, so applying it with no seed must fail. " +
            "Without that, the acceptance below is true whether the host issued a seed or not.");

        denied.Message.ShouldContain(
            "carries no CommandSeed",
            Case.Sensitive,
            "the control has to fail for the seed's absence specifically.");

        var outcome = await host.SubmitAsync(player, null, BeginSession, Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue(
            "the host refused a meta command " + outcome.Rejection + " that fails loudly without a " +
            "seed, so the seed is what it did not issue.");
    }

    /// <summary>
    /// A run command takes no fresh entropy — including <c>START_RUN</c>, which arrives addressed to
    /// the player and would be classified meta by anything reading the endpoint instead of the row.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_draws_no_fresh_entropy_for_a_run_command_including_START_RUN()
    {
        var ids = new RecordingIdGenerator(new CountingIdGenerator());
        var host = Hosts.Over(new InMemoryLocalCache(), ids: ids);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var beforeRunCommand = ids.Guids;
        var opened = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        opened.Accepted.ShouldBeTrue("starting a run was refused " + opened.Rejection + ".");

        var drawnForTheRunCommand = ids.Guids - beforeRunCommand;

        var beforeMetaCommand = ids.Guids;
        var session = await host.SubmitAsync(player, null, BeginSession, Worlds.Cancel);

        session.Accepted.ShouldBeTrue("beginning a session was refused " + session.Rejection + ".");

        // The control: without it, "no entropy for a run command" is also true of a host that never
        // draws any, which would leave every meta command unable to draw at all.
        (ids.Guids - beforeMetaCommand).ShouldBeGreaterThan(
            0,
            "a meta command took no fresh entropy either, so the host issues no per-command seed at " +
            "all and the claim below is about a host that simply never draws.");

        drawnForTheRunCommand.ShouldBe(
            0,
            "START_RUN took fresh entropy. A run command draws off the run's committed seed and its " +
            "persisted counters, so a seed issued to one is a second source with no rule about which " +
            "wins — and START_RUN is exactly the row a host classifying by endpoint gets wrong.");
    }

    // ═══════════════════════════════════════════════════════════ the entitlement

    /// <summary>
    /// The entitlement a composition root states is the one the domain reads. Nothing else in this
    /// suite passes one of its own, so without this a host that ignored the argument and resolved its
    /// own absence value would satisfy every case here and every case about the constructor.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_applies_the_command_under_the_entitlement_the_host_was_composed_with()
    {
        var freeAllowance = Worlds.Content.ReadInt32(AuthoredFreePresetAllowance);
        var beyondIt = new SavePresetCommand(freeAllowance + 1, "the slot a subscription is the answer to");

        var unresolved = Hosts.Over(new InMemoryLocalCache());
        var free = await unresolved.OpenProfileAsync(Worlds.Cancel);

        // The control: the very same command inside the allowance is accepted for the very same
        // player, so the refusal below is the entitlement rule and not the slot, the name or the row.
        var inside = await unresolved.SubmitAsync(
            free, null, new SavePresetCommand(freeAllowance, "a slot the allowance covers"), Worlds.Cancel);

        inside.Accepted.ShouldBeTrue(
            "a slot inside the free allowance was refused " + inside.Rejection + ", so nothing below " +
            "distinguishes the entitlement from whatever refused this.");

        var refused = await unresolved.SubmitAsync(free, null, beyondIt, Worlds.Cancel);

        refused.Rejection.ShouldBe(
            RejectionReason.NOT_ENTITLED,
            "the host composed with the absence factory let a free player write past the authored " +
            "allowance, so the entitlement the composition root stated is not the one that reached " +
            "the domain.");

        var subscribed = Hosts.Over(
            new InMemoryLocalCache(), entitlements: new Entitlements(hasPlus: true, expiresAtUtc: null));
        var subscriber = await subscribed.OpenProfileAsync(Worlds.Cancel);

        var accepted = await subscribed.SubmitAsync(subscriber, null, beyondIt, Worlds.Cancel);

        accepted.Accepted.ShouldBeTrue(
            "a host composed with Plus refused the slot Plus buys " + accepted.Rejection + ". Read " +
            "with the refusal above: one argument changed and the domain answered differently, which " +
            "is the only thing that says the argument is read at all.");
    }

    /// <summary>How many preset slots a player without a subscription may write, read where the game reads it.</summary>
    private const string AuthoredFreePresetAllowance = "tuning/ads.json#/plus/freePresets";

    // ══════════════════════════════════════════════════════════════════ the clock

    [Fact]
    public async Task SubmitAsync_applies_the_command_at_the_instant_the_clock_reads()
    {
        var clock = new AdjustableClock();
        clock.Set(Worlds.Start);

        var host = Hosts.Over(new InMemoryLocalCache(), clock: clock);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var created = (await host.ReadOwnStateAsync(player, null, Worlds.Cancel)).View!.Player;

        clock.Advance(TimeSpan.FromHours(7));

        var applied = clock.UtcNow;

        applied.ShouldNotBe(
            created.LastAppliedAtUtc,
            "the clock has to have moved since the profile was written, or 'the stamp is the clock's " +
            "instant' would also hold for a host that never read the clock again.");

        var outcome = await host.SubmitAsync(player, null, BeginSession, Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue("beginning a session was refused " + outcome.Rejection + ".");

        var stamped = (await host.ReadOwnStateAsync(player, null, Worlds.Cancel)).View!.Player;

        stamped.LastAppliedAtUtc.ShouldBe(
            applied,
            "the instant the domain applied the command at is not the one the clock read. Anything " +
            "the host does to that reading — rounding it, re-reading it, normalising it — makes the " +
            "state a function of something other than its inputs.");

        stamped.LastAppliedAtUtc.Offset.ShouldBe(
            TimeSpan.Zero,
            "the reading is passed through unchanged, and the port already promises a zero offset. A " +
            "converted instant would hash differently from the same moment written by another host.");
    }

    // ═══════════════════════════════════════════════════════════ commit and re-read

    [Fact]
    public async Task SubmitAsync_commits_the_state_a_fresh_host_over_the_same_cache_reads_back()
    {
        var store = new InMemoryLocalCache();
        var host = Hosts.Over(store);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var opened = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        opened.Accepted.ShouldBeTrue("starting a run was refused " + opened.Rejection + ".");

        var read = await Hosts.Over(store.Reopen()).ReadOwnStateAsync(player, null, Worlds.Cancel);

        read.Lookup.ShouldBe(OwnStateLookup.Found, "the profile that just acted was not found.");
        Worlds.Hash(new StoredSlice(read.View!.Player, read.View.Run)).ShouldBe(
            Worlds.Hash(opened.State),
            "the next launch reads something other than the state the command produced, so the " +
            "command was applied in memory and committed as something else — or not at all.");
    }

    [Fact]
    public async Task ReadOwnStateAsync_answers_with_a_finished_run_after_the_next_run_has_started()
    {
        var host = Hosts.Over(new InMemoryLocalCache());
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var opened = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        opened.Accepted.ShouldBeTrue("starting the first run was refused " + opened.Rejection + ".");

        var first = opened.State.Run!.Id;

        var ended = await host.SubmitAsync(player, first, new AbandonRunCommand(), Worlds.Cancel);

        ended.Accepted.ShouldBeTrue("abandoning the run was refused " + ended.Rejection + ".");

        var next = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        next.Accepted.ShouldBeTrue("starting the next run was refused " + next.Rejection + ".");
        next.State.Run!.Id.ShouldNotBe(first, "the second run carries the first one's identity.");

        var read = await host.ReadOwnStateAsync(player, first, Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.Found,
            "the finished run is gone. Its results screen is opened after the next run has already " +
            "started, so the archive is what that read answers from.");
        read.View!.Run!.Id.ShouldBe(first, "some other run was handed back for the id asked about.");
        read.View.Run.Phase.ShouldBe(RunPhase.Ended, "the run asked about is over.");
    }

    // ══════════════════════════════════════════════════════════════════ the sinks

    [Fact]
    public async Task SubmitAsync_accepts_a_command_when_the_host_is_wired_with_no_sinks_at_all()
    {
        var host = Hosts.Over(new InMemoryLocalCache(), sinks: []);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var outcome = await host.SubmitAsync(player, null, BeginSession, Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue(
            "a host with nobody listening refused the command " + outcome.Rejection + ". Sinks are a " +
            "side channel; an empty list is the ordinary shape, not a misconfiguration.");
        outcome.Events.ShouldNotBeEmpty(
            "the command produced no events at all, so 'nothing failed to be delivered' below would " +
            "hold over an empty batch.");
        outcome.DispatchFailures.ShouldBeEmpty("nothing was registered, so nothing can have failed.");
    }

    [Fact]
    public async Task SubmitAsync_keeps_the_commit_and_names_the_sink_that_threw()
    {
        var store = new InMemoryLocalCache();
        var survivor = new RecordingSink();
        var host = Hosts.Over(store, sinks: [new ThrowingSink(), survivor]);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var outcome = await host.SubmitAsync(player, null, BeginSession, Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue(
            "a side channel being down turned a command that provably happened into a refusal, so the " +
            "player is told no about a state change their own row already records.");

        outcome.DispatchFailures.Count.ShouldBe(
            1, "exactly one sink failed, and a failure swallowed silently is a channel nobody knows is down.");
        outcome.DispatchFailures[0].Error.ShouldContain(
            ThrowingSink.Message, Case.Sensitive, "what the sink actually threw");

        survivor.Batches.Count.ShouldBe(
            1, "the sink registered after the failing one was skipped, so one broken transport silences the rest.");

        var read = await Hosts.Over(store.Reopen()).ReadOwnStateAsync(player, null, Worlds.Cancel);

        Worlds.Hash(new StoredSlice(read.View!.Player, read.View.Run)).ShouldBe(
            Worlds.Hash(outcome.State), "the commit was undone by a failing sink.");
    }

    // ══════════════════════════════════════════════════════════ what the rows carry

    /// <summary>
    /// A floor, never an equality: the number moves upward with every versioned migration, and a case
    /// asserting today's value would have to be edited by the very change it is meant to notice.
    /// </summary>
    [Fact]
    public async Task The_rows_the_host_commits_carry_at_least_the_schema_version_this_shape_was_frozen_at()
    {
        var host = Hosts.Over(new InMemoryLocalCache());
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var opened = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        opened.Accepted.ShouldBeTrue("starting a run was refused " + opened.Rejection + ".");

        var first = opened.State.Run!.Id;
        var ended = await host.SubmitAsync(player, first, new AbandonRunCommand(), Worlds.Cancel);

        ended.Accepted.ShouldBeTrue("abandoning the run was refused " + ended.Rejection + ".");

        var read = await host.ReadOwnStateAsync(player, first, Worlds.Cancel);

        read.Lookup.ShouldBe(OwnStateLookup.Found, "the run just archived was not found.");

        read.View!.Player.SchemaVersion.ShouldBeGreaterThanOrEqualTo(
            SchemaVersionFloor,
            "a committed player row carries the serialisation version it was written under. A zero " +
            "there is a row no rehydration will accept back.");

        read.View.Run!.SchemaVersion.ShouldBeGreaterThanOrEqualTo(
            SchemaVersionFloor, "…and so does the archived run row, which is written by a second path.");
    }

    /// <summary>
    /// The snapshot schema version at the time this suite was written. A floor: it is asserted as
    /// "at least", so a later versioned migration raises it without this case having to be touched.
    /// </summary>
    private const int SchemaVersionFloor = 14;
}
