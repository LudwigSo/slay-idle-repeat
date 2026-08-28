using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// The cleared-chapter-tier key format, and the tripwire that keeps it agreeing with the aggregate
/// that writes it.
/// </summary>
/// <remarks>
/// 🔴 <b>There are two implementations of this format and that is the whole point of this file.</b>
/// <c>Player</c> spells the key itself, and <see cref="ChapterClearance"/> spells it for readers
/// outside the aggregate — the inbox's segment predicate is the first. The aggregate was
/// deliberately left untouched (its stored blob carries line endings no other file in the repository
/// has, so any edit to it rewrites the whole file and conflicts with every concurrent branch), so
/// what holds the two together is this pairing rather than a shared call. If it goes red, the
/// formats have parted company and a segment predicate is deciding who a compensation grant reaches
/// on keys the aggregate never writes.
/// </remarks>
public sealed class ChapterClearanceTests
{
    [Theory]
    [InlineData(1, DifficultyTier.NORMAL)]
    [InlineData(7, DifficultyTier.HEROIC)]
    public void The_key_this_primitive_spells_is_the_key_the_aggregate_reads(
        int chapterId, DifficultyTier tier)
    {
        var snapshot = PlayerSnapshots.With(
            clearedChapterTiers: new Dictionary<string, long>(StringComparer.Ordinal)
            {
                [ChapterClearance.Key(chapterId, tier)] = 1,
            });

        var hero = Worlds.Rehydrated(snapshot);

        hero.HasClearedChapterTier(chapterId, tier).ShouldBeTrue(
            "the aggregate must recognise a key this primitive wrote. Two spellings of one format " +
            "is a predicate that quietly selects nobody.");
    }

    [Fact]
    public void A_key_the_aggregate_did_not_write_is_not_recognised()
    {
        var snapshot = PlayerSnapshots.With(
            clearedChapterTiers: new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["3-NORMAL"] = 1,
            });

        var hero = Worlds.Rehydrated(snapshot);

        hero.HasClearedChapterTier(3, DifficultyTier.NORMAL).ShouldBeFalse(
            "the negative control: an aggregate that answered true for any key at all would satisfy " +
            "the pairing above without the two formats agreeing on anything.");
    }

    [Fact]
    public void An_empty_record_reads_as_the_first_chapter()
    {
        ChapterClearance.HighestClearedIn(Array.Empty<string>()).ShouldBe(
            ChapterClearance.FirstChapter,
            "a player who has cleared nothing is still playing chapter one, so a predicate scaled " +
            "against how far they have got has a chapter to compare.");
    }

    [Fact]
    public void No_record_at_all_reads_as_the_first_chapter()
    {
        ChapterClearance.HighestClearedIn(null).ShouldBe(ChapterClearance.FirstChapter);
    }

    [Fact]
    public void The_highest_chapter_counts_any_tier()
    {
        var keys = new[]
        {
            ChapterClearance.Key(2, DifficultyTier.MYTHIC),
            ChapterClearance.Key(5, DifficultyTier.NORMAL),
        };

        ChapterClearance.HighestClearedIn(keys).ShouldBe(
            5,
            "clearing a chapter on Normal is as much a statement about how far the player has come " +
            "as clearing it on Mythic.");
    }

    [Fact]
    public void A_key_that_does_not_parse_is_skipped_rather_than_thrown_on()
    {
        var keys = new[] { "not-a-key", ChapterClearance.Key(4, DifficultyTier.NORMAL) };

        ChapterClearance.HighestClearedIn(keys).ShouldBe(
            4,
            "this reads stored rows. One unreadable key must not make 'how far has this player got' " +
            "unanswerable for the whole account.");
    }
}
