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
/// The half that quantifies over the real command vocabulary is <b>vacuous today</b> — M1-02
/// authors the commands — and is written against its final subject anyway, with no <c>Skip</c>, in
/// the same shape as <c>Model/Snapshots/SnapshotFieldOrderPinTests</c>. The half that proves the
/// rule has teeth is not vacuous: it drives <see cref="CommandSeedPin.Violations"/> over the nine
/// frozen names and over a command that draws nothing, so both defects are demonstrably caught
/// before there is a command type to catch them on.
/// </para>
/// <para>
/// ⚠️ The 19 run-command names are M1-02's to author and are deliberately absent here.
/// <see cref="DrawsNothing"/> stands in for "any command outside the nine" and is spelled so it
/// cannot be mistaken for a vocabulary claim.
/// </para>
/// </remarks>
public sealed class CommandSeedPinTests
{
    /// <summary>
    /// A command name that is not one of the nine. Deliberately not a real command name: enumerating
    /// the 19 run commands here would be inventing vocabulary that M1-02 owns (steering S6).
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

    // ------------------------------------------------------------- vacuous until M1-02, on purpose

    /// <summary>
    /// 🔒 `14` §8.1 — every one of the nine seed-bearing names names a command type that actually
    /// exists. Vacuous while the vocabulary is absent; a real assertion the moment M1-02 declares
    /// it, and the thing that catches a typo in the nine or a rename on the other side.
    /// </summary>
    [Fact]
    public void Every_seed_bearing_command_name_names_a_real_command_type()
    {
        var declared = CommandSeedPin.CommandTypes
            .Select(CommandSeedPin.WireNameOf)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = declared.Count == 0
            ? Array.Empty<string>()
            : CommandSeedPin.SeedBearingMetaCommands
                .Where(name => !declared.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name =>
                    $"'{name}' is declared seed-bearing but no type under {CommandSeedPin.CommandsNamespace} " +
                    $"declares it. Declared: [{string.Join(", ", declared.OrderBy(n => n, StringComparer.Ordinal))}]. " +
                    "Either this list has a typo, or M1-02's dispatch row for that command declares a " +
                    $"different wire name — fix the disagreement, do not delete the rule. {CommandSeedPin.Consequence}")
                .ToArray();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The tripwire (steering S3/S4). The rule above holds over nothing today; this is the
    /// assertion that <b>announces</b> the day that stops being true, rather than letting a
    /// still-empty subject set keep looking like a passing pin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Named for its own subject, and not for the vocabulary.</b> This quantifies over
    /// <em>concrete types under <c>SlayIdleRepeat.Core.Commands</c></em>, which is not the same set
    /// as <c>Commands.GameCommandTests.The_command_vocabulary_is_still_absent_and_says_so_when_it_arrives</c>'s
    /// — concrete <c>GameCommand</c> subtypes, wherever declared. A command declared elsewhere wakes
    /// that one and not this; a helper type dropped into this namespace wakes this one and not that.
    /// Two mechanisms with two failure messages, so they carry two names.
    /// </para>
    /// <para>
    /// 🔒 This tripwire and the rule above share one predicate — whether
    /// <see cref="CommandSeedPin.CommandTypes"/> is empty — which is why that selector filters
    /// nesting only and not accessibility: an <c>internal</c> vocabulary must wake both, or the two
    /// would go silent together. ⚠️ The independent backstop is <b>not</b>
    /// <c>SubjectSetFloorTests</c>'s entry for this namespace: M1-06 moved that entry out of
    /// <c>Pending</c> and it now watches only that the namespace <em>exists</em>, which the abstract
    /// base already made true. It is the two rules named above, which read the assembly with
    /// Mono.Cecil and see internal types.
    /// </para>
    /// <para>
    /// <b>When this fails, the pin has woken up.</b> M1-02 landed the command vocabulary. Check
    /// <see cref="Every_seed_bearing_command_name_names_a_real_command_type"/> is green, wire
    /// <see cref="CommandSeedPin.Violations"/> into the command-construction path so the invariant
    /// is asserted per command rather than only over the nine names, and delete this tripwire.
    /// Never weaken the selector to make it green again.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_concrete_type_under_the_commands_namespace_exists_yet_and_says_so_when_one_does()
    {
        CommandSeedPin.CommandTypes.ShouldBeEmpty(
            "when this fails the CommandSeed pin has woken up — M1-02 landed the command vocabulary. " +
            "Confirm Every_seed_bearing_command_name_names_a_real_command_type is green, wire " +
            "CommandSeedPin.Violations into the command-construction path, and delete this tripwire. " +
            "Do not weaken the selector: an empty subject set is the failure this file exists to prevent.");
    }

    /// <summary>
    /// A command shape for driving the dispatch table's lookup. Deliberately <b>not</b> named after
    /// any of `14` §2.3's 49 rows and deliberately not under <c>Core/Commands/</c>: the vocabulary
    /// is M1-02's, and a fixture that borrowed a real name would read as one.
    /// </summary>
    private sealed record PinFixtureCommand : GameCommand;
}
