namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6.1 — the eight base shapes every enemy in the game is derived from.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6: <em>"Enemies are not hand-statted. They are derived from the chapter <c>Power</c> value
/// and an archetype coefficient set. This keeps 8 chapters × 3 tiers × 64 enemy variants
/// authorable."</em> The eight shapes are reskinned per biome — the coefficient row is what an
/// enemy <em>is</em>, and the art is what it looks like.
/// </para>
/// <para>
/// 🔒 <b>An enum, not a string id.</b> `05` §6.1's table is closed: a ninth shape is a design
/// decision that has to change the document, the schema's <c>archetypeId</c> vocabulary and every
/// chapter's weight row together. Every other table in <c>content/enemies/enemies.json</c>
/// references these names, and <c>EnemyCatalogue</c> parses them back to this type, so a typo in
/// the data is a load failure rather than a silently absent archetype.
/// </para>
/// <para>
/// The wire values are 1-based and append-only, for the reason
/// <see cref="Content.Effects.StatId"/> records: the names travel in content and a renumbering
/// would be a content-format change, not a rename.
/// </para>
/// </remarks>
internal enum EnemyArchetype
{
    /// <summary>`05` §6.1 — the baseline. Every coefficient 1.00.</summary>
    GRUNT = 1,

    /// <summary>`05` §6.1 — three weak bodies per draw; punishes single-target.</summary>
    SWARM = 2,

    /// <summary>`05` §6.1 — slow heavy hitter.</summary>
    BRUTE = 3,

    /// <summary>`05` §6.1 — fast, high dodge.</summary>
    SKIRMISHER = 4,

    /// <summary>`05` §6.1 — tanky; applies <c>SUNDER</c> on hit (§6.1a).</summary>
    WARDEN = 5,

    /// <summary>`05` §6.1 — applies its chapter's biome status on hit (§6.1a).</summary>
    CASTER = 6,

    /// <summary>`05` §6.1 — sustain drain; the only shape with authored lifesteal.</summary>
    LEECH = 7,

    /// <summary>`05` §6.1 — crit spiker. Deliberately absent from Chapter 1's pool.</summary>
    REAVER = 8,
}

/// <summary>
/// 🔒 `05` §6.1a — which on-hit parameter set an archetype carries.
/// </summary>
/// <remarks>
/// ⚠️ <see cref="None"/> is an <b>authored fact</b>, not an unfilled hole. Six of the eight shapes
/// apply nothing on hit and `05` §6.1a says so by listing only two; representing that as a
/// <c>null</c> would put it in the same shape as a value the documents have not authorised, which
/// is the distinction <c>game-data/README.md</c>'s null convention exists to keep.
/// </remarks>
internal enum ArchetypeOnHit
{
    /// <summary>`05` §6.1a — this shape applies no status on hit.</summary>
    None = 0,

    /// <summary>
    /// `05` §6.1a — <c>WARDEN</c>'s one authored parameter set, which holds wherever a
    /// <c>WARDEN</c> appears, in any chapter, including a boss's <c>WARDEN</c> drones (`17` §7).
    /// </summary>
    Sunder = 1,

    /// <summary>`05` §6.1a — <c>CASTER</c> applies the status of the chapter it is fought in.</summary>
    BiomeStatus = 2,
}
