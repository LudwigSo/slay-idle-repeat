using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// Which minigame a tile offers: a deterministic pick over the run seed and the tile's own index.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The pick is client-side and unenforced — that is documented at the seam and is not what
/// these cases are about.</b> What they are about is the half that still matters: an honest client
/// has to offer the SAME game every time the same tile is opened. A pick that re-rolled on resume
/// would let a player reload until the arm they wanted came up, turning an unenforced choice into an
/// enforced advantage.
/// </para>
/// <para>
/// 🔒 <b>The seeds are fixed.</b> A determinism case over a random seed is a case that passes on
/// whatever it happened to draw, and the distribution assertion below would be flaky rather than
/// wrong.
/// </para>
/// </remarks>
public sealed class MinigameChoiceTests
{
    /// <summary>The run seed every case picks against unless it is about a different one.</summary>
    private const ulong Seed = 0x5EED_1D1EUL;

    /// <summary>A second seed, so "the same answer for every run" cannot pass as determinism.</summary>
    private const ulong OtherSeed = 0xB0A4_D1CEUL;

    /// <summary>How many tiles the distribution case sweeps, so a sweep of none cannot pass.</summary>
    private const int TilesSwept = 200;

    /// <summary>
    /// 🔒 The same tile of the same run always offers the same game.
    /// </summary>
    /// <remarks>
    /// 🔴 The resume case. The board reopens a pending Minigame tile through the same routing table
    /// it opened it with, so a pick that answered differently the second time would replace the game
    /// under a player who had already started it.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(43)]
    public void The_same_tile_of_the_same_run_always_offers_the_same_game(int tile)
    {
        var first = MinigameChoice.For(Seed, tile, MinigameArms.Built);
        var second = MinigameChoice.For(Seed, tile, MinigameArms.Built);

        second.ShouldBe(
            first,
            "tile " + tile + " offered '" + first + "' and then '" + second + "'. The board reopens " +
            "a pending Minigame tile through the same routing table, so a pick that re-rolled would " +
            "replace the game under a player who had already begun it — and would let a reload " +
            "shop for the most generous arm.");
    }

    /// <summary>…and the answer is always one of the arms it was handed.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(43)]
    public void The_game_offered_is_one_of_the_arms_the_client_can_draw(int tile) =>
        MinigameArms.Built.ShouldContain(
            MinigameChoice.For(Seed, tile, MinigameArms.Built),
            "the pick answered with an arm outside the list it was handed, so a tile can open on a " +
            "game this client has no screen for.");

    /// <summary>
    /// 🔒 <b>Two different runs do not walk the same board of minigames.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control for determinism. A pick that ignored the seed entirely — one that
    /// answered off the tile index alone — satisfies every stability case above and gives every
    /// player in the game the identical sequence of minigames.
    /// </remarks>
    [Fact]
    public void Two_runs_do_not_offer_the_identical_sequence_of_games()
    {
        var mine = Sequence(Seed);
        var theirs = Sequence(OtherSeed);

        mine.Length.ShouldBe(
            TilesSwept, "with no tiles swept the comparison below holds over two empty sequences.");
        mine.ShouldNotBe(
            theirs,
            "two different run seeds produced the identical sequence of " + TilesSwept + " games, " +
            "so the seed is not reaching the pick at all and every player meets the same minigames " +
            "in the same order.");
    }

    /// <summary>
    /// 🔒 <b>Every built arm really comes up.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 A pick that always answered with the first arm satisfies determinism, membership and — with
    /// two different seeds — nothing here would notice, because the sequences would still differ if
    /// the seed changed anything at all. Over two hundred tiles each of three arms appearing is not a
    /// distribution claim; it is the claim that the other two are reachable.
    /// </remarks>
    [Fact]
    public void Every_built_arm_is_reachable()
    {
        var offered = Sequence(Seed).Distinct(StringComparer.Ordinal).ToArray();

        foreach (var arm in MinigameArms.Built)
        {
            offered.ShouldContain(
                arm,
                "'" + arm + "' never came up across " + TilesSwept + " tiles of one run, so it is " +
                "either unreachable or so rare that a player will not meet it — and a screen nobody " +
                "opens is a screen nobody tests.");
        }
    }

    [Fact]
    public void A_null_arm_list_is_refused() =>
        Should.Throw<ArgumentNullException>(() => MinigameChoice.For(Seed, 0, null!));

    /// <summary>
    /// An empty arm list is refused rather than answered.
    /// </summary>
    /// <remarks>
    /// 🔒 There is no honest answer: a pick over nothing would have to invent an id or return an
    /// empty string, and both reach <c>MINIGAME_SUBMIT</c> as an unknown minigame — a refusal the
    /// player sees as the game having broken rather than as a client with no screens.
    /// </remarks>
    [Fact]
    public void An_empty_arm_list_is_refused() =>
        Should.Throw<ArgumentException>(() => MinigameChoice.For(Seed, 0, []));

    private static string[] Sequence(ulong seed) =>
        Enumerable.Range(0, TilesSwept)
                  .Select(tile => MinigameChoice.For(seed, tile, MinigameArms.Built))
                  .ToArray();
}
