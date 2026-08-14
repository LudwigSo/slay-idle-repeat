using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// 🔴 <b>R18 — `18` §10 step 4's <em>"add the op to the client/server parity test"</em>, made
/// mechanical.</b> The eight vocabularies <see cref="BuildPermutationGenerator"/> draws from, written
/// out as literals rather than read off the enums.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why literals, when <c>EffectOps.All</c> exists.</b> A generator that emitted
/// <c>Enum.GetValues&lt;EffectOp&gt;()</c> could not be <em>wrong</em>, and could therefore not be
/// tested. Steering S3 asks for a floor on the subject set of every metadata-driven rule, in both
/// directions; the floor only has teeth when the emitted set and the catalogue are two independent
/// statements that a test compares.
/// <c>DslDeterminismBaselineTests.Every_op_18_declares_is_emitted_by_the_permutation_generator</c>
/// is that comparison, and dropping a single line from the list below turns it red — which is the
/// red-then-green proof this file exists to make possible.
/// </para>
/// <para>
/// ⚠️ <b>These are emission <em>sets</em>, not coverage claims.</b> Membership here says the
/// generator may draw the token; the coverage tests assert against what the corpus
/// <em>actually emitted</em> over all 10 000 permutations, read back off the generated
/// <see cref="EffectDefinition"/>s. A token listed here but never reached by any anchor or filler
/// would still fail.
/// </para>
/// <para>
/// 🔒 <b>The counts are `18` §11's, and M2-01's <c>EffectVocabularyCountTests</c> is the floor that
/// keeps this honest</b> — 44 ops, 23 triggers, 23 conditions, 11 targets, 26 stats, 6 duration
/// scopes, 5 stacking modes, 8 value modes. A 45th op cannot be added without either appearing here
/// or failing <c>Every_op_18_declares_is_emitted_by_the_permutation_generator</c>.
/// </para>
/// <para>
/// 🔴 <b>The 44th arrived that way.</b> `18` §10.1 E6's <c>RANDOM_OUTCOME</c> was declared by M2-12's
/// Phase 1a and reached no permutation, so that rule was red until it was listed here — which is the
/// mechanism working, not a break. Listing it moved every hash in <c>DslDeterminismBaseline.json</c>
/// (see the paragraph below), and `18` §11.1 documents that as a regeneration.
/// </para>
/// <para>
/// ⚠️ <b>The <em>order</em> of these lists is part of the corpus definition, not decoration.</b> The
/// generator anchors each axis by <c>list[index % list.Count]</c>, so inserting, removing or
/// reordering a token shifts every subsequent permutation and moves every hash in
/// <c>DslDeterminismBaseline.json</c>. That is correct and is worth knowing before the edit: a change
/// to `18`'s vocabulary <b>is</b> a change to the baseline, and the regeneration command exists for
/// exactly that case. It is not a determinism break, and the reviewer says so in <c>review.why</c>.
/// </para>
/// <para>
/// ⚠️ Every list is built with <c>new List&lt;T&gt; { … }</c> rather than a collection expression or
/// an array literal: Roslyn folds an array literal of several constants into a
/// <c>&lt;PrivateImplementationDetails&gt;</c> type in the <b>global</b> namespace, which is the shape
/// M1-12's <c>Every_Core_type_lives_under_a_documented_namespace</c> rejects. That rule's subject set
/// is <c>SlayIdleRepeat.Core</c> and not this assembly, so nothing here would fail today — the form is
/// used anyway, because the day this vocabulary moves into a production helper is not the day to
/// discover it.
/// </para>
/// </remarks>
internal static class EffectVocabularyEmissionSets
{
    /// <summary>`18` §2's 44 ops, in `18` §2's five families.</summary>
    internal static IReadOnlyList<EffectOp> Ops { get; } = new List<EffectOp>
    {
        // §2.1 stat (6)
        EffectOp.STAT_ADD_FLAT,
        EffectOp.STAT_ADD_PCT,
        EffectOp.STAT_MULT,
        EffectOp.STAT_SET,
        EffectOp.STAT_CONVERT,
        EffectOp.STAT_CAP_OVERRIDE,

        // §2.2 damage and healing (7)
        EffectOp.DAMAGE,
        EffectOp.DAMAGE_TRUE,
        EffectOp.DAMAGE_MAXHP_PCT,
        EffectOp.HEAL,
        EffectOp.HEAL_LEECH,
        EffectOp.SHIELD,
        EffectOp.REFLECT,

        // §2.3 status (6)
        EffectOp.APPLY_STATUS,
        EffectOp.REMOVE_STATUS,
        EffectOp.EXTEND_STATUS,
        EffectOp.IMMUNE_STATUS,
        EffectOp.STATUS_POWER_PCT,
        EffectOp.STATUS_DURATION_PCT,

        // §2.4 combat-flow (12)
        EffectOp.EXTRA_ATTACK,
        EffectOp.ATTACK_MULT_NEXT,
        EffectOp.FORCE_CRIT_NEXT,
        EffectOp.REDUCE_COOLDOWN,
        EffectOp.SURVIVE_LETHAL,
        EffectOp.REVIVE,
        EffectOp.SUMMON,
        EffectOp.SET_TARGET_PRIORITY,
        EffectOp.DAMAGE_TAKEN_MULT,
        EffectOp.CLEAR_SUMMONS,
        EffectOp.STAT_COPY,
        EffectOp.RANDOM_OUTCOME,

        // §2.5 run and board (13)
        EffectOp.GRANT_CURRENCY,
        EffectOp.GRANT_ITEM,
        EffectOp.GRANT_PERK,
        EffectOp.UPGRADE_PERK,
        EffectOp.MODIFY_DIE_FACE,
        EffectOp.GRANT_REROLL,
        EffectOp.MOVE_NODES,
        EffectOp.REVEAL_TILES,
        EffectOp.RESOLVE_TILE_AGAIN,
        EffectOp.MODIFY_SHOP,
        EffectOp.MODIFY_DROP_TABLE,
        EffectOp.APPLY_CURSE,
        EffectOp.CLEANSE_CURSE,
    };

