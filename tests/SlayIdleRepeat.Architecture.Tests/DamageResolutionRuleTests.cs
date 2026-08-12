using System.Globalization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `05` §4 / §4.1 — the two things about the damage pipeline that must stay stated <b>once</b>:
/// the mitigation quotient over its two 📐 dials, and the ward pool.
/// </summary>
/// <remarks>
/// <para>
/// `05` §4 of the mitigation constants: <em>"the <c>120</c> and <c>20</c> constants are the two most
/// important balance dials in the game. Expose them in data."</em> They already live twice on
/// purpose — <c>content/combat_caps.json#/mitigation</c> and <c>tuning/power_model.json#/mitigation</c>,
/// which a <c>DeclaredRules</c> mirror rule keeps in step, because `29` §2.3's model grades the
/// simulator and the two cannot be allowed to mitigate differently. A <b>third</b> copy in code is
/// outside that mirror: nothing would compare it to either file, and `05` §9's assertion A10 would be
/// comparing two different games with every test green.
/// </para>
/// <para>
/// `05` §4.1 of the ward pool: it is <em>"one absorb pool per actor"</em> with an absorption order,
/// two ceilings and the <c>WardBroken</c>-versus-expiry distinction `18` §6's
/// <c>until: WARD_BROKEN</c> is built on. A second thing that subtracts from a ward would be a second
/// statement of all four.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those (steering S12).
/// </para>
/// </remarks>
public sealed class DamageResolutionRuleTests
{
    /// <summary>The `05` §4 dials, as the type that carries them out of <c>combat_caps.json</c>.</summary>
    private const string DialsType = "SlayIdleRepeat.Core.Rules.Stats.MitigationConstants";

    /// <summary>`05` §4.1's pool.</summary>
    private const string WardPoolType = "SlayIdleRepeat.Core.Rules.Combat.WardPool";

    /// <summary>The one engine `05` §4 authorises to run the pipeline.</summary>
    private const string PipelineType = "SlayIdleRepeat.Core.Rules.Combat.AttackPipeline";

    /// <summary>
    /// A floor under the scanned set (steering S3). <c>Core</c> carried well over a thousand method
    /// bodies when these rules landed; the floor is far below that, so ordinary deletion is not a
    /// test edit, and it still breaks the silence if the module stops loading or the filter stops
    /// matching.
    /// </summary>
    private const int ScannedMethodFloor = 200;

    /// <summary>
    /// 🔒 `05` §4 step 3 — <c>effDef / (effDef + flat + perLevel × attacker.Level)</c> is computed in
    /// exactly <b>one</b> method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subject is a method that both <b>reads a dial</b> and <b>divides</b>. That pairing is what
    /// separates the formula from the two legitimate readings that are not it:
    /// <c>MitigationConstants.ToString</c> renders both dials into a failure message, and
    /// <c>BattlePlan.Validated</c> range-checks them — neither divides, and a rule stated on the
    /// reading alone would have to exempt both by name and would then exempt a formula that moved
    /// into either.
    /// </para>
    /// <para>
    /// ⚠️ <b>What it cannot see.</b> A second mitigation computed from dials passed as bare
    /// <see cref="double"/>s rather than through <c>MitigationConstants</c> — which is exactly
    /// how <c>CombatSimulator.Simulate</c>'s public overload takes them, and is why that overload
    /// hands them straight to the record rather than doing arithmetic on them. And a division
    /// separated from its reads by an intervening call, which the rule counts per method body rather
    /// than per expression, so it is caught as long as both are in the same method.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_05_4_mitigation_quotient_is_computed_in_exactly_one_place()
    {
        var offenders = MitigationQuotients()
            .Where(method => !method.DeclaringType.FullName.Equals(PipelineType, StringComparison.Ordinal))
            .Select(method =>
                $"{Il.Describe(method)} reads a {DialsType} dial and divides. `05` §4 step 3's " +
                "mitigation curve belongs to " + PipelineType + " alone — it is the one expression " +
                "`05` §4 calls 'the two most important balance dials in the game', and a second " +
                "statement of it is a second game the 29 §2.3 power model is not grading.")
            .ToArray();

        ArchRule.Empty(
            offenders,
            "05 §4 step 3's mitigation curve is computed once, in the attack pipeline.");
    }

