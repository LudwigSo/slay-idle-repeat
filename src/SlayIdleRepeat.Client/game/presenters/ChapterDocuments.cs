using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>One authored chapter as the Home screen names it.</summary>
/// <param name="ChapterId">The chapter's id, which is also its place in the campaign.</param>
/// <param name="ChapterName">Its display name, already resolved through the catalogue.</param>
public sealed record ChapterDocument(int ChapterId, string ChapterName);

/// <summary>The chapters a content set authors, listed by id and named.</summary>
/// <remarks>
/// Read once per screen and handed to both the progress tile and the run panel, so the two name
/// chapters off one list and cannot disagree about what chapter 2 is called.
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
