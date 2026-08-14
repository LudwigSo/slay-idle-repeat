using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `18` §1.1 / §2.2 / §6 — the three effect-evaluation namespaces M2-06 added are pure functions
/// of the state they are handed: they never draw, never read a clock, and hold no static state that
/// could carry a value from one evaluation pass to the next.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this file exists at all.</b> <c>ConditionPurityRuleTests</c> (M2-05) states exactly this
/// claim over <c>Rules/Effects/Conditions/</c> and <c>Rules/Effects/Targeting/</c> — the only two
/// namespaces that existed under <c>Rules/Effects/</c> when it was written. M2-06 added three more —
/// <c>Values/</c>, <c>Duration/</c> and <c>Stacking/</c> — and a namespace filter governs the
/// namespaces it names and no others, so those three landed as regions of the effects layer that no
/// purity rule reached. Nothing was wrong with the code; what was missing was anything that would
/// stay right.
/// </para>
/// <para>
/// 🔒 <b>The claim is the same one, and it is load-bearing for the same reason.</b> `18` §1.1
/// re-evaluates a <c>valueScale</c> <em>"at every resolution pass for <c>ALWAYS</c> effects, at fire
/// time for triggered ones"</em>, `05` §3.1 re-reads a stack count <em>"at the moment the tick
/// lands"</em> at 20 Hz, and `18` §6 re-asks every applied effect whether it has ended on every one
/// of those ticks. All three are therefore called thousands of times per battle over state that
/// moves underneath them — which is precisely where a memo dictionary gets added, and precisely
/// where one is wrong. A divergence here does not throw: the client and the server compute different
/// HP from the same seed, and `14` §8.2's determinism job is the far end of a very long feedback
/// loop.
/// </para>
/// <para>
/// ⚠️ <b>The banned-type table is restated rather than shared with
/// <c>ConditionPurityRuleTests</c>.</b> Hoisting it would mean editing that file, which sits in the
/// path of two in-flight siblings (M2-03's <c>Ops/</c> and M2-04's <c>Triggers/</c> both extend the
/// effects layer). The duplication is five strings and is recorded here so that whoever consolidates
/// the effects-layer purity rules — the natural moment is when M2-08's R17 rule lands next door —
/// moves both tables at once.
/// </para>
/// <para>
/// ⚠️ <b>Scope.</b> An IL scan of the three namespaces only. It deliberately does not extend to
/// <c>Rules/Effects/</c> as a whole: <c>Targeting/</c> beneath it draws by design (`18` §5's
/// <c>RANDOM_ENEMY</c>), and a rule that called that a violation would be one people learn to
/// suppress.
/// </para>
/// </remarks>
public sealed class EffectEvaluationPurityRuleTests
{
    /// <summary>`18` §1.1's <c>valueScale</c> and `18` §2.2's value modes.</summary>
    internal const string ValuesNamespace = "SlayIdleRepeat.Core.Rules.Effects.Values";

    /// <summary>`18` §6's duration scopes and terminators.</summary>
    internal const string DurationNamespace = "SlayIdleRepeat.Core.Rules.Effects.Duration";

    /// <summary>`18` §6's stacking modes.</summary>
    internal const string StackingNamespace = "SlayIdleRepeat.Core.Rules.Effects.Stacking";

