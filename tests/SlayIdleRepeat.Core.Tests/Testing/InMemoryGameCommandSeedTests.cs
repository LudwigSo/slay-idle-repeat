using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Commands;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The harness is the server host for its own simulation, so it issues the per-command seed — and it
/// asks the dispatch table's own predicate which commands get one, never a list of its own.
/// </summary>
public sealed class InMemoryGameCommandSeedTests
{
    /// <summary>What a handler denied its seed says, so a case looks for that failure rather than for "something threw".</summary>
    private const string MiswiredHostMarker = "carries no CommandSeed";

    [Fact]
    public void Send_hands_a_meta_command_the_seed_a_null_one_would_have_failed_the_command_on()
    {
        var (game, player) = Harnesses.WithPlayer();

        // The control, and it has to run first: BEGIN_SESSION draws only on the day's first call, so
        // once the harness has sent one this slice would no longer demand a seed at all — and "the
        // harness supplied one" would be true of a harness that supplied none.
        var denied = Should.Throw<InvalidOperationException>(
            () => GameRules.Apply(
                game.State(player),
                Harnesses.BeginSession,
                new GameContext(
                    game.Clock.NowUtc, CommandSeed: null, game.Content, game.Entitlements, game.Flags)),
            "this command draws on the day's first call, so a context with no seed must be a defect. " +
            "Without that, the assertion below holds whether the harness issues a seed or not.");

        denied.Message.ShouldContain(
            MiswiredHostMarker,
            Case.Sensitive,
            "the control has to fail for the seed's absence specifically — any other defect would " +
            "leave the case below pinning nothing.");

        var result = game.Send(player, Harnesses.BeginSession);

        result.Accepted.ShouldBeTrue(
            "the harness refused its own meta command " + result.Rejection + ". The very same command " +
            "on the very same slice fails loudly without a seed, so reaching acceptance is what says " +
            "the harness issued one.");
    }

    /// <summary>
    /// The whole meta half, not only the row that happens to draw today: a harness that classified any
    /// one of the thirty as a run command would deny it a seed, and the day that row starts drawing it
    /// would fail on a player's device rather than here.
    /// </summary>
    [Fact]
    public void Send_hands_every_meta_command_in_the_registry_a_CommandSeed()
    {
        var game = Harnesses.New();

        var metaRows = GameRules.CommandTypesByWireName
            .Where(row => GameRules.RegistrationFor(row.Value)!.Kind == CommandKind.Meta)
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();

        metaRows.Length.ShouldBeGreaterThanOrEqualTo(
            30, "the registry's meta table has 30 rows; a shrunken subject set makes this sweep silent.");

        metaRows.Select(row => row.Key).ShouldContain(
            "BEGIN_SESSION",
            "the one row swept here that actually reads its seed today. Without it the sweep would be " +
            "quantifying over twenty-nine commands that could not tell a seed from its absence.");

        var offenders = metaRows
            .Select(row => Denied(game, row.Key, CommandVocabularyTests.Build(row.Value)))
            .Where(offender => offender is not null)
            .ToArray();

        offenders.ShouldBeEmpty(
            "a meta command reached its handler with no seed. An out-of-run draw has no persisted " +
            "counter to fall back on, so that command cannot draw at all — and the domain calls it a " +
            "miswired host, which is exactly what the harness would be.");
    }

    /// <summary>
    /// Sends one command to a player of its own and reports the miswired-host failure, or <c>null</c>
    /// when none escaped.
    /// </summary>
    /// <remarks>
    /// A fresh player per row, so one command's accepted state cannot change what the next row's
    /// handler decides — the sweep is about thirty independent commands, not about a sequence.
    /// Only the seed's absence is caught: any other failure is a different rule's to report.
    /// </remarks>
    private static string? Denied(InMemoryGame game, string wireName, GameCommand command)
    {
        try
        {
            game.Send(game.CreatePlayer(), command);

            return null;
        }
        catch (InvalidOperationException failure)
            when (failure.Message.Contains(MiswiredHostMarker, StringComparison.Ordinal))
        {
            return $"'{wireName}' is a meta row and the harness sent it with no CommandSeed: " +
                   failure.Message;
        }
    }
}
