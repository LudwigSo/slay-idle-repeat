using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The per-stream draw counters the <c>Run</c> aggregate holds: an open map validated by
/// <c>RngStreams.IsRegistered</c> (the registry's last slot is parameterised), with one seam,
/// no partial commit, and monotone-or-throw.
/// </summary>
public sealed class RunRngStreamTests
{
    private static Run WithStreams(params (string Stream, ulong Position)[] streams) =>
        Run.Rehydrate(RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(streams))).Value;

    /// <summary>
    /// Sparse on purpose: eager zeros for every stream would put dead bytes in every
    /// <c>stateHash</c>, and <c>new DeterministicRng(seed, name)</c> genuinely starts at draw 0.
    /// </summary>
    [Fact]
    public void An_undrawn_stream_stands_at_zero_and_is_not_stored()
    {
        var run = WithStreams((RngStreams.Dice, 12UL));

        run.StreamPosition(RngStreams.Drops).ShouldBe(0UL);
        run.RngStreamPositions.Keys.ShouldBe(new[] { RngStreams.Dice });
    }

    /// <summary>
    /// Answering zero would make a typo indistinguishable from a real, undrawn stream — it does
    /// not fail, it silently opens a different, perfectly valid sequence.
    /// </summary>
    [Theory]
    [InlineData("loot")]
    [InlineData("DICE")]
    [InlineData("minigame:03")]
    [InlineData("")]
    public void StreamPosition_refuses_a_name_the_registry_does_not_recognise(string streamName)
    {
        Should.Throw<ArgumentException>(() => WithStreams().StreamPosition(streamName))
              .Message.ShouldMatchWildcard("*14 §8.1*");
    }

    [Fact]
    public void StreamPosition_refuses_a_null_name()
    {
        Should.Throw<ArgumentNullException>(() => WithStreams().StreamPosition(null!));
    }

    [Fact]
    public void CommitStreamPositions_replaces_the_whole_map()
    {
        var run = WithStreams((RngStreams.Dice, 12UL), (RngStreams.Board, 8UL));

        run.CommitStreamPositions(new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [RngStreams.Dice] = 13,
            [RngStreams.Board] = 8,
            [RngStreams.Drops] = 2,
        });

        run.StreamPosition(RngStreams.Dice).ShouldBe(13UL);
        run.StreamPosition(RngStreams.Board).ShouldBe(8UL);
        run.StreamPosition(RngStreams.Drops).ShouldBe(2UL);
    }

    /// <summary>
    /// A throw and deliberately not a <c>RejectionReason</c>: a rejection hands a corrupt scope
    /// back to the player as a polite "no" and leaves the run in it.
    /// </summary>
    [Fact]
    public void A_stream_position_that_moves_backwards_is_a_determinism_defect_and_throws()
    {
        var run = WithStreams((RngStreams.Dice, 12UL));

        var act = () => run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Dice] = 11 });

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*dice*12*11*");

        run.StreamPosition(RngStreams.Dice).ShouldBe(12UL, "a refused commit changes nothing");
    }

    [Fact]
    public void A_stream_position_that_did_not_move_is_accepted()
    {
        var run = WithStreams((RngStreams.Dice, 12UL));

        run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Dice] = 12 });

        run.StreamPosition(RngStreams.Dice).ShouldBe(12UL);
    }

    /// <summary>A dropped key would silently reset that stream to 0 and replay a sequence the player already played.</summary>
    [Fact]
    public void A_commit_that_drops_an_already_committed_stream_is_refused()
    {
        var run = WithStreams((RngStreams.Dice, 12UL), (RngStreams.Board, 8UL));

        var act = () => run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Dice] = 13 });

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*board*");

        run.StreamPosition(RngStreams.Board).ShouldBe(8UL, "a refused commit changes nothing");
        run.StreamPosition(RngStreams.Dice).ShouldBe(12UL);
    }

    [Theory]
    [InlineData("loot")]
    [InlineData("DICE")]
    [InlineData("minigame:03")]
    public void A_commit_carrying_an_unregistered_stream_name_is_refused(string streamName)
    {
        var run = WithStreams();

        var act = () => run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [streamName] = 1 });

        var message = Should.Throw<ArgumentException>(act).Message;

        message.ShouldMatchWildcard("*" + streamName + "*14 §8.1*");

        // ShouldMatchWildcard is case-insensitive, and "DICE" versus "dice" is the whole point of
        // the registry being ordinal — so the key must come back exactly as persisted.
        message.ShouldContain(streamName, Case.Sensitive);

        run.RngStreamPositions.ShouldBeEmpty("a refused commit changes nothing");
    }

    [Fact]
    public void A_null_commit_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => WithStreams().CommitStreamPositions(null!));
    }

    public static TheoryData<string> EveryRegisteredStream
    {
        get
        {
            var streams = new TheoryData<string>();
            foreach (var name in RngStreams.FixedNames)
            {
                streams.Add(name);
            }

            streams.Add(RngStreams.Minigame(0));
            streams.Add(RngStreams.Minigame(11));
            return streams;
        }
    }

    /// <summary>Without this half, a validation that refused everything would satisfy the refusal cases above.</summary>
    [Theory]
    [MemberData(nameof(EveryRegisteredStream))]
    public void Every_row_of_the_registry_can_be_committed_including_a_minigame_index(string streamName)
    {
        var run = WithStreams();

        run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [streamName] = 1 });

        run.StreamPosition(streamName).ShouldBe(1UL);
    }

    [Fact]
    public void A_map_the_caller_still_holds_cannot_rewrite_the_committed_positions()
    {
        var run = WithStreams();
        var handed = new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Dice] = 4 };

        run.CommitStreamPositions(handed);

        handed[RngStreams.Dice] = 0;
        handed[RngStreams.Board] = 77;

        run.StreamPosition(RngStreams.Dice).ShouldBe(4UL);
        run.StreamPosition(RngStreams.Board).ShouldBe(0UL);
    }

    [Fact]
    public void The_exposed_position_map_cannot_be_mutated_through_its_reference()
    {
        var run = WithStreams((RngStreams.Dice, 4UL));
        var exposed = run.RngStreamPositions;

        Should.Throw<NotSupportedException>(
            () => ((IDictionary<string, ulong>)exposed)[RngStreams.Dice] = 999);

        run.StreamPosition(RngStreams.Dice).ShouldBe(4UL);
    }

    /// <summary>
    /// Under <see cref="StringComparer.OrdinalIgnoreCase"/> the key <c>DICE</c> IS <c>dice</c>, so
    /// a run that adopted the caller's comparer would answer 4 for a stream that does not exist —
    /// and round-trip to a different hash than the one it was stored under.
    /// </summary>
    [Fact]
    public void The_committed_keys_are_compared_ordinally_whatever_comparer_the_caller_used()
    {
        var run = WithStreams();
        var caseInsensitive = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            [RngStreams.Dice] = 4,
        };

        run.CommitStreamPositions(caseInsensitive);

        run.RngStreamPositions.ContainsKey("DICE").ShouldBeFalse(
            "the aggregate copies into an ordinal dictionary rather than adopting the caller's comparer");
        run.StreamPosition(RngStreams.Dice).ShouldBe(4UL);
    }
}
