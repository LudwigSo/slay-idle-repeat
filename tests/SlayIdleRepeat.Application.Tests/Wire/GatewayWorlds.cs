using System.Globalization;
using System.Text.Json;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>A throttle a case can throw and clear by hand — the M5-14 seam driven from both sides.</summary>
internal sealed class ManualThrottle : ICommandThrottle
{
    /// <summary>Whether the next ask is refused.</summary>
    internal bool Limited { get; set; }

    /// <inheritdoc/>
    public bool ShouldReject(PlayerId player) => Limited;
}

/// <summary>One gateway over real everything: shipped content, the real use case, an in-memory store.</summary>
/// <remarks>
/// The seams a case may reach into: the clock, the throttle, the ledger and the byte cache. The
/// gateway is always driven through its two public submits, exactly as the endpoints drive it.
/// </remarks>
internal sealed class GatewayWorld
{
    private GatewayWorld(
        CommandGateway gateway,
        WorldSliceStore store,
        InMemoryLocalCache cache,
        VolatileCommandLedger ledger,
        ManualThrottle throttle,
        AdjustableClock clock,
        PlayerId player,
        FeatureFlags flags)
    {
        Gateway = gateway;
        Store = store;
        Cache = cache;
        Ledger = ledger;
        Throttle = throttle;
        Clock = clock;
        Player = player;
        Flags = flags;
    }

    internal CommandGateway Gateway { get; }

    internal WorldSliceStore Store { get; }

    /// <summary>The bytes under <see cref="Store"/>, so a case can put a second store over the same rows.</summary>
    internal InMemoryLocalCache Cache { get; }

    internal VolatileCommandLedger Ledger { get; }

    internal ManualThrottle Throttle { get; }

    internal AdjustableClock Clock { get; }

    internal PlayerId Player { get; }

    internal FeatureFlags Flags { get; }

    /// <summary>A world holding one starting player and nothing else.</summary>
    /// <param name="flags">The kill switches, defaulting to none thrown.</param>
    /// <param name="ledger">The ledger seam, defaulting to the placeholder — a case about the seam's failure shapes passes a decorated one.</param>
    /// <param name="currentFlags">The live flags source — a reload case swaps what it answers between commands; defaults to a constant read of <paramref name="flags"/>.</param>
    internal static async Task<GatewayWorld> WithAStartingPlayerAsync(
        FeatureFlags? flags = null, ICommandLedgerStore? ledger = null, Func<FeatureFlags>? currentFlags = null)
    {
        if (flags is not null && currentFlags is not null)
        {
            throw new ArgumentException(
                "Pass flags or currentFlags, never both — a fixed value the live source contradicts tests nothing.",
                nameof(currentFlags));
        }

        var cache = new InMemoryLocalCache();
        var store = new WorldSliceStore(cache);
        var clock = new AdjustableClock();
        clock.Set(Worlds.Start);
        var volatileLedger = new VolatileCommandLedger();
        var throttle = new ManualThrottle();
        var resolvedFlags = flags ?? LocalHostAmbience.NoRemoteConfigResolved();

        var player = new PlayerId("PLAYER_wire");
        var starting = PlayerAggregate.CreateStartingNamedAfterItsOwnId(player, clock.UtcNow, Worlds.Content);
        if (starting.IsFailure)
        {
            throw new InvalidOperationException("The starting player does not rehydrate: " + starting.Error);
        }

        await store.SaveAsync(new WorldSlice(starting.Value, null), Worlds.Cancel);

        var gateway = new CommandGateway(
            new ApplyCommandUseCase(store, new DomainEventDispatcher([])),
            clock,
            new CountingIdGenerator(),
            Worlds.Content,
            LocalHostAmbience.NoSubscriptionResolved(),
            currentFlags ?? (() => resolvedFlags),
            ledger ?? volatileLedger,
            throttle);

        return new GatewayWorld(gateway, store, cache, volatileLedger, throttle, clock, player, resolvedFlags);
    }

    /// <summary>A second starting player in the same world, so cross-player claims compare two real principals.</summary>
    internal async Task<PlayerId> SeedSecondPlayerAsync(string id)
    {
        var player = new PlayerId(id);
        var starting = PlayerAggregate.CreateStartingNamedAfterItsOwnId(player, Clock.UtcNow, Worlds.Content);
        if (starting.IsFailure)
        {
            throw new InvalidOperationException("The second player does not rehydrate: " + starting.Error);
        }

        await Store.SaveAsync(new WorldSlice(starting.Value, null), Worlds.Cancel);

        return player;
    }

