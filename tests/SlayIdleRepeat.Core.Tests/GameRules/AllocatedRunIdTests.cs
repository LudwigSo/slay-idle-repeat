using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Commands;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The wire half of the run allocator: <c>GameRules.OpensRun</c> — the second public door onto the
/// dispatch table — and <c>GameContext.AllocatedRunId</c>, the ambient slot a wire host issues the
/// new run's identity through. The in-process regime (a null slot, the deterministic mint) is
/// asserted beside it so neither host's behaviour is implied by the other's.
/// </summary>
public sealed class AllocatedRunIdTests
{
    private static IReadOnlyDictionary<string, Type> Registry => GameRules.CommandTypesByWireName;

    /// <summary>The identity a wire host would issue — deliberately nothing like the mint's <c>RUN_&lt;player&gt;_&lt;counter&gt;</c> shape.</summary>
    private static readonly RunId WireIssued = new("RUN_9f2a4c000000000000000000000000ab");

    [Fact]
    public void OpensRun_answers_true_for_exactly_the_one_registered_row_that_opens_a_run()
    {
        // Floored by named member before the sweep, so an emptied registry cannot pass it vacuously.
        Registry.ContainsKey("START_RUN").ShouldBeTrue("the one opening row this whole case is about.");

        var openers = Registry
            .Where(row => GameRules.OpensRun(CommandVocabularyTests.Build(row.Value)))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => row.Key)
            .ToArray();

        openers.ShouldBe(
            new[] { "START_RUN" },
            "OpensRun is the endpoint split's exception list, and the registry has exactly one " +
            "exception: the run command submitted on the player endpoint because no run id exists " +
            "yet. A second true here is a second command the transport would let create runs.");
    }

    [Fact]
    public void OpensRun_answers_false_for_a_run_command_a_meta_command_and_an_unregistered_type()
    {
        GameRules.OpensRun(new RollDiceCommand()).ShouldBeFalse(
            "ROLL_DICE acts inside a run that already has its identity.");
        GameRules.OpensRun(new BeginSessionCommand("1.0.0", "hash")).ShouldBeFalse(
            "a meta command never opens a run at all.");
        GameRules.OpensRun(new UnregisteredFixtureCommand()).ShouldBeFalse(
            "an unregistered type has no row to read the flag off, and throwing would fail the host " +
            "before Apply could refuse the command in its own words (RequiresCommandSeed's totality " +
            "argument).");
    }

    [Fact]
    public void OpensRun_refuses_a_null_command()
    {
        Should.Throw<ArgumentNullException>(() => GameRules.OpensRun(null!))
            .ParamName.ShouldBe("command", "a null command has no row to answer for.");
    }

    [Fact]
    public void START_RUN_opens_the_run_at_the_allocated_identity_when_the_host_issued_one()
    {
        var result = GameRules.Apply(
            Worlds.OutsideARun(),
            new StartRunCommand(1, DifficultyTier.NORMAL),
            Worlds.Context with { AllocatedRunId = WireIssued });

        result.Accepted.ShouldBeTrue("the fixture player may start (1, NORMAL); this case is about the id alone.");
        result.NewState.Run.ShouldNotBeNull();
        result.NewState.Run.Id.ShouldBe(
            WireIssued,
            "the wire regime: the server allocates the RunId (14 §2.3), the handler installs it " +
            "verbatim. A run under any other id is a run the client cannot address.");
    }

    [Fact]
    public void START_RUN_falls_back_to_the_deterministic_mint_when_no_identity_was_issued()
    {
        var slice = Worlds.OutsideARun();
        var runsStartedBefore = slice.Player.RunsStarted;

        var result = GameRules.Apply(
            slice, new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Id.Value.ShouldBe(
            $"RUN_{slice.Player.Id.Value}_{runsStartedBefore + 1}",
            "the in-process regime: with no allocated id the mint derives the identity from the " +
            "player and the lifetime run counter, so a local host's runs stay reproducible.");
    }

    [Fact]
    public void An_allocated_id_on_a_command_that_does_not_open_a_run_is_a_miswired_host_not_a_rejection()
    {
        // Two shapes, per the guard's own wording: the run command that already has a run, and the
        // meta command that never opens one.
        var onARunCommand = Should.Throw<InvalidOperationException>(() => GameRules.Apply(
            Worlds.InARun(),
            new RollDiceCommand(),
            Worlds.Context with { AllocatedRunId = WireIssued }));

        onARunCommand.Message.ShouldContain(
            "GameContext.AllocatedRunId",
            customMessage: "the failure must name the slot the host miswired, or the repair is a search.");
        onARunCommand.Message.ShouldContain(
            "ROLL_DICE",
            customMessage: "and the row it was miswired onto.");

        var onAMetaCommand = Should.Throw<InvalidOperationException>(() => GameRules.Apply(
            Worlds.OutsideARun(),
            new BeginSessionCommand("1.0.0", "hash"),
            Worlds.Drawing(0xF00DUL) with { AllocatedRunId = WireIssued }));

        onAMetaCommand.Message.ShouldContain("GameContext.AllocatedRunId");
    }

    /// <summary>A command shape with no dispatch row, and no name that could be mistaken for a real one.</summary>
    private sealed record UnregisteredFixtureCommand : GameCommand;
}
