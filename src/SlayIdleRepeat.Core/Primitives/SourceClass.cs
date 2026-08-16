namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The ten grant-source classes every drop, chest, egg, crate, enhance, draft, wheel spin and
/// minigame payout in the game belongs to.
/// </summary>
/// <remarks>
/// An enum rather than an invention — the vocabulary is already authored in
/// <c>tuning/luck.json#/sourceClasses</c>, one row per member, and the class of a grant decides
/// which pity counter it advances. Adding a new grant source means assigning it one of these; the
/// content schema is what refuses a source with no class.
/// <para>
/// The member set is closed; the per-class <em>counter keys and scopes are not</em>, and are read
/// from the document rather than restated here. <c>CHEST_STANDARD</c> alone runs three counters
/// simultaneously, so a class does not address a counter on its own — the pairing of an authored
/// counter key with the guarantee it protects does, and <c>LuckTuning</c> owns forming it.
/// </para>
/// <para>
/// It lives in <c>Primitives/</c> rather than <c>Content/</c> because both the tuning reader and
/// the luck rules name it, and <c>Primitives</c> is the layer beneath both. A public enum under
/// <c>Core/Model/</c> would additionally fail the accessibility-boundary rule on its
/// compiler-generated <c>value__</c> field.
/// </para>
/// <para>
/// The numeric values are wire values: a stored pity counter, an event payload and an analytics row
/// all carry the class of the grant that moved them. Renumbering would re-label every counter and
/// every historical row in existence. Append, never renumber, never reuse.
/// </para>
/// <para>
/// There is deliberately no <c>0</c> member, so <c>default(SourceClass)</c> cannot read as
/// <see cref="CHEST_STANDARD"/> and quietly advance the standard-chest ladder from an
/// uninitialised column.
/// </para>
/// </remarks>
public enum SourceClass
{
    /// <summary>Standard chests. Three simultaneous ladders (A, S, SS).</summary>
    CHEST_STANDARD = 1,

    /// <summary>Premium chests. Two ladders, at a much shorter cadence.</summary>
    CHEST_PREMIUM = 2,

    /// <summary>Apex chests. One ladder, so dense it needs no soft pity.</summary>
    CHEST_APEX = 3,

    /// <summary>In-run drops. Its rule is a dry-streak breaker rather than a rarity ladder.</summary>
    DROP_RUN = 4,

    /// <summary>Pet Eggs.</summary>
    EGG_PET = 5,

    /// <summary>Mount Crates.</summary>
    CRATE_MOUNT = 6,

    /// <summary>Gear enhancement. Its counter is scoped to the gear instance, not to the player.</summary>
    ENHANCE = 7,

    /// <summary>The in-run perk draft. Its counter is scoped to the run.</summary>
    DRAFT = 8,

    /// <summary>The daily wheel.</summary>
    WHEEL = 9,

    /// <summary>The chest-pick minigame. The other minigames are skill-scaled and carry no counter.</summary>
    MINIGAME = 10,
}
