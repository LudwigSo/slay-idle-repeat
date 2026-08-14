using System.Globalization;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 One derived enemy — `05` §6's <em>"every stat of every enemy is computable from
/// <c>(power, archetype, chapter, tier)</c> with no free variables"</em>, as a value.
/// </summary>
/// <remarks>
/// <para>
/// The four inputs are carried alongside the result, because the whole claim of `05` §6 is that the
/// result is a function of them: a definition that held only the stat block could not be checked
/// against the formula that produced it.
/// </para>
/// <para>
/// 🔒 <b><see cref="TargetPriority"/> is declared here and read nowhere in this milestone.</b>
/// `05` §3.2 and `17` §11 ask for the field; the M2 kickoff confirmed it as an int, with the hero
/// targeting the <b>highest</b> priority and breaking ties on the <b>lowest current HP</b>. The
/// three authored values — the default, the one a summon the player should ignore carries
/// (Sporequeen's sporelings), and the one that forces focus — are <b>data</b>, on
/// <see cref="EnemyCatalogue.DefaultTargetPriority"/>,
/// <see cref="EnemyCatalogue.DeprioritisedTargetPriority"/> and
/// <see cref="EnemyCatalogue.ForcedTargetPriority"/>. They are deliberately <em>not</em> restated as
/// constants here: a second copy is a second source of truth, and it is the copy the tick engine
/// would bind to. The <em>selection algorithm</em> belongs to the tick engine (M2-08) and is
/// likewise not implemented beside the data — two implementations of one targeting rule is one too
/// many.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Level"/> is not one of the fourteen stats.</b> `05` §1's actor block holds
/// <c>MAX_HP</c>..<c>THORNS</c>; Level is the separate term `05` §4's mitigation denominator reads,
/// which is exactly why `05` §6.0 had to add a table for it.
/// </para>
/// </remarks>
/// <param name="Archetype">The `05` §6.1 shape this was derived from.</param>
/// <param name="Power">`02` §4.3's <c>EnemyPower(i)</c> the block was derived at.</param>
/// <param name="Chapter">The chapter, which fixes the level and the <c>CASTER</c> biome status.</param>
/// <param name="TierOrdinal">The tier's ordinal in <c>EnemyLevelTable.Ordinals</c>.</param>
/// <param name="Level">`05` §6.0's <c>EnemyLevel(chapter, tier)</c>.</param>
/// <param name="Stats">`05` §6's complete 14-stat block.</param>
/// <param name="UnitsPerDraw">`05` §6.1 — bodies one draw of this shape spawns.</param>
/// <param name="OnHit">`05` §6.1a — the status this enemy applies on a landed hit, or <c>null</c>.</param>
/// <param name="Modifier">
/// `05` §6.2 — the drawn modifier when this is an Elite, or <c>null</c> when it is not. ⚠️ Here
/// <c>null</c> means "not an Elite", which is a fact and not an unauthorised value.
/// </param>
/// <param name="TargetPriority">`05` §3.2 — the field M2-08's selection algorithm reads.</param>
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
    /// <summary>Whether `05` §6.2's elite treatment was applied.</summary>
    internal bool IsElite => Modifier is not null;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Archetype}{(IsElite ? $" [{Modifier}]" : string.Empty)} " +
        $"ch{Chapter.ToString(CultureInfo.InvariantCulture)} " +
        $"t{TierOrdinal.ToString(CultureInfo.InvariantCulture)} " +
        $"L{Level.ToString(CultureInfo.InvariantCulture)} " +
        $"@{Power.ToString("R", CultureInfo.InvariantCulture)} power";
}
