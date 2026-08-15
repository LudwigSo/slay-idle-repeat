using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Ports.Shared;

/// <summary>
/// The I/O boundary of the content pipeline: hands out the raw bytes of the data files and
/// nothing else. Parsing, schema validation, override merging and hashing are deterministic
/// computation over those bytes and live in <c>Services/Content/</c>.
/// </summary>
/// <remarks>
/// Deliberately synchronous — content is read once at boot and again on an explicit dev
/// hot-reload, never mid-loop, so an <c>async</c> signature would buy nothing. Lives under
/// <c>Shared/</c> rather than <c>Client/</c> or <c>Server/</c> because both hosts read the same
/// content bytes through this one seam.
/// </remarks>
public interface IContentSourcePort
{
    /// <summary>
    /// An opaque token that changes if and only if the content changes. Lets a dev hot-reload
    /// decide whether a rebuild is necessary without re-hashing everything.
    /// </summary>
    string Revision { get; }

    /// <summary>
    /// Every available document path, snapshot-relative, forward-slashed, ordinal-sorted. The
    /// loader's determinism depends on this ordering.
    /// </summary>
    IReadOnlyList<string> ListDocuments();

    /// <summary>The raw UTF-8 bytes of one document.</summary>
    /// <exception cref="MissingContentException">
    /// The declared failure for any path this source does not list, whatever the reason — absent,
    /// outside the content root, or a non-existent directory — so a caller behaves the same
    /// against the fake and the real adapter.
    /// </exception>
    ReadOnlyMemory<byte> ReadDocument(string documentPath);
}
