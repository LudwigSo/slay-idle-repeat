using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The rules no single schema can state, because they hold <em>between</em> files.
/// </summary>
/// <remarks>
/// `14` §6 names five failure classes; a JSON Schema can express two of them on its own
/// (unknown ids, out-of-range values) and gets duplicate ids only within one array. Orphaned
/// references, missing icons, duplicate ids across files, and every "these two files must agree"
/// rule the design docs state live here. M0-10 ran 29 of these as throwaways while authoring the
/// data; this class is where they were reconstructed so they survive the next refactor.
/// </remarks>
public static class ContentInvariants
{
    /// <summary>Every cross-file rule, over the merged, schema-valid document set.</summary>
    /// <param name="documents">Data documents by snapshot-relative path. Schemas excluded.</param>
    public static IReadOnlyList<ContentIssue> Check(IReadOnlyDictionary<string, ContentValue> documents) =>
        throw new NotImplementedException();
}
