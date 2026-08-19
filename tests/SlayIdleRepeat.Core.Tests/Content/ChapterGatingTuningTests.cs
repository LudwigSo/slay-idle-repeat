using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <see cref="ChapterGatingTuning"/> — `10` §7's chapter/tier ladder, read out of
/// <c>tuning/progression.json#/chapterGating</c>, and every way the read refuses rather than defaults.
/// </summary>
public sealed class ChapterGatingTuningTests
{
    /// <summary>The shipped ladder, read once — the subject of every case in the first section.</summary>
    private static ChapterGatingTuning Shipped() => ChapterGatingTuning.Read(TuningDocuments.Shipped);

    /// <summary>The pointer a rung's clear token is authored at.</summary>
    private static string ClearPointer(DifficultyTier tier) =>
        $"{ChapterGatingTuning.GatingReference}/{tier}/requiresClear";

    /// <summary>The pointer a rung's Legend Level is authored at.</summary>
    private static string LevelPointer(DifficultyTier tier) =>
        $"{ChapterGatingTuning.GatingReference}/{tier}/requiresLegendLevel";

    /// <summary>
    /// The shipped rungs, listed rather than reused from <c>ProgressionDocuments.ShippedChapterGating</c>:
    /// the cases below take one rung out of the ladder or swap one token, and a finished block cannot
    /// be taken apart again.
    /// </summary>
    private static readonly (DifficultyTier Tier, string Clear, int? Level)[] ShippedRungs =
    {
        (DifficultyTier.NORMAL, ProgressionDocuments.ShippedNormalRequiresClear, null),
        (DifficultyTier.HEROIC, ProgressionDocuments.ShippedHeroicRequiresClear, null),
        (DifficultyTier.MYTHIC,
            ProgressionDocuments.ShippedMythicRequiresClear,
            ProgressionDocuments.ShippedMythicRequiresLegendLevel),
    };

    /// <summary>
    /// A snapshot whose ladder is <see cref="ShippedRungs"/> with one tier's rung replaced, or —
    /// when <paramref name="rung"/> is <c>null</c> — left out of the block entirely.
    /// </summary>
    /// <param name="tier">The tier whose rung is being edited.</param>
    /// <param name="rung">The rung to author instead, or <c>null</c> to omit the tier.</param>
    private static ContentSnapshot LadderWith(DifficultyTier tier, ContentValue? rung) =>
        ProgressionDocuments.With(chapterGating: ProgressionDocuments.ChapterGating(
            ShippedRungs
                .Select(row => (
                    Tier: row.Tier,
                    Rung: row.Tier == tier
                        ? rung
                        : ProgressionDocuments.Rung(
                            ContentValue.Text(row.Clear),
                            row.Level is null
                                ? ContentValue.Unauthorised
                                : ContentValue.Number(row.Level.Value))))
                .Where(row => row.Rung is not null)
                .Select(row => (row.Tier.ToString(), row.Rung!))
                .ToArray()));

    // ------------------------------------------------------- the shipped ladder, read off the document

    /// <summary>
    /// Every declared tier's rung reads back exactly the clear token and the Legend Level the
    /// document authors for it. The expectation is read out of the snapshot rather than transcribed;
    /// what the numbers ought to be is checked against the shipped file in <c>Application.Tests</c>.
    /// </summary>
    [Theory]
    [InlineData(DifficultyTier.NORMAL)]
    [InlineData(DifficultyTier.HEROIC)]
    [InlineData(DifficultyTier.MYTHIC)]
    public void Each_tiers_rung_reads_back_the_clear_token_and_level_the_document_authors(DifficultyTier tier)
    {
        var content = TuningDocuments.Shipped;
        var rung = Shipped().Rung(tier);

        var authoredClear = content.Read(ClearPointer(tier));
        var authoredLevel = content.Read(LevelPointer(tier));

        rung.RequiresClear.ShouldBe(
            authoredClear.IsUnauthorised ? null : content.ReadText(ClearPointer(tier)),
            $"the {tier} rung's clear token is read from {ClearPointer(tier)} and from nowhere else. " +
            "A reader answering from a C# table would keep gating on the old ladder after the " +
            "document moved.");

        rung.RequiresLegendLevel.ShouldBe(
            authoredLevel.IsUnauthorised ? null : content.ReadInt32(LevelPointer(tier)),
            $"the {tier} rung's Legend Level is read from {LevelPointer(tier)}. An authored null " +
            "means the rung demands no level and must read back as null — read as 0 it would be a " +
            "level gate nobody can fail, and read as 1 it would be one nobody can fail either but " +
            "which the screen would still draw.");
    }

