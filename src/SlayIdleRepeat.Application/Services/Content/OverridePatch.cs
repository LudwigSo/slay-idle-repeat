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
        out IReadOnlyDictionary<string, ContentValue> patchesByDocumentPath)
    {
        var issues = new List<ContentIssue>();
        var patches = new Dictionary<string, ContentValue>(StringComparer.Ordinal);

        if (overrideRoot.Kind != ContentValueKind.Object)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.OverrideTargetMissing, overrideDocumentPath,
                "an override file is an object keyed by canonical file name (21 §3.3), " +
                $"and this one is {overrideRoot.Kind}."));
            patchesByDocumentPath = patches;
            return issues;
        }

        foreach (var fileName in overrideRoot.MemberNames)
        {
            var matches = canonicalDocumentPaths
                .Where(p => p.EndsWith("/" + fileName, StringComparison.Ordinal) ||
                            string.Equals(p, fileName, StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            if (matches.Length != 1)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OverrideTargetMissing, $"{overrideDocumentPath}#/{fileName}",
                    matches.Length == 0
                        ? $"names '{fileName}', which is not a canonical content document. An " +
                          "override that names nothing sweeps nothing, silently."
                        : $"names '{fileName}', which is ambiguous across {matches.Length} documents."));
                continue;
            }

            overrideRoot.TryGetMember(fileName, out var patch);
            patches[matches[0]] = patch!;
        }

        patchesByDocumentPath = patches;
        return issues;
    }

    /// <summary>Applies one patch to one canonical document, returning a new value tree.</summary>
    public static ContentValue Apply(
        ContentValue canonical,
        ContentValue patch,
        string location,
        ICollection<ContentIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (canonical.Kind != ContentValueKind.Object || patch.Kind != ContentValueKind.Object)
        {
            return patch;
        }

        var merged = new List<KeyValuePair<string, ContentValue>>();

        foreach (var name in canonical.MemberNames)
        {
            canonical.TryGetMember(name, out var canonicalMember);

            merged.Add(patch.TryGetMember(name, out var patchMember)
                ? new KeyValuePair<string, ContentValue>(
                    name, Apply(canonicalMember!, patchMember!, $"{location}/{name}", issues))
                : new KeyValuePair<string, ContentValue>(name, canonicalMember!));
        }

        foreach (var name in patch.MemberNames)
        {
            if (canonical.TryGetMember(name, out _))
            {
                continue;
            }

            issues.Add(new ContentIssue(
                ContentIssueCode.OverrideTargetMissing, $"{location}/{name}",
                $"the override sets '{name}', which the canonical document does not have. An " +
                "override may only change what exists — inventing a key is how a misspelt sweep " +
                "runs for a week against a parameter nothing reads."));
        }

        return ContentValue.Object(merged);
    }
}
