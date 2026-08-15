namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The 11 targets.</summary>
/// <remarks>
/// Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c> member.
/// </remarks>
public enum EffectTarget
{
    /// <summary>The effect's own holder.</summary>
    SELF = 1,

    /// <summary>The holder's current attack target.</summary>
    CURRENT_TARGET = 2,

    /// <summary>
    /// All enemies <b>except the attack's primary target</b> — <c>PK_CLEAVE</c>'s splash no longer
    /// double-hits its primary. Valid only inside an attack context; elsewhere it degrades to
    /// <see cref="ALL_ENEMIES"/>.
    /// </summary>
    OTHER_ENEMIES = 3,

    /// <summary>Every living enemy.</summary>
    ALL_ENEMIES = 4,

    /// <summary>The living enemy with the lowest current HP.</summary>
    LOWEST_HP_ENEMY = 5,

    /// <summary>The living enemy with the highest current HP — what a pet's targeted ability picks.</summary>
    HIGHEST_HP_ENEMY = 6,

    /// <summary>A living enemy drawn from the deterministic RNG.</summary>
    RANDOM_ENEMY = 7,

    /// <summary>Every equipped pet.</summary>
    ALL_PETS = 8,

    /// <summary>The actor that dealt the hit being reacted to.</summary>
    ATTACKER = 9,

    /// <summary>
    /// The summoner of the source actor (a sporeling's owner is Sporequeen). On an actor that is
    /// not a summon, the effect is skipped.
    /// </summary>
    OWNER = 10,

    /// <summary>The run itself, for board ops.</summary>
    RUN = 11,
}
