using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The same contract, run against the file-backed adapter — the cache a device actually keeps.
/// Works inside a temporary directory of its own and deletes it afterwards.
/// </summary>
[ContractFixtureFor(typeof(LocalFileCache))]
public sealed class LocalFileCacheContractTests : ILocalCachePortContractTests
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "sir-cache-contract-" + Guid.NewGuid().ToString("N"));

    protected override ILocalCachePort Create() => new LocalFileCache(_root);

    /// <summary>A second port over the same directory — literally what the next launch opens.</summary>
    protected override ILocalCachePort Reopen(ILocalCachePort cache) =>
        new LocalFileCache(((LocalFileCache)cache).CacheDirectoryPath);

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        base.Dispose(disposing);
    }
}
