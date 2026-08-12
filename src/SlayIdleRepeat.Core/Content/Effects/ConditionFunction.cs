namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 The 23 condition functions of `18` §4 — pure functions of current state.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>23.</b> `18` §11: <em>"23 conditions = 20 + the three <c>ATTACKER_IS_*</c>"</em>.
/// </para>
/// <para>
/// The same set is the domain of <see cref="ValueScale.Fn"/> (`18` §1.1: <em>"any condition
/// function from §4"</em>), which is why this is one enum and not two.
/// </para>
/// <para>
/// ⚠️ The three <c>ATTACKER_IS_*</c> functions are <em>"valid only in contexts with an attacker
/// (<c>ON_HIT_TAKEN</c>, <c>ON_DODGE</c>/<c>ON_BLOCK</c>, and <c>DAMAGE_TAKEN_MULT</c> evaluation
/// inside `05` §4 step 6); <c>false</c> elsewhere"</em>. That is an evaluation rule (M2-05), not a
/// vocabulary rule: the schema declares them everywhere the DSL allows a condition.
/// </para>
/// <para>
/// 🔒 Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c>
/// member.
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
