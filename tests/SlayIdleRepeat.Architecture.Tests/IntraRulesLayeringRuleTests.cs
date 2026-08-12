using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 <b>R17 — the layering <em>inside</em> <c>Core/Rules/</c>:
/// <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c>.</b> <c>Rules.Effects</c> is the bottom and may
/// name neither of the other two.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule did not exist and had to.</b> `30` §11.4's layering table has a single
/// <c>Rules</c> row and governs nothing <em>inside</em> it, which
/// <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> faithfully reproduces:
/// <c>Rules.Effects</c> naming <c>Rules.Combat</c> is a <c>Rules</c> type naming a <c>Rules</c> type
/// and passes. M2-05's <c>IEffectActorView</c> flagged exactly this — <em>"which direction that
/// dependency should run … is a milestone-level decision … until it is taken deliberately, a cycle
/// between the two can form with every architecture rule green"</em>. R17 took the decision; this
/// enforces it.
/// </para>
/// <para>
/// <b>What it buys, concretely.</b> The trigger layer fires <em>into</em> the combat log, which lives
/// in <c>Rules.Combat</c>. The natural implementation is a call to
/// <c>CombatLog.AppendRunEffectQueued</c>, and it would compile, pass every existing rule, and put a
/// namespace cycle in the middle of the game's hottest path. What R17 forces instead is a seam the
/// upper layer implements — <c>IRunEffectSink</c> — and this rule is what makes the seam load-bearing
/// rather than a stylistic preference the next edit can quietly bypass.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those, and patching a component another agent is scheduled to touch
/// is how M0 broke thirty tests (steering S12). The namespace constants are therefore restated here
/// rather than added to <c>Domain</c>, and <see cref="The_namespaces_R17_governs_are_the_ones_under_Rules"/> pins that
/// restatement against the real tree so it cannot go stale.
/// </para>
/// </remarks>
public sealed class IntraRulesLayeringRuleTests
{
    /// <summary>The bottom of the intra-<c>Rules</c> layering — `18`'s effect DSL interpreter.</summary>
    internal const string EffectsNamespace = "SlayIdleRepeat.Core.Rules.Effects";

    /// <summary>The middle — `05` §1-2's stat block and `18` §8's aggregation.</summary>
    internal const string StatsNamespace = "SlayIdleRepeat.Core.Rules.Stats";

    /// <summary>The top — `05` §3's simulation and §7's combat log.</summary>
    internal const string CombatNamespace = "SlayIdleRepeat.Core.Rules.Combat";

