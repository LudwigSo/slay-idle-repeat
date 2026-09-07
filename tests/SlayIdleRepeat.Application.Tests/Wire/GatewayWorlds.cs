using System.Globalization;
using System.Text.Json;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Inbox;
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
        RecordingUnitOfWork unitOfWork,
        ManualThrottle throttle,
        AdjustableClock clock,
        PlayerId player,
        FeatureFlags flags,
        PinnedContent pins,
        InMemoryMessageRepository messages,
        IIdGeneratorPort ids)
    {
        Gateway = gateway;
        Store = store;
        Cache = cache;
        Ledger = ledger;
        UnitOfWork = unitOfWork;
        Throttle = throttle;
        Clock = clock;
        Player = player;
        Flags = flags;
        Pins = pins;
        Messages = messages;
        Ids = ids;
    }

    internal CommandGateway Gateway { get; }

    internal WorldSliceStore Store { get; }

    /// <summary>The bytes under <see cref="Store"/>, so a case can put a second store over the same rows.</summary>
    internal InMemoryLocalCache Cache { get; }

    internal VolatileCommandLedger Ledger { get; }

    internal RecordingUnitOfWork UnitOfWork { get; }

    internal ManualThrottle Throttle { get; }

    internal AdjustableClock Clock { get; }

    internal PlayerId Player { get; }

    internal FeatureFlags Flags { get; }

    /// <summary>The content pins this world's gateway judges against, and the store behind them.</summary>
    internal PinnedContent Pins { get; }

    /// <summary>The inbox the gateway's claim path reads and stamps — empty until a case fills it.</summary>
    internal InMemoryMessageRepository Messages { get; }

    /// <summary>
    /// The generator the gateway mints run ids and meta command seeds from — exposed so a case that
    /// needs a second host drawing the SAME entropy (the client/server parity corpus) can align the
    /// two sides rather than guessing at how many draws this one has made.
    /// </summary>
    internal IIdGeneratorPort Ids { get; }

    /// <summary>A world holding one starting player and nothing else.</summary>
    /// <param name="flags">The kill switches, defaulting to none thrown.</param>
    /// <param name="ledger">The ledger seam, defaulting to the placeholder — a case about the seam's failure shapes passes a decorated one.</param>
    /// <param name="currentFlags">The live flags source — a reload case swaps what it answers between commands; defaults to a constant read of <paramref name="flags"/>.</param>
    /// <param name="pins">The content pins, defaulting to an empty store over the shipped snapshot alone.</param>
    /// <param name="sinks">The post-commit fan-out, defaulting to none — a case about delivery failure passes one that throws.</param>
    /// <param name="order">A shared log the unit of work and the sinks write their step into, for the cases about what happens before what.</param>
    /// <param name="ids">The id generator, defaulting to a fresh counting one. The parity corpus passes a generator a second host can reproduce.</param>
    internal static async Task<GatewayWorld> WithAStartingPlayerAsync(
        FeatureFlags? flags = null,
        ICommandLedgerStore? ledger = null,
        Func<FeatureFlags>? currentFlags = null,
        PinnedContent? pins = null,
        IReadOnlyList<IDomainEventSink>? sinks = null,
        List<string>? order = null,
        IIdGeneratorPort? ids = null)
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
        var resolvedPins = pins ?? PinnedContent.OverTheShippedSnapshot();
        var resolvedIds = ids ?? new CountingIdGenerator();

        var player = new PlayerId("PLAYER_wire");
        var starting = PlayerAggregate.CreateStartingNamedAfterItsOwnId(player, clock.UtcNow, Worlds.Content);
        if (starting.IsFailure)
        {
            throw new InvalidOperationException("The starting player does not rehydrate: " + starting.Error);
        }

        // Funded: START_RUN charges a run's price and a starting row holds nothing, so without this
        // every case that opens a run through the gateway is refused for Energy. Written onto the
        // row rather than granted by BEGIN_SESSION, which would consume the sequence number the
        // cases below expect START_RUN to take.
        await store.SaveAsync(
            new WorldSlice(Worlds.HoldingARunsPrice(starting.Value), null), Worlds.Cancel);

        // The inbox seam is composed here rather than left null: without it CLAIM_INBOX faults as
        // the loading defect it is, and every case that submits one would be asserting on this
        // fixture's wiring instead of on the gateway.
        var messages = new InMemoryMessageRepository(clock);
        var unitOfWork = new RecordingUnitOfWork(store, volatileLedger, Worlds.Content, order, messages);

        var gateway = new CommandGateway(
            new ApplyCommandUseCase(
                store, new DomainEventDispatcher(sinks ?? []), new InboxCommandSupport(messages)),
            clock,
            resolvedIds,
            Worlds.Content,
            LocalHostAmbience.NoSubscriptionResolved(),
            currentFlags ?? (() => resolvedFlags),
            ledger ?? volatileLedger,
            throttle,
            resolvedPins.Pinning,
            unitOfWork);

        return new GatewayWorld(
            gateway, store, cache, volatileLedger, unitOfWork, throttle, clock, player, resolvedFlags,
            resolvedPins, messages, resolvedIds);
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

        await Store.SaveAsync(
            new WorldSlice(Worlds.HoldingARunsPrice(starting.Value), null), Worlds.Cancel);

        return player;
    }

    /// <summary>
    /// A world whose player stands in a started run — <c>START_RUN</c> accepted through the
    /// gateway itself, so the run scope is genuinely open. Player sequence 1 is consumed.
    /// </summary>
    /// <param name="pins">The content pins, defaulting to an empty store over the shipped snapshot alone.</param>
    /// <param name="ids">The id generator, defaulting to a fresh counting one.</param>
    internal static async Task<(GatewayWorld World, RunId Run)> InAStartedRunAsync(
        PinnedContent? pins = null, IIdGeneratorPort? ids = null)
    {
        var world = await WithAStartingPlayerAsync(pins: pins, ids: ids);

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
    /// <param name="reply">The gateway's answer.</param>
    /// <param name="reason">The rejection reason the caller expects, by name.</param>
    /// <param name="what">
    /// What the caller was driving, appended to a failure. A theory arm's own description belongs
    /// here rather than in an assertion of its own — a string constant from <c>InlineData</c> cannot
    /// be empty, so asserting on it is an assertion that cannot fail.
    /// </param>
    internal static JsonElement Rejection(GatewayReply reply, string reason, string? what = null)
    {
        var context = what is null ? string.Empty : " (" + what + ")";
        var body = Parse(reply, 200);

        if (!body.TryGetProperty("rejected", out var rejected) || !rejected.GetBoolean())
        {
            throw new InvalidOperationException(
                "Expected a rejection envelope" + context + ", got: " + reply.Body);
        }

        var actual = body.GetProperty("reason").GetString();
        if (!string.Equals(actual, reason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected reason {reason}{context}, got {actual}: {reply.Body}");
        }

        return body;
    }
}
