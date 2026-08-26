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
/// The endpoint handlers as the plain functions they are: the auth mapping in front of the
/// gateway (401/403 and the pass-through), and the route's part of the contract. No ASP.NET
/// anywhere — that is the point of the handlers' shape.
/// </summary>
public sealed class CommandRequestHandlerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    /// <summary>A resolver pinned to one answer, so each HTTP mapping arm is reachable directly (S25).</summary>
    private sealed class FixedResolver(PrincipalResolution resolution) : IPrincipalResolver
    {
        public PrincipalResolution Resolve(string? authorizationHeader) => resolution;
    }

    // One gateway for the whole class: loading and validating the shipped content set is the
    // expensive half, and these cases never depend on each other's ledger state — each uses its
    // own player or never reaches the gateway at all.
    private static readonly PlaceholderVolatileWorldStore Store = new();

    private static readonly Lazy<CommandGateway> SharedGateway = new(BuildGateway);

    private static readonly Lazy<WorldSliceStore> SharedStore = new(() => new WorldSliceStore(Store));

    private static readonly Lazy<ContentSnapshot> Content = new(
        () => ContentLoader.Load(new LocalFileContentSource(FindGameData())).Require());

    [Fact]
    public async Task An_unauthorized_principal_is_401_with_an_empty_body_and_the_gateway_is_never_asked()
    {
        // The body is deliberate garbage: if the handler consulted the gateway anyway, this would
        // come back 400, so the 401 also proves the ordering.
        var reply = await CommandRequestHandler.HandlePlayerCommandAsync(
            new FixedResolver(PrincipalResolution.Unauthorized()),
            SharedGateway.Value, authorizationHeader: null, body: "not json", Cancel);

        reply.StatusCode.ShouldBe(401);
        reply.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_locked_account_is_403_the_account_state_screen()
    {
        var reply = await CommandRequestHandler.HandleRunCommandAsync(
            new FixedResolver(PrincipalResolution.Locked()),
            SharedGateway.Value, "Bearer whoever", "RUN_x", "not json", Cancel);

        reply.StatusCode.ShouldBe(403);
        reply.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_resolved_principal_passes_through_to_the_gateway()
    {
        var player = await SeedPlayerAsync("PLAYER_handler_pass");

        var reply = await CommandRequestHandler.HandlePlayerCommandAsync(
            new FixedResolver(PrincipalResolution.Resolved(player)),
            SharedGateway.Value,
            "Bearer " + player.Value,
            "{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"sequence\": 1, \"type\": \"START_RUN\", " +
            "\"payload\": {\"chapterId\": 1, \"tier\": \"NORMAL\"}}",
            Cancel);

        reply.StatusCode.ShouldBe(200, "an authenticated, well-formed command reaches the pipeline: " + reply.Body);
        reply.Body.ShouldContain("\"outcome\"");
    }

    [Fact]
    public async Task The_route_segment_is_the_run_the_command_is_sequenced_against()
    {
        var player = await SeedPlayerAsync("PLAYER_handler_route");

        var reply = await CommandRequestHandler.HandleRunCommandAsync(
            new FixedResolver(PrincipalResolution.Resolved(player)),
            SharedGateway.Value,
            "Bearer " + player.Value,
            "RUN_nobody_opened",
            "{\"protocolVersion\": 1, \"commandId\": \"c-1\", \"sequence\": 1, \"type\": \"ROLL_DICE\", \"payload\": {}}",
            Cancel);

        reply.StatusCode.ShouldBe(200);
        reply.Body.ShouldContain(
            "RUN_NOT_FOUND", customMessage: "the routed id reached the gateway as the scope, and no such scope exists");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_runId_segment_that_names_nothing_is_the_routers_404(string runId)
    {
        var reply = await CommandRequestHandler.HandleRunCommandAsync(
            new FixedResolver(PrincipalResolution.Resolved(new PlayerId("PLAYER_x"))),
            SharedGateway.Value, "Bearer PLAYER_x", runId, "{}", Cancel);

        reply.StatusCode.ShouldBe(404);
        reply.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_400_from_the_gateway_rides_through_unchanged()
    {
        var player = await SeedPlayerAsync("PLAYER_handler_400");

        var reply = await CommandRequestHandler.HandlePlayerCommandAsync(
            new FixedResolver(PrincipalResolution.Resolved(player)),
            SharedGateway.Value, "Bearer " + player.Value, "not an envelope", Cancel);

        reply.StatusCode.ShouldBe(400);
        reply.Body.ShouldBeEmpty();
    }

    private static CommandGateway BuildGateway() => new(
        new ApplyCommandUseCase(SharedStore.Value, new DomainEventDispatcher([])),
        new SystemClock(),
        new SystemIdGenerator(),
        Content.Value,
        LocalHostAmbience.NoSubscriptionResolved(),
        LocalHostAmbience.NoRemoteConfigResolved(),
        new VolatileCommandLedger(),
        new UnlimitedCommandThrottle());

    private static async Task<PlayerId> SeedPlayerAsync(string id)
    {
        var player = new PlayerId(id);
        var starting = PlayerAggregate.CreateStartingNamedAfterItsOwnId(
            player, new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.Zero), Content.Value);

        if (starting.IsFailure)
        {
            throw new InvalidOperationException("The starting player does not rehydrate: " + starting.Error);
        }

        await SharedStore.Value.SaveAsync(new WorldSlice(starting.Value, null), Cancel);

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
