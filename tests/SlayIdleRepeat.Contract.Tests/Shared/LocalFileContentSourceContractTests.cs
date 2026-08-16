using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The same contract, run against the real filesystem adapter — the implementation both hosts
/// actually boot on. Writes into a temporary directory of its own and deletes it afterwards.
/// </summary>
[ContractFixtureFor(typeof(LocalFileContentSource))]
public sealed class LocalFileContentSourceContractTests : IContentSourcePortContractTests
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "sir-contract-" + Guid.NewGuid().ToString("N"));

    protected override IContentSourcePort Create(IReadOnlyDictionary<string, string> documents)
    {
        Directory.CreateDirectory(_root);
        foreach (var (path, text) in documents)
        {
            WriteFile(path, text);
        }

        return new LocalFileContentSource(_root);
    }

    protected override void Write(IContentSourcePort source, string documentPath, string utf8Text)
    {
        WriteFile(documentPath, utf8Text);

        // Revision is derived from last-write ticks; push the timestamp forward so a write inside
        // the filesystem's timestamp granularity doesn't produce the same token as before.
        var stamp = DateTime.UtcNow.AddSeconds(2);
        File.SetLastWriteTimeUtc(Path.Combine(_root, documentPath.Replace('/', Path.DirectorySeparatorChar)), stamp);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        base.Dispose(disposing);
    }

    private void WriteFile(string documentPath, string utf8Text)
    {
        var absolute = Path.Combine(_root, documentPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, utf8Text);
    }
}