    /// <summary>`18` §3.1's 23 trigger kinds.</summary>
    internal static IReadOnlyList<TriggerKind> Triggers { get; } = new List<TriggerKind>
    {
        TriggerKind.ALWAYS,
        TriggerKind.ON_BATTLE_START,
        TriggerKind.ON_BATTLE_END,
        TriggerKind.ON_ATTACK,
        TriggerKind.ON_HIT,
        TriggerKind.ON_CRIT,
        TriggerKind.ON_HIT_TAKEN,
        TriggerKind.ON_DODGE,
        TriggerKind.ON_BLOCK,
        TriggerKind.ON_KILL,
        TriggerKind.ON_DEATH,
        TriggerKind.ON_REVIVE,
        TriggerKind.ON_LOW_HP,
        TriggerKind.ON_LETHAL,
        TriggerKind.ON_HEAL,
        TriggerKind.PERIODIC,
        TriggerKind.ON_PHASE_ENTER,
        TriggerKind.ON_TILE_RESOLVED,
        TriggerKind.ON_ROLL,
        TriggerKind.ON_PERK_TAKEN,
        TriggerKind.ON_STAGE_GATE,
        TriggerKind.ON_RUN_START,
        TriggerKind.ON_RUN_END,
    };

    /// <summary>`18` §4's 23 condition functions.</summary>
    internal static IReadOnlyList<ConditionFunction> Conditions { get; } = new List<ConditionFunction>
    {
        ConditionFunction.SELF_HP_PCT,
        ConditionFunction.TARGET_HP_PCT,
        ConditionFunction.SELF_MISSING_HP_PCT,
        ConditionFunction.ENEMY_COUNT,
        ConditionFunction.TARGET_IS_ELITE,
        ConditionFunction.TARGET_IS_BOSS,
        ConditionFunction.BATTLE_TIME,
        ConditionFunction.BATTLE_TIME_REMAINING_EST,
        ConditionFunction.HAS_STATUS,
        ConditionFunction.STATUS_STACKS,
        ConditionFunction.PERK_COUNT,
        ConditionFunction.DISTINCT_PERK_CATEGORIES,
        ConditionFunction.PET_COUNT,
        ConditionFunction.DIE_FACE_COUNT,
        ConditionFunction.GOLD_HELD,
        ConditionFunction.BATTLES_WON_THIS_RUN,
        ConditionFunction.STAGE_INDEX,
        ConditionFunction.CHAPTER,
        ConditionFunction.TIER,
        ConditionFunction.IS_PVP,
        ConditionFunction.ATTACKER_IS_ELITE,
        ConditionFunction.ATTACKER_IS_BOSS,
        ConditionFunction.ATTACKER_IS_SUMMON,
    };

