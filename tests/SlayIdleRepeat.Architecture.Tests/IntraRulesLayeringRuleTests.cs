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

    /// <remarks>
    /// 🔒 Stated as a <b>table</b>, in <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c>'
    /// shape, rather than as one scan over <c>Rules.Effects</c>. R17 is an ordering of three
    /// namespaces and therefore has <b>two</b> edges: an earlier draft enforced only the bottom one,
    /// so <c>Rules.Stats</c> naming <c>Rules.Combat</c> — the other cycle-forming edge,
    /// <c>Combat → Stats → Combat</c> — passed with the rule's own headline claiming otherwise. It
    /// costs nothing to close today, because <c>Rules/Stats/</c> names nothing in <c>Rules.Combat</c>,
    /// and it will not be free later.
    /// </remarks>
    private static readonly (string Subject, string Forbidden, string Reason)[] ForbiddenEdges =
    {
        (EffectsNamespace, StatsNamespace,
            "R17 puts Rules.Stats ABOVE Rules.Effects: a stat aggregation reads effects, not the " +
            "other way round. 18 §8's resolution order is stated over effects the aggregator collects."),
        (EffectsNamespace, CombatNamespace,
            "R17 puts Rules.Combat at the TOP. A trigger that appended to the combat log directly " +
            "would put a namespace cycle in the game's hottest path — emit through a seam " +
            "Rules.Combat implements (IRunEffectSink)."),
        (StatsNamespace, CombatNamespace,
            "R17 puts Rules.Combat above Rules.Stats. `05` §3.1 has the simulator aggregate stats, " +
            "not the reverse — a stat block that named the simulator would close the cycle from the " +
            "middle rather than from the bottom."),
    };

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
        var offenders = new List<string>();

        foreach (var (subject, forbidden, reason) in ForbiddenEdges)
        {
            foreach (var type in Il.TypesUnder(ProductionAssemblies.CoreModule, subject))
            {
                offenders.AddRange(
                    Il.ReferencedTypeNames(type)
                      .Where(referenced => Il.IsUnder(NamespaceOf(referenced), forbidden))
                      .Select(referenced => $"{type.FullName} names {referenced} — {reason}"));
            }
        }

        ArchRule.Empty(
            offenders,
            "R17: the layering inside Core/Rules/ is Rules.Combat -> Rules.Stats -> Rules.Effects, " +
            "and every edge of it runs one way.");
    }

    /// <summary>
    /// 🔒 <b>The floors</b> under R17's subject and target sets — `23` §6, over the namespaces
    /// `30` §11.4 puts beneath <c>Rules</c>. Steering S3: the rule above is "no member of set S names
    /// set F", which passes vacuously when either set empties. Both can — a rename of
    /// <c>Rules/Effects/</c> empties the subject, and a rename of <c>Rules/Combat/</c> empties the
    /// target while the rule stays green over a cycle that now runs to a differently-named namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counts are floors rather than equalities, so adding a type is not a test edit. They are
    /// set below the counts on the commit the rule landed, which
    /// <see cref="The_floors_are_below_the_counts_the_rule_was_written_against"/> pins so the numbers
    /// in this comment cannot go stale (steering S9).
    /// </para>
    /// <para>
    /// 🔒 <b><c>Rules.Effects</c> is floored twice, and it has to be.</b> <c>Il.TypesUnder</c> matches
    /// by namespace <b>prefix</b>, so a single floor over <c>Rules.Effects</c> is satisfied by
    /// <c>Rules/Effects/Triggers/</c> alone: move the whole `18` §4/§5 interpreter out and the rule
    /// still reports a healthy subject set while quantifying over none of it. The second floor is on
    /// the root namespace's <b>own</b> types — the shared roster predicate, the evaluation context,
    /// the two seams — which is what a move would actually empty.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_namespaces_R17_governs_are_the_ones_under_Rules()
    {
        var offenders = new List<string>();

        Floor(offenders, EffectsNamespace, EffectsFloor,
            "the subject set of Rules_Effects_is_the_bottom_of_the_intra_Rules_layering. Empty, it " +
            "reports success over nothing and 18's interpreter is free to reach up into the " +
            "simulator again.");

        Floor(offenders, TriggersNamespace, TriggersFloor,
            "`18` §3's trigger model — the part of the subject set that most wants to name the " +
            "combat log, and the part a prefix floor over Rules.Effects would hide the loss of.");

        Floor(offenders, StatsNamespace, StatsFloor,
            "both the subject of the Stats -> Combat edge and half the forbidden set of the " +
            "Effects -> Stats edge. Empty, two of the three edges govern nothing.");

        Floor(offenders, CombatNamespace, CombatFloor,
            "the forbidden set of two edges — and the one that matters, because the combat log is " +
            "what a trigger most wants to reach.");

        // 🔒 The root namespace's OWN types, not the prefix. See the remarks.
        RootFloor(offenders, EffectsNamespace, EffectsRootFloor,
            "A prefix floor cannot see this shrink, and `18` §4/§5's interpreter moving out is " +
            "exactly what would shrink it.");

        // 🔴 The SAME argument, in the direction it had not been applied. Found by measuring every
        //    floor in this file against the tree: CombatFloor is 4 against 88 types under
        //    Rules.Combat, of which 47 are in Bosses/, Enemies/ and Status/. The combat log — the
        //    thing CombatFloor's own message says it is there for, "the one that matters, because
        //    the combat log is what a trigger most wants to reach" — could be deleted along with
        //    the whole of Rules/Combat/'s root, and a prefix floor of 4 stays satisfied by the boss
        //    engine next door. That is precisely the hazard the remarks above identify for
        //    Rules.Effects and close with EffectsRootFloor; Rules.Combat was left on the bare prefix.
        RootFloor(offenders, CombatNamespace, CombatRootFloor,
            "`05` §7's combat log, `05` §4's attack pipeline and `05` §3's tick loop are declared " +
            "directly here, and they are the forbidden set the Effects -> Combat and Stats -> " +
            "Combat edges exist to protect. A prefix floor over Rules.Combat is satisfied by " +
            "Bosses/, Enemies/ and Status/ alone, so it cannot see them go.");

        // 🔒 The three restated namespace constants are the ones Domain declares. Not editing
        // Domain.cs was deliberate (M1-12 holds it, steering S12), but the two statements can still
        // be pinned to each other — otherwise M1-12 renaming a constant makes R17 govern a namespace
        // that no longer exists, silently.
        offenders.AddRange(
            new[]
            {
                (Restated: StatsNamespace, Declared: Domain.StatsRulesNamespace, Name: nameof(StatsNamespace)),
                (Restated: CombatNamespace, Declared: Domain.CombatRulesNamespace, Name: nameof(CombatNamespace)),
                (Restated: EffectsNamespace, Declared: Domain.RulesNamespace + ".Effects", Name: nameof(EffectsNamespace)),
            }
            .Where(pair => !pair.Restated.Equals(pair.Declared, StringComparison.Ordinal))
            .Select(pair =>
                $"{pair.Name} is '{pair.Restated}' here and '{pair.Declared}' in Domain. R17 would " +
                "then govern a namespace that does not exist, and report success over nothing."));

        ArchRule.Empty(
            offenders,
            "R17's subject and target sets are the ones the layering rule was written against (23 §6).");
    }

    /// <summary>
    /// 🔒 `30` §11.4 / `23` §6 — <b>R17 is a total order over the sub-namespaces of
    /// <c>Core.Rules</c>, so the set it orders must be the set that exists.</b> A fourth
    /// sub-namespace appearing with no declared edge is not
    /// a rule failure today and never becomes one: <see cref="ForbiddenEdges"/> is a hand-written
    /// whitelist of three ordered pairs, and a namespace named in none of them is governed by
    /// nothing, in either direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is not hypothetical, and the name is already written down.</b>
    /// <c>SubjectSetFloorTests</c>' <c>LuckService</c> entry (M4-01) says in as many words that the
    /// `05` §6.2 no-repeat seam's implementation must not live on <c>LuckService</c> itself <em>"or
    /// <c>Rules.Luck</c> ends up naming <c>Rules.Combat</c> and R17 has no edge for it"</em>. That
    /// sentence is a correct diagnosis of a hole this file did not close: with
    /// <c>Rules/Luck/</c> on disk, <see cref="Rules_Effects_is_the_bottom_of_the_intra_Rules_layering"/>
    /// quantifies over three namespaces that do not include it and stays green over the cycle.
    /// </para>
    /// <para>
    /// 🔒 <b>What this asserts, and what it deliberately does not.</b> It does not guess where a new
    /// namespace belongs — that is a milestone-level decision, exactly as M2-05 said of
    /// <c>IEffectActorView</c>. It asserts only that the decision was <em>taken</em>: every namespace
    /// directly beneath <c>Core.Rules</c> appears in <see cref="ForbiddenEdges"/> as a subject, as a
    /// forbidden target, or both. Adding <c>Rules/Luck/</c> therefore fails here, in a message that
    /// names the pairs to add, rather than passing silently two milestones from now.
    /// </para>
    /// <para>
    /// ⚠️ Stated over the namespaces that <b>hold a type</b>, not over the directories: an empty
    /// folder emits nothing into the assembly and there is nothing for an ordering to govern.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_namespace_under_Rules_has_a_declared_place_in_R17()
    {
        var governed = ForbiddenEdges
            .SelectMany(edge => new[] { edge.Subject, edge.Forbidden })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The namespaces that actually exist directly under Core.Rules, by the segment that follows
        // it — Il.TypesUnder is a PREFIX match, so a type in Rules.Combat.Bosses reports "Combat".
        var present = Il.TypesUnder(ProductionAssemblies.CoreModule, Domain.RulesNamespace)
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Select(t => Il.NamespaceOf(t))
            .Where(ns => ns.Length > Domain.RulesNamespace.Length + 1)
            .Select(ns => Domain.RulesNamespace + "." + ns[(Domain.RulesNamespace.Length + 1)..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToArray();

        var offenders = present
            .Where(ns => !governed.Contains(ns, StringComparer.Ordinal))
            .Select(ns =>
                $"{ns} holds types but appears in no R17 edge. The layering is a hand-written list of " +
                "ordered pairs, so a namespace named in none of them is unordered against all three " +
                "of Rules.Combat, Rules.Stats and Rules.Effects — a cycle through it passes " +
                "Rules_Effects_is_the_bottom_of_the_intra_Rules_layering with nothing going red. " +
                "Decide where it sits in `30` §11.4's Rules row and add its edges to ForbiddenEdges " +
                "in the same commit; do not widen this rule to ignore it.")
            .ToList();

        // S3 — the other direction. A governed namespace that holds nothing is a rule quantifying
        // over an empty set, which the floors above catch for the three named ones; this catches an
        // edge added for a namespace that was then never created.
        offenders.AddRange(
            governed
                .Where(ns => !present.Contains(ns, StringComparer.Ordinal))
                .Select(ns =>
                    $"{ns} is an R17 edge endpoint but no type is declared under it. The edge governs " +
                    "nothing. If the namespace was renamed, rename it here too."));

        ArchRule.Empty(
            offenders,
            "R17 orders every sub-namespace of Core.Rules that exists, and every namespace it orders " +
            "exists (30 §11.4, R17).");
    }

    /// <summary>
    /// 🔒 `23` §6 — the floors above are below the counts on the commit that wrote them, so none of
    /// them is already breached and reporting a false pass.
    /// </summary>
    /// <remarks>
    /// Steering S9, applied to this file's own numbers: a floor set <em>above</em> the real count
    /// fails loudly, but one set at a number nobody checked is a claim about the tree that was never
    /// verified. This asserts the headroom rather than the exact counts, so adding a type stays free.
    /// </remarks>
    [Fact]
    public void The_floors_are_below_the_counts_the_rule_was_written_against()
    {
        var offenders = new List<string>();

        foreach (var (ns, floor) in new[]
                 {
                     (EffectsNamespace, EffectsFloor),
                     (TriggersNamespace, TriggersFloor),
                     (StatsNamespace, StatsFloor),
                     (CombatNamespace, CombatFloor),
                 })
        {
            var found = Count(ns);

            if (found < floor)
            {
                offenders.Add($"{ns}: {found} types, floor {floor} — already breached");
            }
        }

        ArchRule.Empty(offenders, "Every floor in this file has headroom over the tree it was written against (23 §6).");
    }

    /// <summary>Types under <c>Rules/Effects/</c> and below, the whole `18` interpreter.</summary>
    private const int EffectsFloor = 12;

    /// <summary>Types declared directly in <c>Rules.Effects</c>, not in a sub-namespace.</summary>
    private const int EffectsRootFloor = 6;

    /// <summary>
    /// Types declared directly in <c>Rules.Combat</c>, not in <c>Bosses/</c>, <c>Enemies/</c> or
    /// <c>Status/</c> — `05` §3's loop, §4's pipeline, §4.1's ward pool and §7's log. 41 on the
    /// commit this floor landed; set well below so adding or removing one is not a test edit.
    /// </summary>
    private const int CombatRootFloor = 20;

    /// <summary>`18` §3's trigger model.</summary>
    private const int TriggersFloor = 8;

    /// <summary>`05` §1-2's stat block and `18` §8's aggregation.</summary>
    private const int StatsFloor = 4;

    /// <summary>`05` §7's combat log.</summary>
    private const int CombatFloor = 4;

    /// <summary>`18` §3's trigger model — floored separately. See the remarks above.</summary>
    internal const string TriggersNamespace = "SlayIdleRepeat.Core.Rules.Effects.Triggers";

    private static int Count(string ns) =>
        Il.TypesUnder(ProductionAssemblies.CoreModule, ns).Count(t => !Domain.IsCompilerGenerated(t));

    /// <summary>
    /// The floor over a namespace's <b>own</b> types, excluding everything in a sub-namespace.
    /// </summary>
    /// <remarks>
    /// <c>Il.TypesUnder</c> matches by prefix, so a floor stated with it is satisfied by any one
    /// sub-namespace. This is the companion that watches the root itself.
    /// </remarks>
    private static void RootFloor(List<string> offenders, string ns, int floor, string consequence)
    {
        var found = Il.TypesUnder(ProductionAssemblies.CoreModule, ns)
                      .Count(t => !Domain.IsCompilerGenerated(t) &&
                                  Il.NamespaceOf(t).Equals(ns, StringComparison.Ordinal));

        if (found < floor)
        {
            offenders.Add(
                $"types declared directly in {ns}: found {found}, floor is {floor}. {consequence} " +
                "If this shrank on purpose, lower the floor in the same commit and say why.");
        }
    }

    private static void Floor(List<string> offenders, string ns, int floor, string consequence)
    {
        var found = Count(ns);

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
