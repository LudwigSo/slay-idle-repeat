namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The 44 operations — the complete verb vocabulary of the effect DSL.</summary>
/// <remarks>
/// <para>
/// There is no per-perk, per-talent or per-boss code. If a design cannot be expressed in this DSL,
/// the DSL is extended — the design is never special-cased. A closed enum is the mechanism: an op
/// that is not a member cannot be authored, and adding one means writing the design as JSON with a
/// new op name and letting schema validation fail until the enum, schema and resolver all extend.
/// </para>
/// <para>
/// The numbers are wire values, on the same rule as <see cref="Primitives.CurrencyId"/>: append,
/// never renumber, never reuse. No <c>0</c> member, so an uninitialised field cannot read as a real
/// op. The declaration order below is documentation only — nothing in the game orders effects by
/// op; effects are ordered by effect id, ordinally, through <see cref="EffectOrder"/>.
/// </para>
/// <para>
/// Declaring an op is not implementing it: the run-and-board family is resolved by the run
/// controller, never by the combat simulator, and a declared op with no resolver yet is a correct,
/// intermediate state.
/// </para>
/// </remarks>
public enum EffectOp
{
    // ---------------------------------------------------------------- §2.1 stat (6)

    /// <summary>Add a flat amount to a stat, before percent aggregation.</summary>
    STAT_ADD_FLAT = 1,

    /// <summary>Add to the additive percent bucket for a stat.</summary>
    STAT_ADD_PCT = 2,

    /// <summary>Multiply the stat after all additive aggregation (Legendary-tier only).</summary>
    STAT_MULT = 3,

    /// <summary>Force a stat to a value (<c>CP_GLASS_HEART</c> only).</summary>
    STAT_SET = 4,

    /// <summary>Convert a percentage of stat A into stat B (<c>PK_TURTLE</c>, <c>PK_JUGGERNAUT</c>).</summary>
    STAT_CONVERT = 5,

    /// <summary>Raise or redirect a stat cap (<c>Perfect Strike</c> keystone).</summary>
    STAT_CAP_OVERRIDE = 6,

    // ---------------------------------------------------------------- §2.2 damage and healing (7)

    /// <summary>Deal damage. <c>value</c> is a multiple of the source's ATK unless <c>valueMode</c> says otherwise.</summary>
    DAMAGE = 7,

    /// <summary>Damage ignoring DEF, DR and mitigation entirely.</summary>
    DAMAGE_TRUE = 8,

    /// <summary>Damage as a percentage of the target's Max HP.</summary>
    DAMAGE_MAXHP_PCT = 9,

    /// <summary>Heal a flat amount or a % of Max HP.</summary>
    HEAL = 10,

    /// <summary>Heal a % of damage just dealt.</summary>
    HEAL_LEECH = 11,

    /// <summary>Grant a <c>WARD</c> absorb of a given size.</summary>
    SHIELD = 12,

    /// <summary>Return a % of incoming damage.</summary>
    REFLECT = 13,

    // ---------------------------------------------------------------- §2.3 status (6)

    /// <summary>Apply one of the 12 statuses.</summary>
    APPLY_STATUS = 14,

    /// <summary>Clear a status or a tag group.</summary>
    REMOVE_STATUS = 15,

    /// <summary>Add duration to an existing status.</summary>
    EXTEND_STATUS = 16,

    /// <summary>Grant immunity to a status for a duration.</summary>
    IMMUNE_STATUS = 17,

    /// <summary>Scale the potency of statuses this actor applies.</summary>
    STATUS_POWER_PCT = 18,

    /// <summary>Scale duration of statuses applied to this actor.</summary>
    STATUS_DURATION_PCT = 19,

    // ---------------------------------------------------------------- §2.4 combat-flow (12)

    /// <summary>Perform an additional attack immediately.</summary>
    EXTRA_ATTACK = 20,

    /// <summary>Multiply the damage of the next N attacks.</summary>
    ATTACK_MULT_NEXT = 21,

    /// <summary>The next N attacks always crit.</summary>
    FORCE_CRIT_NEXT = 22,

    /// <summary>Reduce pet/boss ability cooldowns.</summary>
    REDUCE_COOLDOWN = 23,

    /// <summary>Survive an otherwise-fatal hit at a given HP fraction.</summary>
    SURVIVE_LETHAL = 24,

