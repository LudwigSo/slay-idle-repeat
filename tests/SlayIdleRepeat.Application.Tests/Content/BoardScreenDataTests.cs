using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// <c>content/board/board.json</c>'s tile-caption group — the authored half of the tile vocabulary
/// the Board screen renders.
/// </summary>
public sealed class BoardScreenDataTests
{
    private const string BoardDocument = "content/board/board.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    /// <summary>
    /// The screen names the mini-boss kind. The run reports the tile it stands on as a bare integer
    /// and the client's name table is indexed by it, so a kind with no caption authored here is a
    /// tile the board can only draw as a number — and a mini-boss is the one node a player may not
    /// walk past, so an unnamed one is an unexplained stop.
    /// </summary>
    [Fact]
    public void The_tile_captions_name_the_mini_boss_kind()
    {
        var data = Data();

        data.ReadText($"{BoardDocument}#/tile/miniBoss").ShouldBe("loc.tile.miniboss.name");
        data.ReadText("loc/en.json#/strings/loc.tile.miniboss.name").ShouldNotBeNullOrWhiteSpace();
        data.ReadText("loc/de.json#/strings/loc.tile.miniboss.name").ShouldNotBeNullOrWhiteSpace();
    }
}
