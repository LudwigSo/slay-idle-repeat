using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>The twelve-curse catalogue and its chapter gate, read out of <c>content/curses/curses.json</c>.</summary>
/// <remarks>
/// <para>
/// This is a reader, not the curse engine: the <c>effect</c> and <c>reward</c> columns are the
/// design document's own prose, and nothing here parses either. The mechanical engine (stacking,
/// the paired-reward payout, the skip hook, mount immunity) is a later milestone's; this type only
/// lets the curse-tile resolver pick a chapter-eligible row.
/// </para>
/// <para>
/// Named <c>CurseTileRow</c> and not <c>Curse</c>/<c>CurseDefinition</c> deliberately, so a row of
/// an authored table is not confused with the future engine's own type.
/// </para>
/// </remarks>
internal sealed class CurseTuning
{
    /// <summary>The document the curse catalogue is transcribed into.</summary>
    internal const string DocumentPath = "content/curses/curses.json";

    /// <summary>The twelve-row catalogue.</summary>
    internal const string CursesReference = DocumentPath + "#/curses";

    private CurseTuning(IReadOnlyList<CurseTileRow> all)
    {
        All = all;
    }

    /// <summary>Every authored curse, in the order the document lists them.</summary>
    /// <remarks>
    /// The order is load-bearing: <see cref="AvailableFrom"/> preserves it and the resolver draws
    /// by index off the run's random stream, so re-ordering the file changes which curse every
    /// existing run seed draws.
    /// </remarks>
    internal IReadOnlyList<CurseTileRow> All { get; }

    /// <summary>
    /// The curses a run in <paramref name="chapterId"/> may draw — those whose
    /// <c>availableFromChapter</c> has been reached — in the document's order.
    /// </summary>
    /// <param name="chapterId">The chapter, from 1.</param>
    /// <remarks>
    /// The comparison is <c>availableFromChapter &lt;= chapterId</c>: the column is the earliest
    /// chapter a curse opens in and a curse stays available afterwards.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal IReadOnlyList<CurseTileRow> AvailableFrom(int chapterId)
    {
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId),
                chapterId,
                "02 §1 runs chapters from 1 and chapter.schema.json sets \"minimum\": 1; " +
                Text(chapterId) + " is below it.");
        }

        var eligible = new List<CurseTileRow>(All.Count);

        foreach (var row in All)
        {
            if (row.AvailableFromChapter <= chapterId)
            {
                eligible.Add(row);
            }
        }

        return eligible;
    }

    /// <summary>Reads the curse catalogue. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static CurseTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var array = content.Read(CursesReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                CursesReference,
                "19 Part E authors a non-empty curse catalogue (twelve rows as shipped). This " +
                "document authors " + array + ".");
        }

        var rows = new CurseTileRow[array.Items.Count];
        var ids = new HashSet<string>(array.Items.Count, StringComparer.Ordinal);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = CursesReference + "/" + Text(i);
            var entry = array.Items[i];

            var id = Member(entry, "id", pointer).AsText(pointer + "/id");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidTunableException(pointer + "/id", "A curse id must not be blank.");
            }

            if (!ids.Add(id))
            {
                throw new InvalidTunableException(
                    pointer + "/id",
                    "'" + id + "' is authored twice. 14 §6 makes a duplicate id a build failure: " +
                    "the second row would be drawn twice as often as every other and the first " +
                    "would be unreachable by id.");
            }

            // Both columns are free-form prose and are blank-checked for the same reason as the id:
            // nothing parses them, so a blank one is invisible until it reaches a player.
            var effect = RequiredText(entry, "effect", pointer);
            var reward = RequiredText(entry, "reward", pointer);
            var availableFrom = Member(entry, "availableFromChapter", pointer)
                .AsInt32(pointer + "/availableFromChapter");

            if (availableFrom < 1)
            {
                throw new InvalidTunableException(
                    pointer + "/availableFromChapter",
                    "The chapter gate names the earliest chapter a curse opens in, and 02 §1 runs " +
                    "chapters from 1. This document authors " + Text(availableFrom) + ".");
            }

            rows[i] = new CurseTileRow(id, effect, reward, availableFrom);
        }

        return new CurseTuning(Array.AsReadOnly(rows));
    }

    /// <summary>A required text member, refused when blank. The same helper shape
    /// <c>EventCatalogue</c> uses.</summary>
    private static string RequiredText(ContentValue obj, string name, string pointer)
    {
        var reference = pointer + "/" + name;
        var text = Member(obj, name, pointer).AsText(reference);

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidTunableException(reference, "'" + name + "' must not be blank.")
            : text;
    }

    private static ContentValue Member(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null
            ? value
            : throw new MissingContentException(pointer + "/" + name, "'" + name + "' resolves to nothing");

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One authored row of the curse catalogue.</summary>
/// <param name="Id">The curse id, e.g. <c>CUR_SLIPPERY</c>.</param>
/// <param name="Effect">The Effect column, verbatim prose. Nothing in <c>Core</c> parses it.</param>
/// <param name="Reward">
/// The Reward column, verbatim prose — a curse pays, the debuff is never free. Nothing here parses
/// it either; <see cref="CurseRewards"/> carries a narrow, named table for the rows that can
/// actually be paid.
/// </param>
/// <param name="AvailableFromChapter">The earliest chapter this curse can be drawn in.</param>
internal readonly record struct CurseTileRow(
    string Id, string Effect, string Reward, int AvailableFromChapter);