    /// <summary>
    /// 🔒 `23` §6 — the floor under the rule above (steering S3). If no method in <c>Core</c> reads a
    /// dial and divides, the mitigation curve is not implemented at all and the rule is green over
    /// nothing.
    /// </summary>
    [Fact]
    public void The_one_place_the_mitigation_quotient_lives_still_computes_it()
    {
        var inside = MitigationQuotients()
            .Count(method => method.DeclaringType.FullName.Equals(PipelineType, StringComparison.Ordinal));

        if (inside == 0)
        {
            throw new ArchitectureRuleViolationException(
                "05 §4 step 3's mitigation curve is still computed in the attack pipeline (23 §6).",
                new[]
                {
                    $"No method on {PipelineType} reads a {DialsType} dial and divides. Either the " +
                    "pipeline has been renamed — in which case " +
                    "The_05_4_mitigation_quotient_is_computed_in_exactly_one_place is now reporting " +
                    "the real formula as an offender — or `05` §4 step 3 has stopped being computed " +
                    "and every hit in the game is unmitigated.",
                });
        }
    }

    /// <summary>
    /// 🔒 `05` §4 — no production method restates <b>both</b> dials as literals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>10000</c>-twice rule's shape, and for its reason: <b>both</b> constants, because either
    /// alone is ordinary. <c>20</c> is `05` §3's tick rate and appears all over the simulator, and
    /// <c>120</c> is an unremarkable number; the <em>pair</em> in one method body is `05` §4 step 3
    /// hard-coded, which is the only way the dials can be reintroduced without going through
    /// <c>combat_caps.json</c>.
    /// </para>
    /// <para>
    /// Both the floating-point and the integer spellings are read, because
    /// <c>combat_caps.json</c> authors them as whole numbers and an author copying them out would
    /// naturally write <c>120</c> rather than <c>120.0</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_production_method_restates_both_05_4_dials_as_literals()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var method in Il.MethodsWithBodies(ProductionAssemblies.Module(ProductionAssemblies.CoreName)))
        {
            scanned++;

            var instructions = Il.Instructions(method).ToArray();

            if (instructions.Any(i => IsConstant(i, 120)) && instructions.Any(i => IsConstant(i, 20)))
            {
                offenders.Add(
                    $"{Il.Describe(method)} carries both 120 and 20 as literals. `05` §4: 'the 120 and " +
                    "20 constants are the two most important balance dials in the game. Expose them in " +
                    "data.' They are in content/combat_caps.json#/mitigation, mirrored against " +
                    "tuning/power_model.json by a content build rule; a copy in code is outside that " +
                    "mirror and nothing would ever compare it to either file.");
            }
        }

        // S3 — the floor under the subject set, in the same case as the rule, so that a scan over an
        // empty module cannot report success.
        if (scanned < ScannedMethodFloor)
        {
            offenders.Add(
                $"only {scanned.ToString(CultureInfo.InvariantCulture)} method bodies were scanned in " +
                $"{ProductionAssemblies.CoreName}; the floor is " +
                $"{ScannedMethodFloor.ToString(CultureInfo.InvariantCulture)}. The rule is quantifying " +
                "over almost nothing.");
        }

