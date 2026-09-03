namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The model an enemy is drawn with.</summary>
/// <param name="ScenePath">The <c>res://</c> path of the model.</param>
/// <param name="HeightUnits">How tall the model stands, so a plate can sit above it.</param>
public sealed record EnemyModel(string ScenePath, float HeightUnits);

/// <summary>
/// Which model draws which enemy: the six standard archetypes per biome, the elites and the boss
/// by their own ids. An identity with no model answers null — never a stand-in.
/// </summary>
public static class EnemyModelCatalogue
{
    /// <summary>The model for an identity in a biome, or null when there is none.</summary>
    /// <param name="biome">The chapter's art set with its prefix stripped, such as <c>greenwood</c>.</param>
    /// <param name="identity">The archetype, elite id or boss id the roster names.</param>
    public static EnemyModel? For(string biome, string identity)
    {
        _ = biome;
        _ = identity;

        return null;
    }
}
