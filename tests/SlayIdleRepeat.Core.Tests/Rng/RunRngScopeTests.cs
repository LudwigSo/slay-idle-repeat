using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// <see cref="RunRngScope"/> on its own: how it is seeded, how it counts, and why <c>combat</c> is
/// not opened like the others. Its integration with <c>Apply</c> is covered in <c>GameRulesRngTests</c>.
/// </summary>
public sealed class RunRngScopeTests
{
    private const ulong Seed = 0x0123456789ABCDEFUL;

    private static IReadOnlyDictionary<string, ulong> Committed(params (string Stream, ulong Position)[] rows) =>
        rows.ToDictionary(r => r.Stream, r => r.Position, StringComparer.Ordinal);

    // ------------------------------------------------------------------ seeding

    /// <summary>
    /// A stream opens at the position the run committed it at, so the next draw is the next one
    /// rather than a repeat. Asserted against the derivation rather than a magic number.
    /// </summary>
    [Fact]
    public void A_stream_opens_at_its_committed_position()
    {
        var scope = new RunRngScope(Seed, Committed((RngStreams.Dice, 12UL)));

        var stream = scope.Stream(RngStreams.Dice);

        stream.Position.ShouldBe(12UL);
        stream.NextUInt().ShouldBe(new DeterministicRng(Seed, RngStreams.Dice, 12UL).NextUInt());
        stream.Position.ShouldBe(13UL);
    }

    /// <summary>A stream the run has never drawn from opens at 0.</summary>
    [Fact]
    public void An_undrawn_stream_opens_at_zero()
    {
        new RunRngScope(Seed, Committed()).Stream(RngStreams.Board).Position.ShouldBe(0UL);
    }

    /// <summary>
    /// The same name yields the same stream for the whole command — a second <c>Stream(dice)</c>
    /// continues the sequence rather than restarting it.
    /// </summary>
    [Fact]
    public void The_same_stream_name_yields_the_same_stream_for_the_whole_command()
    {
        var scope = new RunRngScope(Seed, Committed());

        var first = scope.Stream(RngStreams.Dice);
        first.NextUInt();

        var again = scope.Stream(RngStreams.Dice);

        again.ShouldBeSameAs(first);
        again.Position.ShouldBe(1UL);
    }

    /// <summary>Different names are different sequences.</summary>
    [Fact]
    public void Different_streams_are_independent()
    {
        var scope = new RunRngScope(Seed, Committed());

        var dice = scope.Stream(RngStreams.Dice);
        var board = scope.Stream(RngStreams.Board);

        dice.NextUInt();
        dice.NextUInt();

        board.Position.ShouldBe(0UL);
        dice.Position.ShouldBe(2UL);
    }

    /// <summary>The parameterised ninth row works like the eight fixed ones.</summary>
    [Fact]
    public void The_parameterised_minigame_stream_is_a_stream_like_any_other()
    {
        var scope = new RunRngScope(Seed, Committed((RngStreams.Minigame(7), 4UL)));

        scope.Stream(RngStreams.Minigame(7)).Position.ShouldBe(4UL);
        scope.Stream(RngStreams.Minigame(8)).Position.ShouldBe(0UL);
    }

    /// <summary>A name outside the registry cannot be drawn from.</summary>
    [Theory]
    [InlineData("DICE")]
    [InlineData("dic")]
    [InlineData("minigame:03")]
    [InlineData("")]
    public void An_unregistered_stream_name_is_refused(string streamName)
    {
        Should.Throw<ArgumentException>(() => new RunRngScope(Seed, Committed()).Stream(streamName))
            .Message.ShouldContain("not a row of the 14 §8.1 stream registry", Case.Sensitive);
    }

