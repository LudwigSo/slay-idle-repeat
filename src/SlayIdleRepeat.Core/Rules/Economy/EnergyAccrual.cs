using System.Globalization;
using System.Text;

using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// 🔒 What one accrual did: the banks afterwards, and how far the accrual anchor moves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recorded assumption A1.</b> <see cref="AnchorAdvance"/> is <c>wholeUnits × the regeneration
/// interval</c> — never the elapsed span the caller asked about. The caller advances its stored
/// anchor by exactly this, so the sub-interval remainder stays banked in the gap between the
/// anchor and now instead of being discarded once per command.
/// </para>
/// <para>
/// `10` §3's regeneration is the only offline accrual in the game, and M1-08's <c>AdvanceTime</c>
/// runs it as the first step of every command handler; a hundred commands in an hour must
/// regenerate exactly what one command in that hour regenerates.
/// </para>
/// <para>
/// ⚠️ <b>Why a span and not the new anchor, honestly.</b> Not because `30` §9 forbids an instant —
/// it bans <em>reading</em> a clock, and <c>GameContext.NowUtc</c> is a <c>DateTimeOffset</c>
/// handed to <c>Core</c> on every command, so <c>Accrue(…, anchor, now)</c> returning the new
/// anchor would violate nothing. It is a trade, and the cost is real: returning a span leaves
/// "never advance to now" as a caller obligation this API cannot enforce, and M1-08 is free to
/// write <c>anchor = now</c> with every single-step test still green. What it buys is that the rule
/// takes no instants at all, so it cannot grow a dependency on <em>which</em> instant, and the
/// caller keeps one anchor rather than the rule and the caller each holding one. If M1-08 finds the
/// obligation hard to hold, taking both instants is the fix and it costs this type nothing.
/// </para>
/// <para>
/// <see cref="AnchorAdvance"/> moves even when <see cref="Banks"/> does not. A player idling at a
/// full bar and a full Reserve regenerates nothing storable, but the time still passed — were the
/// anchor to stay put, a week of it would be waiting the instant they spent a single point.
/// </para>
/// </remarks>
/// <param name="Banks">The two banks after the accrual.</param>
/// <param name="AnchorAdvance">
/// How far to move the accrual anchor: a whole multiple of the regeneration interval, and never
/// past the instant the caller asked about.
/// </param>
internal readonly record struct EnergyAccrual(EnergyBanks Banks, TimeSpan AnchorAdvance)
{
    /// <summary>
    /// 🔒 Renders with <see cref="CultureInfo.InvariantCulture"/>, replacing the synthesized
    /// <c>PrintMembers</c>, which formats with the ambient culture. Same reason as
    /// <c>GameContext</c>: `14` §8.2 wants <c>Core</c> reading identically everywhere.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"Banks = {Banks}");
        builder.Append(CultureInfo.InvariantCulture, $", AnchorAdvance = {AnchorAdvance:c}");

        return true;
    }
}
