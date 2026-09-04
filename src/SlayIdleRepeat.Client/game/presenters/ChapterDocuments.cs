using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>One authored chapter, as the screens that list or name chapters read it.</summary>
/// <param name="ChapterId">The chapter's id, which is also its place in the campaign.</param>
/// <param name="ChapterName">Its display name, already resolved through the catalogue.</param>
public sealed record ChapterDocument(int ChapterId, string ChapterName);

/// <summary>The chapters a content set authors, listed by id and named.</summary>
/// <remarks>
/// The one reading of the chapter documents: Home's progress tile and run panel and Chapter Select's
/// list all name chapters off it, so no two screens can disagree about what chapter 2 is called.
/// </remarks>
public static class ChapterDocuments
{
    private const string ChaptersDirectoryPrefix = "content/chapters/";

    private const string ChapterIdMember = "id";

    private const string ChapterDisplayNameMember = "displayName";

    /// <summary>Every chapter <paramref name="content"/> authors, in id order.</summary>
    /// <param name="content">The loaded content set.</param>
    /// <param name="strings">Key to display string, over the same content set.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static IReadOnlyList<ChapterDocument> Read(ContentSnapshot content, LocaleStringCatalogue strings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);

        var chapters = new List<ChapterDocument>();

        foreach (var path in content.DocumentPaths)
        {
            if (!path.StartsWith(ChaptersDirectoryPrefix, StringComparison.Ordinal) ||
                !content.TryGetDocument(path, out var document))
            {
                continue;
            }

            var root = document!.Root;

            if (root.TryGetMember(ChapterIdMember, out var id) &&
                id!.Kind == ContentValueKind.Number &&
                root.TryGetMember(ChapterDisplayNameMember, out var displayName) &&
                displayName!.Kind == ContentValueKind.Text)
            {
                chapters.Add(new ChapterDocument(id.AsInt32(), strings.Resolve(displayName.AsText())));
            }
        }

        // The campaign's own order, not the content set's: document paths sort by file name, and a
        // chapter renamed on disk would otherwise move on the tile.
        chapters.Sort((left, right) => left.ChapterId.CompareTo(right.ChapterId));

        return chapters;
    }
}