    /// <summary>A null name is a caller bug rather than an unregistered stream.</summary>
    [Fact]
    public void A_null_stream_name_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new RunRngScope(Seed, Committed()).Stream(null!))
            .ParamName.ShouldBe("streamName");
    }

    /// <summary>
    /// A run committed at a stream the registry does not recognise cannot open a scope at all: a
    /// name that cannot be drawn from cannot be resumed either. Asserted against this rule's own
    /// message, not merely against <c>ArgumentException</c>, since <c>Stream</c> refuses the same
    /// name with a different sentence.
    /// </summary>
    [Fact]
    public void A_committed_map_with_an_unregistered_stream_is_refused()
    {
        var thrown = Should.Throw<ArgumentException>(() => new RunRngScope(Seed, Committed(("DICE", 1UL))));

        thrown.ParamName.ShouldBe("committedPositions");
        thrown.Message.ShouldContain("no run can stand at a position in it", Case.Sensitive);
    }

    /// <summary>A null committed map is a caller bug — an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_committed_map_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new RunRngScope(Seed, null!))
            .ParamName.ShouldBe("committedPositions");
    }

    // ------------------------------------------------------------------ combat

    /// <summary>
    /// <c>combat</c> is not opened like the others, and the refusal says why: opening it as an
    /// ordinary stream would advance the battle counter once per die roll inside a fight, and every
    /// later battle in the run would be seeded from a number no replay could reproduce.
    /// </summary>
    [Fact]
    public void The_combat_stream_cannot_be_opened_like_the_others()
    {
        var thrown = Should.Throw<ArgumentException>(() =>
            new RunRngScope(Seed, Committed()).Stream(RngStreams.Combat));

        thrown.Message.ShouldContain("EXACTLY ONCE PER BATTLE", Case.Sensitive);
        thrown.Message.ShouldContain("BeginBattle", Case.Sensitive);
    }

    /// <summary>
    /// <c>BeginBattle</c> consumes exactly one index of the <c>combat</c> stream and derives the
    /// battle seed through <c>SeedDerivation.BattleSeed</c>.
    /// </summary>
    [Fact]
    public void BeginBattle_consumes_one_index_and_derives_that_battles_seed()
    {
        var scope = new RunRngScope(Seed, Committed((RngStreams.Combat, 2UL)));

        var battle = scope.BeginBattle();

        battle.BattleIndex.ShouldBe(2);
        battle.Seed.ShouldBe(SeedDerivation.BattleSeed(Seed, 2));
        battle.Draws.Position.ShouldBe(0UL, "a battle's own draws start at 0, always.");

        scope.FinalPositions()[RngStreams.Combat].ShouldBe(3UL);
    }

    /// <summary>
    /// Draws inside a battle are never persisted: forty of them leave the committed <c>combat</c>
    /// position at exactly one past where it started.
    /// </summary>
    [Fact]
    public void Draws_inside_a_battle_are_never_persisted()
    {
        var scope = new RunRngScope(Seed, Committed());

        var battle = scope.BeginBattle();

        for (var i = 0; i < 40; i++)
        {
            battle.Draws.NextUInt();
        }

        battle.Draws.Position.ShouldBe(40UL);
        scope.FinalPositions()[RngStreams.Combat].ShouldBe(1UL);
    }

    /// <summary>
    /// A battle's stream is rooted at the battle seed, not the run seed, so the client can be handed
    /// a fight to simulate without ever seeing <c>runSeed</c>.
    /// </summary>
    [Fact]
    public void A_battles_draws_come_from_the_battle_seed_and_not_the_run_seed()
    {
        var battle = new RunRngScope(Seed, Committed()).BeginBattle();

        battle.Draws.NextUInt().ShouldBe(new DeterministicRng(battle.Seed, RngStreams.Combat).NextUInt());
        battle.Seed.ShouldNotBe(Seed);
    }

    // ------------------------------------------------------------------ FinalPositions

    /// <summary>The map handed to <c>Run.CommitStreamPositions</c> is a superset of the committed one.</summary>
    [Fact]
    public void FinalPositions_carries_every_committed_stream_forward()
    {
        var scope = new RunRngScope(Seed, Committed(
            (RngStreams.Dice, 12UL), (RngStreams.Board, 8UL), (RngStreams.Drops, 3UL)));

        scope.Stream(RngStreams.Dice).NextUInt();

        var positions = scope.FinalPositions();

        positions[RngStreams.Dice].ShouldBe(13UL);
        positions[RngStreams.Board].ShouldBe(8UL);
        positions[RngStreams.Drops].ShouldBe(3UL);
        positions.Count.ShouldBe(3);
    }

    /// <summary>
    /// A stream opened but never drawn from, and never committed, does not appear: the eager
    /// alternative would put a <c>"shrine": 0</c> row in the state hash of every run that ever
    /// looked at a shrine stream, and the client recomputes that hash per command, on a handset.
    /// </summary>
    [Fact]
    public void An_opened_but_undrawn_stream_writes_no_row()
    {
        var scope = new RunRngScope(Seed, Committed());

        scope.Stream(RngStreams.Shrine);
        scope.Stream(RngStreams.Treasure);

        scope.FinalPositions().ShouldBeEmpty();
    }

    /// <summary>
    /// A stream that was committed at 0 stays in the map even if this command never drew from it:
    /// dropping it would violate <c>Run.CommitStreamPositions</c>' superset contract.
    /// </summary>
    [Fact]
    public void A_stream_committed_at_zero_survives_a_command_that_did_not_touch_it()
    {
        new RunRngScope(Seed, Committed((RngStreams.Events, 0UL)))
            .FinalPositions()
            .ContainsKey(RngStreams.Events)
            .ShouldBeTrue();
    }

    /// <summary>
    /// A scope nothing touched hands back exactly what it was given — the identity a command that
    /// draws nothing must produce.
    /// </summary>
    [Fact]
    public void A_scope_nothing_touched_hands_back_what_it_was_given()
    {
        var committed = Committed((RngStreams.Dice, 12UL));

        new RunRngScope(Seed, committed).FinalPositions().ShouldBe(committed);
    }

    /// <summary>
    /// The map is handed out read-only and ordered ordinally, because
    /// <c>CanonicalStateWriter</c> orders string keys ordinally and a map compared any other way
    /// would round-trip to a different hash than it was stored under.
    /// </summary>
    [Fact]
    public void FinalPositions_is_read_only_and_ordinal()
    {
        var positions = new RunRngScope(Seed, Committed((RngStreams.Dice, 1UL))).FinalPositions();

        // ReadOnlyDictionary<,> does implement IDictionary<,>, explicitly, and throws on every
        // write. Asserting the WRITE throws is the assertion that describes the boundary; a type
        // check would pass for any wrapper and prove nothing about it.
        var asDictionary = (IDictionary<string, ulong>)positions;

        Should.Throw<NotSupportedException>(() => asDictionary[RngStreams.Dice] = 0UL);
        Should.Throw<NotSupportedException>(() => asDictionary.Add(RngStreams.Board, 1UL));

        positions.ContainsKey("DICE").ShouldBeFalse("the registry is ordinal: DICE is not dice.");
    }

    /// <summary>A scope never moves a counter backwards, whatever a handler does with it.</summary>
    [Fact]
    public void A_scope_never_moves_a_counter_backwards()
    {
        var scope = new RunRngScope(Seed, Committed((RngStreams.Dice, 12UL)));

        scope.FinalPositions()[RngStreams.Dice].ShouldBe(12UL);

        scope.Stream(RngStreams.Dice).NextUInt();

        scope.FinalPositions()[RngStreams.Dice].ShouldBe(13UL);
    }
}
