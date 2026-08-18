using System.Text;
using SlayIdleRepeat.Adapters.Content.Packed;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The same contract, run against the adapter an EXPORTED build boots on — the one whose absence
/// meant a packed build had no content at all (M7-10y).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This fixture is possible only because the engine half was kept out of the adapter.</b>
/// X-06 demands a contract fixture per concrete port implementation, and a
/// <c>GodotContentSource : IContentSourcePort</c> could not have one: M7-01b measured that a
/// GodotSharp call from this tier is an uncatchable <c>AccessViolationException</c> that kills the
/// test host, taking every other case with it. <c>23</c> §7.2a states the same conclusion as a rule.
/// So everything engine-specific sits behind <see cref="IPackedDocumentReader"/>, and what is left is
/// plain C# that a dictionary can drive — which is the whole reason the seam exists.
/// </para>
/// <para>
/// ⚠️ <b>What this therefore does NOT prove: that a real <c>.pck</c> can be enumerated at all.</b>
/// That is an engine fact, and it was settled separately by probing the client's own exported
/// artefact — a walk of all 88 documents and one read back byte for byte — before
/// <c>GodotPackedDocuments</c> was written. This suite proves the port's meaning; the probe and the
/// export run prove the engine's behaviour. Neither substitutes for the other, and reading this file
/// as coverage of the packed path would be the more comfortable mistake.
/// </para>
/// </remarks>
[ContractFixtureFor(typeof(PackedContentSource))]
public sealed class PackedContentSourceContractTests : IContentSourcePortContractTests
{
    /// <summary>
    /// The artefact this fixture stands in for, keyed the way a packed archive keys its own index.
    /// </summary>
    /// <remarks>
    /// Insertion-ordered rather than sorted, deliberately: the port promises ordinal order and the
    /// adapter is the thing that has to produce it, so a fixture that handed over a sorted list would
    /// make <c>ListDocuments_is_ordinal_sorted</c> pass without the adapter sorting anything.
    /// </remarks>
    private readonly Dictionary<string, byte[]> _artefact = new(StringComparer.Ordinal);

    protected override IContentSourcePort Create(IReadOnlyDictionary<string, string> documents)
    {
        _artefact.Clear();

        foreach (var (path, text) in documents)
        {
            _artefact[path] = Encoding.UTF8.GetBytes(text);
        }

        return new PackedContentSource(new DictionaryPackedDocuments(_artefact));
    }

    protected override void Write(IContentSourcePort source, string documentPath, string utf8Text)
    {
        // 🔒 No timestamp nudge, unlike the filesystem fixture's. The packed source hashes the bytes
        // rather than reading an mtime it does not have, so a changed document moves the revision on
        // its content alone — which is the stronger property and is why it is worth the full read.
        _artefact[documentPath] = Encoding.UTF8.GetBytes(utf8Text);
    }

    /// <summary>
    /// A reader over a dictionary, answering about it as it stands right now.
    /// </summary>
    /// <remarks>
    /// It holds the dictionary rather than a copy, because the contract's revision cases mutate the
    /// artefact behind a source that has already been constructed — a snapshot taken at construction
    /// would make <c>Revision_moves_when_a_document_is_added</c> unfailable.
    /// </remarks>
    private sealed class DictionaryPackedDocuments : IPackedDocumentReader
    {
        private readonly Dictionary<string, byte[]> _documents;

        internal DictionaryPackedDocuments(Dictionary<string, byte[]> documents) =>
            _documents = documents;

        public IReadOnlyList<string> EnumerateDocuments() => _documents.Keys.ToArray();

        /// <remarks>
        /// 🔴 <b>This fixture is why <see cref="IPackedDocumentReader.TryRead"/> is a Try pattern.</b> Its
        /// first version returned <c>ReadOnlyMemory&lt;byte&gt;?</c> and answered
        /// <c>found ? bytes : null</c> — which compiles, and reports a MISSING document as a PRESENT one
        /// of length zero, because the conditional's natural type resolves to
        /// <c>ReadOnlyMemory&lt;byte&gt;</c> through the implicit conversion from <c>byte[]</c>. The
        /// contract case about an unlisted path was the thing that caught it, with the adapter innocent.
        /// The interface changed rather than the fixture, because the next implementer would have written
        /// the same line.
        /// </remarks>
        public bool TryRead(string documentPath, out ReadOnlyMemory<byte> bytes)
        {
            var found = _documents.TryGetValue(documentPath, out var document);

            bytes = found ? document : ReadOnlyMemory<byte>.Empty;

            return found;
        }
    }
}
