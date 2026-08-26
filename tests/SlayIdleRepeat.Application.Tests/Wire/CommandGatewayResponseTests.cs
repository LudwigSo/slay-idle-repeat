using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The response envelope's contents: the projection in <c>profile</c>, the wire <c>stateHash</c>
/// under both scopes, the outcome's run-side fields, and the <c>battleSeed</c>.
/// </summary>
public sealed class CommandGatewayResponseTests
{
    [Fact]
    public async Task An_accepted_run_scoped_command_hashes_player_then_run_over_the_wire_projection()
    {
        var (world, _) = await GatewayWorld.InAStartedRunAsync();
        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            Envelopes.Body("LOCK_ITEM", 2, "c-meta", "{\"itemId\": \"g-none\", \"locked\": true}"),
            Worlds.Cancel);

        // The fixture player owns no such item, so this is a domain rejection — which still hashes
        // state. Meta scope: the player ALONE, even while a run is open.
        var body = Replies.Rejection(reply, "NOT_OWNED");
        var rows = await world.RowsAsync();

        body.GetProperty("stateHash").GetString().ShouldBe(
            WireProjections.HashPlayerAlone(rows.Player),
            "a meta command hashes PlayerSnapshot alone (14 §16.6), through the client-visible " +
            "projection — a run hash here would make every meta exchange unverifiable for a mirror " +
            "that followed the spec");
    }

    [Fact]
    public async Task The_run_scoped_hash_is_player_then_run_and_the_seed_is_not_in_it()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        var rows = await world.RowsAsync();

        var expected = WireProjections.HashPlayerAndRun(rows.Player, rows.Run!);
        body.GetProperty("stateHash").GetString().ShouldBe(expected);

        // The discriminating half: flip ONLY the seed on the stored run and the wire hash must not
        // move — runSeed is excluded from the projection (`02` §2 wins), so a client that never
        // sees it can still reproduce every hash.
        var reseeded = rows.Run! with { RunSeed = rows.Run!.RunSeed ^ 0xDEAD_BEEFUL };
        WireProjections.HashPlayerAndRun(rows.Player, reseeded).ShouldBe(
            expected,
            "the wire hash covers the client-visible projection; a hash that moved with the seed " +
            "would leak it one bit at a time");

        // Negative control: a field the client DOES see moves the hash.
        var richer = rows.Run! with { Gold = rows.Run!.Gold + 1 };
        WireProjections.HashPlayerAndRun(rows.Player, richer).ShouldNotBe(expected);
    }

    [Fact]
    public async Task An_accepted_response_carries_the_full_profile_projection_and_omits_the_rejection_half()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        var rows = await world.RowsAsync();

        body.GetProperty("profile").GetProperty("id").GetString()
            .ShouldBe(world.Player.Value, "profile is the full client-visible PlayerSnapshot projection");
        body.GetProperty("profile").TryGetProperty("battleHashMismatches", out _).ShouldBeFalse(
            "the anti-cheat tally is never player-facing (14 §9) — it may not ride the wire in any spelling");

        body.GetProperty("outcome").GetProperty("run").GetProperty("id").GetString()
            .ShouldBe(run.Value);
        body.GetProperty("outcome").GetProperty("run").TryGetProperty("runSeed", out _).ShouldBeFalse(
            "the run seed never leaves the server (`02` §2)");
        body.GetProperty("outcome").TryGetProperty("rngStreamStates", out var states).ShouldBeTrue(
            "14 §2.3 names the literal draw counters on the outcome");
        states.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);

        body.TryGetProperty("rejected", out _).ShouldBeFalse();
        body.TryGetProperty("reason", out _).ShouldBeFalse();
        body.TryGetProperty("detail", out _).ShouldBeFalse(
            "detail is null on every rejection this build produces and absent from acceptances");
    }

    [Fact]
    public async Task A_meta_acceptance_carries_no_run_side_outcome_fields()
    {
        var (world, _) = await GatewayWorld.InAStartedRunAsync();

        // BEGIN_SESSION draws, so this also proves the gateway issued it a CommandSeed.
        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            Envelopes.Body("BEGIN_SESSION", 2, "c-day", "{\"clientVersion\": \"1.0\", \"contentHash\": \"h\"}"),
            Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        body.TryGetProperty("rejected", out _).ShouldBeFalse("BEGIN_SESSION on a fresh day is accepted");

        var outcome = body.GetProperty("outcome");
        outcome.TryGetProperty("runId", out _).ShouldBeFalse("a meta command cannot write the run, so echoing it would be a second copy of unchanged state");
        outcome.TryGetProperty("run", out _).ShouldBeFalse();
        outcome.TryGetProperty("rngStreamStates", out _).ShouldBeFalse();

        outcome.GetProperty("events").ValueKind.ShouldBe(
            System.Text.Json.JsonValueKind.Array, "the events are the animation script the client replays");

        var rows = await world.RowsAsync();
        body.GetProperty("stateHash").GetString().ShouldBe(WireProjections.HashPlayerAlone(rows.Player));
    }

    [Fact]
    public async Task Events_ride_the_outcome_with_a_type_discriminator()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();
        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);

        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 2, "c-2"), Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        var events = body.GetProperty("outcome").GetProperty("events").EnumerateArray().ToArray();

        events.ShouldNotBeEmpty("a roll produces at least the DiceRolled event");
        events.Count(e => e.TryGetProperty("type", out _)).ShouldBe(events.Length);
        events.Select(e => e.GetProperty("type").GetString()).ShouldContain(
            "DiceRolled", customMessage: "the discriminator is the event record's own name, so the client's replay can dispatch on it");
    }

    [Fact]
    public async Task START_BATTLE_answers_with_the_battle_seed_in_its_wire_spelling()
    {
        var (world, run) = await GatewayWorld.InAStartedRunAsync();

        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("PICK_PERK", 1, "c-1", "{\"optionIndex\": 0}"), Worlds.Cancel);
        await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 2, "c-2"), Worlds.Cancel);

        // Parked onto an Enemy tile directly — the board's tile weights are not this case's
        // subject — then the battle is opened through the real pipeline.
        await world.ParkRunOnAFightTileAsync();

        var reply = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("START_BATTLE", 3, "c-3"), Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        body.TryGetProperty("rejected", out _).ShouldBeFalse("START_BATTLE on an unresolved Enemy tile is accepted");

        var rows = await world.RowsAsync();
        RunBattle.HasOpenBattle(rows.Run!).ShouldBeTrue("the run must actually be standing in the fight");

        body.GetProperty("outcome").GetProperty("battleSeed").GetString().ShouldBe(
            "0x" + RunBattle.SeedOf(rows.Run!).ToString("x16", CultureInfo.InvariantCulture),
            "the seed the client simulates from, in 14 §2.3's 0x spelling, from the one sanctioned derivation");

        // And it rides only while the battle is open: the acceptance BEFORE the battle carried none.
        var earlier = await world.Gateway.SubmitRunCommandAsync(
            world.Player, run, Envelopes.Body("ROLL_DICE", 2, "c-2"), Worlds.Cancel);
        Replies.Parse(earlier, expectedStatus: 200)
            .GetProperty("outcome").TryGetProperty("battleSeed", out _).ShouldBeFalse(
                "(read back through the idempotent replay of sequence 2)");
    }

    [Fact]
    public async Task The_throttle_seam_answers_RATE_LIMITED_before_any_sequence_is_consumed()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        world.Throttle.Limited = true;
        var limited = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-l"), Worlds.Cancel);
        Replies.Rejection(limited, "RATE_LIMITED");

        world.Throttle.Limited = false;
        var accepted = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-ok"), Worlds.Cancel);
        Replies.Parse(accepted, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse(
            "a throttled command consumed nothing — the client backs off and retries at the same sequence");
    }

    [Theory]
    [InlineData("START_RUN", "{\"chapterId\": 1, \"tier\": \"NORMAL\"}", "disabledChapter")]
    [InlineData("CLAIM_AD_REWARD", "{\"placementId\": \"ad_killed\"}", "disabledPlacement")]
    [InlineData("START_DUEL", "{\"ghostId\": \"g1\"}", "pvpKilled")]
    [InlineData("CLAIM_INBOX", "{}", "mailKilled")]
    public async Task Each_kill_switch_answers_FEATURE_DISABLED_before_dispatch(
        string type, string payload, string killSwitch)
    {
        var flags = killSwitch switch
        {
            "disabledChapter" => new FeatureFlags(true, true, true, [], ["1"]),
            "disabledPlacement" => new FeatureFlags(true, true, true, ["ad_killed"], []),
            "mailKilled" => new FeatureFlags(pvpEnabled: true, plusOfferEnabled: true, mailEnabled: false, [], []),
            _ => new FeatureFlags(pvpEnabled: false, plusOfferEnabled: true, mailEnabled: true, [], []),
        };

        var world = await GatewayWorld.WithAStartingPlayerAsync(flags);

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body(type, 1, "c-k", payload), Worlds.Cancel);

        // START_DUEL is a DEFERRED registry row — the gate firing on it proves the kill switch is
        // asked before dispatch, since dispatch would answer ILLEGAL_STATE instead.
        Replies.Rejection(reply, "FEATURE_DISABLED");
    }

    [Fact]
    public async Task An_unthrown_kill_switch_lets_the_same_commands_through_to_dispatch()
    {
        // The negative control for the gate: same commands, no switch thrown — none may answer
        // FEATURE_DISABLED. (START_DUEL is deferred, so its dispatch answer is ILLEGAL_STATE.)
        var world = await GatewayWorld.WithAStartingPlayerAsync();

        var duel = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("START_DUEL", 1, "c-d", "{\"ghostId\": \"g1\"}"), Worlds.Cancel);
        Replies.Rejection(duel, "ILLEGAL_STATE");

        // CLAIM_INBOX's registry row is Deferred to M5-08 — same construction as START_DUEL above.
        var inbox = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("CLAIM_INBOX", 2, "c-i", "{}"), Worlds.Cancel);
        Replies.Rejection(inbox, "ILLEGAL_STATE");

        var start = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 3, commandId: "c-s"), Worlds.Cancel);
        Replies.Parse(start, expectedStatus: 200).TryGetProperty("rejected", out _).ShouldBeFalse();
    }

    /// <summary>The gate reads the live flags source per command, so a reload needs no gateway rebuild.</summary>
    [Fact]
    public async Task A_reloaded_kill_switch_applies_to_the_next_command_without_a_gateway_rebuild()
    {
        var flags = LocalHostAmbience.NoRemoteConfigResolved();
        var world = await GatewayWorld.WithAStartingPlayerAsync(currentFlags: () => flags);

        var before = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("START_DUEL", 1, "c-1", "{\"ghostId\": \"g1\"}"), Worlds.Cancel);
        Replies.Rejection(before, "ILLEGAL_STATE");

        flags = new FeatureFlags(pvpEnabled: false, plusOfferEnabled: true, mailEnabled: true, [], []);

        var after = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("START_DUEL", 2, "c-2", "{\"ghostId\": \"g1\"}"), Worlds.Cancel);
        Replies.Rejection(after, "FEATURE_DISABLED");
    }

    /// <summary>
    /// One snapshot per submitted command — as far as the public surface can observe, the gate and
    /// the <c>GameContext</c> read the same per-command instance (kickoff ruling 5).
    /// </summary>
    [Fact]
    public async Task Each_submitted_command_reads_the_flags_source_exactly_once()
    {
        var reads = 0;
        var world = await GatewayWorld.WithAStartingPlayerAsync(
            currentFlags: () =>
            {
                reads++;
                return LocalHostAmbience.NoRemoteConfigResolved();
            });

        var baseline = reads;

        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-1"), Worlds.Cancel);
        (reads - baseline).ShouldBe(1,
            "two reads per command could hand the gate and the GameContext different snapshots, " +
            "and zero means the gateway kept a stale construction-time copy");

        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            Envelopes.Body("BEGIN_SESSION", 2, "c-2", "{\"clientVersion\": \"1.0\", \"contentHash\": \"h\"}"),
            Worlds.Cancel);
        (reads - baseline).ShouldBe(2);
    }
}
