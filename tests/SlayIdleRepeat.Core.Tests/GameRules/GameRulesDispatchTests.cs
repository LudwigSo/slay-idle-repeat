using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §2 / §2.2 — the façade's dispatch: which handler runs, what happens when none does, and the
/// boundary between a <b>rejection</b> (the game saying no) and a <b>defect</b> (the caller or the
/// domain being wrong).
/// </summary>
/// <remarks>
/// Driven through <c>GameRules.Execute</c> over tables built in the test, because the shapes they need
/// must <b>never</b> be committed to <c>Core</c> — a handler that hand-writes an RNG counter, a
/// duplicate registration, an undefined <c>CommandKind</c>. ⚠️ Every row of the real table is
/// <c>Deferred</c>, so a handler-shaped rule stated over it would report success forever.
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
    /// 🔒 `30` §2.1 <b>P3</b>, on the real façade: <c>Apply</c> over the production table refuses a
    /// command <b>no row names</b> rather than throwing.
    /// </summary>
    /// <remarks>
    /// It proves the public entry point delegates to the same body the rest of this file drives
    /// rather than being a second implementation. ⚠️ The command is a <em>fixture</em> and stays one
    /// now that the table has 49 real rows: this is the arm
    /// <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c> keeps unreachable in
    /// production, so the only way to exercise it is with a command type deliberately outside
    /// `14` §2.3.
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

    /// <summary>
    /// 🔒 The mirror of the rule above, from the handler's side: a <c>CommandKind.Meta</c> command
    /// that reaches for <c>input.Run</c> is told its <b>dispatch row</b> is classified wrongly.
    /// </summary>
    /// <remarks>
    /// The two guards a misclassified row can hit are deliberately different sentences (steering
    /// <b>S2</b>): this one is "a meta command tried to act on a run", and
    /// <c>GameRulesRngTests.A_meta_command_has_no_run_scope_even_with_a_run_loaded</c> is "a meta
    /// command tried to draw from the run's streams". A reader handed the wrong one reclassifies in
    /// the wrong direction. ⚠️ The slice carries <b>no</b> run, which is the only shape that reaches
    /// this guard: a meta command mid-run is handed the run quite happily (a player can open the
    /// shop without leaving), and a <c>CommandKind.Run</c> command with no run never reaches a
    /// handler at all — <c>Apply</c> refuses it first as a loading defect.
    /// </remarks>
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
    /// 🔒 A handler that returned <c>default(HandlerResult)</c> is a <b>defect</b>, and the answer
    /// says which one rather than surfacing as a null reference two frames later.
    /// </summary>
    /// <remarks>
    /// The same hole <c>CommandResultTests.The_default_struct_is_not_a_result_and_says_so</c> covers
    /// one layer out, and it is reachable the same way: <c>HandlerResult</c> is a
    /// <c>readonly record struct</c>, so the language hands out an instance that ran no constructor
    /// — carrying no rejection (which reads as <em>accepted</em>) and no event list.
    /// </remarks>
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
    /// 🔒 A refused registration leaves the table <b>exactly as it was</b>, rather than half added.
    /// </summary>
    /// <remarks>
    /// The two indices are written together or not at all — the same construction, and the same
    /// reason, as <c>Run.CommitStreamPositions</c> validating the whole incoming map before writing
    /// any of it. A row that indexed itself by type and then threw on its wire name would leave a
    /// dispatch that answered <c>For(type)</c> for a command no wire name can reach, and the
    /// registrar's own retry under a corrected name would then fail as a duplicate type.
    /// </remarks>
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
    /// 🔒 `14` §2.3's ids are <c>SCREAMING_SNAKE</c>. This is the one place a command's wire name is
    /// declared, so a typo here is a wire-contract break nothing else would see.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Each row names the rule that refused it</b> (steering <b>S2</b>), because two different
    /// ones do: an <b>absent</b> name is a registration that declared nothing, and a malformed one
    /// is a name spelled differently from the id on the wire. They are different mistakes with
    /// different fixes, and <c>Should.Throw&lt;ArgumentException&gt;</c> alone is also satisfied by
    /// <c>ArgumentNullException</c> and <c>ArgumentOutOfRangeException</c> — the guard on the row
    /// below this one.
    /// </remarks>
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
                .Deferred<Worlds.MetaFixtureCommand>(Worlds.MetaWireName, (CommandKind)0, "M4-09"))
            .Message.ShouldContain("is either a RUN command or a META command", Case.Sensitive);
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
    /// 🔒 Steering <b>S3</b> — the floor under this whole file, restated for the table that now
    /// exists: the production registry carries `14` §2.3's <b>49</b> rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces M1-06's tripwire, which asserted the table was <em>empty</em> and was deleted on
    /// the commit that made it false. The floor it was standing in for has not gone away: the rules
    /// above are still driven from tables built in the test, because the shapes they need — a handler
    /// that hand-writes an RNG counter, a duplicate registration, an undefined <c>CommandKind</c> —
    /// must never be committed to <c>Core</c>. What has changed is that <c>Apply</c> over the real
    /// table is no longer a one-assertion affair;
    /// <c>Commands.CommandVocabularyTests.Every_command_in_the_vocabulary_is_applied_and_refused_rather_than_thrown</c>
    /// drives all forty-nine through it.
    /// </para>
    /// <para>
    /// The number is a literal rather than the registry's own <c>Count</c> compared to itself, and
    /// the <b>set</b> behind it — which a count cannot see — is pinned against a hand-transcribed
    /// list in both directions by <c>CommandVocabularyTests</c>.
    /// </para>
    /// </remarks>
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
