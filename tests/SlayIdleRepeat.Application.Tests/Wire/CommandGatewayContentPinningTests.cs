using Shouldly;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The content-version arm of the gateway: a client on content the server no longer serves is
/// turned back before the domain is called, and a run is played out against the content it opened
/// on rather than whatever is current when its next command arrives.
/// </summary>
public sealed class CommandGatewayContentPinningTests
{
    /// <summary>Stamps that are not the one this server is serving, each for a different reason.</summary>
    public static TheoryData<string> StampsTheServerDoesNotServe() =>
        new()
        {
            new string('a', ContentVersion.HexLength),
            "deadbeef",
            "sha256:" + Worlds.Content.Version.Value,
            Worlds.Content.Version.Value.ToUpperInvariant(),
            string.Empty,
        };

    [Theory]
    [MemberData(nameof(StampsTheServerDoesNotServe))]
    public async Task A_session_begin_naming_another_stamp_is_refused_for_content_and_not_for_its_envelope(
        string claimed)
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, SessionEnvelopes.BeginSession(1, "c-begin", claimed), Worlds.Cancel);

        // A truncated, prefixed, upper-cased or empty stamp is a client that needs to re-fetch
        // content, exactly like an outdated one — MALFORMED_COMMAND would send it hunting for an
        // envelope bug instead.
        Replies.Rejection(reply, "CONTENT_VERSION_MISMATCH");
    }

    [Fact]
    public async Task A_session_begin_refused_for_content_consumes_no_sequence()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var refused = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            SessionEnvelopes.BeginSession(1, "c-begin", new string('b', ContentVersion.HexLength)),
            Worlds.Cancel);
        Replies.Rejection(refused, "CONTENT_VERSION_MISMATCH");

        var next = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        Replies.Parse(next, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "the content check is stateless and pre-dispatch: sequence 1 is still the expected one, " +
            "so a client that updates its content resends rather than resyncing its counter");
    }

    [Fact]
    public async Task A_session_begin_refused_for_content_never_pays_the_days_grants()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();
        var before = (await world.RowsAsync()).Player;

        var refused = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            SessionEnvelopes.BeginSession(1, "c-begin", new string('c', ContentVersion.HexLength)),
            Worlds.Cancel);
        Replies.Rejection(refused, "CONTENT_VERSION_MISMATCH");

        var rows = await world.RowsAsync();

        // The handler writes this counter last, after the calendar advance and the Energy refill, so
        // an unset counter is the whole day's grants not having happened.
        rows.Player.DailyCounters.ContainsKey("begin_session").ShouldBeFalse(
            "a transport-tier refusal is decided before the domain is called, so the day the player " +
            "never got to begin is still unpaid and still theirs to claim after they update");

        // Compared against what the row held BEFORE, not against a written-down number: the calendar
        // is counted from 1, so a constant here would agree with a handler that had run and left the
        // player on their opening day anyway.
        rows.Player.LoginCalendarDay.ShouldBe(
            before.LoginCalendarDay,
            "the calendar advance rides the same first-call-of-the-day path the counter marks, so a " +
            "refused begin must leave the player on exactly the day they were already on");
        rows.Player.Energy.ShouldBe(
            before.Energy, "the daily free refill is granted on that same path and must not have run");
    }

    [Fact]
    public async Task A_command_carrying_no_content_hash_is_never_refused_for_content()
    {
        var pins = PinnedContent.AlsoServing(RetunedContent.Snapshot);
        var (world, run) = await GatewayWorld.InAStartedRunAsync(pins);

        // The player's session stands on a stamp this client never named. Nothing below carries a
        // content hash, so nothing below has anything to disagree with it.
        await pins.Store.WriteSessionPinAsync(
            world.Player, RetunedContent.Snapshot.Version, world.Clock.UtcNow, Worlds.Cancel);

        var meta = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            Envelopes.Body("SET_AUTO_SALVAGE_RULES", 2, "c-filter", "{\"rules\": []}"),
            Worlds.Cancel);
        Replies.Parse(meta, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "only the session begin states which content the client loaded; refusing a meta command " +
            "for content would refuse it for a claim it never made");

        var runCommand = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("SKIP_DRAFT", 1, "c-skip"), Worlds.Cancel);
        Replies.Parse(runCommand, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "a run command carries no hash either — the run's own pin decides which numbers it reads, " +
            "never whether it is allowed to run");
    }

    [Fact]
    public async Task A_session_begin_at_the_served_stamp_is_accepted_and_pins_the_session()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            SessionEnvelopes.BeginSession(1, "c-begin", Worlds.Content.Version.Value),
            Worlds.Cancel);

        Replies.Parse(reply, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse();

        var pinned = await world.Pins.Store.ReadSessionPinAsync(world.Player, Worlds.Cancel);
        pinned.ShouldBe(
            Worlds.Content.Version,
            "the accepted begin is what fixes the version this session is judged against, so a " +
            "content swap mid-session cannot silently move the stamp the next begin is compared to");
    }

    [Fact]
    public async Task A_second_session_begin_at_the_stamp_the_first_pinned_still_matches()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();
        var stamp = Worlds.Content.Version.Value;

        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, SessionEnvelopes.BeginSession(1, "c-first", stamp), Worlds.Cancel);

        var again = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, SessionEnvelopes.BeginSession(2, "c-second", stamp), Worlds.Cancel);

        Replies.Parse(again, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "the expected version is the session's own pin when it has one: a client that matched " +
            "once must not be turned back by its own pin the next time it says hello");
    }

    [Fact]
    public async Task A_fresh_command_on_a_pinned_run_reads_the_pinned_numbers_and_not_the_current_ones()
    {
        var pins = PinnedContent.AlsoServing(RetunedContent.Snapshot);
        var (world, run) = await GatewayWorld.InAStartedRunAsync(pins);

        await pins.Store.WriteRunPinAsync(
            run, RetunedContent.Snapshot.Version, world.Clock.UtcNow, Worlds.Cancel);

        var before = (await world.RowsAsync()).Run!.Gold;
        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("SKIP_DRAFT", 1, "c-skip"), Worlds.Cancel);
        Replies.Parse(reply, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse();

        var paid = (await world.RowsAsync()).Run!.Gold - before;

        paid.ShouldBe(
            RetunedContent.RetunedSkipReward,
            $"the run is pinned to the retuned set, whose draft skip pays {RetunedContent.RetunedSkipReward}; " +
            $"{RetunedContent.ShippedSkipReward} would mean the command read the CURRENT snapshot and " +
            "a balance patch had silently changed a run already in flight");
    }

    [Fact]
    public async Task A_run_pinned_to_a_version_that_no_longer_resolves_falls_back_to_current_out_loud()
    {
        var pins = PinnedContent.OverTheShippedSnapshot();
        var (world, run) = await GatewayWorld.InAStartedRunAsync(pins);

        var swept = RetunedContent.Snapshot.Version;
        await pins.Store.WriteRunPinAsync(run, swept, world.Clock.UtcNow, Worlds.Cancel);

        var before = (await world.RowsAsync()).Run!.Gold;
        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("SKIP_DRAFT", 1, "c-skip"), Worlds.Cancel);
        Replies.Parse(reply, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "a swept bundle is the server's bookkeeping problem, never a refusal the player pays for");

        ((await world.RowsAsync()).Run!.Gold - before).ShouldBe(
            RetunedContent.ShippedSkipReward,
            "with nothing to resolve the pin to, the only content there is to play against is current");

        string.Join(" | ", pins.Warnings).ShouldContain(
            swept.Short,
            customMessage: "a pin that no longer resolves means a bundle was swept while a live run " +
            "still wanted it — that run is now on different numbers than it opened with, which has to " +
            "be greppable rather than a fallback nobody can see happening");
    }

    [Fact]
    public async Task A_replayed_command_answers_from_the_record_and_consults_no_pin()
    {
        var pins = PinnedContent.AlsoServing(RetunedContent.Snapshot);
        var (world, run) = await GatewayWorld.InAStartedRunAsync(pins);

        await pins.Store.WriteRunPinAsync(
            run, RetunedContent.Snapshot.Version, world.Clock.UtcNow, Worlds.Cancel);

        var skip = Envelopes.Body("SKIP_DRAFT", 1, "c-skip");
        var first = await world.Gateway.SubmitRunCommandAsync(world.Player, run, skip, Worlds.Cancel);
        Replies.Parse(first, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse();

        var readsAfterTheFresh = pins.Store.RunPinReads;
        var resolvesAfterTheFresh = pins.ResolverCalls;
        readsAfterTheFresh.ShouldBeGreaterThan(
            0, "the FRESH command has to consult the pin, or the frozen counters below prove nothing");

        var replay = await world.Gateway.SubmitRunCommandAsync(world.Player, run, skip, Worlds.Cancel);

        replay.Body.ShouldBe(first.Body);
        pins.Store.RunPinReads.ShouldBe(
            readsAfterTheFresh,
            "a duplicate replays the stored OUTCOME instead of re-executing, so there is no command " +
            "left for a snapshot to be chosen for");
        pins.ResolverCalls.ShouldBe(
            resolvesAfterTheFresh,
            "resolving a pinned snapshot for a command that will never be applied would make the " +
            "replay depend on a bundle still being retained");
    }

    [Fact]
    public async Task An_accepted_START_RUN_pins_its_minted_run_to_the_version_it_opened_against()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var started = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);
        var run = new RunId(
            Replies.Parse(started, expectedStatus: 200).GetProperty("outcome").GetProperty("runId").GetString()!);

        var pinned = await world.Pins.Store.ReadRunPinAsync(run, Worlds.Cancel);

        pinned.ShouldBe(
            Worlds.Content.Version,
            "the pin lands where the run scope is opened: a run whose scope exists but whose pin " +
            "does not would be played half against one content set and half against another");
    }

    [Fact]
    public async Task A_refused_START_RUN_pins_nothing()
    {
        var (world, _) = await GatewayWorld.InAStartedRunAsync();

        var afterTheAcceptedStart = world.Pins.Store.RunPinWrites.Count;
        afterTheAcceptedStart.ShouldBe(
            1, "the accepted START_RUN pins exactly one run — without that this case compares zero with zero");

        var refused = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 2, commandId: "c-again"), Worlds.Cancel);
        Replies.Rejection(refused, "ILLEGAL_STATE");

        world.Pins.Store.RunPinWrites.Count.ShouldBe(
            afterTheAcceptedStart,
            "the gateway mints a run id for every command that opens a run, refused ones included; " +
            "pinning that id would leave a retention reference to a run that never existed");
    }
}
