namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The 23 trigger kinds — <em>when</em> an effect fires.</summary>
/// <remarks>
/// <para>
/// Six of these fire on the run layer rather than in a battle — <see cref="ON_TILE_RESOLVED"/>,
/// <see cref="ON_ROLL"/>, <see cref="ON_PERK_TAKEN"/>, <see cref="ON_STAGE_GATE"/>,
/// <see cref="ON_RUN_START"/> and <see cref="ON_RUN_END"/>. They are declared here and wired by the
/// run controller, never by the combat simulator.
/// </para>
/// <para>
/// Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c> member.
/// </para>
/// </remarks>
public enum TriggerKind
{
    /// <summary>Passive, always active. No parameters.</summary>
    ALWAYS = 1,

    /// <summary>Once when a battle begins. No parameters.</summary>
    ON_BATTLE_START = 2,

    /// <summary>Once when a battle ends. Parameter: <c>onlyIfWon</c>.</summary>
    ON_BATTLE_END = 3,

    /// <summary>Each attack made. Parameter: <c>everyNth</c>.</summary>
    ON_ATTACK = 4,

    /// <summary>Each successful hit landed. Parameter: <c>chance</c>.</summary>
    ON_HIT = 5,

    /// <summary>Each critical hit. Parameter: <c>chance</c>.</summary>
    ON_CRIT = 6,

    /// <summary>Each hit received. Parameters: <c>chance</c>, <c>cooldown</c>.</summary>
    ON_HIT_TAKEN = 7,

    /// <summary>On a successful dodge. Parameter: <c>cooldown</c>.</summary>
    ON_DODGE = 8,

    /// <summary>On a successful block. Parameter: <c>cooldown</c>.</summary>
    ON_BLOCK = 9,

    /// <summary>The owner kills an enemy. Parameter: <c>everyNth</c>.</summary>
    ON_KILL = 10,

    /// <summary>
    /// The owning actor dies — fires before removal. E.g. an on-death explosion or heal. No parameters.
    /// </summary>
    ON_DEATH = 11,

    /// <summary>
    /// The owning actor returns from 0 HP — the <see cref="EffectOp.REVIVE"/> op or the ad revive.
    /// <see cref="EffectOp.SURVIVE_LETHAL"/> does not count, because the actor never died. No parameters.
    /// </summary>
    ON_REVIVE = 12,

    /// <summary>Self HP crosses a threshold downward. Parameters: <c>threshold</c>, <c>once</c>.</summary>
    ON_LOW_HP = 13,

    /// <summary>Would take fatal damage. Parameter: <c>once</c>.</summary>
    ON_LETHAL = 14,

    /// <summary>
    /// Healing is received. The only context in which the <see cref="ValueMode.HEAL_AMOUNT"/> and
    /// <see cref="ValueMode.OVERHEAL_AMOUNT"/> value modes are meaningful. No parameters.
    /// </summary>
    ON_HEAL = 15,

    /// <summary>Every N seconds of battle time. Parameters: <c>interval</c>, <c>startDelay</c>.</summary>
    PERIODIC = 16,

    /// <summary>Boss phase begins. Parameter: <c>phase</c>.</summary>
    ON_PHASE_ENTER = 17,

    /// <summary>A board tile resolves. Parameter: <c>tileType</c>. Run layer.</summary>
    ON_TILE_RESOLVED = 18,

    /// <summary>A die roll completes. Parameter: <c>faceKind</c>. Run layer.</summary>
    ON_ROLL = 19,

    /// <summary>A perk is drafted. Parameter: <c>category</c>. Run layer.</summary>
    ON_PERK_TAKEN = 20,

    /// <summary>A stage boundary is crossed. No parameters. Run layer.</summary>
    ON_STAGE_GATE = 21,

    /// <summary>A run begins. No parameters. Run layer.</summary>
    ON_RUN_START = 22,

    /// <summary>A run ends. No parameters. Run layer.</summary>
    ON_RUN_END = 23,
}
