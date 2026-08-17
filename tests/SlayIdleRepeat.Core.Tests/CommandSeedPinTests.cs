using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// <c>CommandSeed</c> is server-issued and meta-only: <c>null</c> on all 19 run commands, non-null on
/// the eleven meta commands that draw. Every sweep below takes its subject set from the dispatch
/// table's <c>CommandKind</c> rather than from <see cref="CommandSeedPin.Violations"/> itself, so the
/// rule can't end up agreeing with its own list.
/// </summary>
public sealed class CommandSeedPinTests
{
    /// <summary>Not one of the nine, and deliberately not one of the fifty-two either.</summary>
    private const string DrawsNothing = "A_COMMAND_THAT_DRAWS_NOTHING";

    private const ulong AnySeed = 0x0123456789ABCDEFUL;

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

    [Theory]
    [MemberData(nameof(SeedBearingCommands))]
    public void A_meta_command_that_draws_must_carry_a_CommandSeed(string commandName)
    {
        var offenders = CommandSeedPin.Violations(commandName, GameContexts.WithSeed(null));

        offenders.ShouldHaveSingleItem()
            .ShouldContain("must carry the server-issued seed", Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(SeedBearingCommands))]
    public void A_meta_command_that_draws_is_clean_when_it_carries_one(string commandName)
    {
        CommandSeedPin.Violations(commandName, GameContexts.WithSeed(AnySeed)).ShouldBeEmpty();
    }

    /// <summary>
    /// A seed on a command that draws nothing is a second source of entropy alongside the run's
    /// committed seed, and the two would diverge the first time a replay used the stored one.
    /// </summary>
    [Fact]
    public void A_command_that_draws_nothing_must_not_carry_a_CommandSeed()
    {
        var offenders = CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(AnySeed));

