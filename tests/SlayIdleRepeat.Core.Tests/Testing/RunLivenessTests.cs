using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Testing;

/// <summary>
/// 🔒 <b>The liveness sweep: a run can always act, and can always leave.</b>
/// </summary>
/// <remarks>
/// <para>
/// Every other suite in this repo asks whether one rule is right. This one asks the question those
/// rules are collectively for: <b>is there any state a legal sequence of commands can reach in which
/// the player is stuck?</b> A stuck run is not a bad payout or a wrong number — it is an account
/// that has permanently lost a run and a chapter's worth of Energy with it, and it is the class of
/// defect this game has shipped twice (Minigame and Portal in M3, Shop and Dice Forge in M7).
/// </para>
/// <para>
/// 🔒 <b>Two claims, and the second is the one that cannot rot.</b> The first — some command is
/// accepted — is only as good as the driver's imagination: a state it does not know how to answer
/// reads as stuck whether it is or not. The second — <c>ABANDON_RUN</c> is accepted <em>at every
/// state visited</em> — needs no imagination at all, and it is the guarantee the product owner
/// asked for in as many words. A future tile that wedges the loop still cannot trap the player,
/// and this suite says so at every single step rather than at the end.
/// </para>
/// <para>
/// The abandon probe is run on a COPY of the state and its result thrown away, so proving the escape
/// hatch open never changes the run being swept. <c>GameRules.Apply</c> clones before any handler
/// runs, which is what makes that safe rather than merely intended.
/// </para>
/// </remarks>
public sealed class RunLivenessTests
{
    /// <summary>How many distinct runs each sweep drives.</summary>
    /// <remarks>
    /// 🔴 <b>The runs are varied by the START INSTANT, not by the harness's root seed, and that is a
    /// fact about <c>SeedDerivation.RunSeed</c> rather than a preference.</b> A run's seed is
    /// <c>(playerId, chapter, tier, nowUtc, runCounter)</c> — the harness's own seed is nowhere in
    /// it, because it feeds only the per-command seeds META commands take. A first version of this
    /// suite swept twelve root seeds and drove <em>the identical run twelve times</em>: every tile
    /// count came back an exact multiple of twelve, which is what gave it away. Minutes apart on one
    /// game day, so the daily boundaries every other assertion in this repo rests on stay put.
    /// </remarks>
    private const int RunsPerSweep = 16;

    /// <summary>The most commands one swept run may take before the sweep gives up on it.</summary>
    /// <remarks>
    /// Generous: 43 nodes at up to six commands each (roll, resolve, choose, battle, confirm,
    /// draft) with room for refusals on the choice ladder. A run that exhausts this has not been
    /// caught being stuck — it has been caught looping, which the sweep reports separately.
    /// </remarks>
    private const int CommandBudget = 500;

    /// <summary>How deep the choice ladder goes before a refused choice counts as a dead end.</summary>
    /// <remarks>
    /// Six, because the SHOP is the deepest tile: four slots to try buying, then a refresh, then
    /// SHOP_LEAVE. A ladder of four stopped one rung short of the leave command and reported a
    /// perfectly healthy shop as a dead end — the bound has to cover the widest tile, not the
    /// average one.
    /// </remarks>
    private const int ChoiceLadder = 6;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void No_reachable_state_of_a_run_leaves_the_player_stuck(int chapterId)
    {
        var stuck = new List<string>();
        var reached = 0;

        for (var index = 0; index < RunsPerSweep; index++)
        {
            var walk = Sweep(chapterId, index);

            reached += walk.StatesVisited;
            stuck.AddRange(walk.DeadEnds);
        }

        stuck.ShouldBeEmpty(
            "a legal sequence of commands reached a state with no legal move out of it. That is a " +
            "run the player has permanently lost, and it is the defect class this suite exists for.");

        reached.ShouldBeGreaterThan(
            200,
            "the sweep only reached " + Text(reached) + " states, which is too few for the emptiness " +
            "above to be evidence of anything. A sweep that stopped at the first tile would report " +
            "no dead ends and prove nothing.");
    }

