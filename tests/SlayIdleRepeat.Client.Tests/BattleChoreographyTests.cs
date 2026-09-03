using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

public sealed class BattleChoreographyTests
{
    private const byte Hero = 0;
    private const byte Enemy = 4;
    private const byte Summon = 6;
    private const byte Nobody = 255;

    // Not the shipped timings, so a playhead with those written in lands somewhere a case can see.
    private static readonly BattleMotionTimings Timings = new(
        SwingSeconds: 0.4, SwingReach: 1.0,
        RecoilSeconds: 0.2, RecoilDistance: 0.5,
        DodgeSeconds: 0.3, DodgeSideStep: 0.7,
        BraceSeconds: 0.5, BraceSquash: 0.2,
        FallSeconds: 1.0, FallTipDegrees: 70, FallSink: 0.4,
        EnterSeconds: 0.6, PoseHoldSeconds: 0.9, ReducedMotionSeconds: 0.05);

    [Fact]
    public void PoseOf_an_actor_nothing_has_moved_is_standing_at_rest()
    {
        var pose = new BattleChoreography(Timings).PoseOf(Enemy);

        pose.ShouldBe(Rest);
    }

    [Fact]
    public void A_cue_asking_no_motion_moves_nothing()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.None, Hero));
        choreography.Advance(0.1);

        choreography.InProgress.ShouldBeFalse();
        choreography.PoseOf(Enemy).ShouldBe(Rest);
    }

    [Fact]
    public void A_swing_reaches_its_full_distance_toward_its_counterpart_at_its_midpoint()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        choreography.Advance(Timings.SwingSeconds / 2);

        var pose = choreography.PoseOf(Hero);

        pose.Advance.ShouldBe(Timings.SwingReach, 1e-9);
        pose.Toward.ShouldBe(Enemy, "the swing is aimed at the defender the cue names.");
        pose.SideStep.ShouldBe(0d, 1e-9);
    }

    [Fact]
    public void A_swing_is_still_under_way_and_on_its_way_back_at_three_quarters_of_its_seconds()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        choreography.Advance(Timings.SwingSeconds * 0.75);

        var late = choreography.PoseOf(Hero);

        late.Advance.ShouldBeGreaterThan(0d);
        late.Advance.ShouldBeLessThan(Timings.SwingReach);
        choreography.InProgress.ShouldBeTrue();
    }

    [Fact]
    public void A_swing_leaves_no_displacement_behind_once_its_seconds_are_up()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        choreography.Advance(Timings.SwingSeconds + 0.001);

        choreography.PoseOf(Hero).ShouldBe(Rest, "a finished swing leaves no displacement behind.");
        choreography.InProgress.ShouldBeFalse();
    }

    [Fact]
    public void A_recoil_moves_the_defender_away_from_its_striker_by_the_authored_distance_at_its_midpoint()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.Recoil, Hero));
        choreography.Advance(Timings.RecoilSeconds / 2);

        var pose = choreography.PoseOf(Enemy);

        pose.Advance.ShouldBe(-Timings.RecoilDistance, 1e-9, "away from the striker is negative advance toward it.");
        pose.Toward.ShouldBe(Hero);
    }

    [Fact]
    public void A_dodge_steps_aside_by_the_authored_side_step_at_its_midpoint_without_advancing()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.Dodge, Hero));
        choreography.Advance(Timings.DodgeSeconds / 2);

        var pose = choreography.PoseOf(Enemy);

        pose.SideStep.ShouldBe(Timings.DodgeSideStep, 1e-9);
        pose.Advance.ShouldBe(0d, 1e-9);
    }

    [Fact]
    public void A_brace_squashes_by_the_authored_amount_at_its_midpoint()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.Brace, Hero));
        choreography.Advance(Timings.BraceSeconds / 2);

        choreography.PoseOf(Enemy).Squash.ShouldBe(Timings.BraceSquash, 1e-9);
    }

    [Fact]
    public void A_wind_up_pulses_for_exactly_the_seconds_the_cue_states()
    {
        var choreography = new BattleChoreography(Timings);

        // Longer than every authored timing, so a wind-up timed off one of those ends early.
        choreography.Play(Cue(Enemy, ReplayMotion.WindUp, Nobody, seconds: 1.5));
        choreography.Advance(1.4);

        choreography.PoseOf(Enemy).Pulse.ShouldBeGreaterThan(0d);

        choreography.Advance(0.11);

        choreography.PoseOf(Enemy).Pulse.ShouldBe(0d, 1e-9);
    }

    [Fact]
    public void A_wind_up_stating_no_seconds_pulses_for_the_pose_hold()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.WindUp, Nobody, seconds: 0));
        choreography.Advance(Timings.PoseHoldSeconds - 0.05);

        choreography.PoseOf(Enemy).Pulse.ShouldBeGreaterThan(0d);

        choreography.Advance(0.06);

        choreography.PoseOf(Enemy).Pulse.ShouldBe(0d, 1e-9);
    }

    [Fact]
    public void A_fall_tips_and_sinks_the_actor_progressively_and_leaves_it_lying_there()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.Fall, Hero));
        choreography.Advance(Timings.FallSeconds / 2);

        var midway = choreography.PoseOf(Enemy);
        midway.Fallen.ShouldBeTrue("an actor is out of the fight from the moment it starts to go down.");
        midway.TipDegrees.ShouldBeGreaterThan(0d);
        midway.TipDegrees.ShouldBeLessThan(Timings.FallTipDegrees);

        choreography.Advance(Timings.FallSeconds / 2);
        choreography.Advance(5d);

        var landed = choreography.PoseOf(Enemy);
        landed.Fallen.ShouldBeTrue();
        landed.TipDegrees.ShouldBe(Timings.FallTipDegrees, 1e-9);
        landed.Sink.ShouldBe(Timings.FallSink, 1e-9);
        choreography.InProgress.ShouldBeFalse();
    }

    [Fact]
    public void A_motion_played_after_a_fall_is_ignored()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Enemy, ReplayMotion.Fall, Hero));
        choreography.Advance(Timings.FallSeconds / 2);
        choreography.Play(Cue(Enemy, ReplayMotion.Swing, Hero));
        choreography.Advance(Timings.SwingSeconds / 2);

        var pose = choreography.PoseOf(Enemy);

        pose.Fallen.ShouldBeTrue("a fall is terminal.");
        pose.Advance.ShouldBe(0d, 1e-9, "the swing never started.");
        pose.TipDegrees.ShouldBeGreaterThan(0d, "and the fall carried on.");
    }

    [Fact]
    public void SnapFallen_lays_the_actor_down_at_once_and_shuts_it_to_later_motions()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.SnapFallen(Enemy);

        var pose = choreography.PoseOf(Enemy);
        pose.Fallen.ShouldBeTrue();
        pose.TipDegrees.ShouldBe(Timings.FallTipDegrees, 1e-9);
        pose.Sink.ShouldBe(Timings.FallSink, 1e-9);
        choreography.InProgress.ShouldBeFalse();

        choreography.Play(Cue(Enemy, ReplayMotion.Swing, Hero));
        choreography.Advance(Timings.SwingSeconds / 2);

        choreography.PoseOf(Enemy).Advance.ShouldBe(0d, 1e-9);
    }

    [Fact]
    public void A_later_motion_pre_empts_one_still_in_flight_on_the_same_actor()
    {
        var interrupted = new BattleChoreography(Timings);
        interrupted.Play(Cue(Enemy, ReplayMotion.Swing, Hero));
        interrupted.Advance(Timings.SwingSeconds / 2);
        interrupted.Play(Cue(Enemy, ReplayMotion.Recoil, Hero));
        interrupted.Advance(Timings.RecoilSeconds / 2);

        var alone = new BattleChoreography(Timings);
        alone.Play(Cue(Enemy, ReplayMotion.Recoil, Hero));
        alone.Advance(Timings.RecoilSeconds / 2);

        interrupted.PoseOf(Enemy).Advance.ShouldBe(-Timings.RecoilDistance, 1e-9, "only the recoil is left.");
        ShouldMatch(interrupted.PoseOf(Enemy), alone.PoseOf(Enemy));
    }

    [Fact]
    public void Each_actor_moves_on_its_own()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        choreography.Play(Cue(Enemy, ReplayMotion.Recoil, Hero));
        choreography.Advance(0.1);

        choreography.PoseOf(Hero).Advance.ShouldBeGreaterThan(0d);
        choreography.PoseOf(Enemy).Advance.ShouldBeLessThan(0d);
    }

    [Fact]
    public void One_long_frame_lands_where_several_short_ones_land()
    {
        var oneFrame = new BattleChoreography(Timings);
        oneFrame.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        oneFrame.Advance(0.1);

        var manyFrames = new BattleChoreography(Timings);
        manyFrames.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        manyFrames.Advance(0.025);
        manyFrames.Advance(0.025);
        manyFrames.Advance(0.025);
        manyFrames.Advance(0.025);

        oneFrame.PoseOf(Hero).Advance.ShouldBeGreaterThan(0d);
        ShouldMatch(oneFrame.PoseOf(Hero), manyFrames.PoseOf(Hero));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void A_frame_that_took_no_time_moves_nothing(double delta)
    {
        var choreography = new BattleChoreography(Timings);

        choreography.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        choreography.Advance(delta);
        choreography.Advance(Timings.SwingSeconds / 2);

        choreography.PoseOf(Hero).Advance.ShouldBe(
            Timings.SwingReach, 1e-9, "the empty frame did not count toward the swing.");
    }

    [Fact]
    public void An_absent_actor_enters_by_growing_to_full_size_over_its_enter_seconds()
    {
        var choreography = new BattleChoreography(Timings);

        choreography.SnapAbsent(Summon);
        choreography.PoseOf(Summon).Present.ShouldBeFalse("nothing has brought the summon on yet.");

        choreography.Play(Cue(Summon, ReplayMotion.Enter, Nobody));
        choreography.Advance(Timings.EnterSeconds / 2);

        var entering = choreography.PoseOf(Summon);
        entering.Present.ShouldBeTrue();
        entering.Scale.ShouldBeGreaterThan(0d);
        entering.Scale.ShouldBeLessThan(1d);

        choreography.Advance(Timings.EnterSeconds / 2 + 0.001);

        choreography.PoseOf(Summon).ShouldBe(Rest);
    }

    [Fact]
    public void Reduced_motion_completes_a_swing_on_its_first_advance()
    {
        var full = new BattleChoreography(Timings);
        full.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        full.Advance(0.016);

        var reduced = new BattleChoreography(Timings, reducedMotion: true);
        reduced.Play(Cue(Hero, ReplayMotion.Swing, Enemy));
        reduced.Advance(0.016);

        full.InProgress.ShouldBeTrue("the same frame leaves a full-motion swing in flight.");
        full.PoseOf(Hero).Advance.ShouldBeGreaterThan(0d);
        reduced.InProgress.ShouldBeFalse();
        reduced.PoseOf(Hero).ShouldBe(Rest);
    }

    [Fact]
    public void Reduced_motion_still_leaves_a_fallen_actor_lying_there()
    {
        var choreography = new BattleChoreography(Timings, reducedMotion: true);

        choreography.Play(Cue(Enemy, ReplayMotion.Fall, Hero));
        choreography.Advance(0.016);

        var pose = choreography.PoseOf(Enemy);

        pose.Fallen.ShouldBeTrue();
        pose.TipDegrees.ShouldBe(Timings.FallTipDegrees, 1e-9);
        pose.Sink.ShouldBe(Timings.FallSink, 1e-9);
    }

    [Fact]
    public void Reduced_motion_still_brings_an_entering_actor_on_stage()
    {
        var choreography = new BattleChoreography(Timings, reducedMotion: true);

        choreography.SnapAbsent(Summon);
        choreography.Play(Cue(Summon, ReplayMotion.Enter, Nobody));
        choreography.Advance(0.016);

        var pose = choreography.PoseOf(Summon);

        pose.Present.ShouldBeTrue();
        pose.Scale.ShouldBe(1d, 1e-9);
    }

    private static readonly ActorPose Rest = new(
        Advance: 0, Toward: null, SideStep: 0, Squash: 0, Lift: 0, TipDegrees: 0, Sink: 0,
        Scale: 1, Pulse: 0, Fallen: false, Present: true);

    /// <summary>Two poses reached along different frame sequences: equal up to accumulated rounding.</summary>
    private static void ShouldMatch(ActorPose actual, ActorPose expected)
    {
        const double tolerance = 1e-9;

        actual.Advance.ShouldBe(expected.Advance, tolerance);
        actual.Toward.ShouldBe(expected.Toward);
        actual.SideStep.ShouldBe(expected.SideStep, tolerance);
        actual.Squash.ShouldBe(expected.Squash, tolerance);
        actual.Lift.ShouldBe(expected.Lift, tolerance);
        actual.TipDegrees.ShouldBe(expected.TipDegrees, tolerance);
        actual.Sink.ShouldBe(expected.Sink, tolerance);
        actual.Scale.ShouldBe(expected.Scale, tolerance);
        actual.Pulse.ShouldBe(expected.Pulse, tolerance);
        actual.Fallen.ShouldBe(expected.Fallen);
        actual.Present.ShouldBe(expected.Present);
    }

    private static ReplayCue Cue(byte actor, ReplayMotion motion, byte counterpart, double seconds = 0) =>
        new(actor, counterpart, ReplaySide.Enemy, motion, seconds,
            ReplayFloater.None, 0, ReplayBurst.None, Health: null, Died: false, StatusId: null, StatusStacks: 0);
}
