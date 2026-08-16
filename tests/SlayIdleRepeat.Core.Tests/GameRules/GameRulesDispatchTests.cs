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
    // ------------------------------------------------------------------ totality

    /// <summary>
    /// A command no dispatch row names returns a result. It does not throw, and the reason is
    /// domain tier: <c>ILLEGAL_STATE</c>, not the transport-tier <c>UNKNOWN_COMMAND_TYPE</c> — the
    /// server refuses an unregistered wire name before the domain is ever invoked.
    /// </summary>
    [Fact]
    public void An_unregistered_command_is_rejected_rather_than_thrown()
    {
        var state = Worlds.OutsideARun();

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            new CommandDispatch(), state, new Worlds.MetaFixtureCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.Events.ShouldBeEmpty();
        result.NewState.ShouldBeSameAs(state);
    }

    /// <summary>
    /// On the real façade: <c>Apply</c> over the production table refuses a command no row names
    /// rather than throwing — proving the public entry point delegates to the same body the rest of
    /// this file drives, rather than being a second implementation.
    /// </summary>
    [Fact]
    public void Apply_over_the_real_table_refuses_a_command_no_row_names()
    {
        var state = Worlds.OutsideARun();

        var result = SlayIdleRepeat.Core.GameRules.Apply(state, new Worlds.MetaFixtureCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.ShouldBeSameAs(state);
    }

    /// <summary>
    /// A command whose system arrives in a later milestone rejects with <c>ILLEGAL_STATE</c>, and
    /// its row names the owning milestone.
    /// </summary>
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
    /// The two routes to <c>ILLEGAL_STATE</c> are told apart at the table, because the result
    /// itself gives no room to say which: "nobody registered this command" is a build-time hole,
    /// and "its milestone has not landed" is a deliberate, registered deferral.
    /// </summary>
    [Fact]
    public void An_unregistered_type_and_a_deferred_command_are_told_apart_at_the_table()
    {
        var table = new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09");

        table.For(typeof(Worlds.MetaFixtureCommand))!.DeferredTo.ShouldBe("M4-09");
        table.For(typeof(Worlds.OtherFixtureCommand)).ShouldBeNull();
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

    // ------------------------------------------------------- defects, not rejections

    /// <summary>
    /// A <c>CommandKind.Run</c> command whose slice carries no run is a loading defect: it throws
    /// rather than rejecting, because <c>RUN_NOT_FOUND</c> is transport tier — the transport already
    /// refused a command whose run is genuinely missing, so reaching the domain means the caller
    /// loaded the wrong slice.
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

    /// <summary>
    /// A meta command is dispatched perfectly happily with a run in the slice: a player can open
    /// the shop mid-run, since the split is about what the command acts on, not what happens to be
    /// loaded.
    /// </summary>
    [Fact]
    public void A_meta_command_runs_with_a_run_in_the_slice()
    {
        var table = Worlds.MetaTable((_, _) => HandlerResult.Accept());

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.InARun(), new Worlds.MetaFixtureCommand(), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run.ShouldNotBeNull();
    }

    /// <summary>
    /// The mirror of the rule above, from the handler's side: a <c>CommandKind.Meta</c> command
    /// that reaches for <c>input.Run</c> is told its dispatch row is classified wrongly. The slice
    /// here carries no run, since a meta command mid-run is handed the run quite happily.
    /// </summary>
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
    /// A handler that returned <c>default(HandlerResult)</c> is a defect, and the answer says which
    /// one rather than surfacing as a null reference two frames later: <c>HandlerResult</c> is a
    /// <c>readonly record struct</c>, so the default carries no rejection (reading as accepted) and
    /// no event list.
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

    /// <summary>Null arguments are caller defects and are named individually.</summary>
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

    // ------------------------------------------------------------------ the table itself

    /// <summary>
    /// The wire vocabulary is one-to-one: two rows may not claim one wire name, and two rows may
    /// not claim one command type. The two failures carry different wording on purpose: a
    /// duplicated type makes which rule runs depend on declaration order, and a duplicated name
    /// makes the envelope ambiguous in both directions.
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

    /// <summary>
    /// A refused registration leaves the table exactly as it was, rather than half added: the two
    /// indices are written together or not at all.
    /// </summary>
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
    /// Wire ids are <c>SCREAMING_SNAKE</c>. This is the one place a command's wire name is
    /// declared, so a typo here is a wire-contract break nothing else would see. Each row names the
    /// rule that refused it: an absent name is a registration that declared nothing, and a
    /// malformed one is a name spelled differently from the id on the wire.
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
    /// An undefined <see cref="CommandKind"/> is refused rather than defaulted. The kind decides
    /// whether a <c>RunRngScope</c> is built and whether the run's TTL slides, and both defaults
    /// are wrong in a way nothing downstream could notice.
    /// </summary>
    [Fact]
    public void An_undefined_command_kind_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandDispatch()
                .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, (CommandKind)0, "M4-09"))
            .Message.ShouldContain("is either a RUN command or a META command", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <b><c>OpensRun</c> belongs to a run command only, and the table refuses the pairing rather
    /// than leaving <c>Execute</c> to re-check it on every command.</b>
    /// </summary>
    /// <remarks>
    /// The flag exempts a row from both of <c>Execute</c>'s run guards, and the second of those
    /// <em>clears</em> a finished run off the working slice. A meta row carrying it would discard a
    /// run it is not even permitted to write, and would do it invisibly: the ownership check that
    /// catches a meta command writing the run compares against a run that is no longer there.
    /// </remarks>
    [Fact]
    public void A_meta_command_may_not_open_a_run()
    {
        Should.Throw<ArgumentException>(() => new CommandDispatch()
                .Handled<Worlds.MetaFixtureCommand>(
                    Worlds.MetaWireName, CommandKind.Meta, Accepts, opensRun: true))
            .Message.ShouldContain("Only a run command can open a run", Case.Sensitive);
    }

    /// <summary>…and the same registration is accepted the moment the row is a run command.</summary>
    /// <remarks>
    /// The negative control under the rule above: without it, a refusal thrown for some unrelated
    /// reason — the wire name, the kind, the handler — would read as the pairing being enforced.
    /// </remarks>
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

    /// <summary>
    /// The wire-name index is the single declared source of the type-name mapping, handed out as a
    /// view that cannot be written through.
    /// </summary>
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

    /// <summary>
    /// The floor under this whole file: the production registry carries all 49 rows. The number is
    /// a literal rather than the registry's own <c>Count</c> compared to itself, and the set behind
    /// it — which a count cannot see — is pinned against a hand-transcribed list in both directions
    /// by <c>CommandVocabularyTests</c>.
    /// </summary>
    [Fact]
    public void The_production_dispatch_table_carries_the_whole_registry()
    {
        SlayIdleRepeat.Core.GameRules.CommandTypesByWireName.Count.ShouldBe(
            49,
            "14 §2.3's registry is 19 run + 30 meta, and M1-02 registered every row. An empty table " +
            "would make Apply refuse every command in the game with ILLEGAL_STATE while this suite, " +
            "which drives its own tables, stayed entirely green — the failure this assertion exists " +
            "to prevent.");
    }
}
