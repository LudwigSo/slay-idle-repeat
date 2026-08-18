using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>One clear a rung can demand: a chapter, and the tier it must have been cleared on.</summary>
/// <param name="ChapterId">The chapter that must have been cleared.</param>
/// <param name="Tier">The tier it must have been cleared on.</param>
internal readonly record struct ChapterClear(int ChapterId, DifficultyTier Tier);

/// <summary>One rung of the chapter/tier ladder: what a tier demands before a run may start on it.</summary>
/// <remarks>
/// Both demands are optional and independently so: the Normal rung of chapter 1 demands neither, the
/// Heroic rung demands a clear and no level, and only the Mythic rung demands both.
/// </remarks>
internal sealed class ChapterGatingRung
{
    /// <summary>The authored clear token, or <c>null</c> when the rung demands no clear.</summary>
    internal string? RequiresClear => throw new NotImplementedException(NoBodyYet);

    /// <summary>The authored Legend Level, or <c>null</c> when the rung demands no level.</summary>
    internal int? RequiresLegendLevel => throw new NotImplementedException(NoBodyYet);

    /// <summary>The clear this rung demands of a given chapter, or <c>null</c> when it demands none.</summary>
    /// <param name="chapterId">The chapter the run is being started on.</param>
    /// <returns>The clear, or <c>null</c>.</returns>
    internal ChapterClear? RequiredClear(int chapterId) => throw new NotImplementedException(NoBodyYet);

    /// <summary>Why every member above throws today.</summary>
    private const string NoBodyYet =
        "ChapterGatingRung has no body yet: the suite that names it is written and red, and the " +
        "reader itself is the next change.";
}

/// <summary>
/// The chapter/tier unlock ladder — which clear and which Legend Level a tier demands — read out of
/// <c>tuning/progression.json#/chapterGating</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ladder's home is the tuning document, not this type.</b> The three rungs and the one
/// Legend Level are authored content; what is added here is the reading and the refusals.
/// </para>
/// <para>
/// 🔒 <b>An unrecognised clear token is refused, never read as "demands nothing".</b> The chapter
/// select screen's own fallback opens a rung it cannot translate, which is the right answer for a
/// screen and the wrong one for the authority: a gate that opened on a typo would ship a tier
/// unlocked to everybody. Every refusal here follows the same rule as the rest of
/// <c>Core/Content/</c> — a hole is never a default.
/// </para>
/// <para>
/// 🔒 <b>The floor is the tier list itself.</b> A rung is required for every declared
/// <see cref="DifficultyTier"/>, so a data edit that dropped one fails the read rather than leaving
/// that tier with nothing to compare against and therefore silently ungated.
/// </para>
/// </remarks>
internal sealed class ChapterGatingTuning
{
    /// <summary>The document the ladder lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    /// <summary>The block the ladder lives in.</summary>
    internal const string GatingReference = DocumentPath + "#/chapterGating";

    /// <summary>The chapter before this one, cleared on Normal. Names nothing for chapter 1.</summary>
    internal const string PreviousChapterNormal = "PREVIOUS_CHAPTER_NORMAL";

    /// <summary>This same chapter, cleared on Normal.</summary>
    internal const string SameChapterNormal = "SAME_CHAPTER_NORMAL";

    /// <summary>This same chapter, cleared on Heroic.</summary>
    internal const string SameChapterHeroic = "SAME_CHAPTER_HEROIC";

    /// <summary>
    /// Reads the ladder. Throws rather than defaulting on anything missing, unauthorised, mistyped
    /// or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The ladder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The block, or a declared tier's rung, is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The block holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The block is not an object of rungs.</exception>
    /// <exception cref="InvalidTunableException">A rung is authorised but unusable.</exception>
    internal static ChapterGatingTuning Read(ContentSnapshot content) =>
        throw new NotImplementedException(NoBodyYet);

    /// <summary>The rung one tier stands on.</summary>
    /// <param name="tier">A declared tier.</param>
    /// <returns>The rung.</returns>
    internal ChapterGatingRung Rung(DifficultyTier tier) => throw new NotImplementedException(NoBodyYet);

    /// <summary>Why every member above throws today.</summary>
    private const string NoBodyYet =
        "ChapterGatingTuning has no body yet: the suite that names it is written and red, and the " +
        "reader itself is the next change.";
}
