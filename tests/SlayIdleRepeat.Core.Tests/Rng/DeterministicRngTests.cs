using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// The counter-based stream. Draw <c>i</c> of stream <c>s</c> over seed <c>r</c> is
/// <c>Hash64(r, s, i)</c>; <c>Position</c> is the next draw index and the entire persistable state.
/// </summary>
/// <remarks>
/// The load-bearing property is one call, one draw index: rehydrating is
/// <c>new DeterministicRng(runSeed, name, position)</c> and nothing else.
/// </remarks>
public sealed class DeterministicRngTests
{
    private const ulong RunSeed = 0x0123456789ABCDEFUL;

    [Fact]
    public void Position_starts_at_zero_when_no_position_is_given()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Dice);

        rng.Position.ShouldBe(0UL);
    }

    /// <summary>
    /// The identity the rest of the model rests on: draw <c>i</c> of stream <c>s</c> over seed
    /// <c>r</c> is <c>Hash64(r, s, i)</c>, not another function of the three and not a generator
    /// stepped <c>i</c> times. The accessor tests pin against the accessor columns; this joins them
    /// to the <c>draw</c> column those came from.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void A_draw_is_Hash64_of_the_seed_the_stream_name_and_the_draw_index(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);

        Hash64.Of(row.Seed, row.Stream, row.Position).ShouldBe(row.Draw);
        row.NextUInt.ShouldBe((uint)(row.Draw >> 32));
    }

    /// <summary>The top 32 bits of the draw, pinned against the committed table.</summary>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void NextUInt_returns_the_committed_draw_value(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        rng.NextUInt().ShouldBe(row.NextUInt);
    }

    /// <summary>
    /// <c>(draw &gt;&gt; 11) * 2^-53</c>, asserted on the exact IEEE-754 bits. The construction is
    /// exact in binary floating point — a 53-bit integer times a power of two — so a tolerance
    /// here could only hide a real divergence.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void NextDouble_returns_the_committed_draw_value_bit_for_bit(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        BitConverter.DoubleToUInt64Bits(rng.NextDouble()).ShouldBe(row.NextDoubleBits);
    }

    /// <summary>
    /// <c>NextDouble</c> is a draw, not an accumulation point — the 4-decimal rounding rule applies
    /// to combat accumulation, not here, so rounding here would throw away 49 bits of every draw.
    /// </summary>
    [Fact]
    public void NextDouble_is_not_rounded_to_four_decimal_places()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Drops, 99UL);

        var value = rng.NextDouble();

        value.ShouldNotBe(Math.Round(value, 4));
    }

    /// <summary>[0,1) — never 1.0, or a weighted pick could fall off the end of its table.</summary>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void NextDouble_stays_inside_the_half_open_unit_interval(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        var draw = rng.NextDouble();

        draw.ShouldBeGreaterThanOrEqualTo(0.0);
        draw.ShouldBeLessThan(1.0);
    }

    /// <summary>
    /// <c>min + (int)(draw % (ulong)(max - min))</c>, pinned against the committed table. The
    /// modulo bias is accepted deliberately: rejection sampling would consume a variable number of
    /// draws and destroy the one-call-one-index property.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrawIds))]
    public void Range_returns_the_committed_draw_value(string rowId)
    {
        var row = ReferenceVectors.DrawRow(rowId);
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        rng.Range(row.RangeMinInclusive, row.RangeMaxExclusive).ShouldBe(row.Range);
    }

    /// <summary>A one-value range still costs a draw, and still returns its one value.</summary>
    [Fact]
    public void Range_returns_the_only_value_of_a_single_value_range()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Board);

        rng.Range(7, 8).ShouldBe(7);
    }

    /// <summary>
    /// The widest range an <c>int</c> has: <c>max − min</c> is <c>2^32 − 1</c>, which does not fit
    /// in an <c>int</c>, so the width must be computed in 64 bits. Pinned to the exact value,
    /// because bounds alone prove nothing: an overflowing computation would still return an answer
    /// that is still an <c>int</c> and still in range.
    /// </summary>
    [Fact]
    public void Range_spans_the_full_int_range_without_overflowing_its_width()
    {
        var row = ReferenceVectors.DrawRow("drops-99");
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        var value = rng.Range(int.MinValue, int.MaxValue);

        value.ShouldBe(-396853717);
    }

    /// <summary>A negative lower bound is ordinary; the modulo must not fold it away.</summary>
    [Fact]
    public void Range_honours_a_negative_lower_bound()
    {
        var row = ReferenceVectors.DrawRow("drops-99");
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        row.RangeMinInclusive.ShouldBeNegative();
        var value = rng.Range(row.RangeMinInclusive, row.RangeMaxExclusive);

        value.ShouldBe(row.Range);
        value.ShouldBeGreaterThanOrEqualTo(row.RangeMinInclusive);
        value.ShouldBeLessThan(row.RangeMaxExclusive);
    }

    /// <summary>An empty or inverted range has no value to return; that is a caller bug.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(5, 4)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void Range_rejects_an_empty_or_inverted_range(int minInclusive, int maxExclusive)
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Board);

        Action act = () => _ = rng.Range(minInclusive, maxExclusive);

        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    /// <summary>The property the whole persistence model rests on: every call consumes exactly one draw index.</summary>
    [Fact]
    public void Every_accessor_advances_the_position_by_exactly_one()
    {
        var table = new[] { ("a", 1.0), ("b", 1.0) };

        AdvanceOf(rng => rng.NextUInt()).ShouldBe(1UL);
        AdvanceOf(rng => rng.NextDouble()).ShouldBe(1UL);
        AdvanceOf(rng => rng.Range(0, 10)).ShouldBe(1UL);
        AdvanceOf(rng => rng.WeightedPick(table)).ShouldBe(1UL);
    }

    /// <summary>The counter continues from where it was rehydrated, not from zero.</summary>
    [Fact]
    public void Position_continues_from_the_rehydrated_position()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Dice, 12UL);

        rng.NextUInt();
        rng.NextUInt();

        rng.Position.ShouldBe(14UL);
    }

    /// <summary>
    /// Random access: rehydrating at position 5 gives the sixth value of a stream started at zero,
    /// so any draw can be re-derived without replaying the ones before it.
    /// </summary>
    [Fact]
    public void A_stream_rehydrated_at_a_position_yields_the_draw_made_there()
    {
        var byReplay = Sequence(RngStreams.Drops, 6);

        var sixthByRandomAccess = new DeterministicRng(RunSeed, RngStreams.Drops, 5UL).NextUInt();

        sixthByRandomAccess.ShouldBe(byReplay[5]);
    }

    /// <summary>Rehydration is construction: two instances at the same position agree forever after.</summary>
    [Fact]
    public void Two_streams_at_the_same_position_produce_the_same_sequence()
    {
        var first = new DeterministicRng(RunSeed, RngStreams.Draft, 3UL);
        var second = new DeterministicRng(RunSeed, RngStreams.Draft, 3UL);

        var fromFirst = new[] { first.NextUInt(), first.NextUInt(), first.NextUInt() };
        var fromSecond = new[] { second.NextUInt(), second.NextUInt(), second.NextUInt() };

        fromFirst.ShouldBe(fromSecond);
    }

    /// <summary>Two streams over the same seed are different sequences, not the same one relabelled.</summary>
    [Fact]
    public void Different_streams_over_the_same_seed_produce_different_sequences()
    {
        var dice = new DeterministicRng(RunSeed, RngStreams.Dice);
        var board = new DeterministicRng(RunSeed, RngStreams.Board);

        var fromDice = new[] { dice.NextUInt(), dice.NextUInt(), dice.NextUInt() };
        var fromBoard = new[] { board.NextUInt(), board.NextUInt(), board.NextUInt() };

        fromDice.SequenceEqual(fromBoard).ShouldBeFalse();
    }

    [Fact]
    public void The_same_stream_over_different_seeds_produces_different_sequences()
    {
        var first = new DeterministicRng(RunSeed, RngStreams.Dice);
        var second = new DeterministicRng(RunSeed + 1, RngStreams.Dice);

        first.NextUInt().ShouldNotBe(second.NextUInt());
    }

    /// <summary>
    /// Interleaving two streams changes neither. A stateful generator with a shared or cached
    /// internal buffer would fail this; a pure function of <c>(seed, stream, index)</c> cannot.
    /// </summary>
    [Fact]
    public void Interleaving_two_streams_changes_neither_sequence()
    {
        var expectedDice = Sequence(RngStreams.Dice, 4);
        var expectedBoard = Sequence(RngStreams.Board, 4);

        var dice = new DeterministicRng(RunSeed, RngStreams.Dice);
        var board = new DeterministicRng(RunSeed, RngStreams.Board);

        var interleaved = Enumerable.Range(0, 4)
            .Select(_ => (FromDice: dice.NextUInt(), FromBoard: board.NextUInt()))
            .ToArray();

        interleaved.Select(pair => pair.FromDice).ShouldBe(expectedDice);
        interleaved.Select(pair => pair.FromBoard).ShouldBe(expectedBoard);
    }

    /// <summary>
    /// The final index is reserved: a stream at <c>ulong.MaxValue</c> refuses to draw rather than
    /// drawing once with nowhere to put the next position. Wrapping silently would restart a
    /// stream at draw 0 while the wire still reported a huge position.
    /// </summary>
    [Fact]
    public void A_stream_at_the_reserved_final_index_refuses_to_draw_rather_than_wrapping()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Shrine, ulong.MaxValue);

        Action act = () => _ = rng.NextUInt();

        Should.Throw<InvalidOperationException>(act);
        rng.Position.ShouldBe(ulong.MaxValue);
    }

    /// <summary>The index below the reserved one is ordinary and draws normally.</summary>
    [Fact]
    public void A_stream_one_below_the_reserved_final_index_still_draws()
    {
        var row = ReferenceVectors.DrawRow("shrine-near-max");
        var rng = new DeterministicRng(row.Seed, row.Stream, row.Position);

        rng.NextUInt().ShouldBe(row.NextUInt);
        rng.Position.ShouldBe(ulong.MaxValue);
    }

    /// <summary>
    /// A rejected call is not a draw: <c>Position</c> counts calls actually made, and an argument
    /// the method refused to act on made none — otherwise a caller's validation bug would silently
    /// desynchronise the persisted counter from the sequence it indexes.
    /// </summary>
    [Fact]
    public void A_rejected_call_consumes_no_draw_index()
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Dice, 12UL);

        Action invertedRange = () => _ = rng.Range(5, 4);
        Action emptyTable = () => _ = rng.WeightedPick(Array.Empty<(string, double)>());

        Should.Throw<ArgumentOutOfRangeException>(invertedRange);
        Should.Throw<ArgumentException>(emptyTable);
        rng.Position.ShouldBe(12UL);
    }

    /// <summary>A stream-name typo must be a caught invariant, not a silently different sequence.</summary>
    [Theory]
    [InlineData("dcie")]
    [InlineData("Dice")]
    [InlineData("dice ")]
    [InlineData("combat:0")]
    [InlineData("")]
    public void The_constructor_rejects_a_stream_name_that_is_not_in_the_registry(string streamName)
    {
        Action act = () => _ = new DeterministicRng(RunSeed, streamName);

        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void The_constructor_rejects_a_null_stream_name()
    {
        Action act = () => _ = new DeterministicRng(RunSeed, null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>Every registered stream name is constructible — the registry and the guard agree.</summary>
    [Theory]
    [MemberData(nameof(FixedStreamNames))]
    public void The_constructor_accepts_every_registered_stream_name(string streamName)
    {
        Action act = () => _ = new DeterministicRng(RunSeed, streamName);

        Should.NotThrow(act);
    }

    [Fact]
    public void The_constructor_accepts_a_parameterised_minigame_stream()
    {
        Action act = () => _ = new DeterministicRng(RunSeed, RngStreams.Minigame(3));

        Should.NotThrow(act);
    }

    private static ulong AdvanceOf(Action<DeterministicRng> call)
    {
        var rng = new DeterministicRng(RunSeed, RngStreams.Dice, 100UL);

        call(rng);

        return rng.Position - 100UL;
    }

    private static IReadOnlyList<uint> Sequence(string streamName, int count)
    {
        var rng = new DeterministicRng(RunSeed, streamName);
        var values = new List<uint>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(rng.NextUInt());
        }

        return values;
    }

    public static TheoryData<string> DrawIds() => ReferenceVectors.DrawIds();

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
