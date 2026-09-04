using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>The chapters a content set authors, as the Home screen lists them: by id, and named.</summary>
public sealed class ChapterDocumentsTests
{
    private static readonly int[] TwoChapters = [1, 2];

    /// <summary>
    /// 🔒 The fixture files chapter 2 BEFORE chapter 1 in path order, so a reader following the
    /// content set's own order lists them backwards.
    /// </summary>
    [Fact]
    public void Read_lists_the_authored_chapters_in_id_order_whatever_order_the_content_set_holds_them()
    {
        var content = ScreenContent.Authoring(TwoChapters);

        ChapterDocuments.Read(content, ScreenContent.Catalogue(content)).Select(chapter => chapter.ChapterId).ShouldBe(
            new[] { 1, 2 },
            "the progress tile and the run panel both name chapters off this list, and a list in " +
            "file order agrees with id order on the shipped files only by an accident of naming.");
    }

    [Fact]
    public void Read_resolves_each_chapters_display_name_through_the_catalogue()
    {
        var content = ScreenContent.Authoring(TwoChapters);

        ChapterDocuments.Read(content, ScreenContent.Catalogue(content)).ShouldContain(
            new ChapterDocument(2, ScreenContent.EnglishValueOf(ScreenContent.ChapterNameKey(2))),
            "the document names a loc key and the screen shows words — a list carrying the key puts " +
            "'loc.chapter.2.name' on the progress tile.");
    }

    [Fact]
    public void Read_lists_nothing_for_a_content_set_authoring_no_chapter()
    {
        var content = ScreenContent.Strings();

        ChapterDocuments.Read(content, ScreenContent.Catalogue(content)).ShouldBeEmpty(
            "no chapter documents is no chapters, not a thrown lookup on a screen that can say " +
            "'nothing cleared' perfectly well without one.");
    }
}
