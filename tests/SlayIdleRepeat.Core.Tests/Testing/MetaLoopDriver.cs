using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Testing;

namespace SlayIdleRepeat.Core.Tests.Testing;

/// <summary>
/// The simulated player: it looks at the run the harness is holding, picks the one command that is
/// legal next, and sends it — nothing else. Every state change in <see cref="MetaLoopTests"/> comes
/// from here, and therefore from <c>GameRules.Apply</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>It reads the aggregates and writes nothing.</b> Choosing the next command needs to know
/// whether a fork is open, whether a draft is pending and what tile is waiting, and those are
/// <c>internal</c> members of <c>Run</c> this assembly may read under `30` §11.3's one
/// <c>InternalsVisibleTo</c> grant. Reading them is not the thing
/// <c>The_harness_drives_the_aggregates_through_their_public_seam_only</c> forbids — that rule is
/// about <c>Core/Testing/</c> reaching a <em>mutator</em>. There is no call to one here, and the
/// only reason the distinction is worth a paragraph is that the two look alike from a diff.
/// </para>
/// <para>
/// 🔴 <b>The driver resolves each board position once — until the run stalls, and then exactly once
/// more, to end it.</b> The first half is what keeps the loop honest: <see cref="MetaLoopTests"/>
/// records that a run parks on the last node of its stage and re-arrives at that same tile on every
/// subsequent roll, so a driver that kept fighting would farm one enemy indefinitely and could make
/// any income or XP assertion pass.
/// </para>
/// <para>
/// ⚠️ The second half is <b>not</b> free, and pretending otherwise would be the same dishonesty in
/// smaller print. Once <see cref="StalledAt"/> is set the guard is lifted, because losing a battle
/// is the only way a run reaches an ending and the only battle left is the one on the stalled node.
/// So on a board that stalls on an enemy, that enemy is fought twice: won once, then lost. What
/// rests on the second fight is the <em>ending</em> — and therefore the run payout, and therefore
/// every assertion downstream of it. What does not rest on it is any claim about how far the run
/// travelled or how many distinct tiles it resolved, which is what the first half protects.
/// </para>
/// </remarks>
internal sealed class MetaLoopDriver
{
    /// <summary>Any well-formed <c>LogHash</c>. The handler parses the shape and does not recompute it.</summary>
    private const string BattleLog = "1";

    /// <summary>The highest option index of a choice to try before giving the run up as stuck.</summary>
    /// <remarks>Four options are tried in all — indices 0 through this.</remarks>
    private const int ChoiceLadder = 3;

    /// <summary>
    /// The most commands one run may take, so a defect cannot hang the suite.
    /// </summary>
    /// <remarks>
    /// 🔒 Sized for a board the run can cross END TO END, not for the four tiles a stalled run gets:
    /// chapter 1 is 42 spine nodes plus the boss, and a node costs up to five commands (roll, resolve,
    /// battle, confirm, draft). A budget sized to today's stalled run would turn the repair of
    /// <c>MovementEngine</c>'s stage-end clamp into a failure of the one clause this file drives
    /// cleanly — measured: at 200 the repaired run exhausts the budget mid-board.
    /// </remarks>
    private const int CommandBudget = 600;

    /// <summary>
    /// The chest-pick minigame — the one <c>MINIGAME</c> instance that carries a pity counter, and
    /// therefore the one that puts this loop on `24` §11's protected path rather than beside it.
    /// </summary>
    /// <remarks>
    /// Read from the production catalogue rather than spelled again: an id this driver invented
    /// would be refused by <c>MINIGAME_SUBMIT</c>'s first guard, and the run would silently take the
    /// stuck branch instead of the protected one.
    /// ⚠️ Whether the loop reaches a Minigame tile at all is the board's choice, not this driver's —
    /// only the forge case's board resolves one. <see cref="Tiles"/> is what a caller asserts on.
    /// </remarks>
    private const string ChestPick = MinigameCatalogue.ChestPick;

    private readonly InMemoryGame _game;
    private readonly PlayerId _player;
    private readonly List<string> _log = [];
    private readonly HashSet<int> _resolved = [];
    private readonly HashSet<int> _seen = [];
    private readonly List<int> _visited = [];
    private readonly List<TileKind> _tiles = [];

    /// <summary>Which option of a choice this player is on. Reset the moment one is accepted.</summary>
    private int _choice;

