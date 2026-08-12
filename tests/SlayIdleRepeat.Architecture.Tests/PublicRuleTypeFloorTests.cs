using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `30` §11.2 / `23` §6 — the rules that watch the <b>exemption arm</b> of
/// <c>AccessibilityBoundaryTests.Handlers_and_Rules_are_internal</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Handlers_and_Rules_are_internal</c> is <em>"every public type under <c>Rules/</c> or
/// <c>Handlers/</c> is in <c>Domain.PublicRuleTypes</c>"</em>. That rule is not vacuous — it
/// quantifies over every type in both namespaces — but its exemption arm is a hard-coded list of
/// simple names, and a list of simple names has two silent failure modes that the rule itself cannot
/// see, because both make it <em>stricter</em> rather than quieter:
/// </para>
/// <list type="number">
///   <item>
///     <b>A name in the list that resolves to an <c>internal</c> type.</b> R15/R16 widened the list
///     from two names to six, and the widening had a second half: making those types public. If half
///     the commit had landed, the list would carry four names that exempt nothing, and the next
///     person to read it would believe `05` §7's replay types were already exported. This is the
///     failure mode that would have happened to <b>this</b> commit.
///   </item>
///   <item>
///     <b>A name in the list that resolves to nothing at all</b> — a rename. That is
///     <c>SubjectSetFloorTests</c>' subject, but only for the names it happens to track; the floor
///     below covers the list as a whole, so a name added later without an entry there is still
///     watched.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>The rule is stated over the names that <em>exist</em>, and that is deliberate.</b>
/// <c>PowerCalculator</c> is in <c>Domain.PublicRuleTypes</c> and is declared pending in
/// <c>SubjectSetFloorTests</c> (M2-07 owns <c>Rules/Stats/</c>, and the type is not there yet).
/// Requiring every listed name to exist would duplicate that register in a second file with a
/// different expiry — S4's <em>"one mechanism per repo"</em>. What is asserted here is the other
/// half, which nothing else asserts: a listed name that <b>does</b> resolve names a <b>public</b>
/// type.
/// </para>
/// </remarks>
public sealed class PublicRuleTypeFloorTests
{
    /// <summary>
    /// How many of <c>Domain.PublicRuleTypes</c> must resolve to a real <c>Core</c> type.
    /// </summary>
    /// <remarks>
    /// Five on the commit R16 landed: <c>CombatSimulator</c>, <c>SimulationResult</c>,
    /// <c>CombatEvent</c>, <c>CombatEventType</c> and <c>ActorStats</c>. <c>PowerCalculator</c> is the
    /// sixth and does not exist yet. A floor rather than an equality so that landing it is not a test
    /// edit; lowering this one is a deliberate decision that belongs in the same commit as the
    /// deletion forcing it.
    /// </remarks>
    private const int ResolvedPublicRuleTypeFloor = 5;

    /// <summary>
    /// 🔒 `30` §11.2 — every name in <c>Domain.PublicRuleTypes</c> that resolves to a <c>Core</c>
    /// type names a <b>public</b> one, so the exemption arm of
    /// <c>Handlers_and_Rules_are_internal</c> exempts something.
    /// </summary>
    [Fact]
    public void Every_declared_public_rule_type_that_exists_is_actually_public()
    {
        var offenders = new List<string>();

        foreach (var name in Domain.PublicRuleTypes)
        {
            var type = Domain.FindInCore(name);
            if (type is null)
            {
                continue;
            }

            if (!type.IsPublic)
            {
                offenders.Add(
                    $"'{name}' is in Domain.PublicRuleTypes but {type.FullName} is not public. The list is " +
                    "the EXEMPTION arm of Handlers_and_Rules_are_internal, so an internal type in it exempts " +
                    "nothing and the rule silently gets stricter instead of failing. R15/R16 widened the " +
                    "list to the signature closure of CombatSimulator.Simulate and that widening had two " +
                    "halves — the names here, and the `public` keyword on the types. This is the half that " +
                    "goes unnoticed.");

                continue;
            }

            if (!Il.IsUnder(Il.NamespaceOf(type), Domain.RulesNamespace) &&
                !Il.IsUnder(Il.NamespaceOf(type), Domain.HandlersNamespace))
            {
                offenders.Add(
                    $"'{name}' is in Domain.PublicRuleTypes but {type.FullName} is in " +
                    $"{Il.NamespaceOf(type)}, which is under neither {Domain.RulesNamespace} nor " +
                    $"{Domain.HandlersNamespace}. Handlers_and_Rules_are_internal only ever looks at those " +
                    "two, so the entry governs nothing — and a type that moved out of Rules/ took its " +
                    "`public` with it, unreviewed.");
            }
        }

        ArchRule.Empty(
            offenders,
            "Every name in Domain.PublicRuleTypes that exists names a public type under Rules/ or " +
            "Handlers/ (30 §11.2, R15/R16).");
    }

    /// <summary>
    /// `23` §6 — the exemption list still reaches real types. An empty or shrunken one means the
    /// widening R16 authorised has been undone by a rename, with nothing going red.
    /// </summary>
    [Fact]
    public void The_public_rule_type_list_still_reaches_the_types_it_was_written_against()
    {
        var resolved = Domain.PublicRuleTypes.Count(n => Domain.FindInCore(n) is not null);

        var offenders = new List<string>();

        if (resolved < ResolvedPublicRuleTypeFloor)
        {
            offenders.Add(
                $"Domain.PublicRuleTypes resolves {resolved} of {Domain.PublicRuleTypes.Count} names to a " +
                $"Core type; the floor is {ResolvedPublicRuleTypeFloor}. R16 enumerated the signature " +
                "closure of CombatSimulator.Simulate and the list is the only place that closure is " +
                "written down — a rename that empties it takes the exemption arm of " +
                "Handlers_and_Rules_are_internal with it. If this shrank on purpose, lower the floor in " +
                "the same commit and say why in the message.");
        }

        // 🔒 R16: ENUMERATED, never a blanket "anything reachable from a public type". The count is
        // pinned from above as well as below, so a third public entry point — or a closure that grew
        // because somebody widened a signature — is a deliberate edit to this number and not a silent
        // drift. `30` §11.2's two entry points plus `05` §7's four signature types is six.
        if (Domain.PublicRuleTypes.Count > 6)
        {
            offenders.Add(
                $"Domain.PublicRuleTypes now holds {Domain.PublicRuleTypes.Count} names. R16 is explicit " +
                "that the widening is the ENUMERATED signature closure of CombatSimulator.Simulate and " +
                "'never a blanket anything-reachable-from-a-public-type rule', so that a third public " +
                "entry point stays a deliberate decision in a diff. Adding one is allowed — raise this " +
                "number in the same commit and name the entry point and its consumer, the way 30 §11.2 " +
                "names 14 §2.4 and 29 §1.");
        }

        ArchRule.Empty(
            offenders,
            "Domain.PublicRuleTypes is the enumerated closure it was written as, and it still resolves " +
            "(23 §6, R16).");
    }
}
