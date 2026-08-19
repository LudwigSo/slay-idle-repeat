using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// The stream registry: a system that needs randomness draws from one of these streams or gets a
/// new row here. A stream name is a compile-time constant rather than a string literal scattered
/// through call sites — a typo in a literal would silently open a different, valid sequence
/// instead of failing.
/// </summary>
public sealed class RngStreamsTests
{
    [Fact]
    public void The_registry_holds_exactly_the_ten_fixed_streams_of_the_specification()
    {
        RngStreams.FixedNames.ShouldBe(new[]
        {
            "board", "dice", "draft", "drops", "treasure", "shrine", "combat", "events", "forge",
            "shop",
        });
    }

    /// <summary>Each constant carries the exact wire name — these strings reach the client.</summary>
    [Fact]
    public void Each_stream_constant_carries_its_specified_wire_name()
    {
        RngStreams.Board.ShouldBe("board");
        RngStreams.Dice.ShouldBe("dice");
        RngStreams.Draft.ShouldBe("draft");
        RngStreams.Drops.ShouldBe("drops");
        RngStreams.Treasure.ShouldBe("treasure");
        RngStreams.Shrine.ShouldBe("shrine");
        RngStreams.Combat.ShouldBe("combat");
        RngStreams.Events.ShouldBe("events");
        RngStreams.Forge.ShouldBe("forge");
        RngStreams.Shop.ShouldBe("shop");
    }

    /// <summary>The eleventh row is parameterised: <c>minigame:{index}</c>.</summary>
    [Theory]
    [InlineData(0, "minigame:0")]
    [InlineData(3, "minigame:3")]
    [InlineData(42, "minigame:42")]
    public void Minigame_builds_the_parameterised_stream_name(int index, string expected)
    {
        RngStreams.Minigame(index).ShouldBe(expected);
    }

    /// <summary>A negative minigame index is not a row in the registry.</summary>
    [Fact]
    public void Minigame_rejects_a_negative_index()
    {
        var act = () => RngStreams.Minigame(-1);

        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [MemberData(nameof(FixedStreamNames))]
    public void IsRegistered_accepts_every_fixed_stream_name(string streamName)
    {
        RngStreams.IsRegistered(streamName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("minigame:0")]
    [InlineData("minigame:7")]
    [InlineData("minigame:2147483647")]
    public void IsRegistered_accepts_a_parameterised_minigame_stream(string streamName)
    {
        RngStreams.IsRegistered(streamName).ShouldBeTrue();
    }

    /// <summary>
    /// The typos that must not pass. <c>minigame:03</c> is the sharpest one: it differs from
    /// <c>minigame:3</c> and would be a different sequence for the "same" minigame.
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
        RngStreams.IsRegistered(streamName).ShouldBeFalse();
    }

    [Fact]
    public void IsRegistered_rejects_null()
    {
        RngStreams.IsRegistered(null!).ShouldBeFalse();
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

        produced.ShouldNotBeEmpty();
        foreach (var name in produced)
        {
            RngStreams.IsRegistered(name).ShouldBeTrue($"the registry produced '{name}' but does not accept it");
        }
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
