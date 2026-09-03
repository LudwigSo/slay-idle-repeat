namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// Reads a scene or a theme as the text file it is — no engine, no Node, no scene harness.
/// </summary>
internal static class SceneText
{
    /// <summary>
    /// The one node of a scene with this name, or <c>null</c> when the scene holds none or several.
    /// </summary>
    /// <remarks>
    /// Null for "several" as well as for "none" on purpose: every rule asking for a node asks about
    /// one that must be unique, and two nodes sharing a name is the state in which a scene-unique
    /// lookup resolves to whichever the engine reached first.
    /// </remarks>
    internal static SceneNode? Node(string relativePath, string name)
    {
        var nodes = new List<SceneNode>();
        SceneNode? current = null;

        foreach (var line in Read(relativePath).Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith('['))
            {
                current = line.StartsWith("[node ", StringComparison.Ordinal) &&
                          line.Contains($"name=\"{name}\"", StringComparison.Ordinal)
                    ? new SceneNode(line)
                    : null;

                if (current is not null)
                {
                    nodes.Add(current);
                }

                continue;
            }

            current?.Body.Add(line);
        }

        return nodes.Count == 1 ? nodes[0] : null;
    }

    /// <summary>The file's text. A file the checkout does not hold throws — it is not a passing case.</summary>
    internal static string Read(string relativePath)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, relativePath);

        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException(
                $"No '{relativePath}' under '{RepoPaths.RepositoryRoot}'. These cases read the " +
                "checkout's own scene and theme files, so a missing one is not a passing case.",
                path);
    }
}

/// <summary>One <c>[node ...]</c> header and the trimmed property lines under it.</summary>
internal sealed record SceneNode(string Header)
{
    internal List<string> Body { get; } = [];
}
