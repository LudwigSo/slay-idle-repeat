using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `17` §11 — <em>"All 8 bosses expressed purely in the effect DSL — zero bespoke boss code."</em>
/// The boss engine is one controller for eight fights: it derives no seed, holds no draw stream,
/// names no boss, and remembers nothing between battles.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this file had to exist.</b> <c>Rules/Combat/Enemies/</c> has
/// <see cref="EnemyDerivationRuleTests"/> for exactly these shapes, and `05` §6.2's sixteen elite
/// identities are a far smaller claim than `17` §11's. The boss engine spans both ends of R17 — the
/// 44th op sits in <c>Rules/Effects/Ops/</c> and the engine in <c>Rules/Combat/Bosses/</c>, joined by
/// the <c>IBossOutcomes</c> seam — and it is the one subsystem the design explicitly promises has no
/// per-identity code in it. A promise nothing quantifies over is a comment.
/// </para>
/// <para>
/// Three things would falsify `17` §11 quietly, and each is a shape somebody would write for a good
/// reason:
/// </para>
/// <list type="number">
///   <item>An <c>if (bossId == "BOSS_DICELORD")</c>, or a table in code keyed on one. `18`'s headnote
///   is 🔒 that there is <em>"no per-perk, per-talent or per-boss code"</em>, and `17` §1.2's
///   coefficient table is the single source of truth for the eight rows — the moment one identity is
///   named in code the table has a second, invisible copy, and it is the second one nobody
///   retunes.</item>
///   <item>The engine <b>holding</b> a draw stream or deriving a seed. `14` §8.1 roots the combat
///   stream at a <c>battleSeed</c> the caller opens; `18` §10.1 E6's <c>RANDOM_OUTCOME</c> is the one
///   thing in the boss engine's reach that draws at all, and it draws in the <b>op</b> layer from the
///   stream the battle handed it. A boss type that could reach a <c>runSeed</c> could make a phase
///   depend on something `05` §3.1's phase check does not name.</item>
///   <item>Static state under the namespace — a memoised built-in set, a cached encounter — which
///   carries a value from one battle into the next. `17` §1's fights are replayed from a log
///   (`05` §7); anything the engine remembers across battles is a difference between the client's
///   replay and the server's recomputation that no seed explains.</item>
/// </list>
/// <para>
/// 🔒 A <b>new file</b>, on <see cref="EnemyDerivationRuleTests"/>' precedent and for its reason:
/// M1-12 holds <c>Infrastructure/Domain.cs</c>, so the namespace constant is restated here and
/// <see cref="The_namespace_these_rules_govern_is_the_one_under_Rules_Combat"/> pins the restatement
/// against the real tree so it cannot go stale (steering S12).
/// </para>
/// </remarks>
public sealed class BossEngineRuleTests
{
    /// <summary>`17` §1's phase machine, built-ins, telegraphs, summon roster and outcome resolver.</summary>
    internal const string BossesNamespace = "SlayIdleRepeat.Core.Rules.Combat.Bosses";

    /// <summary>
    /// Types the boss engine must not <b>name at all</b>. Each is a legitimate part of the game and an
    /// illegitimate part of a subsystem whose whole job is to run eight authored scripts identically.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>DeterministicRng</c> is deliberately <b>absent</b> from this table and governed by the
    /// field scan instead. `18` §10.1 E6's <c>RANDOM_OUTCOME</c> is <em>supposed</em> to draw, and it
    /// does so one layer down in <c>Rules/Effects/Ops/</c> off the stream the battle opened; what no
    /// boss type may do is <em>hold</em> one. A rule that banned the type outright would be one people
    /// learn to suppress.
    /// </remarks>
    private static readonly (string FullName, string Reason)[] ForbiddenTypes =
    {
        ("SlayIdleRepeat.Core.Rng.SeedDerivation",
            "deriving a seed here is the boss engine reaching for a runSeed. 14 §8.1 roots the combat " +
            "stream at a battleSeed the CALLER opens and hands in, and 05 §3.1's phase check is a " +
            "function of HP and of the phase already entered — neither of which a seed appears in."),
        ("SlayIdleRepeat.Core.Rng.Hash64",
            "hashing here is deriving a seed by another name — the same defect one layer down (14 §8.1)."),
        ("SlayIdleRepeat.Core.GameContext",
            "time enters Core as GameContext.NowUtc (30 §3). 17 §1's 70 s enrage and 1.0-1.5 s " +
            "telegraphs are stated in TICKS of 05 §3's fixed-tick loop; a boss whose schedule moved " +
            "with the wall clock would differ between the client's replay and the server's " +
            "recomputation of the same battleSeed."),
    };

