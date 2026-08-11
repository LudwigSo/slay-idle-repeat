using Shouldly;
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
    /// 🔒 Steering S3 — a floor under the set every rule here quantifies over. The M1 kickoff froze
    /// the vocabulary at 49 commands of which exactly nine draw; a set that quietly shrank would
    /// take every rule above green over fewer and fewer commands.
    /// </summary>
    [Fact]
    public void Exactly_nine_meta_commands_are_declared_seed_bearing()
    {
        CommandSeedPin.SeedBearingMetaCommands.Count.ShouldBe(
            9,
            "the M1 kickoff froze the command vocabulary at 49 (19 run + 30 meta) with exactly nine " +
            "seed-bearing meta commands. Changing this number is a vocabulary decision, not a test edit.");
    }

    /// <summary>
    /// 🔒 Membership is ordinal. No wire-name casing convention is enforced anywhere in code, so a
    /// case-insensitive set would classify a differently-cased near-miss as seed-bearing and hand
    /// a run command a seed with this rule green.
    /// </summary>
    [Fact]
    public void The_seed_bearing_set_matches_ordinally()
    {
        CommandSeedPin.SeedBearingMetaCommands.ShouldContain("SPIN_WHEEL");
        CommandSeedPin.SeedBearingMetaCommands.ShouldNotContain("spin_wheel");
    }

    /// <summary>
    /// The wire-name heuristic maps a command type to `14` §2.3's SCREAMING_SNAKE name, with and
    /// without the conventional <c>Command</c> suffix.
    /// </summary>
    /// <remarks>
    /// ⚠️ A heuristic, not an authored scheme — see <see cref="CommandSeedPin.WireName"/>. It is
    /// tested so that when it stops matching M1-02's real names, the failure is understood as
    /// "teach this the real convention", not "the rule is broken".
    /// </remarks>
    [Fact]
    public void WireName_maps_a_command_type_to_its_SCREAMING_SNAKE_name()
    {
        CommandSeedPin.WireName(typeof(SpinWheelCommand)).ShouldBe("SPIN_WHEEL");
        CommandSeedPin.WireName(typeof(BeginSessionCommand)).ShouldBe("BEGIN_SESSION");
        CommandSeedPin.WireName(typeof(OpenChest)).ShouldBe("OPEN_CHEST");
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
        CommandSeedPin.PublicTypesUnder("SlayIdleRepeat.Core.Content")
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
            .Select(CommandSeedPin.WireName)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = declared.Count == 0
            ? Array.Empty<string>()
            : CommandSeedPin.SeedBearingMetaCommands
                .Where(name => !declared.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name =>
                    $"'{name}' is declared seed-bearing but no type under {CommandSeedPin.CommandsNamespace} " +
                    $"maps to it. Declared: [{string.Join(", ", declared.OrderBy(n => n, StringComparer.Ordinal))}]. " +
                    "Either the name is wrong, or CommandSeedPin.WireName does not know M1-02's naming " +
                    $"convention — teach it, do not delete the rule. {CommandSeedPin.Consequence}")
                .ToArray();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The tripwire (steering S3/S4). The rule above holds over nothing today; this is the
    /// assertion that <b>announces</b> the day that stops being true, rather than letting a
    /// still-empty subject set keep looking like a passing pin.
    /// </summary>
    /// <remarks>
    /// <b>When this fails, the pin has woken up.</b> M1-02 landed the command vocabulary. Check
    /// <see cref="Every_seed_bearing_command_name_names_a_real_command_type"/> is green, wire
    /// <see cref="CommandSeedPin.Violations"/> into the command-construction path so the invariant
    /// is asserted per command rather than only over the nine names, and delete this tripwire.
    /// Never weaken the selector to make it green again.
    /// </remarks>
    [Fact]
    public void The_command_vocabulary_is_still_absent_and_says_so_when_it_arrives()
    {
        CommandSeedPin.CommandTypes.ShouldBeEmpty(
            "when this fails the CommandSeed pin has woken up — M1-02 landed the command vocabulary. " +
            "Confirm Every_seed_bearing_command_name_names_a_real_command_type is green, wire " +
            "CommandSeedPin.Violations into the command-construction path, and delete this tripwire. " +
            "Do not weaken the selector: an empty subject set is the failure this file exists to prevent.");
    }

    // Wire-name fixtures. Shapes only — CommandSeedPin.WireName reads Type.Name and nothing else,
    // so these prove the mapping without pretending to be M1-02's command types.
    private sealed record SpinWheelCommand;

    private sealed record BeginSessionCommand;

    private sealed record OpenChest;
}
