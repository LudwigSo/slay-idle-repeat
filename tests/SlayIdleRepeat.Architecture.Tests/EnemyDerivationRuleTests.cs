using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `05` §6 — the enemy derivation is a pure function of <c>(power, archetype, chapter, tier)</c>,
/// and no enemy identity is a special case in code.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6 makes a claim that is unusually easy to erode: <em>"every stat of every enemy is
/// computable from <c>(power, archetype, chapter, tier)</c> with no free variables."</em> Three
/// things would quietly falsify it, and each is a shape somebody would write for a good reason:
/// </para>
/// <list type="number">
/// <item>The derivation <b>holding</b> a draw stream or deriving a seed, rather than taking the
/// stream it is handed. `14` §8.1 roots the combat stream at a <c>battleSeed</c> that the caller
/// opens; a derivation that could reach a <c>runSeed</c> could make an enemy's stats depend on
/// something the four inputs do not name.</item>
/// <item>Static state under the namespace — a memoised archetype row, a cached catalogue — which
/// carries a value from one battle into the next.</item>
/// <item>A <c>if (eliteId == "EL_SPORELORD")</c>. `18`'s headnote is explicit that there is
/// <em>"no per-perk, per-talent or per-boss code"</em>, and `05` §6.2's identity table is 🔒
/// <em>"the single source of truth"</em> for the mapping. The moment one identity is named in code
/// the table has a second, invisible copy.</item>
/// </list>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those and M2-08 is editing <c>Domain.cs</c> in this milestone, so
/// patching a component another agent is scheduled to touch is how M0 broke thirty tests (steering
/// S12). The namespace constant is therefore declared here, and
/// <see cref="The_namespace_these_rules_govern_is_the_one_under_Rules_Combat"/> pins it against the
/// real tree so it cannot go stale.
/// </para>
/// </remarks>
public sealed class EnemyDerivationRuleTests
{
    /// <summary>`05` §6's derivation, level table, on-hit tables, elites and pools.</summary>
    internal const string EnemiesNamespace = "SlayIdleRepeat.Core.Rules.Combat.Enemies";

    /// <summary>
    /// Types the derivation must not <b>name at all</b>. Each is a legitimate part of the game and
    /// an illegitimate part of a function `05` §6 says has no free variables.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>DeterministicRng</c> is deliberately <b>absent</b> from this table and governed by the
    /// second rule instead. The pools and the elite modifier draw are <em>supposed</em> to draw
    /// (`05` §6.4, §6.2); what they must not do is <em>hold</em> a stream. A rule that banned the
    /// type outright would be one people learn to suppress.
    /// </remarks>
    private static readonly (string FullName, string Reason)[] ForbiddenTypes =
    {
        ("SlayIdleRepeat.Core.Rng.SeedDerivation",
            "deriving a seed here is the derivation reaching for a runSeed. 14 §8.1 roots the combat " +
            "stream at a battleSeed the CALLER opens and hands in — and a derivation that can derive " +
            "one can make an enemy's stats depend on something 05 §6's four inputs do not name."),
        ("SlayIdleRepeat.Core.Rng.Hash64",
            "hashing here is deriving a seed by another name — the same defect one layer down (14 §8.1)."),
        ("SlayIdleRepeat.Core.GameContext",
            "time enters Core as GameContext.NowUtc (30 §3). 05 §6's enemy stats are a function of " +
            "power, archetype, chapter and tier; an enemy whose stats moved with the wall clock would " +
            "differ between the client's replay and the server's recomputation."),
    };

