using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `18` §4 — <em>"Conditions gate an effect without changing when it is evaluated. All are pure
/// functions of current state."</em> Enforced mechanically rather than trusted.
/// </summary>
/// <remarks>
/// <para>
/// A condition that draws, reads a clock or mutates anything is a determinism defect, and it is the
/// worst-behaved kind: `18` §1.1 has <c>valueScale</c> re-evaluate the same functions <em>"at every
/// resolution pass for <c>ALWAYS</c> effects"</em>, so an impure condition produces a different
/// number every pass — and the client and the server, evaluating the same battle from the same seed,
/// land on different HP. `14` §8.2's cross-platform determinism job would catch it eventually, at
/// the far end of a very long feedback loop.
/// </para>
/// <para>
/// ⚠️ <b>Why the existing rules do not already cover this.</b> <c>AmbientApiTests</c> bans
/// <c>DateTime.Now</c>, <c>System.Random</c> and friends across all of <c>Core</c> — the ambient
/// sources. Nothing bans <c>DeterministicRng</c>, and nothing should: it is the sanctioned way for
/// the rest of the game to draw, and `18` §5's <c>RANDOM_ENEMY</c> genuinely needs it one directory
/// over. What is forbidden is a <b>condition</b> reaching for it, and that is a claim about one
/// namespace rather than about an API.
/// </para>
/// <para>
/// ⚠️ <b>Scope.</b> An IL scan of <c>SlayIdleRepeat.Core.Rules.Effects.Conditions</c> only. The
/// target resolver next door is deliberately outside it — <c>RANDOM_ENEMY</c> draws by design, and a
/// rule that called that a violation would be a rule people learn to suppress.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files: those are being changed by another task
/// in flight, and patching a component someone else is scheduled to touch is how M0 broke thirty
/// tests.
/// </para>
/// </remarks>
public sealed class ConditionPurityRuleTests
{
    /// <summary>The namespace the purity rules govern — `18` §4's evaluation, and nothing else.</summary>
    internal const string ConditionsNamespace = "SlayIdleRepeat.Core.Rules.Effects.Conditions";

    /// <summary>`18` §5's target resolution, which draws by design and is governed more narrowly.</summary>
    internal const string TargetingNamespace = "SlayIdleRepeat.Core.Rules.Effects.Targeting";

    /// <summary>
    /// Types a condition must not name. Each is a legitimate part of the game and an illegitimate
    /// part of a pure function of current state.
    /// </summary>
    private static readonly (string FullName, string Reason)[] ImpureTypes =
    {
        ("SlayIdleRepeat.Core.Rng.DeterministicRng",
            "a condition that draws is re-evaluated at every resolution pass (18 §1.1) and answers " +
            "differently each time. RANDOM_ENEMY draws; a condition does not."),
        ("SlayIdleRepeat.Core.Rng.Hash64",
            "hashing here is drawing by another name — the same defect one layer down (14 §8.1)."),
        ("SlayIdleRepeat.Core.Rng.SeedDerivation",
            "a condition deriving a seed is a condition preparing to draw (14 §8.1)."),
        ("SlayIdleRepeat.Core.GameContext",
            "time enters Core as GameContext.NowUtc (30 §3), and 18 §4's clock is the battle's " +
            "elapsed seconds on the evaluation context, handed in by the caller."),

        // 🔒 The one-hop hole, closed. Il.ReferencedTypeNames is a DIRECT-reference scan, not a
        // transitive closure, so a condition that called TargetResolver.Resolve would name only
        // TargetResolver, EffectTarget and IEffectActorView — none of them banned — while
        // TargetResolver.RandomEnemy draws, and the Targeting namespace is deliberately exempt from
        // this rule. Both purity rules would have stayed green over a condition that draws.
        ("SlayIdleRepeat.Core.Rules.Effects.Targeting.TargetResolver",
            "a condition that resolves a target reaches RANDOM_ENEMY's draw stream one hop away — " +
            "18 §4 conditions read state, they do not select actors."),
    };

    /// <summary>
    /// Types outside <see cref="ConditionsNamespace"/> that a condition reaches and that must
    /// therefore meet a condition's standard of purity.
    /// </summary>
    /// <remarks>
    /// 🔒 A namespace filter alone is defeated by one hop into a helper. <c>BattleRoster</c> is the
    /// shared "living enemies hostile to the holder" predicate behind <c>ENEMY_COUNT</c> and every
    /// `18` §5 enemy token; it lives in <c>Rules/Effects/</c> because the target resolver needs it
    /// too, and it is governed here by name because <c>ConditionEvaluator</c> calls it.
    /// </remarks>
    private static readonly string[] ReachedByAConditionByName =
    {
        "SlayIdleRepeat.Core.Rules.Effects.BattleRoster",
    };

