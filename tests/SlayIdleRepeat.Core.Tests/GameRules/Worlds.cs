using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;
using SlayIdleRepeat.Core.Tests.BalanceHarness;

namespace SlayIdleRepeat.Core.Tests;

// Namespace SlayIdleRepeat.Core.Tests, not ...Tests.GameRules, even though the files sit under
// GameRules/: a child namespace named `GameRules` shadows the type GameRules for everything inside
// SlayIdleRepeat.Core.Tests, so a test written here could not name the very class it is testing
// (CS0118).

/// <summary>
/// Hermetic <see cref="WorldSlice"/> and command fixtures for the <c>GameRules.Apply</c> suite.
/// Aggregates are built only through <c>Rehydrate</c> over the snapshot fixtures. The command
/// fixtures are deliberately not named after any real wire command.
/// </summary>
internal static class Worlds
{
    /// <summary>
    /// The instant every fixture applies at — one second <em>after</em> the snapshots' anchors, or
    /// "the timestamp advanced" would be untestable.
    /// </summary>
    internal static readonly DateTimeOffset NowUtc = new(2026, 8, 12, 9, 41, 8, TimeSpan.Zero);

    /// <summary>A context at <see cref="NowUtc"/> with the shipped tuning and no command seed.</summary>
    internal static GameContext Context { get; } = new(
        NowUtc,
        CommandSeed: null,
        // START_RUN scores Max HP off the hero's build, so the suite needs the combat caps, the
        // gear catalogue and the par table filled in.
        ShippedHarness.WithShippedGaps(TuningDocuments.Shipped),
        TestSupport.GameContexts.WithoutPlus,
        TestSupport.GameContexts.NoKillSwitchThrown);

    /// <summary>
    /// The context a drawing command gets: <see cref="Context"/> plus a server-issued
    /// <c>CommandSeed</c>. The seed is required rather than defaulted, so the call site shows it.
    /// </summary>
    internal static GameContext Drawing(ulong commandSeed, DateTimeOffset? nowUtc = null) =>
        Context with { CommandSeed = commandSeed, NowUtc = nowUtc ?? NowUtc };

    /// <summary>The same instant, one game day later.</summary>
    internal static DateTimeOffset NextDay(DateTimeOffset from) => from.AddDays(1);

    /// <summary>A player rehydrated from <c>PlayerSnapshots.Valid</c>.</summary>
    internal static Player NewPlayer() => Rehydrated(PlayerSnapshots.Valid);

    /// <summary>A player rehydrated from a modified row.</summary>
    internal static Player Rehydrated(PlayerSnapshot snapshot)
    {
        var player = Player.Rehydrate(snapshot, TuningDocuments.Shipped);

        return player.IsSuccess
            ? player.Value
            : throw new InvalidOperationException(
                "The fixture PlayerSnapshot does not rehydrate: " + player.Error);
    }

    /// <summary>A run rehydrated from a row, defaulting to <c>RunSnapshots.Valid</c>.</summary>
    internal static RunAggregate NewRun(RunSnapshot? snapshot = null)
    {
        var run = RunAggregate.Rehydrate(snapshot ?? RunSnapshots.Valid);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException(
                "The fixture RunSnapshot does not rehydrate: " + run.Error);
    }

    /// <summary>A slice with a player and no run — the shape every meta command sees.</summary>
    internal static WorldSlice OutsideARun() => new(NewPlayer(), null);

    /// <summary>A slice with a player and a run.</summary>
    internal static WorldSlice InARun(RunSnapshot? run = null) => new(NewPlayer(), NewRun(run));

    /// <summary>A command that acts inside a run. Its wire name cannot be mistaken for a real one.</summary>
    internal sealed record RunFixtureCommand : GameCommand;

    /// <summary>A command that acts outside a run.</summary>
    internal sealed record MetaFixtureCommand : GameCommand;

    /// <summary>A second command type, for the tests that need two rows in one table.</summary>
    internal sealed record OtherFixtureCommand : GameCommand;

    /// <summary>The wire name <see cref="RunFixtureCommand"/> is registered under.</summary>
    internal const string RunWireName = "FIXTURE_RUN_COMMAND";

    /// <summary>The wire name <see cref="MetaFixtureCommand"/> is registered under.</summary>
    internal const string MetaWireName = "FIXTURE_META_COMMAND";

    /// <summary>The wire name <see cref="OtherFixtureCommand"/> is registered under.</summary>
    internal const string OtherWireName = "FIXTURE_OTHER_COMMAND";

    /// <summary>A table with one run command, bound to <paramref name="handler"/>.</summary>
    internal static CommandDispatch RunTable(CommandHandler<RunFixtureCommand> handler) =>
        new CommandDispatch().Handled(RunWireName, CommandKind.Run, handler);

    /// <summary>A table with one meta command, bound to <paramref name="handler"/>.</summary>
    internal static CommandDispatch MetaTable(CommandHandler<MetaFixtureCommand> handler) =>
        new CommandDispatch().Handled(MetaWireName, CommandKind.Meta, handler);
}
