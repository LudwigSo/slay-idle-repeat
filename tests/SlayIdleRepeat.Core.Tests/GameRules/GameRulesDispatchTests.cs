using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §2 / §2.2 — the façade's dispatch: which handler runs, what happens when none does, and
/// the boundary between a <b>rejection</b> (the game saying no to the player) and a <b>defect</b>
/// (the caller or the domain being wrong).
/// </summary>
/// <remarks>
/// The rules here are driven through <c>GameRules.Execute</c> over tables built in the test, for the
/// reason <c>GapRegister.Expired(entries)</c> takes its entries as a parameter: the real table is
/// empty until M1-02 lands the 49 commands, so every rule stated over it would hold vacuously and
/// report success forever (steering <b>S3</b>). <c>Apply</c> itself is exercised too, against the
/// real table, for the one thing that is true of it today.
/// </remarks>
public sealed class GameRulesDispatchTests
{
    // ------------------------------------------------------------------ P3 · totality

    /// <summary>
    /// 🔒 `30` §2.1 <b>P3</b> — a command no dispatch row names returns a <b>result</b>. It does not
    /// throw, and the reason is domain tier.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>ILLEGAL_STATE</c> rather than `14` §16.2's <c>UNKNOWN_COMMAND_TYPE</c>, which is a
    /// <b>transport</b>-tier value: the server refuses a wire name it has no row for before the
    /// domain is invoked, and `30` §2 forbids <c>Apply</c> from returning one at all. Which rule
    /// fired is pinned separately, at the mechanism, by
    /// <see cref="An_unregistered_type_and_a_deferred_command_are_told_apart_at_the_table"/> —
    /// several rules produce <c>ILLEGAL_STATE</c>, so the code alone would not say (steering S2).
    /// </remarks>
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
    /// 🔒 `30` §2.1 <b>P3</b>, on the real façade: <c>Apply</c> over the production table — empty
    /// until M1-02 — refuses rather than throws.
    /// </summary>
    /// <remarks>
    /// The one assertion about <c>Apply</c> that is meaningful before the vocabulary exists, and it
    /// is worth having: it proves the public entry point delegates to the same body the rest of this
    /// file drives, rather than being a second implementation.
    /// </remarks>
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
    /// 🔒 `14` §16.2 — a command whose <b>system</b> arrives in a later milestone rejects with
    /// <c>ILLEGAL_STATE</c> and its row names the owning milestone.
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
    /// 🔒 Steering <b>S2</b> — the two routes to <c>ILLEGAL_STATE</c> are told apart at the
    /// <b>table</b>, because the wire contract of `30` §2 gives the result no room to say which.
    /// </summary>
    /// <remarks>
    /// A test that only asserted the code would pass while the opposite defect fired: "nobody
    /// registered this command" is a build-time hole that
    /// <c>Every_command_type_is_handled_by_Apply</c> exists to close, and "its milestone has not
    /// landed" is a deliberate, registered deferral. The same rejection reaches the player either
    /// way — correctly, because the player's experience is identical — so the distinction lives
    /// where an engineer reads it.
    /// </remarks>
    [Fact]
    public void An_unregistered_type_and_a_deferred_command_are_told_apart_at_the_table()
    {
        var table = new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, CommandKind.Meta, "M4-09");

        table.For(typeof(Worlds.MetaFixtureCommand))!.DeferredTo.ShouldBe("M4-09");
        table.For(typeof(Worlds.OtherFixtureCommand)).ShouldBeNull();
    }

    /// <summary>
    /// 🔒 A deferred row must name an owner: without one the <c>ILLEGAL_STATE</c> it produces is
    /// indistinguishable from a rule that refused the player, and its <c>GapRegister</c> entry has
    /// nothing to agree with.
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
    /// 🔒 A <c>CommandKind.Run</c> command whose slice carries no run is a <b>loading defect</b>.
    /// </summary>
    /// <remarks>
    /// It throws rather than rejecting, and the choice is the tier boundary of `30` §2 read from the
    /// other side: `14` §16.2's <c>RUN_NOT_FOUND</c> is transport tier, so the transport has already
    /// refused a command whose run is genuinely missing. Reaching the domain means the Application
    /// layer loaded the wrong slice (`30` §4.1), and answering <c>ILLEGAL_STATE</c> would report a
    /// miswired caller to the player as a rule.
    /// </remarks>
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
    /// A meta command is dispatched perfectly happily <b>with</b> a run in the slice: a player can
    /// open the shop mid-run, and `14` §2.3's split is about what the command acts on, not about
    /// what happens to be loaded.
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
    /// 🔒 `14` §2.3 is <b>one</b> vocabulary: two rows may not claim one wire name, and two rows may
    /// not claim one command type.
    /// </summary>
    /// <remarks>
    /// The two failures carry different wording on purpose (steering S2): a duplicated type makes
    /// which rule runs depend on declaration order, and a duplicated name makes the envelope
    /// ambiguous in both directions. They are opposite mistakes with opposite fixes.
    /// </remarks>
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
    /// 🔒 `14` §2.3's ids are <c>SCREAMING_SNAKE</c>. This is the one place a command's wire name is
    /// declared, so a typo here is a wire-contract break nothing else would see.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("roll_dice")]
    [InlineData("RollDice")]
    [InlineData("ROLL DICE")]
    [InlineData("ROLL-DICE")]
    [InlineData("ROLL.DICE")]
    public void A_wire_name_that_is_not_SCREAMING_SNAKE_is_refused(string wireName)
    {
        Should.Throw<ArgumentException>(() => new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(wireName, CommandKind.Meta, "M4-09"));
    }

    /// <summary>The shapes `14` §2.3 actually uses are accepted.</summary>
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
    /// 🔒 An undefined <see cref="CommandKind"/> is refused rather than defaulted. The kind decides
    /// whether a <c>RunRngScope</c> is built and whether the run's `14` §16.3 TTL slides, and both
    /// defaults are wrong in a way nothing downstream could notice.
    /// </summary>
    [Fact]
    public void An_undefined_command_kind_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandDispatch()
            .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, (CommandKind)0, "M4-09"));
    }

    /// <summary>
    /// 🔒 The wire-name index is the single declared source of the type↔name mapping (carried-forward
    /// item 4), and it is handed out as a view that cannot be written through.
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
    /// 🔒 Steering <b>S3</b> — the floor under this whole file: the production table is <b>empty</b>
    /// today, so every rule above is driven from a table built here rather than from that one.
    /// </summary>
    /// <remarks>
    /// <b>When this fails, M1-02 has landed the vocabulary.</b> Confirm every rule in this file
    /// still holds against the real rows, add the coverage the 49 commands need, and delete this
    /// tripwire — do not weaken it. It is the assertion that says out loud why <c>Execute</c> takes
    /// its table as a parameter.
    /// </remarks>
    [Fact]
    public void The_production_dispatch_table_is_still_empty_and_says_so_when_it_is_not()
    {
        SlayIdleRepeat.Core.GameRules.CommandTypesByWireName.ShouldBeEmpty(
            "when this fails M1-02 has landed the 49 commands of 14 §2.3. Every rule in this file is " +
            "driven from a table built in the test precisely because this one was empty; re-read them " +
            "against the real rows, then delete this tripwire. Never weaken it — an empty subject set " +
            "reported as success is the failure this assertion exists to prevent.");
    }
}
