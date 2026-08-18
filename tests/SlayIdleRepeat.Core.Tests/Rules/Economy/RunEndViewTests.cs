using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// <c>RunEndView</c> — the run-end moment: why a run is over, whether a revive is still on the table,
/// and what the payout will be.
/// </summary>
/// <remarks>
/// 🔒 <b>The load-bearing claim is that the payout shown is the payout PAID</b> (steering S2). Everything
/// else here is structural and would survive a screen that multiplied the banked figures itself — which
/// is the one failure that matters, because it parts company with <c>END_RUN</c> the first time a
/// multiplier is retuned, and the player believes the screen.
/// <see cref="The_payout_is_the_arithmetic_END_RUN_will_apply"/> is the case that says otherwise, and it
/// says it by APPLYING the command rather than by recomputing the multiplier here — a test doing its own
/// arithmetic would be the third implementation this projection exists to prevent, and would agree with
/// a wrong projection as happily as a right one.
/// </remarks>
public sealed class RunEndViewTests
{
    private static ContentSnapshot Content => TileWorlds.Context.Content;

    // ------------------------------------------------------------------ why the run is over

    /// <summary>A dead boss is a victory.</summary>
    [Fact]
    public void A_dead_boss_is_a_victory()
    {
        Project(RunEndWorlds.InProgress(bossDefeated: true)).Kind.ShouldBe(RunEndKind.Victory);
    }

    /// <summary>Zero hit points with the boss alive is a death.</summary>
    [Fact]
    public void Zero_hit_points_is_a_death()
    {
        Project(Dead()).Kind.ShouldBe(RunEndKind.Death);
    }

    /// <summary>And a living hero ending a live run is abandoning it.</summary>
    /// <remarks>
    /// Named rather than folded into a death: the two pay the same multiplier today, but they are
    /// different things to say to a player, and a screen that could not tell them apart would tell
    /// someone who quit that they had died.
    /// </remarks>
    [Fact]
    public void A_living_hero_ending_a_live_run_is_abandoning_it()
    {
        Project(RunEndWorlds.InProgress(currentHp: 50)).Kind.ShouldBe(RunEndKind.Abandoned);
    }

    // ------------------------------------------------------ 🔒 the payout, and it is the same one

    /// <summary>
    /// 🔒 The figures shown are the ones <c>END_RUN</c> pays, because both come from one arithmetic.
    /// </summary>
    [Fact]
    public void The_payout_is_the_arithmetic_END_RUN_will_apply()
    {
        var state = Dead(bankedLegendXp: 900, bankedSoulShards: 40);

        var shown = Project(state);
        var before = state.Player.LegendXp;

        var applied = SlayIdleRepeat.Core.GameRules.Apply(
            state, new EndRunCommand(), TileWorlds.Context);

        applied.Accepted.ShouldBeTrue("END_RUN was refused " + applied.Rejection + ".");

        (applied.NewState.Player.LegendXp - before).ShouldBe(
            shown.PayoutLegendXp,
            "the screen showed one Legend XP figure and END_RUN paid another, so the two are computing " +
            "the completion multiplier separately — and the player believes the screen.");
    }

    /// <summary>…and both sides of the multiplier are carried, because the difference is the point.</summary>
    /// <remarks>
    /// 🔒 `24` §9's S14 row wants the completion multiplier legible. A screen handed only the final
    /// number cannot show what dying cost; handed only the banked one it would promise a payout the
    /// player does not receive.
    /// </remarks>
    [Fact]
    public void A_death_pays_less_than_it_banked_and_the_view_shows_both()
    {
        var view = Project(Dead(bankedLegendXp: 900, bankedSoulShards: 40));

        view.BankedLegendXp.ShouldBe(900);
        view.PayoutLegendXp.ShouldBeLessThan(
            view.BankedLegendXp,
            "a death takes a completion multiplier below one, so a payout equal to the banked figure " +
            "means the multiplier never reached the arithmetic.");
    }

