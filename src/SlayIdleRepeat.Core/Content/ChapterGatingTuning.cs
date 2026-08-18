using System.Collections.ObjectModel;
using System.Globalization;
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
    /// <summary>Built by <see cref="ChapterGatingTuning.Read"/>, which has already validated both demands.</summary>
    /// <param name="requiresClear">A token <see cref="ChapterGatingTuning"/> recognises, or <c>null</c>.</param>
    /// <param name="requiresLegendLevel">A level inside <see cref="LegendTuning"/>'s range, or <c>null</c>.</param>
    internal ChapterGatingRung(string? requiresClear, int? requiresLegendLevel)
    {
        RequiresClear = requiresClear;
        RequiresLegendLevel = requiresLegendLevel;
    }

    /// <summary>The authored clear token, or <c>null</c> when the rung demands no clear.</summary>
    internal string? RequiresClear { get; }

    /// <summary>The authored Legend Level, or <c>null</c> when the rung demands no level.</summary>
    internal int? RequiresLegendLevel { get; }

    /// <summary>The clear this rung demands of a given chapter, or <c>null</c> when it demands none.</summary>
    /// <remarks>
    /// The token is resolved, never the tier it was found on: which rung carries which token is the
    /// document's business, and a resolver keyed on the tier would answer the shipped ladder for
    /// every ladder.
    /// </remarks>
    /// <param name="chapterId">The chapter the run is being started on.</param>
    /// <returns>The clear, or <c>null</c>.</returns>
    internal ChapterClear? RequiredClear(int chapterId) => RequiresClear switch
    {
        // This token names the chapter BEFORE this one, and there is none before the first — which
        // is what keeps the ladder from locking the game's own front door.
        ChapterGatingTuning.PreviousChapterNormal => chapterId > 1
            ? new ChapterClear(chapterId - 1, DifficultyTier.NORMAL)
            : null,
        ChapterGatingTuning.SameChapterNormal => new ChapterClear(chapterId, DifficultyTier.NORMAL),
        ChapterGatingTuning.SameChapterHeroic => new ChapterClear(chapterId, DifficultyTier.HEROIC),

        // Deliberately not an "unrecognised token" arm: the only value that reaches here is an
        // authored null, because ChapterGatingTuning.Read refused every other unknown token before
        // this rung was ever built.
        _ => null,
    };
}

/// <summary>
/// The chapter/tier unlock ladder — which clear and which Legend Level a tier demands — read out of
/// <c>tuning/progression.json#/chapterGating</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ladder's home is the tuning document, not this type.</b> `10` §7's three rungs and the one
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
/// that tier with nothing to compare against and therefore silently ungated. Unlike
/// <c>UnlockTuning</c>'s hand-transcribed floor this one is reflected off the enum and so cannot go
/// stale — nor can it go empty: <see cref="DifficultyTier"/> declares three members, and
/// <c>DifficultyTierMatchesTuningDataTests</c> holds them against the authored data.
/// </para>
/// </remarks>
internal sealed class ChapterGatingTuning
{
    /// <summary>The document the ladder lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    /// <summary>The block the ladder lives in.</summary>
    internal const string GatingReference = DocumentPath + "#/chapterGating";

    /// <summary>The <c>_doc</c> member every authored block carries, which is prose rather than a rung.</summary>
    private const string DocMember = "_doc";

    /// <summary>The chapter before this one, cleared on Normal. Names nothing for chapter 1.</summary>
    internal const string PreviousChapterNormal = "PREVIOUS_CHAPTER_NORMAL";

    /// <summary>This same chapter, cleared on Normal.</summary>
    internal const string SameChapterNormal = "SAME_CHAPTER_NORMAL";

    /// <summary>This same chapter, cleared on Heroic.</summary>
    internal const string SameChapterHeroic = "SAME_CHAPTER_HEROIC";

    /// <summary>The member name a rung authors its clear under.</summary>
    private const string RequiresClearMember = "requiresClear";

    /// <summary>The member name a rung authors its Legend Level under.</summary>
    private const string RequiresLegendLevelMember = "requiresLegendLevel";

