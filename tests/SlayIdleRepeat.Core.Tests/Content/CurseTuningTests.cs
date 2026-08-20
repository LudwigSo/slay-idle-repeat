using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <see cref="CurseTuning"/>'s read of <c>content/curses/curses.json</c>, and
/// <see cref="CurseRewards"/>' narrow four-row payout table.
/// </summary>
public sealed class CurseTuningTests
{
    private static readonly CurseTuning Shipped = CurseTuning.Read(InRunIncomeDocuments.Shipped);

    /// <summary>The twelve rows, in the document's own order.</summary>
    [Fact]
    public void Every_curse_row_comes_from_the_curses_document()
    {
        Shipped.All.Count.ShouldBe(InRunIncomeDocuments.ShippedCurses.Length);

        for (var i = 0; i < Shipped.All.Count; i++)
        {
            var (id, reward, availableFrom) = InRunIncomeDocuments.ShippedCurses[i];

            Shipped.All[i].Id.ShouldBe(id);
            Shipped.All[i].Reward.ShouldBe(reward);
            Shipped.All[i].AvailableFromChapter.ShouldBe(availableFrom);
        }
    }

    /// <summary>
    /// The chapter gate: the column is the EARLIEST chapter a curse opens in, so the comparison is
    /// <c>availableFromChapter &lt;= chapterId</c> and a curse stays available after.
    /// </summary>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 9)]
    [InlineData(4, 9)]
    [InlineData(5, 10)]
    [InlineData(8, 10)]
    public void The_chapter_gate_opens_a_curse_and_keeps_it_open(int chapterId, int expected)
    {
        Shipped.AvailableFrom(chapterId).Count.ShouldBe(expected);
    }

    /// <summary>
    /// The gate genuinely EXCLUDES a later curse from an earlier chapter — the half of the filter
    /// that a missing or inverted comparison would break, and the one that matters: <c>CUR_HUNTED</c>
    /// pays a per-Elite gear drop no system can grant.
    /// </summary>
    [Fact]
    public void A_chapter_five_curse_is_excluded_from_a_chapter_one_draw()
    {
        Shipped.AvailableFrom(1).Select(c => c.Id).ShouldNotContain("CUR_HUNTED");
        Shipped.AvailableFrom(1).Select(c => c.Id).ShouldNotContain("CUR_UNTIMELY");
    }

    /// <summary>…and the negative control: it IS included from the chapter it opens in.</summary>
    [Fact]
    public void A_chapter_five_curse_is_included_from_chapter_five()
    {
        Shipped.AvailableFrom(5).Select(c => c.Id).ShouldContain("CUR_HUNTED");
    }

    /// <summary>A chapter below the game's floor has no eligible set to compute.</summary>
    [Fact]
    public void A_chapter_below_one_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Shipped.AvailableFrom(0));
    }

    /// <summary>A duplicate id would make one curse unreachable and the other twice as likely.</summary>
    [Fact]
    public void A_duplicate_curse_id_is_refused()
    {
        var row = InRunIncomeDocuments.Obj(
            ("id", ContentValue.Text("CUR_SLIPPERY")),
            ("displayName", ContentValue.Text("loc.curse.slippery.name")),
            ("effect", ContentValue.Text("-1 to all Pip rolls (minimum 1)")),
            ("reward", ContentValue.Text("+250 Gold")),
            ("availableFromChapter", ContentValue.Number(1m)));

        Should.Throw<InvalidTunableException>(() =>
                CurseTuning.Read(InRunIncomeDocuments.With(curses: ContentValue.Array([row, row]))))
            .Message.ShouldContain("CUR_SLIPPERY", Case.Sensitive);
    }

    /// <summary>An empty catalogue leaves a curse tile with nothing to draw.</summary>
    [Fact]
    public void An_empty_curse_catalogue_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            CurseTuning.Read(InRunIncomeDocuments.With(curses: ContentValue.EmptyArray)));
    }

    // ------------------------------------------------------------------ CurseRewards

    /// <summary>
    /// The three-row payout table: transcribed from the content's <c>reward</c> prose ("+250 Gold"),
    /// which nothing parses. ⚠️ Three, not four: <c>CUR_DIZZY</c> is gone with the reroll.
    /// </summary>
    [Theory]
    [InlineData("CUR_SLIPPERY", CurrencyId.GOLD, 250L)]
    [InlineData("CUR_MARKED", CurrencyId.ENHANCE_STONES, 2L)]
    [InlineData("CUR_FRACTURED", CurrencyId.GOLD, 500L)]
    public void Each_payable_curse_pays_its_authored_currency_and_amount(
        string curseId, CurrencyId currency, long amount)
    {
        CurseRewards.IsPayable(curseId).ShouldBeTrue();
        CurseRewards.For(curseId).ShouldBe((currency, amount));
    }

    /// <summary>A curse whose reward is a percentage or a gear drop is refused rather than paid a guessed amount.</summary>
    [Theory]
    [InlineData("CUR_HUNTED")]
    [InlineData("CUR_FAMISHED")]
    [InlineData("CUR_BLIND")]
    [InlineData("CUR_NOT_REAL")]
    public void An_unpayable_curse_is_refused(string curseId)
    {
        CurseRewards.IsPayable(curseId).ShouldBeFalse();

        Should.Throw<ArgumentException>(() => CurseRewards.For(curseId))
            .Message.ShouldContain(curseId, Case.Sensitive);
    }
}
