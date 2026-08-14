using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `14` §8.1 — the RNG write-back choke point: <c>Apply</c> hands a handler a <c>RunRngScope</c>
/// over the run's committed seed and counters, and <c>Apply</c> — never the handler — folds the final
/// positions back into the <c>Run</c>.
/// </summary>
/// <remarks>
/// The persisted stream position <em>is</em> the draw counter, so a handler that drew and forgot to
/// write its counter back would break determinism silently: the run replays with different draws, the
/// parity test compares two universes, and nothing goes red. Every rule below removes one route there.
/// </remarks>
public sealed class GameRulesRngTests
{
    /// <summary>
    /// 🔒 A handler that draws and writes nothing back still advances the counter — the fold is
    /// <c>Apply</c>'s and unconditional, so "forgetting" is not an available failure.
    /// </summary>
    /// <remarks>
    /// Delete the <c>CommitStreamPositions</c> call in <c>FoldRngPositions</c> and this is what goes
    /// red; nothing else in the repository would.
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
    /// 🔒 The counter <b>resumes</b> where the run left off: a second command continues the sequence
    /// rather than replaying it.
    /// </summary>
    /// <remarks>
    /// The draws are compared, not just the counter: a scope opening every stream at 0 leaves the
    /// counter looking right (2 → 4, because the fold writes the scope's position) while handing the
    /// player the same two faces twice.
    /// <para>
    /// 🔒 And against <b>the sequence `14` §8.1 derives</b>, not merely "different from the first two" —
    /// which every wrong resume position satisfies. The second command's draws are
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

    /// <summary>🔒 A <b>refused</b> command consumes no draw indices — discarded with the clone.</summary>
    /// <remarks>
    /// Without this a client could shift a run's whole future by sending commands it knew would be
    /// refused, and a replay of the accepted commands alone would produce different draws.
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

    /// <summary>🔒 A handler that <b>hand-writes</b> a stream position is refused, loudly.</summary>
    /// <remarks>
    /// <c>CommitStreamPositions</c> takes the <b>whole</b> map and rejects a dropped key, so a handler
    /// wanting to write one position has to reconstruct the entire committed set to get this far. This
    /// catches the one that did. It throws rather than rejecting because a rejection would hand the
    /// corrupt scope back as a polite "no" and leave the run in it.
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
    /// ⚠️ A handler that hand-writes a <b>partial</b> map never reaches <c>Apply</c>'s check, because
    /// the aggregate refuses the dropped key first.
    /// </summary>
    /// <remarks>
    /// A rule of its own so the two failures are not confused: this one names the missing stream, the
    /// one above names the handler, and a reader handed the wrong message fixes the wrong thing.
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
    /// A dropped key would silently reset that stream to 0 and the next draw would repeat a sequence
    /// the player has already played. <c>Run.CommitStreamPositions</c> refuses that, so a scope failing
    /// to carry the committed rows forward could not commit at all.
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
    /// 🔒 The fold is <b>sparse</b>: a command that drew nothing leaves the map as it was, with no
    /// eager zeros.
    /// </summary>
    /// <remarks>
    /// `14` §2.3's wire echo shows only the streams that moved, and <c>Run</c> reads an absent stream as
    /// draw 0. Writing zeros for the eight fixed streams would put dead bytes in every
    /// <c>stateHash</c> — and `14` §2.4 has the client recompute one per command, on a handset.
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
    /// It is the position of a <em>different</em> quantity: the <c>runSeed</c>-rooted <c>combat</c>
    /// stream is consumed once per battle to derive that battle's seed, so its position is the next
    /// <c>battleIndex</c>. The forty draws below come from the <b>battle</b> stream and none is
    /// persisted — which is what makes <em>"a revived battle restarts from draw 0 of the same battle
    /// stream"</em> true. Treating <c>combat</c> like the others leaves the position at 41 and seeds
    /// the next battle from a number no replay could reproduce.
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
    /// 🔒 A <b>meta</b> handler that hand-writes the run's stream positions is the same defect as a
    /// run handler doing it — even though a meta command has no scope to fold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the route the check would have left open if it had returned early on a null
    /// <c>RunRngScope</c>. A meta command <em>is</em> dispatched with a run in the slice — a player
    /// can open the shop without leaving one, which
    /// <c>GameRulesDispatchTests.A_meta_command_runs_with_a_run_in_the_slice</c> pins — and
    /// <c>HandlerInput.Run</c> hands it that run quite happily. Reaching
    /// <c>CommitStreamPositions</c> from there would move `14` §8.1's counters with nothing
    /// watching, and the run would replay differently for the rest of its life.
    /// </para>
    /// <para>
    /// The refusal names the meta regime explicitly (steering <b>S2</b>): the fix is not "use the
    /// scope" — a meta command has none — it is "read the run, never write its counters".
    /// </para>
    /// </remarks>
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