    /// <summary>
    /// 🔒 …and every swept run actually FINISHES — the claim the emptiness above cannot make on its
    /// own.
    /// </summary>
    /// <remarks>
    /// A walk that stopped early for any reason reports no dead ends, so "no dead ends" is satisfied
    /// by a sweep that barely moved. This one says where each run ended up: on a beaten boss, on a
    /// dead hero, or on an <c>Ended</c> run — and never on the command budget, which is what a loop
    /// would look like.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_swept_run_reaches_an_ending(int chapterId)
    {
        var unfinished = new List<string>();

        for (var index = 0; index < RunsPerSweep; index++)
        {
            var walk = Sweep(chapterId, index);

            if (!walk.Finished)
            {
                unfinished.Add("run " + index + ": " + walk.LastState);
            }
        }

        unfinished.ShouldBeEmpty(
            "a swept run neither won, died nor ended inside its command budget of " +
            Text(CommandBudget) + ". That is a loop rather than a dead end — the run keeps " +
            "accepting commands and never gets anywhere — and it would leave every emptiness " +
            "assertion in this file green.");
    }

    /// <summary>
    /// 🔒 The product owner's requirement, asserted at every state the sweep visits rather than at a
    /// handful of hand-built ones: <b>abandoning a run is available at all times.</b>
    /// </summary>
    /// <remarks>
    /// The hand-built cases in <c>AbandonRunTests</c> name the states that USED to refuse. This one
    /// covers the states nobody thought to name — including any a later tile invents.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Abandoning_is_accepted_at_every_state_a_run_can_reach(int chapterId)
    {
        var refused = new List<string>();
        var probed = 0;

        for (var index = 0; index < RunsPerSweep; index++)
        {
            var walk = Sweep(chapterId, index);

            probed += walk.AbandonProbes;
            refused.AddRange(walk.AbandonRefusals);
        }

        refused.ShouldBeEmpty(
            "ABANDON_RUN was refused from a state a live run can stand in. It is the one command " +
            "whose whole job is getting the player out, so a state it cannot answer is a trap.");

        probed.ShouldBeGreaterThan(
            200,
            "only " + Text(probed) + " states were probed, which is too few for the emptiness above " +
            "to mean anything.");
    }

    /// <summary>
    /// The negative control for both sweeps above: an ENDED run refuses everything, including the
    /// abandon probe.
    /// </summary>
    /// <remarks>
    /// Without it, both assertions are satisfied by a probe that is somehow always accepted — and a
    /// domain that accepted <c>ABANDON_RUN</c> on a finished run would be a second payout on a run
    /// already banked.
    /// </remarks>
    [Fact]
    public void The_probe_can_fail_an_ended_run_refuses_it()
    {
        var game = new InMemoryGame(
            ShippedHarness.Content, Harnesses.Seed, new VirtualClock(Harnesses.Start));
        var player = game.CreatePlayer(inventory: Harnesses.FarAboveParStock());

        Harnesses.Equip(game, player);
        game.Send(player, new StartRunCommand(1, DifficultyTier.NORMAL));
        game.Send(player, new AbandonRunCommand());

        var second = game.Send(player, new AbandonRunCommand());

        second.Accepted.ShouldBeFalse(
            "the probe used by both sweeps above is not unconditionally accepted — it answers " +
            "RUN_ALREADY_ENDED once there is no run left to leave.");
        second.Rejection.ShouldBe(RejectionReason.RUN_ALREADY_ENDED);
    }

    // ---------------------------------------------------------------------------------- the walk

    /// <summary>What one swept run produced.</summary>
    /// <param name="StatesVisited">How many states the walk stood in and asked for a move at.</param>
    /// <param name="AbandonProbes">How many of those it probed the escape hatch from.</param>
    /// <param name="DeadEnds">States with no legal move out. Empty is the whole point.</param>
    /// <param name="AbandonRefusals">States the escape hatch refused. Same.</param>
    /// <param name="Finished">Whether the run reached one of its two endings, or was already ended.</param>
    /// <param name="LastState">The state the walk stopped in, for the failure message.</param>
    private sealed record Walk(
        int StatesVisited,
        int AbandonProbes,
        IReadOnlyList<string> DeadEnds,
        IReadOnlyList<string> AbandonRefusals,
        bool Finished,
        string LastState);

