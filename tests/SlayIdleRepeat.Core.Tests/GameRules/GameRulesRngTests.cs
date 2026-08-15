using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The RNG write-back choke point: <c>Apply</c> hands a handler a <c>RunRngScope</c> over the
/// run's committed seed and counters, and <c>Apply</c> — never the handler — folds the final
/// positions back into the <c>Run</c>.
/// </summary>
/// <remarks>
/// The persisted stream position is the draw counter, so a handler that drew and forgot to write
/// its counter back would break determinism silently: the run replays with different draws and
/// nothing goes red. Every rule below removes one route there.
/// </remarks>
public sealed class GameRulesRngTests
{
    /// <summary>
    /// A handler that draws and writes nothing back still advances the counter — the fold is
    /// <c>Apply</c>'s and unconditional, so "forgetting" is not an available failure.
    /// </summary>
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
    /// The counter resumes where the run left off: a second command continues the sequence rather
    /// than replaying it. The draws themselves are compared, not just the counter: a scope opening
    /// every stream at 0 leaves the counter looking right while handing the player the same two
    /// faces twice.
    /// </summary>
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
    /// A refused command consumes no draw indices — discarded with the clone. Otherwise a client
    /// could shift a run's whole future by sending commands it knew would be refused.
    /// </summary>
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
    /// A handler that hand-writes a stream position is refused, loudly — it throws rather than
    /// rejecting, because a rejection would hand the corrupt scope back as a polite "no".
    /// </summary>
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
    /// A handler that hand-writes a partial map never reaches <c>Apply</c>'s check, because the
    /// aggregate refuses the dropped key first — a separate rule so the two failures name different
    /// things and a reader is not misled.
    /// </summary>
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
    /// The fold is a superset of what the run had committed: a stream this command never touched
    /// keeps its position, since a dropped key would silently reset that stream to 0.
    /// </summary>
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
    /// The fold is sparse: a command that drew nothing leaves the map as it was, with no eager
    /// zeros. <c>Run</c> reads an absent stream as draw 0, so writing zeros for every fixed stream
    /// would put dead bytes in every state hash the client recomputes per command, on a handset.
    /// </summary>
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
    /// The <c>combat</c> position advances once per battle, not once per draw: the
    /// <c>runSeed</c>-rooted stream is consumed once per battle to derive that battle's seed, so its
    /// position is the next <c>battleIndex</c>. The forty draws below come from the battle stream
    /// and none is persisted.
    /// </summary>
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

    /// <summary>Two battles in one command advance the position by exactly two, and each gets its own seed.</summary>
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
    /// A revived battle restarts from draw 0 of the same battle stream: the same
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
    /// A meta command gets no <c>RunRngScope</c>, even with a run in the slice: out-of-run draws
    /// come from <c>GameContext.CommandSeed</c> with no persisted counter.
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
    /// A meta handler that hand-writes the run's stream positions is the same defect as a run
    /// handler doing it, even though a meta command has no scope to fold: a meta command is
    /// dispatched with a run in the slice, and <c>HandlerInput.Run</c> hands it that run quite
    /// happily, so the check must not return early on a null <c>RunRngScope</c>.
    /// </summary>
    [Fact]
    public void A_meta_handler_that_hand_writes_a_stream_position_is_a_defect_too()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                input.Run.CommitStreamPositions(new Dictionary<string, ulong>(StringComparer.Ordinal)
                {
                    [RngStreams.Dice] = 99UL,
                });

                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain("wrote the run's 14 §8.1 stream positions itself", Case.Sensitive);
        thrown.Message.ShouldContain(Worlds.MetaWireName, Case.Sensitive);
        thrown.Message.ShouldContain("CommandKind.Meta command it has no scope at all", Case.Sensitive);
    }

    /// <summary>A meta command's slice keeps the run's counters exactly as they were.</summary>
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