    /// <summary>
    /// 🔒 `05` §6 / `14` §8.1 — the derivation never derives a seed, never hashes, never reads a
    /// clock, and never <b>holds</b> a draw stream.
    /// </summary>
    /// <remarks>
    /// The two halves are different shapes on purpose. The first is a reference scan; the second is a
    /// scan of <em>field types</em>, because holding a <c>DeterministicRng</c> is the specific way
    /// "the seed is handed in" stops being true while every reference in the file still looks
    /// innocent.
    /// </remarks>
    [Fact]
    public void The_enemy_derivation_never_derives_a_seed_and_never_holds_a_draw_stream()
    {
        var offenders = new List<string>();

        foreach (var type in Subjects())
        {
            foreach (var referenced in Il.ReferencedTypeNames(type))
            {
                var hit = ForbiddenTypes.FirstOrDefault(
                    b => b.FullName.Equals(referenced, StringComparison.Ordinal));

                if (hit.FullName is not null)
                {
                    offenders.Add($"{type.FullName} names {referenced} — {hit.Reason}");
                }
            }

            foreach (var field in type.Fields.Where(f => !Domain.IsCompilerGenerated(f)))
            {
                if (field.FieldType.FullName.Equals(DeterministicRngName, StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{Il.Describe(field)} holds a {DeterministicRngName} — 14 §8.1 has the caller " +
                        "open the combat stream at the encounter's battleSeed and hand it in. A held " +
                        "stream is state that outlives the call, so two draws that look independent " +
                        "share a position and a replay of the same seed stops reproducing.");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            $"05 §6: nothing under {EnemiesNamespace} derives a seed, hashes, reads a clock or holds a " +
            "draw stream — the stats are a function of (power, archetype, chapter, tier) and the " +
            "battle seed is handed in.");
    }

    /// <summary>
    /// 🔒 `05` §6.2 / `18`'s headnote — no enemy or elite identity is named in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `05` §6.2 is 🔒 that its identity table <em>"is the single source of truth"</em>. An
    /// <c>EL_</c> id in a string literal is a second copy of one of its rows, and the second copy is
    /// the one nobody updates. The scan is over <c>ldstr</c> operands rather than over the source
    /// text, so a comment naming an elite — and the remarks in this very file do — is not a hit,
    /// while a fully-qualified switch arm is.
    /// </para>
    /// <para>
    /// ⚠️ Test assemblies are deliberately out of scope. <c>EnemiesDataTests</c> asserts all sixteen
    /// ids by name, which is exactly what a transcription test is for.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_elite_identity_is_named_in_code()
    {
        var offenders = new List<string>();

        foreach (var type in Subjects())
        {
            foreach (var method in Il.AllMethods(type))
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    if (instruction.OpCode != OpCodes.Ldstr ||
                        instruction.Operand is not string literal ||
                        !literal.StartsWith(EliteIdPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    offenders.Add(
                        $"{Il.Describe(method)} names the elite identity '{literal}' — 05 §6.2's table " +
                        "is the single source of truth for the sixteen identities, and 18's headnote " +
                        "forbids per-enemy code. Read it from content/enemies/enemies.json.");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "05 §6.2: the sixteen elite identities are data. No production code names one.");
    }

    /// <summary>
    /// 🔒 `05` §6 — no type under the namespace holds static state that could carry a value from one
    /// battle into the next.
    /// </summary>
    /// <remarks>
    /// The shape is <c>EffectEvaluationPurityRuleTests</c>'s, and for its reason: a filter on
    /// <c>IsStatic &amp;&amp; !IsInitOnly</c> would skip
    /// <c>private static readonly Dictionary&lt;…&gt; _cache</c> entirely, because the field is
    /// <c>initonly</c> and only the object it points at changes. So the test is on the field's
    /// <b>type</b>. A catalogue cache is the specific thing this is watching for: the content
    /// snapshot is versioned and an override patch (`21` §3.3) changes it under a running process.
    /// </remarks>
    [Fact]
    public void The_enemy_derivation_holds_no_mutable_static_state()
    {
        var offenders = Subjects().SelectMany(MutableStaticState);

        ArchRule.Empty(
            offenders,
            $"Nothing under {EnemiesNamespace} caches: 05 §6's tables come from a versioned content " +
            "snapshot that an override patch can change under a running process.");
    }

    /// <summary>
    /// 🔒 `23` §6 / S3 — the floor under all three rules above. Each is "no member of set S does X" and passes
    /// vacuously when S empties; a rename of <c>Rules/Combat/Enemies/</c> empties it, and the
    /// sibling floor over <c>Rules.Combat</c> would stay satisfied by the combat log next door,
    /// because <c>Il.TypesUnder</c> matches by namespace <b>prefix</b>.
    /// </summary>
    [Fact]
    public void The_namespace_these_rules_govern_is_the_one_under_Rules_Combat()
    {
        var found = Subjects().Count;

        Assert.True(
            found >= EnemiesFloor,
            $"types under {EnemiesNamespace}: found {found}, floor is {EnemiesFloor}. All three rules " +
            "in this file are stated over them; empty, they report success over nothing and 05 §6's " +
            "derivation is free to hold a seed, cache a catalogue or switch on an elite id again. " +
            "If this shrank on purpose, lower the floor in the same commit and say why.");

        // 🔒 And the namespace really is beneath the one Domain declares — otherwise a rename in
        // Domain.cs (which M1-12 and M2-08 are both editing) would leave these rules governing a
        // namespace that no longer exists, silently.
        Assert.True(
            Il.IsUnder(EnemiesNamespace, Domain.CombatRulesNamespace),
            $"{EnemiesNamespace} is no longer beneath {Domain.CombatRulesNamespace}.");
    }

    /// <summary>
    /// Types under <c>Rules/Combat/Enemies/</c> on the commit these rules landed: the two enums plus
    /// <c>ArchetypeOnHit</c> and <c>PotencyBasis</c>, <c>ArchetypeRow</c>,
    /// <c>EnemyDerivationConstants</c>, <c>EnemyDerivation</c>, <c>EnemyLevelTable</c>,
    /// <c>OnHitStatus</c>, <c>EliteModifierRow</c>, <c>IEliteModifierHistory</c>,
    /// <c>EliteModifierHistory</c>, <c>EliteModifierDraw</c>, <c>ArchetypeWeight</c>,
    /// <c>ChapterEnemyPool</c>, <c>EnemyCatalogue</c> and <c>EnemyDefinition</c> — 17, plus whatever
    /// record plumbing the compiler emits. The floor is well below that so adding a type is not a
    /// test edit.
    /// </summary>
    private const int EnemiesFloor = 10;

    private const string DeterministicRngName = "SlayIdleRepeat.Core.Rng.DeterministicRng";

    /// <summary>`05` §6.2's identity prefix — the id space <c>enemies.json</c> declares.</summary>
    private const string EliteIdPrefix = "EL_";

    private static IReadOnlyList<TypeDefinition> Subjects() =>
        Il.TypesUnder(ProductionAssemblies.CoreModule, EnemiesNamespace)
          .Where(t => !Domain.IsCompilerGenerated(t))
          .ToArray();

    /// <summary>Every piece of static state a type holds that could survive a battle.</summary>
    private static IEnumerable<string> MutableStaticState(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (!field.IsStatic || field.IsLiteral ||
                Domain.IsCompilerGenerated(field) || Domain.IsCompilerGenerated(field.DeclaringType))
            {
                continue;
            }

            if (!IsImmutable(field.FieldType, depth: 0))
            {
                yield return
                    $"{Il.Describe(field)} is static and of the mutable type {field.FieldType.FullName} " +
                    "— a cached table is wrong the moment the content snapshot it summarised is " +
                    "replaced (21 §3.3). `readonly` does not help: it pins the reference, not the " +
                    "contents.";
                continue;
            }

            // 🔒 The second arm, and it is not redundant — it is the half the FIRST probe of this
            // rule missed. `private static int _drawCount;` is of an immutable TYPE and is still
            // state that survives a battle, so a rule stated only over field types reported success
            // over it. Found by re-probing with a second shape, which is the whole reason the
            // steering rules ask for one.
            if (!field.IsInitOnly)
            {
                yield return
                    $"{Il.Describe(field)} is a reassignable static field — 05 §6's derivation is a " +
                    "function of (power, archetype, chapter, tier), and anything it remembers between " +
                    "calls is a fifth input the section does not name.";
            }
        }
    }

    /// <summary>
    /// True for a type that can carry nothing from one call to the next: a primitive, a string, an
    /// enum, or a <c>readonly</c> value type all of whose instance fields are themselves immutable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>readonly struct</c> holding an array is still mutable by this test, because
    /// <c>readonly</c> pins the reference and not the contents. The recursion is depth-limited
    /// against a value type that reaches itself through a generic.
    /// </para>
    /// <para>
    /// ⚠️ The <see cref="TypeSpecification"/> arm is not defensive tidiness, and
    /// <c>EffectEvaluationPurityRuleTests</c> records why it had to plant a violation to find it:
    /// Cecil resolves an array, pointer or by-ref reference to its <b>element</b> type, so
    /// <c>double[]</c>.Resolve() is <c>System.Double</c> — a value type built out of a primitive,
    /// and therefore immutable by every test below. A hand-rolled memo wrapping a <c>double[]</c>
    /// would have been waved through while the plain <c>Dictionary</c> beside it was caught.
    /// </para>
    /// </remarks>
    private static bool IsImmutable(TypeReference reference, int depth)
    {
        if (depth > 4)
        {
            return false;
        }

        if (reference.IsPrimitive || reference.FullName == "System.String")
        {
            return true;
        }

        if (reference is TypeSpecification)
        {
            return false;
        }

        var definition = reference.Resolve();
        if (definition is null || !definition.IsValueType)
        {
            return false;
        }

        if (definition.IsEnum)
        {
            return true;
        }

        var isReadOnly = definition.CustomAttributes.Any(
            a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");

        return isReadOnly &&
               definition.Fields
                         .Where(f => !f.IsStatic)
                         .All(f => IsImmutable(f.FieldType, depth + 1));
    }
}
