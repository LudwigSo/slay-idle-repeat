using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `27` §1 / `07` §1 — the <b>one door</b> a hero name comes through: only the name rule
/// constructs a <c>HeroName</c>, and only a <c>HeroName</c> reaches the aggregate's field.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this needs a rule.</b> `27` §1's filter is stated as "at creation <b>and on every
/// edit</b>", and the design that delivers it is a type nobody but the rule can produce:
/// <c>Player.Rename</c> takes a <c>HeroName</c> and there is no <c>Rename(string)</c> beside it.
/// Three files assert that as the load-bearing reason the guarantee holds — and the constructor is
/// <c>internal</c>, not private, so <em>every type in <c>Core</c></em> can call
/// <c>new HeroName("…")</c>. The claim was true about the code and false about the type. Every
/// comparable choke point in this repository already has an IL rule over it
/// (<c>FeatCounterWritePathRuleTests</c> is the model, and says so).
/// </para>
/// <para>
/// 🔒 A <b>new file</b> rather than an edit to the M0-authored rule files, on
/// <c>FeatCounterWritePathRuleTests</c>' and <c>IntraRulesLayeringRuleTests</c>' precedent: several
/// M4 lanes are in flight on the shared rule files, and patching a component another agent is
/// scheduled to touch is steering <b>S12</b>'s failure mode.
/// </para>
/// <para>
/// ⚠️ <b>What it does not close.</b> A <c>newobj</c> is visible to an IL scan, so steering S18's
/// <c>const</c>-invisibility does not apply here — but the scan cannot tell whether the rule's own
/// checks are the <em>right</em> checks, and it says nothing about a name set through persistence.
/// It closes exactly one direction: a second constructor call site.
/// </para>
/// </remarks>
public sealed class HeroNameWritePathRuleTests
{
    /// <summary>The validated-name value object. Public getter, <c>internal</c> constructor.</summary>
    private const string HeroNameType = "HeroName";

    /// <summary>The one type `27` §1's filter lives in, and the only one allowed to mint a name.</summary>
    private const string SanctionedMinter = "SlayIdleRepeat.Core.Rules.Hero.HeroNameRule";

    /// <summary>The rule's two entry points — the door a name must come through.</summary>
    private static readonly string[] FilterEntryPoints = ["Validate", "Default"];

    /// <summary>
    /// `27` §1 — one minter, named by identity, with a floor under the subject set.
    /// </summary>
    [Fact]
    public void Only_the_name_rule_constructs_a_HeroName()
    {
        var minters = ConstructorsOf(HeroNameType).ToArray();

        // S3 — an IDENTITY floor rather than a count. A rule stated as "no unsanctioned minter" is
        // satisfied forever by a scan that finds no minter at all, which is exactly the state it
        // would be in if HeroName were renamed or the rule stopped producing one.
        minters.ShouldNotBeEmpty(
            "nothing constructs a HeroName. Either the name rule stopped producing one — in which " +
            "case 27 §1's filter has no output and Player.Rename can never be called — or the type " +
            "was renamed and this rule is scanning for a name that does not exist.");

        minters.Select(m => m.DeclaringType.FullName).ShouldContain(
            SanctionedMinter,
            "HeroNameRule is where a name is checked and therefore where one is minted. If it no " +
            "longer constructs a HeroName, the filter moved somewhere this rule has to sanction " +
            "deliberately.");

        ArchRule.Empty(
            minters
                .Where(m => !m.DeclaringType.FullName.Equals(SanctionedMinter, StringComparison.Ordinal))
                .Select(m =>
                    $"{Il.Describe(m)} constructs a HeroName. 27 §1 filters a name at creation AND " +
                    "on every edit, and the only thing that makes 'every edit' true is that a name " +
                    "cannot be built without passing the filter. A second construction site is a " +
                    "second way into Player.DisplayName, and it is the one that skips the word " +
                    "lists. Call HeroNameRule.Validate and use the name it answers."),
            "Only the name rule constructs a HeroName (27 §1, 07 §1).");
    }

