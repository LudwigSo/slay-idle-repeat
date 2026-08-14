namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The <see cref="EffectOp"/> vocabulary, read as a set: every op, and the `18` §2 family each one
/// belongs to.
/// </summary>
/// <remarks>
/// 🔒 <see cref="FamilyOf"/> names all 44 ops explicitly and throws on anything else. The default
/// arm exists only because C# requires one for an enum (<c>CS8524</c>: an enum-typed value may hold
/// an unnamed number), so it cannot be the thing that catches a forty-fifth op — a new member
/// would fall into it and throw at run time, in whichever battle first authored one.
/// <c>EffectOpsTests.Every_op_belongs_to_exactly_one_family</c> is what catches it, at build time,
/// by enumerating <see cref="All"/>. That is the arm of `18` §10's "op, schema and document in the
/// same commit" rule this repository can actually hold.
/// </remarks>
public static class EffectOps
{
    /// <summary>Every op `18` §2 declares, in declaration order.</summary>
    /// <remarks>
    /// Declaration order, not id order. `18` §8's ordering rule is over effect <em>ids</em>
    /// (<see cref="EffectOrder"/>); nothing in the game orders by op.
    /// </remarks>
    public static IReadOnlyList<EffectOp> All { get; } = Enum.GetValues<EffectOp>();

    /// <summary>The `18` §2 family an op belongs to.</summary>
    public static EffectOpFamily FamilyOf(EffectOp op) => op switch
    {
        EffectOp.STAT_ADD_FLAT or
        EffectOp.STAT_ADD_PCT or
        EffectOp.STAT_MULT or
        EffectOp.STAT_SET or
        EffectOp.STAT_CONVERT or
        EffectOp.STAT_CAP_OVERRIDE => EffectOpFamily.STAT,

        EffectOp.DAMAGE or
        EffectOp.DAMAGE_TRUE or
        EffectOp.DAMAGE_MAXHP_PCT or
        EffectOp.HEAL or
        EffectOp.HEAL_LEECH or
        EffectOp.SHIELD or
        EffectOp.REFLECT => EffectOpFamily.DAMAGE_AND_HEALING,

        EffectOp.APPLY_STATUS or
        EffectOp.REMOVE_STATUS or
        EffectOp.EXTEND_STATUS or
        EffectOp.IMMUNE_STATUS or
        EffectOp.STATUS_POWER_PCT or
        EffectOp.STATUS_DURATION_PCT => EffectOpFamily.STATUS,

        EffectOp.EXTRA_ATTACK or
        EffectOp.ATTACK_MULT_NEXT or
        EffectOp.FORCE_CRIT_NEXT or
        EffectOp.REDUCE_COOLDOWN or
        EffectOp.SURVIVE_LETHAL or
        EffectOp.REVIVE or
        EffectOp.SUMMON or
        EffectOp.SET_TARGET_PRIORITY or
        EffectOp.DAMAGE_TAKEN_MULT or
        EffectOp.CLEAR_SUMMONS or
        EffectOp.STAT_COPY or

        // 🔒 18 §10 E6 — declared at the END of EffectOp because the numbers are wire values, and
        //    §2.4's all the same. This arm is the authority on the family; the position is not.
        EffectOp.RANDOM_OUTCOME => EffectOpFamily.COMBAT_FLOW,

        EffectOp.GRANT_CURRENCY or
        EffectOp.GRANT_ITEM or
        EffectOp.GRANT_PERK or
        EffectOp.UPGRADE_PERK or
        EffectOp.MODIFY_DIE_FACE or
        EffectOp.GRANT_REROLL or
        EffectOp.MOVE_NODES or
        EffectOp.REVEAL_TILES or
        EffectOp.RESOLVE_TILE_AGAIN or
        EffectOp.MODIFY_SHOP or
        EffectOp.MODIFY_DROP_TABLE or
        EffectOp.APPLY_CURSE or
        EffectOp.CLEANSE_CURSE => EffectOpFamily.RUN_AND_BOARD,

        _ => throw new ArgumentOutOfRangeException(
            nameof(op), op,
            "18 §2 declares 44 ops in five families and this value is none of them. A new op belongs " +
            "to a family here, to game-data/schema/effect.schema.json and to 18 itself, in one commit " +
            "(18 §10)."),
    };

    /// <summary>
    /// True for the `18` §2.5 ops the run controller resolves and the combat simulator never does.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is a property of the <b>op</b>, never of the trigger. `18` §2.5's combat-context
    /// exception is explicit that a <em>combat</em> trigger may emit a run/board op — the Dicelord's
    /// Scramble fires <see cref="EffectOp.MODIFY_DIE_FACE"/> from a <see cref="TriggerKind.PERIODIC"/>
    /// trigger — and the simulator appends a <c>RunEffectQueued</c> event rather than resolving it.
    /// A rule that forbade run ops on combat triggers would be wrong.
    /// </remarks>
    public static bool IsRunAndBoard(EffectOp op) => FamilyOf(op) == EffectOpFamily.RUN_AND_BOARD;
}