    /// <summary>
    /// 🔒 `18` §4 — nothing under <c>Rules/Effects/Conditions/</c> draws, hashes, derives a seed or
    /// reads wall-clock time. Conditions are <em>"pure functions of current state"</em>.
    /// </summary>
    [Fact]
    public void A_condition_never_draws_and_never_reads_a_clock()
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
            "18 §4: conditions are pure functions of current state — nothing under " +
            ConditionsNamespace + " draws, hashes or reads a clock.");
    }

    /// <summary>
    /// 🔒 `18` §4 — nothing under <c>Rules/Effects/Conditions/</c> mutates: no instance field is
    /// written outside a constructor or an <c>init</c> accessor, no static field is written outside a
    /// static constructor, and no static field is writable at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mutation this is really watching for is a cache. A memoised reading is the most natural
    /// thing in the world to add to an evaluator called once per effect per pass, and it is a
    /// determinism break the moment the state it summarised moves — which, in a 20 Hz tick loop, is
    /// every tick.
    /// </para>
    /// <para>
    /// ⚠️ Compiler-generated fields are exempt, and the exemption is precise rather than broad: it
    /// covers writes whose <b>target field's declaring type</b> carries
    /// <c>CompilerGeneratedAttribute</c>. That is the lambda cache — C# emits
    /// <c>if (&lt;&gt;c.&lt;&gt;9__0_0 == null) &lt;&gt;c.&lt;&gt;9__0_0 = …</c>, a <c>stsfld</c>
    /// executed from an ordinary method — and without the exemption a single LINQ lambda in the
    /// evaluator would report as a mutation. Exempting by the <em>writing method</em> instead would
    /// have let a real static cache through as long as it were written from a lambda.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_condition_never_mutates_anything()
    {
        var offenders = new List<string>();

        foreach (var type in Subjects())
        {
            offenders.AddRange(MutableStaticState(type, "a memoised reading is a determinism break " +
                                                        "the moment the state it summarised moves (18 §4)"));

            foreach (var method in type.Methods)
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    var written = Written(instruction);
                    if (written is null || IsCompilerPlumbing(written))
                    {
                        continue;
                    }

                    // A static field may only be written by the static constructor; an instance field
                    // only by an instance constructor or an `init` accessor. Everything else is a
                    // mutation.
                    var permitted = instruction.OpCode.Code == Code.Stsfld
                        ? method.IsConstructor && method.IsStatic
                        : (method.IsConstructor && !method.IsStatic) || IsInitAccessor(method);

                    if (!permitted)
                    {
                        offenders.Add(
                            $"{Il.Describe(method)} writes {written.FullName} outside a constructor — " +
                            "18 §4: conditions gate an effect, they do not change state.");
                    }
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "18 §4: conditions are pure functions of current state — nothing under " +
            ConditionsNamespace + " mutates a field.");
    }

    /// <summary>
    /// 🔒 `18` §5 — the target resolver holds no writable static state either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Narrower than the two rules above, and deliberately so. <c>RANDOM_ENEMY</c> genuinely draws
    /// (`18` §5, `14` §8.1), so the no-draw rule cannot extend here; and the resolver writes to the
    /// <see cref="Mono.Cecil.Cil.Instruction"/>-level locals a candidate list needs, so the
    /// no-instance-write rule would be noise.
    /// </para>
    /// <para>
    /// What remains is the defect that would actually bite: a <b>memoised candidate list</b>. A
    /// static cache of "the living enemies" is the most natural optimisation to reach for in a
    /// resolver called several times per tick, and it is wrong on the very next death — silently,
    /// and identically on client and server, so `14` §8.2's determinism job would not catch it
    /// either.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_18_5_target_resolver_holds_no_writable_static_state()
    {
        var offenders = Targets().SelectMany(
            type => MutableStaticState(
                type,
                "a memoised candidate list is wrong on the next death, and wrong identically on " +
                "client and server, so 14 §8.2's determinism job would not catch it either (18 §5)"));

        ArchRule.Empty(
            offenders,
            "Nothing under " + TargetingNamespace + " caches: 18 §5's tokens are resolved against the " +
            "roster they are handed, every time.");
    }

    /// <summary>
    /// How many types the condition rules examined. Read by <c>SubjectSetFloorTests</c>, which owns
    /// the floor under them (steering S3 — a namespace filter can be emptied by a rename, and both
    /// rules would then report success forever).
    /// </summary>
    internal static int SubjectCount => Subjects().Count;

    /// <summary>How many types the targeting rule examined. Floored by <c>SubjectSetFloorTests</c> too.</summary>
    internal static int TargetSubjectCount => Targets().Count;

    /// <summary>
    /// The types the condition rules govern: everything under <see cref="ConditionsNamespace"/>, plus
    /// the named helpers a condition reaches.
    /// </summary>
    private static IReadOnlyList<TypeDefinition> Subjects() =>
        Il.TypesUnder(ProductionAssemblies.CoreModule, ConditionsNamespace)
          .Concat(Domain.CoreTypes.Where(
              t => ReachedByAConditionByName.Contains(t.FullName, StringComparer.Ordinal)))
          .ToArray();

    private static IReadOnlyList<TypeDefinition> Targets() =>
        Il.TypesUnder(ProductionAssemblies.CoreModule, TargetingNamespace).ToArray();

    /// <summary>The field an instruction writes, or <c>null</c> when it writes none.</summary>
    private static FieldReference? Written(Instruction instruction) =>
        instruction.OpCode.Code is Code.Stfld or Code.Stsfld
            ? instruction.Operand as FieldReference
            : null;

    /// <summary>
    /// Every piece of static state a type holds that could carry a cache from one evaluation to the
    /// next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Not "writable" — <em>mutable</em>, which is not the same thing and was the hole.</b> The
    /// first version of both rules filtered <c>IsStatic &amp;&amp; !IsInitOnly &amp;&amp; !IsLiteral</c>, so
    /// <c>private static readonly Dictionary&lt;string, double&gt; _memo</c> was <b>skipped</b> — the
    /// field is <c>initonly</c>, only the object it points at changes. That is precisely the
    /// "memoised reading" and "memoised candidate list" each rule names as its reason for existing,
    /// and the IL scan could not see it either: <c>_memo[key] = value</c> is a <c>callvirt
    /// set_Item</c>, not a <c>stsfld</c>. Both rules passed over the one defect they were written to
    /// catch.
    /// </para>
    /// <para>
    /// The test is therefore on the field's <b>type</b>: a static field is acceptable only when it
    /// cannot hold mutable state at all — a primitive, a string or an enum. A static array, list,
    /// dictionary, or object of any other kind is reported whether or not the field itself can be
    /// reassigned.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> MutableStaticState(TypeDefinition type, string consequence)
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

            if (IsImmutableScalar(field.FieldType))
            {
                // A `static readonly int` carries nothing from one evaluation to the next. A
                // reassignable one still does, so the initonly check stays — for scalars only.
                if (field.IsInitOnly)
                {
                    continue;
                }

                yield return $"{Il.Describe(field)} is a reassignable static field — {consequence}.";
                continue;
            }

            yield return
                $"{Il.Describe(field)} is static and of the mutable type {field.FieldType.FullName} — " +
                $"{consequence}. `readonly` does not help: it pins the reference, not the contents.";
        }
    }

    /// <summary>True for a type that cannot hold mutable state: a primitive, a string, or an enum.</summary>
    private static bool IsImmutableScalar(TypeReference type) =>
        type.IsPrimitive ||
        type.FullName.Equals("System.String", StringComparison.Ordinal) ||
        SafeResolve(type)?.IsEnum == true;

    /// <summary>Resolves a type reference, answering <c>null</c> rather than throwing when it cannot.</summary>
    private static TypeDefinition? SafeResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            // An unresolvable type is not a scalar as far as this rule is concerned — failing open
            // here would be the same hole the remarks above describe, one level down.
            return null;
        }
    }

    /// <summary>
    /// True for a write the compiler emitted on its own behalf — the lambda cache, a closure's
    /// captured locals — rather than one the author wrote.
    /// </summary>
    private static bool IsCompilerPlumbing(FieldReference field) =>
        field.DeclaringType?.Resolve() is { } declaring && Domain.IsCompilerGenerated(declaring);

    /// <summary>
    /// True for an <c>init</c>-only property accessor.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>An <c>init</c> setter is not mutation, and this rule cannot tell without asking.</b> In
    /// IL an <c>init</c> accessor is an ordinary <c>set_X</c> method writing a backing field — the
    /// only thing distinguishing it from a real setter is a <c>modreq</c> of
    /// <c>System.Runtime.CompilerServices.IsExternalInit</c> on its return type, which is what the
    /// compiler enforces the once-at-construction rule from. Without this arm the rule reported all
    /// three <c>init</c> accessors of <c>ConditionArguments</c> as mutations, which is a rule crying
    /// wolf on correct code — and one people learn to suppress.
    /// <para>
    /// Matched on the modifier, never on the <c>set_</c> name: a plain settable property is spelled
    /// identically and IS the mutation this rule exists to catch.
    /// </para>
    /// </remarks>
    private static bool IsInitAccessor(MethodDefinition method) =>
        method.IsSetter &&
        method.ReturnType is IModifierType modifier &&
        modifier.ModifierType.FullName.Equals(
            "System.Runtime.CompilerServices.IsExternalInit",
            StringComparison.Ordinal);
}
