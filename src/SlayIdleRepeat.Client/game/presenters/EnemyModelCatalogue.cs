namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The model an enemy is drawn with.</summary>
/// <param name="ScenePath">The <c>res://</c> path of the model.</param>
/// <param name="HeightUnits">How tall the model stands, so a plate can sit above it.</param>
public sealed record EnemyModel(string ScenePath, float HeightUnits);

/// <summary>
/// Which model draws which enemy: the six standard archetypes per biome, the elites and the boss
/// by their own ids. An identity with no model answers null — never a stand-in.
/// </summary>
/// <remarks>
/// The heights are measured off the shipped glb files — each one's highest vertex — so they are
/// facts about the art rather than numbers to tune. A second biome is another row of the table.
/// </remarks>
public static class EnemyModelCatalogue
{
    private const string ArtDirectory = "res://game/art/";
    private const string ElitePrefix = "EL_";
    private const string BossPrefix = "BOSS_";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, EnemyModel>> CastByBiome =
        new Dictionary<string, IReadOnlyDictionary<string, EnemyModel>>(StringComparer.Ordinal)
        {
            ["greenwood"] = Cast(
                "greenwood",
                archetypes:
                [
                    ("GRUNT", 2.216f),
                    ("SWARM", 0.854f),
                    ("BRUTE", 2.642f),
                    ("SKIRMISHER", 1.868f),
                    ("WARDEN", 1.939f),
                    ("CASTER", 2.406f),
                ],
                elites:
                [
                    ("EL_THORN_SENTINEL", 3.406f),
                    ("EL_MOSSBACK_ALPHA", 2.487f),
                ],
                boss: ("BOSS_THORNMAW", 4.912f)),
        };

    /// <summary>The model for an identity in a biome, or null when there is none — a null biome or identity among them.</summary>
    /// <param name="biome">The chapter's art set with its prefix stripped, such as <c>greenwood</c>; null when the chapter authors none.</param>
    /// <param name="identity">The archetype, elite id or boss id the roster names; null when it names none.</param>
    public static EnemyModel? For(string? biome, string? identity)
    {
        if (biome is null || identity is null || !CastByBiome.TryGetValue(biome, out var cast))
        {
            return null;
        }

        return cast.TryGetValue(identity, out var model) ? model : null;
    }

    private static IReadOnlyDictionary<string, EnemyModel> Cast(
        string biome,
        IReadOnlyList<(string Id, float Height)> archetypes,
        IReadOnlyList<(string Id, float Height)> elites,
        (string Id, float Height) boss)
    {
        var cast = new Dictionary<string, EnemyModel>(StringComparer.Ordinal);

        foreach (var (id, height) in archetypes)
        {
            cast[id] = Model($"chr_enemy_{biome}_{Lower(id)}", height);
        }

        foreach (var (id, height) in elites)
        {
            cast[id] = Model($"chr_elite_{Lower(id[ElitePrefix.Length..])}", height);
        }

        cast[boss.Id] = Model($"chr_boss_{Lower(boss.Id[BossPrefix.Length..])}", boss.Height);

        return cast;
    }

    private static EnemyModel Model(string fileStem, float height) =>
        new($"{ArtDirectory}{fileStem}.glb", height);

    private static string Lower(string id) => id.ToLowerInvariant();
}
