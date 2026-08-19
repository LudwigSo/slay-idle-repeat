using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>Which rows demand a seed is pinned registry-wide by <c>RequiresCommandSeedTests</c>;
/// this pins that the harness, as the host of its own simulation, actually issues one.</summary>
public sealed class InMemoryGameCommandSeedTests
{
    [Fact]
    public void Send_hands_a_meta_command_the_seed_a_null_one_would_have_failed_the_command_on()
    {
        var (game, player) = Harnesses.WithPlayer();

        // The control must run first: BEGIN_SESSION draws only on the day's first call, so once the
        // harness has sent one this slice would no longer demand a seed at all.
        Should.Throw<InvalidOperationException>(
                () => GameRules.Apply(
                    game.State(player),
                    Harnesses.BeginSession,
                    new GameContext(
                        game.Clock.NowUtc,
                        CommandSeed: null,
                        game.Content,
                        game.Entitlements,
                        game.Flags)))
            .Message.ShouldContain(
                "carries no CommandSeed",
                Case.Sensitive,
                "the control has to fail for the seed's absence specifically — any other defect " +
                "would leave the assertion below pinning nothing.");

        var result = game.Send(player, Harnesses.BeginSession);

        result.Accepted.ShouldBeTrue(
            "the very same command on the very same slice fails loudly without a seed, so reaching " +
            "acceptance is what says the harness issued one (refused: " + result.Rejection + ").");
    }
}
