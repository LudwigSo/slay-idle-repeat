using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// The shared fixture for the BEGIN_SESSION suite: a player, a command, and a <see cref="Send"/>
/// that drives the production dispatch table.
/// </summary>
/// <remarks>
/// Everything goes through GameRules.Apply, never BeginSession.Handle directly, since the handler's
/// idempotence is only meaningful in composition with catch-up. State is carried forward between
/// commands rather than re-sent against the original slice, so repeats are genuine repeats.
/// </remarks>
internal static class BeginSessions
{
    /// <summary>The seed every fixture command carries unless it says otherwise.</summary>
    internal const ulong Seed = 0x5EED_0000_0000_0001UL;

    /// <summary>The command payload. Neither field is read by the handler.</summary>
    internal static BeginSessionCommand Command { get; } =
        new("client-version-nobody-reads", "content-hash-nobody-parses");

    /// <summary>The game day the fixtures sit in.</summary>
    internal static DateTimeOffset Today => PlayerSnapshots.Wednesday;

    /// <summary>Mid-morning on <see cref="Today"/>.</summary>
    internal static DateTimeOffset Morning => Worlds.NowUtc;

    /// <summary>A slice holding one player and no run, built from a row this fixture can shape.</summary>
    /// <param name="energy">The two banks. Defaults to empty, which is where the refill is visible.</param>
    /// <param name="legendLevel">The Legend Level Max Energy is derived from. Defaults to 1.</param>
    /// <param name="loginCalendarDay">The open calendar day. Defaults to day 1.</param>
    /// <param name="loginCalendarDayClaimed">Whether it has been claimed. Defaults to <c>false</c>.</param>
    /// <param name="dailyCounters">The day's counters. Defaults to none.</param>
    /// <param name="run">A run to put in the slice, or <c>null</c> for the usual meta shape.</param>
    /// <remarks>
    /// The anchors are pinned to <see cref="Morning"/> so no accrual happens unless a test asks for it.
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

    /// <summary>Sends BEGIN_SESSION through the production dispatch table and handler.</summary>
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
    /// <remarks>Read from the fixture content set rather than restated, so tests never drift from it.</remarks>
    internal static EnergyTuning Tuning { get; } = EnergyTuning.Read(TuningDocuments.Shipped);
}
