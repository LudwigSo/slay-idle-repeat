using System.Collections.ObjectModel;

using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Application.Tests.Hosting;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Application.Tests.Services;

/// <summary>
/// Rows, hosts and screens for the Home hub's cases: a real starting player, tailored one field at
/// a time, served through the real in-process host over the in-memory cache.
/// </summary>
/// <remarks>
/// The row is built by the domain and then edited rather than assembled from nothing, so every
/// fixture is a state the rules can actually produce — and every edit goes back through
/// <c>Player.Rehydrate</c>, which refuses a row the aggregate's invariants do not allow.
/// </remarks>
internal static class HomeWorlds
{
    /// <summary>The instant every case reads at, unless it is about the clock.</summary>
    internal static readonly DateTimeOffset Now = Worlds.Start;

    /// <summary>Where the regeneration interval is authored.</summary>
    private const string RegenMinutesReference = "tuning/progression.json#/energy/regenMinutesPerPoint";

    /// <summary>Where a run's price is authored.</summary>
    private const string RunCostReference = "tuning/progression.json#/energy/runCost";

    /// <summary>The chapter the shipped ladder opens with — the one a fresh profile may enter.</summary>
    internal const int UnlockedChapter = 1;

    /// <summary>The next chapter up, which nothing has cleared the way to.</summary>
    internal const int LockedChapter = 2;

    /// <summary>How long one Energy point takes, read from the shipped document rather than transcribed.</summary>
    internal static TimeSpan RegenInterval { get; } =
        TimeSpan.FromMinutes((double)Worlds.Content.ReadNumber(RegenMinutesReference));

    /// <summary>What a run costs, read from the shipped document rather than transcribed.</summary>
    internal static int RunCost { get; } = Worlds.Content.ReadInt32(RunCostReference);

    /// <summary>
    /// What the main bar holds at the starting Legend Level: the authored base, since the per-level
    /// increment counts levels gained and a starting player has gained none.
    /// </summary>
    internal static int MaxAtStartingLevel { get; } =
        Worlds.Content.ReadInt32("tuning/progression.json#/energy/baseMax");

    /// <summary>A real starting player's row, with the named fields replaced.</summary>
    internal static PlayerSnapshot Row(
        string? displayName = null,
        long? crowns = null,
        EnergyBanks? energy = null,
        DateTimeOffset? energyAnchorUtc = null)
    {
        var game = Worlds.Game();
        var starting = game.State(game.CreatePlayer()).Player.ToSnapshot();

        return starting with
        {
            DisplayName = displayName ?? starting.DisplayName,
            Wallet = crowns is { } balance ? WalletWith(starting.Wallet, balance) : starting.Wallet,
            // 🔴 A run is charged for, and a created player's banks are both zero — so the default
            // row holds exactly one run's price. Without it every case that starts a run is refused
            // for Energy, and the two that are ABOUT Energy override this anyway.
            Energy = energy ?? new EnergyBanks(RunCost, 0),
            EnergyAnchorUtc = energyAnchorUtc ?? starting.EnergyAnchorUtc,
        };
    }

    /// <summary>The real in-process host, serving one row out of the in-memory cache.</summary>
    internal static InProcessGameHost HostServing(PlayerSnapshot row, IClockPort clock) =>
        Hosts.Over(Worlds.CacheHolding(SliceOf(row)), clock);

    /// <summary>A clock stopped at one instant.</summary>
    internal static AdjustableClock ClockAt(DateTimeOffset instant)
    {
        var clock = new AdjustableClock();

        clock.Set(instant);

        return clock;
    }

    /// <summary>A screen over the real host, one row, one instant and one power reading.</summary>
    internal static HomeScreen Screen(
        PlayerSnapshot row, DateTimeOffset? at = null, IHeroPowerSource? power = null) =>
        new(
            HostServing(row, ClockAt(at ?? Now)),
            ClockAt(at ?? Now),
            Worlds.Content,
            power ?? StubHeroPower.Computing(DefaultPowerIndex),
            row.Id);

    /// <summary>A screen over a host that answers exactly what a case tells it to.</summary>
    internal static HomeScreen ScreenOver(
        IGameHost host, PlayerSnapshot row, IHeroPowerSource? power = null) =>
        new(
            host,
            ClockAt(Now),
            Worlds.Content,
            power ?? StubHeroPower.Computing(DefaultPowerIndex),
            row.Id);

    /// <summary>An arbitrary but fixed power reading, so a case not about power says nothing about it.</summary>
    internal const double DefaultPowerIndex = 8_450d;

    private static WorldSlice SliceOf(PlayerSnapshot row)
    {
        var player = PlayerAggregate.Rehydrate(row, Worlds.Content);

        return player.IsFailure
            ? throw new InvalidOperationException(
                "the Home fixture row does not rehydrate: " + player.Error + " Fix the fixture; a " +
                "row the aggregate refuses is not a state the screen can ever be shown.")
            : new WorldSlice(player.Value, null);
    }

    private static IReadOnlyDictionary<CurrencyId, long> WalletWith(
        IReadOnlyDictionary<CurrencyId, long> wallet, long crowns)
    {
        var copy = wallet.ToDictionary(entry => entry.Key, entry => entry.Value);

        copy[CurrencyId.CROWNS] = crowns;

        return new ReadOnlyDictionary<CurrencyId, long>(copy);
    }
}
