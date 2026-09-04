using Shouldly;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>Reads a <c>.tscn</c> as the text file it is: no engine, no Node, no scene harness.</summary>
internal static class SceneText
{
    /// <summary>The whole scene file. A missing file is a failing case, never a passing one.</summary>
    internal static string Read(string relativePath)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, relativePath);

        File.Exists(path).ShouldBeTrue(
            $"No '{relativePath}' under '{RepoPaths.RepositoryRoot}'. These cases read the " +
            "checkout's own scene files, so a missing one is not a passing case.");

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The one node of a scene with this name, or <c>null</c> when the scene holds none or several —
    /// two nodes sharing a name is the state in which a scene-unique lookup resolves to either.
    /// </summary>
    internal static SceneNode? Node(string relativePath, string name)
    {
        var nodes = Nodes(relativePath)
            .Where(node => node.Header.Contains($"name=\"{name}\"", StringComparison.Ordinal))
            .ToList();

        return nodes.Count == 1 ? nodes[0] : null;
    }

    /// <summary>Every node of a scene, in file order, each with the property lines under its header.</summary>
    internal static IReadOnlyList<SceneNode> Nodes(string relativePath)
    {
        var nodes = new List<SceneNode>();
        SceneNode? current = null;

        foreach (var line in Read(relativePath).Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith('['))
            {
                current = line.StartsWith("[node ", StringComparison.Ordinal) ? new SceneNode(line) : null;

                if (current is not null)
                {
                    nodes.Add(current);
                }

                continue;
            }

            current?.Body.Add(line);
        }

        return nodes;
    }
}

/// <summary>One <c>[node …]</c> block of a scene: its header line and the property lines under it.</summary>
internal sealed record SceneNode(string Header)
{
    internal List<string> Body { get; } = [];
}
