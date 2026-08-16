using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>MG_CHEST_PICK</c>'s pity counter as <c>MINIGAME_SUBMIT</c> moves it: player-scoped, lifetime,
/// and untouched by the other three minigames.
/// </summary>
/// <remarks>
/// Separate from <c>MinigameSubmitTests</c>, which is about the legality gate and the reward table.
/// The counter is a different concern with a different scope, and the per-tile resolution map those
/// cases are written against is exactly the thing it must NOT be stored in.
/// </remarks>
public sealed class MinigameChestPickPityTests
{
    /// <summary>The counter id, formed the way production forms it. Never spelled here.</summary>
    private static string CounterKey =>
        LuckTuning.Read(LuckDocuments.LuckOnly())
            .CounterKey(SourceClass.MINIGAME, LuckDocuments.ShippedChestPickGuaranteeToken);

    private static WorldSlice Submit(WorldSlice state, string minigameId, int claimed = 0) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(minigameId, claimed), Worlds.Context).NewState;

    /// <summary>A run standing on a fresh, unresolved tile at <paramref name="position"/>.</summary>
    private static WorldSlice AtTile(int position) =>
        Worlds.InARun(RunSnapshots.With(position: position));

    private static int Counter(WorldSlice state) => state.Player.PityCounters.Get(CounterKey);

    // ------------------------------------------------------------------ the counter moves

    /// <summary>A chest pick that misses the gold tier advances the counter.</summary>
    [Fact]
    public void A_chest_pick_moves_the_chest_pick_counter()
    {
        var after = Submit(AtTile(5), MinigameCatalogue.ChestPick);

        Counter(after).ShouldNotBe(
            0,
            "24 §4.9's guarantee is unreachable if nothing ever advances its counter, and a counter " +
            "that never moves is invisible to every other case in this file.");
    }

    /// <summary>
    /// The other three minigames never move it. They are skill-scaled and carry no counter at all.
    /// </summary>
    /// <remarks>
    /// 🔒 The negative control. A handler that advanced the counter on every <c>MINIGAME_SUBMIT</c>
    /// would satisfy the case above and would make the gold chest farmable through the cheapest
    /// minigame on the board — the anti-farming question `24` §1.2 asks of every counter.
    /// </remarks>
    [Theory]
    [InlineData(MinigameCatalogue.TimingBar)]
    [InlineData(MinigameCatalogue.MemoryRune)]
    [InlineData(MinigameCatalogue.DiceDuel)]
    public void The_other_three_minigames_never_move_the_chest_pick_counter(string minigameId)
    {
        Counter(Submit(AtTile(5), minigameId)).ShouldBe(0);
    }

    // ------------------------------------------------------------------ the scope

    /// <summary>
    /// The counter lives on the player and survives the run it was advanced in.
    /// </summary>
    /// <remarks>
    /// 🔒 The scope claim, asserted through a <c>PlayerSnapshot</c> round-trip rather than through
    /// the live aggregate: the run-scoped alternative — a row in <c>Run.ResolvedMinigames</c> — would
    /// pass every in-run assertion above and lose the counter at the run boundary, which makes a
    /// four-miss guarantee unreachable for a player who meets one chest pick per run.
    /// </remarks>
    [Fact]
    public void The_counter_is_player_scoped_and_survives_the_run_that_advanced_it()
    {
        var after = Submit(AtTile(5), MinigameCatalogue.ChestPick);
        var advanced = Counter(after);

        advanced.ShouldNotBe(0);

        var rehydrated = Core.Model.Player.Rehydrate(after.Player.ToSnapshot(), Worlds.Context.Content);

        rehydrated.IsSuccess.ShouldBeTrue(rehydrated.Error);
        rehydrated.Value.PityCounters.Get(CounterKey).ShouldBe(advanced);
    }

    /// <summary>Two chest picks in different runs accumulate on the same counter.</summary>
    /// <remarks>
    /// The run is rebuilt between them — a new <c>RunSnapshot</c> at a different position, which is
    /// what a second run is to this handler — while the player carries over.
    /// </remarks>
    [Fact]
    public void Two_chest_picks_across_two_runs_accumulate_on_one_counter()
    {
        var first = Submit(AtTile(5), MinigameCatalogue.ChestPick);

        var nextRun = new WorldSlice(
            first.Player,
            Core.Model.Run.Rehydrate(RunSnapshots.With(position: 9)).Value);

        var second = Submit(nextRun, MinigameCatalogue.ChestPick);

        Counter(second).ShouldBeGreaterThan(
            Counter(first),
            "24 §1.1: counters never reset on anything but their own guarantee. A counter that " +
            "started over every run would make the four-pick guarantee unreachable.");
    }

    // ------------------------------------------------------------------ the guarantee

    /// <summary>Four consecutive chest picks in one player's life produce a gold tier.</summary>
    /// <remarks>
    /// Driven end to end rather than against the resolver, because the claim is about what the
    /// handler stores between picks: a resolver that decides correctly against a counter nothing
    /// persists guarantees nothing at all.
    /// </remarks>
    [Fact]
    public void Four_consecutive_picks_produce_a_gold_tier()
    {
        var state = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                pityCounters: PlayerSnapshots.Pity((CounterKey, LuckDocuments.ShippedMinigameChestPickN - 1)))),
            Worlds.NewRun(RunSnapshots.With(position: 5)));

        var after = Submit(state, MinigameCatalogue.ChestPick);

        after.Player.PityCounters.Get(CounterKey).ShouldBe(
            0,
            "the forced pick satisfies the guarantee, and satisfying a guarantee resets its counter.");
    }

    // ------------------------------------------------------------------ S24 regression guard

    /// <summary>
    /// The pending tile is still cleared as the last step of a submission.
    /// </summary>
    /// <remarks>
    /// A regression guard rather than a new claim: recording the resolution touches only the
    /// per-tile legality proxy, so a submission that stopped clearing the pending tile would leave
    /// <c>ROLL_DICE</c> unable to fire again for the rest of the run — and wiring a counter through
    /// this handler is exactly the kind of edit that drops a trailing statement.
    /// </remarks>
    [Theory]
    [InlineData(MinigameCatalogue.ChestPick)]
    [InlineData(MinigameCatalogue.TimingBar)]
    public void A_submission_still_clears_the_pending_tile(string minigameId)
    {
        var state = Worlds.InARun(
            RunSnapshots.OnPendingTile((int)Core.Rules.Board.TileKind.Minigame, linearIndex: 5, stage: 1)
                with { Position = 5 });

        var after = Submit(state, minigameId);

        after.Run!.ToSnapshot().PendingTileKind.ShouldBe(
            RunSnapshots.NoPendingTile,
            "the pending tile is cleared last, and nothing added to this handler may land after it.");
    }
}
