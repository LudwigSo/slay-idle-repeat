using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `14` §8.1 — the per-stream draw counters the <c>Run</c> aggregate holds, and the single seam
/// that writes them.
/// </summary>
/// <remarks>
/// <para>
/// §8.1 makes the counters <em>authoritative run state</em> beside <c>runSeed</c>: a resumed run
/// re-derives its next board, drop and draft draw from them, so a counter that reset, went backwards
/// or went missing produces a run nobody can reproduce — including the player who is in it.
/// </para>
/// <para>
/// The storage is an <b>open map validated by <c>RngStreams.IsRegistered</c></b>, because §8.1's
/// ninth row is parameterised (<c>minigame:{index}</c>) and nine fixed slots could not hold
/// <c>minigame:7</c> at all. The tests below pin the four properties that make the map safe: one
/// seam, registry-validated keys, no partial commit, and monotone or throw.
/// </para>
/// </remarks>
public sealed class RunRngStreamTests
{
    private static Run WithStreams(params (string Stream, ulong Position)[] streams) =>
        Run.Rehydrate(RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(streams))).Value;

    /// <summary>
    /// A registered stream nobody has drawn from stands at 0, and it is absent from the map rather
    /// than stored as an eager zero.
    /// </summary>
    /// <remarks>
    /// Sparse on purpose: `14` §2.3's wire echo <c>{"dice":12,"board":8}</c> shows only the streams
    /// that moved, and eight eager zeros would put dead bytes in every <c>stateHash</c>. Zero for an
    /// undrawn stream is not the S6 hole-filled-with-a-default — <c>new DeterministicRng(seed,
    /// name)</c> genuinely starts at draw 0.
    /// </remarks>
    [Fact]
    public void An_undrawn_stream_stands_at_zero_and_is_not_stored()
    {
        var run = WithStreams((RngStreams.Dice, 12UL));

        run.StreamPosition(RngStreams.Drops).ShouldBe(0UL);
        run.RngStreamPositions.Keys.ShouldBe(new[] { RngStreams.Dice });
    }

    /// <summary>
    /// 🔒 A name outside the `14` §8.1 registry is refused rather than answered with a zero — the
    /// same predicate <c>DeterministicRng</c>'s constructor uses.
    /// </summary>
    /// <remarks>
    /// Answering zero would make <c>StreamPosition("dic")</c> indistinguishable from a real, undrawn
    /// stream, which is precisely the typo <c>RngStreams</c>' constants exist to prevent: it does not
    /// fail, it silently opens a different — perfectly valid — sequence.
    /// </remarks>
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

    /// <summary>…and null is a caller bug rather than a membership question.</summary>
    [Fact]
    public void StreamPosition_refuses_a_null_name()
    {
        Should.Throw<ArgumentNullException>(() => WithStreams().StreamPosition(null!));
    }

    /// <summary>
    /// 🔒 <c>CommitStreamPositions</c> replaces the whole map: it is the one seam, and there is no
    /// per-stream setter, no settable collection and no indexer.
    /// </summary>
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
    /// 🔒 A position that goes <b>backwards</b> throws — it is a determinism defect, not a rejection.
    /// </summary>
    /// <remarks>
    /// ⚠️ An <see cref="InvalidOperationException"/> and deliberately <b>not</b> a
    /// <c>RejectionReason</c>: a rejection is the game correctly saying no to a legal request, and
    /// handing a corrupt scope back to the player as a polite "no" would leave the run in it. A
    /// counter that moved backwards means the next draw repeats a sequence the player has already
    /// played, which is the one outcome §8.1's counter model exists to prevent.
    /// </remarks>
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

    /// <summary>An unchanged position is accepted: a scope that drew nothing is not a defect.</summary>
    [Fact]
    public void A_stream_position_that_did_not_move_is_accepted()
    {
        var run = WithStreams((RngStreams.Dice, 12UL));

        run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Dice] = 12 });

        run.StreamPosition(RngStreams.Dice).ShouldBe(12UL);
    }

    /// <summary>
    /// 🔒 A <b>partial</b> map is refused: every stream already committed has to be present in the
    /// incoming one.
    /// </summary>
    /// <remarks>
    /// A dropped key would silently reset that stream to 0 and the next draw from it would repeat a
    /// sequence the player has already played. This refusal is also what makes "<c>Apply</c> folds
    /// the scope's final positions into the new <c>Run</c>" the <em>only</em> expressible call: a
    /// handler that wanted to hand-write one position would have to reconstruct the entire committed
    /// set to do it.
    /// </remarks>
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

    /// <summary>
    /// 🔒 A key the `14` §8.1 registry does not recognise is refused, naming the key and the
    /// registry.
    /// </summary>
    /// <remarks>
    /// Same predicate as <c>DeterministicRng</c>'s constructor, which is the point: a name that
    /// cannot be drawn from cannot be persisted either. <c>minigame:03</c> is included because it is
    /// the case <c>RngStreams.IsRegistered</c> exists to separate — a different string, and therefore
    /// a different sequence, for what every human reading it would call the same minigame.
    /// </remarks>
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

        // ⚠️ ShouldMatchWildcard is case-INSENSITIVE (it reproduces FluentAssertions' semantics), so
        // the pattern above is also satisfied by a message that lower-cased the offending key — and
        // "DICE" versus "dice" is the whole point of the registry being ordinal. The key has to come
        // back exactly as it was persisted, or the reader cannot find the row that carries it.
        message.ShouldContain(streamName, Case.Sensitive);

        run.RngStreamPositions.ShouldBeEmpty("a refused commit changes nothing");
    }

    /// <summary>…and a null map is a caller bug: an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_commit_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => WithStreams().CommitStreamPositions(null!));
    }

    /// <summary>
    /// Every row of the registry can be committed, including the parameterised ninth. Without this
    /// half, a validation that refused everything would satisfy the case above.
    /// </summary>
    [Fact]
    public void Every_row_of_the_registry_can_be_committed_including_a_minigame_index()
    {
        var run = WithStreams();

        var everyStream = RngStreams.FixedNames
            .Select((name, index) => (Stream: name, Position: (ulong)(index + 1)))
            .Append((Stream: RngStreams.Minigame(0), Position: 9UL))
            .Append((Stream: RngStreams.Minigame(11), Position: 10UL))
            .ToArray();

        everyStream.Length.ShouldBe(
            10,
            "eight fixed rows plus two minigame indices. A shrunken fixture would leave this case " +
            "asserting over fewer streams than 14 §8.1 has.");

        run.CommitStreamPositions(everyStream.ToDictionary(s => s.Stream, s => s.Position, StringComparer.Ordinal));

        foreach (var (stream, position) in everyStream)
        {
            run.StreamPosition(stream).ShouldBe(position);
        }
    }

    /// <summary>
    /// The map handed to <c>CommitStreamPositions</c> is <b>copied</b>: a caller that kept its
    /// dictionary cannot rewrite the run's counters afterwards.
    /// </summary>
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

    /// <summary>
    /// …and the map the aggregate hands out cannot be written through its reference either.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Apply_is_the_only_public_mutation</c> would not see this hole: that rule inspects
    /// setters, public fields, constructors and mutating methods, not exposed collections.
    /// </remarks>
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
    /// 🔒 The keys are compared <b>ordinally</b>, because <c>CanonicalStateWriter</c> orders string
    /// keys ordinally — a map comparing them any other way would round-trip to a different hash than
    /// the one it was stored under.
    /// </summary>
    /// <remarks>
    /// Driven through a deliberately case-insensitive dictionary: under
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> the key <c>DICE</c> <em>is</em> <c>dice</c>, so
    /// a run that adopted the caller's comparer would answer 4 for a stream `14` §8.1 does not have.
    /// </remarks>
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
            "the aggregate copies into an ordinal dictionary rather than adopting the caller's " +
            "comparer; CanonicalStateWriter orders string keys ordinally and the stateHash follows " +
            "that order.");
        run.StreamPosition(RngStreams.Dice).ShouldBe(4UL);
    }

    /// <summary>
    /// 🔒 <c>CommitStreamPositions</c> is the <b>one</b> seam. There is no per-stream setter, no
    /// <c>AdvanceStream</c>, no indexer.
    /// </summary>
    /// <remarks>
    /// The set is read off the type rather than transcribed, and floored so the assertion cannot pass
    /// by finding nothing (steering S3). It is the type-shape half of the design: a second write site
    /// would let a handler advance one counter and leave the rest, which is exactly the partial
    /// commit the refusal above exists to make unrepresentable.
    /// </remarks>
    [Fact]
    public void CommitStreamPositions_is_the_only_member_that_writes_a_stream_position()
    {
        var members = typeof(Run)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .ToArray();

        members.ShouldContain(
            nameof(Run.CommitStreamPositions),
            "if this is absent the reflection is looking at the wrong type and the rest of this " +
            "case is asserting over nothing.");

        members.Where(name => name.Contains("Stream", StringComparison.OrdinalIgnoreCase))
               .ShouldBe(
                   new[]
                   {
                       "_streamPositions",
                       nameof(Run.RngStreamPositions),
                       nameof(Run.StreamPosition),
                       nameof(Run.CommitStreamPositions),
                   },
                   ignoreOrder: true,
                   "one private field, one read-only view, one reader and one commit. Anything else " +
                   "named for a stream is a second write site.");
    }

    /// <summary>
    /// 🔒 The <c>combat</c> position is the <c>battleIndex</c> `14` §8.1 derives the <b>next</b>
    /// battle's seed from — <c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c> — so a run
    /// standing at 3 opens battle 3, which is none of the three it has already fought.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>What this case proves, and what it deliberately does not.</b> That the counter's
    /// <em>unit</em> is battles-started rather than combat-draws is a <b>ruling</b>, and this
    /// aggregate cannot enforce it: the position is a <c>ulong</c> and every monotone value is a
    /// legal commit, by design (a handler is what folds a scope's final positions in). Naming the
    /// case after the ruling would be a name promising what no assertion here can deliver (steering
    /// S1). What <em>is</em> checkable is asserted: the value the run holds is the index
    /// <see cref="SeedDerivation.BattleSeed"/> takes, and the battle it opens is not one already
    /// fought. The ruling itself is recorded on <c>Run.RngStreamPositions</c>'s remarks, and the
    /// case below pins its other checkable half — that a battle's own draws never reach the run.
    /// </remarks>
    [Fact]
    public void The_combat_position_is_the_battle_index_the_next_battle_seed_is_derived_from()
    {
        var run = WithStreams((RngStreams.Combat, 3UL));

        var next = SeedDerivation.BattleSeed(run.RunSeed, (int)run.StreamPosition(RngStreams.Combat));

        next.ShouldBe(
            SeedDerivation.BattleSeed(RunSnapshots.Seed, 3),
            "three battles started means the next battleIndex is 3.");

        Enumerable.Range(0, 3)
            .Select(index => SeedDerivation.BattleSeed(RunSnapshots.Seed, index))
            .ShouldNotContain(next, "and it is not any battle this run has already fought.");
    }

    /// <summary>
    /// 🔒 A battle's <b>own</b> draws never reach the run's <c>combat</c> position: fifty attack
    /// rolls move the battle-rooted stream fifty places and leave the run exactly where it was.
    /// </summary>
    /// <remarks>
    /// This is the discriminating half of `14` §8.1's re-rooting, and it is the assertion that stops
    /// the next reader wiring a battle's draw counter into the run: the draws <em>inside</em> a
    /// battle are rooted at <c>battleSeed</c>, restart at 0 for every battle and are <b>never
    /// persisted</b>, which is what makes §8.1's <em>"a revived battle restarts from draw 0 of the
    /// same battle stream: reproducible by construction"</em> true. Committing the draw count
    /// instead of the battle count would skip forty-nine battle indices and derive every later
    /// battle from a seed no replay reconstructs.
    /// </remarks>
    [Fact]
    public void A_battles_own_draws_never_reach_the_runs_combat_position()
    {
        var run = WithStreams((RngStreams.Combat, 3UL));

        var battle = new DeterministicRng(SeedDerivation.BattleSeed(run.RunSeed, 3), RngStreams.Combat);
        for (var draw = 0; draw < 50; draw++)
        {
            battle.NextUInt();
        }

        battle.Position.ShouldBe(50UL, "the battle's own stream counted every draw…");
        run.StreamPosition(RngStreams.Combat).ShouldBe(
            3UL,
            "…and not one of them reached the run: the per-battle draws are rooted at battleSeed " +
            "and are never persisted (14 §8.1).");

        run.CommitStreamPositions(
            new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Combat] = 4 });

        run.StreamPosition(RngStreams.Combat).ShouldBe(
            4UL, "one battle started, one step — not fifty-three.");
    }
}
