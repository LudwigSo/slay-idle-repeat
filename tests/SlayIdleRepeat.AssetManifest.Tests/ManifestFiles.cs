namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// The real committed manifest, loaded once, plus the raw JSON the mutation cases edit.
/// </summary>
internal static class ManifestFiles
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);
    private static readonly Lazy<AssetManifestSet> LazySet =
        new(() => AssetManifestReader.Load(DataRoot));

    /// <summary>The repository root.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>The <c>game-data</c> root.</summary>
    internal static string DataRoot => Path.Combine(RepositoryRoot, "game-data");

    /// <summary>The manifest directory.</summary>
    internal static string AssetsDirectory =>
        Path.Combine(DataRoot, AssetManifestReader.AssetsDirectory);

    /// <summary>The schema directory.</summary>
    internal static string SchemaDirectory => Path.Combine(DataRoot, "schema");

    /// <summary>The shipped register.</summary>
    internal static AssetManifestSet Shipped => LazySet.Value;

    /// <summary>The art manifest's raw JSON.</summary>
    internal static string ArtJson() =>
        File.ReadAllText(Path.Combine(AssetsDirectory, AssetManifestReader.ArtFileName));

    /// <summary>The audio manifest's raw JSON.</summary>
    internal static string AudioJson() =>
        File.ReadAllText(Path.Combine(AssetsDirectory, AssetManifestReader.AudioFileName));

    /// <summary>
    /// The shipped register with one textual substitution applied — how every mutation case below
    /// proves its rule can actually fail.
    /// </summary>
    internal static AssetManifestSet WithArtEdit(string find, string replace)
    {
        var json = ArtJson();
        if (!json.Contains(find, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The mutation '{find}' matches nothing in the shipped art manifest, so the case " +
                "below would prove nothing. Fix the mutation, do not delete the test.");
        }

        return AssetManifestReader.LoadFrom(Replace(json, find, replace), AudioJson());
    }

    /// <summary>The shipped register with one textual substitution applied to the audio manifest.</summary>
    internal static AssetManifestSet WithAudioEdit(string find, string replace)
    {
        var json = AudioJson();
        if (!json.Contains(find, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The mutation '{find}' matches nothing in the shipped audio manifest, so the case " +
                "below would prove nothing. Fix the mutation, do not delete the test.");
        }

        return AssetManifestReader.LoadFrom(ArtJson(), Replace(json, find, replace));
    }

    /// <summary>Replaces the FIRST occurrence only, so a mutation stays surgical.</summary>
    private static string Replace(string source, string find, string replace)
    {
        var index = source.IndexOf(find, StringComparison.Ordinal);
        return string.Concat(source.AsSpan(0, index), replace, source.AsSpan(index + find.Length));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} in any ancestor of {AppContext.BaseDirectory}. " +
                "These cases read the real game-data/assets.");
    }
}
