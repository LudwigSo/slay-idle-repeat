namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 `30` §2.3's game calendar — the 05:00 UTC game day and the Monday 05:00 UTC game week
/// (milestone assumption <b>A2</b>, derived from `27` §4), computed from an instant.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Two callers, one definition, and that is the whole reason this type exists.</b>
/// <c>Player</c> holds `30` §2.3's boundary as an <em>invariant</em> — it refuses a daily period
/// that is not 05:00 UTC and a weekly one that is not a Monday — and <c>GameRules.AdvanceTime</c>
/// has to <em>compute</em> the boundary it hands the aggregate. Those two are written in layers
/// that cannot see each other, so before this type they were two transcriptions of the same
/// number, and a calendar that drifted would not fail as a wrong answer but as an
/// <see cref="ArgumentOutOfRangeException"/> out of <c>GameRules.Apply</c> — a `30` §2.1 <b>P3</b>
/// violation reported as a crash. Every member below has a production caller: the two
/// <c>…StartAt</c> methods from the catch-up, the two predicates from the aggregate's own guards.
/// </para>
/// <para>
/// 🔒 <b>Why it lives here.</b> <c>Player</c> previously held a private <c>GameDayStart</c> of 05:00
/// UTC for its own boundary invariants, and `30` §11.4 forbids <c>Model</c> from referencing
/// <c>Rules</c> — so a copy of the arithmetic in <c>Rules/</c> would be the <b>second</b>
/// transcription of 05:00 UTC, which is the exact defect <c>EnergyTuning.MaxEnergyAt</c> was moved
/// into <c>Content/</c> to fix. <c>Primitives</c> sits beneath <b>both</b> <c>Model</c> and
/// <c>Rules</c>, names only <c>System</c> types and never reaches the <c>Core</c> root, so both
/// layering rows of <c>Core_internal_layering_holds</c> hold. <see cref="RejectionReasons"/> is the
/// precedent for a static helper here.
/// </para>
/// <para>
/// 🔒 <b>The precondition is stated rather than guarded.</b> Every instant handed in must be UTC
/// (a zero offset). <c>GameContext.RequireUtc</c> already refuses anything else on every path
/// including <c>with</c>, so a guard here would be a branch no input can reach — steering
/// <b>S1</b>'s own defect shape, and the reasoning <c>Player.Rehydrate</c> records for the energy
/// check it deliberately does not have.
/// </para>
/// </remarks>
internal static class GameCalendar
{
    /// <summary>
    /// 🔒 The UTC time of day every game day begins at: <b>05:00</b> (`30` §2.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a 📐 tunable. `30` §2.3 writes 05:00 UTC into the reset <em>rule</em> itself —
    /// <em>"quest expiry, the wheel's free spin, ad caps and dungeon entries all reset at 05:00 UTC
    /// whether or not anyone logs in"</em> — so it is the boundary the design is written against
    /// rather than a dial someone turns, and `21` §3.1's build check enumerates 📐 markers against
    /// schema keys, of which this has none.
    /// </para>
    /// <para>
    /// ⚠️ <b>But it is <em>not</em> true that no <c>tuning/</c> document mentions this instant, and
    /// saying so would be a claim a reader can falsify in one grep.</b> Four authored keys carry it
    /// today: <c>dungeons.json</c>'s <c>entries.refreshUtc</c>, <c>events.json</c>'s
    /// <c>calendar.startEndUtc</c>, <c>guilds.json</c>'s <c>quests.drawUtc</c> — all
    /// <c>"05:00"</c> — and <c>guilds.json</c>'s <c>boss.weekStartUtc</c>, <c>"Monday 05:00"</c>,
    /// which is <see cref="WeekStart"/> as well. Each of those is <em>its own system's</em> dial
    /// (`25`, `26` §8, `27` §3/§4) and none of them is the game calendar; nothing in `Core` reads
    /// any of them, and nothing can — <c>Primitives</c> may not name <c>Content</c>
    /// (<c>Core_internal_layering_holds</c>), so a calendar that read a tuning document could not
    /// live at the one layer both <c>Model</c> and <c>Rules</c> can see, which is the whole reason
    /// this type is here.
    /// </para>
    /// <para>
    /// 🔒 <b>The consequence, stated rather than discovered later.</b> Those four keys and this
    /// constant are two spellings of one instant with nothing checking that they agree — the same
    /// drift this type exists to remove from <c>Core</c>, one boundary further out. It is not
    /// M1-08's to close: the drift becomes reachable on the commit that first <em>reads</em> one of
    /// them, which is M4-09 (the guild/dungeon dailies) and M13-01 (the event windows), and closing
    /// it needs a ruling on which side is authoritative — a `21` §3.1 amendment making the reset
    /// hour a schema key the calendar is validated against, or a `30` §2.3 amendment saying the
    /// tuning keys merely restate the rule. Whoever rules, rules at that milestone's kickoff.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan DayStart = TimeSpan.FromHours(5);

    /// <summary>
    /// 🔒 The weekday every game week begins on: <b>Monday</b> (milestone assumption <b>A2</b>,
    /// derived from `27` §4).
    /// </summary>
    /// <remarks>
    /// ⚠️ `27` §4 also authors this as data — <c>guilds.json</c>'s <c>boss.weekStartUtc</c>, the
    /// literal <c>"Monday 05:00"</c>. See <see cref="DayStart"/>'s remarks for why that key is the
    /// guild boss cadence's dial rather than this constant's, and for who owns reconciling the two.
    /// </remarks>
    internal const DayOfWeek WeekStart = DayOfWeek.Monday;

