namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 `30` §2.3's game calendar — the 05:00 UTC game day and the Monday 05:00 UTC game week
/// (milestone assumption <b>A2</b>, derived from `27` §4), computed from an instant.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>STUB. M1-08 Phase 1 declares the surface; Phase 3 writes the arithmetic.</b> Every member
/// below throws <see cref="NotImplementedException"/> on purpose: the failing suite this task
/// starts with has to <em>compile</em> against the shape it is asserting about, and a body that
/// guessed at the answer would make the tests pass before the rule existed.
/// </para>
/// <para>
/// 🔒 <b>Why it lives here.</b> <c>Player</c> already holds a private <c>GameDayStart</c> of 05:00
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
    /// Not a 📐 tunable. `30` §2.3 writes 05:00 UTC into the reset rule itself — <em>"quest expiry,
    /// the wheel's free spin, ad caps and dungeon entries all reset at 05:00 UTC whether or not
    /// anyone logs in"</em> — and no <c>tuning/</c> document authors it as a dial.
    /// </remarks>
    internal static readonly TimeSpan DayStart = TimeSpan.FromHours(5);

    /// <summary>
    /// 🔒 The weekday every game week begins on: <b>Monday</b> (milestone assumption <b>A2</b>,
    /// derived from `27` §4).
    /// </summary>
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
    /// <param name="nowUtc">A UTC instant — <c>GameContext.NowUtc</c>. See the type's remarks.</param>
    internal static DateTimeOffset GameDayStartAt(DateTimeOffset nowUtc) =>
        throw new NotImplementedException("M1-08 Phase 3.");

    /// <summary>
    /// The Monday 05:00 UTC game-week boundary the game day of <paramref name="nowUtc"/> belongs
    /// to, floored at <see cref="FirstGameDay"/>.
    /// </summary>
    /// <param name="nowUtc">A UTC instant — <c>GameContext.NowUtc</c>. See the type's remarks.</param>
    internal static DateTimeOffset GameWeekStartAt(DateTimeOffset nowUtc) =>
        throw new NotImplementedException("M1-08 Phase 3.");

    /// <summary>Whether <paramref name="instant"/> is itself a 05:00 UTC game-day boundary.</summary>
    /// <param name="instant">A UTC instant. See the type's remarks.</param>
    internal static bool IsGameDayBoundary(DateTimeOffset instant) =>
        throw new NotImplementedException("M1-08 Phase 3.");

    /// <summary>Whether <paramref name="instant"/> is itself a Monday 05:00 UTC game-week boundary.</summary>
    /// <param name="instant">A UTC instant. See the type's remarks.</param>
    internal static bool IsGameWeekBoundary(DateTimeOffset instant) =>
        throw new NotImplementedException("M1-08 Phase 3.");
}
