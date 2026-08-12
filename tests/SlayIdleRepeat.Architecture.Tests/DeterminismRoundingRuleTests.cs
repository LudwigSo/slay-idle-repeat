using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 <b>`05` §1.1's 4-decimal-place rule is stated in exactly ONE place</b> —
/// <c>SlayIdleRepeat.Core.Primitives.DeterminismRounding</c>. Nothing else in <c>Core</c> or
/// <c>Application</c> rounds to four places itself.
/// </summary>
/// <remarks>
/// <para>
/// `05` §1.1: <em>"all combat math uses <c>double</c>, rounded to 4 decimal places
/// (<c>Math.Round(x, 4)</c>) at every accumulation point. This is the locked determinism rule
/// (`14` §8.2, `18` §8 step 10, `16` A3)."</em> The rule is a <b>determinism</b> rule: its whole
/// value is that every accumulation point in the game agrees, to the bit, on two devices. A rule
/// stated in six places is six chances for one of them to say <c>5</c>, to drop the trailing
/// <c>+ 0.0</c> that normalises a negative zero, or to pick a different
/// <see cref="MidpointRounding"/> mode — and each of those produces a divergence that only shows up
/// as a <c>stateHash</c> or <c>LogHash</c> mismatch three layers away.
/// </para>
/// <para>
/// ⚠️ <b>Why this rule did not exist and had to.</b> It <em>was</em> stated six times. M2-03
/// recorded the duplication it could see — <c>Rules/Effects/Ops/OpRounding</c> against
/// <c>Rules/Stats/StatRounding</c>, forced by R17, since the bottom layer cannot name the one above
/// it — and pinned the two together with a numeric test. That test fires on <b>drift</b>, which is
/// the smaller half: it says nothing about a <em>seventh</em> statement being added, and nothing
/// about the four other copies that already existed in <c>Content</c>, <c>Conditions</c> and
/// <c>Triggers</c>. M2-02 consolidated all six onto a <c>Primitives</c> primitive — which reaches
/// under `30` §11.4's whole dependency chain, where <c>Rules.Stats</c> could not — and this is what
/// keeps them consolidated.
/// </para>
/// <para>
/// 🔒 <b>An IL scan, not a source grep.</b> A grep is defeated by <c>const int Places = 4;</c> one
/// line up, by a <c>using static</c>, and by a fully-qualified <c>System.Math.Round</c>; and it fires
/// inside comments, of which this repository's rounding code has a great many — the two files that
/// most want to write <c>Math.Round(x, 4)</c> are the ones whose remarks quote `05` §1.1 verbatim.
/// The scan looks for the call itself with a literal <c>4</c> pushed for its precision argument.
/// </para>
/// <para>
/// ⚠️ <b>WHAT THIS RULE CANNOT SEE, listed rather than implied.</b> Each is a real hole and none is
/// used anywhere in the repository today.
/// </para>
/// <list type="bullet">
///   <item>A precision that reaches <c>Math.Round</c> through a <b>local or a field</b> rather than a
///   literal — <c>var places = 4; Math.Round(x, places);</c>. The scan reads the pushed constant, so
///   an indirected one is invisible. This is the same class of hole
///   <c>IntraRulesLayeringRuleTests</c> records for a <c>const</c>, and it is narrow for the same
///   reason: nobody indirects a rounding precision by accident.</item>
///   <item><c>decimal.Round</c>, <c>Math.Round(decimal, int)</c> and
///   <c>double.Round</c>. `05` §1.1 says <em>"all combat math uses <c>double</c>"</em> and
///   <c>Content</c>'s decimals are authored values, not accumulation points — but a decimal
///   accumulation point would be outside this scan.</item>
///   <item>Hand-rolled rounding — <c>Math.Floor(x * 10000 + 0.5) / 10000</c>. It is a different
///   <em>rule</em> (away-from-zero rather than to-even) and would produce a divergence this rule
///   would not name. <see cref="No_production_code_hand_rolls_a_4_dp_rounding_out_of_10000"/> closes
///   the one spelling of it that is easy to reach for.</item>
///   <item>Rounding in <c>tools/BalanceHarness</c>, which is outside the scanned assemblies for the
///   reason <c>StringOrderingRuleTests</c> gives: it is not a shipped assembly.</item>
/// </list>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those (steering S12).
/// </para>
/// </remarks>
public sealed class DeterminismRoundingRuleTests
{
    /// <summary>The one type `05` §1.1's rule is allowed to live in.</summary>
    internal const string RoundingPrimitive = "SlayIdleRepeat.Core.Primitives.DeterminismRounding";

