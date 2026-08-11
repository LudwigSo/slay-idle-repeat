using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The same contract, run against the real filesystem adapter — the implementation both hosts
/// actually boot on.
/// </summary>
/// <remarks>
/// It writes into a temporary directory of its own and deletes it afterwards. `23` §3 keeps the
/// filesystem out of <c>Application</c>'s tests; this project is where an adapter is allowed to
/// touch the thing it adapts, and where the two implementations are proven to agree.
/// </remarks>
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

        // The revision is derived from paths, sizes and last-write ticks. A write inside the
        // filesystem's timestamp granularity would otherwise produce the same token for different
        // content — the adapter's own doc flags that, and this keeps the contract case honest
        // rather than papering over it.
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