    /// <summary>`18` §5's 11 targets.</summary>
    internal static IReadOnlyList<EffectTarget> Targets { get; } = new List<EffectTarget>
    {
        EffectTarget.SELF,
        EffectTarget.CURRENT_TARGET,
        EffectTarget.OTHER_ENEMIES,
        EffectTarget.ALL_ENEMIES,
        EffectTarget.LOWEST_HP_ENEMY,
        EffectTarget.HIGHEST_HP_ENEMY,
        EffectTarget.RANDOM_ENEMY,
        EffectTarget.ALL_PETS,
        EffectTarget.ATTACKER,
        EffectTarget.OWNER,
        EffectTarget.RUN,
    };

    /// <summary>`18` §2.1's 26 stats — the 14 combat stats and the 12 run and meta modifiers.</summary>
    internal static IReadOnlyList<StatId> Stats { get; } = new List<StatId>
    {
        StatId.MAX_HP,
        StatId.ATK,
        StatId.DEF,
        StatId.ASPD,
        StatId.CRIT,
        StatId.CDMG,
        StatId.LIFESTEAL,
        StatId.DODGE,
        StatId.BLOCK,
        StatId.PEN,
        StatId.DMG_PCT,
        StatId.DR_PCT,
        StatId.HEAL_PCT,
        StatId.THORNS,
        StatId.GOLD_PCT,
        StatId.CROWNS_PCT,
        StatId.DROP_CHANCE,
        StatId.RARITY_SHIFT,
        StatId.ENERGY_REGEN_PCT,
        StatId.PET_AURA_PCT,
        StatId.REROLL_CHARGES,
        StatId.TILE_PREVIEW,
        StatId.SHOP_PRICE_PCT,
        StatId.XP_PCT,
        StatId.BEAST_FEED_PCT,
        StatId.STONE_PCT,
    };

    /// <summary>`18` §6's 6 duration scopes, after `16` A7 added <c>PHASE</c>.</summary>
    internal static IReadOnlyList<DurationScope> DurationScopes { get; } = new List<DurationScope>
    {
        DurationScope.INSTANT,
        DurationScope.BATTLE,
        DurationScope.PHASE,
        DurationScope.STAGE,
        DurationScope.RUN,
        DurationScope.PERMANENT,
    };

    /// <summary>`18` §6's 5 stacking modes.</summary>
    internal static IReadOnlyList<StackingMode> StackingModes { get; } = new List<StackingMode>
    {
        StackingMode.ADDITIVE,
        StackingMode.MULTIPLICATIVE,
        StackingMode.REPLACE,
        StackingMode.HIGHEST_WINS,
        StackingMode.NONE,
    };

    /// <summary>`18` §2.2's 8 value modes.</summary>
    internal static IReadOnlyList<ValueMode> ValueModes { get; } = new List<ValueMode>
    {
        ValueMode.ATK_MULT,
        ValueMode.FLAT,
        ValueMode.SELF_MAXHP_PCT,
        ValueMode.TARGET_MAXHP_PCT,
        ValueMode.TARGET_MISSING_HP_PCT,
        ValueMode.DAMAGE_DEALT_PCT,
        ValueMode.HEAL_AMOUNT,
        ValueMode.OVERHEAL_AMOUNT,
    };

