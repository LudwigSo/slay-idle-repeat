using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>Whether a Legend Level has reached the rung a system opens at.</summary>
/// <remarks>
/// <para>
/// One comparison, stated once, so the ten systems `07` §1.1 gates cannot each grow their own copy of
/// it — and, more usefully, so none of them can grow a copy that reads a level from somewhere else.
/// The ladder is authored data and <see cref="UnlockTuning"/> reads it; this is the question asked of
/// it.
/// </para>
/// <para>
/// ⚠️ <b>Nothing calls it yet, and that is worth saying plainly rather than describing a mechanism
/// that is in use.</b> Every system on the ladder belongs to a milestone that has not run: the pet
/// slots and the Menagerie are M4-07's, the mount slot is M4-08's, the Fortune branch is M4-06's, the
/// Forge is M4-04's, PvP is M12's, Mythic is the difficulty gate's and Codex mastery is later still.
/// The gate lands with the curve because the curve is what makes a rung mean anything, and because
/// the alternative — each of those tasks inventing its own comparison against its own reading of the
/// ladder — is how one of them ends up gating on a level nobody authored.
/// </para>
/// <para>
/// It answers a <see cref="bool"/> and not a rejection: what a locked system refuses <em>with</em> is
/// the calling command's decision, and `14` §16.2's table has no row that means "not levelled enough"
/// for this rule to pick on its behalf.
/// </para>
/// </remarks>
internal static class UnlockGate
{
    /// <summary>Whether a system is open to a player at this Legend Level.</summary>
    /// <param name="unlockId">One of the ids the ladder authors.</param>
    /// <param name="legendLevel">The player's Legend Level.</param>
    /// <param name="ladder">The authored ladder.</param>
    /// <returns><see langword="true"/> when the level has reached the rung.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ladder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="unlockId"/> is blank.</exception>
    /// <exception cref="MissingContentException">
    /// The ladder authors no rung for <paramref name="unlockId"/>. Never answered as "unlocked" and
    /// never as "locked": a typo that read as either would be a gate silently deciding on its own.
    /// </exception>
    internal static bool IsUnlocked(string unlockId, int legendLevel, UnlockTuning ladder)
    {
        ArgumentNullException.ThrowIfNull(ladder);

        return legendLevel >= ladder.LevelFor(unlockId);
    }

    /// <summary>Whether a system is open, reading the ladder out of the content snapshot.</summary>
    /// <param name="unlockId">One of the ids the ladder authors.</param>
    /// <param name="legendLevel">The player's Legend Level.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <returns><see langword="true"/> when the level has reached the rung.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    internal static bool IsUnlocked(string unlockId, int legendLevel, ContentSnapshot content) =>
        IsUnlocked(unlockId, legendLevel, UnlockTuning.Read(content));
}
