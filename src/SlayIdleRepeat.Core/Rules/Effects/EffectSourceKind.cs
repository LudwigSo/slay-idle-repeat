namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>The ten sources step 1 collects from, in the order it names them.</summary>
/// <remarks>
/// Collection order (this list) and application order (effect-id order, used at aggregation time)
/// are different things — <c>PERKS</c> carrying "in draft order" is a statement about the perks a
/// build holds, not about arithmetic. That said, this declaration order is still load-bearing: it's
/// the first tiebreak of <see cref="EffectResolutionOrder"/>, which makes the resolution order total
/// when a build collects two effects sharing one id.
/// </remarks>
internal enum EffectSourceKind
{
    /// <summary>Equipped gear. M4-03.</summary>
    GEAR = 1,

    /// <summary>Gear affixes. M4-03.</summary>
    AFFIXES = 2,

    /// <summary>Set-bonus engines. M4-03.</summary>
    SET_BONUSES = 3,

    /// <summary>The talent tree. M4-06.</summary>
    TALENTS = 4,

    /// <summary>Pet auras. M4-07.</summary>
    /// <remarks>
    /// The aura block only — a pet's active ability runs on its own cooldown and is not a build
    /// effect, so step 1 does not collect it.
    /// </remarks>
    PET_AURAS = 5,

    /// <summary>Equipped mount. M4-08.</summary>
    MOUNT = 6,

    /// <summary>Run buffs — shop consumables and run-scoped grants. M3-08.</summary>
    RUN_BUFFS = 7,

    /// <summary>Shrine buffs. M3-11.</summary>
    SHRINE_BUFFS = 8,

    /// <summary>The curse catalogue. M3-11.</summary>
    CURSES = 9,

    /// <summary>Perks, in draft order. M3-07.</summary>
    /// <remarks>Last in the source list, so a perk loses the same-id tiebreak to every other source.</remarks>
    PERKS = 10,
}