    /// <summary>
    /// Types an effect evaluator must not name. Each is a legitimate part of the game and an
    /// illegitimate part of a function that is re-run over moving state.
    /// </summary>
    /// <remarks>
    /// 🔒 The last entry closes the same one-hop hole <c>ConditionPurityRuleTests</c> records:
    /// <c>Il.ReferencedTypeNames</c> is a DIRECT-reference scan, so an evaluator that called
    /// <c>TargetResolver.Resolve</c> would name only types that are not banned, while
    /// <c>RandomEnemy</c> draws one hop away inside a namespace this rule deliberately exempts.
    /// </remarks>
    private static readonly (string FullName, string Reason)[] ImpureTypes =
    {
        ("SlayIdleRepeat.Core.Rng.DeterministicRng",
            "an evaluator that draws answers differently on every resolution pass (18 §1.1), so the " +
            "same effect scales, ends or stacks differently on the client and on the server."),
        ("SlayIdleRepeat.Core.Rng.Hash64",
            "hashing here is drawing by another name — the same defect one layer down (14 §8.1)."),
        ("SlayIdleRepeat.Core.Rng.SeedDerivation",
            "an evaluator deriving a seed is an evaluator preparing to draw (14 §8.1)."),
        ("SlayIdleRepeat.Core.GameContext",
            "time enters Core as GameContext.NowUtc (30 §3). 18 §6's clock is the battle's elapsed " +
            "seconds on the probe, handed in by the caller — which is why DurationProbe carries a " +
            "number and DurationEvaluator holds no clock."),
        ("SlayIdleRepeat.Core.Rules.Effects.Targeting.TargetResolver",
            "resolving a target reaches RANDOM_ENEMY's draw stream one hop away, inside the one " +
            "namespace this rule exempts. 18 §1.1, §2.2 and §6 read state; they do not select actors."),
    };

