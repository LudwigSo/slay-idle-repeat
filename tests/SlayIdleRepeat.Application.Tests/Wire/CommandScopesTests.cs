using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>The scope-key spelling and its typed resolution — one decider, both directions.</summary>
public sealed class CommandScopesTests
{
    private static readonly PlayerId Player = new("PLAYER_1");
    private static readonly RunId Run = new("RUN_9");

    [Fact]
    public void A_run_scopes_key_is_the_gateways_historical_spelling()
    {
        // Pinned literally: the gateway spelled scopes this way before this class existed, and a
        // respelling would strand every stored record under a key nothing builds any more.
        CommandScopes.ForRun(Player, Run).ShouldBe("run:PLAYER_1:RUN_9");
    }

    [Fact]
    public void A_player_scopes_key_is_the_gateways_historical_spelling()
    {
        CommandScopes.ForPlayer(Player).ShouldBe("player:PLAYER_1");
    }

    [Fact]
    public void A_run_key_resolves_back_to_its_typed_scope()
    {
        var resolved = CommandScopes.Resolve(CommandScopes.ForRun(Player, Run));

        resolved.Kind.ShouldBe(IdempotencyScopeKind.Run);
        resolved.Player.ShouldBe(Player);
        resolved.Run.ShouldBe(Run);
    }

    [Fact]
    public void A_player_key_resolves_back_to_its_typed_scope()
    {
        var resolved = CommandScopes.Resolve(CommandScopes.ForPlayer(Player));

        resolved.Kind.ShouldBe(IdempotencyScopeKind.Player);
        resolved.Player.ShouldBe(Player);
        resolved.Run.ShouldBeNull();
    }

    [Theory]
    [InlineData("session:PLAYER_1")]
    [InlineData("run:PLAYER_1")]
    [InlineData("player:")]
    [InlineData("run:PLAYER_1:")]
    [InlineData("")]
    public void Text_this_class_never_produced_is_refused(string scope)
    {
        Should.Throw<ArgumentException>(() => CommandScopes.Resolve(scope),
            "a store resolving an unrecognised scope key must refuse rather than guess: a guessed "
            + "domain files the record where no reader will ever look for it.");
    }
}
