using FluentAssertions;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 `14` §8.1 — the stream registry. "This table is the complete stream registry — a system
/// that needs randomness draws from one of these streams or gets a new row here."
/// </summary>
/// <remarks>
/// The registry exists so a stream name is a compile-time constant rather than a string literal
/// scattered through forty call sites. A typo in a literal does not fail — it silently opens a
/// <i>different, perfectly valid</i> sequence, and the run it corrupts is unreproducible by
/// definition.
/// </remarks>
public sealed class RngStreamsTests
{
    /// <summary>
    /// The eight fixed rows of `14` §8.1, spelled out. If this test has to change, the design
    /// document changed with it — that is the point of writing the table twice.
    /// </summary>
    [Fact]
    public void The_registry_holds_exactly_the_eight_fixed_streams_of_the_specification()
    {
        RngStreams.FixedNames.Should().Equal(
            "board", "dice", "draft", "drops", "treasure", "shrine", "combat", "events");
    }

    /// <summary>Each constant carries the exact wire name — these strings reach the client.</summary>
    [Fact]
    public void Each_stream_constant_carries_its_specified_wire_name()
    {
        RngStreams.Board.Should().Be("board");
        RngStreams.Dice.Should().Be("dice");
        RngStreams.Draft.Should().Be("draft");
        RngStreams.Drops.Should().Be("drops");
        RngStreams.Treasure.Should().Be("treasure");
        RngStreams.Shrine.Should().Be("shrine");
        RngStreams.Combat.Should().Be("combat");
        RngStreams.Events.Should().Be("events");
    }

    /// <summary>The ninth row is parameterised: <c>minigame:{index}</c>.</summary>
    [Theory]
    [InlineData(0, "minigame:0")]
    [InlineData(3, "minigame:3")]
    [InlineData(42, "minigame:42")]
    public void Minigame_builds_the_parameterised_stream_name(int index, string expected)
    {
        RngStreams.Minigame(index).Should().Be(expected);
    }

    /// <summary>A negative minigame index is not a row in the registry.</summary>
    [Fact]
    public void Minigame_rejects_a_negative_index()
    {
        var act = () => RngStreams.Minigame(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [MemberData(nameof(FixedStreamNames))]
    public void IsRegistered_accepts_every_fixed_stream_name(string streamName)
    {
        RngStreams.IsRegistered(streamName).Should().BeTrue();
    }

    [Theory]
    [InlineData("minigame:0")]
    [InlineData("minigame:7")]
    [InlineData("minigame:2147483647")]
    public void IsRegistered_accepts_a_parameterised_minigame_stream(string streamName)
    {
        RngStreams.IsRegistered(streamName).Should().BeTrue();
    }

    /// <summary>
    /// 🔒 The typos that must not pass. <c>minigame:03</c> is the sharpest one: it is a
    /// different string from <c>minigame:3</c> and would therefore be a different sequence for
    /// what every human reading it would call the same minigame.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("dcie")]
    [InlineData("Dice")]
    [InlineData("DICE")]
    [InlineData("dice ")]
    [InlineData(" dice")]
    [InlineData("combat:0")]
    [InlineData("minigame")]
    [InlineData("minigame:")]
    [InlineData("minigame:-1")]
    [InlineData("minigame:03")]
    [InlineData("minigame:x")]
    [InlineData("minigame:3:4")]
    [InlineData("minigame:99999999999999999999")]
    [InlineData("run")]
    public void IsRegistered_rejects_anything_that_is_not_a_registry_row(string streamName)
    {
        RngStreams.IsRegistered(streamName).Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_rejects_null()
    {
        RngStreams.IsRegistered(null!).Should().BeFalse();
    }

    /// <summary>
    /// Every name the registry hands out is a name it recognises. A registry whose builder and
    /// validator disagree would reject its own output — the kind of defect that only shows up
    /// on the one code path nobody exercised.
    /// </summary>
    [Fact]
    public void Every_name_the_registry_produces_is_a_name_it_accepts()
    {
        var produced = RngStreams.FixedNames.Concat(new[] { RngStreams.Minigame(0), RngStreams.Minigame(11) });

        produced.Should().AllSatisfy(name => RngStreams.IsRegistered(name).Should().BeTrue());
    }

    /// <summary>The registry names are distinct — two rows sharing a name would share a sequence.</summary>
    [Fact]
    public void The_registry_names_are_unique()
    {
        RngStreams.FixedNames.Should().OnlyHaveUniqueItems();
    }

    public static TheoryData<string> FixedStreamNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in RngStreams.FixedNames)
        {
            data.Add(name);
        }

        return data;
    }
}
