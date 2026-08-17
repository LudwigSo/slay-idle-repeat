using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Content;

namespace SlayIdleRepeat.Core.Tests;

// Named Core.Tests rather than Core.Tests.Testing so this namespace does not shadow
// SlayIdleRepeat.Core.Testing for every file under this directory.

/// <summary>Hermetic <see cref="InMemoryGame"/> fixtures: shipped content, a fixed seed and start
/// instant, and the two drives every multi-day assertion is written over.</summary>
internal static class Harnesses
{
    /// <summary>The root seed every fixture harness uses unless a test is about the seed.</summary>
    internal const ulong Seed = 0xA11CE_0000_1111UL;

    /// <summary>2026-08-12 05:00 UTC — a Wednesday, so a game-day boundary that is not also a
    /// game-week boundary, keeping the two arithmetics distinguishable.</summary>
    internal static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>The Energy numbers the rules are reading — never a second transcription.</summary>
    internal static EnergyTuning Tuning { get; } = EnergyTuning.Read(TuningDocuments.Shipped);

    /// <summary>The one <c>Handled</c> command in the registry, reused from
    /// <c>Handlers.BeginSessions</c> rather than rebuilt.</summary>
    internal static BeginSessionCommand BeginSession => Handlers.BeginSessions.Command;

    /// <summary>A harness over the shipped tuning, at <see cref="Start"/>, with <see cref="Seed"/>.</summary>
    internal static InMemoryGame New(
        DateTimeOffset? start = null,
        ulong? seed = null,
        ContentSnapshot? content = null) =>
        new(content ?? TuningDocuments.Shipped, seed ?? Seed, new VirtualClock(start ?? Start));

    /// <summary>A harness with one player already created, and that player's id.</summary>
    /// <param name="start">When the simulation starts. Defaults to <see cref="Start"/>.</param>
    /// <param name="seed">The root seed. Defaults to <see cref="Seed"/>.</param>
    /// <param name="inventory">
    /// The stock the player starts with, or <c>null</c> for the empty inventory a new player has.
    /// </param>
    /// <remarks>
    /// 🔴 <paramref name="inventory"/> is APPENDED, and every caller passes it by name. A parameter
    /// inserted ahead of an existing optional one merges textually clean and silently re-binds every
    /// positional argument after it.
    /// <para>
    /// The seam exists because a comparison whose subject is "how does the per-command cost move with
    /// the size of the stock" cannot build a three-hundred-item inventory one grant command at a time
    /// — it would be measuring the grants.
    /// </para>
    /// </remarks>
    internal static (InMemoryGame Game, PlayerId Player) WithPlayer(
        DateTimeOffset? start = null, ulong? seed = null, InventorySnapshot? inventory = null)
    {
        var game = New(start, seed);

        return (game, game.CreatePlayer(inventory: inventory));
    }

    /// <summary>The multi-day drive every long assertion shares: for each game day, send
    /// <paramref name="commandsPerDay"/> <c>BEGIN_SESSION</c>s spread evenly across it.</summary>
    internal static void Drive(InMemoryGame game, PlayerId player, int days, int commandsPerDay)
    {
        for (var day = 0; day < days; day++)
        {
            DriveDay(game, player, commandsPerDay);
        }
    }

    /// <summary>One game day of <see cref="Drive"/>, exposed so two simulations can be interleaved
    /// day by day while still running the identical cadence.</summary>
    internal static void DriveDay(InMemoryGame game, PlayerId player, int commandsPerDay)
    {
        // +1 so the last command lands strictly before the next boundary, not exactly on it.
        var step = TimeSpan.FromDays(1) / (commandsPerDay + 1);

        for (var command = 0; command < commandsPerDay; command++)
        {
            game.Clock.Advance(step);
            game.Send(player, BeginSession);
        }

        game.Clock.Advance(TimeSpan.FromDays(1) - (step * commandsPerDay));
    }

    /// <summary>Every <c>CurrencyChanged</c> in a harness's event list carrying the given reason.</summary>
    /// <remarks>Ordinal comparison: Shouldly's <c>ShouldContain</c> defaults to case-insensitive, which
    /// would conflate <c>energy_regen</c> and <c>ENERGY_REGEN</c>.</remarks>
    internal static IReadOnlyList<CurrencyChanged> CurrencyRows(InMemoryGame game, string reason) =>
        game.Events
            .OfType<CurrencyChanged>()
            .Where(e => e.Reason.Equals(reason, StringComparison.Ordinal))
            .ToArray();

    /// <summary>The reason token <c>GameRules.AdvanceTime</c> logs regeneration under.</summary>
    /// <remarks>Transcribed rather than read from <c>GameRules</c>' private constant, so a test keeps
    /// asserting the actual logged value rather than tracking a rename under it.</remarks>
    internal const string EnergyRegenReason = "energy_regen";

    /// <summary>The reason token the daily free refill is logged under.</summary>
    internal const string DailyRefillReason = "daily_free_refill";
}
