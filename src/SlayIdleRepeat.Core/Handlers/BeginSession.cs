using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>BEGIN_SESSION</c> handler: the game's day cycle. On the first call of the game day it
/// advances the login calendar, grants the daily free Energy refill, and consumes the day's draw
/// seed; every later call that day is a no-op.
/// </summary>
internal static class BeginSession
{
    /// <summary>
    /// The daily counter key that answers "has today's BEGIN_SESSION already run?".
    /// </summary>
    /// <remarks>
    /// Must be set by this handler itself, after the daily-counter catch-up runs, not keyed off a
    /// counter an earlier command set. A forward clock jump pins the daily period boundary early, so
    /// a later, corrected clock still finds this counter set and treats the day as already paid —
    /// grants land early, never twice.
    /// </remarks>
    internal const string DailyRunCounter = "begin_session";

    /// <summary>The income-attribution reason the daily free refill is logged under.</summary>
    /// <remarks>Kept distinct from <c>energy_regen</c> so the two budgets stay separately auditable.</remarks>
    internal const string DailyRefillReason = "daily_free_refill";

    /// <summary>Applies <c>BEGIN_SESSION</c>.</summary>
    /// <param name="command">
    /// The command. Both fields are deliberately unread here: <c>ContentHash</c>/<c>ClientVersion</c>
    /// checks are transport-tier concerns handled before the domain is ever called.
    /// </param>
    /// <param name="input">The cloned, already-caught-up slice and everything ambient.</param>
    /// <returns>
    /// Accepted, always: the day's grants on the first call of the game day, no events on every
    /// later one.
    /// </returns>
    internal static HandlerResult Handle(BeginSessionCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var player = input.Player;

        if (player.DailyCount(DailyRunCounter) > 0)
        {
            return HandlerResult.Accept();
        }

        // Read before anything is granted, so a host that forgot to supply a seed fails on the
        // day's first call rather than mid-way through a half-applied day. Nothing is drawn from it
        // yet — the systems that will (quest slate, Daily shop) don't exist.
        _ = input.MetaDraws;

        player.AdvanceLoginCalendar(LoginCalendarTuning.Read(input.Context.Content));

        var tuning = EnergyTuning.Read(input.Context.Content);

        // Deficit-only: a full bar grants nothing and never overflows.
        var refilled = EnergyMath.RefillToFull(tuning, player.LegendLevel, player.Energy);

        // Published even at zero delta, so a full-bar login is distinguishable in the income report
        // from no login at all.
        var refill = player.SetEnergy(refilled, tuning, DailyRefillReason);

        // Written last, so a throw mid-grant can't mark a day as paid that wasn't.
        player.CountDaily(DailyRunCounter, 1);

        return HandlerResult.Accept(refill);
    }
}
