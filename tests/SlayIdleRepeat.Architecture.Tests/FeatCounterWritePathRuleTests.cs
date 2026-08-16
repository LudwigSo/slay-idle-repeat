using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `28` D2 / `30` §12.7 — the <b>one choke point</b> for a lifetime feat counter:
/// <c>GameRules</c> advances them from the event list <c>Apply</c> returns, and nothing else in
/// <c>Core</c> calls <c>Player.CountFeat</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this needs a rule and not just a unit test.</b> <c>CountFeat</c> is <c>internal</c>,
/// so every handler and every rule in the assembly can reach it. A handler that also counted its
/// own event would double-count against the projection — silently, and <b>permanently</b>: `30`
/// §12.7 forbids rebuilding a counter after the fact, so there is no pass that could later work out
/// what the real number was. Every comparable irreversible write path in this repository already
/// has an IL rule over it (<c>Every_currency_mutation_emits_CurrencyChanged</c> is the model); this
/// one had only unit tests over the code that exists today.
/// </para>
/// <para>
/// 🔒 A <b>new file</b> rather than an edit to the M0-authored rule files, on
/// <c>IntraRulesLayeringRuleTests</c>' own precedent: several M4 lanes are in flight on
/// <c>DomainPurityTests</c> and <c>Infrastructure/Domain.cs</c>, and patching a component another
/// agent is scheduled to touch is steering <b>S12</b>'s failure mode.
/// </para>
/// <para>
/// ⚠️ <b>What it does not close.</b> An IL scan sees a <c>call</c>; it cannot see a <c>const</c>
/// read (steering S18) — irrelevant here, since <c>CountFeat</c> is a method — and it cannot tell a
/// projection row that should exist from one that does not. It closes exactly one direction: a
/// second writer.
/// </para>
/// </remarks>
public sealed class FeatCounterWritePathRuleTests
{
    private const string PlayerType = "Player";
    private const string CountFeatMethod = "CountFeat";

    /// <summary>The one type allowed to advance a lifetime counter.</summary>
    private const string SanctionedCaller = "SlayIdleRepeat.Core.GameRules";

    /// <summary>
    /// `28` D2 / `30` §12.7 — one writer, named by identity, with a floor under the subject set.
    /// </summary>
    [Fact]
    public void Only_GameRules_advances_a_lifetime_feat_counter()
    {
        var callers = CallersOf(CountFeatMethod).ToArray();

        // S3 — the floor, and it is an IDENTITY floor rather than a count: a rule stated as "no
        // unsanctioned caller" is satisfied forever by a scan that finds no caller at all, which is
        // the state it would be in if CountFeat were renamed or the call site were removed.
        callers.ShouldNotBeEmpty(
            "nothing calls Player.CountFeat. Either the counters stopped being advanced at all — in " +
            "which case 28 D2's retroactivity guarantee is quietly dead and every Feat will unlock " +
            "at zero — or the method was renamed and this rule is now scanning for a name that does " +
            "not exist.");

        callers.Select(c => c.DeclaringType.FullName).ShouldContain(
            SanctionedCaller,
            "GameRules.Apply is where a counter is advanced. If it no longer calls CountFeat, the " +
            "call moved somewhere this rule then has to sanction deliberately.");

        ArchRule.Empty(
            callers
                .Where(c => !c.DeclaringType.FullName.Equals(SanctionedCaller, StringComparison.Ordinal))
                .Select(c =>
                    $"{Il.Describe(c)} calls Player.{CountFeatMethod}. A lifetime feat counter has " +
                    "exactly one writer: GameRules, over the event list Apply returns. A second " +
                    "writer double-counts every event that reaches both, and 30 §12.7 forbids " +
                    "rebuilding a counter — so the wrong number is the number, for good. Emit the " +
                    "event and add a row to FeatCounterProjection instead."),
            "Only GameRules advances a lifetime feat counter (28 D2, 30 §12.7).");
    }

    /// <summary>
    /// `23` §6 — the teeth: the scan really does distinguish a called method from an uncalled one,
    /// in both directions. Without this, a broken matcher would report "no unsanctioned callers"
    /// forever.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>This is not decoration — it caught the rule dead on its first run.</b> The first draft
    /// matched <c>OpCodes.Call</c> only, and C# emits <c>callvirt</c> for an instance method on a
    /// reference type even when the method is not virtual. Both arms above reported zero callers,
    /// which the rule itself could only read as "nobody writes a counter". Steering S1's M2
    /// amendment names this exact shape.
    /// </remarks>
    [Fact]
    public void The_caller_scan_finds_a_method_that_is_called_and_none_for_one_that_is_not()
    {
        CallersOf("MarkChapterTierCleared").ShouldNotBeEmpty(
            "a handler calls it, so a scan that finds nothing here is matching on something other " +
            "than the method name and this file's rule is silent by construction.");

        CallersOf("AMethodPlayerDoesNotDeclare").ShouldBeEmpty(
            "if this finds callers the matcher is not comparing names at all.");
    }

    /// <summary>Every method in <c>Core</c> whose body calls <c>Player.&lt;name&gt;</c>.</summary>
    /// <remarks>
    /// Both call opcodes: C# emits <c>callvirt</c> for an instance method on a reference type even
    /// when the method is not virtual, so a <c>Call</c>-only scan sees none of these.
    /// </remarks>
    private static IEnumerable<MethodDefinition> CallersOf(string methodName) =>
        Il.MethodsWithBodies(ProductionAssemblies.CoreModule)
            .Where(method => Il.Instructions(method).Any(instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference called &&
                called.Name.Equals(methodName, StringComparison.Ordinal) &&
                called.DeclaringType.Name.Equals(PlayerType, StringComparison.Ordinal)));
}
