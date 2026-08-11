using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Turns the bytes behind an <see cref="IContentSourcePort"/> into an immutable, version-stamped
/// <c>ContentSnapshot</c>: parse → layer overrides → validate against
/// <c>SlayIdleRepeat.Data/schema/</c> → check cross-file invariants → stamp.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Deterministic by construction. Documents are processed in the source's ordinal path order,
/// object members are held ordinal-sorted, and the stamp is computed over a canonical
/// serialisation — so the same bytes always produce the same snapshot and the same stamp, on any
/// machine. `14` §16.6's hashing depends on that and would fail silently without it.
/// </para>
/// <para>
/// The directory convention it reads, from `SlayIdleRepeat.Data/README.md`:
/// <list type="bullet">
/// <item><c>schema/*.json</c> — JSON Schema, never content.</item>
/// <item><c>loc/*.json</c> — governed by <c>schema/loc.schema.json</c>.</item>
/// <item><c>tuning/experiments/*.json</c> — override patches. 🔒 Never part of a snapshot; the
/// experiments README is explicit that they are "never shipped, never served from the content
/// endpoint, and never part of a <c>ContentSnapshot</c>". They load only when named in
/// <see cref="ContentLoadOptions.OverrideDocuments"/>.</item>
/// <item>everything else — governed by <c>schema/&lt;stem&gt;.schema.json</c>.</item>
/// </list>
/// </para>
/// </remarks>
public static class ContentLoader
{
    /// <summary>Loads the canonical content with no overrides.</summary>
    public static ContentLoadResult Load(IContentSourcePort source) => Load(source, ContentLoadOptions.Canonical);

    /// <summary>Loads content, layering the named sparse override patches (`21` §3.3).</summary>
    public static ContentLoadResult Load(IContentSourcePort source, ContentLoadOptions options) =>
        throw new NotImplementedException();
}
