using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `14` §8.1 / `30` §3 — <c>CommandSeed</c> is server-issued and <b>meta-only</b>: it is
/// <c>null</c> on all 19 run commands and non-null on the nine meta commands that draw.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The half that quantifies over the real vocabulary went live in M1-02.</b> It was written
/// against its final subject in M1-07 and held vacuously until then, with no <c>Skip</c>, in the
/// same shape as <c>Model/Snapshots/SnapshotFieldOrderPinTests</c>. It now sweeps the dispatch
/// table: all 19 <c>CommandKind.Run</c> rows must be refused a seed, exactly nine of the 30
/// <c>CommandKind.Meta</c> rows must require one, and each of the nine must resolve to a
/// <c>Meta</c> row.
/// </para>
/// <para>
/// 🔒 <b>Those sweeps take their subject set from the dispatch table's <c>CommandKind</c>, never
/// from <see cref="CommandSeedPin.SeedBearingMetaCommands"/></b>, and the separation is what stops
/// them agreeing with themselves: a rule that asked the classifier's own list which commands draw
/// would pass over any list whatsoever, including one holding <c>ROLL_DICE</c>.
/// </para>
/// <para>
/// <see cref="DrawsNothing"/> is kept as the stand-in for "a name outside the nine" in the
/// classifier's own teeth-checks. It is deliberately not one of the forty-nine, so those checks
/// stay independent of the vocabulary they are now also driven over.
/// </para>
/// </remarks>
public sealed class CommandSeedPinTests
{
    /// <summary>
    /// A command name that is not one of the nine, and deliberately not one of the forty-nine
    /// either — so the classifier's teeth-checks below stay independent of the vocabulary the
    /// sweeps drive over.
    /// </summary>
    private const string DrawsNothing = "A_COMMAND_THAT_DRAWS_NOTHING";

    private const ulong AnySeed = 0x0123456789ABCDEFUL;

