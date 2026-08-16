using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>
/// Reconciles a player's Legend Level against their lifetime Legend XP, and works out what the
/// level-ups grant.
/// </summary>
/// <remarks>
/// <para>
/// <b>The level is derived, not incremented.</b> A player's Legend Level is a function of one number
/// they already carry, so this asks the curve what that number buys rather than trusting anything to
/// have counted the level-ups. Every path that banks XP therefore levels the player up, including the
/// ones no milestone has written yet, and a level-up cannot be granted twice by a command that is
/// replayed — the second reconciliation of the same total answers the same level.
/// </para>
/// <para>
/// 🔒 <b>It never lowers a level.</b> The exponent is the pacing dial and is expected to move; raising
/// it makes the curve steeper, and a player who was level 40 under the old curve would derive to 34
/// under the new one. Taking six levels back — with their Talent Points, their Max Energy and every
/// gate they had passed — would turn a balance patch into an account rollback, which is the trade the
/// Energy ceiling and the inventory capacity already refuse. So the derived level is a floor to rise
/// to, never a value to settle back to.
/// </para>
/// <para>
/// <b>What a level-up grants</b> is two things from two documents, and both land here so neither can
/// be forgotten: `07` §1.1's Talent Point per level, and `10` §3.1's Energy refill to full. The refill
/// is computed against the <em>new</em> level's maximum — `10` §3 grows Max Energy with the level, so
/// refilling against the old one would leave the player short by exactly what the level-up just gave
/// them.
/// </para>
/// <para>
/// ⚠️ <b>The Talent Points are granted and stored, and nothing spends them.</b> The tree itself is
/// M4-06's. Granting them now is the half that cannot wait: `07` §1.1 grants a point per level and
/// the points are earned by play, so a counter that starts when the tree ships is a counter of zero
/// for every player who levelled before it — the same argument that made the lifetime feat counters
/// land ahead of the feats.
/// </para>
/// </remarks>
internal static class LegendProgression
{
    /// <summary>Works out the level a player's lifetime XP has bought, and what reaching it grants.</summary>
    /// <param name="currentLevel">The Legend Level the player stands at.</param>
    /// <param name="lifetimeXp">Their lifetime banked Legend XP.</param>
    /// <param name="banks">Their Energy banks, as the command left them.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <returns>
    /// The reconciliation. <c>Occurred</c> is false — and the banks come back unchanged — when the
    /// player is already standing where their XP puts them, which is the normal case on every command.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetimeXp"/> is negative.</exception>
    internal static LegendLevelUp Reconcile(
        int currentLevel, long lifetimeXp, EnergyBanks banks, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var range = LegendTuning.Read(content);
        var curve = LegendCurveTuning.Read(content);

        var derived = LegendLevelCurve.LevelFor(lifetimeXp, curve, range);
        var next = Math.Max(currentLevel, derived);

        if (next == currentLevel)
        {
            return new LegendLevelUp(currentLevel, currentLevel, 0L, banks);
        }

        var energy = EnergyTuning.Read(content);

        return new LegendLevelUp(
            currentLevel,
            next,
            (long)(next - currentLevel) * curve.TalentPointsPerLevel,
            EnergyMath.RefillToFull(energy, next, banks));
    }
}
