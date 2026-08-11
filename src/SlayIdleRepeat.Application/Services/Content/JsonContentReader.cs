using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Strict UTF-8 JSON → <see cref="ContentValue"/>. The only place in the codebase that knows JSON
/// exists on the read path.
/// </summary>
/// <remarks>
/// Strict means strict: RFC 8259 only — no comments, no trailing commas — so CI can never be more
/// forgiving than the runtime loader. It also reports <see cref="ContentIssueCode.DuplicateKey"/>,
/// which a plain deserialise cannot: the object model keeps the last duplicate and the earlier
/// value simply disappears.
/// <para>
/// 🔒 JSON <c>null</c> becomes <see cref="ContentValue.Unauthorised"/>, never a default.
/// </para>
/// </remarks>
public static class JsonContentReader
{
    /// <summary>Parses one document.</summary>
    /// <param name="documentPath">Used only to locate findings.</param>
    /// <param name="utf8">The raw bytes.</param>
    /// <param name="root">The parsed root, or null when <paramref name="issues"/> is non-empty.</param>
    /// <param name="issues">Everything wrong with the bytes.</param>
    public static bool TryRead(
        string documentPath,
        ReadOnlySpan<byte> utf8,
        out ContentValue? root,
        out IReadOnlyList<ContentIssue> issues) => throw new NotImplementedException();
}
