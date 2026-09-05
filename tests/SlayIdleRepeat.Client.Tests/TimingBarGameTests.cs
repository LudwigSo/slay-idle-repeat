using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The timing-bar minigame and the numbers it is played by — the engine-free half of a skill game,
/// on <c>BoardWalkTests</c>' pattern.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Every number comes out of the fixture document, and the fixture's numbers are not the
/// shipped ones.</b> A game with its sweep or its window written into C# passes every case below if
/// the fixture mirrors <c>game-data</c>, and the whole reason these are authored is that a designer
/// retunes them.
/// </para>
/// <para>
/// 🔒 <b>The frame-rate case is an EQUIVALENCE, not a tolerance.</b> One long frame and several
/// short ones summing to the same time have to leave the cursor in the same place — a game that
/// nudged the cursor per frame sweeps faster on a fast handset and turns the window into a different
/// size of target depending on hardware, which on a skill game decides what a player is paid.
/// </para>
/// </remarks>
public sealed class TimingBarGameTests
{
    /// <summary>
    /// The floating-point slack the equivalence is allowed. Two orders of magnitude tighter than any
    /// per-frame quantisation, which lands about a tenth of a bar away.
    /// </summary>
    private const double Slack = 1e-9;

    /// <summary>The centre of the bar, which is the point a strike is judged against.</summary>
    private const double Centre = 0.5;

    // ---- the authored numbers -------------------------------------------------------------------

    /// <summary>
    /// 🔒 The four numbers are read out of the document rather than carried in code.
    /// </summary>
    [Fact]
    public void The_authored_numbers_are_read_out_of_the_tuning_document()
    {
        var rules = TimingBarRules.Read(MinigameContent.TimingBarOnly());

        rules.Strikes.ShouldBe(MinigameContent.Strikes);
        rules.SweepSeconds.ShouldBe(
            MinigameContent.SweepSeconds,
            "the fixture authors a sweep no shipped value uses, so a game carrying the shipped " +
            "number in code cannot satisfy this.");
        rules.HitWindowHalfWidth.ShouldBe(MinigameContent.HitWindowHalfWidth);
        rules.ReducedMotionStepFraction.ShouldBe(MinigameContent.ReducedMotionStepFraction);
    }

    [Fact]
    public void A_null_content_set_is_refused_by_name() =>
        Should.Throw<ArgumentNullException>(() => TimingBarRules.Read(null!))
            .ParamName.ShouldBe("content");

    /// <summary>A content set with no such document is a content failure, not a default.</summary>
    /// <remarks>
    /// 🔒 Refused rather than defaulted, on every other reader's precedent: a sweep silently read as
    /// zero is a cursor that never moves, and a game nobody can score is worse than one that will not
    /// open.
    /// </remarks>
    [Fact]
    public void A_content_set_without_the_document_is_refused_rather_than_defaulted() =>
        Should.Throw<ContentException>(() => TimingBarRules.Read(MinigameContent.StringsOnly()));

    /// <summary>
    /// 🔴 <b>A strike count that disagrees with the reward table is refused at the READ.</b>
    /// </summary>
    /// <remarks>
    /// The tier is the hit count, so <c>strikes + 1</c> outcomes exist and the table must author
    /// exactly that many rows. Caught here rather than at the press: a perfect game whose tier the
    /// rules layer refuses reaches the player as a rules refusal after they have played it.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void A_strike_count_the_reward_table_cannot_pay_is_refused(int strikes) =>
        Should.Throw<ContentException>(
            () => TimingBarRules.Read(MinigameContent.TimingBarAuthoring(strikes: strikes)));

    /// <summary>
    /// 🔒 …and with no reward table at all the read refuses rather than believing the number.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control for the pair above. A read that never opened the table and simply
    /// compared the strike count against a four it carried in code satisfies both of those rows and
    /// the one below — and would then agree with a retuned table nobody grew, which is the drift the
    /// refusal exists to catch.
    /// </remarks>
    [Fact]
    public void A_content_set_without_the_reward_table_is_refused_rather_than_believed() =>
        Should.Throw<ContentException>(
            () => TimingBarRules.Read(MinigameContent.TimingBarWithoutTheRewardTable()));