    /// <summary>Return from 0 HP at a given HP fraction.</summary>
    REVIVE = 25,

    /// <summary>Spawn N enemies of an archetype (boss use).</summary>
    SUMMON = 26,

    /// <summary>
    /// Adjust targeting weight. The hero targets the enemy with the highest
    /// <c>targetPriority</c>, ties broken by lowest current HP; the default is 0, <c>-1</c>
    /// deprioritises and <c>+1</c> forces focus.
    /// </summary>
    SET_TARGET_PRIORITY = 27,

    /// <summary>
    /// Multiply incoming damage. A state flag on the actor, not a second actor: e.g. ×1.6 incoming,
    /// with no change to targeting.
    /// </summary>
    DAMAGE_TAKEN_MULT = 28,

    /// <summary>
    /// Despawn all living summons owned by the target (default <c>SELF</c>). Despawned ≠ killed: no
    /// <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death explosions, no rewards.
    /// </summary>
    CLEAR_SUMMONS = 29,

    /// <summary>
    /// Copy <c>value</c> × the copy-source's final resolved stat onto the holder as a percent-bucket
    /// add for <c>duration</c>. Reads the start-of-tick snapshot, so mutual copies cannot recurse.
    /// <c>stat</c> may be a stat name or <c>HIGHEST_PCT_BONUS</c>.
    /// </summary>
    STAT_COPY = 30,

    // ---------------------------------------------------------------- §2.5 run and board (13)

    /// <summary>Gold, Crowns, Soul Shards, Beast Feed, Enhance Stones, Merge Dust, Honor, Energy.</summary>
    GRANT_CURRENCY = 31,

    /// <summary>A gear item at a given rarity band.</summary>
    GRANT_ITEM = 32,

    /// <summary>A perk, random or specified.</summary>
    GRANT_PERK = 33,

    /// <summary>Raise an owned perk one tier.</summary>
    UPGRADE_PERK = 34,

    // 35 and 36 were MODIFY_DIE_FACE and GRANT_REROLL. Both are gone with the die's special faces
    // and the reroll charge: the die is an ordinary 1..6 and nothing may replace a face or hand out
    // a reroll. The numbers stay retired rather than reused — they are wire values, and a row or a
    // payload written when they meant something must never decode to a different op now.

    /// <summary>Move the token forward/backward N nodes.</summary>
    MOVE_NODES = 37,

    /// <summary>Extend tile preview range.</summary>
    REVEAL_TILES = 38,

    /// <summary>Re-resolve the current tile at a multiplier.</summary>
    RESOLVE_TILE_AGAIN = 39,

    /// <summary>Slot count, price multiplier, forced rarity.</summary>
    MODIFY_SHOP = 40,

    /// <summary>Rarity shift or drop-count bonus.</summary>
    MODIFY_DROP_TABLE = 41,

    /// <summary>Run-scoped curse handling — apply.</summary>
    APPLY_CURSE = 42,

    /// <summary>Run-scoped curse handling — cleanse.</summary>
    CLEANSE_CURSE = 43,

    // ------------------------------------------------- §2.4 combat-flow, a later extension
    //
    // Declared HERE and not among its family above because the numbers are wire values: append,
    //    never renumber (see the type remarks). Its family is COMBAT_FLOW all the same —
    //    EffectOps.FamilyOf is the authority, never the position in this file.

    /// <summary>
    /// The forty-fourth op. Draws one value from the battle's combat stream over the
    /// <c>outcomes</c> weight table and fires the single effect it names — one visible die with
    /// mutually exclusive weighted outcomes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why three <c>chance</c>-gated effects are not this op: conditions are pure functions of
    /// current state and a draw is not state, so three gated effects are three independent draws —
    /// all three can fire, or none can, and neither is a die roll. They would also spend three draw
    /// indices where a single weighted pick spends one, desynchronising every later draw of the
    /// battle.
    /// </para>
    /// <para>
    /// Carries no <c>value</c>: the table is the new <c>outcomes</c> key
    /// (<see cref="EffectDefinition.Outcomes"/>), and each row names a sibling effect id rather than
    /// embedding an effect object inside an effect (<see cref="RandomOutcomeEntry"/> states why).
    /// Its own number is the 1-based index of the row that won.
    /// </para>
    /// </remarks>
    RANDOM_OUTCOME = 44,
}
