using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `14` §8.1 / M1 kickoff decision 5 — the RNG write-back choke point: <c>Apply</c> hands a
/// handler a <c>RunRngScope</c> over the run's committed seed and counters, and <c>Apply</c> — never
/// the handler — folds the final positions back into the <c>Run</c>.
/// </summary>
/// <remarks>
/// <b>The hazard, stated once.</b> `14` §8.1 makes the persisted stream position <em>the draw
/// counter</em>. A handler that drew and forgot to write its counter back would break determinism
/// silently and unreproducibly: the run replays with different draws, the parity test (`14` §13)
/// compares two universes, and nothing goes red. Every rule below exists to make one route to that
/// outcome unavailable.
/// </remarks>
public sealed class GameRulesRngTests
{
    /// <summary>
    /// 🔒 A handler that draws and writes nothing back still advances the run's counter — the fold
    /// is <c>Apply</c>'s and is unconditional, so "forgetting" is not an available failure.
    /// </summary>
    /// <remarks>
    /// This is the positive half of the hazard above. Delete the <c>CommitStreamPositions</c> call
    /// in <c>GameRules.FoldRngPositions</c> and this is what goes red; nothing else in the
    /// repository would.
    /// </remarks>
    [Fact]
    public void A_handler_that_draws_and_writes_nothing_back_still_advances_the_counter()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                var dice = input.Rng.Stream(RngStreams.Dice);
                dice.Range(1, 7);
                dice.Range(1, 7);
                dice.Range(1, 7);

                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(3UL);
    }

    /// <summary>
    /// 🔒 The counter <b>resumes</b> where the run left off. A second command continues the sequence
    /// rather than replaying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The draws are compared, not just the counter: a scope that opened every stream at 0 would
    /// leave the counter looking right (2 → 4, because the fold writes the scope's position) while
    /// handing the player the same two faces twice.
    /// </para>
    /// <para>
    /// 🔒 And they are compared against <b>the sequence `14` §8.1 derives</b>, not merely asserted
    /// to differ from the first two. "These four numbers are not those two" is satisfied by every
    /// wrong resume position there is — a scope that opened at 1, or at 12, or at whatever the
    /// previous command's counter happened to be plus one — so it would report success for an
    /// implementation that continued the stream in the wrong place. The second command's draws are
    /// <c>Hash64(runSeed, "dice", 2)</c> and <c>…, 3)</c> or the run does not replay.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_second_command_continues_the_sequence_rather_than_replaying_it()
    {
        var drawn = new List<int>();

        var table = Worlds.RunTable((_, input) =>
        {
            var dice = input.Rng.Stream(RngStreams.Dice);
            drawn.Add(dice.Range(1, 1_000_000));
            drawn.Add(dice.Range(1, 1_000_000));

            return HandlerResult.Accept();
        });

        var first = SlayIdleRepeat.Core.GameRules.Execute(
            table, Worlds.InARun(), new Worlds.RunFixtureCommand(), Worlds.Context);

        var second = SlayIdleRepeat.Core.GameRules.Execute(
            table, first.NewState, new Worlds.RunFixtureCommand(), Worlds.Context);

        second.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(4UL);

        var resumed = new DeterministicRng(RunSnapshots.Seed, RngStreams.Dice, 2UL);

        drawn.Skip(2).ShouldBe(new[] { resumed.Range(1, 1_000_000), resumed.Range(1, 1_000_000) },
            "the second command must take draws 2 and 3 of the run's dice stream — not draws 0 and " +
            "1 again, and not draws from some other position that merely happens to differ.");

        drawn.Take(2).ShouldNotBe(drawn.Skip(2).ToList(),
            "and the two commands' draws are therefore different numbers, which is what a player " +
            "would notice if the stream reopened at 0.");
    }

    /// <summary>
    /// 🔒 A <b>refused</b> command consumes no draw indices. The scope's work is discarded with the
    /// clone.
    /// </summary>
    /// <remarks>
    /// Without this a client could shift a run's whole future by sending commands it knew would be
    /// refused, and a replay of the accepted commands alone would produce different draws — the
    /// unreproducible run `14` §8.1's counter model exists to prevent.
    /// </remarks>
    [Fact]
    public void A_refused_command_consumes_no_draws()
    {
        var state = Worlds.InARun();

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                input.Rng.Stream(RngStreams.Dice).Range(1, 7);
                input.Rng.Stream(RngStreams.Board).NextUInt();

                return HandlerResult.Reject(RejectionReason.INSUFFICIENT_ENERGY);
            }),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeFalse();
        state.Run!.RngStreamPositions.ShouldBeEmpty();
        result.NewState.Run!.RngStreamPositions.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A handler that <b>hand-writes</b> a stream position is refused — loudly, as a defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M1-05's seam already refuses the partial version of this mistake: <c>CommitStreamPositions</c>
    /// takes the <b>whole</b> map and rejects a dropped key, so a handler wanting to write one
    /// position has to reconstruct the entire committed set to get this far. This is what catches the
    /// handler that did.
    /// </para>
    /// <para>
    /// It throws rather than rejecting because a rejection would hand the corrupt scope back to the
    /// player as a polite "no" and leave the run in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_handler_that_hand_writes_a_stream_position_is_a_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                input.Run.CommitStreamPositions(new Dictionary<string, ulong>(StringComparer.Ordinal)
                {
                    [RngStreams.Dice] = 99UL,
                });

                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("wrote the run's 14 §8.1 stream positions itself", Case.Sensitive);
        thrown.Message.ShouldContain(Worlds.RunWireName, Case.Sensitive);
    }

    /// <summary>
    /// ⚠️ M1-05's superset contract, restated from this side: a handler that hand-writes a
    /// <b>partial</b> map never reaches <c>Apply</c>'s check at all, because the aggregate refuses
    /// the dropped key first.
    /// </summary>
    /// <remarks>
    /// Worth a rule of its own so the two failures are not confused: this one names the missing
    /// stream, and the one above names the handler. A reader handed the wrong message fixes the
    /// wrong thing (steering S2).
    /// </remarks>
    [Fact]
    public void A_partial_hand_written_map_is_refused_by_the_aggregate_before_Apply_sees_it()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                input.Run.CommitStreamPositions(new Dictionary<string, ulong>(StringComparer.Ordinal)
                {
                    [RngStreams.Board] = 5UL,
                });

                return HandlerResult.Accept();
            }),
            Worlds.InARun(RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 12UL)))),
            new Worlds.RunFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("has no row for the stream 'dice'", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The fold is a <b>superset</b> of what the run had committed: a stream this command never
    /// touched keeps its position.
    /// </summary>
    /// <remarks>
    /// A dropped key would silently reset that stream to 0 and the next draw from it would repeat a
    /// sequence the player has already played. <c>Run.CommitStreamPositions</c> refuses that, so a
    /// scope that failed to carry the committed rows forward could not commit at all — which is the
    /// point, and this is what proves the scope satisfies it.
    /// </remarks>
    [Fact]
    public void The_fold_carries_forward_every_stream_this_command_did_not_touch()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                input.Rng.Stream(RngStreams.Dice).Range(1, 7);
                return HandlerResult.Accept();
            }),
            Worlds.InARun(RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(
                (RngStreams.Dice, 12UL),
                (RngStreams.Board, 8UL),
                (RngStreams.Minigame(3), 2UL)))),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        var positions = result.NewState.Run!.RngStreamPositions;

        positions[RngStreams.Dice].ShouldBe(13UL);
        positions[RngStreams.Board].ShouldBe(8UL);
        positions[RngStreams.Minigame(3)].ShouldBe(2UL);
        positions.Count.ShouldBe(3);
    }

    /// <summary>
    /// 🔒 The fold is <b>sparse</b>: a command that drew nothing leaves the map exactly as it was,
    /// with no eager zeros.
    /// </summary>
    /// <remarks>
    /// `14` §2.3's wire echo (<c>{"dice":12,"board":8}</c>) shows only the streams that moved, and
    /// <c>Run</c> reads an absent stream as draw 0. Writing zeros for the eight fixed streams would
    /// put dead bytes in every <c>stateHash</c> — and `14` §2.4 has the client recompute one per
    /// command, on a handset.
    /// </remarks>
    [Fact]
    public void A_command_that_draws_nothing_writes_no_rows()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept()),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Run!.RngStreamPositions.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ combat, the other unit

    /// <summary>
    /// 🔒 `14` §8.1 — the <c>combat</c> position advances <b>once per battle</b>, not once per draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the position of a <em>different</em> quantity: the <c>runSeed</c>-rooted <c>combat</c>
    /// stream is consumed exactly once per battle, to derive that battle's seed, so its position is
    /// the next <c>battleIndex</c>. The forty draws below all come from the <b>battle</b> stream,
    /// rooted at that seed, and none of them is persisted.
    /// </para>
    /// <para>
    /// That is what makes §8.1's <em>"a revived battle restarts from draw 0 of the same battle
    /// stream: reproducible by construction"</em> true. A scope that treated <c>combat</c> like the
    /// others would leave the position at 41 and seed the next battle from a number no replay could
    /// reproduce.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_combat_position_advances_once_per_battle_and_not_once_per_draw()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                var battle = input.Rng.BeginBattle();

                for (var i = 0; i < 40; i++)
                {
                    battle.Draws.NextUInt();
                }

                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Run!.StreamPosition(RngStreams.Combat).ShouldBe(
            1UL,
            "one battle started, so the next battleIndex is 1. Forty draws INSIDE the battle come " +
            "from the battle stream and are never persisted (14 §8.1).");
    }

    /// <summary>
    /// 🔒 Two battles in one command advance the position by exactly two, and each gets its own
    /// seed.
    /// </summary>
    [Fact]
    public void Each_battle_takes_the_next_index_and_its_own_seed()
    {
        var battles = new List<BattleRng>();

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                battles.Add(input.Rng.BeginBattle());
                battles.Add(input.Rng.BeginBattle());

                return HandlerResult.Accept();
            }),
            Worlds.InARun(RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Combat, 4UL)))),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Run!.StreamPosition(RngStreams.Combat).ShouldBe(6UL);

        battles.Select(b => b.BattleIndex).ShouldBe(new[] { 4, 5 });
        battles[0].Seed.ShouldBe(SeedDerivation.BattleSeed(RunSnapshots.Seed, 4));
        battles[1].Seed.ShouldBe(SeedDerivation.BattleSeed(RunSnapshots.Seed, 5));
        battles[0].Seed.ShouldNotBe(battles[1].Seed);
    }

    /// <summary>
    /// 🔒 `14` §8.1 — a revived battle restarts from draw 0 of the same battle stream: the same
    /// <c>battleIndex</c> reproduces the same seed and therefore the same fight.
    /// </summary>
    [Fact]
    public void A_battle_replayed_at_the_same_index_reproduces_its_draws()
    {
        var scope = new RunRngScope(RunSnapshots.Seed, RunSnapshots.Streams((RngStreams.Combat, 9UL)));
        var replay = new RunRngScope(RunSnapshots.Seed, RunSnapshots.Streams((RngStreams.Combat, 9UL)));

        var original = scope.BeginBattle();
        var revived = replay.BeginBattle();

        revived.Seed.ShouldBe(original.Seed);
        revived.BattleIndex.ShouldBe(original.BattleIndex);

        Enumerable.Range(0, 10).Select(_ => revived.Draws.NextUInt())
            .ShouldBe(Enumerable.Range(0, 10).Select(_ => original.Draws.NextUInt()).ToArray());
    }

    // ------------------------------------------------------------------ the two regimes

    /// <summary>
    /// 🔒 `30` §3 — a <b>meta</b> command gets no <c>RunRngScope</c>, even with a run in the slice:
    /// out-of-run draws come from <c>GameContext.CommandSeed</c> with no persisted counter.
    /// </summary>
    [Fact]
    public void A_meta_command_has_no_run_scope_even_with_a_run_loaded()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                input.Rng.Stream(RngStreams.Dice).Range(1, 7);
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("GameContext.CommandSeed", Case.Sensitive);
        thrown.Message.ShouldContain("NO persisted counter", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A meta command's slice keeps the run's counters exactly as they were: the meta regime does
    /// not touch `14` §8.1's counters at all.
    /// </summary>
    [Fact]
    public void A_meta_command_leaves_the_runs_counters_alone()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            Worlds.InARun(RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 12UL)))),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.NewState.Run!.StreamPosition(RngStreams.Dice).ShouldBe(12UL);
    }
}
