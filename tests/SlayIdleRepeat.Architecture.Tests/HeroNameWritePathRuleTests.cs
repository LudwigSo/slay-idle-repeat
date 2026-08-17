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
    /// ⚠️ Scoped to <c>src/</c> only. <b>The M4 review closed the hole this paragraph used to
    /// admit.</b> It read: <em>"one production path writes <c>DisplayName</c> without the filter —
    /// <c>Player.CreateStarting</c>, which takes a plain string… 🔴 Neither scan below can see that
    /// parameter: a caller that passed player-chosen text to it would set an unfiltered name with
    /// both rules still green."</em> That was the honest statement of a real gap, on the one path
    /// `27` §1 names by name ("at creation and on every edit"), and it is now shut:
    /// <c>CreateStarting</c> takes a <c>HeroName</c>, so it cannot be handed text the filter
    /// has not seen.
    /// </para>
    /// <para>
    /// The two callers that name an account after something the player never typed went to doors of
    /// their own: <c>CreateStartingNamedAfterItsOwnId</c>, which takes <em>no name parameter at
    /// all</em>, and <c>CreateStartingWithUnfilteredName</c>, which says so in its name. The second
    /// is the one that still writes an unfiltered name — but it is a named method rather than a
    /// parameter, so its callers are enumerable, and
    /// <see cref="Only_the_harness_uses_the_explicitly_unfiltered_starting_door"/> enumerates them
    /// across <c>Core</c> <em>and</em> <c>Application</c>.
    /// </para>
    /// <para>
    /// 🔴 <b>What is still not closed.</b> Nothing stops a future caller of the unfiltered door
    /// beyond that rule's own sanctioned list, and no scan here says a name set through persistence
    /// was ever filtered — <c>Rehydrate</c> loads a stored name as written, deliberately (see
    /// <c>HeroNameRule</c>'s remarks on content changing without a build).
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

    /// <summary>The starting-account door that stores a name <b>nothing filtered</b>.</summary>
    private const string UnfilteredStartingDoor = "CreateStartingWithUnfilteredName";

    /// <summary>The aggregate that declares it.</summary>
    private const string PlayerType = "SlayIdleRepeat.Core.Model.Player";

    /// <summary>
    /// The types allowed to call it, with why. A closed list with a named owner per row — the shape
    /// <c>GapRegister</c> and <c>RoutingExemptions</c> use.
    /// </summary>
    /// <remarks>
    /// Exactly one row. The in-process host is deliberately <b>not</b> on it: it names a profile
    /// after the identity it minted, which is <c>CreateStartingNamedAfterItsOwnId</c>'s job, and a
    /// host that reached for this door instead would be storing a name from a path that could carry
    /// player text tomorrow.
    /// </remarks>
    private static readonly (string Type, string Why)[] SanctionedUnfilteredCallers =
    [
        ("SlayIdleRepeat.Core.Testing.InMemoryGame",
            "the 30 §6 domain harness. It labels fixture players ('Ludwig the Unhurried') and runs " +
            "on hermetic content sets that carry no word lists at all, so there is no filter for it " +
            "to pass and nothing player-chosen for it to filter. Owner: M5-06, with the rest of the " +
            "account-creation wiring."),
    ];

    /// <summary>
    /// 🔒 `27` §1 — the door that skips the filter has a <b>closed, reasoned caller list</b>, scanned
    /// across <c>Core</c> and <c>Application</c> both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the rule that makes splitting the old <c>CreateStarting(string)</c> worth doing. The
    /// unfiltered write did not disappear — the harness still needs it — but it stopped being an
    /// invisible <em>parameter</em> on the method every caller reaches for and became a named method
    /// whose callers an IL scan can enumerate. A new caller now has to argue for a row here.
    /// </para>
    /// <para>
    /// S3: the subject set is floored on the sanctioned caller being <em>present</em>, not on a
    /// count — a scan that matched nothing would otherwise report "no unsanctioned caller" forever,
    /// which is indistinguishable from the door having been deleted.
    /// </para>
    /// </remarks>
    [Fact]
    public void Only_the_harness_uses_the_explicitly_unfiltered_starting_door()
    {
        var callers = CallersOf(PlayerType, UnfilteredStartingDoor)
            .Concat(CallersOf(ProductionAssemblies.ApplicationModule, PlayerType, UnfilteredStartingDoor))
            .Where(caller => !caller.DeclaringType.FullName.Equals(PlayerType, StringComparison.Ordinal))
            .ToArray();

        callers.Select(c => c.DeclaringType.FullName).ShouldContain(
            SanctionedUnfilteredCallers[0].Type,
            $"nothing calls Player.{UnfilteredStartingDoor}. Either the harness stopped using it — " +
            "in which case delete the door and this rule in the same commit, because an unfiltered " +
            "write path nobody needs is one nobody should be able to reach — or it was renamed and " +
            "this rule is scanning for a method that does not exist.");

        ArchRule.Empty(
            callers
                .Where(caller => !SanctionedUnfilteredCallers.Any(row =>
                    row.Type.Equals(caller.DeclaringType.FullName, StringComparison.Ordinal)))
                .Select(caller =>
                    $"{Il.Describe(caller)} calls Player.{UnfilteredStartingDoor}, which stores a " +
                    "display name 27 §1's filter has never seen. 27 §1 filters a name at creation " +
                    "AND on every edit; this door exists for the one caller that has no filter " +
                    "available and no player-chosen text to put through it. If yours has player " +
                    "text, call Player.CreateStarting with the HeroName the filter answers. If it " +
                    "is naming an account after an identity, call " +
                    "Player.CreateStartingNamedAfterItsOwnId, which takes no name at all. If it is " +
                    "genuinely neither, add a row to SanctionedUnfilteredCallers with the reason " +
                    "and the milestone that removes it."),
            $"Only the domain harness calls Player.{UnfilteredStartingDoor} (27 §1, 07 §1).");
    }

    /// <summary>
    /// `23` §6 — the teeth on the rule above: the two-module scan finds a real site and none for an
    /// absent one.
    /// </summary>
    /// <remarks>
    /// The <c>Application</c> half is the one that needs proving — the door is <c>public</c>
    /// precisely so the harness does not breach `30` §6's public-seam rule, which means
    /// <c>Application</c> <em>can</em> reach it, which is the direction a <c>Core</c>-only scan would
    /// miss entirely.
    /// </remarks>
    [Fact]
    public void The_two_module_scan_sees_Application_as_well_as_Core()
    {
        CallersOf(ProductionAssemblies.ApplicationModule, PlayerType, "CreateStartingNamedAfterItsOwnId")
            .ShouldNotBeEmpty(
                "InProcessGameHost.OpenProfileAsync builds its starting row through this door, so a " +
                "scan that finds nothing in Application is not reading that module at all — and the " +
                "unfiltered-caller rule above would then be blind to every host, adapter and client " +
                "that could reach the public door it guards.");

        CallersOf(ProductionAssemblies.ApplicationModule, PlayerType, "AMethodPlayerDoesNotDeclare")
            .ShouldBeEmpty("if this finds callers the matcher is not comparing method names at all.");
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
        CallersOf(ProductionAssemblies.CoreModule, declaringType, methodName);

    /// <summary>The same scan over any production module.</summary>
    private static IEnumerable<MethodDefinition> CallersOf(
        ModuleDefinition module, string declaringType, string methodName) =>
        Il.MethodsWithBodies(module)
            .Where(method => Il.Instructions(method).Any(instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference called &&
                called.Name.Equals(methodName, StringComparison.Ordinal) &&
                called.DeclaringType.FullName.Equals(declaringType, StringComparison.Ordinal)));
}
