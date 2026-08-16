using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The content par curve read out of <c>tuning/par_power.json</c> — the number every rolled item's
/// stats are a fraction of, one authored cell per chapter.
/// </summary>
/// <remarks>
/// The table is authored per cell and the default fill is a documented convenience rather than a
/// constraint, so a chapter with no row is refused by name rather than extrapolated. Extrapolating
/// would silently invent the balance point of a chapter nobody has tuned, and every item dropped in it
/// would be scaled against that invention.
/// </remarks>
public sealed class ParPowerTuningTests
{
    /// <summary>Each chapter's Normal-tier par, as authored.</summary>
    [Theory]
    [InlineData(1, 1000.0)]
    [InlineData(2, 2000.0)]
    [InlineData(3, 4000.0)]
    [InlineData(4, 8000.0)]
    [InlineData(5, 16000.0)]
    [InlineData(6, 32000.0)]
    [InlineData(7, 64000.0)]
    [InlineData(8, 128000.0)]
    public void The_reader_answers_the_chapter_par_the_document_authors(int chapter, double target)
    {
        ParPowerTuning.Read(GearDocuments.Shipped).ChapterPowerTarget(chapter).ShouldBe(target);
    }

    /// <summary>The table covers exactly the chapters the document authors, ascending.</summary>
    [Fact]
    public void The_table_covers_exactly_the_chapters_the_document_authors()
    {
        ParPowerTuning.Read(GearDocuments.Shipped).Chapters.ShouldBe(
            GearDocuments.ShippedParPower.Select(row => row.Chapter),
            "the chapters are answered in ascending order, which is what lets a caller ask 'how far " +
            "is the curve tuned' without sorting the answer itself.");
    }

    /// <summary>A par is read from the document rather than derived from the doubling curve.</summary>
    /// <remarks>
    /// The negative control on the transcription above: every shipped cell is exactly twice its
    /// predecessor, so a reader that applied the default fill formula would agree with all eight rows
    /// and would silently ignore a hand-tuned chapter.
    /// </remarks>
    [Fact]
    public void The_reader_answers_a_retuned_cell_rather_than_the_default_fill()
    {
        var table = ContentValue.Array(
        [
            GearDocuments.ParRow(ContentValue.Number(1), ContentValue.Number(1000)),
            GearDocuments.ParRow(ContentValue.Number(2), ContentValue.Number(2500)),
        ]);

        var tuning = ParPowerTuning.Read(GearDocuments.With(parPower: table));

        tuning.ChapterPowerTarget(2).ShouldBe(2500.0);
        tuning.ChapterPowerTarget(1).ShouldBe(
            1000.0, "re-tuning one cell must leave its neighbours where the document put them");
    }

    /// <summary>A chapter the table has no row for is refused by number.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public void ChapterPowerTarget_refuses_a_chapter_the_table_has_no_row_for(int chapter)
    {
        var thrown = Should.Throw<InvalidTunableException>(
            () => ParPowerTuning.Read(GearDocuments.Shipped).ChapterPowerTarget(chapter));

        thrown.Reference.ShouldBe(ParPowerTuning.ParPowerReference);
        thrown.Message.ShouldContain(
            chapter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive,
            "item power is a fraction of a chapter's par, so a chapter with no par has no item power " +
            "either — the refusal has to name the chapter rather than merely the table.");
    }

    /// <summary>A chapter authored twice is refused: which of the two pars wins would be an accident.</summary>
    [Fact]
    public void A_chapter_authored_twice_is_refused()
    {
        var table = ContentValue.Array(
        [
            GearDocuments.ParRow(ContentValue.Number(1), ContentValue.Number(1000)),
            GearDocuments.ParRow(ContentValue.Number(1), ContentValue.Number(2000)),
        ]);

        var thrown = Should.Throw<InvalidTunableException>(
            () => ParPowerTuning.Read(GearDocuments.With(parPower: table)));

        thrown.Reference.ShouldBe(ParPowerTuning.ParPowerReference + "/1/chapter");
        thrown.Message.ShouldContain("twice", Case.Sensitive);
    }

    /// <summary>A par that is not positive is refused: every item in the chapter is a fraction of it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void A_par_that_is_not_positive_is_refused(int par)
    {
        var table = ContentValue.Array(
        [
            GearDocuments.ParRow(ContentValue.Number(1), ContentValue.Number(par)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => ParPowerTuning.Read(GearDocuments.With(parPower: table)))
            .Reference.ShouldBe(
                ParPowerTuning.ParPowerReference + "/0/" + ParPowerTuning.NormalColumn,
                "which cell, not merely that some value was refused — this reader raises the same " +
                "type for a chapter below one and for a chapter authored twice.");
    }

    /// <summary>A chapter numbered below one is refused.</summary>
    [Fact]
    public void A_chapter_numbered_below_one_is_refused()
    {
        var table = ContentValue.Array(
        [
            GearDocuments.ParRow(ContentValue.Number(0), ContentValue.Number(1000)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => ParPowerTuning.Read(GearDocuments.With(parPower: table)))
            .Reference.ShouldBe(ParPowerTuning.ParPowerReference + "/0/chapter");
    }

    /// <summary>An empty table is refused rather than read as a curve with no cells.</summary>
    [Fact]
    public void An_empty_table_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => ParPowerTuning.Read(GearDocuments.With(parPower: ContentValue.EmptyArray)))
            .Reference.ShouldBe(ParPowerTuning.ParPowerReference);
    }

    /// <summary>A missing document is a <c>MissingContentException</c>, not an empty curve.</summary>
    [Fact]
    public void A_missing_document_throws_rather_than_defaulting()
    {
        Should.Throw<MissingContentException>(
                () => ParPowerTuning.Read(GearDocuments.Without(GearDocuments.ParPowerDocumentPath)))
            .Reference.ShouldBe(ParPowerTuning.DocumentPath);
    }

    /// <summary>The reader refuses a null content set rather than dereferencing it.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => ParPowerTuning.Read(null!))
            .ParamName.ShouldBe("content");
    }
}
