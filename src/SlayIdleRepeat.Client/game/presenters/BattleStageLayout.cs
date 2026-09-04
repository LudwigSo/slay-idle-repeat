namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>A point on the battle stage's floor, in engine units.</summary>
/// <param name="X">Across the stage: the hero's side is negative, the enemies' positive.</param>
/// <param name="Z">Along the stage, either side of the centre line the hero and the first enemy stand on.</param>
public readonly record struct StagePoint(float X, float Z);

/// <summary>Which way along X an actor faces.</summary>
public enum StageFacing
{
    /// <summary>Toward positive X — the hero's side looking at the enemies.</summary>
    PositiveX = 0,

    /// <summary>Toward negative X — the enemies looking at the hero.</summary>
    NegativeX = 1,
}

/// <summary>The distances a stage is laid out with. Handed in by the scene; nothing here defaults them.</summary>
/// <param name="HalfGap">How far the hero and the first enemy each stand from the stage's centre.</param>
/// <param name="EnemySpacing">The step along Z between one enemy slot and the next.</param>
/// <param name="ArcDepth">How far back along X each step away from the centre slot pushes an enemy.</param>
public sealed record BattleStageMetrics(float HalfGap, float EnemySpacing, float ArcDepth);

/// <summary>Where one actor stands and which way it faces.</summary>
/// <param name="ActorId">The slot the log identifies the actor by.</param>
/// <param name="Position">Where it stands.</param>
/// <param name="Facing">Which way it faces.</param>
public sealed record StagePlacement(byte ActorId, StagePoint Position, StageFacing Facing);

/// <summary>
/// Where every actor of a fight stands: the hero at −HalfGap facing +X, the enemies at +HalfGap
/// facing −X in a fixed slot order (centre, −spacing, +spacing, −2·spacing, +2·spacing, each pushed
/// back by ArcDepth per step from the centre), and pets behind the hero.
/// </summary>
/// <remarks>
/// The slot order is fixed so an actor that arrives mid-fight takes the next slot and moves nobody.
/// The hero's side is the enemies' arc mirrored in X: the hero holds its centre slot and each pet
/// takes the next one, so two pets stand in two places without a second set of distances.
/// </remarks>
public static class BattleStageLayout
{
    private const int HeroArcSlot = 0;
    private const int FirstPetArcSlot = 1;

    /// <summary>Places every actor, one placement each, in the order the actors are given.</summary>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static IReadOnlyList<StagePlacement> Place(
        IReadOnlyList<ReplayActor> actors, BattleStageMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(metrics);

        var placements = new StagePlacement[actors.Count];
        var nextEnemyArcSlot = 0;
        var nextPetArcSlot = FirstPetArcSlot;

        for (var index = 0; index < actors.Count; index++)
        {
            var actor = actors[index];

            placements[index] = actor.Side switch
            {
                ReplaySide.Hero => OnHeroSide(actor.ActorId, HeroArcSlot, metrics),
                ReplaySide.Enemy => OnEnemySide(actor.ActorId, nextEnemyArcSlot++, metrics),
                _ => OnHeroSide(actor.ActorId, nextPetArcSlot++, metrics),
            };
        }

        return placements;
    }

    private static StagePlacement OnEnemySide(byte actorId, int arcSlot, BattleStageMetrics metrics)
    {
        var (back, z) = ArcOffset(arcSlot, metrics);

        return new StagePlacement(actorId, new StagePoint(metrics.HalfGap + back, z), StageFacing.NegativeX);
    }

    private static StagePlacement OnHeroSide(byte actorId, int arcSlot, BattleStageMetrics metrics)
    {
        var (back, z) = ArcOffset(arcSlot, metrics);

        return new StagePlacement(actorId, new StagePoint(-metrics.HalfGap - back, z), StageFacing.PositiveX);
    }

    /// <summary>
    /// Where an arc slot sits relative to the arc's front centre: slot 0 is the centre, then the slots
    /// alternate −Z, +Z at one step, −Z, +Z at two steps, and so on, each step further back.
    /// </summary>
    private static (float Back, float Z) ArcOffset(int arcSlot, BattleStageMetrics metrics)
    {
        var steps = (arcSlot + 1) / 2;
        var sign = arcSlot % 2 == 1 ? -1 : 1;

        return (steps * metrics.ArcDepth, sign * steps * metrics.EnemySpacing);
    }
}