    /// <summary>
    /// 🔒 `27` §1 — <b>a self-expiring witness for a deferral that has none.</b> Nothing in
    /// production calls the name filter yet, and the day something does, this fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `14` §2.3's registry is exhaustive and authors no rename command, so a hero name is set when
    /// an account is created — and no account-creation path exists. <c>GapRegister</c> carries that
    /// as a named-owner comment (M5-06) because the register keys on a TYPE and the gap is a missing
    /// CALLER, and a comment on its own is what steering <b>S4</b> exists to refuse: nothing would
    /// fail when it stopped being true.
    /// </para>
    /// <para>
    /// This is that failing witness, and it expires by being <em>satisfied</em>: the commit that
    /// wires account creation to <c>HeroNameRule</c> turns it red, and the fix is to delete both this
    /// rule and the <c>GapRegister</c> note in that commit. It is deliberately stated in the
    /// direction that goes red on progress rather than on regression — which is the only direction
    /// available for "this is not wired up yet".
    /// </para>
    /// <para>
    /// ⚠️ Scoped to <c>src/</c> only, and one production path writes <c>DisplayName</c> without the
    /// filter: <c>Player.CreateStarting</c>, which takes a plain string and stores it as given, and
    /// records that limit in its own remarks. Both the domain harness and the in-process host build
    /// their starting row through it, and neither hands it player-chosen text — the harness runs on
    /// hermetic content sets that carry no word lists, and the host names the profile after the
    /// identity it minted. 🔴 Neither scan below can see that parameter: a caller that passed
    /// player-chosen text to it would set an unfiltered name with both rules still green. What the
    /// second scan sees is the filter GAINING a caller, which is the other direction.
    /// </para>
    /// </remarks>
    [Fact]
    public void Nothing_in_production_calls_the_name_filter_yet_and_this_fails_when_something_does()
    {
        var callers = FilterEntryPoints
            .SelectMany(entry => CallersOf(SanctionedMinter, entry))
            .Where(caller => !caller.DeclaringType.FullName.Equals(SanctionedMinter, StringComparison.Ordinal))
            .ToArray();

        ArchRule.Empty(
            callers.Select(caller =>
                $"{Il.Describe(caller)} calls the hero name filter. 🎉 THIS IS GOOD NEWS AND THE " +
                "RULE IS DOING ITS JOB: it exists only to fail on the commit that finally wires " +
                "27 §1's filter to a real caller. Delete this test AND GapRegister's named-owner " +
                "note about the unwired filter in the same commit — the deferral has been " +
                "discharged, and an exemption that outlives what it excused is steering S4's " +
                "failure mode."),
            "Nothing in production calls the hero name filter yet (27 §1; owner M5-06).");
    }

    /// <summary>
    /// `23` §6 — the teeth: both scans distinguish a real site from an absent one, in both
    /// directions.
    /// </summary>
    /// <remarks>
    /// Without this, a matcher broken in either scan would report "no unsanctioned minter" and
    /// "nothing calls the filter" forever — and the second of those reads as the very state it is
    /// asserting, which is the worst possible way for a rule to be wrong.
    /// </remarks>
    [Fact]
    public void Both_scans_find_a_real_site_and_none_for_an_absent_one()
    {
        ConstructorsOf("GearInstance").ShouldNotBeEmpty(
            "Inventory rebuilds a locked item through the internal constructor, so a scan that " +
            "finds nothing here is matching on something other than the type name and the minter " +
            "rule above is silent by construction.");

        ConstructorsOf("ATypeCoreDoesNotDeclare").ShouldBeEmpty(
            "if this finds construction sites the matcher is not comparing type names at all.");

        CallersOf("SlayIdleRepeat.Core.Rules.Hero.LoadoutRules", "IsEquippable").ShouldNotBeEmpty(
            "LoadoutRules.Applied and the ApplyPreset handler both call it, so a scan that finds " +
            "nothing here is matching on something other than the declaring type and the method " +
            "name — and the caller rule above would then be asserting its own blind spot.");

        CallersOf(SanctionedMinter, "AMethodTheRuleDoesNotDeclare").ShouldBeEmpty(
            "if this finds callers the matcher is not comparing method names at all.");
    }

    /// <summary>Every method in <c>Core</c> whose body constructs the named type.</summary>
    private static IEnumerable<MethodDefinition> ConstructorsOf(string typeName) =>
        Il.MethodsWithBodies(ProductionAssemblies.CoreModule)
            .Where(method => Il.Instructions(method).Any(instruction =>
                instruction.OpCode == OpCodes.Newobj &&
                instruction.Operand is MethodReference constructed &&
                constructed.DeclaringType.Name.Equals(typeName, StringComparison.Ordinal)));

    /// <summary>Every method in <c>Core</c> whose body calls <c>&lt;declaringType&gt;.&lt;methodName&gt;</c>.</summary>
    /// <remarks>
    /// Both call opcodes, for <c>FeatCounterWritePathRuleTests</c>' recorded reason: C# emits
    /// <c>callvirt</c> for an instance method on a reference type even when it is not virtual, and a
    /// <c>Call</c>-only scan sees none of those.
    /// </remarks>
    private static IEnumerable<MethodDefinition> CallersOf(string declaringType, string methodName) =>
        Il.MethodsWithBodies(ProductionAssemblies.CoreModule)
            .Where(method => Il.Instructions(method).Any(instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference called &&
                called.Name.Equals(methodName, StringComparison.Ordinal) &&
                called.DeclaringType.FullName.Equals(declaringType, StringComparison.Ordinal)));
}
