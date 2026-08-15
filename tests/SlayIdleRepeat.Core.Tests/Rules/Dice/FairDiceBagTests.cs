using Shouldly;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Dice;

/// <summary>`04` §4's Fair-Dice weighted bag: decay 0.55, boost 0.12, clamp [0.25, 2.0].</summary>
public sealed class FairDiceBagTests
{
    private const ulong Seed = 0x1122334455667788UL;

    [Fact]
    public void Initial_weights_are_six_ones()
    {
        FairDiceBag.InitialWeights.Count.ShouldBe(6);
        FairDiceBag.InitialWeights.ShouldAllBe(w => w == 1.0);
    }

    [Fact]
    public void One_step_decays_the_drawn_face_and_boosts_every_other()
    {
        var rng = DeterministicRng.OpenAt(Seed, RngStreams.Dice, 0);

        var (face, next) = FairDiceBag.Step(rng, FairDiceBag.InitialWeights);

        face.ShouldBeInRange(1, 6);

        for (var i = 0; i < 6; i++)
        {
            if (i + 1 == face)
            {
                // Hardcoded literals, not FairDiceBag's own constants — a test that read the
                // constant back would pass no matter what value production used (S1).
                next[i].ShouldBe(0.55, tolerance: 1e-9);
            }
            else
            {
                next[i].ShouldBe(1.12, tolerance: 1e-9);
            }
        }
    }

    [Fact]
    public void Weights_never_leave_the_clamp_band_across_many_draws()
    {
        var rng = DeterministicRng.OpenAt(Seed, RngStreams.Dice, 0);
        var weights = FairDiceBag.InitialWeights;

        for (var i = 0; i < 500; i++)
        {
            (_, weights) = FairDiceBag.Step(rng, weights);

            weights.ShouldAllBe(w => w >= FairDiceBag.MinWeight - 1e-9 && w <= FairDiceBag.MaxWeight + 1e-9);
        }
    }

    [Fact]
    public void Replay_from_zero_to_zero_is_the_initial_vector()
    {
        FairDiceBag.Replay(Seed, resetAtDraw: 0, uptoDraw: 0).ShouldBe(FairDiceBag.InitialWeights);
    }

    [Fact]
    public void Replay_reconstructs_exactly_what_stepping_forward_produces()
    {
        var stepped = FairDiceBag.InitialWeights;
        var rng = DeterministicRng.OpenAt(Seed, RngStreams.Dice, 0);

        for (var i = 0; i < 10; i++)
        {
            (_, stepped) = FairDiceBag.Step(rng, stepped);
        }

        var replayed = FairDiceBag.Replay(Seed, resetAtDraw: 0, uptoDraw: 10);

        replayed.ShouldBe(stepped);
    }

    [Fact]
    public void Replay_is_deterministic_call_twice_get_the_same_vector()
    {
        var first = FairDiceBag.Replay(Seed, 0, 37);
        var second = FairDiceBag.Replay(Seed, 0, 37);

        first.ShouldBe(second);
    }

    [Fact]
    public void UptoDraw_before_resetAtDraw_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FairDiceBag.Replay(Seed, resetAtDraw: 5, uptoDraw: 4));
    }

    [Fact]
    public void Step_refuses_a_weight_vector_that_is_not_six_wide()
    {
        var rng = DeterministicRng.OpenAt(Seed, RngStreams.Dice, 0);

        Should.Throw<ArgumentException>(() => FairDiceBag.Step(rng, new[] { 1.0, 1.0 }));
    }

    /// <summary>
    /// S1 — the decay/boost/clamp constants are the whole point of this type; mutate each and prove
    /// the tests above actually notice, then confirm they pass again once reverted (via re-reading the
    /// production file after the check — see the M3-04 completion report for the literal before/after
    /// captured while these were live).
    /// </summary>
    [Fact]
    public void Decay_and_boost_constants_are_04_4s_own_numbers()
    {
        FairDiceBag.DecayFactor.ShouldBe(0.55);
        FairDiceBag.BoostAmount.ShouldBe(0.12);
        FairDiceBag.MinWeight.ShouldBe(0.25);
        FairDiceBag.MaxWeight.ShouldBe(2.0);
    }
}