    /// <summary>The nine meta commands the M1 kickoff froze as seed-bearing.</summary>
    public static TheoryData<string> SeedBearingCommands()
    {
        var data = new TheoryData<string>();

        foreach (var name in CommandSeedPin.SeedBearingMetaCommands.OrderBy(n => n, StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    // ------------------------------------------------------------------ the classifier's teeth

    /// <summary>
    /// 🔒 `14` §8.1 — a meta command that draws and was handed no seed is a violation. Without a
    /// seed its draws have nothing to hash, and the only ways out are for the domain to invent
    /// entropy or to silently skip the draw.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeedBearingCommands))]
    public void A_meta_command_that_draws_must_carry_a_CommandSeed(string commandName)
    {
        var offenders = CommandSeedPin.Violations(commandName, GameContexts.WithSeed(null));

        offenders.ShouldHaveSingleItem()
            .ShouldContain("must carry the server-issued seed", Case.Sensitive);
    }

    /// <summary>The negative half: the same command with a seed is clean.</summary>
    [Theory]
    [MemberData(nameof(SeedBearingCommands))]
    public void A_meta_command_that_draws_is_clean_when_it_carries_one(string commandName)
    {
        CommandSeedPin.Violations(commandName, GameContexts.WithSeed(AnySeed)).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `14` §8.1 — the other direction, and the one that matters most: a command that draws
    /// nothing out of run must be handed <c>null</c>. A seed on a run command is a second source of
    /// entropy alongside the <c>Run</c> aggregate's committed <c>runSeed</c>, and the two would
    /// diverge between client and server the first time a replay used the stored one.
    /// </summary>
    [Fact]
    public void A_command_that_draws_nothing_must_not_carry_a_CommandSeed()
    {
        var offenders = CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(AnySeed));

        offenders.ShouldHaveSingleItem()
            .ShouldContain("must be null", Case.Sensitive);
    }

    /// <summary>The negative half: the same command with no seed is clean.</summary>
    [Fact]
    public void A_command_that_draws_nothing_is_clean_with_no_seed()
    {
        CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(null)).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The two failures are told apart, not merely counted (steering S2). A rule whose message
    /// said only "CommandSeed is wrong" would pass its own test while catching the opposite defect.
    /// </summary>
    [Fact]
    public void The_two_failures_say_which_one_fired()
    {
        var missing = CommandSeedPin.Violations("SPIN_WHEEL", GameContexts.WithSeed(null)).ShouldHaveSingleItem();
        var spurious = CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(AnySeed)).ShouldHaveSingleItem();

        missing.ShouldContain("SPIN_WHEEL", Case.Sensitive);
        missing.ShouldNotContain("must be null", Case.Sensitive);

        spurious.ShouldContain(DrawsNothing, Case.Sensitive);
        spurious.ShouldNotContain("must carry the server-issued seed", Case.Sensitive);

        missing.ShouldContain("14 §8.1", Case.Sensitive);
        spurious.ShouldContain("14 §8.1", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Zero is a seed. The check is <c>is null</c>, not <c>== 0</c>: a host that legitimately
    /// issued <c>0</c> must not read as "no seed", and a run command handed <c>0</c> must still be
    /// caught. This is the assertion that fails if the nullability is ever collapsed to a sentinel.
    /// </summary>
    [Fact]
    public void A_zero_seed_is_a_seed_and_not_an_absent_one()
    {
        CommandSeedPin.Violations("SPIN_WHEEL", GameContexts.WithSeed(0UL)).ShouldBeEmpty();
        CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(0UL)).ShouldNotBeEmpty();
    }

    // ------------------------------------------------------- floors under the classifier's inputs

    /// <summary>
    /// 🔒 Steering S3 — a floor under the set every rule here quantifies over, pinned by
    /// <b>identity</b> rather than cardinality. A count-only floor is satisfied by swapping one of
    /// the nine for a run command, and every other rule in this file is driven from this very set,
    /// so the substitution would leave the whole file green with a run command classified as
    /// seed-bearing.
    /// </summary>
    /// <remarks>
    /// The nine are the ⚄-marked rows of `14` §2.3, restated by the M1 kickoff (2026-08-11), which
    /// froze the vocabulary at 49 — 19 run commands, all carrying <c>null</c>, and 30 meta commands
    /// of which exactly these nine draw. Changing this list is a vocabulary decision, not a test edit.
    /// </remarks>
    [Fact]
    public void The_seed_bearing_set_is_the_nine_the_command_vocabulary_freezes()
    {
        CommandSeedPin.SeedBearingMetaCommands.ShouldBe(
            new[]
            {
                "BEGIN_SESSION", "OPEN_CHEST", "OPEN_CRATE", "OPEN_EGG", "REFORGE_ITEM",
                "REROLL_QUEST", "RETUNE_ITEM", "SPIN_WHEEL", "START_DUEL",
            },
            StringComparer.Ordinal,
            ignoreOrder: true,
            "these are the nine meta commands 14 §2.3 marks as drawing randomness. Every other rule " +
            "in this file quantifies over this set, so a substitution here silences all of them.");
    }

    /// <summary>
    /// 🔒 Membership is ordinal, asserted <b>through the classifier</b> rather than through the
    /// set. Shouldly's collection <c>ShouldContain</c>/<c>ShouldNotContain</c> compare with
    /// <c>EqualityComparer&lt;T&gt;.Default</c> and never consult a <c>HashSet</c>'s own comparer,
    /// so a set-level assertion here would pass unchanged if the set were rebuilt
    /// <c>OrdinalIgnoreCase</c> — which is exactly the defect this rule exists to catch.
    /// </summary>
    [Fact]
    public void The_seed_bearing_set_matches_ordinally()
    {
        CommandSeedPin.SeedBearingMetaCommands.Contains("spin_wheel").ShouldBeFalse();

        CommandSeedPin.Violations("SPIN_WHEEL", GameContexts.WithSeed(AnySeed)).ShouldBeEmpty();
        CommandSeedPin.Violations("spin_wheel", GameContexts.WithSeed(AnySeed)).ShouldNotBeEmpty(
            "a lowercase near-miss is not one of the nine, so a seed handed to it is spurious. A " +
            "case-insensitive set would classify it as seed-bearing and let a run command carry a seed.");
    }

    /// <summary>
    /// 🔒 `14` §2.3 — the wire name is <b>read off the dispatch row that declares it</b>, not
    /// derived from the type's name (carried-forward item 4, closed by M1-06).
    /// </summary>
    /// <remarks>
    /// The predecessor of this test pinned a heuristic — <c>SpinWheelCommand</c> → <c>SPIN_WHEEL</c>,
    /// and <c>OpenPvPCommand</c> → <c>OPEN_PV_P</c>, a limit it could only document. It existed
    /// because no authored scheme did. This drives the real lookup against a dispatch table built
    /// here, so the mechanism is proven before the 49 rows M1-02 adds exist to prove it on.
    /// </remarks>
    [Fact]
    public void A_wire_name_is_read_off_the_dispatch_row_that_declares_it()
    {
        var dispatch = new CommandDispatch()
            .Deferred<PinFixtureCommand>("A_FIXTURE_COMMAND", CommandKind.Meta, "M1-06");

        dispatch.TypesByWireName["A_FIXTURE_COMMAND"].ShouldBe(typeof(PinFixtureCommand));
        dispatch.For(typeof(PinFixtureCommand))!.WireName.ShouldBe("A_FIXTURE_COMMAND");
    }

    /// <summary>
    /// 🔒 `14` §2.3 — a type the dispatch table does not name has no declared wire name, and says
    /// <c>null</c> rather than inventing one from its type name.
    /// </summary>
    /// <remarks>
    /// This is the assertion that fails if the heuristic is ever reintroduced beside the
    /// declaration: a guesser would answer <c>PIN_FIXTURE</c> here. The unregistered command itself
    /// is reported by <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c>, which is why
    /// this file does not complain about it a second time (steering S4).
    /// </remarks>
    [Fact]
    public void An_unregistered_command_type_has_no_declared_wire_name()
    {
        CommandSeedPin.WireNameOf(typeof(PinFixtureCommand)).ShouldBeNull();
    }

    /// <summary>A missing type is a caller bug, not an empty wire name.</summary>
    [Fact]
    public void WireNameOf_refuses_a_null_type()
    {
        Should.Throw<ArgumentNullException>(() => CommandSeedPin.WireNameOf(null!))
            .ParamName.ShouldBe("commandType");
    }

    /// <summary>
    /// 🔒 Steering S3 — the selector that builds <see cref="CommandSeedPin.CommandTypes"/> is not
    /// itself the reason that set is empty. Pointed at a namespace that exists today it reaches
    /// real types; if it did not, the vacuous rule below would stay green on the day M1-02 lands
    /// the whole vocabulary, which is the one failure a vacuous rule cannot announce.
    /// </summary>
    [Fact]
    public void The_command_type_selector_reaches_a_namespace_that_exists_today()
    {
        CommandSeedPin.ConcreteTypesUnder("SlayIdleRepeat.Core.Content")
            .Select(t => t.Name)
            .ShouldContain(nameof(ContentSnapshot));

        CommandSeedPin.IsUnder(CommandSeedPin.CommandsNamespace, CommandSeedPin.CommandsNamespace).ShouldBeTrue();
        CommandSeedPin.IsUnder("SlayIdleRepeat.Core.Commands.Run", CommandSeedPin.CommandsNamespace).ShouldBeTrue();
        CommandSeedPin.IsUnder("SlayIdleRepeat.Core.CommandsExtra", CommandSeedPin.CommandsNamespace).ShouldBeFalse();
        CommandSeedPin.IsUnder("SlayIdleRepeat.Core", CommandSeedPin.CommandsNamespace).ShouldBeFalse();
    }

    // --------------------------------------------------- live over the real vocabulary from M1-02

    /// <summary>
    /// 🔒 `14` §8.1 / `30` §3 — <b>every one of the 19 run commands is handed <c>null</c></b>, and a
    /// seed on any of them is a violation. Driven over the wire names the <b>dispatch table</b>
    /// carries for <c>CommandKind.Run</c> rows.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>This is the half that cannot be tautological, and that is why it is written this way.</b>
    /// The subject set comes from the dispatch table's <c>CommandKind</c>, which
    /// <see cref="CommandSeedPin.SeedBearingMetaCommands"/> has never seen: a rule that derived
    /// "does it draw?" from the same list the classifier consults would agree with itself over any
    /// list at all. Put <c>ROLL_DICE</c> into the nine and this fails, naming it.
    /// <para>
    /// A seed on a run command is a <b>second source of entropy</b> beside the <c>Run</c>
    /// aggregate's committed <c>runSeed</c>, and the two diverge between client and server the first
    /// time a replay uses the stored one.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_run_command_in_the_registry_may_carry_a_CommandSeed()
    {
        var runCommands = WireNamesOfKind(CommandKind.Run);

        runCommands.Count.ShouldBe(
            19,
            "14 §2.3's run table has 19 rows. If this set shrank, the sweep below would quantify over " +
            "less than the run half of the vocabulary and report success for the rest.");

        var offenders = new List<string>();

        foreach (var name in runCommands)
        {
            if (CommandSeedPin.Violations(name, GameContexts.WithSeed(null)).Count != 0)
            {
                offenders.Add($"'{name}' is a run command and was refused a null CommandSeed.");
            }

            var withSeed = CommandSeedPin.Violations(name, GameContexts.WithSeed(AnySeed));

            if (withSeed.Count != 1 || !withSeed[0].Contains("must be null", StringComparison.Ordinal))
            {
                offenders.Add(
                    $"'{name}' is a run command and a CommandSeed handed to it was not refused with " +
                    $"'must be null'. Got: [{string.Join(" | ", withSeed)}]");
            }
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `14` §8.1 — the other half over the real table: of the 30 meta rows, <b>exactly the nine
    /// marked ⚄ require a seed</b> and the other 21 must be handed <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The nine come from `14` §2.3's ⚄ marks, transcribed in
    /// <see cref="CommandSeedPin.SeedBearingMetaCommands"/> and pinned by
    /// <see cref="The_seed_bearing_set_is_the_nine_the_command_vocabulary_freezes"/>; the thirty come
    /// from the dispatch table. Two independent sources, so a name moved between them fails here
    /// rather than agreeing with itself.
    /// </remarks>
    [Fact]
    public void Exactly_nine_of_the_thirty_meta_commands_draw()
    {
        var metaCommands = WireNamesOfKind(CommandKind.Meta);

        metaCommands.Count.ShouldBe(30, "14 §2.3's meta table has 30 rows, under a header that says 29.");

        var drawing = metaCommands
            .Where(name => CommandSeedPin.Violations(name, GameContexts.WithSeed(null)).Count != 0)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        drawing.ShouldBe(
            CommandSeedPin.SeedBearingMetaCommands.OrderBy(n => n, StringComparer.Ordinal),
            StringComparer.Ordinal,
            ignoreOrder: false,
            "the meta rows that refuse a null seed must be exactly the ⚄ nine. A name in the list " +
            "that is not a registered meta command, or a meta command that quietly joined the nine, " +
            "shows up here as a set difference rather than as a count that still says nine.");
    }

    /// <summary>
    /// 🔒 The cross-check that closes the loop: <b>every seed-bearing name is registered
    /// <c>CommandKind.Meta</c></b>. `30` §3 puts <c>CommandSeed</c> on meta commands only, so a run
    /// command in the nine is the invariant inverted.
    /// </summary>
    /// <remarks>
    /// It is separate from the two sweeps above and it is not redundant with them: those quantify
    /// over the table's rows and would both stay green if one of the nine named no registered
    /// command at all — that is
    /// <see cref="Every_seed_bearing_command_name_names_a_real_command_type"/>'s job — while this one
    /// asserts the <b>kind</b> of the row each of the nine resolves to.
    /// </remarks>
    [Fact]
    public void Every_seed_bearing_command_is_registered_as_a_meta_command()
    {
        var offenders = new List<string>();

        foreach (var name in CommandSeedPin.SeedBearingMetaCommands.OrderBy(n => n, StringComparer.Ordinal))
        {
            var type = SlayIdleRepeat.Core.GameRules.CommandTypesByWireName[name];
            var kind = SlayIdleRepeat.Core.GameRules.RegistrationFor(type)!.Kind;

            if (kind != CommandKind.Meta)
            {
                offenders.Add(
                    $"'{name}' is declared seed-bearing but its dispatch row is {kind}. 30 §3 puts " +
                    "CommandSeed on meta commands only: an in-run draw comes from the Run aggregate's " +
                    "committed runSeed and its persisted counter, so a run command with a seed has two " +
                    $"sources of entropy and no rule about which one wins. {CommandSeedPin.Consequence}");
            }
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>Every wire name the dispatch table registers under a kind.</summary>
    private static IReadOnlyList<string> WireNamesOfKind(CommandKind kind) =>
        SlayIdleRepeat.Core.GameRules.CommandTypesByWireName
            .Where(row => SlayIdleRepeat.Core.GameRules.RegistrationFor(row.Value)!.Kind == kind)
            .Select(row => row.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 🔒 `14` §8.1 — every one of the nine seed-bearing names names a command type that actually
    /// exists. It held vacuously from M1-07 until M1-02 landed the vocabulary; it is <b>live</b>
    /// now, and it is what catches a typo in the nine or a rename on the other side.
    /// </summary>
    /// <remarks>
    /// ⚠️ The <c>declared.Count == 0</c> arm is what made it vacuous, and it is <b>kept</b> rather
    /// than deleted — with its own floor beneath it. Deleting the arm would turn "the vocabulary is
    /// gone" into nine indistinguishable "no type declares this" complaints; keeping it without the
    /// floor would let an emptied namespace pass. The floor says which failure it is.
    /// </remarks>
    [Fact]
    public void Every_seed_bearing_command_name_names_a_real_command_type()
    {
        var declared = CommandSeedPin.CommandTypes
            .Select(CommandSeedPin.WireNameOf)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        declared.Count.ShouldBe(
            49,
            "14 §2.3's registry is 19 run + 30 meta, and every one of them is declared under " +
            $"{CommandSeedPin.CommandsNamespace} and registered on a dispatch row. If this is 0 the " +
            "selector has gone quiet — the vacuity this rule used to have on purpose, and must never " +
            "have again — and if it is anything else, a command has lost its declaration or its row.");

        var offenders = declared.Count == 0
            ? Array.Empty<string>()
            : CommandSeedPin.SeedBearingMetaCommands
                .Where(name => !declared.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name =>
                    $"'{name}' is declared seed-bearing but no type under {CommandSeedPin.CommandsNamespace} " +
                    $"declares it. Declared: [{string.Join(", ", declared.OrderBy(n => n, StringComparer.Ordinal))}]. " +
                    "Either this list has a typo, or the dispatch row for that command declares a " +
                    $"different wire name — fix the disagreement, do not delete the rule. {CommandSeedPin.Consequence}")
                .ToArray();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// A command shape for driving the dispatch table's lookup. Deliberately <b>not</b> named after
    /// any of `14` §2.3's 49 rows and deliberately not under <c>Core/Commands/</c>: the vocabulary
    /// is M1-02's, and a fixture that borrowed a real name would read as one.
    /// </summary>
    private sealed record PinFixtureCommand : GameCommand;
}
