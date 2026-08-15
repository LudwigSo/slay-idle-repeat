using System.Globalization;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// One derived enemy — every stat computable from <c>(power, archetype, chapter, tier)</c> with no
/// free variables, as a value.
/// </summary>
/// <remarks>
/// <para>
/// The four inputs are carried alongside the result, because the whole point is that the result is
/// a function of them: a definition that held only the stat block could not be checked against the
/// formula that produced it.
/// </para>
/// <para>
/// <see cref="TargetPriority"/> is declared here and read nowhere in this milestone: the hero
/// targets the highest priority, breaking ties on lowest current HP. The three authored values are
/// data on <see cref="EnemyCatalogue.DefaultTargetPriority"/>,
/// <see cref="EnemyCatalogue.DeprioritisedTargetPriority"/> and
/// <see cref="EnemyCatalogue.ForcedTargetPriority"/> rather than restated here, so there is one
/// source of truth. The selection algorithm belongs to the tick engine and is likewise not
/// implemented beside the data.
/// </para>
/// <para>
/// <see cref="Level"/> is not one of the fourteen stats — it is the separate term the mitigation
/// denominator reads, which is why a separate table exists for it.
/// </para>
/// </remarks>
/// <param name="Archetype">The shape this was derived from.</param>
/// <param name="Power">The enemy power the block was derived at.</param>
/// <param name="Chapter">The chapter, which fixes the level and the <c>CASTER</c> biome status.</param>
/// <param name="TierOrdinal">The tier's ordinal in <c>EnemyLevelTable.Ordinals</c>.</param>
/// <param name="Level">The enemy level for this chapter and tier.</param>
/// <param name="Stats">The complete 14-stat block.</param>
/// <param name="UnitsPerDraw">Bodies one draw of this shape spawns.</param>
/// <param name="OnHit">The status this enemy applies on a landed hit, or <c>null</c>.</param>
/// <param name="Modifier">
/// The drawn modifier when this is an Elite, or <c>null</c> when it is not — a fact, not an
/// unauthorised value.
/// </param>
/// <param name="TargetPriority">The field the selection algorithm reads.</param>
internal sealed record EnemyDefinition(
    EnemyArchetype Archetype,
    double Power,
    int Chapter,
    int TierOrdinal,
    int Level,
    ActorStats Stats,
    int UnitsPerDraw,
    OnHitStatus? OnHit,
    EliteModifier? Modifier,
    int TargetPriority)
{
    /// <summary>Whether the elite treatment was applied.</summary>
    internal bool IsElite => Modifier is not null;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Archetype}{(IsElite ? $" [{Modifier}]" : string.Empty)} " +
        $"ch{Chapter.ToString(CultureInfo.InvariantCulture)} " +
        $"t{TierOrdinal.ToString(CultureInfo.InvariantCulture)} " +
        $"L{Level.ToString(CultureInfo.InvariantCulture)} " +
        $"@{Power.ToString("R", CultureInfo.InvariantCulture)} power";
}