    /// <summary>The number of decimal places `05` §1.1 locks — what the scan looks for.</summary>
    private const int Places = 4;

    /// <summary>
    /// 🔒 `05` §1.1 — <em>"rounded to 4 decimal places (<c>Math.Round(x, 4)</c>) at every
    /// accumulation point … this is the locked determinism rule"</em> (`14` §8.2, `18` §8 step 10).
    /// Every 4-dp <c>Math.Round</c> in <c>Core</c> and <c>Application</c> is inside
    /// <see cref="RoundingPrimitive"/>, and one locked rule is stated once.
    /// </summary>
    [Fact]
    public void The_4_dp_rule_of_05_1_1_is_stated_in_exactly_one_place()
    {
        var offenders = FourDecimalPlaceRoundings()
            .Where(site => !site.DeclaringType.Equals(RoundingPrimitive, StringComparison.Ordinal))
            .Select(site =>
                $"{site.Method} calls {site.Callee} with a literal precision of 4 — " +
                $"`05` §1.1's determinism rule belongs to {RoundingPrimitive} alone. Two statements " +
                "of one rounding rule is how a 4 becomes a 5, or a trailing `+ 0.0` goes missing and " +
                "a -0.0 reaches CanonicalStateWriter, on one platform only.")
            .ToArray();

        ArchRule.Empty(
            offenders,
            "05 §1.1's 4-decimal-place rounding is stated once, in Core.Primitives.DeterminismRounding " +
            "(14 §8.2, 18 §8 step 10, 16 A3).");
    }

    /// <summary>
    /// 🔒 `23` §6 — <b>the floor under the rule's subject set (steering S3)</b>, over `05` §1.1's one
    /// remaining statement. The scan looks for a specific call shape; if that shape disappears from
    /// the assembly entirely — because the primitive was renamed, deleted, or rewritten to round some
    /// other way — the rule above quantifies over nothing and reports success while every
    /// accumulation point in the game is unguarded.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>It is a floor of ONE, and one is the right number.</b> The whole content of the rule
    /// above is that the count of 4-dp rounding sites outside the primitive is zero; the matching
    /// claim here is that the count <em>inside</em> it is not zero. A higher floor would fail the day
    /// the primitive's two methods were expressed as one.
    /// </remarks>
    [Fact]
    public void The_one_place_the_rule_is_stated_still_states_it()
    {
        var inside = FourDecimalPlaceRoundings()
            .Count(site => site.DeclaringType.Equals(RoundingPrimitive, StringComparison.Ordinal));

        if (inside == 0)
        {
            throw new ArchitectureRuleViolationException(
                "05 §1.1's rounding primitive still performs 4-dp rounding (23 §6).",
                new[]
                {
                    $"{RoundingPrimitive} performs NO 4-dp Math.Round. Either it has been renamed — in " +
                    "which case The_4_dp_rule_of_05_1_1_is_stated_in_exactly_one_place is now " +
                    "quantifying over an exemption that matches nothing, and every real rounding site " +
                    "in Core is an offender it will report — or the rule has stopped being stated at " +
                    "all and 05 §1.1 is unimplemented.",
                });
        }
    }

    /// <summary>
    /// 🔒 The <c>× 10000</c> spelling of the same rule. It is not <c>Math.Round</c>, so the rule above
    /// cannot see it, and it rounds <b>away from zero</b> where `05` §1.1's
    /// <c>Math.Round(x, 4)</c> rounds <b>to even</b> — so the two disagree at exactly the midpoints
    /// M2-03's <c>OpRoundingTests</c> pins (<c>1.00005</c> is <c>1.0000</c> to even and
    /// <c>1.0001</c> away from zero).
    /// </summary>
    /// <remarks>
    /// Both scanned assemblies, and both the <c>double</c> and the <c>int</c> spellings of the
    /// constant. This is narrow by design: it closes the one hand-rolled form that is easy to reach
    /// for, and the type remarks record that a determined author can still write another.
    /// </remarks>
    [Fact]
    public void No_production_code_hand_rolls_a_4_dp_rounding_out_of_10000()
    {
        var offenders = new List<string>();

        foreach (var (module, method) in ScannedMethods())
        {
            if (method.DeclaringType.FullName.Equals(RoundingPrimitive, StringComparison.Ordinal))
            {
                continue;
            }

            var scales = Il.Instructions(method).Count(IsTenThousand);

            // 🔒 TWO occurrences, not one: the hand-rolled idiom multiplies AND divides by the same
            //    constant. A single 10000 in a method is a tuning number, a currency scale or a
            //    tick count, and reporting those would make the rule noise that gets suppressed.
            if (scales >= 2)
            {
                offenders.Add(
                    $"{Il.Describe(method)} in {module} uses the constant 10000 twice — if that is a " +
                    $"hand-rolled 4-dp rounding, use {RoundingPrimitive}.Round: `05` §1.1 rounds to " +
                    "even and a floor/ceil of x*10000 rounds away from zero, so the two disagree at " +
                    "every midpoint.");
            }
        }

        ArchRule.Empty(
            offenders,
            "05 §1.1's rounding is Math.Round to even, performed by Core.Primitives.DeterminismRounding " +
            "— not hand-rolled out of a scale factor.");
    }