    // ------------------------------------------------------------------------ the revive offer

    /// <summary>A fresh death offers the revive.</summary>
    [Fact]
    public void A_fresh_death_offers_the_revive()
    {
        Project(Dead()).ReviveOffered.ShouldBeTrue();
    }

    /// <summary>…and a run that already used its one revive does not.</summary>
    /// <remarks>
    /// 🔒 `02` §6's limit is *once per run, hard*, counted on the run so a second run gets its own. The
    /// count is the same one <c>Handlers.Revive</c> refuses on, read rather than restated.
    /// </remarks>
    [Fact]
    public void A_run_that_already_revived_is_not_offered_another()
    {
        var used = Dead(adUses: new Dictionary<string, long> { [ReviveTuning.PlacementId] = 1 });

        Project(used).ReviveOffered.ShouldBeFalse(
            "02 §6's limit is once per run and hard, so a second offer is one the rules would refuse.");
    }

    /// <summary>…and a victory is offered none, however the row got there.</summary>
    [Fact]
    public void A_victory_is_offered_no_revive()
    {
        Project(RunEndWorlds.InProgress(bossDefeated: true, currentHp: 0)).ReviveOffered.ShouldBeFalse(
            "a run whose boss is dead has nothing to be revived for.");
    }

    /// <summary>
    /// 🔒 <b>The two ways a revive is unavailable are told APART, because they are two different
    /// sentences to a player.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <c>Handlers.Revive</c> refuses with <c>CAP_REACHED</c> for a run that has spent its one
    /// revive and with <c>ILLEGAL_STATE</c> for a run a revive does not apply to. Collapsed into a
    /// single "not offered", the screen has to guess — and the guess a screen naturally makes from
    /// <c>Kind == Death</c> is *"you already used it"*, which is said to a player who never revived at
    /// all. This is the case that makes the distinction load-bearing rather than decorative: both rows
    /// below report <c>ReviveOffered == false</c>, so nothing but the standing separates them.
    /// </remarks>
    [Fact]
    public void The_two_ways_a_revive_is_unavailable_are_told_apart()
    {
        var spent = Project(
            Dead(adUses: new Dictionary<string, long> { [ReviveTuning.PlacementId] = 1 }));

        spent.ReviveStanding.ShouldBe(
            RunEndReviveStanding.AlreadyUsed,
            "the run spent its one revive, which is the handler's CAP_REACHED arm.");

        var alive = Project(RunEndWorlds.InProgress(currentHp: 50));

        alive.ReviveStanding.ShouldBe(
            RunEndReviveStanding.NotApplicable,
            "a hero who is not at zero hit points has not lost anything to be revived from, which is " +
            "the handler's ILLEGAL_STATE arm — and telling this player they had already used their " +
            "revive would be false.");

        spent.ReviveOffered.ShouldBeFalse();
        alive.ReviveOffered.ShouldBeFalse();
    }

    /// <summary>…and an offered revive reports the standing that says so.</summary>
    /// <remarks>
    /// 🔒 Both members asserted on one row, because they are one field and its predicate: a stored
    /// second flag able to disagree with the standing is exactly what this pins shut.
    /// </remarks>
    [Fact]
    public void An_offered_revive_reports_both_the_standing_and_the_predicate()
    {
        var view = Project(Dead());

        view.ReviveStanding.ShouldBe(RunEndReviveStanding.Offered);
        view.ReviveOffered.ShouldBeTrue();
    }

    // ------------------------------------------- 24 §1.1 — the DROP_RUN counters in the footer

    /// <summary>Both authored dry-streak breakers reach the footer, whatever they stand at.</summary>
    /// <remarks>
    /// 🔒 `24` §9 names S14 for the <c>DROP_RUN</c> counters and §1.1's Visibility rule is *always*, so a
    /// counter standing at zero is exactly the case a "show it when it matters" reading would drop.
    /// </remarks>
    [Fact]
    public void Both_drop_run_counters_reach_the_footer()
    {
        var view = Project(Dead());

        view.DropCounters.Count.ShouldBe(2, "24 §4.3 authors two dry-streak breakers, elite and boss.");
        view.DropCounters
            .Select(counter => counter.Key)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(2, "the two force different bands, so they count under different keys.");
    }

