using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// <see cref="CoreBoard.FromLayout"/> — the seam a future authored-layout loader (`03` §3's
/// bypass) would call directly, without going through <see cref="BoardGenerator"/>.
/// </summary>
public sealed class BoardTests
{
    private static (BoardNode a, BoardNode b) TwoNodes() =>
        (new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1), new BoardNode(new NodeId(1), TileKind.Boss, 1, CoreBoard.BossStage));

    [Fact]
    public void A_minimal_two_node_layout_round_trips()
    {
        var (a, b) = TwoNodes();
        var edge = new BoardEdge(a.Id, b.Id, EdgeKind.Continue);

        var board = CoreBoard.FromLayout(new[] { a, b }, new[] { edge }, new[] { a.Id, b.Id }, Array.Empty<NodeId>());

        board.NodeCount.ShouldBe(2);
        board.FirstNodeId.ShouldBe(a.Id);
        board.BossNodeId.ShouldBe(b.Id);
        board.OutgoingEdges(a.Id).ShouldHaveSingleItem();
        board.OutgoingEdges(b.Id).ShouldBeEmpty();
        board.IsJunction(a.Id).ShouldBeFalse();
    }

    [Fact]
    public void An_edge_referencing_an_unknown_node_is_rejected()
    {
        var (a, b) = TwoNodes();
        var stray = new BoardEdge(a.Id, new NodeId(99), EdgeKind.Continue);

        var ex = Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a, b }, new[] { stray }, new[] { a.Id, b.Id }, Array.Empty<NodeId>()));

        ex.ParamName.ShouldBe("edges");
    }

    [Fact]
    public void A_linear_index_entry_referencing_an_unknown_node_is_rejected()
    {
        var (a, b) = TwoNodes();

        var ex = Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a, b }, Array.Empty<BoardEdge>(), new[] { a.Id, new NodeId(99) }, Array.Empty<NodeId>()));

        ex.ParamName.ShouldBe("spineByLinearIndex");
    }

    [Fact]
    public void A_junction_without_exactly_two_outgoing_edges_is_rejected()
    {
        var (a, b) = TwoNodes();
        var edge = new BoardEdge(a.Id, b.Id, EdgeKind.Continue);

        var ex = Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a, b }, new[] { edge }, new[] { a.Id, b.Id }, new[] { a.Id }));

        ex.ParamName.ShouldBe("junctionIds");
    }

    [Fact]
    public void An_empty_linear_index_is_rejected()
    {
        var (a, _) = TwoNodes();

        Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a }, Array.Empty<BoardEdge>(), Array.Empty<NodeId>(), Array.Empty<NodeId>()));
    }

    [Fact]
    public void Looking_up_an_unknown_node_throws()
    {
        var (a, b) = TwoNodes();
        var edge = new BoardEdge(a.Id, b.Id, EdgeKind.Continue);
        var board = CoreBoard.FromLayout(new[] { a, b }, new[] { edge }, new[] { a.Id, b.Id }, Array.Empty<NodeId>());

        Should.Throw<KeyNotFoundException>(() => board.Node(new NodeId(999)));
        Should.Throw<KeyNotFoundException>(() => board.OutgoingEdges(new NodeId(999)));
    }
}
