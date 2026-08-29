using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>The shipped content set, loaded once for the client-side suites that need one.</summary>
/// <remarks>
/// A host mints a real starting player, and a real starting player needs the authored Legend Level
/// range — so these suites cannot run against a stand-in content set without asserting against a
/// world the game does not ship.
/// </remarks>
internal static class ClientWorlds
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private static readonly Lazy<ContentSnapshot> LazyContent = new(() =>
        ContentLoader.Load(new LocalFileContentSource(DataRoot())).Require());

    /// <summary>The shipped content set.</summary>
    internal static ContentSnapshot Content => LazyContent.Value;

    private static string DataRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent!;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"no {SolutionFileName} above {AppContext.BaseDirectory}; these suites read the "
                + "shipped game-data out of the checkout they are running from.")
            : Path.Combine(directory.FullName, "game-data");
    }
}
