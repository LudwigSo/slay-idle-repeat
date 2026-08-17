using System.Collections.ObjectModel;
using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The Legend Level unlock ladder — which system opens at which level — read out of
/// <c>tuning/progression.json#/unlocks</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ladder's home is the tuning document, not this type.</b> `07` §1.1's table is authored
/// content and was already transcribed into <c>#/unlocks</c> before this reader existed; what is
/// added here is the reading and the refusals, never the numbers.
/// </para>
/// <para>
/// 🔒 <b>The key space is open, the `07` §1.1 rows are not.</b> The document carries three rows the
/// hero document does not state — <c>DUNGEONS</c> (`25`), <c>GUILDS</c> (`27` §2) and <c>EVENTS</c>
/// (`26`) — because a system's unlock level is authored by the document that owns the system, and a
/// closed enum here would make every future system a code edit. What is closed is the other
/// direction: <see cref="RowsAuthoredBy07"/> transcribes the nine keyed rows of `07` §1.1 by hand,
/// and a set missing any of them is refused. Without that floor, a data edit that deleted a row would
/// leave <see cref="LevelFor"/> with nothing to answer and every gate it guards silently ungated —
/// which is exactly the failure a reader is supposed to make impossible.
/// </para>
/// <para>
/// ⚠️ <b>`07` §1.1's tenth row is the level cap and is deliberately not here.</b> "200 | Level cap" is
/// <see cref="LegendTuning.MaximumReference"/> — the range, not an unlock — and a second copy of it
/// under <c>#/unlocks</c> would be a number that can disagree with itself. What this reader does
/// instead is refuse a ladder with a rung <em>above</em> the cap, which is the only way the two can
/// contradict each other while both being individually plausible.
/// </para>
/// <para>
/// A hole is never a default. In particular <see cref="LevelFor"/> throws on an unknown id rather
/// than answering the minimum level: an unlock nobody authored, read as "unlocked from level 1", is
/// every gate in the game open by typo.
/// </para>
/// </remarks>
internal sealed class UnlockTuning
{
    /// <summary>The document the ladder lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    /// <summary>The block the ladder lives in.</summary>
    internal const string UnlocksReference = DocumentPath + "#/unlocks";

    /// <summary>The <c>_doc</c> member every authored block carries, which is prose rather than a rung.</summary>
    private const string DocMember = "_doc";

    /// <summary>Pet slot 1 and the Menagerie screen. Legend Level 5.</summary>
    internal const string PetSlot1 = "PET_SLOT_1";

    /// <summary>The Forge — merge and enhance. Legend Level 8.</summary>
    internal const string Forge = "FORGE";

    /// <summary>PvP Ghost Duel. Legend Level 10.</summary>
    internal const string Pvp = "PVP";

    /// <summary>Pet slot 2. Legend Level 15.</summary>
    internal const string PetSlot2 = "PET_SLOT_2";

    /// <summary>The mount slot. Legend Level 20.</summary>
    internal const string MountSlot = "MOUNT_SLOT";

    /// <summary>Pet slot 3. Legend Level 30.</summary>
    internal const string PetSlot3 = "PET_SLOT_3";

    /// <summary>Talent tree branch 3, Fortune. Legend Level 40.</summary>
    internal const string TalentBranchFortune = "TALENT_BRANCH_FORTUNE";

    /// <summary>The Mythic difficulty tier. Legend Level 60.</summary>
    internal const string MythicTier = "MYTHIC_TIER";

    /// <summary>Codex mastery bonuses. Legend Level 100.</summary>
    internal const string CodexMastery = "CODEX_MASTERY";

    /// <summary>
    /// 🔒 The nine keyed rows of `07` §1.1's unlock table, transcribed by hand from the document.
    /// </summary>
    /// <remarks>
    /// Transcribed rather than derived, which is the only way this can work: a list read off the data
    /// would say the data is complete because the data says so. The tenth row — the level cap — is
    /// the range's, see the type's remarks.
    /// </remarks>
    internal static IReadOnlyList<string> RowsAuthoredBy07 { get; } = Array.AsReadOnly(new[]
    {
        PetSlot1,
        Forge,
        Pvp,
        PetSlot2,
        MountSlot,
        PetSlot3,
        TalentBranchFortune,
        MythicTier,
        CodexMastery,
    });