    /// <summary>
    /// A fixture authoring a Legend Level the shipped file does not carry reads back that level —
    /// a reader hard-coding the shipped number passes every other case and fails this one.
    /// </summary>
    [Fact]
    public void A_ladder_authoring_a_different_Legend_Level_reads_back_that_level()
    {
        const int Unshipped = 7;

        var ladder = ChapterGatingTuning.Read(ProgressionDocuments.With(
            chapterGating: ProgressionDocuments.ChapterGating(
                ("NORMAL", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.PreviousChapterNormal), ContentValue.Unauthorised)),
                ("HEROIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterNormal), ContentValue.Unauthorised)),
                ("MYTHIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterHeroic),
                    ContentValue.Number(Unshipped))))));

        ladder.Rung(DifficultyTier.MYTHIC).RequiresLegendLevel.ShouldBe(
            Unshipped,
            "the Mythic rung's Legend Level is plumbing, not a constant. A reader that answered the " +
            "shipped number regardless of the document would be indistinguishable from a correct one " +
            "on every other case in this file.");
    }

    /// <summary>
    /// A rung authoring a token the shipped ladder puts on a <em>different</em> rung reads back —
    /// and resolves to — the token it was given. Each row moves a token onto a rung the shipped file
    /// does not put it on, which is where a hard-coded tier→token table and the document disagree.
    /// </summary>
    [Theory]
    [InlineData(
        DifficultyTier.NORMAL, ProgressionDocuments.ShippedMythicRequiresClear, 4, 4, DifficultyTier.HEROIC)]
    [InlineData(
        DifficultyTier.HEROIC, ProgressionDocuments.ShippedNormalRequiresClear, 3, 2, DifficultyTier.NORMAL)]
    [InlineData(
        DifficultyTier.MYTHIC, ProgressionDocuments.ShippedHeroicRequiresClear, 4, 4, DifficultyTier.NORMAL)]
    public void A_rung_authoring_a_token_the_shipped_ladder_puts_elsewhere_reads_back_that_token(
        DifficultyTier tier, string token, int chapterId, int expectedChapter, DifficultyTier expectedTier)
    {
        var moved = ChapterGatingTuning.Read(
            LadderWith(tier, ProgressionDocuments.Rung(ContentValue.Text(token), ContentValue.Unauthorised)))
            .Rung(tier);

        moved.RequiresClear.ShouldBe(
            token,
            $"the {tier} rung authors {token} in this document, and the reader answers from the " +
            "document. A tier→token table baked into C# agrees with every other case in this file " +
            "and disagrees here.");

        moved.RequiredClear(chapterId).ShouldBe(
            new ChapterClear(expectedChapter, expectedTier),
            $"{token} on chapter {chapterId} names chapter {expectedChapter} on {expectedTier}, " +
            "whichever rung it happens to be authored on. Resolving from the tier rather than from " +
            "the token would give the shipped answer for this rung and be wrong for this ladder.");
    }

    // ------------------------------------------------------------------ resolving a token to a clear

    /// <summary>
    /// <c>PREVIOUS_CHAPTER_NORMAL</c> names no chapter before chapter 1, so the Normal rung
    /// demands nothing there.
    /// </summary>
    [Fact]
    public void PREVIOUS_CHAPTER_NORMAL_demands_nothing_of_the_first_chapter()
    {
        Shipped().Rung(DifficultyTier.NORMAL).RequiredClear(1).ShouldBeNull(
            "10 §7 names the chapter BEFORE this one, and there is none before chapter 1. A clear " +
            "demanded here is the game's first chapter locked against every new player.");
    }

    /// <summary>The same token names the immediately preceding chapter, on Normal, for every chapter after the first.</summary>
    /// <remarks>
    /// Two chapters, not one: an off-by-one that returned the chapter itself is invisible at a single
    /// sample, and 5 is far enough from the boundary that "always chapter 1" fails too.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void PREVIOUS_CHAPTER_NORMAL_names_the_chapter_before_this_one_on_Normal(int chapterId)
    {
        Shipped().Rung(DifficultyTier.NORMAL).RequiredClear(chapterId).ShouldBe(
            new ChapterClear(chapterId - 1, DifficultyTier.NORMAL),
            $"10 §7: chapter {chapterId} Normal is unlocked by clearing chapter {chapterId - 1} on " +
            "Normal. Naming this chapter would gate it on itself; naming a tier other than Normal " +
            "would gate it on a clear the ladder above it never demands.");
    }

    /// <summary><c>SAME_CHAPTER_NORMAL</c> names this chapter, on Normal.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void SAME_CHAPTER_NORMAL_names_this_chapter_on_Normal(int chapterId)
    {
        Shipped().Rung(DifficultyTier.HEROIC).RequiredClear(chapterId).ShouldBe(
            new ChapterClear(chapterId, DifficultyTier.NORMAL),
            $"10 §7: chapter {chapterId} Heroic is unlocked by clearing chapter {chapterId} on " +
            "Normal — this chapter, not the one before it.");
    }

    /// <summary><c>SAME_CHAPTER_HEROIC</c> names this chapter, on Heroic.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void SAME_CHAPTER_HEROIC_names_this_chapter_on_Heroic(int chapterId)
    {
        Shipped().Rung(DifficultyTier.MYTHIC).RequiredClear(chapterId).ShouldBe(
            new ChapterClear(chapterId, DifficultyTier.HEROIC),
            $"10 §7: chapter {chapterId} Mythic is unlocked by clearing chapter {chapterId} on " +
            "Heroic. Naming Normal here would let a player skip the middle tier entirely.");
    }

    /// <summary>A rung authoring no clear token demands no clear, at any chapter.</summary>
    [Fact]
    public void A_rung_authoring_no_clear_token_demands_no_clear()
    {
        var ladder = ChapterGatingTuning.Read(ProgressionDocuments.With(
            chapterGating: ProgressionDocuments.ChapterGating(
                ("NORMAL", ProgressionDocuments.Rung(ContentValue.Unauthorised, ContentValue.Unauthorised)),
                ("HEROIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterNormal), ContentValue.Unauthorised)),
                ("MYTHIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterHeroic),
                    ContentValue.Number(ProgressionDocuments.ShippedMythicRequiresLegendLevel))))));

        var rung = ladder.Rung(DifficultyTier.NORMAL);

        rung.RequiresClear.ShouldBeNull();
        rung.RequiredClear(3).ShouldBeNull(
            "an authored null is 'this rung demands no clear' — the one authored shape that opens a " +
            "rung. Everything the reader cannot translate is refused instead.");
    }

    // -------------------------------------------------------------------------------- the refusals

    /// <summary>The block is not in the document at all.</summary>
    [Fact]
    public void A_document_with_no_chapterGating_block_is_refused()
    {
        Should.Throw<MissingContentException>(
                () => ChapterGatingTuning.Read(ProgressionDocuments.WithoutChapterGating()))
            .Reference.ShouldBe(
                ChapterGatingTuning.GatingReference,
                "a missing ladder must be refused at the ladder's own pointer. Answered as 'no rung " +
                "demands anything', every tier of every chapter would be open to everybody.");
    }

    /// <summary>
    /// An authored <c>null</c> block is a different failure from an absent one, told apart by type.
    /// </summary>
    [Fact]
    public void A_chapterGating_block_authored_as_null_is_refused()
    {
        Should.Throw<UnauthorisedTunableException>(
                () => ChapterGatingTuning.Read(ProgressionDocuments.With(chapterGating: ContentValue.Unauthorised)))
            .Reference.ShouldBe(ChapterGatingTuning.GatingReference);
    }

    /// <summary>The block is present and authorised, and is not an object of rungs.</summary>
    [Fact]
    public void A_chapterGating_block_that_is_not_an_object_is_refused()
    {
        Should.Throw<ContentTypeMismatchException>(
                () => ChapterGatingTuning.Read(
                    ProgressionDocuments.With(chapterGating: ContentValue.Text("PREVIOUS_CHAPTER_NORMAL"))))
            .Reference.ShouldBe(ChapterGatingTuning.GatingReference);
    }

    /// <summary>
    /// A rung is required for every declared <see cref="DifficultyTier"/>, and a ladder missing one
    /// is refused by name. Every tier is dropped in turn: a reader that looked for a MYTHIC key by
    /// name would pass a single-tier case and leave the two rungs a run actually starts on ungated.
    /// </summary>
    [Theory]
    [InlineData(DifficultyTier.NORMAL)]
    [InlineData(DifficultyTier.HEROIC)]
    [InlineData(DifficultyTier.MYTHIC)]
    public void A_ladder_missing_a_declared_tiers_rung_is_refused_by_name(DifficultyTier missing)
    {
        var thrown = Should.Throw<MissingContentException>(
            () => ChapterGatingTuning.Read(LadderWith(missing, rung: null)));

        thrown.Reference.ShouldBe(
            $"{ChapterGatingTuning.GatingReference}/{missing}",
            $"the refusal must name the tier whose rung is gone. DifficultyTier declares {missing}, " +
            "so a ladder without that rung is a tier the gate cannot answer for — and answering it " +
            "with 'demands nothing' opens it to a brand-new account.");

        thrown.Message.ShouldContain(
            missing.ToString(),
            Case.Sensitive,
            "a reader whose message does not name the missing tier sends its reader to diff two files.");
    }

    /// <summary>
    /// An unrecognised clear token is refused — deliberately opposite to the chapter select screen,
    /// whose fallback treats an unknown token as demanding nothing and therefore OPENS the rung.
    /// </summary>
    [Fact]
    public void An_unrecognised_requiresClear_token_is_refused_rather_than_read_as_demanding_nothing()
    {
        const string Typo = "PREVIOUS_CHAPTER_NORMALL";

        var mistyped = ProgressionDocuments.With(
            chapterGating: ProgressionDocuments.ChapterGating(
                ("NORMAL", ProgressionDocuments.Rung(ContentValue.Text(Typo), ContentValue.Unauthorised)),
                ("HEROIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterNormal), ContentValue.Unauthorised)),
                ("MYTHIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterHeroic),
                    ContentValue.Number(ProgressionDocuments.ShippedMythicRequiresLegendLevel)))));

        var thrown = Should.Throw<InvalidTunableException>(() => ChapterGatingTuning.Read(mistyped));

        thrown.Reference.ShouldBe(
            ClearPointer(DifficultyTier.NORMAL),
            "the refusal must land on the rung that carries the token, not on the block. Two arms of " +
            "this reader throw InvalidTunableException, and the pointer is what tells them apart.");

        thrown.Message.ShouldContain(
            Typo,
            Case.Sensitive,
            "the refusal must quote the token it could not translate — that string is the entire " +
            "diagnosis, and without it the reader is told only that something in the ladder is wrong.");
    }

    /// <summary>
    /// A Legend Level outside the range a player can hold is refused — both ends, because a
    /// comparison written against one bound only passes the other half by accident.
    /// </summary>
    [Theory]
    [InlineData(ProgressionDocuments.ShippedLegendLevelMax + 1)]
    [InlineData(ProgressionDocuments.ShippedLegendLevelMin - 1)]
    public void A_rung_demanding_a_Legend_Level_outside_the_range_is_refused(int level)
    {
        var outOfRange = ProgressionDocuments.With(
            chapterGating: ProgressionDocuments.ChapterGating(
                ("NORMAL", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.PreviousChapterNormal), ContentValue.Unauthorised)),
                ("HEROIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterNormal), ContentValue.Unauthorised)),
                ("MYTHIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterHeroic), ContentValue.Number(level)))));

        Should.Throw<InvalidTunableException>(() => ChapterGatingTuning.Read(outOfRange))
            .Reference.ShouldBe(
                LevelPointer(DifficultyTier.MYTHIC),
                $"a rung demanding Legend Level {level} must be refused at the level's own pointer — " +
                "the same type the unrecognised-token arm throws, so the pointer is the only thing " +
                "that says which of the two fired.");
    }

    /// <summary>Negative control for the range refusal: the two bounds themselves are accepted.</summary>
    [Theory]
    [InlineData(ProgressionDocuments.ShippedLegendLevelMin)]
    [InlineData(ProgressionDocuments.ShippedLegendLevelMax)]
    public void A_rung_demanding_a_Legend_Level_at_either_bound_is_accepted(int level)
    {
        var atTheBound = ProgressionDocuments.With(
            chapterGating: ProgressionDocuments.ChapterGating(
                ("NORMAL", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.PreviousChapterNormal), ContentValue.Unauthorised)),
                ("HEROIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterNormal), ContentValue.Unauthorised)),
                ("MYTHIC", ProgressionDocuments.Rung(
                    ContentValue.Text(ChapterGatingTuning.SameChapterHeroic), ContentValue.Number(level)))));

        ChapterGatingTuning.Read(atTheBound).Rung(DifficultyTier.MYTHIC).RequiresLegendLevel.ShouldBe(level);
    }

    /// <summary>A null snapshot is a caller defect, not a ladder that demands nothing.</summary>
    [Fact]
    public void A_null_snapshot_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => ChapterGatingTuning.Read(null!));
    }
}