    private MetaLoopDriver(InMemoryGame game, PlayerId player)
    {
        _game = game;
        _player = player;
    }

    /// <summary>Every command sent, with the answer it got — the trace a failure is diagnosed from.</summary>
    internal IReadOnlyList<string> Log => _log;

    /// <summary>
    /// The board positions the run stood on, in order, with <em>consecutive</em> repeats collapsed.
    /// </summary>
    /// <remarks>
    /// Consecutive only, so a caller asking "did this run travel" has to say
    /// <c>Visited.Distinct()</c> — a run bouncing between two nodes would otherwise look like four.
    /// The trailhead is not a board node and is not counted.
    /// </remarks>
    internal IReadOnlyList<int> Visited => _visited;

    /// <summary>The tile kinds the run resolved, one entry per distinct position, in order.</summary>
    internal IReadOnlyList<TileKind> Tiles => _tiles;

    /// <summary>
    /// How many <c>CONFIRM_BATTLE_RESULT</c>s were accepted — not how many distinct enemies were
    /// met. The stalled node's enemy is fought twice; see this type's remarks.
    /// </summary>
    internal int BattlesFought { get; private set; }

    /// <summary>How many of those were won.</summary>
    internal int BattlesWon { get; private set; }

    /// <summary>How many drafts the run opened and this player skipped.</summary>
    internal int DraftsSkipped { get; private set; }

    /// <summary>
    /// The position a <c>ROLL_DICE</c> was first accepted at without moving the run, or <c>null</c>
    /// if that never happened. See <see cref="MetaLoopTests"/> for what it means.
    /// </summary>
    internal int? StalledAt { get; private set; }

    /// <summary>How the run finished: the command that ended it, or why none could.</summary>
    internal string Ending { get; private set; } = "not started";

    /// <summary>A driver with no run behind it — for the meta commands sent between runs.</summary>
    /// <param name="game">The harness.</param>
    /// <param name="player">The player.</param>
    /// <returns>The driver.</returns>
    internal static MetaLoopDriver Idle(InMemoryGame game, PlayerId player) => new(game, player);

    /// <summary>Plays one run from <c>START_RUN</c> to an ended run, entirely through commands.</summary>
    /// <param name="game">The harness.</param>
    /// <param name="player">The player.</param>
    /// <param name="chapter">The chapter to run.</param>
    /// <param name="tier">The difficulty tier.</param>
    /// <param name="budget">The most commands this run may take, so a defect cannot hang the suite.</param>
    /// <returns>The driver, holding the trace.</returns>
    internal static MetaLoopDriver Play(
        InMemoryGame game, PlayerId player, int chapter, DifficultyTier tier, int budget = CommandBudget)
    {
        ArgumentNullException.ThrowIfNull(game);

        var driver = new MetaLoopDriver(game, player);

        driver.Send(new StartRunCommand(chapter, tier));
        driver.Drive(budget);

        return driver;
    }

    /// <summary>Sends one command and records what came back.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The result <c>Apply</c> produced.</returns>
    internal CommandResult Send(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = _game.Send(_player, command);
        var run = _game.State(_player).Run;

        _log.Add(
            command.GetType().Name + " -> " +
            (result.Accepted ? "accepted" : "refused " + result.Rejection) +
            " @" + Text(run?.Position ?? int.MinValue));

        return result;
    }

