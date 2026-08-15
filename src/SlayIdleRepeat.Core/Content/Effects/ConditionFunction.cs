namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The 23 condition functions — pure functions of current state.</summary>
/// <remarks>
/// <para>
/// The same set is the domain of <see cref="ValueScale.Fn"/>, which is why this is one enum and not
/// two.
/// </para>
/// <para>
/// The three <c>ATTACKER_IS_*</c> functions are valid only in contexts with an attacker; <c>false</c>
/// elsewhere. That is an evaluation rule, not a vocabulary rule: the schema declares them everywhere
/// the DSL allows a condition.
/// </para>
/// <para>
/// Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c> member.
/// </para>
/// </remarks>
public enum ConditionFunction
{
    /// <summary>0..1.</summary>
    SELF_HP_PCT = 1,

    /// <summary>0..1.</summary>
    TARGET_HP_PCT = 2,

    /// <summary>0..1.</summary>
    SELF_MISSING_HP_PCT = 3,

    /// <summary>int.</summary>
    ENEMY_COUNT = 4,

    /// <summary>bool.</summary>
    TARGET_IS_ELITE = 5,

    /// <summary>bool.</summary>
    TARGET_IS_BOSS = 6,

    /// <summary>Seconds elapsed.</summary>
    BATTLE_TIME = 7,

    /// <summary>Seconds to the 70 s enrage.</summary>
    BATTLE_TIME_REMAINING_EST = 8,

    /// <summary>bool, by status id.</summary>
    HAS_STATUS = 9,

    /// <summary>int, by status id.</summary>
    STATUS_STACKS = 10,

    /// <summary>int, optionally by category.</summary>
    PERK_COUNT = 11,

    /// <summary>int.</summary>
    DISTINCT_PERK_CATEGORIES = 12,

    /// <summary>int.</summary>
    PET_COUNT = 13,

    /// <summary>int, by face kind.</summary>
    DIE_FACE_COUNT = 14,

    /// <summary>int.</summary>
    GOLD_HELD = 15,

    /// <summary>int.</summary>
    BATTLES_WON_THIS_RUN = 16,

    /// <summary>1..3.</summary>
    STAGE_INDEX = 17,

    /// <summary>int.</summary>
    CHAPTER = 18,

    /// <summary>enum — the difficulty tier.</summary>
    TIER = 19,

    /// <summary>
    /// bool — <b>the hook that lets a perk behave differently in a duel</b>. `18` §9.3 keeps the
    /// Dice &amp; Board and Economy perk ban in PvP; this exists so gear affixes like
    /// <c>+X% Gold Gain</c> can be skipped rather than converted.
    /// </summary>
    IS_PVP = 20,

    /// <summary>bool. Valid only in contexts with an attacker; <c>false</c> elsewhere.</summary>
    ATTACKER_IS_ELITE = 21,

    /// <summary>bool. Valid only in contexts with an attacker; <c>false</c> elsewhere.</summary>
    ATTACKER_IS_BOSS = 22,

    /// <summary>bool. Valid only in contexts with an attacker; <c>false</c> elsewhere.</summary>
    ATTACKER_IS_SUMMON = 23,
}