    /// <summary>
    /// 🔒 R17, over `30` §11.4's <c>Rules</c> row — nothing under <c>Rules/Effects/</c> names a type
    /// under <c>Rules/Stats/</c> or <c>Rules/Combat/</c>. `18` §2.5's run-op emission goes through a
    /// seam the upper layer implements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An IL scan rather than a text grep, for <c>Il</c>'s stated reason: a grep is defeated by a
    /// <c>using</c> alias, a fully-qualified call or an extension method, and it fires inside comments
    /// — and the file that most wants to name <c>CombatLog</c> is the one whose remarks explain at
    /// length why it does not.
    /// </para>
    /// <para>
    /// ⚠️ <b>ONE THING AN IL SCAN CANNOT SEE: a <c>const</c>.</b> Found by making this rule fail on
    /// purpose (steering S1). The first probe reached up with
    /// <c>_ = CombatLog.TicksPerSecond</c> and the rule stayed <b>green</b> — C# folds a
    /// <c>const</c> into the call site at compile time, so no reference to the declaring type reaches
    /// the metadata. Re-probed with a method call, the rule reported both
    /// <c>CombatLog</c> and <c>CombatEvent</c>.
    /// </para>
    /// <para>
    /// This is a real hole and it is recorded rather than papered over — but it is narrow, and the one
    /// place it bites is already closed by other means. <c>Rules.Combat</c>'s consts are the tick
    /// rate, the fight cap, the telegraph band and <c>CombatActor</c>'s id layout; the only two
    /// <c>Rules.Effects</c> has any use for are the first two, which is exactly why
    /// <c>TriggerSchedule</c> declares its own and <c>PeriodicAnchoringTests.The_tick_rate_agrees_
    /// with_the_combat_log</c> compares them. A const borrowed across the boundary would be a
    /// compile-time copy of a number rather than a runtime dependency — the cycle this rule exists to
    /// prevent cannot be built out of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void Rules_Effects_is_the_bottom_of_the_intra_Rules_layering()
    {
        var forbidden = new[]
        {
            (Namespace: StatsNamespace,
                Reason: "R17 puts Rules.Stats ABOVE Rules.Effects: a stat aggregation reads effects, " +
                        "not the other way round. 18 §8's resolution order is stated over effects the " +
                        "aggregator collects."),
            (Namespace: CombatNamespace,
                Reason: "R17 puts Rules.Combat at the TOP. A trigger that appended to the combat log " +
                        "directly would put a namespace cycle in the game's hottest path — emit " +
                        "through a seam Rules.Combat implements (IRunEffectSink)."),
        };

        var offenders = new List<string>();

        foreach (var type in Il.TypesUnder(ProductionAssemblies.CoreModule, EffectsNamespace))
        {
            foreach (var referenced in Il.ReferencedTypeNames(type))
            {
                foreach (var (ns, reason) in forbidden)
                {
                    if (Il.IsUnder(NamespaceOf(referenced), ns))
                    {
                        offenders.Add($"{type.FullName} names {referenced} — {reason}");
                    }
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "R17: the layering inside Core/Rules/ is Rules.Combat -> Rules.Stats -> Rules.Effects, " +
            "and Rules.Effects is the bottom.");
    }

    /// <summary>
    /// 🔒 <b>The floors</b> under R17's subject and target sets — `23` §6, over the namespaces
    /// `30` §11.4 puts beneath <c>Rules</c>. Steering S3: the rule above is "no member of set S names
    /// set F", which passes vacuously when either set empties. Both can — a rename of
    /// <c>Rules/Effects/</c> empties the subject, and a rename of <c>Rules/Combat/</c> empties the
    /// target while the rule stays green over a cycle that now runs to a differently-named namespace.
    /// </summary>
    /// <remarks>
    /// The counts are floors rather than equalities, so adding a type is not a test edit. They are
    /// well below the counts on the commit the rule landed — 20 under <c>Effects</c>, 7 under
    /// <c>Stats</c>, 5 under <c>Combat</c>, before compiler-generated types.
    /// </remarks>
    [Fact]
    public void The_namespaces_R17_governs_are_the_ones_under_Rules()
    {
        var offenders = new List<string>();

        Floor(offenders, EffectsNamespace, 8,
            "the subject set of Rules_Effects_is_the_bottom_of_the_intra_Rules_layering. Empty, it " +
            "reports success over nothing and 18's interpreter is free to reach up into the " +
            "simulator again.");

        Floor(offenders, StatsNamespace, 3,
            "one half of that rule's forbidden set. Empty, half the rule governs nothing.");

        Floor(offenders, CombatNamespace, 3,
            "the other half — and the one that matters, because the combat log is what a trigger " +
            "most wants to reach.");

        ArchRule.Empty(
            offenders,
            "R17's subject and target sets are the ones the layering rule was written against (23 §6).");
    }

    private static void Floor(List<string> offenders, string ns, int floor, string consequence)
    {
        var found = Il.TypesUnder(ProductionAssemblies.CoreModule, ns)
                      .Count(t => !Domain.IsCompilerGenerated(t));

        if (found < floor)
        {
            offenders.Add(
                $"types under {ns}: found {found}, floor is {floor}. {consequence} " +
                "If this shrank on purpose, lower the floor in the same commit and say why.");
        }
    }

    /// <summary>The namespace part of a full type name, for a name this suite holds as a string.</summary>
    /// <remarks>
    /// <c>Il.NamespaceOf</c> takes a <see cref="TypeDefinition"/>; the references being scanned are
    /// names, and a nested type's name carries a <c>/</c> that must be cut before the last dot is
    /// meaningful.
    /// </remarks>
    private static string NamespaceOf(string fullName)
    {
        var outer = fullName.Split('/')[0];
        var lastDot = outer.LastIndexOf('.');

        return lastDot < 0 ? string.Empty : outer[..lastDot];
    }
}
