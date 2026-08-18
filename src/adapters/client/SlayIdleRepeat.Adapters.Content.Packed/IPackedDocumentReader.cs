namespace SlayIdleRepeat.Adapters.Content.Packed;

/// <summary>
/// The two things <see cref="PackedContentSource"/> needs from whatever can actually reach inside
/// a packed artefact — and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Not a port, deliberately.</b> It lives beside its one consumer rather than in
/// <c>Application/Ports/</c>, because a port would drag two obligations that both point the wrong
/// way: X-06 would demand a contract fixture for the engine-backed implementation, and M7-01b
/// measured that a GodotSharp call from the unit tier is an uncatchable
/// <c>AccessViolationException</c> that kills the test host rather than throwing something a fixture
/// could assert on. <c>23</c> §7.2a settles it from the other side too — a class able to reach the
/// engine API implements no port. This is the seam that lets the port implementation stay plain C#.
/// </para>
/// <para>
/// 🔒 <b>It knows nothing about where the content root is.</b> Paths in and out are relative to the
/// content root, forward-slashed, exactly as <c>IContentSourcePort</c> states them — so
/// <c>res://</c>, <c>user://</c> and every other engine-specific prefix stays on the implementing
/// side, and this assembly names none of them. That is what keeps
/// <see cref="PackedContentSource"/> testable against a dictionary.
/// </para>
/// <para>
/// ⚠️ <b>Both members answer about the artefact as it is NOW, and neither may cache.</b> The
/// content source re-asks on every call because its <c>Revision</c> is required to move when a
/// document does. A packed artefact cannot change while it is mounted, so for the real
/// implementation "now" is a constant and the re-asking costs an enumeration of a few dozen
/// entries; for a fake it is what makes the contract's revision cases meaningful at all.
/// </para>
/// </remarks>
public interface IPackedDocumentReader
{
    /// <summary>
    /// Every document inside the packed content root, as content-root-relative forward-slashed
    /// paths, in whatever order the artefact yields them.
    /// </summary>
    /// <remarks>
    /// 🔒 Order is explicitly NOT this interface's promise — <see cref="PackedContentSource"/> sorts.
    /// The loader's determinism is stated over the port's ordering, and a directory walk's order is
    /// the engine's business: guaranteeing it here would be a promise the implementation cannot keep
    /// and the consumer would then stop enforcing.
    /// </remarks>
    IReadOnlyList<string> EnumerateDocuments();

    /// <summary>Reads one document, answering whether the artefact holds it at all.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>False rather than an exception, because "absent" is an ordinary answer here.</b> What
    /// absence MEANS belongs to the port: <c>IContentSourcePort</c> declares
    /// <c>MissingContentException</c> for every way a path can fail, and an implementation that threw
    /// its own exception first would make that declaration a lie for one caller.
    /// </para>
    /// <para>
    /// 🔴 <b>A <c>bool</c> and an <c>out</c> rather than a <c>ReadOnlyMemory&lt;byte&gt;?</c>, and the
    /// reason is measured rather than stylistic.</b> With a nullable return, the obvious implementation
    /// — <c>found ? bytes : null</c> — compiles and is WRONG: <c>ReadOnlyMemory&lt;byte&gt;</c> has an
    /// implicit conversion from <c>byte[]</c>, and <c>null</c> converts to <c>byte[]</c>, so the
    /// conditional finds a natural type of <c>ReadOnlyMemory&lt;byte&gt;</c> and the miss becomes an
    /// EMPTY buffer wrapped in a non-null nullable. Measured here: the first fixture written against
    /// that signature reported a present document of length zero for a path the artefact did not hold,
    /// and the contract case about an unlisted path failed with the adapter entirely innocent. A
    /// signature whose correct use is that easy to miss is the wrong signature — an <c>out</c> cannot
    /// be got wrong the same way.
    /// </para>
    /// </remarks>
    /// <param name="documentPath">A content-root-relative, forward-slashed path.</param>
    /// <param name="bytes">The document's bytes, or empty when this returns false.</param>
    bool TryRead(string documentPath, out ReadOnlyMemory<byte> bytes);
}
