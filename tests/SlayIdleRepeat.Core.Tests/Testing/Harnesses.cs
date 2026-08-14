using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Content;

namespace SlayIdleRepeat.Core.Tests;

// 🔒 Namespace SlayIdleRepeat.Core.Tests, not ...Tests.Testing, and the files still sit under
// Testing/. The same measurement Worlds.cs and PlayerSnapshots.cs record for their directories: a
// child namespace whose name collides with something the tests have to NAME makes it unnameable,
// and `SlayIdleRepeat.Core.Tests.Testing` would shadow `SlayIdleRepeat.Core.Testing` for every file
// underneath it. The directory is the file layout; the namespace is the layer.

/// <summary>
/// Hermetic <see cref="InMemoryGame"/> fixtures — a shipped content set, a fixed seed, a fixed start
/// instant, and the two drives every multi-day assertion in this suite is written over.
/// </summary>
/// <remarks>
/// 🔒 The content set is built in memory: `30` §6's <c>LoadFromDisk</c> sketch is a documented
/// erratum, since loading JSON is I/O and <c>Core.Tests</c> is hermetic.
/// <para>
/// 🔒 Nothing here computes an expected value. Every number an assertion compares against is read
/// from the fixture content set or is a `10` §3 constant <c>ProgressionDocuments</c> transcribes — a
/// fixture that reimplemented <c>EnergyMath</c> would assert the test against itself.
/// </para>
/// </remarks>
internal static class Harnesses
{
    /// <summary>The root seed every fixture harness uses unless a test is about the seed.</summary>
    /// <remarks>
    /// ⚠️ Arbitrary and fixed, and <b>not</b> a claim about any draw — nothing in M1 draws. What it
    /// buys today is the <em>seam</em>; see <c>InMemoryGameDeterminismTests</c>.
    /// </remarks>
    internal const ulong Seed = 0xA11CE_0000_1111UL;

    /// <summary>
    /// 2026-08-12 05:00 UTC — a Wednesday, so a game-<b>day</b> boundary that is not a game-<b>week</b>
    /// boundary.
    /// </summary>
    /// <remarks>
    /// Starting on a boundary makes "game days crossed" equal "whole days advanced", so a multi-day
    /// test states its own arithmetic instead of carrying an invisible off-by-one.
    /// </remarks>
    internal static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>The Energy numbers the rules are reading — never a second transcription.</summary>
    internal static EnergyTuning Tuning { get; } = EnergyTuning.Read(TuningDocuments.Shipped);

    /// <summary>`14` §2.3's <c>BEGIN_SESSION</c>, the one <c>Handled</c> row in the registry.</summary>
    /// <remarks>
    /// Reused from <c>Handlers.BeginSessions</c> rather than rebuilt, so the "neither field is read"
    /// ruling recorded there has one statement rather than two.
    /// </remarks>
    internal static BeginSessionCommand BeginSession => Handlers.BeginSessions.Command;

    /// <summary>A harness over the shipped tuning, at <see cref="Start"/>, with <see cref="Seed"/>.</summary>
    internal static InMemoryGame New(
        DateTimeOffset? start = null,
        ulong? seed = null,
        ContentSnapshot? content = null) =>
        new(content ?? TuningDocuments.Shipped, seed ?? Seed, new VirtualClock(start ?? Start));

    /// <summary>A harness with one player already created, and that player's id.</summary>
    internal static (InMemoryGame Game, PlayerId Player) WithPlayer(
        DateTimeOffset? start = null, ulong? seed = null)
    {
        var game = New(start, seed);

        return (game, game.CreatePlayer());
    }

    /// <summary>
    /// 🔒 The multi-day drive every long assertion shares: for each game day, send
    /// <paramref name="commandsPerDay"/> <c>BEGIN_SESSION</c>s spread evenly across it.
    /// </summary>
    /// <remarks>
    /// 🔒 Several commands per day is the point: a test sending one per day cannot tell "grants once
    /// per day" from "grants on every command", so the daily block's idempotence is under test on
    /// every multi-day assertion.
    /// <para>
    /// 🔒 The step is <c>24h / (commandsPerDay + 1)</c>: at <c>24h / commandsPerDay</c> the last
    /// command lands <em>exactly on</em> the next boundary and belongs to the following game day, so
    /// driving <c>D</c> days would touch <c>D + 1</c> of them.
    /// </para>
    /// </remarks>
    internal static void Drive(InMemoryGame game, PlayerId player, int days, int commandsPerDay)
    {
        for (var day = 0; day < days; day++)
        {
            DriveDay(game, player, commandsPerDay);
        }
    }

    /// <summary>
    /// One game day of <see cref="Drive"/>, so two simulations can be <b>interleaved</b> day by day
    /// and still each run the identical drive.
    /// </summary>
    /// <remarks>
    /// Exposed rather than inlined because the alternative is a second hand-written cadence — and two
    /// cadences meant to be the same is how a determinism comparison reports a difference the code did
    /// not cause.
    /// </remarks>
    internal static void DriveDay(InMemoryGame game, PlayerId player, int commandsPerDay)
    {
        var step = TimeSpan.FromDays(1) / (commandsPerDay + 1);

        for (var command = 0; command < commandsPerDay; command++)
        {
            game.Clock.Advance(step);
            game.Send(player, BeginSession);
        }

        game.Clock.Advance(TimeSpan.FromDays(1) - (step * commandsPerDay));
    }

    /// <summary>
    /// Every <c>CurrencyChanged</c> in a harness's event list carrying the given `30` §7 token.
    /// </summary>
    /// <remarks>
    /// Ordinal, because Shouldly's string <c>ShouldContain</c> family defaults to case-<b>insensitive</b>
    /// — a loose comparison would make <c>energy_regen</c> and <c>ENERGY_REGEN</c> the same row.
    /// </remarks>
    internal static IReadOnlyList<CurrencyChanged> CurrencyRows(InMemoryGame game, string reason) =>
        game.Events
            .OfType<CurrencyChanged>()
            .Where(e => e.Reason.Equals(reason, StringComparison.Ordinal))
            .ToArray();

    /// <summary>The `30` §7 token <c>GameRules.AdvanceTime</c> logs regeneration under (A5).</summary>
    /// <remarks>
    /// Transcribed rather than read from <c>GameRules</c>' private constant, deliberately: the token is
    /// what `21` §8.3's <c>income_attribution.csv</c> groups by, so a test reading the production
    /// constant would keep passing after a rename under the dashboards' feet.
    /// </remarks>
    internal const string EnergyRegenReason = "energy_regen";

    /// <summary>The token `10` §3.1's daily free refill is logged under (M1-09).</summary>
    /// <inheritdoc cref="EnergyRegenReason"/>
    internal const string DailyRefillReason = "daily_free_refill";
}
