using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SlayIdleRepeat.BalanceHarness.Experiments;

/// <summary>In-memory edits to <c>content/bosses/bosses.json</c> for the two balance experiments.</summary>
/// <remarks>
/// Nothing here ever writes to <c>game-data/</c>: every method takes the shipped JSON text and returns
/// a new string, which <c>GameDataLoader.LoadWith</c> substitutes into a snapshot in memory. An edit
/// that matched nothing throws, rather than silently producing a "variant" run byte-identical to the
/// baseline (which would report a difference of exactly zero and read like a finding).
/// </remarks>
public static class BossDocumentOverrides
{
    /// <summary>The document path <c>GameDataLoader.LoadWith</c> keys on.</summary>
    public const string DocumentPath = "content/bosses/bosses.json";

    /// <summary>The shipped text of the boss document, off the given data root.</summary>
    public static string ReadShipped(string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);

        return File.ReadAllText(Path.Combine(dataRoot, DocumentPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Removes one effect from one script — both its declaration in <c>effects</c> and every
    /// <c>mechanics</c> entry naming it, since leaving either half orphaned makes
    /// <c>BossEncounterBuilder</c> refuse the script outright.
    /// </summary>
    /// <param name="json">The shipped document text.</param>
    /// <param name="scriptId">The script to edit, e.g. <c>BOSS_THORNMAW</c>.</param>
    /// <param name="effectId">The effect to remove, e.g. <c>BOSS_THORNMAW_P3_RAGE</c>.</param>
    /// <exception cref="InvalidOperationException">The script or the effect was not found.</exception>
    public static string WithoutEffect(string json, string scriptId, string effectId)
    {
        ArgumentNullException.ThrowIfNull(json);

        var root = Parse(json);
        var script = Script(root, scriptId);

        var effects = script["effects"]!.AsArray();
        var removedDeclarations = RemoveWhere(
            effects, node => Text(node?["id"]) == effectId);

        var removedMechanics = 0;
        foreach (var phase in script["phases"]!.AsArray())
        {
            if (phase?["mechanics"] is JsonArray mechanics)
            {
                removedMechanics += RemoveWhere(mechanics, node => Text(node?["effectId"]) == effectId);
            }
        }

        if (removedDeclarations == 0 || removedMechanics == 0)
        {
            throw new InvalidOperationException(
                $"Removing '{effectId}' from '{scriptId}' matched " +
                $"{Int(removedDeclarations)} effect declaration(s) and {Int(removedMechanics)} phase " +
                "mechanic(s); both must be non-zero. An override that matched nothing produces a " +
                "'variant' run identical to the baseline, and an A/B whose two arms are the same run " +
                "reports a difference of exactly zero — which reads like a finding.");
        }

        return root.ToJsonString();
    }

    /// <summary>
    /// Sets <c>addsPowerFraction</c> on every script that already carries one — never a script that
    /// summons nothing, which would author a value design never stated.
    /// </summary>
    /// <exception cref="InvalidOperationException">No script carried the key.</exception>
    public static string WithAddsPowerFraction(string json, double fraction)
    {
        ArgumentNullException.ThrowIfNull(json);

        var root = Parse(json);
        var changed = 0;

        foreach (var script in root["scripts"]!.AsArray())
        {
            if (script?["addsPowerFraction"] is not null)
            {
                script["addsPowerFraction"] = JsonValue.Create(fraction);
                changed++;
            }
        }

        if (changed == 0)
        {
            throw new InvalidOperationException(
                "No script in the boss document carries addsPowerFraction, so this override changed " +
                "nothing and the run would silently be the unmodified one.");
        }

        return root.ToJsonString();
    }

    /// <summary>How many scripts carry <c>addsPowerFraction</c>, for the subject-set floor.</summary>
    public static int CountSummoners(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return Parse(json)["scripts"]!.AsArray().Count(s => s?["addsPowerFraction"] is not null);
    }

    private static JsonObject Parse(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
            ?.AsObject()
        ?? throw new InvalidOperationException(
            $"{DocumentPath} did not parse as a JSON object.");

    private static JsonObject Script(JsonObject root, string scriptId) =>
        root["scripts"]!.AsArray()
            .FirstOrDefault(s => Text(s?["id"]) == scriptId)
            ?.AsObject()
        ?? throw new InvalidOperationException(
            $"'{scriptId}' is not a script in {DocumentPath}.");

    private static string? Text(JsonNode? node) => node?.GetValue<string>();

    private static int RemoveWhere(JsonArray array, Func<JsonNode?, bool> predicate)
    {
        var removed = 0;
        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (predicate(array[i]))
            {
                array.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
