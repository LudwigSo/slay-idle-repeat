using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// 🔒 The size of every closed set in the effect DSL, pinned against `18` §11's closing note.
/// </summary>
/// <remarks>
/// Steering S3: a rule driven by reflection over an enum needs a floor on its subject set, or it passes
/// forever over an emptied one. Every parity, partition and totality rule in this area quantifies over
/// one of these enums, so deleting members would take all of them green together.
/// <para>
/// 🔒 <b>Equalities, not floors.</b> These are closed vocabularies fixed by a document, not growing
/// populations — and an op <em>arriving</em> is as much a change to review as one leaving. `18` §10
/// requires the op, the schema and the document to move in one commit, and this number is the fourth
/// thing that has to move with them.
/// </para>
/// </remarks>
public sealed class EffectVocabularyCountTests
{
    [Fact]
    public void There_are_44_ops_in_five_families()
    {
        EffectOps.All.Count.ShouldBe(
            44,
            "18 §11: '44 ops = 41 + CLEAR_SUMMONS + STAT_COPY + RANDOM_OUTCOME'");

        Enum.GetValues<EffectOpFamily>().Length.ShouldBe(5, "18 §2.1-§2.5");
    }

    [Theory]
    [InlineData(EffectOpFamily.STAT, 6)]                 // 18 §2.1
    [InlineData(EffectOpFamily.DAMAGE_AND_HEALING, 7)]   // 18 §2.2
    [InlineData(EffectOpFamily.STATUS, 6)]               // 18 §2.3
    [InlineData(EffectOpFamily.COMBAT_FLOW, 12)]         // 18 §2.4, twelfth is 18 §10.1 E6's RANDOM_OUTCOME
    [InlineData(EffectOpFamily.RUN_AND_BOARD, 13)]       // 18 §2.5
    public void Each_family_holds_the_ops_its_section_tabulates(EffectOpFamily family, int expected)
    {
        EffectOps.All.Count(op => EffectOps.FamilyOf(op) == family).ShouldBe(
            expected,
            $"18 §2 tabulates {expected} ops in {family}; 6+7+6+12+13 = 44");
    }

    [Fact]
    public void There_are_23_trigger_kinds()
    {
        Enum.GetValues<TriggerKind>().Length.ShouldBe(
            23,
            "18 §11: '23 triggers = 21 + ON_DEATH + ON_REVIVE'");
    }

    [Fact]
    public void There_are_23_condition_functions()
    {
        Enum.GetValues<ConditionFunction>().Length.ShouldBe(
            23,
            "18 §11: '23 conditions = 20 + the three ATTACKER_IS_*'");
    }

    [Fact]
    public void There_are_seven_comparators_and_three_combinators()
    {
        Enum.GetValues<ConditionComparator>().Length.ShouldBe(
            7,
            "18 §4: 'eq · neq · lt · lte · gt · gte · between'");

        // ConditionKind is the three combinators plus TERM, which has no JSON token of its own.
        Enum.GetValues<ConditionKind>().Length.ShouldBe(
            4,
            "18 §4's three combinators (all · any · not) plus the comparison node itself");
    }

    [Fact]
    public void There_are_11_targets()
    {
        Enum.GetValues<EffectTarget>().Length.ShouldBe(
            11,
            "18 §11: '11 targets = 9 + OTHER_ENEMIES + OWNER'");
    }

    [Fact]
    public void There_are_26_stats_of_which_14_are_combat_stats()
    {
        StatIds.All.Count.ShouldBe(26, "18 §2.1 — 14 combat plus 12 non-combat");
        StatIds.Combat.Count.ShouldBe(14, "18 §2.1's first list, which is the actor stat block");
        StatIds.NonCombat.Count.ShouldBe(12, "18 §2.1's second list");
    }

    [Fact]
    public void There_are_six_duration_scopes_and_five_stacking_modes()
    {
        Enum.GetValues<DurationScope>().Length.ShouldBe(
            6,
            "18 §11: '6 duration scopes = 5 + PHASE'");

        Enum.GetValues<StackingMode>().Length.ShouldBe(
            5,
            "18 §6: 'ADDITIVE · MULTIPLICATIVE · REPLACE · HIGHEST_WINS · NONE'");
    }

    [Fact]
    public void There_are_eight_value_modes()
    {
        Enum.GetValues<ValueMode>().Length.ShouldBe(
            8,
            "18 §2.2: ATK_MULT (default) · FLAT · SELF_MAXHP_PCT · TARGET_MAXHP_PCT · " +
            "TARGET_MISSING_HP_PCT · DAMAGE_DEALT_PCT · HEAL_AMOUNT · OVERHEAL_AMOUNT");
    }

    /// <summary>
    /// The single-member enums, asserted so that a second member cannot be added without somebody
    /// deciding it is authorised. Steering S6: `18` writes exactly one token for each.
    /// </summary>
    [Fact]
    public void The_single_token_enums_still_hold_exactly_the_one_token_18_authors()
    {
        Enum.GetValues<DurationTerminator>().ShouldBe([DurationTerminator.WARD_BROKEN]);
        Enum.GetValues<DieFaceScope>().ShouldBe([DieFaceScope.NEXT_3_ROLLS]);
    }

    /// <summary>
    /// 🔒 <see cref="StatCapKind"/> left that set in M2-03, and this is the decision that moved it.
    /// </summary>
    /// <remarks>
    /// `18` writes one <c>capKind</c> — <c>HEAL_CEILING</c> (§7.6's <em>Avatar of War</em>) — and it
    /// is a ceiling on <c>Heal()</c> (`05` §4.3), <b>not</b> one of `05` §1's six stat caps. With
    /// only that token the op could satisfy neither half of its own §2.1 description, <em>"raise or
    /// redirect a stat cap"</em>, and `09` §4's <em>Perfect Strike</em> — the one authored redirect
    /// in the game — had no JSON anywhere. Both were added under `18` §10's procedure (op, schema
    /// and document in one commit); the count is asserted so a fourth is a decision too.
    /// </remarks>
    [Fact]
    public void There_are_three_cap_kinds_after_the_18_10_extension()
    {
        Enum.GetValues<StatCapKind>().ShouldBe(
            [StatCapKind.HEAL_CEILING, StatCapKind.STAT_MAX, StatCapKind.REDIRECT_EXCESS]);
    }

    /// <summary>
    /// 🔒 No enum in the DSL has a <c>0</c> member, on <see cref="Core.Primitives.CurrencyId"/>'s
    /// rule: an uninitialised field must not read as a real op, trigger, stat or target.
    /// </summary>
    [Fact]
    public void No_vocabulary_enum_has_a_zero_member()
    {
        var offenders = new List<string>();

        Check<EffectOp>();
        Check<EffectOpFamily>();
        Check<TriggerKind>();
        Check<ConditionFunction>();
        Check<ConditionComparator>();
        Check<ConditionKind>();
        Check<EffectTarget>();
        Check<StatId>();
        Check<StatSelectorKind>();
        Check<ValueMode>();
        Check<DurationScope>();
        Check<DurationTerminator>();
        Check<StackingMode>();
        Check<StatCapKind>();
        Check<DieFaceScope>();

        offenders.ShouldBeEmpty();

        void Check<T>() where T : struct, Enum
        {
            offenders.AddRange(
                Enum.GetValues<T>()
                    .Where(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) == 0)
                    .Select(v => $"{typeof(T).Name}.{v} is 0 — an uninitialised field would read as it"));
        }
    }
}
