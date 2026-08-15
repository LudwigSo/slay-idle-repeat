using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `19` Part E — the twelve-curse catalogue and its chapter gate, read out of
/// <c>content/curses/curses.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This is a reader, not the curse engine.</b> `19` Part E's <c>effect</c> and <c>reward</c>
/// columns are the design document's own <b>prose</b> — the content file's own <c>_doc</c> says so —
/// and nothing here parses either. The mechanical engine (no stacking, the paired-reward payout, the
/// <c>AD_SKIP_CURSE</c> hook, mount immunity) is M3-11's, tracked by <c>GapRegister</c>'s
/// <c>Curses</c> entry; this type exists so M3-03's <c>TILE_CURSE</c> resolver can pick a
/// chapter-eligible row rather than inventing one.
/// </para>
/// <para>
/// ⚠️ <b>Named <c>CurseTileRow</c> and not <c>Curse</c>/<c>CurseDefinition</c>, deliberately.</b>
/// <c>GapRegister</c>'s <c>Curses</c> entry is keyed on the simple name <c>CurseDefinition</c> and
/// fails the build the day one exists in <c>Core</c> — that is the mechanism that makes M3-11's
/// deferral expire on time. A row of an authored table is not that type.
/// </para>
/// </remarks>
internal sealed class CurseTuning
{
    /// <summary>The document `19` Part E is transcribed into.</summary>
    internal const string DocumentPath = "content/curses/curses.json";

    /// <summary>`19` Part E — the twelve-row catalogue.</summary>
    internal const string CursesReference = DocumentPath + "#/curses";

    private CurseTuning(IReadOnlyList<CurseTileRow> all)
    {
        All = all;
    }

    /// <summary>Every authored curse, in the order the document lists them.</summary>
    /// <remarks>
    /// 🔒 The order is load-bearing: <see cref="AvailableFrom"/> preserves it and the resolver draws
    /// by index off `14` §8.1's stream, so re-ordering the file changes which curse every existing
    /// run seed draws.
    /// </remarks>
    internal IReadOnlyList<CurseTileRow> All { get; }

    /// <summary>
    /// The curses a run in <paramref name="chapterId"/> may draw — those whose
    /// <c>availableFromChapter</c> has been reached — in the document's order.
    /// </summary>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <remarks>
    /// 🔒 The comparison is <c>availableFromChapter &lt;= chapterId</c>: the column is the
    /// <em>earliest</em> chapter a curse opens in and a curse stays available afterwards. A strict
    /// <c>&lt;</c> would make every curse unreachable in the chapter it is authored to open in, and
    /// dropping the filter entirely would let a chapter-1 run draw <c>CUR_HUNTED</c>, whose reward is
    /// a per-Elite gear drop no system can pay.
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
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
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

            // Both columns are 19 Part E's prose, and both are blank-checked for the reason the id
            // is: nothing in Core parses them, so a blank one is invisible until it reaches a player
            // as an empty curse description or defeats CurseRewardsTests' cross-check of the reward
            // amounts against this prose.
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

/// <summary>One authored row of `19` Part E's curse catalogue.</summary>
/// <remarks>
/// ⚠️ See <see cref="CurseTuning"/> for why this is not called <c>Curse</c> or
/// <c>CurseDefinition</c>: both are architecture-test sentinels for M3-11's engine.
/// </remarks>
/// <param name="Id">The curse id, e.g. <c>CUR_SLIPPERY</c>.</param>
/// <param name="Effect">
/// `19` Part E's Effect column, <b>verbatim prose</b>. Nothing in <c>Core</c> parses it — applying it
/// is M3-11's.
/// </param>
/// <param name="Reward">
/// `19` Part E's Reward column, <b>verbatim prose</b> — a curse pays, the debuff is never free.
/// ⚠️ Nothing here parses it either; <c>CurseRewards</c> carries a narrow, named table for the four
/// rows M3-03 can actually pay, rather than a grammar nobody has specified.
/// </param>
/// <param name="AvailableFromChapter">The earliest chapter this curse can be drawn in.</param>
internal readonly record struct CurseTileRow(
    string Id, string Effect, string Reward, int AvailableFromChapter);