    /// <summary>…and the count the table CAN pay is accepted, so the refusal is about that number.</summary>
    [Fact]
    public void The_strike_count_the_reward_table_can_pay_is_accepted() =>
        TimingBarRules.Read(MinigameContent.TimingBarAuthoring(strikes: MinigameContent.Strikes))
                      .Strikes.ShouldBe(
                          MinigameContent.Strikes,
                          "with this refused too, the case above is refusing every read rather than " +
                          "the two counts the table cannot pay.");

    /// <summary>
    /// 🔒 <b>Every authored number is bounded, and the bounds are the schema's own.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Each of these rows is a document that ships a game nobody can play, and none of them is
    /// malformed — so this read is the whole of what stands between such a document and a screen
    /// opening on it. No strikes is a game with no press to make; a sweep of no time is a cursor
    /// everywhere at once; a window of no width is a game no strike wins, and one of half a bar
    /// reaches both ends, which is a game every strike wins wherever the cursor stands; a step of
    /// nothing leaves the reduced-motion player unable to aim, and a step of a whole bar folds the
    /// cursor between the two ENDS and puts it nowhere else — the accessible arm becomes unwinnable
    /// while the timed one is not.
    /// </para>
    /// <para>
    /// 🔒 The two fractions are pinned at exactly the value <c>minigames.schema.json</c> excludes. A
    /// runtime bound one epsilon looser than the schema it stands behind is a bound that only ever
    /// agrees with documents the schema has already refused.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(
        0, MinigameContent.SweepSeconds, MinigameContent.HitWindowHalfWidth,
        MinigameContent.ReducedMotionStepFraction)]
    [InlineData(
        MinigameContent.Strikes, 0.0, MinigameContent.HitWindowHalfWidth,
        MinigameContent.ReducedMotionStepFraction)]
    [InlineData(
        MinigameContent.Strikes, MinigameContent.SweepSeconds, 0.0,
        MinigameContent.ReducedMotionStepFraction)]
    [InlineData(
        MinigameContent.Strikes, MinigameContent.SweepSeconds, 0.5,
        MinigameContent.ReducedMotionStepFraction)]
    [InlineData(
        MinigameContent.Strikes, MinigameContent.SweepSeconds, MinigameContent.HitWindowHalfWidth,
        0.0)]
    [InlineData(
        MinigameContent.Strikes, MinigameContent.SweepSeconds, MinigameContent.HitWindowHalfWidth,
        1.0)]
    public void A_number_that_would_make_the_game_unplayable_is_refused_rather_than_shipped(
        int strikes, double sweepSeconds, double hitWindowHalfWidth, double stepFraction) =>
        Should.Throw<ContentException>(() => TimingBarRules.Read(
            MinigameContent.TimingBarAuthoring(
                strikes, sweepSeconds, hitWindowHalfWidth, stepFraction)));

    /// <summary>
    /// 🔒 …and the widest window and the longest step the schema DOES allow are both accepted.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control for the two boundary rows above. Without it a read that refused every
    /// fraction it was given — or refused on the strike count alone and never looked at either —
    /// satisfies both of them, and the bound would be pinned at nothing in particular.
    /// </remarks>
    [Fact]
    public void The_widest_window_and_the_longest_step_short_of_the_bound_are_accepted()
    {
        var rules = TimingBarRules.Read(MinigameContent.TimingBarAuthoring(
            hitWindowHalfWidth: 0.49, reducedMotionStepFraction: 0.99));

        rules.HitWindowHalfWidth.ShouldBe(0.49, Slack);
        rules.ReducedMotionStepFraction.ShouldBe(0.99, Slack);
    }

    // ---- the cursor ------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The cursor is a triangle wave: out to the far end in half a sweep, back in the other half.
    /// </summary>
    /// <remarks>
    /// 🔴 Five points rather than two. A saw-tooth agrees with a triangle over the first half and
    /// snaps back instead of returning, and a cursor that ran out and stopped agrees over the first
    /// half too — the return leg is the half a player aims on the way back.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 0.5)]
    [InlineData(2.0, 0.0)]
    [InlineData(2.5, 0.5)]
    public void The_cursor_sweeps_out_and_back_over_one_authored_sweep(double elapsed, double expected)
    {
        var game = Game();

        game.Advance(elapsed);

        game.Cursor.ShouldBe(
            expected,
            Slack,
            "at " + elapsed + " seconds of a " + MinigameContent.SweepSeconds + "-second sweep the " +
            "cursor should stand at " + expected + " and stands at " + game.Cursor + ". The bar is " +
            "swept out and back, so the return leg is as playable as the outward one.");
    }

    /// <summary>A fresh game has its cursor at the start and every strike still to take.</summary>
    [Fact]
    public void A_fresh_game_has_taken_no_strikes()
    {
        var game = Game();

        game.Cursor.ShouldBe(0.0, Slack);
        game.Hits.ShouldBe(0);
        game.Tier.ShouldBe(0);
        game.StrikesLeft.ShouldBe(MinigameContent.Strikes);
        game.Finished.ShouldBeFalse();
        game.HalfWidth.ShouldBe(
            MinigameContent.HitWindowHalfWidth,
            "the scene draws the window from this, so a game reporting a different one draws a " +
            "target the strike is not judged against.");
    }

    /// <summary>
    /// 🔴 <b>One long frame lands exactly where several short ones summing to the same time do.</b>
    /// </summary>
    /// <remarks>
    /// The equivalence is the claim. A game that moved the cursor by a fixed amount per call — or
    /// that rounded elapsed time to frames — sweeps at a rate that depends on the handset, so the
    /// same play scores differently on different hardware. Two very different frame budgets are
    /// compared, and both against a single call.
    /// </remarks>
    [Theory]
    [InlineData(10)]
    [InlineData(1000)]
    public void One_long_frame_lands_where_the_same_time_in_short_frames_does(int frames)
    {
        const double Elapsed = 0.7;

        var oneFrame = Game();
        var manyFrames = Game();

        oneFrame.Advance(Elapsed);

        for (var frame = 0; frame < frames; frame++)
        {
            manyFrames.Advance(Elapsed / frames);
        }

        manyFrames.Cursor.ShouldBe(
            oneFrame.Cursor,
            Slack,
            "the same " + Elapsed + " seconds left the cursor at " + manyFrames.Cursor + " over " +
            frames + " frames and at " + oneFrame.Cursor + " over one. The cursor has to be derived " +
            "from accumulated time, not nudged per frame — otherwise the window is a different size " +
            "of target on a fast handset than on a slow one, and the game pays differently for it.");
    }

    [Fact]
    public void A_negative_frame_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Game().Advance(-0.001));

    // ---- the window ------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A strike scores exactly when the cursor is within the half-width of the centre.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The two edges are struck EXACTLY.</b> With a quarter-bar window the boundary is 0.25 and
    /// 0.75, and the comparison is inclusive — so a game using <c>&lt;</c> refuses a strike dead on
    /// the edge of the window it drew, which is the strike a player aiming at the edge makes. The
    /// misses either side are one twentieth of a bar out, close enough that a game with the window
    /// centred elsewhere would still be caught.
    /// </para>
    /// <para>
    /// 🔒 The elapsed times are the ones the TRIANGLE puts those cursors at, and the upper edge is
    /// reached on the RETURN leg. Over a two-second there-and-back sweep the cursor is at 1 after one
    /// second and back at 0 after two, so 0.75 stands at 1.25 seconds and not at 1.5 — an arrangement
    /// computed as <c>elapsed / sweepSeconds</c> puts the cursor somewhere else entirely and the
    /// boundary this case exists for is never struck.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0.25, 0.25, true)]
    [InlineData(0.20, 0.20, false)]
    [InlineData(1.25, 0.75, true)]
    [InlineData(1.20, 0.80, false)]
    [InlineData(1.0, 1.00, false)]
    [InlineData(0.0, 0.00, false)]
    public void A_strike_scores_exactly_inside_the_authored_window(
        double elapsed, double expectedCursor, bool expectedHit)
    {
        var game = Game();

        game.Advance(elapsed);

        game.Cursor.ShouldBe(
            expectedCursor,
            Slack,
            "the arrangement did not put the cursor where this row is about, so whatever the strike " +
            "below answers is not evidence about the window.");

        game.Strike().ShouldBe(
            expectedHit,
            "the cursor stands at " + game.Cursor + ", the centre is " + Centre + " and the window " +
            "is " + MinigameContent.HitWindowHalfWidth + " either side — so |cursor - centre| is " +
            Math.Abs(game.Cursor - Centre) + ". The comparison is inclusive: a strike dead on the " +
            "edge of the window the scene drew has to score, or the game refuses the shot a player " +
            "aiming at the edge takes.");
        game.Hits.ShouldBe(expectedHit ? 1 : 0);
    }

    /// <summary>Every strike is spent whether it scored or not.</summary>
    [Fact]
    public void A_missed_strike_is_still_spent()
    {
        var game = Game();

        game.Strike().ShouldBeFalse("the cursor is at one end of the bar, nowhere near the window.");

        game.StrikesLeft.ShouldBe(
            MinigameContent.Strikes - 1,
            "a miss that cost nothing makes the game unlosable — the player simply strikes until " +
            "the cursor happens to be in the window.");
        game.Hits.ShouldBe(0);
    }

    // ---- the tier --------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The tier is the hit count, and the game finishes after exactly its authored strikes.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Swept over every reachable hit count, including both ends: a game reporting the strike
    /// count as the tier agrees at the top, and one reporting zero agrees at the bottom.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_tier_is_the_number_of_strikes_that_scored(int hits)
    {
        var game = Game();
        var elapsed = 0.0;

        for (var strike = 0; strike < MinigameContent.Strikes; strike++)
        {
            game.Finished.ShouldBeFalse(
                "the game reported itself finished with " + game.StrikesLeft + " strikes left, so " +
                "the loop below is striking a game that has already settled.");

            elapsed = AimAndStrike(game, strike, insideTheWindow: strike < hits, elapsed);
        }

        game.Hits.ShouldBe(hits);
        game.Tier.ShouldBe(
            hits,
            "the tier IS the hit count — it is the index into the reward table, and the table " +
            "authors one row per reachable hit count. Any other number claims an outcome the player " +
            "did not play for.");
        game.Finished.ShouldBeTrue(
            "every authored strike has been taken, so the tier is settled and the screen may submit.");
        game.StrikesLeft.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 A strike taken after the game finished scores nothing and consumes nothing.
    /// </summary>
    /// <remarks>
    /// 🔴 A fourth hit would be tier 4, which the reward table has no row for and
    /// <c>MINIGAME_SUBMIT</c> refuses — so a game that kept counting turns a perfect play into a
    /// rules refusal.
    /// </remarks>
    [Fact]
    public void A_strike_after_the_game_is_finished_scores_nothing()
    {
        var game = Game();
        var elapsed = 0.0;

        for (var strike = 0; strike < MinigameContent.Strikes; strike++)
        {
            elapsed = AimAndStrike(game, strike, insideTheWindow: true, elapsed);
        }

        game.Tier.ShouldBe(MinigameContent.Strikes, "the arrangement is a perfect game.");

        var pastTheEnd = (MinigameContent.Strikes * MinigameContent.SweepSeconds) +
                         (MinigameContent.SweepSeconds / 4);

        game.Advance(pastTheEnd - elapsed);

        game.Cursor.ShouldBe(
            Centre, Slack, "the fourth strike is aimed dead centre, so only the count refuses it.");
        game.Strike().ShouldBeFalse("there was no strike left to take.");
        game.Tier.ShouldBe(
            MinigameContent.Strikes,
            "a fourth hit is a tier the reward table has no row for, and MINIGAME_SUBMIT refuses it " +
            "— so a game that kept counting turns a perfect play into a refusal.");
        game.StrikesLeft.ShouldBe(0);
    }

    // ---- reduced motion --------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>Under reduced motion the clock moves nothing at all.</b>
    /// </summary>
    [Fact]
    public void Reduced_motion_makes_advancing_the_clock_a_no_op()
    {
        var game = Game(reducedMotion: true);

        game.ReducedMotion.ShouldBeTrue();

        game.Advance(MinigameContent.SweepSeconds / 4);

        game.Cursor.ShouldBe(
            0.0,
            Slack,
            "the same elapsed time moves a timed game to the middle of the bar. A reduced- " +
            "motion player has asked for nothing to move on its own, and a cursor that drifted " +
            "anyway is the animation they turned off.");
    }

    /// <summary>…and the step is what moves it instead, by the authored fraction.</summary>
    /// <remarks>
    /// 🔴 Four steps land exactly on the centre with the fixture's fraction, so the accessible arm
    /// can reach the top tier by counting rather than by timing. The ninth step is past the far end
    /// and folds back, which is what keeps the bar traversable in both directions.
    /// </remarks>
    [Theory]
    [InlineData(1, 0.125)]
    [InlineData(4, 0.5)]
    [InlineData(8, 1.0)]
    [InlineData(9, 0.875)]
    public void A_reduced_motion_step_moves_the_cursor_by_the_authored_fraction(
        int steps, double expected)
    {
        var game = Game(reducedMotion: true);

        for (var step = 0; step < steps; step++)
        {
            game.Step();
        }

        game.Cursor.ShouldBe(
            expected,
            Slack,
            steps + " steps of " + MinigameContent.ReducedMotionStepFraction + " should leave the " +
            "cursor at " + expected + " and left it at " + game.Cursor + ". The step is the only " +
            "way a reduced-motion player aims, so a step of the wrong size decides which tiers they " +
            "can reach at all.");
    }

    /// <summary>A reduced-motion game is scored by the same window as a timed one.</summary>
    /// <remarks>
    /// 🔒 The accessible arm is a different way to aim, not a different game: four steps land on the
    /// centre, and the strike there has to score exactly as a well-timed one does.
    /// </remarks>
    [Fact]
    public void A_reduced_motion_strike_is_judged_by_the_same_window()
    {
        var game = Game(reducedMotion: true);

        for (var step = 0; step < 4; step++)
        {
            game.Step();
        }

        game.Cursor.ShouldBe(Centre, Slack);
        game.Strike().ShouldBeTrue(
            "the cursor is dead centre. A reduced-motion player who cannot score a strike there is " +
            "playing a game they cannot win, while the timed arm is winnable.");
    }

    [Fact]
    public void A_null_rule_set_is_refused() =>
        Should.Throw<ArgumentNullException>(() => new TimingBarGame(null!));

    // ---- fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Waits until the cursor is where the case wants it, strikes, and answers the new elapsed time.
    /// </summary>
    /// <remarks>
    /// Aimed by advancing the clock rather than by setting the cursor, because there is no seam that
    /// sets it — which is the point: the only way a player aims is by waiting. Each strike is aimed
    /// at an ABSOLUTE time a whole sweep on from the last, so the cursor is back where it was and
    /// the clock never has to run backwards.
    /// </remarks>
    private static double AimAndStrike(
        TimingBarGame game, int strike, bool insideTheWindow, double elapsed)
    {
        var target = (strike * MinigameContent.SweepSeconds) +
                     (insideTheWindow
                         ? MinigameContent.SweepSeconds / 4
                         : MinigameContent.SweepSeconds / 2);

        game.Advance(target - elapsed);

        (Math.Abs(game.Cursor - Centre) <= MinigameContent.HitWindowHalfWidth).ShouldBe(
            insideTheWindow,
            "the arrangement left the cursor at " + game.Cursor + ", which is not where this strike " +
            "was meant to land — so whatever the strike answers is not evidence about the window.");

        game.Strike();

        return target;
    }

    private static TimingBarGame Game(bool reducedMotion = false) =>
        new(TimingBarRules.Read(MinigameContent.TimingBarOnly()), reducedMotion);
}
