using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Ports.Shared;

/// <summary>
/// The I/O boundary of the content pipeline: hands out the raw bytes of the data files and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// `14` §6: <em>"Loading JSON is I/O and belongs in an adapter; reading content is a rule."</em>
/// This port is that sentence's seam. Everything downstream of it — parsing, schema validation,
/// override merging, hashing — is deterministic computation over bytes and lives in
/// <c>Services/Content/</c>, where it can be exercised against the in-memory fake (`23` §3).
/// </para>
/// <para>
/// Deliberately synchronous. Content is read once at boot, before the loop exists, and again on
/// an explicit dev hot-reload; an <c>async</c> signature here would buy nothing and would push
/// the loader — whose determinism is load-bearing — into a shape where ordering is a scheduling
/// detail. Fetching a newer content package over the network is `IGameApiPort`'s job (`14` §6's
/// "a mismatch triggers a content download"), not this port's.
/// </para>
/// <para>
/// `Shared/` rather than `Client/` or `Server/`: `14` §6 makes the server the source of truth and
/// the client a copy of the same bytes, so both hosts read content through the same seam.
/// </para>
/// </remarks>
public interface IContentSourcePort
{
    /// <summary>
    /// An opaque token that changes whenever any document behind this source changes. Lets a dev
    /// hot-reload decide whether a rebuild is even necessary without re-hashing everything.
    /// </summary>
    /// <remarks>
    /// 🔒 It moves <em>if and only if</em> the content moves. Both halves are the contract: a token
    /// that does not move on a change means a dev never sees their edit, and one that moves without
    /// a change means a rebuild on every poll.
    /// </remarks>
    string Revision { get; }

    /// <summary>
    /// Every available document path, snapshot-relative, forward-slashed, ordinal-sorted.
    /// 🔒 The ordering is part of the contract: the loader's determinism depends on it.
    /// </summary>
    IReadOnlyList<string> ListDocuments();

    /// <summary>The raw UTF-8 bytes of one document.</summary>
    /// <exception cref="MissingContentException">
    /// 🔒 The declared failure for a path this source does not list — <b>whatever the reason</b>:
    /// absent, outside the content root, or a directory that does not exist. One declared type, so
    /// a caller written against the fake behaves the same against the real adapter. The two
    /// implementations disagreed here before <c>Contract.Tests</c> existed to notice.
    /// </exception>
    ReadOnlyMemory<byte> ReadDocument(string documentPath);
}
