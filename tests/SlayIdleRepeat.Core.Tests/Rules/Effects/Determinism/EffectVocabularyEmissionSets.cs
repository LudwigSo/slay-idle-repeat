using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// The eight vocabularies <see cref="BuildPermutationGenerator"/> draws from, written out as literals
/// rather than read off the enums.
/// </summary>
/// <remarks>
/// A generator that emitted <c>Enum.GetValues&lt;EffectOp&gt;()</c> could not be wrong, and could
/// therefore not be tested. The floor only has teeth when the emitted set and the catalogue are two
/// independent statements a test compares — dropping a single line below turns that comparison red.
/// <para>
/// These are emission sets, not coverage claims: membership says the generator may draw the token,
/// while the coverage tests assert what the corpus actually emitted over all 10 000 permutations. A
/// token listed here but never reached would still fail.
/// </para>
/// <para>
/// The order is part of the corpus definition: the generator anchors each axis by
/// <c>list[index % list.Count]</c>, so inserting, removing or reordering a token shifts every
/// subsequent permutation and moves every hash in the baseline. That is correct and worth knowing
/// before the edit — a vocabulary change is a change to the baseline, not a determinism break, and the
/// reviewer says so in <c>review.why</c>.
/// </para>
/// <para>
/// Every list is <c>new List&lt;T&gt; { … }</c> rather than a collection expression: Roslyn folds an
/// array literal of several constants into a <c>&lt;PrivateImplementationDetails&gt;</c> type in the
/// global namespace, which a namespace rule elsewhere in the repository rejects for production code.
/// </para>
/// </remarks>
internal static class EffectVocabularyEmissionSets
{
    /// <summary>The 44 ops, in five families.</summary>
    internal static IReadOnlyList<EffectOp> Ops { get; } = new List<EffectOp>
    {
        // stat (6)
        EffectOp.STAT_ADD_FLAT,
        EffectOp.STAT_ADD_PCT,
        EffectOp.STAT_MULT,
        EffectOp.STAT_SET,
        EffectOp.STAT_CONVERT,
        EffectOp.STAT_CAP_OVERRIDE,

        // damage and healing (7)
        EffectOp.DAMAGE,
        EffectOp.DAMAGE_TRUE,
        EffectOp.DAMAGE_MAXHP_PCT,
        EffectOp.HEAL,
        EffectOp.HEAL_LEECH,
        EffectOp.SHIELD,
        EffectOp.REFLECT,

        // status (6)
        EffectOp.APPLY_STATUS,
        EffectOp.REMOVE_STATUS,
        EffectOp.EXTEND_STATUS,
        EffectOp.IMMUNE_STATUS,
        EffectOp.STATUS_POWER_PCT,
        EffectOp.STATUS_DURATION_PCT,

        // combat-flow (12)
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

        // run and board (13)
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

    /// <summary>The 23 trigger kinds.</summary>
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

    /// <summary>The 23 condition functions.</summary>
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

    /// <summary>The 11 targets.</summary>
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

    /// <summary>The 26 stats — the 14 combat stats and the 12 run and meta modifiers.</summary>
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

    /// <summary>The 6 duration scopes.</summary>
    internal static IReadOnlyList<DurationScope> DurationScopes { get; } = new List<DurationScope>
    {
        DurationScope.INSTANT,
        DurationScope.BATTLE,
        DurationScope.PHASE,
        DurationScope.STAGE,
        DurationScope.RUN,
        DurationScope.PERMANENT,
    };

    /// <summary>The 5 stacking modes.</summary>
    internal static IReadOnlyList<StackingMode> StackingModes { get; } = new List<StackingMode>
    {
        StackingMode.ADDITIVE,
        StackingMode.MULTIPLICATIVE,
        StackingMode.REPLACE,
        StackingMode.HIGHEST_WINS,
        StackingMode.NONE,
    };

    /// <summary>The 8 value modes.</summary>
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

    /// <summary>The extension keys taken during M2, as a closed vocabulary of its own.</summary>
    /// <remarks>
    /// Ten constants are named here — ten rather than nine because one contributes both of
    /// <c>STAT_CAP_OVERRIDE</c>'s <c>capKind</c>s, recorded as one row and being two entirely different
    /// halves of the cap step. Unlike the eight enum-backed axes this list has no closed enum behind
    /// it, so nothing mechanical can be its independent authority: a count pinned with the arithmetic
    /// spelled out, and a third hand-written list of the ten ids driven as <c>[InlineData]</c>, stand
    /// in for one. Deleting a constant therefore fails twice, where comparing the emitted set with
    /// itself would have failed not at all.
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

    /// <summary><c>STAT_CONVERT</c>'s destination stat.</summary>
    internal const string ToStatOnStatConvert = "E1 toStat on STAT_CONVERT";

    /// <summary><c>STAT_CAP_OVERRIDE</c>'s raise half.</summary>
    internal const string CapKindStatMax = "E2 capKind STAT_MAX on STAT_CAP_OVERRIDE";

    /// <summary><c>STAT_CAP_OVERRIDE</c>'s redirect half, with its <c>toStat</c>.</summary>
    internal const string CapKindRedirectExcess = "E2 capKind REDIRECT_EXCESS + toStat on STAT_CAP_OVERRIDE";

    /// <summary>The N of "the next N attacks".</summary>
    internal const string ChargesOnNextAttackOps = "E3 charges on ATTACK_MULT_NEXT / FORCE_CRIT_NEXT";

    /// <summary>Which unit <c>SURVIVE_LETHAL</c>'s value is in.</summary>
    internal const string ValueModeOnSurviveLethal = "E4 valueMode on SURVIVE_LETHAL";

    /// <summary><c>REMOVE_STATUS</c>'s tag group.</summary>
    internal const string StatusTagOnRemoveStatus = "E5 statusTag on REMOVE_STATUS";

    /// <summary><c>ON_ATTACK</c>'s per-attack probability.</summary>
    internal const string ChanceOnOnAttack = "R11 chance on the ON_ATTACK trigger";

    /// <summary><c>valueScale</c>'s <c>statusId</c> argument.</summary>
    internal const string ValueScaleStatusId = "M2-06 statusId argument on valueScale";

    /// <summary><c>valueScale</c>'s <c>faceKind</c> argument.</summary>
    internal const string ValueScaleFaceKind = "M2-06 faceKind argument on valueScale";

    /// <summary><c>valueScale</c>'s <c>category</c> argument.</summary>
    internal const string ValueScaleCategory = "M2-06 category argument on valueScale";
}
