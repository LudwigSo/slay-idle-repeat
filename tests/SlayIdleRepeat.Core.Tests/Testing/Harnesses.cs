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
/// Hermetic <see cref="InMemoryGame"/> fixtures — a shipped content set, a fixed seed, a fixed
/// start instant, and the two drives every multi-day assertion in this suite is written over.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The content set is <c>TuningDocuments.Shipped</c>, built in memory.</b> `30` §6's sketch
/// writes <c>ContentSnapshot.LoadFromDisk("game-data")</c> and the M1 kickoff ruled that a
/// documented erratum: loading JSON is I/O and belongs in an adapter (`14` §6), and
/// <c>SlayIdleRepeat.Core.Tests</c> is hermetic — no file, no parser, no adapter. A fixture that
/// reached for the repository would be the first crack in
/// <c>The_whole_game_is_playable_from_Core_alone</c>, which this milestone exists to wake up. The
/// real <c>game-data/</c> is reached by a composition root over
/// <c>{ Core, Application, Adapters.Content.LocalFile }</c>, and the smoke test that does it belongs
/// in <c>Application.Tests</c>.
/// </para>
/// <para>
/// 🔒 <b>Nothing here computes an expected value.</b> Every number an assertion compares against is
/// either read from the fixture content set through <see cref="Tuning"/> — the tuning the rules
/// themselves are using — or is a `10` §3 constant <c>ProgressionDocuments</c> already transcribes
/// and <c>Application.Tests</c> pins against the shipped file. A fixture that reimplemented
/// <c>EnergyMath</c> to say what it expected would be asserting the test against itself, which is
/// the failure mode `30` §6's harness is most exposed to.
/// </para>
/// </remarks>
internal static class Harnesses
{
    /// <summary>
    /// The root seed every fixture harness uses unless a test is about the seed.
    /// </summary>
    /// <remarks>
    /// ⚠️ Arbitrary and fixed, and <b>not</b> a claim about any draw. Nothing in M1 draws: `30`
    /// §2.3's two daily draws (the quest slate and the Daily shop block) are <c>GapRegister</c>
    /// entries owned by M4-09, and <c>BeginSession</c> takes the seed and reads nothing from it.
    /// What the seed buys today is the <em>seam</em> — see <c>InMemoryGameDeterminismTests</c>,
    /// which says exactly that and will go red on the commit that changes it.
    /// </remarks>
    internal const ulong Seed = 0xA11CE_0000_1111UL;

    /// <summary>
    /// 2026-08-12 05:00 UTC — a Wednesday, and therefore a game-<b>day</b> boundary that is not a
    /// game-<b>week</b> boundary (A2's week starts Monday).
    /// </summary>
    /// <remarks>
    /// The same instant <c>PlayerSnapshots.Wednesday</c> uses, checked against the calendar rather
    /// than assumed. Starting on a day boundary rather than mid-morning is deliberate: it makes
    /// "how many game days has this simulation crossed" equal to "how many whole days has the clock
    /// advanced", so a multi-day test states its own arithmetic instead of carrying an off-by-one
    /// nobody can see.
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
    /// 🔒 The multi-day drive every long assertion in this suite shares: for each game day, send
    /// <paramref name="commandsPerDay"/> <c>BEGIN_SESSION</c>s spread evenly across it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Several commands per day is the whole point and not padding.</b> M1-09 measured that
    /// only 3 of its 7 idempotence tests failed when the per-game-day guard was removed, because a
    /// test that sends one command per day cannot tell "grants once per day" from "grants on every
    /// command". Every drive here sends more than one, so the daily block's idempotence is under
    /// test on every multi-day assertion rather than only in the suite named for it.
    /// </para>
    /// <para>
    /// 🔒 <b>The step is <c>24h / (commandsPerDay + 1)</c> and the day ends with a silent advance to
    /// the boundary, which is arithmetic rather than fussiness.</b> With a step of
    /// <c>24h / commandsPerDay</c> the last command of each day lands <em>exactly on</em> the next
    /// 05:00 UTC boundary and therefore belongs to the following game day — so driving <c>D</c> days
    /// would touch <c>D + 1</c> of them, with the first and last partially filled. Every "one grant
    /// per game day" assertion in this suite would then be comparing against an off-by-one nobody
    /// can see in the call. As written, <see cref="Start"/> being a boundary makes the arithmetic
    /// exact: <paramref name="days"/> game days, <paramref name="commandsPerDay"/> commands strictly
    /// inside each, and the clock finishing on a boundary with no command on it.
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
    /// Exposed rather than inlined because the alternative is a second, hand-written cadence in the
    /// interleaving test — and two cadences that were meant to be the same is precisely how a
    /// determinism comparison ends up reporting a difference the code did not cause. (It did: the
    /// first draft of <c>Two_interleaved_harnesses_do_not_contaminate_each_other</c> stepped a flat
    /// six hours and failed against a drive that steps <c>24h / (n + 1)</c>.)
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
    /// Every <c>CurrencyChanged</c> in a harness's event list that carries the given `30` §7
    /// attribution token.
    /// </summary>
    /// <remarks>
    /// Ordinal comparison, because a reason token is a stable identifier rather than prose — and
    /// because Shouldly's string <c>ShouldContain</c> family defaults to case-INSENSITIVE, which is
    /// the sibling trap <c>WildcardMessageAssertions</c> documents; a helper that compared reasons
    /// loosely would make <c>energy_regen</c> and <c>ENERGY_REGEN</c> the same row.
    /// </remarks>
    internal static IReadOnlyList<CurrencyChanged> CurrencyRows(InMemoryGame game, string reason) =>
        game.Events
            .OfType<CurrencyChanged>()
            .Where(e => e.Reason.Equals(reason, StringComparison.Ordinal))
            .ToArray();

    /// <summary>The `30` §7 attribution token <c>GameRules.AdvanceTime</c> logs regeneration under (A5).</summary>
    /// <remarks>
    /// Transcribed rather than read from <c>GameRules</c>, whose constant is <c>private</c>. That is
    /// a second copy and it is the deliberate kind: the token is what `21` §8.3's
    /// <c>income_attribution.csv</c> groups by, so it is a published identifier, and a test that read
    /// the production constant would keep passing after the token was renamed under the dashboards'
    /// feet.
    /// </remarks>
    internal const string EnergyRegenReason = "energy_regen";

    /// <summary>The token `10` §3.1's daily free refill is logged under (M1-09).</summary>
    /// <inheritdoc cref="EnergyRegenReason"/>
    internal const string DailyRefillReason = "daily_free_refill";
}
