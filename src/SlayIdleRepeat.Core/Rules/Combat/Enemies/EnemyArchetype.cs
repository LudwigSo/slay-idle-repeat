namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// The eight base shapes every enemy in the game is derived from.
/// </summary>
/// <remarks>
/// <para>
/// Enemies are not hand-statted; they are derived from the chapter Power value and an archetype
/// coefficient set. The eight shapes are reskinned per biome — the coefficient row is what an enemy
/// is, and the art is what it looks like.
/// </para>
/// <para>
/// An enum, not a string id: every other table in <c>content/enemies/enemies.json</c> references
/// these names, and <c>EnemyCatalogue</c> parses them back to this type, so a typo in the data is a
/// load failure rather than a silently absent archetype.
/// </para>
/// <para>
/// The wire values are 1-based and append-only: the names travel in content and a renumbering would
/// be a content-format change, not a rename.
/// </para>
/// </remarks>
internal enum EnemyArchetype
{
    /// <summary>The baseline. Every coefficient 1.00.</summary>
    GRUNT = 1,

    /// <summary>Three weak bodies per draw; punishes single-target.</summary>
    SWARM = 2,

    /// <summary>Slow heavy hitter.</summary>
    BRUTE = 3,

    /// <summary>Fast, high dodge.</summary>
    SKIRMISHER = 4,

    /// <summary>Tanky; applies <c>SUNDER</c> on hit.</summary>
    WARDEN = 5,

    /// <summary>Applies its chapter's biome status on hit.</summary>
    CASTER = 6,

    /// <summary>Sustain drain; the only shape with authored lifesteal.</summary>
    LEECH = 7,

    /// <summary>Crit spiker. Deliberately absent from Chapter 1's pool.</summary>
    REAVER = 8,
}

/// <summary>
/// The closed vocabulary, read from an authored name — the parse, stated once.
/// </summary>
/// <remarks>
/// <para>
/// Two call sites read an archetype out of authored text — <c>EnemyCatalogue.ParseArchetype</c> and
/// <c>Bosses.BossSummonSource.ArchetypeOf</c> — and each used to write its own
/// <c>Enum.TryParse</c> + <c>Enum.IsDefined</c> pair, which had already drifted on case-sensitivity.
/// This consolidates the predicate; the callers keep their own failure types and messages.
/// </para>
/// <para>
/// A numeric name is refused: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> accepts
/// the underlying number as well as the name, so an authored <c>"7"</c> would otherwise load as
/// <c>LEECH</c> — a wire value leaking into a place the documents spell with a name.
/// </para>
/// </remarks>
internal static class EnemyArchetypes
{
    /// <summary>
    /// True when <paramref name="name"/> is exactly one of the eight names, case-sensitively.
    /// </summary>
    internal static bool TryParse(string name, out EnemyArchetype archetype)
    {
        archetype = default;

        return !string.IsNullOrEmpty(name) &&
            !char.IsAsciiDigit(name[0]) && name[0] != '-' && name[0] != '+' &&
            Enum.TryParse(name, ignoreCase: false, out archetype) &&
            Enum.IsDefined(archetype);
    }

    /// <summary>The eight names, for a failure message that shows the closed table.</summary>
    internal static string Names => string.Join(", ", Enum.GetNames<EnemyArchetype>());
}

/// <summary>
/// Which on-hit parameter set an archetype carries.
/// </summary>
/// <remarks>
/// <see cref="None"/> is an authored fact, not an unfilled hole: six of the eight shapes apply
/// nothing on hit, and representing that as <c>null</c> would put it in the same shape as a value
/// the documents have not authorised.
/// </remarks>
internal enum ArchetypeOnHit
{
    /// <summary>This shape applies no status on hit.</summary>
    None = 0,

    /// <summary>
    /// <c>WARDEN</c>'s one authored parameter set, which holds wherever a <c>WARDEN</c> appears, in
    /// any chapter, including a boss's <c>WARDEN</c> drones.
    /// </summary>
    Sunder = 1,

    /// <summary><c>CASTER</c> applies the status of the chapter it is fought in.</summary>
    BiomeStatus = 2,
}
