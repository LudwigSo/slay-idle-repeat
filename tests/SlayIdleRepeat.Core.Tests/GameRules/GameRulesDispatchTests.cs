using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The façade's dispatch: which handler runs, what happens when none does, and the boundary
/// between a rejection (the game saying no) and a defect (the caller or the domain being wrong).
/// </summary>
/// <remarks>
/// Driven through <c>GameRules.Execute</c> over tables built in the test, because the shapes they
/// need must never be committed to <c>Core</c> — a handler that hand-writes an RNG counter, a
/// duplicate registration, an undefined <c>CommandKind</c>.
/// </remarks>
public sealed class GameRulesDispatchTests
{
    /// <summary>
    /// <c>ILLEGAL_STATE</c>, not the transport-tier <c>UNKNOWN_COMMAND_TYPE</c> — the server
    /// refuses an unregistered wire name before the domain is ever invoked.
    /// </summary>
    [Fact]
    public void Apply_over_the_real_table_refuses_a_command_no_row_names()
    {
        var state = Worlds.OutsideARun();

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new Worlds.MetaFixtureCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.Events.ShouldBeEmpty();
        result.NewState.ShouldBeSameAs(state);
    }

    [Fact]
    public void A_deferred_command_rejects_and_its_row_names_the_owning_milestone()
    {
        var table = new CommandDispatch()
            .Deferred<Worlds.RunFixtureCommand>(Worlds.RunWireName, CommandKind.Run, "M3-15");

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.InARun(), new Worlds.RunFixtureCommand(), Worlds.Context);

        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);

        var registration = table.For(typeof(Worlds.RunFixtureCommand))!;
        registration.IsHandled.ShouldBeFalse();
        registration.DeferredTo.ShouldBe("M3-15");
    }

    /// <summary>
    /// A deferred row must name an owner: without one the <c>ILLEGAL_STATE</c> it produces is
    /// indistinguishable from a rule that refused the player.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_deferred_row_without_an_owner_is_refused(string owner)
    {
        Should.Throw<ArgumentException>(() => new CommandDispatch()
                .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, owner))
            .Message.ShouldContain("names the milestone task", Case.Sensitive);
    }

    /// <summary>
    /// Throws rather than rejects: <c>RUN_NOT_FOUND</c> is transport tier, so reaching the domain
    /// without a run means the caller loaded the wrong slice.
    /// </summary>
    [Fact]
    public void A_run_command_with_no_run_in_the_slice_is_a_defect_and_not_a_rejection()
    {
        var table = Worlds.RunTable((_, _) => HandlerResult.Accept());

        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.OutsideARun(), new Worlds.RunFixtureCommand(), Worlds.Context));

        thrown.Message.ShouldContain("RUN_NOT_FOUND", Case.Sensitive);
        thrown.Message.ShouldContain(Worlds.RunWireName, Case.Sensitive);
    }

    /// <summary>A player can open the shop mid-run: the split is about what the command acts on, not what is loaded.</summary>
    [Fact]
    public void A_meta_command_runs_with_a_run_in_the_slice()
    {
        var table = Worlds.MetaTable((_, _) => HandlerResult.Accept());

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.InARun(), new Worlds.MetaFixtureCommand(), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run.ShouldNotBeNull();
    }

    /// <summary>The slice carries no run on purpose: a meta command mid-run is handed the run quite happily.</summary>
    [Fact]
    public void A_meta_command_that_reaches_for_the_run_names_the_misclassified_row()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((command, input) =>
            {
                _ = input.Run;

                return HandlerResult.Accept();
            }),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("a CommandKind.Meta command tried to act on a run", Case.Sensitive);
        thrown.Message.ShouldContain("its dispatch row is classified wrongly", Case.Sensitive);
    }

    /// <summary>
    /// <c>HandlerResult</c> is a <c>readonly record struct</c>, so its default carries no rejection
    /// (reading as accepted) and no event list — a defect worth naming at the source.
    /// </summary>
    [Fact]
    public void A_handler_that_returns_the_default_struct_is_a_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => default),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("default(HandlerResult)", Case.Sensitive);
        thrown.Message.ShouldContain("no handler can legitimately return", Case.Sensitive);
    }

    [Fact]
    public void Apply_refuses_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(null!, new Worlds.MetaFixtureCommand(), Worlds.Context))
            .ParamName.ShouldBe("state");

        Should.Throw<ArgumentNullException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), null!, Worlds.Context))
            .ParamName.ShouldBe("command");

        Should.Throw<ArgumentNullException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(Worlds.OutsideARun(), new Worlds.MetaFixtureCommand(), null!))
            .ParamName.ShouldBe("context");
    }

    /// <summary>
    /// The two failures carry different wording on purpose: a duplicated type makes which rule runs
    /// depend on declaration order, and a duplicated name makes the envelope ambiguous both ways.
    /// </summary>
    [Fact]
    public void One_command_type_and_one_wire_name_each()
    {
        var byType = new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09");

        Should.Throw<InvalidOperationException>(() =>
                byType.Deferred<Worlds.MetaFixtureCommand>(Worlds.OtherWireName, CommandKind.Meta, "M4-09"))
            .Message.ShouldContain("registered twice", Case.Sensitive);

        var byName = new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09");

        Should.Throw<InvalidOperationException>(() =>
                byName.Deferred<Worlds.OtherFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09"))
            .Message.ShouldContain("wire name", Case.Sensitive);
    }

    /// <summary>The two indices are written together or not at all — no half-added row.</summary>
    [Fact]
    public void A_refused_registration_leaves_the_table_untouched()
    {
        var table = new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09");

        Should.Throw<InvalidOperationException>(() =>
            table.Deferred<Worlds.RunFixtureCommand>(Worlds.MetaWireName, CommandKind.Run, "M3-15"));

        table.For(typeof(Worlds.RunFixtureCommand)).ShouldBeNull(
            "the wire name was refused, so the type must not have been indexed either.");

        table.Deferred<Worlds.RunFixtureCommand>(Worlds.RunWireName, CommandKind.Run, "M3-15");

        table.For(typeof(Worlds.RunFixtureCommand))!.WireName.ShouldBe(Worlds.RunWireName);
        table.TypesByWireName.Count.ShouldBe(2);
    }

    /// <summary>
    /// The registration is the one place a command's wire name is declared, so a typo here is a
    /// wire-contract break nothing else would see.
    /// </summary>
    [Theory]
    [InlineData("", "A command registration declares 14 §2.3's wire name")]
    [InlineData("roll_dice", "is not a 14 §2.3 wire name")]
    [InlineData("RollDice", "is not a 14 §2.3 wire name")]
    [InlineData("ROLL DICE", "is not a 14 §2.3 wire name")]
    [InlineData("ROLL-DICE", "is not a 14 §2.3 wire name")]
    [InlineData("ROLL.DICE", "is not a 14 §2.3 wire name")]
    public void A_wire_name_that_is_absent_or_not_SCREAMING_SNAKE_is_refused(string wireName, string refusal)
    {
        var thrown = Should.Throw<ArgumentException>(() => new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(wireName, CommandKind.Meta, "M4-09"));

        thrown.ParamName.ShouldBe("wireName");
        thrown.Message.ShouldContain(refusal, Case.Sensitive);
    }

    /// <summary>The shapes the real vocabulary actually uses are accepted.</summary>
    [Theory]
    [InlineData("ROLL_DICE")]
    [InlineData("BEGIN_SESSION")]
    [InlineData("OPEN_CHEST")]
    [InlineData("CLAIM_QUEST_3")]
    public void A_SCREAMING_SNAKE_wire_name_is_accepted(string wireName)
    {
        new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(wireName, CommandKind.Meta, "M4-09")
            .TypesByWireName.ContainsKey(wireName).ShouldBeTrue();
    }

    /// <summary>
    /// The kind decides whether a <c>RunRngScope</c> is built and whether the run's TTL slides, and
    /// both defaults are wrong in a way nothing downstream could notice.
    /// </summary>
    [Fact]
    public void An_undefined_command_kind_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandDispatch()
                .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, (CommandKind)0, "M4-09"))
            .Message.ShouldContain("is either a RUN command or a META command", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <c>OpensRun</c> exempts a row from both of <c>Execute</c>'s run guards, the second of
    /// which clears a finished run off the working slice — a meta row carrying it would discard a
    /// run it is not permitted to write, invisibly.
    /// </summary>
    [Fact]
    public void A_meta_command_may_not_open_a_run()
    {
        Should.Throw<ArgumentException>(() => new CommandDispatch()
                .Handled<Worlds.MetaFixtureCommand>(
                    Worlds.MetaWireName, CommandKind.Meta, Accepts, opensRun: true))
            .Message.ShouldContain("Only a run command can open a run", Case.Sensitive);
    }

    /// <summary>
    /// The negative control: without it, a refusal thrown for some unrelated reason — the wire
    /// name, the kind, the handler — would read as the pairing being enforced.
    /// </summary>
    [Fact]
    public void A_run_command_may_open_a_run()
    {
        var table = new CommandDispatch()
            .Handled<Worlds.RunFixtureCommand>(
                Worlds.RunWireName, CommandKind.Run, Accepts, opensRun: true);

        table.For(typeof(Worlds.RunFixtureCommand))!.OpensRun.ShouldBeTrue();
    }

    /// <summary>A handler that accepts and produces nothing — the rows above are about registration.</summary>
    private static HandlerResult Accepts(Worlds.RunFixtureCommand command, HandlerInput input) =>
        HandlerResult.Accept();

    /// <inheritdoc cref="Accepts(Worlds.RunFixtureCommand, HandlerInput)"/>
    private static HandlerResult Accepts(Worlds.MetaFixtureCommand command, HandlerInput input) =>
        HandlerResult.Accept();

    [Fact]
    public void The_wire_name_index_maps_both_ways_and_is_read_only()
    {
        var table = new CommandDispatch()
            .Deferred<Worlds.RunFixtureCommand>(Worlds.RunWireName, CommandKind.Run, "M3-15")
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09");

        table.TypesByWireName.Count.ShouldBe(2);
        table.TypesByWireName[Worlds.RunWireName].ShouldBe(typeof(Worlds.RunFixtureCommand));
        table.TypesByWireName.TryGetValue(Worlds.MetaWireName, out var meta).ShouldBeTrue();
        meta.ShouldBe(typeof(Worlds.MetaFixtureCommand));
        table.TypesByWireName.ContainsKey(Worlds.OtherWireName).ShouldBeFalse();

        table.TypesByWireName.ShouldNotBeAssignableTo<IDictionary<string, Type>>(
            "a read-only-looking view that casts back to the dictionary behind it is not a boundary. " +
            "The same hole 30 §11.2's exposed-collection check exists for.");
    }
}