    /// <summary>Every tier the game declares, by the member name a rung for it is authored under.</summary>
    /// <remarks>
    /// A name lookup rather than <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/>, which also
    /// accepts the numeric spelling of a member — a rung keyed <c>"1"</c> would silently become the
    /// Normal rung.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, DifficultyTier> DeclaredTiers =
        new ReadOnlyDictionary<string, DifficultyTier>(
            Enum.GetValues<DifficultyTier>().ToDictionary(tier => tier.ToString(), StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<DifficultyTier, ChapterGatingRung> _ladder;

    private ChapterGatingTuning(IReadOnlyDictionary<DifficultyTier, ChapterGatingRung> ladder) =>
        _ladder = ladder;

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
    internal static ChapterGatingTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var range = LegendTuning.Read(content);
        var authored = content.Read(GatingReference);

        if (authored.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(GatingReference);
        }

        if (authored.Kind != ContentValueKind.Object)
        {
            throw new ContentTypeMismatchException(
                GatingReference, authored.Kind, "an object of difficulty tier -> rung");
        }

        var ladder = new Dictionary<DifficultyTier, ChapterGatingRung>(authored.MemberNames.Count);

        foreach (var member in authored.MemberNames)
        {
            if (string.Equals(member, DocMember, StringComparison.Ordinal))
            {
                continue;
            }

            if (!DeclaredTiers.TryGetValue(member, out var tier))
            {
                throw new InvalidTunableException(
                    GatingReference + "/" + member,
                    "'" + member + "' is not a tier this game declares, so nothing will ever ask " +
                    "this rung a question. Skipped rather than refused it would be a gate somebody " +
                    "wrote down and nothing enforces");
            }

            ladder[tier] = ReadRung(content, tier, range);
        }

        foreach (var tier in Enum.GetValues<DifficultyTier>())
        {
            if (ladder.ContainsKey(tier))
            {
                continue;
            }

            throw new MissingContentException(
                GatingReference + "/" + tier,
                "DifficultyTier declares " + tier + " and this document authors no rung for it. " +
                "A tier with no rung is a tier the gate cannot answer for, and answering it with " +
                "'demands nothing' hands it to a brand-new account");
        }

        return new ChapterGatingTuning(
            new ReadOnlyDictionary<DifficultyTier, ChapterGatingRung>(ladder));
    }

    /// <summary>The rung one tier stands on.</summary>
    /// <param name="tier">A declared tier.</param>
    /// <returns>The rung.</returns>
    /// <exception cref="MissingContentException">
    /// <paramref name="tier"/> is not a tier the ladder authors — which <see cref="Read"/>'s floor
    /// makes unreachable for a declared one, so this is a caller that cast an undeclared value.
    /// </exception>
    internal ChapterGatingRung Rung(DifficultyTier tier) =>
        _ladder.TryGetValue(tier, out var rung)
            ? rung
            : throw new MissingContentException(
                GatingReference + "/" + tier,
                "the ladder authors " + Render(_ladder.Count) + " rung(s) and none of them is that " +
                "one. Every DifficultyTier the game declares has one, so this is a tier value cast " +
                "from outside the enum and it must be refused on shape before the ladder is asked");

    /// <summary>Reads one tier's rung, refusing a clear or a level the gate could not act on.</summary>
    private static ChapterGatingRung ReadRung(ContentSnapshot content, DifficultyTier tier, LegendTuning range)
    {
        var rungReference = GatingReference + "/" + tier;
        var authored = content.Read(rungReference);

        if (authored.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(rungReference);
        }

        if (authored.Kind != ContentValueKind.Object)
        {
            throw new ContentTypeMismatchException(
                rungReference, authored.Kind, "an object of requiresClear and requiresLegendLevel");
        }

        return new ChapterGatingRung(
            ReadClear(content, rungReference + "/" + RequiresClearMember),
            ReadLegendLevel(content, rungReference + "/" + RequiresLegendLevelMember, range));
    }

    /// <summary>The clear token a rung demands, or <c>null</c> for an authored <c>null</c>.</summary>
    private static string? ReadClear(ContentSnapshot content, string reference)
    {
        if (content.Read(reference).IsUnauthorised)
        {
            return null;
        }

        var token = content.ReadText(reference);

        if (token is PreviousChapterNormal or SameChapterNormal or SameChapterHeroic)
        {
            return token;
        }

        throw new InvalidTunableException(
            reference,
            "'" + token + "' is not a clear 10 §7's ladder can name. This is refused rather than " +
            "read as 'this rung demands no clear' — the chapter select screen's fallback does read " +
            "it that way, which shows a player one locked chapter too many, while an authority that " +
            "did it would ship the tier unlocked to everybody on a spelling mistake. An authored " +
            "null is how a rung says it demands no clear");
    }

    /// <summary>The Legend Level a rung demands, or <c>null</c> for an authored <c>null</c>.</summary>
    private static int? ReadLegendLevel(ContentSnapshot content, string reference, LegendTuning range)
    {
        if (content.Read(reference).IsUnauthorised)
        {
            return null;
        }

        var level = content.ReadInt32(reference);

        if (level < range.Minimum || level > range.Maximum)
        {
            throw new InvalidTunableException(
                reference,
                "this rung demands Legend Level " + Render(level) + ", outside the " +
                Render(range.Minimum) + ".." + Render(range.Maximum) + " a player can hold. A rung " +
                "above the cap is a tier no account can ever reach, and one below the starting " +
                "level is a gate that was never closed");
        }

        return level;
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so a message reads the same on every host.</summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
