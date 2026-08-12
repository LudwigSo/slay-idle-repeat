namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 The 44 operations of `18` §2 — the complete verb vocabulary of the effect DSL.
/// </summary>
/// <remarks>
/// <para>
/// `18`'s headnote is what this enum exists to make true: <em>"There is no per-perk, per-talent or
/// per-boss code. If a design cannot be expressed in this DSL, the DSL is extended — the design is
/// never special-cased."</em> A closed enum is the mechanism: an op that is not a member cannot be
/// authored, and `18` §10 step 1 makes that the intended failure — <em>"write the design as JSON
/// using a new op name and let the schema validation fail"</em>.
/// </para>
/// <para>
/// 🔒 <b>44, in five families.</b> `18` §11 fixes the count and its arithmetic: <em>"44 ops = 41 +
/// <c>CLEAR_SUMMONS</c> + <c>STAT_COPY</c> + <c>RANDOM_OUTCOME</c>"</em>. The families are §2.1 stat
/// (6), §2.2 damage and healing (7), §2.3 status (6), §2.4 combat-flow (12) and §2.5 run and board
/// (13) — 6+7+6+12+13. Two §2.5 ops share one table row (<c>APPLY_CURSE</c> /
/// <c>CLEANSE_CURSE</c>), which is why a row count of the tables gives 43 and the op count gives 44.
/// </para>
/// <para>
/// 🔒 <b>The numbers are wire values</b>, on the same rule as
/// <see cref="Primitives.CurrencyId"/>: append, never renumber, never reuse. No <c>0</c> member, so
/// an uninitialised field cannot read as a real op. The declaration order is `18` §2's, which is
/// documentation only — nothing in the game orders effects by op. `18` §8 orders by <em>effect
/// id</em>, ordinally, through <see cref="EffectOrder"/>.
/// </para>
/// <para>
/// ⚠️ Declaring an op is not implementing it. M2-01 declares 43 and M2-12 the forty-fourth; §2.5's
/// thirteen run and board
/// ops are <em>"resolved by the run controller, never by the combat simulator"</em>, and that
/// controller is M3 over an aggregate that is M1-05. A declared op with no resolver is the correct
/// state until then.
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

    /// <summary>Apply one of the 12 statuses of `05` §5.</summary>
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
    /// Adjust targeting weight (Sporequeen — `17` §8). `05` §3.2: the hero targets the enemy with
    /// the highest <c>targetPriority</c>, ties broken by lowest current HP; the default is 0,
    /// <c>-1</c> deprioritises (sporelings) and <c>+1</c> forces focus.
    /// </summary>
    SET_TARGET_PRIORITY = 27,

    /// <summary>
    /// Multiply incoming damage (Rimehold's Core — `17` §6). A state flag on the actor, not a
    /// second actor: ×1.6 incoming, with no change to targeting.
    /// </summary>
    DAMAGE_TAKEN_MULT = 28,

    /// <summary>
    /// Despawn all living summons owned by the target (default <c>SELF</c>). Despawned ≠ killed: no
    /// <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death explosions, no rewards (Ossuary King's Rise
    /// Again — `17` §4).
    /// </summary>
    CLEAR_SUMMONS = 29,

    /// <summary>
    /// Copy <c>value</c> × the copy-source's <b>final resolved</b> stat onto the holder as a
    /// percent-bucket add for <c>duration</c>. Reads the start-of-tick snapshot, so mutual copies
    /// cannot recurse. <c>stat</c> may be a stat name or <c>HIGHEST_PCT_BONUS</c> (Cogitator's
    /// Recalibrate — `17` §7; <c>PK_PACK_LEADER</c> copying the hero's CRIT to pets).
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

    /// <summary>Replace a die face (<c>Weighted Faces</c>, <c>The Sixth Star</c>, <c>Scramble</c>).</summary>
    MODIFY_DIE_FACE = 35,

    /// <summary>Add reroll charges.</summary>
    GRANT_REROLL = 36,

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

    // ------------------------------------------------- §2.4 combat-flow, the 18 §10 E6 extension
    //
    // 🔒 Declared HERE and not among its family above because the numbers are wire values: append,
    //    never renumber (see the type remarks). Its family is COMBAT_FLOW all the same —
    //    EffectOps.FamilyOf is the authority, never the position in this file.

    /// <summary>
    /// 🔒 <b>The forty-fourth op, added by M2-12 under `18` §10 (extension E6).</b> Draw <b>one</b>
    /// value from the battle's combat stream over the <c>outcomes</c> weight table and fire the
    /// single effect it names — `17` §9's Dicelord <em>Roll of Fate</em>, one visible d6 with three
    /// <b>mutually exclusive</b> weighted outcomes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why three <c>chance</c>-gated effects are not this op.</b> `18` §4's conditions are
    /// <em>"pure functions of current state"</em> and a draw is not state, so three gated effects are
    /// three <b>independent</b> draws: all three can fire, or none can, and neither is a d6. They
    /// would also spend <b>three</b> draw indices where `14` §8.0's <c>WeightedPick</c> spends
    /// <b>one</b>, and the draw counter is the persisted state of the stream — so the two readings
    /// desynchronise every later draw of the battle.
    /// </para>
    /// <para>
    /// ⚠️ Carries <b>no <c>value</c></b>: the table is the new <c>outcomes</c> key
    /// (<see cref="EffectDefinition.Outcomes"/>), and each row names a <b>sibling</b> effect id —
    /// declared by the same owning content — rather than embedding an effect object inside an effect
    /// (<see cref="RandomOutcomeEntry"/> states why). Its own number is the <b>1-based index</b> of
    /// the row that won.
    /// </para>
    /// </remarks>
    RANDOM_OUTCOME = 44,
}
