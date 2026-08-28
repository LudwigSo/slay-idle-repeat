using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Composition;
using SlayIdleRepeat.Server.Endpoints;
using Xunit;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Server.Tests.Endpoints;

/// <summary>
/// <c>GET /run/{runId}/state?sinceSequence=N</c> as the plain function it is: the auth mapping,
/// the route segment, the query-string parse, and the pass-through. No ASP.NET anywhere.
/// </summary>
public sealed class StateRequestHandlerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    /// <summary>A resolver pinned to one answer, so each HTTP mapping arm is reachable directly (S25).</summary>
    private sealed class FixedResolver(PrincipalResolution resolution) : IPrincipalResolver
    {
        public PrincipalResolution Resolve(string? authorizationHeader) => resolution;
    }

    /// <summary>One player standing in one started run, plus a second player's run nobody may read.</summary>
    private sealed record World(RunStateQuery Query, PlayerId Player, RunId Run, RunId StrangersRun);

    // One world for the whole class: loading and validating the shipped content set is the
    // expensive half, and no case here writes through it.
    private static readonly PlaceholderVolatileWorldStore Rows = new();

    private static readonly Lazy<ContentSnapshot> Content = new(
        () => ContentLoader.Load(new LocalFileContentSource(FindGameData())).Require());

    private static readonly Lazy<Task<World>> Shared = new(BuildAsync);

    [Fact]
    public async Task An_unauthorized_principal_is_401_with_an_empty_body()
    {
        var world = await Shared.Value;

        var reply = await StateRequestHandler.HandleRunStateAsync(
            new FixedResolver(PrincipalResolution.Unauthorized()),
            world.Query, authorizationHeader: null, world.Run.Value, "0", Cancel);

        reply.StatusCode.ShouldBe(401);
        reply.Body.ShouldBeEmpty(
            "the run named here really does exist, so a body would be state handed to a caller who " +
            "produced no credentials");
    }

    [Fact]
    public async Task A_locked_account_is_403_the_account_state_screen()
    {
        var world = await Shared.Value;

        var reply = await StateRequestHandler.HandleRunStateAsync(
            new FixedResolver(PrincipalResolution.Locked()),
            world.Query, "Bearer whoever", world.Run.Value, "0", Cancel);

        reply.StatusCode.ShouldBe(403);
        reply.Body.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_runId_segment_that_names_nothing_is_the_routers_404(string runId)
    {
        var world = await Shared.Value;

        var reply = await StateRequestHandler.HandleRunStateAsync(
            new FixedResolver(PrincipalResolution.Resolved(world.Player)),
            world.Query, "Bearer " + world.Player.Value, runId, "0", Cancel);

        reply.StatusCode.ShouldBe(404);
        reply.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_absent_sinceSequence_is_400()
    {
        var world = await Shared.Value;

        var reply = await StateRequestHandler.HandleRunStateAsync(
            new FixedResolver(PrincipalResolution.Resolved(world.Player)),
            world.Query, "Bearer " + world.Player.Value, world.Run.Value, sinceSequence: null, Cancel);

        reply.StatusCode.ShouldBe(
            400,
            "the parameter is required (14 §3.1): defaulting it to 0 would answer a full replay to a " +
            "client that meant to ask for one outcome, on every request that dropped the query string");
        reply.Body.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task A_sinceSequence_that_is_not_a_whole_non_negative_number_is_400(string sinceSequence)
    {
        var world = await Shared.Value;

        var reply = await StateRequestHandler.HandleRunStateAsync(
            new FixedResolver(PrincipalResolution.Resolved(world.Player)),
            world.Query, "Bearer " + world.Player.Value, world.Run.Value, sinceSequence, Cancel);

        reply.StatusCode.ShouldBe(
            400,
            "'" + sinceSequence + "' names no point in the conversation; a value coerced to 0 or " +
            "truncated to 1 would silently answer a question the client did not ask");
        reply.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_resolved_principal_reading_their_own_run_gets_the_querys_answer_unchanged()
    {
        var world = await Shared.Value;

        var reply = await StateRequestHandler.HandleRunStateAsync(
            new FixedResolver(PrincipalResolution.Resolved(world.Player)),
            world.Query, "Bearer " + world.Player.Value, world.Run.Value, "0", Cancel);

        reply.StatusCode.ShouldBe(200, reply.Body);

        var direct = await world.Query.ReadAsync(world.Player, world.Run, 0, Cancel);
        reply.ShouldBe(
            direct,
            "the handler maps a request onto the query and rides its answer out — a body assembled " +
            "here would be a second renderer of the same state");

        JsonDocument.Parse(reply.Body).RootElement.GetProperty("runId").GetString()
            .ShouldBe(world.Run.Value, "the route segment is the run that was read");
    }

    [Fact]
    public async Task A_strangers_run_and_a_run_that_never_existed_are_the_same_404()
    {
        var world = await Shared.Value;
        var resolver = new FixedResolver(PrincipalResolution.Resolved(world.Player));

        var foreign = await StateRequestHandler.HandleRunStateAsync(
            resolver, world.Query, "Bearer " + world.Player.Value, world.StrangersRun.Value, "0", Cancel);
        var absent = await StateRequestHandler.HandleRunStateAsync(
            resolver, world.Query, "Bearer " + world.Player.Value, "RUN_nobody_opened", "0", Cancel);

        foreign.StatusCode.ShouldBe(404);
        foreign.ShouldBe(
            absent,
            "status and body alike: a caller who could tell 'someone else's run' from 'no such run' " +
            "can enumerate run ids one request at a time");
    }

    private static async Task<World> BuildAsync()
    {
        var store = new WorldSliceStore(Rows);
        var ledger = new VolatileCommandLedger();
        var gateway = new CommandGateway(
            new ApplyCommandUseCase(store, new DomainEventDispatcher([])),
            new SystemClock(),
            new SystemIdGenerator(),
            Content.Value,
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved,
            ledger,
            new UnlimitedCommandThrottle());

        var player = await SeedPlayerAsync(store, "PLAYER_state_handler");
        var stranger = await SeedPlayerAsync(store, "PLAYER_state_stranger");

        return new World(
            new RunStateQuery(new ReadOwnStateUseCase(store), ledger),
            player,
            await StartRunAsync(gateway, player),
            await StartRunAsync(gateway, stranger));
    }

    private static async Task<RunId> StartRunAsync(CommandGateway gateway, PlayerId player)
    {
        var reply = await gateway.SubmitPlayerCommandAsync(
            player,
            "{\"protocolVersion\": 1, \"commandId\": \"c-start\", \"sequence\": 1, \"type\": \"START_RUN\", " +
            "\"payload\": {\"chapterId\": 1, \"tier\": \"NORMAL\"}}",
            Cancel);

        if (reply.StatusCode != 200)
        {
            throw new InvalidOperationException("START_RUN answered HTTP " + reply.StatusCode + ": " + reply.Body);
        }

        var runId = JsonDocument.Parse(reply.Body).RootElement
            .GetProperty("outcome").GetProperty("runId").GetString();

        return new RunId(
            runId ?? throw new InvalidOperationException("START_RUN answered without a runId: " + reply.Body));
    }

    private static async Task<PlayerId> SeedPlayerAsync(WorldSliceStore store, string id)
    {
        var player = new PlayerId(id);
        var starting = PlayerAggregate.CreateStartingNamedAfterItsOwnId(
            player, new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.Zero), Content.Value);

        if (starting.IsFailure)
        {
            throw new InvalidOperationException("The starting player does not rehydrate: " + starting.Error);
        }

        await store.SaveAsync(new WorldSlice(starting.Value, null), Cancel);

        return player;
    }

    /// <summary>The repo's <c>game-data</c>, found the way the test host is reached: upward from the output directory.</summary>
    private static string FindGameData()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "game-data");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No game-data directory above " + AppContext.BaseDirectory + " — this suite reads the shipped content set.");
    }
}
