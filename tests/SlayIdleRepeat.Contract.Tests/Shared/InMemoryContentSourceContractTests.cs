using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The contract, run against the in-memory fake — <c>SlayIdleRepeat.Application.Tests</c> runs
/// entirely on this implementation, so it must be held to the same contract as the real adapter.
/// </summary>
[ContractFixtureFor(typeof(InMemoryContentSource))]
public sealed class InMemoryContentSourceContractTests : IContentSourcePortContractTests
{
    protected override IContentSourcePort Create(IReadOnlyDictionary<string, string> documents)
    {
        var source = new InMemoryContentSource();
        foreach (var (path, text) in documents)
        {
            source.Set(path, text);
        }

        return source;
    }

    protected override void Write(IContentSourcePort source, string documentPath, string utf8Text) =>
        ((InMemoryContentSource)source).Set(documentPath, utf8Text);
}