    /// <summary>
    /// Every call to <c>Math.Round</c> in the scanned assemblies whose precision argument is a
    /// literal <see cref="Places"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Only the <em>precision</em> position counts.</b> <c>Math.Round(x)</c> — one argument, no
    /// precision — is a whole-number rounding and has nothing to do with `05` §1.1;
    /// <c>TriggerSchedule</c> and <c>CombatLog</c> both use it to snap a tick count, and reporting
    /// them would be a false positive that makes the rule unusable. So the arity is checked before
    /// the operand is.
    /// </remarks>
    private static IEnumerable<(string DeclaringType, string Method, string Callee)> FourDecimalPlaceRoundings()
    {
        foreach (var (_, method) in ScannedMethods())
        {
            foreach (var instruction in Il.Instructions(method))
            {
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt) ||
                    instruction.Operand is not MethodReference callee ||
                    !IsMathRoundWithPrecision(callee))
                {
                    continue;
                }

                // The precision is the SECOND argument, so it is pushed second-to-last for the
                // 3-arg overload and last for the 2-arg one. Both are within two instructions back.
                if (PushesFour(instruction.Previous) || PushesFour(instruction.Previous?.Previous))
                {
                    yield return (
                        method.DeclaringType.FullName,
                        Il.Describe(method),
                        callee.FullName);
                }
            }
        }
    }

    /// <summary><c>System.Math.Round</c> in one of its precision-taking overloads.</summary>
    private static bool IsMathRoundWithPrecision(MethodReference callee) =>
        callee.Name.Equals("Round", StringComparison.Ordinal) &&
        callee.DeclaringType.FullName.Equals("System.Math", StringComparison.Ordinal) &&
        callee.Parameters.Count >= 2 &&
        callee.Parameters[1].ParameterType.FullName.Equals("System.Int32", StringComparison.Ordinal);

    /// <summary>The instruction pushes the literal <see cref="Places"/> onto the stack.</summary>
    /// <remarks>
    /// Roslyn emits <c>ldc.i4.4</c> for the small constant; <c>ldc.i4</c>/<c>ldc.i4.s</c> are handled
    /// so that the rule does not depend on which encoding the compiler chose.
    /// </remarks>
    private static bool PushesFour(Instruction? instruction) =>
        instruction is not null &&
        (instruction.OpCode.Code == Code.Ldc_I4_4 ||
         (instruction.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S &&
          Convert.ToInt32(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture) == Places));

    /// <summary>The literal <c>10000</c>, in either the integer or the floating-point spelling.</summary>
    private static bool IsTenThousand(Instruction instruction) =>
        (instruction.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S &&
         Convert.ToInt32(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture) == 10_000) ||
        (instruction.OpCode.Code == Code.Ldc_R8 &&
         Convert.ToDouble(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture) == 10_000.0) ||
        (instruction.OpCode.Code == Code.Ldc_R4 &&
         Convert.ToSingle(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture) == 10_000f);

    /// <summary>
    /// Every method with a body in the two shipped assemblies `05` §1.1 governs.
    /// </summary>
    /// <remarks>
    /// <c>Contracts</c> is excluded: `30` §11.6 shrinks it to wire envelopes, which carry rounded
    /// values rather than producing them. The adapters and composition roots are excluded for the
    /// same reason — an accumulation point is domain arithmetic by definition.
    /// </remarks>
    private static IEnumerable<(string Module, MethodDefinition Method)> ScannedMethods()
    {
        foreach (var name in new[] { ProductionAssemblies.CoreName, ProductionAssemblies.ApplicationName })
        {
            foreach (var method in Il.MethodsWithBodies(ProductionAssemblies.Module(name)))
            {
                yield return (name, method);
            }
        }
    }
}