        offenders.ShouldHaveSingleItem()
            .ShouldContain("must be null", Case.Sensitive);
    }

    [Fact]
    public void A_command_that_draws_nothing_is_clean_with_no_seed()
    {
        CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(null)).ShouldBeEmpty();
    }

    /// <summary>The two failures are told apart, not merely counted.</summary>
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

    /// <summary>Zero is a seed: the check is <c>is null</c>, not <c>== 0</c>, so it survives a collapse to a sentinel.</summary>
    [Fact]
    public void A_zero_seed_is_a_seed_and_not_an_absent_one()
    {
        CommandSeedPin.Violations("SPIN_WHEEL", GameContexts.WithSeed(0UL)).ShouldBeEmpty();
        CommandSeedPin.Violations(DrawsNothing, GameContexts.WithSeed(0UL)).ShouldNotBeEmpty();
    }

    // ------------------------------------------------------- floors under the classifier's inputs

    /// <summary>
    /// Pinned by identity rather than cardinality: a count-only floor would still pass if one of the
    /// eleven were swapped for a run command.
    /// </summary>
    [Fact]
    public void The_seed_bearing_set_is_the_eleven_the_command_vocabulary_freezes()
    {
        CommandSeedPin.SeedBearingMetaCommands.ShouldBe(
            new[]
            {
                "BEGIN_SESSION", "ENHANCE", "MERGE", "OPEN_CHEST", "OPEN_CRATE", "OPEN_EGG",
                "REFORGE_ITEM", "REROLL_QUEST", "RETUNE_ITEM", "SPIN_WHEEL", "START_DUEL",
            },
            StringComparer.Ordinal,
            ignoreOrder: true,
            "these are the eleven meta commands 14 §2.3 marks as drawing randomness — MERGE and " +
            "ENHANCE joined it with M4-04, which found both of them drawing and neither marked. " +
            "Every other rule in this file quantifies over this set, so a substitution here silences " +
            "all of them.");
    }

    /// <summary>
    /// Asserted through the classifier rather than the set: Shouldly's collection assertions compare
    /// with <c>EqualityComparer&lt;T&gt;.Default</c> and never consult a <c>HashSet</c>'s own
    /// comparer, so a set-level assertion would pass unchanged if the set were rebuilt
    /// <c>OrdinalIgnoreCase</c>.
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

    /// <summary>The wire name is read off the dispatch row that declares it, not derived from the type's name.</summary>
    [Fact]
    public void A_wire_name_is_read_off_the_dispatch_row_that_declares_it()
    {
        var dispatch = new CommandDispatch()
            .Deferred<PinFixtureCommand>("A_FIXTURE_COMMAND", CommandKind.Meta, "M1-06");

        dispatch.TypesByWireName["A_FIXTURE_COMMAND"].ShouldBe(typeof(PinFixtureCommand));
        dispatch.For(typeof(PinFixtureCommand))!.WireName.ShouldBe("A_FIXTURE_COMMAND");
    }

    /// <summary>An unregistered type has no declared wire name and answers <c>null</c> rather than inventing one.</summary>
    [Fact]
    public void An_unregistered_command_type_has_no_declared_wire_name()
    {
        CommandSeedPin.WireNameOf(typeof(PinFixtureCommand)).ShouldBeNull();
    }

    [Fact]
    public void WireNameOf_refuses_a_null_type()
    {
        Should.Throw<ArgumentNullException>(() => CommandSeedPin.WireNameOf(null!))
            .ParamName.ShouldBe("commandType");
    }

    /// <summary>
    /// The selector that builds <see cref="CommandSeedPin.CommandTypes"/> is not itself the reason
    /// that set could be empty — proven by pointing it at a namespace known to exist today.
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
    /// Every one of the 19 run commands is handed <c>null</c>, driven over the wire names the
    /// dispatch table carries for <c>CommandKind.Run</c> rows — a subject set
    /// <see cref="CommandSeedPin.SeedBearingMetaCommands"/> has never seen, so the rule can't agree
    /// with the classifier's own list.
    /// </summary>
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
    /// Written as the complement of the eleven, because the obvious form (asking the classifier which
    /// rows draw) cannot fail against itself.
    /// </summary>
    [Fact]
    public void The_nineteen_meta_commands_that_do_not_draw_may_not_carry_a_CommandSeed()
    {
        var metaCommands = WireNamesOfKind(CommandKind.Meta);

        metaCommands.Count.ShouldBe(
            33,
            "14 §2.3's meta table has 33 rows since the M4 retro ruling of 2026-08-17 added UNEQUIP, " +
            "LOCK_ITEM and SET_AUTO_SALVAGE_RULES. None of the three draws, so the ⚄ count is " +
            "unchanged and all three land in the sweep below.");

        var quiet = metaCommands
            .Where(name => !CommandSeedPin.SeedBearingMetaCommands.Contains(name))
            .ToArray();

        quiet.Length.ShouldBe(
            22,
            "33 meta rows minus the 11 marked ⚄. The M4 retro ruling of 2026-08-17 added three rows " +
            "and marked none of them: UNEQUIP writes a slot, LOCK_ITEM writes a flag and " +
            "SET_AUTO_SALVAGE_RULES writes configuration, and not one of the three draws. If this " +
            "is 33 the eleven have stopped naming " +
            "registered commands; if it is 0 the whole meta half has been declared seed-bearing — and " +
            "either way the sweep below would be quantifying over the wrong set rather than failing.");

        var offenders = new List<string>();

        foreach (var name in quiet)
        {
            if (CommandSeedPin.Violations(name, GameContexts.WithSeed(null)).Count != 0)
            {
                offenders.Add($"'{name}' draws no out-of-run randomness and was refused a null CommandSeed.");
            }

            var withSeed = CommandSeedPin.Violations(name, GameContexts.WithSeed(AnySeed));

            if (withSeed.Count != 1 || !withSeed[0].Contains("must be null", StringComparison.Ordinal))
            {
                offenders.Add(
                    $"'{name}' draws no out-of-run randomness and a CommandSeed handed to it was not " +
                    $"refused with 'must be null'. Got: [{string.Join(" | ", withSeed)}]");
            }
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// Separate from the two sweeps above because it is the one assertion that would notice a row
    /// vanishing from the table altogether — the sweeps only quantify over what the table has.
    /// </summary>
    [Fact]
    public void The_two_kinds_partition_the_whole_registry()
    {
        var run = WireNamesOfKind(CommandKind.Run);
        var meta = WireNamesOfKind(CommandKind.Meta);

        run.Count.ShouldBe(19);
        meta.Count.ShouldBe(33);
        (run.Count + meta.Count).ShouldBe(
            SlayIdleRepeat.Core.GameRules.CommandTypesByWireName.Count,
            "every registered row is one kind or the other — CommandKind has no third member and no zero.");

        CommandSeedPin.SeedBearingMetaCommands
            .Where(name => !meta.Contains(name, StringComparer.Ordinal))
            .ShouldBeEmpty("every ⚄ row of 14 §2.3 is in its meta table.");
    }

    /// <summary>
    /// Not redundant with the two sweeps above: those quantify over the table's rows and would stay
    /// green if one of the nine named no registered command at all. This asserts the kind of the row
    /// each of the nine actually resolves to.
    /// </summary>
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

    /// <summary>Catches a typo in the nine, or a rename on the dispatch side.</summary>
    [Fact]
    public void Every_seed_bearing_command_name_names_a_real_command_type()
    {
        var declared = CommandSeedPin.CommandTypes
            .Select(CommandSeedPin.WireNameOf)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        declared.Count.ShouldBe(
            52,
            "14 §2.3's registry is 19 run + 33 meta, and every one of them is declared under " +
            $"{CommandSeedPin.CommandsNamespace} and registered on a dispatch row. If this is 0 the " +
            "selector has gone quiet — the vacuity this rule used to have on purpose, and must never " +
            "have again — and if it is anything else, a command has lost its declaration or its row.");

        var offenders = CommandSeedPin.SeedBearingMetaCommands
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

    /// <summary>A command shape for driving the dispatch table's lookup — not under Core/Commands/ or named after a real row.</summary>
    private sealed record PinFixtureCommand : GameCommand;
}
