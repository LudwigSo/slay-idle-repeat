using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests;

// 🔒 Namespace SlayIdleRepeat.Core.Tests, not ...Tests.GameRules, and the files still sit under
// GameRules/. The same measurement M1-04 recorded for Model/Player/: a child namespace named
// `GameRules` shadows the type GameRules for everything inside SlayIdleRepeat.Core.Tests, so a test
// written here could not name the very class it is testing (CS0118).

/// <summary>
/// Hermetic <see cref="WorldSlice"/> and command fixtures for the <c>GameRules.Apply</c> suite.
/// </summary>
/// <remarks>
/// <para>
/// Aggregates are built the only way `30` §11.3 allows — through <c>Rehydrate</c> over the snapshot
/// fixtures M1-04 and M1-05 authored — so nothing here invents a starting state. The content
/// snapshot is <c>TuningDocuments.Shipped</c>: <c>Player.Rehydrate</c> validates the Legend Level
/// against `07` §1.1's authored range, and M1-09's <c>BEGIN_SESSION</c> is the first command to read
/// a <em>second</em> tuning document (`19` G's calendar cycle, in <c>tuning/currencies.json</c>).
/// </para>
/// <para>
/// 🔒 <b><see cref="Context"/> carries no <c>CommandSeed</c>, and that is correct for every fixture
/// command in this file</b> — `30` §3 makes the seed meta-only and `14` §2.3 marks only nine rows ⚄,
/// none of which these fixtures impersonate. A command that <em>draws</em> takes
/// <see cref="Drawing"/> instead, which is the one door that pairs a seed with a command here
/// (steering <b>S2</b>: "a run command was handed a seed" and "a meta draw was handed none" are
/// opposite defects with opposite fixes, and sharing one fixture would let a test pass on the wrong
/// one).
/// </para>
/// <para>
/// ⚠️ <b>The command fixtures are deliberately not any of `14` §2.3's 49 rows.</b> The vocabulary is
/// M1-02's; a fixture that borrowed a real name would read as a claim about it (steering S6). They
/// are named for the <em>shape</em> each one drives — a handler that draws, a handler that
/// hand-writes a counter — and their wire names are spelled so no reader mistakes them for the
/// registry.
/// </para>
/// </remarks>
internal static class Worlds
{
    /// <summary>The instant every fixture applies at, one second after the snapshots' anchors.</summary>
    /// <remarks>
    /// Later than <c>PlayerSnapshots.Midmorning</c> and <c>RunSnapshots.Midmorning</c> on purpose: a
    /// fixture instant equal to the anchor would make "the timestamp advanced" untestable.
    /// <para>
    /// 🔒 <b>M1-12 corrected the other half of this sentence</b>, which read <em>"and one earlier
    /// would make every accepted command throw"</em>. That was true and it was carried-forward item
    /// 20 — a `30` §2.1 <b>P3</b> violation — recorded here as a property of the fixture rather than
    /// as the defect it was. <c>GameRules.MarkApplied</c> now floors the instant it hands the
    /// aggregates, so an earlier one is accepted and leaves the anchor where it was; see
    /// <c>GameRulesBackwardsClockTests</c>. This fixture is still later than both anchors, because
    /// what it is for is testing that the timestamp <em>moves</em>.
    /// </para>
    /// </remarks>
    internal static readonly DateTimeOffset NowUtc = new(2026, 8, 12, 9, 41, 8, TimeSpan.Zero);

    /// <summary>A context at <see cref="NowUtc"/> with the shipped tuning and no command seed.</summary>
    internal static GameContext Context { get; } = new(
        NowUtc,
        CommandSeed: null,
        TuningDocuments.Shipped,
        TestSupport.GameContexts.WithoutPlus,
        TestSupport.GameContexts.NoKillSwitchThrown);

    /// <summary>
    /// 🔒 The context a ⚄ command gets: <see cref="Context"/> plus a server-issued
    /// <c>CommandSeed</c>.
    /// </summary>
    /// <param name="commandSeed">
    /// The seed. ⚠️ Required rather than defaulted — a default would make "which seed did this test
    /// use" invisible at the call site, and every determinism assertion in this suite is a claim
    /// about <em>that</em> value.
    /// </param>
    /// <param name="nowUtc">When the command is applied. Defaults to <see cref="NowUtc"/>.</param>
    internal static GameContext Drawing(ulong commandSeed, DateTimeOffset? nowUtc = null) =>
        Context with { CommandSeed = commandSeed, NowUtc = nowUtc ?? NowUtc };

    /// <summary>The same instant, on the next game day (`30` §2.3's 05:00 UTC boundary).</summary>
    /// <remarks>
    /// Written as a day's addition to <see cref="NowUtc"/> rather than as a second literal: the two
    /// have to be one game day apart for the idempotence suite to mean anything, and two literals is
    /// how that stops being true without a test noticing.
    /// </remarks>
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

    /// <summary>
    /// A command that acts inside a run. Its wire name is spelled so it cannot be mistaken for one
    /// of `14` §2.3's rows.
    /// </summary>
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
