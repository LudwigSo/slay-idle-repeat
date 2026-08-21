using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Rules.Board.Resolution;

/// <summary>
/// Which landings an armed Escape Rope may skip. The rope is the one way to leave a node without
/// resolving what stands on it, so it is the one way a fight the board refuses to let a run walk
/// past could still be walked past.
/// </summary>
/// <remarks>
/// The movement suite pins that a roll cannot carry a run beyond a mini-boss. That claim is worth
/// nothing on its own if a consumable can then skip the fight the run was forced to stop for: the
/// node stays unmissable and its content does not, which is the same outcome one step later. So the
/// negative case below is the rule, and the positive case beside it is what stops the rule being
/// satisfied by a rope that simply stopped working.
/// </remarks>
public sealed class TileArrivalRopeTests
{
    /// <summary>Chapter 1's stage-1 last spine index: its stage lengths are 12/14/16.</summary>
    private const int StageOneLast = 11;

    /// <summary>
    /// The whole chain, through a real command: a run one node short of stage 1's gate, with a rope
    /// armed, rolls — and arrives with the mini-boss pending and the rope still in hand.
    /// </summary>
    /// <remarks>
    /// Driven through <c>GameRules.Apply</c> rather than through <see cref="TileArrival"/> directly,
    /// because the claim is about a player's run: the generator putting a mini-boss on stage 1's last
    /// node, the stage-end clamp stopping there, and the rope declining to fire all have to hold at
    /// once. Any pip lands on that node from one short — a 1 exactly, anything larger clamped — so
    /// the case does not depend on which face the dice stream draws.
    /// </remarks>
    [Fact]
    public void Rolling_into_stage_ones_gate_with_a_rope_armed_leaves_the_MiniBoss_pending()
    {
        var board = BoardGenerator.GenerateBoard(
            ChapterBoardTuning.Read(TileWorlds.Context.Content, chapterId: 1),
            DeterministicRng.OpenAt(TileWorlds.Seed, RngStreams.Board, 0));

        board.Node(board.SpineNode(StageOneLast)).Tile.ShouldBe(
            TileKind.MiniBoss,
            "the premise: stage 1's last node is the gate. Without it this case measures an " +
            "ordinary tile, and the rope is supposed to skip those.");

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(RunSnapshots.With(
                chapterId: 1,
                runSeed: TileWorlds.Seed,
                position: board.SpineNode(StageOneLast - 1).Value,
                escapeRopeArmed: true)),
            new RollDiceCommand(),
            TileWorlds.Context);

        result.Accepted.ShouldBeTrue();

        var run = result.NewState.Run!;

        run.Position.ShouldBe(
            board.SpineNode(StageOneLast).Value,
            "the premise: any roll from one node short comes to rest on stage 1's last node.");
        // Asked before the value: PendingTileKindValue throws when nothing is pending, and "the rope
        // fired" is exactly that state — so reading it first would report an accessor's refusal
        // instead of the rule that was broken.
        run.HasPendingTile.ShouldBeTrue(
            "the run arrived on the gate with a rope armed and nothing is pending, so the rope fired " +
            "and ate the fight. 'Those places can not be skipped' is the requirement, and a " +
            "consumable that skips the fight standing on one skips the place.");
        run.PendingTileKindValue.ShouldBe(
            (int)TileKind.MiniBoss, "the gate's own fight is what is pending, not some other tile.");
        run.EscapeRopeArmed.ShouldBeTrue(
            "the rope does not FIRE here, so it is not consumed either — it stays in hand for a " +
            "landing it is allowed to skip. A rope spent for nothing is a worse bug than a rope " +
            "that skipped.");
    }

    /// <summary>
    /// The rule at the seam, over every kind the board makes mandatory: the rope declines, the tile
    /// is pending, and the rope is still armed.
    /// </summary>
    [Theory]
    [InlineData(TileKind.MiniBoss)]
    [InlineData(TileKind.Boss)]
    public void An_armed_rope_does_not_fire_on_a_fight_the_board_makes_mandatory(TileKind tile)
    {
        var run = ArmedRun();

        TileArrival.Land(run, Node(tile)).ShouldBeFalse(
            $"a landing on {tile} is one a roll cannot be carried past, so the rope may not carry " +
            "the run past its fight either.");

        run.HasPendingTile.ShouldBeTrue();
        run.PendingTileKindValue.ShouldBe((int)tile);
        run.EscapeRopeArmed.ShouldBeTrue("a rope that did not fire was not consumed.");
    }

    /// <summary>
    /// The control, and it is what makes the case above a claim about mandatory fights rather than
    /// about a rope that stopped working. Every other kind is still skipped, Elite included — an
    /// Elite is a hard fight the board lets a run roll straight past, which is exactly what the rope
    /// is for.
    /// </summary>
    [Theory]
    [InlineData(TileKind.Enemy)]
    [InlineData(TileKind.Elite)]
    [InlineData(TileKind.Curse)]
    [InlineData(TileKind.Shrine)]
    [InlineData(TileKind.Treasure)]
    [InlineData(TileKind.Empty)]
    public void An_armed_rope_still_fires_on_every_other_kind(TileKind tile)
    {
        var run = ArmedRun();

        TileArrival.Land(run, Node(tile)).ShouldBeTrue(
            $"{tile} is a landing a roll can be carried past, so the rope is allowed to skip it.");

        run.HasPendingTile.ShouldBeFalse("a skipped tile is not pending — nothing resolves it.");
        run.EscapeRopeArmed.ShouldBeFalse("a rope is consumed when it fires.");
    }

    private static RunAggregate ArmedRun() =>
        RunAggregate.Rehydrate(RunSnapshots.With(escapeRopeArmed: true)).Value;

    /// <summary>
    /// A hand-made node, because the claim is about the tile a landing carries and not about where
    /// the generator puts one — the case above owns that half.
    /// </summary>
    private static BoardNode Node(TileKind tile) =>
        new(new NodeId(0), tile, LinearIndex: 4, Stage: 1);
}
