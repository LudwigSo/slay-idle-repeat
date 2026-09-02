using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Commands;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The one public door onto the run/meta split: a host asks this and nothing else to decide whether
/// a command carries a <c>CommandSeed</c>. Every case here reads the expected answer off the dispatch
/// row's own <c>CommandKind</c>, so the predicate cannot end up agreeing with a second list.
/// </summary>
public sealed class RequiresCommandSeedTests
{
    private static IReadOnlyDictionary<string, Type> Registry => GameRules.CommandTypesByWireName;

    [Fact]
    public void RequiresCommandSeed_answers_every_registered_row_the_way_its_CommandKind_does()
    {
        // Floors by named member, not by count: a sweep over an emptied or substituted registry
        // reports success, and both halves have to be genuinely present for it to mean anything.
        Registry.ContainsKey("START_RUN").ShouldBeTrue("the run row every later assertion here rests on.");
        Registry.ContainsKey("BEGIN_SESSION").ShouldBeTrue("the meta row every later assertion here rests on.");

        GameRules.RegistrationFor(Registry["START_RUN"])!.Kind.ShouldBe(
            CommandKind.Run, "START_RUN is the named run member this sweep is floored on.");
        GameRules.RegistrationFor(Registry["BEGIN_SESSION"])!.Kind.ShouldBe(
            CommandKind.Meta, "BEGIN_SESSION is the named meta member this sweep is floored on.");

        GameRules.RequiresCommandSeed(CommandVocabularyTests.Build(Registry["START_RUN"])).ShouldBeFalse(
            "a run command draws off the run's committed seed and its persisted counters, so it needs " +
            "none of its own.");
        GameRules.RequiresCommandSeed(CommandVocabularyTests.Build(Registry["BEGIN_SESSION"])).ShouldBeTrue(
            "a meta command has no persisted counter to draw against, so the seed is the only entropy " +
            "it can have — and BEGIN_SESSION's handler reads it on the day's first call.");

        Registry.Count.ShouldBeGreaterThanOrEqualTo(
            49,
            "the registry is 19 run + 30 meta rows. A floor rather than an equality so a later " +
            "milestone may append, but never so low that the sweep below can quantify over nothing.");

        var offenders = Registry
            .Where(row => GameRules.RequiresCommandSeed(CommandVocabularyTests.Build(row.Value)) !=
                          (GameRules.RegistrationFor(row.Value)!.Kind == CommandKind.Meta))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row =>
                $"'{row.Key}' is registered {GameRules.RegistrationFor(row.Value)!.Kind} and " +
                $"RequiresCommandSeed answers " +
                $"{GameRules.RequiresCommandSeed(CommandVocabularyTests.Build(row.Value))}.")
            .ToArray();

        offenders.ShouldBeEmpty(
            "the predicate and the dispatch row disagree about a command. A meta row denied its seed " +
            "cannot draw at all; a run row handed one has two sources of entropy and no rule about " +
            "which wins.");
    }

    /// <summary>
    /// <c>START_RUN</c> on its own, because it is the whole reason a host cannot answer this question
    /// from the endpoint it arrived on: it is submitted with no run id and is still a run command.
    /// </summary>
    [Fact]
    public void RequiresCommandSeed_is_false_for_START_RUN_even_though_it_arrives_on_the_player_endpoint()
    {
        var startRun = new StartRunCommand(1, DifficultyTier.NORMAL);

        GameRules.RegistrationFor(typeof(StartRunCommand))!.OpensRun.ShouldBeTrue(
            "this case is about the one row that creates the run its own kind would otherwise require; " +
            "if that flag moved, the case no longer names the interesting command.");

        GameRules.RequiresCommandSeed(startRun).ShouldBeFalse(
            "a host that classified by endpoint would hand START_RUN a seed, because it arrives " +
            "addressed to the player and not to a run. The dispatch row says otherwise, and the row " +
            "is what decides.");
    }

    [Fact]
    public void RequiresCommandSeed_is_false_for_a_command_type_no_dispatch_row_names()
    {
        GameRules.RegistrationFor(typeof(UnregisteredFixtureCommand)).ShouldBeNull(
            "this case is about a type the table does not carry; if it gained a row the case would be " +
            "asserting about a registered command instead.");

        GameRules.RequiresCommandSeed(new UnregisteredFixtureCommand()).ShouldBeFalse(
            "an unregistered command draws nothing, so there is no seed to issue it. Throwing here " +
            "would make a host fail before Apply ever got to refuse the command in its own words.");
    }

    [Fact]
    public void RequiresCommandSeed_refuses_a_null_command()
    {
        Should.Throw<ArgumentNullException>(() => GameRules.RequiresCommandSeed(null!))
            .ParamName.ShouldBe(
                "command",
                "a null command has no kind to answer for, and answering false for it would tell a " +
                "host a drawing command needs no seed.");
    }

    /// <summary>A command shape with no dispatch row, and no name that could be mistaken for a real one.</summary>
    private sealed record UnregisteredFixtureCommand : GameCommand;
}
