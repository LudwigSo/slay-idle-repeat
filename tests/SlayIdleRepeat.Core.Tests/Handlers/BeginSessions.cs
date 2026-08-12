using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// The shared fixture for the <c>BEGIN_SESSION</c> suite: a player, a command, and a
/// <see cref="Send"/> that drives the <b>production</b> dispatch table.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Everything here goes through <c>GameRules.Apply</c>, never through
/// <c>BeginSession.Handle</c> directly</b>, and that is the whole reason this file exists. The
/// handler's idempotence is only meaningful <em>in composition with</em> `30` §2.3's catch-up: the
/// counter it keys on is one <c>GameRules.AdvanceTime</c> clears at the day boundary and preserves
/// within the day, so a test that called the handler directly would be asserting the handler against
/// a slice no <c>Apply</c> would ever hand it — and would pass just as happily if the two disagreed.
/// </para>
/// <para>
/// 🔒 <b>State is carried forward between commands, exactly as a real session does.</b> `30` §2.1's
/// <b>P4</b> makes <c>Apply</c> return a <em>new</em> slice, so <see cref="Send"/> takes one and
/// answers the next; a test that re-sent against the original slice would be testing two first
/// commands rather than a first and a second, which is precisely the shape that cannot tell "grants
/// once per day" from "grants on every command".
/// </para>
/// </remarks>
internal static class BeginSessions
{
    /// <summary>
    /// The seed every fixture command carries unless it says otherwise. Arbitrary and fixed.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is <b>not</b> a claim about any draw. `14` §2.3 marks <c>BEGIN_SESSION</c> ⚄, so a
    /// context without a seed is a defect (<c>HandlerInput.MetaDraws</c>) — this is what makes the
    /// fixture legal, and the determinism claims live in <c>BeginSessionDrawSeamTests</c> where the
    /// seed is chosen per test and named at the call site.
    /// </remarks>
    internal const ulong Seed = 0x5EED_0000_0000_0001UL;

    /// <summary>
    /// 🔒 `14` §2.3's payload, with values that are deliberately <b>not</b> plausible.
    /// </summary>
    /// <remarks>
    /// Neither field is read by the handler — see <c>BeginSession.Handle</c>'s remarks on why
    /// <c>contentHash</c> cannot be validated inside the domain (`14` §16.2's
    /// <c>CONTENT_VERSION_MISMATCH</c> is transport tier and <c>Apply</c> may not return one). The
    /// values say so out loud: a test that started reading them would have to change this fixture,
    /// which is the moment to re-read that ruling.
    /// </remarks>
    internal static BeginSessionCommand Command { get; } =
        new("client-version-nobody-reads", "content-hash-nobody-parses");

    /// <summary>The game day <c>PlayerSnapshots</c>' fixtures sit in — Wednesday 05:00 UTC.</summary>
    internal static DateTimeOffset Today => PlayerSnapshots.Wednesday;

    /// <summary>Mid-morning on <see cref="Today"/>, which is where <c>Worlds.NowUtc</c> sits.</summary>
    internal static DateTimeOffset Morning => Worlds.NowUtc;

    /// <summary>
    /// A slice holding one player and no run, built from a row this fixture can shape.
    /// </summary>
    /// <param name="energy">The two banks. Defaults to empty, which is where the refill is visible.</param>
    /// <param name="legendLevel">The Legend Level Max Energy is derived from. Defaults to 1.</param>
    /// <param name="loginCalendarDay">`19` G's open day. Defaults to day 1.</param>
    /// <param name="loginCalendarDayClaimed">Whether it has been claimed. Defaults to <c>false</c>.</param>
    /// <param name="dailyCounters">The day's counters. Defaults to none.</param>
    /// <param name="run">A run to put in the slice, or <c>null</c> for the usual meta shape.</param>
    /// <remarks>
    /// ⚠️ The energy anchor and <c>lastAppliedAtUtc</c> are pinned to <see cref="Morning"/> rather
    /// than left at the fixture's default, so no accrual happens unless a test asks for one: an
    /// <c>energy_regen</c> row arriving beside the refill would make "the refill's event reached the
    /// result" true of a list this handler contributed nothing to.
    /// </remarks>
    internal static WorldSlice Slice(
        EnergyBanks? energy = null,
        int? legendLevel = null,
        int? loginCalendarDay = null,
        bool? loginCalendarDayClaimed = null,
        IReadOnlyDictionary<string, long>? dailyCounters = null,
        Run? run = null) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                legendLevel: legendLevel,
                energy: energy ?? new EnergyBanks(0, 0),
                energyAnchorUtc: Morning,
                lastAppliedAtUtc: Morning,
                dailyPeriodStartUtc: Today,
                dailyCounters: dailyCounters,
                loginCalendarDay: loginCalendarDay,
                loginCalendarDayClaimed: loginCalendarDayClaimed)),
            run);

    /// <summary>
    /// 🔒 Sends <c>BEGIN_SESSION</c> through <c>GameRules.Apply</c> — the production table, the
    /// production dispatch row, the production handler.
    /// </summary>
    /// <param name="state">The slice to apply against. Pass a previous result's <c>NewState</c>.</param>
    /// <param name="atUtc">When. Defaults to <see cref="Morning"/>.</param>
    /// <param name="commandSeed">The day's draw seed. Defaults to <see cref="Seed"/>.</param>
    internal static CommandResult Send(
        WorldSlice state, DateTimeOffset? atUtc = null, ulong? commandSeed = null) =>
        GameRules.Apply(
            state,
            Command,
            Worlds.Drawing(commandSeed ?? Seed, atUtc ?? Morning));

    /// <summary>The Energy tuning every assertion about a refill amount is derived from.</summary>
    /// <remarks>
    /// Read from the fixture content set rather than restated, so a test never asserts against a
    /// number the rules are not using — the defect <c>EnergyTuning</c>'s own remarks exist to stop.
    /// </remarks>
    internal static EnergyTuning Tuning { get; } = EnergyTuning.Read(TuningDocuments.Shipped);
}