    /// <summary>
    /// A world whose player stands in a started run — <c>START_RUN</c> accepted through the
    /// gateway itself, so the run scope is genuinely open. Player sequence 1 is consumed.
    /// </summary>
    internal static async Task<(GatewayWorld World, RunId Run)> InAStartedRunAsync()
    {
        var world = await WithAStartingPlayerAsync();

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.StartRun(sequence: 1, commandId: "c-start"), Worlds.Cancel);

        var body = Replies.Parse(reply, expectedStatus: 200);
        var runId = body.GetProperty("outcome").GetProperty("runId").GetString()
            ?? throw new InvalidOperationException("START_RUN answered without a runId.");

        return (world, new RunId(runId));
    }

    /// <summary>The stored rows, read back the way the gateway's own dispatch reads them.</summary>
    internal async Task<(PlayerSnapshot Player, RunSnapshot? Run)> RowsAsync()
    {
        var rows = await Store.ReadSnapshotsAsync(Player, Worlds.Cancel)
            ?? throw new InvalidOperationException("No rows stored for the fixture player.");

        return (rows.Player, rows.Run);
    }

    /// <summary>Parks the player's run on an unresolved Enemy tile — the state <c>START_BATTLE</c> answers from.</summary>
    /// <remarks>The tile is written onto the row, on <c>PendingBattlePathTests</c>' argument: waiting for the board to roll one would tie this fixture to tile weights.</remarks>
    internal async Task ParkRunOnAFightTileAsync()
    {
        var (playerRow, runRow) = await RowsAsync();

        if (runRow is null)
        {
            throw new InvalidOperationException("The fixture player has no run to park.");
        }

        var run = RunAggregate.Rehydrate(runRow with
        {
            PendingTileKind = (int)Core.Rules.Board.TileKind.Enemy,
            PendingTileLinearIndex = 3,
            PendingTileStage = 1,
            Phase = RunPhase.InProgress,
            DraftPending = false,
            DraftBattleKind = -1,
            DraftBattleStage = 0,
            PendingForkJunctionPosition = null,
            PendingForkRemainingSteps = null,
            PendingEventCardId = string.Empty,
        });

        if (run.IsFailure)
        {
            throw new InvalidOperationException("The parked run does not rehydrate: " + run.Error);
        }

        var hero = PlayerAggregate.Rehydrate(playerRow, Worlds.Content);
        if (hero.IsFailure)
        {
            throw new InvalidOperationException("The fixture player does not rehydrate: " + hero.Error);
        }

        await Store.SaveAsync(new WorldSlice(hero.Value, run.Value), Worlds.Cancel);
    }
}

/// <summary>Envelope bodies as a client would send them.</summary>
internal static class Envelopes
{
    /// <summary>One envelope body. <paramref name="payload"/> is raw JSON; <c>null</c> omits the member.</summary>
    internal static string Body(
        string type, long sequence, string commandId, string? payload = "{}", int protocolVersion = 1) =>
        payload is null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"protocolVersion\": {protocolVersion}, \"commandId\": \"{commandId}\", \"sequence\": {sequence}, \"type\": \"{type}\"}}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"protocolVersion\": {protocolVersion}, \"commandId\": \"{commandId}\", \"sequence\": {sequence}, \"type\": \"{type}\", \"payload\": {payload}}}");

    /// <summary>The registry's opening command at chapter 1, NORMAL — the pair a starting player may start.</summary>
    internal static string StartRun(long sequence, string commandId, int protocolVersion = 1) =>
        Body("START_RUN", sequence, commandId, "{\"chapterId\": 1, \"tier\": \"NORMAL\"}", protocolVersion);
}

/// <summary>Reading a reply the way the client contract does.</summary>
internal static class Replies
{
    /// <summary>Asserts the status and parses the body as a JSON object.</summary>
    internal static JsonElement Parse(GatewayReply reply, int expectedStatus)
    {
        if (reply.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException(
                $"Expected HTTP {expectedStatus}, got {reply.StatusCode} with body: {reply.Body}");
        }

        return JsonDocument.Parse(reply.Body).RootElement.Clone();
    }

    /// <summary>Asserts a 200 rejection envelope and returns its parsed body.</summary>
    internal static JsonElement Rejection(GatewayReply reply, string reason)
    {
        var body = Parse(reply, 200);

        if (!body.TryGetProperty("rejected", out var rejected) || !rejected.GetBoolean())
        {
            throw new InvalidOperationException("Expected a rejection envelope, got: " + reply.Body);
        }

        var actual = body.GetProperty("reason").GetString();
        if (!string.Equals(actual, reason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected reason {reason}, got {actual}: {reply.Body}");
        }

        return body;
    }
}
