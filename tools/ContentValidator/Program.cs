using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Services.Content;

namespace SlayIdleRepeat.ContentValidator;

/// <summary>
/// The build-time content check `14` §6 requires: schema validation over every JSON file under
/// <c>game-data</c>.
/// </summary>
/// <remarks>
/// Exit code 0 means the content is loadable. Exit code 1 means it is not, and says exactly where. Nothing else is an exit code: an unhandled exception that
/// escaped to the runtime would print a stack trace naming no document and hand CI a code nobody
/// specified.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"  ERROR content validation could not complete: {exception.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var repositoryRoot = Argument(args, "--repository-root") ?? FindRepositoryRoot();
        var dataRoot = Argument(args, "--data-root") ?? Path.Combine(repositoryRoot, "game-data");

        Console.WriteLine("Content validation (14 §6)");
        Console.WriteLine($"  data root   : {dataRoot}");
        Console.WriteLine();

        var source = new LocalFileContentSource(dataRoot);
        var result = ContentLoader.Load(source);

        Console.WriteLine($"Documents   : {source.ListDocuments().Count}");

        foreach (var issue in result.Issues)
        {
            Console.Error.WriteLine($"  ERROR {issue}");
        }

        if (result.Succeeded)
        {
            Console.WriteLine($"Snapshot    : {result.Snapshot!.DocumentPaths.Count} document(s), " +
                              $"stamp {result.Snapshot.Version.Short}…");
        }

        var failures = result.Issues.Count;
        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "Content validation passed."
            : $"Content validation FAILED with {failures} issue(s).");

        return failures == 0 ? 0 : 1;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate SlayIdleRepeat.sln above the tool's output.");
    }
}
