using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;
using SlayIdleRepeat.Core.Tests.Rules.Combat;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <see cref="WorldSlice"/> and <see cref="GameContext"/> fixtures for the tile-resolution suite: a
/// run standing on a pending tile, over a content set that carries the in-run income blocks, curses
/// and a purpose-built card list.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <c>Worlds</c>: <c>Worlds.Context</c> carries the login-calendar/minigame tuning
/// set, which holds none of the documents these handlers read.
/// </para>
/// <para>
/// The hero is GEARED (with <c>RunBattleWorlds</c>' own loadout, so this suite and M7-06b's compose
/// the same fight): <c>CONFIRM_BATTLE_RESULT</c> recomputes the fight, so a bare hero would turn
/// every payout assertion into a test that a loss pays nothing. <c>geared: false</c> is how a case
/// asks for a LOSS — <c>Won: false</c> is only the client's claim — and a losing case needs an
/// <c>Elite</c> or <c>Boss</c>, since the hero beats a chapter-1 ordinary enemy bare-handed.
/// "Geared enough to win" can drift silently under an M6 retune;
/// <c>ConfirmBattleResultTests.The_fixture_hero_actually_wins_the_fight_it_is_sent_into</c> is the
/// probe that catches it.
/// </para>
/// </remarks>
internal static class TileWorlds
{
    /// <summary>The instant every fixture applies at — <c>Worlds.NowUtc</c>, so the two agree.</summary>
    internal static readonly DateTimeOffset NowUtc = Worlds.NowUtc;

    /// <summary>The run seed every fixture uses unless a test is about a different draw.</summary>
    /// <remarks>
    /// <c>RunSnapshots.Seed</c>, so a determinism assertion here and one in the snapshot suite are
    /// talking about the same stream.
    /// </remarks>
    internal const ulong Seed = RunSnapshots.Seed;

    /// <summary>A context at <see cref="NowUtc"/> over the shipped in-run income content.</summary>
    /// <remarks>
    /// The income documents are laid OVER the full shipped set — which closing a battle needs, for
    /// the combat caps and enemy ladder — so every override this suite depends on still wins.
    /// </remarks>
    internal static GameContext Context { get; } = ContextOver(ShippedHarness.WithShippedGaps(InRunIncomeDocuments.Shipped));

    /// <summary>A context over a content set with individual leaves replaced.</summary>
    internal static GameContext ContextOver(ContentSnapshot content) => new(
        NowUtc,
        CommandSeed: null,
        content,
        TestSupport.GameContexts.WithoutPlus,
        TestSupport.GameContexts.NoKillSwitchThrown);

    /// <summary>
    /// A slice whose run is standing on an unresolved tile of <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">The tile kind the run has landed on.</param>
    /// <param name="chapterId">The chapter. Chapter 1 unless a scaling test needs otherwise.</param>
    /// <param name="gold">The run's starting Gold, for the cost and affordability cases.</param>
    /// <param name="currentHp">The hero's hit points, for the heal and HP-cost cases.</param>
    /// <param name="eventCardId">The card a pending event tile has already drawn, if any.</param>
    /// <param name="runSeed">The committed run seed, for the determinism cases.</param>
    internal static WorldSlice OnTile(
        TileKind kind,
        int chapterId = 1,
        long gold = 0L,
        int currentHp = 100,
        string? eventCardId = null,
        ulong runSeed = Seed,
        int linearIndex = 7,
        int stage = 1,
        RunPhase phase = RunPhase.InProgress,
        bool geared = true,
        IReadOnlyList<string>? curses = null,
        IReadOnlyList<string>? shrineBuffs = null,
        IReadOnlyList<string>? runBuffs = null,
        IReadOnlyDictionary<string, int>? consumables = null,
        bool escapeRopeArmed = false,
        ulong? shopOfferDraw = null) =>
        new(
            Worlds.Rehydrated(RunBattleWorlds.FarAboveParRow()),
            Rehydrated(RunSnapshots.With(
                runSeed: runSeed,
                chapterId: chapterId,
                gold: gold,
                currentHp: currentHp,
                lastAppliedAtUtc: NowUtc,
                pendingTileKind: (int)kind,
                pendingTileLinearIndex: linearIndex,
                pendingTileStage: stage,
                pendingEventCardId: eventCardId ?? RunSnapshots.NoPendingEventCard,
                phase: phase,
                rngStreamPositions: CombatStreamFor(phase),
                startingLoadout: geared
                    ? RunBattleWorlds.FarAboveParLoadout
                    : RunBattleWorlds.BareLoadout,
                curses: curses,
                shrineBuffs: shrineBuffs,
                runBuffs: runBuffs,
                consumables: consumables,
                escapeRopeArmed: escapeRopeArmed,
                shopOfferDraw: shopOfferDraw)));

    /// <summary>
    /// The <c>combat</c> stream position a run in <see cref="RunPhase.BattlePending"/> must carry —
    /// BattlePending with the stream at zero is a state the game cannot reach, and a run with no
    /// committed seed has no fight to recompute. Position one exactly, the position one
    /// <c>BeginBattle</c> leaves behind, so the derived seed is the first battle's.
    /// </summary>
    private static IReadOnlyDictionary<string, ulong>? CombatStreamFor(RunPhase phase) =>
        phase == RunPhase.BattlePending
            ? new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Combat] = 1UL }
            : null;

    /// <summary>A slice whose run is standing on no tile at all — every handler's first refusal.</summary>
    internal static WorldSlice OnNoTile(long gold = 0L, RunPhase phase = RunPhase.InProgress) =>
        new(
            Worlds.NewPlayer(),
            Rehydrated(RunSnapshots.With(gold: gold, lastAppliedAtUtc: NowUtc, phase: phase)));

    /// <summary>
    /// A run between rolls — no tile, no fork — carrying whatever pouch and rope state a case needs.
    /// </summary>
    /// <remarks>
    /// The one board state <c>USE_CONSUMABLE</c> is legal in, and therefore the fixture its suite is
    /// written over. Separate from <see cref="OnNoTile"/> rather than adding four parameters to it:
    /// that overload is the "every handler's first refusal" fixture and a dozen suites pass it
    /// positionally.
    /// </remarks>
    internal static WorldSlice OnNoTileHolding(
        int currentHp = 100,
        IReadOnlyDictionary<string, int>? consumables = null,
        bool escapeRopeArmed = false,
        int freeDraftRerolls = 0) =>
        new(
            Worlds.NewPlayer(),
            Rehydrated(RunSnapshots.With(
                lastAppliedAtUtc: NowUtc,
                currentHp: currentHp,
                consumables: consumables,
                escapeRopeArmed: escapeRopeArmed,
                freeDraftRerolls: freeDraftRerolls)));

    private static RunAggregate Rehydrated(RunSnapshot snapshot)
    {
        var run = RunAggregate.Rehydrate(snapshot);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException("The fixture RunSnapshot does not rehydrate: " + run.Error);
    }
}