        ArchRule.Empty(
            offenders,
            "05 §4's two mitigation dials live in content/combat_caps.json and nowhere in code.");
    }

    /// <summary>
    /// 🔒 `05` §4.1 — <em>"one absorb pool per actor"</em>: only the attack pipeline absorbs, and
    /// nothing else in <c>Core</c> takes damage off a ward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four rules `05` §4.1 states — the ceiling, the absorption order, the per-source cap and
    /// the <c>WardBroken</c>-versus-expiry distinction — are all inside
    /// <see cref="WardPoolType"/>'s two methods. A second subtraction elsewhere would have to restate
    /// all four, and the one it would get wrong silently is the last: expiry emits
    /// <c>StatusExpired</c> and damage emits <c>WardBroken</c>, and `18` §6's
    /// <c>until: WARD_BROKEN</c> terminator reads only the second.
    /// </para>
    /// <para>
    /// ⚠️ <b>Stated over <c>Absorb</c> rather than over <c>Grant</c>.</b> Granting from more than one
    /// place is legitimate and already happens — <c>IAttackPipeline.GrantWard</c> for `05` §4.2's
    /// <c>SHIELD</c> and <c>BattleServices.GrantWard</c> for a `18` §6 duration — and both funnel
    /// into the same pool method. Absorption is the side where a second caller would be re-deciding
    /// the order and the break.
    /// </para>
    /// </remarks>
    [Fact]
    public void Only_the_attack_pipeline_absorbs_damage_with_a_ward()
    {
        var callers = WardAbsorptionCallers().ToArray();

        var offenders = callers
            .Where(method => !method.DeclaringType.FullName.Equals(PipelineType, StringComparison.Ordinal))
            .Select(method =>
                $"{Il.Describe(method)} calls {WardPoolType}.Absorb. `05` §4.1's pool is absorbed from " +
                $"{PipelineType} alone, which is what keeps the absorption order, the floor ordering " +
                "and the WardBroken-versus-expiry distinction stated once.")
            .ToList();

        // S3 — the floor. `05` §4 step 9 is `dmg = defender.Wards.Absorb(dmg)`, so if nothing calls
        // it, wards absorb nothing and the rule above is green over an empty set.
        if (callers.Length == 0)
        {
            offenders.Add(
                $"nothing in {ProductionAssemblies.CoreName} calls {WardPoolType}.Absorb. `05` §4 step " +
                "9 is `dmg = defender.Wards.Absorb(dmg)` — with no caller, every ward in the game " +
                "absorbs nothing and this rule reports success over an empty set.");
        }

        ArchRule.Empty(
            offenders,
            "05 §4.1's ward pool is absorbed from exactly one engine (23 §6).");
    }

    /// <summary>
    /// Every method in <c>Core</c> that reads a <see cref="DialsType"/> dial <b>and</b> divides.
    /// </summary>
    private static IEnumerable<MethodDefinition> MitigationQuotients()
    {
        foreach (var method in Il.MethodsWithBodies(ProductionAssemblies.Module(ProductionAssemblies.CoreName)))
        {
            var instructions = Il.Instructions(method).ToArray();

            var readsADial = instructions.Any(i =>
                i.OpCode.Code is Code.Call or Code.Callvirt &&
                i.Operand is MethodReference callee &&
                callee.DeclaringType.FullName.Equals(DialsType, StringComparison.Ordinal) &&
                (callee.Name.Equals("get_Flat", StringComparison.Ordinal) ||
                 callee.Name.Equals("get_PerLevel", StringComparison.Ordinal)));

            if (readsADial && instructions.Any(i => i.OpCode.Code is Code.Div or Code.Div_Un))
            {
                yield return method;
            }
        }
    }

    /// <summary>Every method in <c>Core</c> that calls <see cref="WardPoolType"/><c>.Absorb</c>.</summary>
    private static IEnumerable<MethodDefinition> WardAbsorptionCallers()
    {
        foreach (var method in Il.MethodsWithBodies(ProductionAssemblies.Module(ProductionAssemblies.CoreName)))
        {
            var absorbs = Il.Instructions(method).Any(i =>
                i.OpCode.Code is Code.Call or Code.Callvirt &&
                i.Operand is MethodReference callee &&
                callee.DeclaringType.FullName.Equals(WardPoolType, StringComparison.Ordinal) &&
                callee.Name.Equals("Absorb", StringComparison.Ordinal));

            if (absorbs)
            {
                yield return method;
            }
        }
    }

    /// <summary>
    /// The instruction pushes <paramref name="value"/>, in the integer or the double spelling.
    /// </summary>
    private static bool IsConstant(Instruction instruction, int value) =>
        (instruction.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S &&
         Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture) == value) ||
        (instruction.OpCode.Code == Code.Ldc_R8 &&
         Convert.ToDouble(instruction.Operand, CultureInfo.InvariantCulture) == value) ||
        (instruction.OpCode.Code == Code.Ldc_R4 &&
         Convert.ToSingle(instruction.Operand, CultureInfo.InvariantCulture) == value);
}