    /// <summary>
    /// 🔒 <b>Each footer row says WHICH breaker it is, so the screen captions it from its identity
    /// rather than from its position.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 `24` §9 requires each counter to name its own unit, and the two units differ — D1 counts
    /// Elite kills, D2 counts boss kills. A screen reading these by index captions them correctly only
    /// while the list is exactly two long in exactly this order, and silently swaps the two the day a
    /// third breaker is authored between them. Asserted in `24` §4.3's own order, which is the order
    /// the projection is required to hand them back in.
    /// </remarks>
    [Fact]
    public void Each_footer_row_names_which_breaker_it_counts()
    {
        var counters = Project(Dead()).DropCounters;

        counters.Select(counter => counter.Kind).ShouldBe(
            [RunEndCounterKind.EliteMercy, RunEndCounterKind.BossMercy],
            "24 §4.3 states D1 the Elite mercy before D2 the Boss mercy, and the footer's captions are " +
            "chosen from these kinds — so a row carrying the wrong one captions boss kills as Elite.");
    }

    /// <summary>…and a fresh account counts down from each authored rung.</summary>
    [Fact]
    public void A_fresh_account_counts_down_from_each_rung()
    {
        Project(Dead()).DropCounters.ShouldAllBe(
            counter => counter.DropsUntilForced == counter.ForcedOnDrop,
            "nothing has been drawn yet, so each countdown is its whole rung.");
    }

    /// <summary>…and a counter standing above its rung still counts down to one, never to zero.</summary>
    [Fact]
    public void A_counter_standing_above_its_rung_still_counts_down_to_one()
    {
        var state = Dead();
        var keys = Project(state).DropCounters.Select(counter => counter.Key).ToArray();

        var standing = RunEndView.Project(
            PlayerSnapshots.With(pityCounters: keys.ToDictionary(key => key, _ => 400)),
            state.Run!.ToSnapshot(),
            Content);

        standing.DropCounters.ShouldAllBe(
            counter => counter.DropsUntilForced == 1,
            "'in 0 drops' describes a drop that has already happened, so one is the floor.");
        standing.DropCounters.ShouldAllBe(
            counter => counter.DropsStood == 400,
            "the standing is reported as it is, never clamped to the rung.");
    }

    // ---------------------------------------------------------------------------------- the doors

    /// <summary>Every argument is required.</summary>
    [Fact]
    public void The_projection_refuses_a_null_argument()
    {
        var state = Dead();
        var run = state.Run!.ToSnapshot();
        var player = state.Player.ToSnapshot();

        Should.Throw<ArgumentNullException>(() => RunEndView.Project(null!, run, Content));
        Should.Throw<ArgumentNullException>(() => RunEndView.Project(player, null!, Content));
        Should.Throw<ArgumentNullException>(() => RunEndView.Project(player, run, null!));
    }

    // ------------------------------------------------------------------------------- fixtures

    /// <summary>A run standing dead on an unresolved stage-1 fight — where S13 opens.</summary>
    private static WorldSlice Dead(
        long bankedLegendXp = 0,
        long bankedSoulShards = 0,
        IReadOnlyDictionary<string, long>? adUses = null) =>
        RunEndWorlds.InProgress(
            currentHp: 0,
            bankedLegendXp: bankedLegendXp,
            bankedSoulShards: bankedSoulShards,
            pendingTileStage: 1,
            hasPendingTile: true,
            adUses: adUses);

    private static RunEndView Project(WorldSlice state) =>
        RunEndView.Project(state.Player.ToSnapshot(), state.Run!.ToSnapshot(), Content);
}
