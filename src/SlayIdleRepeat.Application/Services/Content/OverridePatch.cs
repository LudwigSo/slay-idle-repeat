using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Layers a sparse override patch on top of canonical content, in memory, at load time.
/// </summary>
/// <remarks>
/// <para>
/// `21` §3.3 / `14` §6: <em>"Experiments and what-ifs run as sparse override patches layered on
/// top of the canonical files, never as edits to them. This keeps <c>git diff</c> on
/// <c>SlayIdleRepeat.Data</c> a record of decisions rather than a record of attempts."</em>
/// </para>
/// <para>
/// 🔒 Nothing here writes. The patch produces a new <see cref="ContentValue"/> tree; the canonical
/// bytes on disk are read once and never touched. The <c>IContentSourcePort</c> has no write
/// member at all, which is the structural version of the same guarantee.
/// </para>
/// <para>
/// Merge semantics, chosen so a typo cannot pass for an experiment:
/// <list type="bullet">
/// <item>Object against object → recursive, sparse: only the named members change.</item>
/// <item>Anything else → replacement.</item>
/// <item>Arrays → replaced wholesale. A sparse array patch has no unambiguous reading.</item>
/// <item>A member the canonical document does not have →
/// <see cref="ContentIssueCode.OverrideTargetMissing"/>. An override that invents a key is a
/// misspelling that would otherwise sweep a parameter nothing reads.</item>
/// </list>
/// The merged result is re-validated against the schema afterwards, so an override cannot smuggle
/// an out-of-range value past the build either.
/// </para>
/// </remarks>
public static class OverridePatch
{
    /// <summary>
    /// Splits an override document (`21` §3.3 shape: keyed by canonical file name) into per-file
    /// patches, resolving each file name to a snapshot-relative document path.
    /// </summary>
    public static IReadOnlyList<ContentIssue> Split(
        string overrideDocumentPath,
        ContentValue overrideRoot,
        IReadOnlyCollection<string> canonicalDocumentPaths,
        out IReadOnlyDictionary<string, ContentValue> patchesByDocumentPath) =>
        throw new NotImplementedException();

    /// <summary>Applies one patch to one canonical document, returning a new value tree.</summary>
    public static ContentValue Apply(
        ContentValue canonical,
        ContentValue patch,
        string location,
        ICollection<ContentIssue> issues) => throw new NotImplementedException();
}
