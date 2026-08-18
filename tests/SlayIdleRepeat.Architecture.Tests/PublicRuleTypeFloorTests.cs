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
/// ⚠️ <b>The rule is stated over the names that <em>exist</em>, and that is deliberate.</b> When it
/// was written, <c>PowerCalculator</c> was in <c>Domain.PublicRuleTypes</c> and declared pending in
/// <c>SubjectSetFloorTests</c>; requiring every listed name to exist would have duplicated that
/// register in a second file with a different expiry — S4's <em>"one mechanism per repo"</em>. What
/// is asserted here is the other half, which nothing else asserts: a listed name that <b>does</b>
/// resolve names a <b>public</b> type.
/// </para>
/// <para>
/// 🔒 <b>M2-16a landed <c>PowerCalculator</c> and discharged that pending entry, so all six names
/// now resolve</b> and <see cref="ResolvedPublicRuleTypeFloor"/> was raised to match. The split of
/// responsibilities is unchanged — <c>SubjectSetFloorTests</c> is still the one register — but the
/// floor no longer carries a unit of slack it was never meant to keep.
/// </para>
/// </remarks>
public sealed class PublicRuleTypeFloorTests
{
    /// <summary>
    /// How many of <c>Domain.PublicRuleTypes</c> must resolve to a real <c>Core</c> type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five on the commit R16 landed: <c>CombatSimulator</c>, <c>SimulationResult</c>,
    /// <c>CombatEvent</c>, <c>CombatEventType</c> and <c>ActorStats</c>. A floor rather than an
    /// equality so that landing one is not a test edit; lowering this one is a deliberate decision
    /// that belongs in the same commit as the deletion forcing it.
    /// </para>
    /// <para>
    /// 🔒 <b>Six since M2-16a landed <c>PowerCalculator</c>, and raising it is the point.</b> The
    /// number is the count of names that must <em>resolve</em>, and while it sat at five with six
    /// names resolving it had a permanent unit of slack: `29` §1's power readout could be renamed or
    /// deleted, taking its exemption in <c>Handlers_and_Rules_are_internal</c> with it, and every
    /// rule in this file would still pass. A floor with headroom over the tree it guards is not a
    /// floor — steering S9, and the same argument
    /// <c>IntraRulesLayeringRuleTests.The_floors_are_below_the_counts_the_rule_was_written_against</c>
    /// makes about its own numbers, applied in the direction that had gone stale.
    /// </para>
    /// <para>
    /// 🔒 <b>Eleven since M7-05b widened the list to <c>BoardView</c> and its signature closure</b>
    /// — <c>BoardTrackNode</c>, <c>BoardFork</c>, <c>TileKind</c> and <c>ForkLabel</c>. Raised in the
    /// same commit as the widening, and by exactly the number of names it adds, for the reason the
    /// paragraph above gives: a floor left at six over an eleven-name list carries five units of
    /// slack, and `03` §1.1's whole board projection could be deleted — taking the Board screen's
    /// only way to see a tile track with it — with every rule in this file still green.
    /// </para>
    /// <para>
    /// 🔒 <b>Thirteen since M7-06b widened the list to <c>HeroBuild</c> and <c>RunBattle</c></b> —
    /// two entry points that add no signature type of their own, so the floor rises by exactly two.
    /// Raised in the same commit as the widening, for the reason above: a floor left at eleven over a
    /// thirteen-name list would let the hero's stat block and the run-to-fight composition both be
    /// deleted — and with them the only thing that lets a run leave <c>BattlePending</c> — with every
    /// rule in this file still green.
    /// </para>
    /// <para>
    /// 🔒 <b>Seventeen since M7-07 widened the list to <c>DraftView</c> and <c>ShrineView</c></b> —
    /// with <c>DraftOptionView</c> and <c>ShrineBuffRow</c>, their two return shapes. Raised in the
    /// same commit and by exactly the number of names it adds, for the reason the two paragraphs
    /// above give: the perk draft's and the shrine's projections are the only way the run-decision
    /// screens can see an offer that is never persisted, and a floor left at thirteen would let both
    /// be deleted with every rule in this file still green.
    /// </para>
    /// <para>
    /// 🔒 <b>Nineteen since M7-07's UI review added <c>DraftGuaranteeView</c> and
    /// <c>DraftGuaranteeKind</c></b> — no new entry point, two more shapes reached through
    /// <c>DraftView.Guarantees</c>. Raised in the same commit and by exactly the two names it adds.
    /// The slack this closes is the one that matters most of the three: <c>24</c> §1.1's Visibility
    /// rule is a 🔒 and its Disclosure rule is a store-policy requirement on both platforms, so a
    /// floor left at seventeen would let the only public reading of the <c>DRAFT</c> counters be
    /// deleted — silently returning S07 to the hidden-pity state that rule exists to forbid — with
    /// every rule in this file still green.
    /// </para>
    /// </remarks>
    private const int ResolvedPublicRuleTypeFloor = 19;

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
            // 🔒 Every type with that simple name, not the first. `Domain.FindInCore` is a
            // `FirstOrDefault` while `Handlers_and_Rules_are_internal`'s exemption arm is a `Contains`
            // over the same list — so it exempts EVERY type with the name. A second
            // `Rules/Board/CombatEvent` would be exempted from the internal rule while a
            // first-match guard checked only one of the two, and the guard would verify less than
            // the rule it guards.
            var matches = Domain.CoreTypes
                .Where(t => t.Name.Equals(name, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length > 1)
            {
                offenders.Add(
                    $"'{name}' resolves to {matches.Length} Core types ({string.Join(", ", matches.Select(t => t.FullName))}). " +
                    "Handlers_and_Rules_are_internal exempts every type with a listed name, so a second one " +
                    "is silently public too — and Domain.FindInCore, which the rest of the suite looks " +
                    "subjects up with, would only ever see the first.");
            }

            foreach (var type in matches)
            {
                Check(offenders, name, type);
            }
        }

