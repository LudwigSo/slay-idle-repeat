namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// Locates the checkout the test run is executing from.
/// </summary>
/// <remarks>
/// The engine half of the composition root resolves these two directories through Godot; the
/// pure half only ever sees two absolute paths, so a checkout-relative path is an honest stand-in
/// and needs no engine. Same mechanism the application suite already uses to read the real
/// content: <c>System.IO</c> over the working tree, no port, no adapter, nothing started.
/// </remarks>
internal static class RepoPaths
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private const string ContentDirectoryName = "game-data";

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);

    /// <summary>The directory holding <c>SlayIdleRepeat.sln</c>.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>The shipped content tree the exported build mirrors into the project.</summary>
    /// <remarks>
    /// 🔒 Checked rather than merely composed. Every case that names this path composes a real
    /// graph over it, so a missing tree has to say "there is no game-data here" — not surface
    /// three directories down as whatever the content source makes of a path that is not one.
    /// </remarks>
    internal static string ContentDataRoot
    {
        get
        {
            var path = Path.Combine(RepositoryRoot, ContentDirectoryName);

            return Directory.Exists(path)
                ? path
                : throw new DirectoryNotFoundException(
                    $"No '{ContentDirectoryName}' directory at '{path}'. These cases compose the client " +
                    "over the checkout's own content tree, so a run without one is testing a graph the " +
                    "game will never be built from.");
        }
    }

    /// <summary>A fresh, unused directory path for a cache root.</summary>
    internal static string ScratchCacheRoot() =>
        Path.Combine(Path.GetTempPath(), "SlayIdleRepeat.Client.Tests", Guid.NewGuid().ToString("N"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"No '{SolutionFileName}' above {AppContext.BaseDirectory}. These cases compose over the " +
                "checkout's own content tree, so they need one to be there.");
    }
}
