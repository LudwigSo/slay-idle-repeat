using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// BEGIN_SESSION's CommandSeed: the seam the quest slate and Daily shop block will both draw from
/// once they land. Until then, only the seam itself is testable — that the command demands its seed,
/// reaches a deterministic regime sensitive to it, and moves no run stream position.
/// </summary>
public sealed class BeginSessionDrawSeamTests
{
    [Fact]
    public void A_BEGIN_SESSION_with_no_CommandSeed_is_a_defect_and_names_the_regime()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => GameRules.Apply(
            BeginSessions.Slice(),
            BeginSessions.Command,
            Worlds.Context with { NowUtc = BeginSessions.Morning }));

        thrown.Message.ShouldMatchWildcard("*CommandSeed*META*never invents entropy*BEGIN_SESSION*");
        thrown.Message.ShouldContain(
            "MISWIRED COMPOSITION ROOT",
            Case.Sensitive,
            "the fix is the host that built the context, not a rule and not the player.");
    }

    [Fact]
    public void The_repeat_call_of_the_day_needs_no_seed_because_it_draws_nothing()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());

        // One minute later, inside the regeneration interval, so no accrual muddies the event list.
        var second = Should.NotThrow(() => GameRules.Apply(
            first.NewState,
            BeginSessions.Command,
            Worlds.Context with { NowUtc = BeginSessions.Morning.AddMinutes(1) }));

        second.Accepted.ShouldBeTrue("a no-op that draws nothing needs no seed.");
        second.Events.ShouldBeEmpty();
    }

    /// <remarks>
    /// Asserted on the canonical state hash rather than field-by-field, so a draw landing in a field
    /// this test forgot to name is still caught.
    /// </remarks>
    [Fact]
    public void The_same_CommandSeed_produces_the_same_state()
    {
        const ulong seed = 0xABCD_EF01_2345_6789UL;

        var left = BeginSessions.Send(BeginSessions.Slice(), commandSeed: seed);
        var right = BeginSessions.Send(BeginSessions.Slice(), commandSeed: seed);

        CanonicalStateWriter.HashMetaCommandState(left.NewState.Player.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.HashMetaCommandState(right.NewState.Player.ToSnapshot()),
            "same seed, same state.");
    }

    /// <remarks>
    /// Nothing is drawn from the seed yet, so the claim is made where it is decidable: two seeds,
    /// one stream, one draw index, different values.
    /// </remarks>
    [Fact]
    public void Two_different_CommandSeeds_address_different_draws()
    {
        const ulong left = 0xABCD_EF01_2345_6789UL;
        const ulong right = 0xABCD_EF01_2345_678AUL;

        new MetaDrawScope(left).Stream(RngStreams.Drops).NextUInt().ShouldNotBe(
            new MetaDrawScope(right).Stream(RngStreams.Drops).NextUInt(),
            "two seeds one bit apart must not draw the same value.");

        // …and the two states really are identical today: nothing is drawn yet. When the quest slate
        // and Daily shop block land, this assertion inverts — written down so the day it starts
        // failing is the day it is supposed to.
        CanonicalStateWriter.HashMetaCommandState(
                BeginSessions.Send(BeginSessions.Slice(), commandSeed: left).NewState.Player.ToSnapshot())
            .ShouldBe(
                CanonicalStateWriter.HashMetaCommandState(
                    BeginSessions.Send(BeginSessions.Slice(), commandSeed: right).NewState.Player.ToSnapshot()));
    }

    /// <remarks>
    /// The slice carries non-zero positions, since at all-zero "the positions did not move" would
    /// also be true of a command that reset them.
    /// </remarks>
    [Fact]
    public void A_BEGIN_SESSION_mid_run_moves_no_run_stream_position()
    {
        var positions = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [RngStreams.Board] = 8,
            [RngStreams.Dice] = 12,
            [RngStreams.Drops] = 3,
        };

        var run = Worlds.NewRun(RunSnapshots.With(rngStreamPositions: positions));

        var result = BeginSessions.Send(BeginSessions.Slice(run: run));

        result.Accepted.ShouldBeTrue("a meta command is legal with a run in the slice.");
        result.Events.ShouldNotBeEmpty("positive evidence the daily block ran: the refill was paid.");

        result.NewState.Run.ShouldNotBeNull();
        result.NewState.Run.RngStreamPositions.ShouldBe(
            positions,
            "out-of-run draws use the command seed with no persisted counter; a meta command that " +
            "moved a run stream would consume a draw the run can never see again.");
    }

    /// <remarks>
    /// Driven through the internal GameRules.Execute door with a fixture handler, since this shape
    /// must never be committed to the production table.
    /// </remarks>
    [Fact]
    public void A_meta_handler_that_writes_the_run_is_a_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                // Run Gold, not a stream position — a different field than the other ownership guard covers.
                input.Run.MoveCurrency(CurrencyId.GOLD, 500, "a_meta_command_had_no_business_here");
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldMatchWildcard("*CommandKind.Meta*WROTE THE RUN*");
        thrown.Message.ShouldContain(
            "ownership defect",
            Case.Sensitive,
            "named as an ownership defect rather than a determinism one — the two guards have different fixes.");
    }

    /// <remarks>
    /// The non-empty AdUses is the whole test: a synthesized RunSnapshot compares its
    /// IReadOnlyDictionary components by reference, and Run.CopyAdUses allocates a fresh map whenever
    /// it is non-empty — so the write-guard's equality check sees an untouched run as written the
    /// moment it holds one ad use.
    /// </remarks>
    [Fact]
    public void A_meta_command_is_accepted_over_a_run_that_has_watched_an_ad()
    {
        var withAnAdWatched = Worlds.InARun(RunSnapshots.With(
            adUses: RunSnapshots.AdUses((Placement: "AD_REVIVE", Uses: 1L))));

        var result = GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            withAnAdWatched,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue(
            "the handler wrote nothing; refusing it would strand every mid-run player who watched an ad.");

        result.NewState.Run!.AdUseCount("AD_REVIVE").ShouldBe(
            1L, "…and the ad use survives the command untouched.");
    }

    [Fact]
    public void A_meta_handler_that_only_reads_the_run_is_fine()
    {
        long seenGold = -1;

        var result = GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                seenGold = input.Run.Gold;
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        seenGold.ShouldBeGreaterThanOrEqualTo(0, "the handler really did read the run.");
    }

    /// <remarks>
    /// The fixture context carries a seed, so the refusal is about the command kind, not a missing seed.
    /// </remarks>
    [Fact]
    public void A_run_handler_that_reaches_for_the_meta_regime_is_a_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => GameRules.Execute(
            // Named rather than discarded: `_` would bind the discard to the parameter itself.
            Worlds.RunTable((Worlds.RunFixtureCommand command, HandlerInput input) =>
            {
                _ = command;
                _ = input.MetaDraws;
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Drawing(BeginSessions.Seed)));

        thrown.Message.ShouldMatchWildcard("*CommandKind.Run*EXCLUSIVE*HandlerInput.Rng*");
        thrown.Message.ShouldNotContain(
            "MISWIRED COMPOSITION ROOT",
            Case.Sensitive,
            "this is a handler in the wrong regime, not a host that forgot the seed.");
    }

    [Fact]
    public void A_BEGIN_SESSION_mid_run_does_not_slide_the_runs_TTL()
    {
        var run = Worlds.NewRun();
        var before = run.LastAppliedAtUtc;

        var result = BeginSessions.Send(BeginSessions.Slice(run: run), BeginSessions.Morning.AddHours(3));

        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(
            before, "the run's TTL slides on run commands only.");

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(
            BeginSessions.Morning.AddHours(3),
            "…while the player's does advance, so the assertion above isn't just observing a no-op.");
    }
}