        // 🔒 A public type NESTED in a public Rules/ type is exempt from
        // Handlers_and_Rules_are_internal entirely: its filter is `DeclaringType is null` plus
        // `IsPublic`, and Cecil reports a nested public type as IsNestedPublic with IsPublic false.
        // That gap was unreachable until this commit, because there were no public types under
        // Rules/ to nest inside. There are five now, so it is closed here rather than by editing
        // M0-08's rule file.
        offenders.AddRange(
            Domain.CoreTypes
                .Where(t => t.IsNestedPublic && t.DeclaringType is not null)
                .Where(t => Il.IsUnder(Il.NamespaceOf(t), Domain.RulesNamespace) ||
                            Il.IsUnder(Il.NamespaceOf(t), Domain.HandlersNamespace))
                .Where(t => !Domain.IsCompilerGenerated(t))
                .Select(t =>
                    $"{t.FullName} is a public type nested in a Rules/ or Handlers/ type. " +
                    "Handlers_and_Rules_are_internal cannot see it — its filter is `DeclaringType is null` " +
                    "and Cecil reports a nested public type as IsNestedPublic, not IsPublic — so it is " +
                    "publicly reachable and governed by nothing. 30 §11.2's public surface is an " +
                    "enumerated list of top-level types; make it internal, or lift it out and enumerate it."));

        ArchRule.Empty(
            offenders,
            "Every name in Domain.PublicRuleTypes that exists names a public type under Rules/ or " +
            "Handlers/, and no public type hides inside one (30 §11.2, R15/R16).");