    private static Walk Sweep(int chapterId, int index)
    {
        // Geared, because CONFIRM_BATTLE_RESULT recomputes the fight: a bare hero loses its first
        // battle and the walk ends four tiles in, which would make every emptiness assertion above a
        // claim about the first corner of stage 1.
        //
        // The SHIPPED content, not a hand-assembled tuning set: this sweep drives every tile there
        // is, and each of them reads a document of its own — a set missing one turns a liveness
        // failure into a MissingContentException about the fixture rather than a claim about the game.
        //
        // The CLOCK is what varies the board from run to run; see RunsPerSweep.
        var game = new InMemoryGame(
            ShippedHarness.Content,
            Harnesses.Seed,
            new VirtualClock(Harnesses.Start.AddMinutes(index)));
        var player = game.CreatePlayer(inventory: Harnesses.FarAboveParStock());

        Harnesses.Equip(game, player);

        // 10 §7's chapter ladder is enforced by START_RUN, and no command grants a clear — so a
        // sweep of chapter 2 has to be given one, or START_RUN refuses and the walk visits nothing
        // at all. Written onto the aggregate directly, which is what InMemoryGame.State's own
        // remarks say a test with internals access may do for a fixture that is not the subject.
        for (var below = 1; below < chapterId; below++)
        {
            Harnesses.HasCleared(game, player, below, DifficultyTier.NORMAL);
        }

        var deadEnds = new List<string>();
        var refusals = new List<string>();
        var visited = 0;
        var probes = 0;
        var choice = 0;
        var finished = false;
        var lastState = "the run never started";

        game.Send(player, new StartRunCommand(chapterId, DifficultyTier.NORMAL));

        for (var issued = 0; issued < CommandBudget; issued++)
        {
            var run = game.State(player).Run;

            if (run is null || run.Phase == RunPhase.Ended)
            {
                finished = true;
                break;
            }

            visited++;
            lastState = Describe(run);
            finished = run.BossDefeated || run.CurrentHp == 0;

            // 🔒 The escape hatch, probed from THIS state before anything else is tried — and on a
            // copy, so proving it open cannot change the run being swept. GameRules.Apply clones
            // the slice before any handler runs, which is what makes the throwaway result safe.
            probes++;

            var escape = Probe(game, player, new AbandonRunCommand());

            if (!escape.Accepted)
            {
                refusals.Add(Describe(run) + " -> ABANDON_RUN refused " + escape.Rejection);
            }

            var next = Move(run, choice);

            if (next is null)
            {
                // Nothing left to try. This is only a dead end if the run is still LIVE and could
                // not be ended either — an ended run and a run that chose to end are both fine.
                break;
            }

            var result = game.Send(player, next);

            if (result.Accepted)
            {
                choice = 0;
                continue;
            }

            // A refused CHOICE is an option this player cannot afford or the tile does not offer,
            // not a dead end: the ladder tries the next one. Only when the ladder is exhausted, and
            // the run can neither be ended nor legally acted on, is the state genuinely stuck.
            if (choice < ChoiceLadder)
            {
                choice++;
                continue;
            }

            // 🔴 Stuck means the run cannot PROGRESS, and being able to abandon is deliberately NOT
            // an excuse. Abandoning is losing the run — the thing the player is trying not to do —
            // so a definition that let it discharge this claim would be green for a shop tile no
            // command could ever clear, which is precisely the defect this sweep exists to catch.
            //
            // The two states that are legitimately out of moves are the two endings: a boss beaten
            // and a hero at zero. Both are answered by END_RUN, and both are checked by identity
            // rather than by asking whether END_RUN is accepted — the latter is the rule under test.
            if (!run.BossDefeated && run.CurrentHp != 0)
            {
                deadEnds.Add(
                    Describe(run) + " -> " + next.GetType().Name + " refused " + result.Rejection +
                    " after the whole choice ladder, with the boss alive and the hero on its feet");
            }

            break;
        }

        return new Walk(visited, probes, deadEnds, refusals, finished, lastState);
    }

