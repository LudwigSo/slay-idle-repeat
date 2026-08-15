namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>The twelve parameters a trigger kind can carry, as a set — the C# statement of the partition the content schema states in JSON.</summary>
/// <remarks>
/// <see cref="Content.Effects.EffectTrigger"/> is one record with twelve nullable parameters. The
/// schema partitions authored triggers already, but a trigger built in code (the balance harness, a
/// test, a built-in system effect) is outside that enforcement, so
/// <see cref="TriggerCatalogue.Validate"/> restates the partition for those callers.
/// </remarks>
[Flags]
internal enum TriggerParameter
{
    /// <summary>The kind takes no parameters at all.</summary>
    NONE = 0,

    /// <summary><c>ON_BATTLE_END</c>.</summary>
    ONLY_IF_WON = 1 << 0,

    /// <summary><c>ON_ATTACK</c> and <c>ON_KILL</c>.</summary>
    EVERY_NTH = 1 << 1,

    /// <summary><c>ON_HIT</c>, <c>ON_CRIT</c>, <c>ON_HIT_TAKEN</c>, and <c>ON_ATTACK</c>.</summary>
    CHANCE = 1 << 2,

    /// <summary><c>ON_HIT_TAKEN</c>, <c>ON_DODGE</c>, <c>ON_BLOCK</c>.</summary>
    COOLDOWN = 1 << 3,

    /// <summary><c>ON_LOW_HP</c>.</summary>
    THRESHOLD = 1 << 4,

    /// <summary><c>ON_LOW_HP</c>, <c>ON_LETHAL</c>. A boolean.</summary>
    ONCE = 1 << 5,

    /// <summary><c>PERIODIC</c>.</summary>
    INTERVAL = 1 << 6,

    /// <summary><c>PERIODIC</c>.</summary>
    START_DELAY = 1 << 7,

    /// <summary><c>ON_PHASE_ENTER</c>.</summary>
    PHASE = 1 << 8,

    /// <summary><c>ON_TILE_RESOLVED</c>.</summary>
    TILE_TYPE = 1 << 9,

    /// <summary><c>ON_ROLL</c>.</summary>
    FACE_KIND = 1 << 10,

    /// <summary><c>ON_PERK_TAKEN</c>.</summary>
    CATEGORY = 1 << 11,
}
