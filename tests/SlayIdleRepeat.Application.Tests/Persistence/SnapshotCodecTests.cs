using System.Text;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Persistence;

/// <summary>
/// The codec round-trip, compared by canonical hash rather than by record equality — these rows carry
/// dictionaries, which compare by reference.
/// </summary>
public sealed class SnapshotCodecTests
{
    /// <summary>The lowest snapshot layout this build is written against.</summary>
    /// <remarks>A floor rather than the exact number, so a later migration bumping it does not fail this case.</remarks>
    private const int SchemaFloor = 13;

    [Fact]
    public void DecodeSlice_returns_a_player_row_identical_to_the_one_EncodeSlice_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        Worlds.Hash(decoded).ShouldBe(
            Worlds.Hash(original),
            "a field lost, reordered or re-typed by the codec changes the canonical bytes, and a player " +
            "would come back from storage missing whatever it was.");
    }

    /// <summary>
    /// The discriminating half of the round-trip: the run carries the shapes a naive codec drops — a
    /// string-keyed counter map, an int-keyed map, and optional integers.
    /// </summary>
    [Fact]
    public void DecodeSlice_returns_a_run_row_carrying_the_draw_counters_it_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));

        original.Run!.RngStreamPositions.ShouldNotBeEmpty(
            "this case exists to round-trip a populated counter map; an empty one would round-trip " +
            "through a codec that dropped maps entirely.");

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        decoded.Run.ShouldNotBeNull("the run was dropped by the round-trip.");
        Worlds.RunHash(original.Player, decoded.Run!).ShouldBe(
            Worlds.RunHash(original.Player, original.Run),
            "the run came back differing from the one stored; the same player row is hashed on both " +
            "sides, so the run is the only thing that can have moved.");
    }

    [Fact]
    public void DecodeSlice_preserves_the_absence_of_a_run()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var original = Worlds.Stored(game.State(player));

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        decoded.Run.ShouldBeNull(
            "a player outside a run must not come back holding one — an invented empty run would be a " +
            "run the domain then refuses to start over.");
    }

    [Fact]
    public void DecodeRun_returns_a_run_identical_to_the_one_EncodeRun_was_given()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var slice = game.State(player);
        var original = slice.Run!.ToSnapshot();

        var decoded = SnapshotCodec.DecodeRun(SnapshotCodec.EncodeRun(original));

        Worlds.RunHash(slice.Player.ToSnapshot(), decoded).ShouldBe(
            Worlds.RunHash(slice.Player.ToSnapshot(), original),
            "the archived copy of a finished run is the only record of it left once the next run " +
            "starts, so a field lost here is lost for good.");
    }

    [Fact]
    public void DecodeSlice_preserves_the_layout_version_the_row_was_written_at()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var original = Worlds.Stored(game.State(player));

        var decoded = SnapshotCodec.DecodeSlice(SnapshotCodec.EncodeSlice(original));

        decoded.Player.SchemaVersion.ShouldBe(
            original.Player.SchemaVersion,
            "the version a row was written at is what the aggregate's own loader checks; rewriting it " +
            "on the way through would make an unreadable row look readable.");

        decoded.Player.SchemaVersion.ShouldBeGreaterThanOrEqualTo(
            SchemaFloor,
            "the fixture is writing rows at a layout older than this build has ever shipped, so the " +
            "round-trip above is not exercising the current snapshot shape.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void DecodeSlice_refuses_bytes_that_are_not_a_stored_slice(string text)
    {
        Should.Throw<JsonException>(() => SnapshotCodec.DecodeSlice(Encoding.UTF8.GetBytes(text)))
            .Message.ShouldNotBeEmpty(
                "a row that cannot be read is not a player who has nothing: answering with a blank or " +
                "half-filled state would hand the player a fresh account and overwrite the real one on " +
                "the next command.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    public void DecodeRun_refuses_bytes_that_are_not_a_run_row(string text)
    {
        Should.Throw<JsonException>(() => SnapshotCodec.DecodeRun(Encoding.UTF8.GetBytes(text)))
            .Message.ShouldNotBeEmpty("an unreadable archive row is an error, not an absent run.");
    }
}