    /// <summary>
    /// 🔒 The first game day the calendar can answer: <c>0001-01-01T05:00:00Z</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Reachable, not theoretical</b> (recorded assumption <b>A7</b>).
    /// <c>default(DateTimeOffset)</c> is <c>0001-01-01T00:00:00+00:00</c>, which passes
    /// <c>GameContext</c>'s zero-offset guard — a <c>VirtualClock</c> (M1-11) or a fixture that
    /// forgot to set <c>NowUtc</c> lands exactly there, and the naive
    /// <c>startOfDay.AddDays(-1)</c> would throw <see cref="ArgumentOutOfRangeException"/> out of
    /// <c>GameRules.Apply</c>, which `30` §2.1's <b>P3</b> forbids. The calendar floors here
    /// instead. <c>0001-01-01</c> is a Monday in .NET's proleptic Gregorian calendar, so the weekly
    /// step-back cannot underflow from the clamped value either.
    /// </remarks>
    internal static readonly DateTimeOffset FirstGameDay = new(1, 1, 1, 5, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The latest 05:00 UTC game-day boundary at or before <paramref name="nowUtc"/>, floored at
    /// <see cref="FirstGameDay"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>One step, not a loop.</b> The answer is <em>this</em> calendar day's 05:00 when the
    /// instant is at or past it and the previous day's otherwise — a single
    /// <see cref="DateTimeOffset.AddDays"/>, whatever the gap. M1-11's budget is 180 simulated days
    /// and this runs on every command, so a per-boundary walk would be 180 iterations answering
    /// what one subtraction answers. <c>AddDays(-1)</c> rather than arithmetic on the day-of-month
    /// because the month and year rollovers are the calendar's job, not this method's.
    /// </para>
    /// <para>
    /// ⚠️ The floor is checked <b>first</b>, which is also what keeps <c>AddDays(-1)</c> safe: the
    /// only instants whose previous game day would underflow <see cref="DateTimeOffset.MinValue"/>
    /// are the ones before <see cref="FirstGameDay"/>, and those never reach the step.
    /// </para>
    /// </remarks>
    /// <param name="nowUtc">A UTC instant — <c>GameContext.NowUtc</c>. See the type's remarks.</param>
    internal static DateTimeOffset GameDayStartAt(DateTimeOffset nowUtc)
    {
        if (nowUtc < FirstGameDay)
        {
            return FirstGameDay;
        }

        var todaysStart = nowUtc - nowUtc.TimeOfDay + DayStart;

        return nowUtc.TimeOfDay >= DayStart ? todaysStart : todaysStart.AddDays(-1);
    }

    /// <summary>
    /// The Monday 05:00 UTC game-week boundary the game day of <paramref name="nowUtc"/> belongs
    /// to, floored at <see cref="FirstGameDay"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Anchored to the game <em>day</em>, not to the instant.</b> A Monday at 04:59 UTC is
    /// still inside the previous game week, because the game day it belongs to is Sunday's. Stepping
    /// back from the raw instant would answer a Monday in the <em>future</em> for those five hours
    /// every week, and <c>Player.ResetWeeklyCounters</c> would then record a week the player has not
    /// reached.
    /// </para>
    /// <para>
    /// ⚠️ <b>Step back to the week's <see cref="WeekStart"/>, never seven days from the last
    /// reset.</b> The modulus below answers 0 for a Monday and 6 for a Sunday; a fixed seven-day
    /// step would land on whatever weekday the previous reset happened to fall on, which
    /// <c>Player.ResetWeeklyCounters</c> refuses outright on six days in seven — a `30` §2.1
    /// <b>P3</b> violation rather than a wrong number.
    /// </para>
    /// <para>
    /// The step cannot underflow: <see cref="GameDayStartAt"/> never answers before
    /// <see cref="FirstGameDay"/>, and <c>0001-01-01</c> is itself a <see cref="WeekStart"/>, so the
    /// floor's own offset is zero days.
    /// </para>
    /// </remarks>
    /// <param name="nowUtc">A UTC instant — <c>GameContext.NowUtc</c>. See the type's remarks.</param>
    internal static DateTimeOffset GameWeekStartAt(DateTimeOffset nowUtc)
    {
        var dayStart = GameDayStartAt(nowUtc);
        var daysIntoWeek = ((int)dayStart.DayOfWeek - (int)WeekStart + 7) % 7;

        return dayStart.AddDays(-daysIntoWeek);
    }

    /// <summary>Whether <paramref name="instant"/> is itself a 05:00 UTC game-day boundary.</summary>
    /// <param name="instant">A UTC instant. See the type's remarks.</param>
    internal static bool IsGameDayBoundary(DateTimeOffset instant) => instant.TimeOfDay == DayStart;

    /// <summary>Whether <paramref name="instant"/> is itself a Monday 05:00 UTC game-week boundary.</summary>
    /// <remarks>
    /// 🔒 <b>Both halves, and the time of day is the half that gets forgotten.</b> A predicate that
    /// asked only for <see cref="WeekStart"/> would call every instant of every Monday a week
    /// boundary — including 00:00, which is inside the <em>previous</em> game week — and
    /// <c>Player.ResetWeeklyCounters</c> refuses any weekly period that is not 05:00 UTC exactly.
    /// </remarks>
    /// <param name="instant">A UTC instant. See the type's remarks.</param>
    internal static bool IsGameWeekBoundary(DateTimeOffset instant) =>
        instant.DayOfWeek == WeekStart && IsGameDayBoundary(instant);
}