    /// <summary>
    /// 🔴 The extension keys `18` §10.1 records as taken during M2, as a closed vocabulary of its
    /// own — the list <c>Every_18_10_1_extension_reaches_the_permutation_corpus</c> asserts against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `18` §10's extension procedure ends <em>"add the op to the client/server parity test"</em>. Five
    /// extensions (E1-E5) landed under that procedure in M2-03, and <b>four</b> more keys arrived on
    /// the same rule without a §10.1 row: `18` §3.1's R11 gave <c>ON_ATTACK</c> a <c>chance</c>
    /// (M2-04), and M2-06 gave <c>valueScale</c> the three argument keys <c>statusId</c>,
    /// <c>faceKind</c> and <c>category</c>. <b>All ten constants are named here</b> — ten rather than
    /// nine because E2 contributes both of <c>STAT_CAP_OVERRIDE</c>'s <c>capKind</c>s, which `18`
    /// §10.1 records as one row and which are two entirely different halves of step 9. A generator
    /// that only emitted `18`'s pre-existing shapes would let every one of them drift unnoticed,
    /// which is the failure §10 step 4 exists to prevent.
    /// </para>
    /// <para>
    /// ⚠️ <b>Unlike the eight enum-backed axes, this list has no closed enum behind it</b> — the
    /// extensions are document rows, not a C# vocabulary — so nothing mechanical can be its
    /// independent authority. Two things stand in for one:
    /// <c>The_extension_key_vocabulary_is_18_10_1s_arithmetic</c> pins the count with the arithmetic
    /// spelled out, and <c>Every_named_18_10_1_extension_reaches_the_corpus</c> drives a
    /// <em>third</em>, hand-written list of the ten ids as <c>[InlineData]</c>. Deleting a constant
    /// from this file therefore fails twice, where comparing the emitted set with itself would have
    /// failed not at all.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> ExtensionKeys { get; } = new List<string>
    {
        ToStatOnStatConvert,
        CapKindStatMax,
        CapKindRedirectExcess,
        ChargesOnNextAttackOps,
        ValueModeOnSurviveLethal,
        StatusTagOnRemoveStatus,
        ChanceOnOnAttack,
        ValueScaleStatusId,
        ValueScaleFaceKind,
        ValueScaleCategory,
    };

    /// <summary>`18` §10.1 E1 — <c>STAT_CONVERT</c>'s destination stat.</summary>
    internal const string ToStatOnStatConvert = "E1 toStat on STAT_CONVERT";

    /// <summary>`18` §10.1 E2 — <c>STAT_CAP_OVERRIDE</c>'s raise half.</summary>
    internal const string CapKindStatMax = "E2 capKind STAT_MAX on STAT_CAP_OVERRIDE";

    /// <summary>`18` §10.1 E2 — <c>STAT_CAP_OVERRIDE</c>'s redirect half, with its <c>toStat</c>.</summary>
    internal const string CapKindRedirectExcess = "E2 capKind REDIRECT_EXCESS + toStat on STAT_CAP_OVERRIDE";

    /// <summary>`18` §10.1 E3 — the N of "the next N attacks".</summary>
    internal const string ChargesOnNextAttackOps = "E3 charges on ATTACK_MULT_NEXT / FORCE_CRIT_NEXT";

    /// <summary>`18` §10.1 E4 — which unit <c>SURVIVE_LETHAL</c>'s value is in.</summary>
    internal const string ValueModeOnSurviveLethal = "E4 valueMode on SURVIVE_LETHAL";

    /// <summary>`18` §10.1 E5 — <c>REMOVE_STATUS</c>'s tag group.</summary>
    internal const string StatusTagOnRemoveStatus = "E5 statusTag on REMOVE_STATUS";

    /// <summary>`18` §3.1 R11 (M2-04) — <c>ON_ATTACK</c>'s per-attack probability.</summary>
    internal const string ChanceOnOnAttack = "R11 chance on the ON_ATTACK trigger";

    /// <summary>M2-06 — <c>valueScale</c>'s <c>statusId</c> argument.</summary>
    internal const string ValueScaleStatusId = "M2-06 statusId argument on valueScale";

    /// <summary>M2-06 — <c>valueScale</c>'s <c>faceKind</c> argument.</summary>
    internal const string ValueScaleFaceKind = "M2-06 faceKind argument on valueScale";

    /// <summary>M2-06 — <c>valueScale</c>'s <c>category</c> argument.</summary>
    internal const string ValueScaleCategory = "M2-06 category argument on valueScale";
}