    /// <summary>
    /// Applies a command against the harness's current state WITHOUT storing the result — the
    /// question "would this be accepted from here?" asked without answering it.
    /// </summary>
    /// <remarks>
    /// 🔒 Safe because <c>GameRules.Apply</c> clones the slice before any handler runs, so the
    /// aggregates the harness holds are untouched whatever the probe does. Going through
    /// <c>Apply</c> rather than <c>InMemoryGame.Send</c> is the whole point: <c>Send</c> stores the
    /// resulting slice, which would make proving the escape hatch open the thing that took it.
    /// </remarks>
    private static CommandResult Probe(InMemoryGame game, PlayerId player, GameCommand command) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            game.State(player),
            command,
            new GameContext(
                game.Clock.NowUtc,
                CommandSeed: null,
                game.Content,
                game.Entitlements,
                game.Flags));

    /// <summary>The command a player would send from this state, or <c>null</c> when there is none left.</summary>
    /// <remarks>
    /// Deliberately more adventurous than <c>MetaLoopDriver</c>'s: it BUYS at shops, USES
    /// consumables and takes every campfire option, because the states this sweep is looking for are
    /// the ones a cautious player never reaches. Which is also why it always wins its battles —
    /// dying ends the run early and stops the walk before the tiles further along the board.
    /// </remarks>
    private static GameCommand? Move(RunAggregate run, int choice)
    {
        if (run.Phase == RunPhase.BattlePending)
        {
            return new ConfirmBattleResultCommand("1", Won: true);
        }

        if (run.DraftPending)
        {
            return choice == 0 ? new PickPerkCommand(0) : new SkipDraftCommand();
        }

        if (run.PendingFork is not null)
        {
            return new ChooseForkCommand(choice);
        }

        if (run.BossDefeated || run.CurrentHp == 0)
        {
            return new EndRunCommand();
        }

        if (!run.HasPendingTile)
        {
            // A held consumable is spent on the board, between rolls, which is the one window
            // 03 §7.1 allows — and the window this sweep is here to prove is not a trap.
            if (choice == 0 && run.ConsumableCount(Consumables.HealthDraught) > 0 &&
                run.CurrentHp < run.MaxHp)
            {
                return new UseConsumableCommand(Consumables.HealthDraught);
            }

            if (choice == 0 && run.ConsumableCount(Consumables.EscapeRope) > 0 &&
                !run.EscapeRopeArmed)
            {
                return new UseConsumableCommand(Consumables.EscapeRope);
            }

            return new RollDiceCommand();
        }

        return (TileKind)run.PendingTileKindValue switch
        {
            TileKind.Enemy or TileKind.Elite or TileKind.MiniBoss or TileKind.Boss =>
                new StartBattleCommand(),

            TileKind.Minigame when !run.HasResolvedMinigameAt(run.Position) =>
                new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 0),

            TileKind.Campfire => new CampfireChooseCommand(choice),
            TileKind.Event when run.PendingEventCardId is { Length: > 0 } => new EventChooseCommand(choice),
            TileKind.Shrine => new ShrineChooseCommand(choice),

            // The shop is walked all the way through: stock, buy every slot the ladder reaches,
            // refresh, then leave. Every one of those is a command that can refuse, and a refusal
            // that could not be recovered from is exactly what this sweep is looking for.
            TileKind.Shop when !run.HasOpenShop => new ResolveTileCommand(),
            TileKind.Shop when choice < 4 => new ShopBuyCommand(choice),
            TileKind.Shop when choice == 4 => new ShopRefreshCommand(),
            TileKind.Shop => new ShopLeaveCommand(),

            _ => new ResolveTileCommand(),
        };
    }

    /// <summary>One state, rendered for a failure message.</summary>
    private static string Describe(RunAggregate run) =>
        "pos=" + Text(run.Position) +
        " hp=" + Text(run.CurrentHp) +
        " phase=" + run.Phase +
        " tile=" + (run.HasPendingTile ? ((TileKind)run.PendingTileKindValue).ToString() : "none") +
        " fork=" + (run.PendingFork is { } fork ? Text(fork.JunctionPosition) : "none") +
        " draft=" + run.DraftPending +
        " shop=" + run.HasOpenShop;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