    /// <summary>
    /// 🔒 `17` §1 / `14` §8.1 — the boss engine never derives a seed, never hashes, never reads a
    /// clock, and never <b>holds</b> a draw stream.
    /// </summary>
    /// <remarks>
    /// The two halves are different shapes on purpose. The first is a reference scan; the second is a
    /// scan of <em>field types</em>, because holding a <c>DeterministicRng</c> is the specific way
    /// "the battle hands the stream in" stops being true while every reference in the file still looks
    /// innocent. <c>BossOutcomes</c> and <c>BossPhaseController</c> both hold a
    /// <c>BattleServices</c>, which <em>exposes</em> the fight's stream — that is a routing to the one
    /// stream `14` §8.1 opened, and the field scan is what keeps it from becoming a second one.
    /// </remarks>
    [Fact]
    public void The_boss_engine_never_derives_a_seed_and_never_holds_a_draw_stream()
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
            $"17 §1: nothing under {BossesNamespace} derives a seed, hashes, reads a clock or holds a " +
            "draw stream — a phase is a function of HP and of the phase already entered, and the " +
            "battle seed is handed in.");
    }

    /// <summary>
    /// 🔒 `17` §11 / `18`'s headnote — <b>no boss identity is named anywhere in <c>Core/Rules/</c></b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the mechanical statement of <em>"zero bespoke boss code"</em>, and it is the analogue of
    /// <c>EnemyDerivationRuleTests.No_elite_identity_is_named_in_code</c>. `17` §1.2's eight-row
    /// coefficient table is the single source of truth for who the bosses are; a <c>BOSS_</c> id in a
    /// string literal is a second copy of one of its rows.
    /// </para>
    /// <para>
    /// 🔒 <b>The scope is the whole of <c>Core/Rules/</c>, not just the boss namespace</b>, and the
    /// widening is the point: an <c>if (boss.Id == "BOSS_RIMEHOLD")</c> in the tick loop, the damage
    /// pipeline or a DSL op would be exactly as bespoke as one inside <c>Bosses/</c>, and a rule
    /// scoped to <c>Bosses/</c> would report success over it. `17` §11's claim is about the engine,
    /// not about one folder.
    /// </para>
    /// <para>
    /// The scan is over <c>ldstr</c> operands rather than over source text, so a comment naming a boss
    /// — and the remarks in this very file do — is not a hit, while a fully-qualified switch arm is.
    /// ⚠️ Test assemblies are deliberately out of scope: a boss-script transcription test asserts
    /// these ids by name, which is what a transcription test is for.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_boss_identity_is_named_in_code()
    {
        var offenders = new List<string>();

        foreach (var type in Il.TypesUnder(ProductionAssemblies.CoreModule, Domain.RulesNamespace))
        {
            foreach (var method in Il.AllMethods(type))
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    if (instruction.OpCode != OpCodes.Ldstr ||
                        instruction.Operand is not string literal ||
                        !literal.StartsWith(BossIdPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    offenders.Add(
                        $"{Il.Describe(method)} names the boss identity '{literal}' — 17 §11 is 'all 8 " +
                        "bosses expressed purely in the effect DSL, zero bespoke boss code', and 17 " +
                        "§1.2's table is the single source of truth for the eight rows. Author it as a " +
                        "BossScript in content and let the one BossPhaseController run it.");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "17 §11: the eight boss identities are data. No production rule names one.");
    }

    /// <summary>
    /// 🔒 `17` §1 / `21` §3.3 — no type under the boss namespace holds static state that could carry a
    /// value from one battle into the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first arm is <c>EnemyDerivationRuleTests</c>' shape and for its reason: a filter on
    /// <c>IsStatic &amp;&amp; !IsInitOnly</c> would skip
    /// <c>private static readonly Dictionary&lt;…&gt; _cache</c> entirely, because the field is
    /// <c>initonly</c> and only the object it points at changes. So the test is on the field's
    /// <b>type</b>.
    /// </para>
    /// <para>
    /// 🔴 <b>The second arm closes a hole the enemy rule leaves open, and it is recorded rather than
    /// papered over.</b> That rule skips every compiler-generated field, which means a static
    /// <b>auto-property</b> — <c>internal static Foo Bar { get; }</c> — escapes it entirely, because
    /// its backing field carries <c>CompilerGeneratedAttribute</c>. That exemption is load-bearing and
    /// is kept: <c>BossBuiltIns.All</c> and <c>BossTelegraphs.DamagingOps</c> are exactly that shape
    /// (a fixed table of immutable <c>EffectDefinition</c> records behind an <c>IReadOnlyList</c>),
    /// on <c>EnemyCatalogue.FixedStats</c>' precedent, and banning it would ban the precedent too.
    /// What the second arm bans instead is the backing field of a static auto-property with a
    /// <b>setter</b> — <c>internal static int Foo { get; set; }</c>, whose backing field is
    /// <em>not</em> <c>initonly</c>. That is a settable global, it is real state that survives a
    /// battle, and neither the first arm nor the enemy rule can see it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_boss_engine_holds_no_mutable_static_state()
    {
        var offenders = Subjects().SelectMany(MutableStaticState);

        ArchRule.Empty(
            offenders,
            $"Nothing under {BossesNamespace} caches or accumulates statically: 05 §7 makes the fight " +
            "a replay of a log, and anything the engine remembers between battles is a difference " +
            "between the client's replay and the server's recomputation that no seed explains.");
    }

    /// <summary>
    /// 🔒 `23` §6 / S3 — the floor under the three rules above, <b>and</b> the assertion that puts the
    /// boss engine inside R17's reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each rule above is "no member of set S does X" and passes vacuously when S empties; a rename of
    /// <c>Rules/Combat/Bosses/</c> empties it, and the sibling floor over <c>Rules.Combat</c>
    /// (<c>IntraRulesLayeringRuleTests</c>) would stay satisfied by the combat log next door, because
    /// <c>Il.TypesUnder</c> matches by namespace <b>prefix</b>.
    /// </para>
    /// <para>
    /// 🔒 <b>The second assertion is the load-bearing one, and it is not decoration.</b> R17 forbids
    /// <c>Rules.Effects</c> from naming anything under <c>Rules.Combat</c>, and that is the <em>only</em>
    /// thing keeping `18` §10.1 E6's <c>RANDOM_OUTCOME</c> op from simply calling the boss engine
    /// instead of handing an id across <c>IBossOutcomes</c>. It protects the boss engine <b>by
    /// prefix</b>: move <c>Bosses/</c> out from under <c>Rules.Combat</c> and R17's Effects → Combat
    /// edge stops covering it, with every rule in the suite still green and the seam now optional.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_namespace_these_rules_govern_is_the_one_under_Rules_Combat()
    {
        var found = Subjects().Count;

        Assert.True(
            found >= BossesFloor,
            $"types under {BossesNamespace}: found {found}, floor is {BossesFloor}. All three rules in " +
            "this file are stated over them; empty, they report success over nothing and 17 §11's " +
            "'zero bespoke boss code' is free to become a switch on a boss id again. If this shrank on " +
            "purpose, lower the floor in the same commit and say why.");

        Assert.True(
            Il.IsUnder(BossesNamespace, Domain.CombatRulesNamespace),
            $"{BossesNamespace} is no longer beneath {Domain.CombatRulesNamespace}, so R17's " +
            "Rules.Effects -> Rules.Combat edge no longer covers the boss engine and 18 §10.1 E6's op " +
            "may name a boss type directly — with IntraRulesLayeringRuleTests still green.");
    }

    /// <summary>
    /// Types under <c>Rules/Combat/Bosses/</c> on the commit these rules landed: <c>BossAdds</c>,
    /// <c>BossBuiltIns</c>, <c>BossCoefficients</c>, <c>BossDurationGuardrails</c>,
    /// <c>BossEncounter</c>, <c>BossEncounterBuilder</c>, <c>BossEncounterRequest</c>,
    /// <c>BossMechanic</c>, <c>BossOutcomes</c>, <c>BossPhaseBlock</c>, <c>BossPhaseController</c>,
    /// <c>BossPhaseRules</c>, <c>BossScript</c>, <c>BossSummonSource</c> and <c>BossTelegraphs</c> —
    /// 15, plus whatever record plumbing the compiler emits. The floor is well below that so adding a
    /// type is not a test edit.
    /// </summary>
    private const int BossesFloor = 10;

    private const string DeterministicRngName = "SlayIdleRepeat.Core.Rng.DeterministicRng";

    /// <summary>`17` §1.2's identity prefix — the id space the eight-row coefficient table declares.</summary>
    private const string BossIdPrefix = "BOSS_";

    private static IReadOnlyList<TypeDefinition> Subjects() =>
        Il.TypesUnder(ProductionAssemblies.CoreModule, BossesNamespace)
          .Where(t => !Domain.IsCompilerGenerated(t))
          .ToArray();

    /// <summary>Every piece of static state a type holds that could survive a battle.</summary>
    private static IEnumerable<string> MutableStaticState(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (!field.IsStatic || field.IsLiteral || Domain.IsCompilerGenerated(field.DeclaringType))
            {
                continue;
            }

            // 🔴 The second arm — see the rule's remarks. A compiler-generated static backing field is
            //    exempt from the type check (a fixed table behind an IReadOnlyList is the repository's
            //    established shape for authored constants), but never from the reassignability check:
            //    `static Foo Bar { get; set; }` is a settable global whichever way it is spelled.
            if (Domain.IsCompilerGenerated(field))
            {
                if (!field.IsInitOnly)
                {
                    yield return
                        $"{Il.Describe(field)} is the backing field of a SETTABLE static auto-property " +
                        "— a global anything can reassign between battles. 05 §7 makes the fight a " +
                        "replay of its log; a value that survives one battle into the next is not in " +
                        "the log and not in the seed.";
                }

                continue;
            }

            if (!IsImmutable(field.FieldType, depth: 0))
            {
                yield return
                    $"{Il.Describe(field)} is static and of the mutable type {field.FieldType.FullName} " +
                    "— a cached encounter or built-in set is wrong the moment the content snapshot it " +
                    "summarised is replaced (21 §3.3). `readonly` does not help: it pins the reference, " +
                    "not the contents.";
                continue;
            }

            if (!field.IsInitOnly)
            {
                yield return
                    $"{Il.Describe(field)} is a reassignable static field — 05 §3.1's phase check is a " +
                    "function of the boss's HP and of the phase it has already entered, and anything " +
                    "this namespace remembers between fights is a third input neither 05 §3.1 nor " +
                    "17 §1 names.";
            }
        }
    }

    /// <summary>
    /// True for a type that can carry nothing from one call to the next: a primitive, a string, an
    /// enum, or a <c>readonly</c> value type all of whose instance fields are themselves immutable.
    /// </summary>
    /// <remarks>
    /// The shape — and the <see cref="TypeSpecification"/> arm in particular — is
    /// <c>EnemyDerivationRuleTests</c>', restated here rather than shared because M2-11 owns that file
    /// (steering S12). Cecil resolves an array, pointer or by-ref reference to its <b>element</b> type,
    /// so <c>double[]</c>.Resolve() is <c>System.Double</c> — a value type built out of a primitive,
    /// and therefore immutable by every test below. That arm is what stops a hand-rolled memo wrapping
    /// a <c>double[]</c> from being waved through.
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