        static void Check(List<string> offenders, string name, Mono.Cecil.TypeDefinition type)
        {
            if (!type.IsPublic)
            {
                offenders.Add(
                    $"'{name}' is in Domain.PublicRuleTypes but {type.FullName} is not public. The list is " +
                    "the EXEMPTION arm of Handlers_and_Rules_are_internal, so an internal type in it exempts " +
                    "nothing and the rule silently gets stricter instead of failing. R15/R16 widened the " +
                    "list to the signature closure of CombatSimulator.Simulate and that widening had two " +
                    "halves — the names here, and the `public` keyword on the types. This is the half that " +
                    "goes unnoticed.");

                return;
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
    }

    /// <summary>
    /// 🔒 `30` §11.2 — a public entry point is <b>callable</b>: every parameter type it declares that
    /// lives in <c>Core</c> offers a public way to build one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this closes is subtle and would have passed every other rule in the suite. R15's
    /// warrant for exporting these types is `30` §11.2's two named external consumers — `14` §2.4's
    /// client and `05` §9's balance harness, which is a <b>separate assembly</b> with no
    /// <c>InternalsVisibleTo</c> grant. Export the type but leave its factory internal, and the
    /// public method compiles, the accessibility rules go green, and no outside assembly can call it:
    /// a public API in name only.
    /// </para>
    /// <para>
    /// ⚠️ Stated over <c>Core</c>'s own types only. A BCL parameter (<c>ulong</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>) is not this rule's business.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_public_entry_points_parameters_can_be_built_from_outside_Core()
    {
        var offenders = new List<string>();

        var declared = Domain.CoreTypes
            .Where(t => t.IsPublic && Domain.PublicRuleTypes.Contains(t.Name, StringComparer.Ordinal))
            .ToArray();

        // S3 — the subject set, floored. Without this the rule passes over an empty set the moment a
        // rename empties PublicRuleTypes, which is the silence this whole file exists to break.
        if (declared.Length < ResolvedPublicRuleTypeFloor)
        {
            offenders.Add(
                $"only {declared.Length} of Domain.PublicRuleTypes' names resolve to a public Core type; " +
                $"the floor is {ResolvedPublicRuleTypeFloor}. This rule would be quantifying over almost " +
                "nothing.");
        }

        foreach (var type in declared)
        {
            foreach (var method in type.Methods.Where(m => m.IsPublic && !Domain.IsCompilerGenerated(m)))
            {
                foreach (var parameter in method.Parameters)
                {
                    foreach (var reference in Il.Flatten(parameter.ParameterType))
                    {
                        var resolved = Domain.CoreTypes.FirstOrDefault(
                            t => t.FullName.Equals(reference.FullName, StringComparison.Ordinal));

                        if (resolved is null || !resolved.IsPublic || CanBeBuiltFromOutside(resolved))
                        {
                            continue;
                        }

                        offenders.Add(
                            $"{Il.Describe(method)} takes {resolved.FullName}, which is public but offers no " +
                            "public constructor and no public static factory returning itself. `30` §11.2 " +
                            "exports this entry point for a named external consumer — `05` §9's balance " +
                            "harness is its own assembly with no InternalsVisibleTo grant — and it cannot " +
                            "construct the argument. Make the factory public, or make the entry point " +
                            "internal and take its name out of Domain.PublicRuleTypes.");
                    }
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "Every public Rules entry point can actually be called from outside Core (30 §11.2, R15).");

        static bool CanBeBuiltFromOutside(Mono.Cecil.TypeDefinition type) =>
            type.IsEnum ||
            type.IsValueType ||
            type.Methods.Any(m => m.IsPublic && m.IsConstructor) ||
            type.Methods.Any(m =>
                m.IsPublic && m.IsStatic &&
                m.ReturnType.FullName.Equals(type.FullName, StringComparison.Ordinal));
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
                "closures of the public entry points — CombatSimulator.Simulate, and M7-05b's " +
                "BoardView — and the list is the only place they are written down; a rename that " +
                "empties it takes the exemption arm of " +
                "Handlers_and_Rules_are_internal with it. If this shrank on purpose, lower the floor in " +
                "the same commit and say why in the message.");
        }

        // 🔒 R16: ENUMERATED, never a blanket "anything reachable from a public type". The count is
        // pinned from above as well as below, so a fourth public entry point — or a closure that grew
        // because somebody widened a signature — is a deliberate edit to this number and not a silent
        // drift. `30` §11.2's two entry points plus `05` §7's four signature types was six.
        //
        // 🔒 ELEVEN since M7-05b. It added ONE entry point — BoardView, whose consumer is the client's
        // Board screen (S05) — plus the four types its public members name: BoardTrackNode, BoardFork,
        // TileKind and ForkLabel. The cap is raised by exactly those five and no further, so the next
        // person who wants a sixth board type has to say so in a diff; that is the whole mechanism,
        // and it is what keeps BoardGenerator and BoardGraph out of the list by cost rather than by
        // good intentions.
        //
        // 🔒 THIRTEEN since M7-06b. It added TWO entry points — HeroBuild, whose consumer is the
        // client's Hero and Inventory screens, and RunBattle, whose consumer is the Application
        // layer's SimulatePendingBattleUseCase — and NO signature types, because every type their
        // public members name was already public. The cap rises by exactly two: the derivation's own
        // machinery (StatAggregation, HeroBaseCurve, GearStatDerivation, EncounterFight, BossFight)
        // stays internal, and HeroBattleSurfaceRuleTests is what holds it there.
        //
        // 🔒 SEVENTEEN since M7-07. It added TWO entry points — DraftView, whose consumer is the
        // client's Perk Draft screen, and ShrineView, whose consumer is the Shrine arm of the
        // campfire/shrine screen — plus the two types their public members name, DraftOptionView and
        // ShrineBuffRow. Raised by exactly those four and no further, so PerkDraftEngine and
        // ShrineResolver stay out of the list by cost rather than by good intentions.
        //
        // 🔒 NINETEEN since M7-07's UI review. It added NO entry point: DraftView.Guarantees is a new
        // member on a list member, and DraftGuaranteeView and DraftGuaranteeKind are the two types it
        // names. The consumer is the same Perk Draft screen (S07) and the authority is 24 §1.1, which
        // requires every luck-protection counter shown always with a real number and every N in §4
        // stated on its class's own screen. Raised by exactly those two and no further, so
        // DraftGuarantees, DraftCounters, DraftDemand, DraftForce and HardPity stay out: what is
        // exported is where the counters STAND, never the machinery that floors a slot.
        if (Domain.PublicRuleTypes.Count > 19)
        {
            offenders.Add(
                $"Domain.PublicRuleTypes now holds {Domain.PublicRuleTypes.Count} names. R16 is explicit " +
                "that the widening is the ENUMERATED signature closure of a public entry point and " +
                "'never a blanket anything-reachable-from-a-public-type rule', so that a fourth public " +
                "entry point stays a deliberate decision in a diff. Adding one is allowed — raise this " +
                "number in the same commit and name the entry point and its consumer, the way 30 §11.2 " +
                "names 14 §2.4 and 29 §1 and the way M7-05b names the Board screen.");
        }

        ArchRule.Empty(
            offenders,
            "Domain.PublicRuleTypes is the enumerated closure it was written as, and it still resolves " +
            "(23 §6, R16).");
    }
}