    private void Drive(int budget)
    {
        var dying = false;

        for (var issued = 0; issued < budget; issued++)
        {
            var run = _game.State(_player).Run;

            if (run is null || run.Phase == RunPhase.Ended)
            {
                return;
            }

            if (run.Position >= 0 && (_visited.Count == 0 || _visited[^1] != run.Position))
            {
                _visited.Add(run.Position);
            }

            // Losing is how a run ends today, so the decision to lose is taken once — the moment the
            // run stops being able to advance — rather than at a battle count that would drift with
            // the board.
            dying |= StalledAt is not null;

            if (Step(run, dying) is not { } next)
            {
                return;
            }

            var before = run.Position;
            var hadTile = run.HasPendingTile;
            var result = Send(next);

            if (!result.Accepted)
            {
                // A refused CHOICE is a choice this player cannot afford or the card does not offer,
                // not a dead end: the next one is tried before the run is given up on. A refusal of
                // anything else means the run genuinely cannot go on.
                if (next is EventChooseCommand or CampfireChooseCommand or ChooseForkCommand &&
                    _choice < ChoiceLadder)
                {
                    _choice++;
                    continue;
                }

                Ending = "stuck: " + next.GetType().Name + " refused " + result.Rejection;
                Abandon();
                return;
            }

            _choice = 0;

            switch (next)
            {
                case ConfirmBattleResultCommand confirmed:
                    BattlesFought++;

                    if (confirmed.Won)
                    {
                        BattlesWon++;
                    }

                    break;

                case SkipDraftCommand:
                    DraftsSkipped++;
                    break;

                default:
                    break;
            }

            var after = _game.State(_player).Run;

            // A tile counts as resolved once it is CLEARED, not once a command was aimed at it: an
            // Event needs RESOLVE_TILE and then EVENT_CHOOSE, and marking it on the first would make
            // the second look like a re-arrival.
            if (hadTile && after?.HasPendingTile == false && before >= 0)
            {
                _resolved.Add(before);
            }

            if (next is RollDiceCommand && StalledAt is null && after?.Position == before && before >= 0)
            {
                StalledAt = before;
            }
        }

        Ending = "budget of " + Text(budget) + " commands exhausted";
    }

    /// <summary>The one command that is legal next, or <c>null</c> when the run cannot go on.</summary>
    private GameCommand? Step(Run run, bool dying)
    {
        if (run.Phase == RunPhase.BattlePending)
        {
            // Counted in Drive once the command is ACCEPTED — a decision method that also tallies
            // would credit a win to a refused command.
            return new ConfirmBattleResultCommand(BattleLog, !dying);
        }

        if (run.DraftPending)
        {
            return new SkipDraftCommand();
        }

        if (run.PendingFork is not null)
        {
            return new ChooseForkCommand(_choice);
        }

        // 🔒 The victory ending is unreachable today — the stage-boundary stall parks every run in
        // stage 1, so no board this driver can walk reaches the boss node. It is written anyway, and
        // FIRST: the day movement is repaired, a run that beats the boss must have an ending waiting
        // for it, or this driver would walk to the boss and then exhaust its budget. That would fail
        // the one clause of the exit criterion this file drives cleanly, for a reason that has
        // nothing to do with the clause.
        if (run.BossDefeated)
        {
            Ending = "END_RUN after a victory";

            return new EndRunCommand();
        }

        if (run.CurrentHp == 0)
        {
            Ending = "END_RUN after a death";

            return new EndRunCommand();
        }

        if (run.HasPendingTile)
        {
            return Resolve(run, dying);
        }

        if (dying)
        {
            // Stalled on a tile that cannot kill anybody. Abandoning is the only ending left, and it
            // pays the run out, so the meta half of the loop still has something to spend.
            Ending = "ABANDON_RUN, stalled with no battle to lose";
            Abandon();

            return null;
        }

        return new RollDiceCommand();
    }

    private GameCommand? Resolve(Run run, bool dying)
    {
        var kind = (TileKind)run.PendingTileKindValue;

        if (_resolved.Contains(run.Position) && !dying)
        {
            // Already resolved here — this is a re-arrival on the stalled node, not a second tile.
            // Roll on and let the stall detector see it.
            return new RollDiceCommand();
        }

        if (_seen.Add(run.Position))
        {
            _tiles.Add(kind);
        }

        return kind switch
        {
            TileKind.Enemy or TileKind.Elite or TileKind.Boss => new StartBattleCommand(),
            TileKind.Minigame when !run.HasResolvedMinigameAt(run.Position) =>
                new MinigameSubmitCommand(ChestPick, 0),
            TileKind.Campfire => new CampfireChooseCommand(_choice),
            TileKind.Event when run.PendingEventCardId is { Length: > 0 } =>
                new EventChooseCommand(_choice),
            TileKind.Minigame => new RollDiceCommand(),
            _ => new ResolveTileCommand(),
        };
    }

    /// <summary>
    /// Gives the run up, and records it if even that is refused — an <see cref="Ending"/> naming a
    /// command that did not land would misattribute every assertion downstream of it.
    /// </summary>
    private void Abandon()
    {
        var result = Send(new AbandonRunCommand());

        if (!result.Accepted)
        {
            Ending = "unendable: " + Ending + ", and ABANDON_RUN was refused " + result.Rejection;
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