    private readonly IReadOnlyDictionary<string, int> _ladder;

    private UnlockTuning(IReadOnlyDictionary<string, int> ladder) => _ladder = ladder;

    /// <summary>Every rung, keyed by unlock id. Ordinal, and never empty.</summary>
    internal IReadOnlyDictionary<string, int> Ladder => _ladder;

    /// <summary>The Legend Level one system unlocks at.</summary>
    /// <param name="unlockId">An id the ladder authors.</param>
    /// <returns>The level.</returns>
    /// <exception cref="ArgumentException"><paramref name="unlockId"/> is blank.</exception>
    /// <exception cref="MissingContentException">
    /// The ladder authors no rung for it. Refused rather than answered with the starting level: an
    /// unlock nobody wrote down, read as "open from level 1", is a gate that silently is not one.
    /// </exception>
    internal int LevelFor(string unlockId)
    {
        if (string.IsNullOrWhiteSpace(unlockId))
        {
            throw new ArgumentException(
                "An unlock id names the system it opens, so it is never blank.", nameof(unlockId));
        }

        return _ladder.TryGetValue(unlockId, out var level)
            ? level
            : throw new MissingContentException(
                UnlocksReference + "/" + unlockId,
                "the ladder authors " + Render(_ladder.Count) + " rung(s) and none of them is that " +
                "one. This is refused rather than answered with the starting Legend Level, because " +
                "an unlock read as 'open from level 1' is a gate that has silently stopped being one");
    }

    /// <summary>
    /// Reads the ladder. Throws rather than defaulting on anything missing, unauthorised, mistyped or
    /// nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The ladder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The block or a `07` §1.1 row is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The block holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The block is not an object of whole levels.</exception>
    /// <exception cref="InvalidTunableException">A rung is authorised but unusable.</exception>
    internal static UnlockTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var range = LegendTuning.Read(content);
        var authored = content.Read(UnlocksReference);

        if (authored.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(UnlocksReference);
        }

        if (authored.Kind != ContentValueKind.Object)
        {
            throw new ContentTypeMismatchException(
                UnlocksReference, authored.Kind, "an object of unlock id -> Legend Level");
        }

        var ladder = new Dictionary<string, int>(authored.MemberNames.Count, StringComparer.Ordinal);

        foreach (var member in authored.MemberNames)
        {
            if (string.Equals(member, DocMember, StringComparison.Ordinal))
            {
                continue;
            }

            var reference = UnlocksReference + "/" + member;
            var level = content.ReadInt32(reference);

            if (level < range.Minimum || level > range.Maximum)
            {
                throw new InvalidTunableException(
                    reference,
                    "'" + member + "' unlocks at Legend Level " + Render(level) + ", outside the " +
                    Render(range.Minimum) + ".." + Render(range.Maximum) + " a player can hold. A " +
                    "rung above the cap is a system no account can ever reach, and one below the " +
                    "starting level is a gate that was never closed.");
            }

            ladder[member] = level;
        }

        foreach (var row in RowsAuthoredBy07)
        {
            if (ladder.ContainsKey(row))
            {
                continue;
            }

            throw new MissingContentException(
                UnlocksReference + "/" + row,
                "07 §1.1's unlock table authors it and this document does not. The nine keyed rows " +
                "of that table are transcribed in UnlockTuning.RowsAuthoredBy07 and are required: " +
                "without the floor, a data edit that dropped a row would leave every gate it guards " +
                "with nothing to compare against, and the ladder would report itself complete");
        }

        return new UnlockTuning(new ReadOnlyDictionary<string, int>(ladder));
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so a message reads the same on every host.</summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