    /// <summary>
    /// 🔒 `18` §1.1 / §6 — nothing under <c>Rules/Effects/Values/</c>, <c>Duration/</c> or
    /// <c>Stacking/</c> draws, hashes, derives a seed or reads wall-clock time.
    /// </summary>
    [Fact]
    public void An_effect_evaluator_never_draws_and_never_reads_a_clock()
    {
        var offenders = new List<string>();

        foreach (var type in Subjects())
        {
            foreach (var referenced in Il.ReferencedTypeNames(type))
            {
                var hit = ImpureTypes.FirstOrDefault(
                    b => b.FullName.Equals(referenced, StringComparison.Ordinal));

                if (hit.FullName is not null)
                {
                    offenders.Add($"{type.FullName} names {referenced} — {hit.Reason}");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "18 §1.1, §2.2 and §6 are evaluated against the state they are handed — nothing under " +
            $"{ValuesNamespace}, {DurationNamespace} or {StackingNamespace} draws, hashes or reads a clock.");
    }

    /// <summary>
    /// 🔒 `05` §3.1 / `18` §6 — no type under the three evaluation namespaces holds static state
    /// that could survive an evaluation pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this is watching for is a cache, and M2 wave 2 already paid for getting the shape
    /// of that test wrong twice: a filter on <c>IsStatic &amp;&amp; !IsInitOnly</c> skips
    /// <c>private static readonly Dictionary&lt;…&gt; _memo</c> entirely, because the field is
    /// <c>initonly</c> and only the object it points at changes — and an IL scan for <c>stsfld</c>
    /// misses <c>_memo[key] = value</c>, which is a <c>callvirt set_Item</c>. So the test is on the
    /// field's <b>type</b>, exactly as <c>ConditionPurityRuleTests.MutableStaticState</c> settled it.
    /// </para>
    /// <para>
    /// ⚠️ <b>One deliberate widening over that version: a fully immutable value type is admitted.</b>
    /// <c>DurationEvaluator</c>'s <c>private static readonly DurationOutcome Running</c> is a
    /// <c>readonly record struct</c> of a <c>bool</c> and an enum — it can carry nothing from one
    /// evaluation to the next, and reporting it would be the rule crying wolf on correct code, which
    /// is how a rule earns a suppression. The widening is narrow and recursive: a <c>readonly</c>
    /// value type <em>all</em> of whose instance fields are themselves immutable by this same test.
    /// A <c>readonly struct</c> holding a <c>double[]</c> is still reported, because <c>readonly</c>
    /// pins the reference and not the contents — which is the whole hole this rule descends from.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_effect_evaluator_holds_no_mutable_static_state()
    {
        var offenders = Subjects().SelectMany(MutableStaticState);

        ArchRule.Empty(
            offenders,
            $"Nothing under {ValuesNamespace}, {DurationNamespace} or {StackingNamespace} caches: 18 §1.1 " +
            "re-evaluates a valueScale at every resolution pass, and 05 §3.1 re-reads a stack count at " +
            "the moment each 20 Hz tick lands.");
    }

    /// <summary>
    /// How many types the two rules above examined. Read by <c>SubjectSetFloorTests</c>, which owns
    /// the floor under them (steering S3 — three namespace filters can all be emptied by one folder
    /// rename, and both rules would then report success forever).
    /// </summary>
    internal static int SubjectCount => Subjects().Count;

    /// <summary>Every type under the three effect-evaluation namespaces.</summary>
    private static IReadOnlyList<TypeDefinition> Subjects() =>
        new[] { ValuesNamespace, DurationNamespace, StackingNamespace }
            .SelectMany(ns => Il.TypesUnder(ProductionAssemblies.CoreModule, ns))
            .ToArray();

    /// <summary>Every piece of static state a type holds that could carry a value across a pass.</summary>
    private static IEnumerable<string> MutableStaticState(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (!field.IsStatic || field.IsLiteral)
            {
                continue;
            }

            if (Domain.IsCompilerGenerated(field) || Domain.IsCompilerGenerated(field.DeclaringType))
            {
                continue;
            }

            if (!IsImmutable(field.FieldType, depth: 0))
            {
                yield return
                    $"{Il.Describe(field)} is static and of the mutable type {field.FieldType.FullName} — " +
                    "a memoised reading is a determinism break the moment the state it summarised moves " +
                    "(18 §1.1, 05 §3.1). `readonly` does not help: it pins the reference, not the contents.";
                continue;
            }

            if (!field.IsInitOnly)
            {
                yield return
                    $"{Il.Describe(field)} is a reassignable static field — an evaluator that remembers " +
                    "anything between passes is not a function of the state it was handed (18 §1.1).";
            }
        }
    }

    /// <summary>
    /// True for a type that cannot hold mutable state: a primitive, a string, an enum, or a
    /// <c>readonly</c> value type built entirely out of those.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unresolvable type answers <c>false</c>. Failing open here would reopen the hole this rule
    /// descends from, one level down.
    /// </para>
    /// <para>
    /// ⚠️ <b>The <see cref="TypeSpecification"/> arm is not defensive tidiness — without it this
    /// method answered <c>true</c> for <c>double[]</c>, and the planted violation that proved it is
    /// the reason the arm exists.</b> Cecil resolves an array, pointer or by-ref reference to its
    /// ELEMENT type: <c>double[]</c>.Resolve() is <c>System.Double</c>, which is a <c>readonly</c>
    /// value type built out of a primitive and therefore immutable by every test below. So a
    /// <c>static readonly</c> struct wrapping a <c>double[]</c> — a hand-rolled memo, the exact
    /// defect this rule names — was waved through while the plain <c>Dictionary</c> next to it was
    /// caught. A generic instantiation is rejected by the same arm and for the same reason: its
    /// arguments are not what <c>Resolve()</c> hands back.
    /// </para>
    /// </remarks>
    private static bool IsImmutable(TypeReference type, int depth)
    {
        if (type.IsPrimitive || type.FullName.Equals("System.String", StringComparison.Ordinal))
        {
            return true;
        }

        // Arrays, pointers, by-refs and generic instantiations, all of which Resolve() answers with
        // something other than themselves. Fail CLOSED.
        if (type is TypeSpecification)
        {
            return false;
        }

        if (SafeResolve(type) is not { } resolved)
        {
            return false;
        }

        if (resolved.IsEnum)
        {
            return true;
        }

        // A struct cannot contain itself, so the recursion terminates on its own; the bound is a
        // guard against a pathological generic instantiation, and it fails CLOSED.
        if (depth >= 4 || !resolved.IsValueType || !IsReadOnly(resolved))
        {
            return false;
        }

        return resolved.Fields
            .Where(f => !f.IsStatic)
            .All(f => IsImmutable(f.FieldType, depth + 1));
    }

    /// <summary>True for a type the compiler marked <c>readonly</c>.</summary>
    private static bool IsReadOnly(TypeDefinition type) =>
        type.CustomAttributes.Any(a =>
            a.AttributeType.FullName.Equals(
                "System.Runtime.CompilerServices.IsReadOnlyAttribute", StringComparison.Ordinal));

    /// <summary>Resolves a type reference, answering <c>null</c> rather than throwing when it cannot.</summary>
    private static TypeDefinition? SafeResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }
}
