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
/// 🔒 `05` §6.1's closed vocabulary, read from an authored name — <b>the parse, stated once</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this exists.</b> Two call sites read an archetype out of authored text —
/// <c>EnemyCatalogue.ParseArchetype</c> (from <c>content/enemies/enemies.json</c>) and
/// <c>Bosses.BossSummonSource.ArchetypeOf</c> (from `18` §2.4's <c>archetype</c> key on a
/// <c>SUMMON</c>) — and each wrote its own <c>Enum.TryParse</c> + <c>Enum.IsDefined</c> pair. They
/// had already drifted: one spelled <c>ignoreCase: false</c> and the other relied on the overload's
/// default for it. Two statements of one closed table is the shape this milestone keeps finding, and
/// a ninth shape would have had to be right in both places with nothing tying them together.
/// </para>
/// <para>
/// ⚠️ <b>The callers keep their own failure types, and that is not a second statement of the
/// rule.</b> <c>EnemyCatalogue</c> throws a <c>ContentTypeMismatchException</c> naming the JSON
/// pointer; <c>BossSummonSource</c> throws an <c>EffectContextException</c> naming the summoning
/// effect. Both messages are load-bearing — steering S2 asks which rule fired — and neither is the
/// parse. What is consolidated here is the <b>predicate</b>.
/// </para>
/// <para>
/// 🔒 <b>A numeric name is refused.</b> <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
/// accepts the underlying number as well as the name, so an authored <c>"7"</c> would have loaded as
/// <c>LEECH</c> through both former copies — a wire value leaking into a place the documents spell
/// with a name, and one that would silently follow a renumbering this enum's own remarks forbid.
/// </para>
/// </remarks>
internal static class EnemyArchetypes
{
    /// <summary>
    /// True when <paramref name="name"/> is exactly one of `05` §6.1's eight names, case-sensitively.
    /// </summary>
    internal static bool TryParse(string name, out EnemyArchetype archetype)
    {
        archetype = default;

        return !string.IsNullOrEmpty(name) &&
            !char.IsAsciiDigit(name[0]) && name[0] != '-' && name[0] != '+' &&
            Enum.TryParse(name, ignoreCase: false, out archetype) &&
            Enum.IsDefined(archetype);
    }

    /// <summary>`05` §6.1's eight names, for a failure message that shows the closed table.</summary>
    internal static string Names => string.Join(", ", Enum.GetNames<EnemyArchetype>());
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
