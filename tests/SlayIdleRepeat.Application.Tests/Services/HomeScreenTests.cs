using Shouldly;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services;

/// <summary>
/// <c>HomeScreen</c> — what the hub draws, and the one thing it does.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Two claims carry this suite.</b> That every Energy number is the one
/// <see cref="HomeEnergyView"/> answers rather than a second arithmetic at this layer, and that each
/// of the three ways a start can end is distinguished by WHICH condition fired rather than by an
/// enum member alone (steering S2) — so every refusal case asserts the payload the screen will
/// actually say out loud.
/// </para>
/// <para>
/// The rows are real starting players edited one field at a time and served through the real
/// in-process host over the in-memory cache. No real adapter is composed anywhere.
/// </para>
/// </remarks>
public sealed class HomeScreenTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const string FixtureName = "Ryn the Fixture";

    private const long FixtureCrowns = 412_345L;

    /// <summary>
    /// A Legend Level a starting player is nowhere near, so the badge's number has to have come
    /// off the row.
    /// </summary>
    private const int FixtureLegendLevel = 37;

    // ------------------------------------------------------------------- what the header carries

    [Fact]
    public async Task GetViewModelAsync_carries_the_display_name_the_row_holds()
    {
        var screen = HomeWorlds.Screen(HomeWorlds.Row(displayName: FixtureName));

        var view = await screen.GetViewModelAsync(Cancel);

        view.PlayerName.ShouldBe(
            FixtureName,
            "the name beside the avatar is the row's, not the host's idea of a default. A screen " +
            "drawing someone else's name is a screen about someone else.");
    }

    /// <summary>
    /// 🔴 The Legend Level on the avatar's badge is the row's, and not a number this layer settled
    /// on.
    /// </summary>
    /// <remarks>
    /// Its own case because nothing had one: the criterion says the view model carries the name
    /// AND the Legend Level, the case above asserts only the name, and every fixture here was a
    /// starting player. Proved by mutation — the view model handing out a hard <c>0</c> left all
    /// 1857 cases green, and the badge on the avatar would have read zero for every player in the
    /// game.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_carries_the_Legend_Level_the_row_holds()
    {
        var screen = HomeWorlds.Screen(HomeWorlds.Row(legendLevel: FixtureLegendLevel));

        var view = await screen.GetViewModelAsync(Cancel);

        view.PlayerLevel.ShouldBe(
            FixtureLegendLevel,
            "the badge on the avatar is the row's level. A screen answering the starting level to " +
            "everyone tells a player who has earned thirty-six of them that they have earned none.");
    }

    /// <summary>
    /// 🔒 Crowns, and Crowns is a different balance from the Gold a run carries.
    /// </summary>
    /// <remarks>
    /// Gold is run-scoped and wiped at run end, so a between-runs screen showing it would show a
    /// balance about to stop existing. The fixture sets only Crowns, so a view model reading any
    /// other wallet row answers the starting zero.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_carries_the_Crowns_balance()
    {
        var screen = HomeWorlds.Screen(HomeWorlds.Row(crowns: FixtureCrowns));

        var view = await screen.GetViewModelAsync(Cancel);

        view.Crowns.ShouldBe(FixtureCrowns);
    }

    /// <summary>
    /// 🔒 Every Energy number is <see cref="HomeEnergyView"/>'s, not a second arithmetic here.
    /// </summary>
    /// <remarks>
    /// Asserted against the projection rather than against literals, because the claim is that the
    /// two agree — a view model computing its own maximum would agree with the shipped tuning today
    /// and part company with the rules on the first retune. Every number compared is floored on the
    /// projection's side first (steering S32): two zeros agree as happily as two right answers, and
    /// the row is built short of a run's price precisely so the shortfall has something to be.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_takes_every_energy_number_from_the_core_projection()
    {
        var row = HomeWorlds.Row(
            energy: new EnergyBanks(3, 0), energyAnchorUtc: HomeWorlds.Now - HomeWorlds.RegenInterval);

        var view = await HomeWorlds.Screen(row).GetViewModelAsync(Cancel);

        var expected = HomeEnergyView.Project(row, Worlds.Content, HomeWorlds.Now);

        expected.Current.ShouldBeGreaterThan(0, "a floor under the comparison below.");
        expected.Max.ShouldBeGreaterThan(0, "a floor under the comparison below.");
        expected.RunCost.ShouldBeGreaterThan(0, "a floor under the comparison below.");
        expected.Shortfall.ShouldBeGreaterThan(0, "a floor under the comparison below.");

        view.Energy.ShouldBe(expected.Current);
        view.EnergyMax.ShouldBe(expected.Max);
        view.EnergyCost.ShouldBe(expected.RunCost);
        view.EnergyShortfall.ShouldBe(expected.Shortfall);
    }

    /// <summary>
    /// 🔒 The shortfall counts the Reserve, so a player whose Reserve covers the run is not told
    /// they cannot afford it.
    /// </summary>
    /// <remarks>
    /// The bar alone is three short of the price and the Reserve holds far more than the difference.
    /// <c>EnergyMath.Spend</c> draws the bar first and the Reserve for the remainder, so this player
    /// can start the run — and a view model subtracting <c>Energy</c> from <c>EnergyCost</c> answers
    /// three, which is the launch block's instruction to offer a refill instead of a start.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_reports_no_shortfall_when_the_reserve_covers_what_the_bar_cannot()
    {
        var row = HomeWorlds.Row(
            energy: new EnergyBanks(HomeWorlds.RunCost - 3, HomeWorlds.RunCost),
            energyAnchorUtc: HomeWorlds.Now);

        var view = await HomeWorlds.Screen(row).GetViewModelAsync(Cancel);

        view.Energy.ShouldBeLessThan(
            view.EnergyCost, "the fixture is only discriminating while the bar alone is short.");
        view.EnergyShortfall.ShouldBe(
            0,
            "the Reserve covers what the bar does not, so the run is affordable and nothing is " +
            "missing. A screen reading affordability off the main bar refuses this player.");
    }

    // ------------------------------------------------------------------------ the countdown

    /// <summary>
    /// The caption's countdown, at the three instants that decide what it says: full, a second out,
    /// and an anchor long past.
    /// </summary>
    /// <remarks>
    /// Driven through the clock rather than by handing the screen an instant, because the seam is
    /// where the reading enters: a screen that captured "now" at construction shows the countdown
    /// it had when it was built for as long as it stays open.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_hides_the_countdown_by_answering_zero_when_both_banks_are_full()
    {
        var full = HomeWorlds.Row(
            energy: FullBanks(), energyAnchorUtc: HomeWorlds.Now);

        var view = await HomeWorlds.Screen(full).GetViewModelAsync(Cancel);

        view.EnergyRefillIn.ShouldBe(
            TimeSpan.Zero,
            "zero is the pill's instruction to hide its caption, and both banks being at capacity " +
            "is the one state in which nothing anywhere is counting down.");
    }

    [Fact]
    public async Task GetViewModelAsync_counts_down_the_second_that_is_left_before_the_next_point()
    {
        var row = HomeWorlds.Row(
            energy: new EnergyBanks(0, 0),
            energyAnchorUtc: HomeWorlds.Now - HomeWorlds.RegenInterval + TimeSpan.FromSeconds(1));

        var view = await HomeWorlds.Screen(row).GetViewModelAsync(Cancel);

        view.EnergyRefillIn.ShouldBe(TimeSpan.FromSeconds(1));
    }

    /// <summary>An anchor long past never leaves the caption counting backwards.</summary>
    [Fact]
    public async Task GetViewModelAsync_keeps_the_countdown_positive_when_the_anchor_is_long_past()
    {
        var row = HomeWorlds.Row(
            energy: new EnergyBanks(0, 0), energyAnchorUtc: HomeWorlds.Now - TimeSpan.FromHours(10));

        var view = await HomeWorlds.Screen(row).GetViewModelAsync(Cancel);

        view.EnergyRefillIn.ShouldBeGreaterThan(
            TimeSpan.Zero,
            "the bar is full but the Reserve is not, so a point is still coming — and a countdown " +
            "computed from a boundary ten hours gone would be a negative span rendered as a caption.");
    }

    // ---------------------------------------------------------------------------- the power

    [Fact]
    public async Task GetViewModelAsync_carries_the_index_the_power_source_read()
    {
        var screen = HomeWorlds.Screen(
            HomeWorlds.Row(), power: StubHeroPower.Computing(HomeWorlds.DefaultPowerIndex));

        var view = await screen.GetViewModelAsync(Cancel);

        view.Power.ShouldBe(HomeWorlds.DefaultPowerIndex);
    }

    /// <summary>A reading that could not be taken arrives as an absence, never as a zero.</summary>
    /// <remarks>
    /// Zero is a real reading of a hero wearing nothing, and it is not what happened. A pill showing
    /// zero power to a geared hero is the screen lying about the build the player just spent an hour on.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_carries_no_power_at_all_when_the_source_could_not_read_one()
    {
        var screen = HomeWorlds.Screen(
            HomeWorlds.Row(), power: StubHeroPower.Standing(HeroPowerStanding.ContentUnavailable));

        var view = await screen.GetViewModelAsync(Cancel);

        view.Power.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 A reading cannot carry a number beside an absence, and the TYPE is what refuses it.
    /// </summary>
    /// <remarks>
    /// <c>HeroPowerReading</c> says "the index when — and only when — one was computed", and two
    /// callers depend on that sentence differently: <c>HomeScreen</c> takes <c>PowerIndex</c>
    /// without consulting the standing at all, while <c>HomePresenter.ReadPower</c> guards on it.
    /// While the invariant was upheld only by the implementations and a test fake, a source that
    /// broke it would have put a stale power on one of the two screens and an absence on the other,
    /// and neither reader could have been called wrong. Stated on the type, both are right by
    /// construction.
    /// </remarks>
    /// <param name="standing">Each of the four standings that means no reading was taken.</param>
    [Theory]
    [InlineData(HeroPowerStanding.BuildNotAggregable)]
    [InlineData(HeroPowerStanding.RowNotRehydratable)]
    [InlineData(HeroPowerStanding.LegendLevelOutsideCurve)]
    [InlineData(HeroPowerStanding.ContentUnavailable)]
    public void A_reading_that_took_no_measurement_refuses_to_carry_a_number(HeroPowerStanding standing) =>
        Should.Throw<ArgumentException>(() => new HeroPowerReading(standing, HomeWorlds.DefaultPowerIndex))
              .ParamName.ShouldBe(
                  "PowerIndex",
                  "a number beside an absence is drawn on the pill as a power the source never " +
                  "measured, and the screen that reads the index without the standing cannot tell.");

    /// <summary>…and a computed reading cannot carry nothing, which is the same defect inverted.</summary>
    /// <remarks>
    /// Both arms are needed and neither implies the other: a rule stated only over the absences
    /// would let <c>Computed</c> with no index through, and that is a screen reporting a hero it did
    /// not measure — to the reader that trusts the standing rather than the number.
    /// </remarks>
    [Fact]
    public void A_computed_reading_refuses_to_carry_no_number() =>
        Should.Throw<ArgumentException>(
                  () => new HeroPowerReading(HeroPowerStanding.Computed, PowerIndex: null))
              .ParamName.ShouldBe("PowerIndex");

    /// <summary>The two shapes a reading legitimately takes are both still constructible.</summary>
    /// <remarks>
    /// A negative control on the two rules above: a guard that refused every reading would satisfy
    /// both of them and take the power pill off the screen entirely.
    /// </remarks>
    [Fact]
    public void The_two_readings_a_source_can_legitimately_take_are_both_accepted()
    {
        new HeroPowerReading(HeroPowerStanding.Computed, HomeWorlds.DefaultPowerIndex).PowerIndex
            .ShouldBe(HomeWorlds.DefaultPowerIndex);

        new HeroPowerReading(HeroPowerStanding.ContentUnavailable, PowerIndex: null).PowerIndex
            .ShouldBeNull();
    }

    // ------------------------------------------------------------------- the three outcomes

    /// <summary>A start that the rules accept reports the run it started.</summary>
    /// <remarks>
    /// The run id, not merely the enum member: a screen that answered <c>Started</c> without one
    /// could not hand the next screen the run the player is now in.
    /// </remarks>
    [Fact]
    public async Task StartRunAsync_reports_the_run_it_started()
    {
        var screen = HomeWorlds.Screen(HomeWorlds.Row());

        var outcome = await screen.StartRunAsync(HomeWorlds.UnlockedChapter, Cancel);

        outcome.Result.ShouldBe(StartRunResult.Started);
        outcome.Run.ShouldNotBeNull(
            "a started run the screen cannot name is a run nothing can navigate to.");
    }

    /// <summary>
    /// 🔒 A stage the ladder has not opened is refused as a value, and the refusal names the stage.
    /// </summary>
    /// <remarks>
    /// The stage id is what makes this refusal distinguishable from the other (steering S2): both
    /// end in a button that does not start a run, and only the payload says which sentence the
    /// player is owed.
    /// </remarks>
    [Fact]
    public async Task StartRunAsync_refuses_a_locked_stage_and_names_it()
    {
        var screen = HomeWorlds.Screen(HomeWorlds.Row());

        var outcome = await screen.StartRunAsync(HomeWorlds.LockedChapter, Cancel);

        outcome.Result.ShouldBe(StartRunResult.StageLocked);
        outcome.LockedStageId.ShouldBe(HomeWorlds.LockedChapter);
        outcome.Run.ShouldBeNull("nothing was started, so there is no run to name.");
    }

    /// <summary>
    /// 🔒 A start refused for Energy says by how much, and does not throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The banks hold part of the price, so the shortfall and the price are DIFFERENT numbers —
    /// which is the only fixture in which the claim can be checked. Six in the bar and three in the
    /// Reserve against a price of twenty is a shortfall of eleven; a screen answering the price
    /// instead of the difference answers twenty and tells a player holding nine that they need
    /// twenty more.
    /// </para>
    /// <para>
    /// 🔴 Reached through a scripted host because no shipped command produces this refusal:
    /// <c>Handlers.StartRun</c> never reads the run cost. See <see cref="ScriptedHomeHost"/> — the
    /// fixture exists precisely so the mapping is tested before the rule that fires it lands
    /// (steering S25).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StartRunAsync_refuses_for_energy_with_the_shortfall_rather_than_throwing()
    {
        var row = HomeWorlds.Row(energy: new EnergyBanks(6, 3), energyAnchorUtc: HomeWorlds.Now);
        var host = ScriptedHomeHost.Refusing(row, RejectionReason.INSUFFICIENT_ENERGY, Worlds.Content);

        var outcome = await HomeWorlds.ScreenOver(host, row)
            .StartRunAsync(HomeWorlds.UnlockedChapter, Cancel);

        outcome.Result.ShouldBe(StartRunResult.InsufficientEnergy);
        outcome.EnergyShortfall.ShouldBe(
            HomeWorlds.RunCost - 9,
            "the two banks hold nine of the price between them, so nine is what the refusal must " +
            "subtract. Answering the price itself is the same number in every fixture where the " +
            "banks are empty, and a different one for every player who is only nearly there.");
    }

    // ----------------------------------------------------------------------------- the badges

    /// <summary>Each tab's dot is its own, and reading one never answers another's.</summary>
    /// <remarks>
    /// One tab lit at a time, five times over: a record wired to read the same field for two tabs
    /// passes any case that lights them together, and fails every row here.
    /// </remarks>
    [Theory]
    [InlineData(HomeTab.Home)]
    [InlineData(HomeTab.Gear)]
    [InlineData(HomeTab.Talents)]
    [InlineData(HomeTab.Collection)]
    [InlineData(HomeTab.Shop)]
    public void On_answers_for_the_tab_asked_about_and_no_other(HomeTab lit)
    {
        var badges = OnlyLit(lit);
        var others = Tabs().Where(tab => tab != lit).ToArray();

        badges.On(lit).ShouldBeTrue($"{lit} is the tab this fixture lit.");

        others.Length.ShouldBe(4, "a floor: the rule below is stated over the four tabs left dark.");
        others.ShouldAllBe(
            tab => !badges.On(tab),
            "a dot on a tab with nothing behind it sends the player to a screen that has nothing to " +
            "show them, and two tabs reading one flag light together.");
    }

    /// <summary>
    /// ⚠️ Nothing lights a tab yet, and the screen must not invent one.
    /// </summary>
    /// <remarks>
    /// No system in this build records "the player has not seen this", so a dot drawn today would be
    /// a claim nothing can substantiate (steering S6). The floor is the tab list itself: the case
    /// asks every one of the five, so it cannot pass by asking none.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_lights_no_tab_until_something_authorises_one()
    {
        var view = await HomeWorlds.Screen(HomeWorlds.Row()).GetViewModelAsync(Cancel);

        var tabs = Tabs();

        tabs.Length.ShouldBe(5, "a floor: the rule below is stated over all five tabs, never none.");
        tabs.ShouldAllBe(
            tab => !view.Badges.On(tab),
            "a dot means a system has something the player has not seen, and no system records that " +
            "yet. Deleting this case is the deliberate act that records the day one does.");
    }

    // -------------------------------------------------------------------- the declared holes

    /// <summary>
    /// 🔒 The stage and its recommendation ARE authored, and both reach the view model — which is
    /// what makes the launch block's Underpowered state reachable outside a test fake.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route is <c>Core.Rules.Board.NextChapterView</c>, the par table's public projection —
    /// the same construction as <c>HomeEnergyView</c>. A brand-new profile has cleared nothing, so
    /// the chapter it is pointed at is the campaign's first, and the recommendation is that
    /// chapter's own Normal cell in <c>tuning/par_power.json</c>.
    /// </para>
    /// <para>
    /// Asserted against the projection rather than against a literal, for the reason every Energy
    /// number is: a view model carrying its own copy of the par table would agree with the shipped
    /// file today and part company with it on the first retune. The floor is on the projection's
    /// side (steering S32) — two nulls agree as happily as two right answers.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_carries_the_stage_the_campaign_offers_next_and_its_recommendation()
    {
        var row = HomeWorlds.Row();

        var view = await HomeWorlds.Screen(row).GetViewModelAsync(Cancel);

        var expected = NextChapterView.Project(row, Worlds.Content);

        expected.ShouldNotBeNull("a floor: the comparison below is only evidence if there is a stage.");
        expected.RecommendedPower.ShouldBeGreaterThan(0d, "a floor under the comparison below.");

        view.NextStageId.ShouldBe(expected.ChapterId);
        view.RecommendedPower.ShouldBe(expected.RecommendedPower);
    }

    /// <summary>
    /// ⚠️ The stage's NAME and the reward line have no authorised source, and stay absent rather
    /// than plausible.
    /// </summary>
    /// <remarks>
    /// A chapter document authors its name as a loc key (<c>loc.chapter.1.name</c>) and resolving
    /// one is <c>LocaleStringCatalogue</c>'s job in the client assembly, which neither this layer
    /// nor <c>Core</c> may reference — so a key carried in a field called a name would be drawn on
    /// screen as if it were one, and the client names the chapter from <c>NextStageId</c> instead.
    /// Nothing in <c>game-data/</c> authors a reward vocabulary at all. Steering S6: both holes stay
    /// greppable rather than being filled with something that looks precise. Deleting this case is
    /// what records the day a route exists.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_leaves_the_unauthorised_stage_fields_absent()
    {
        var view = await HomeWorlds.Screen(HomeWorlds.Row()).GetViewModelAsync(Cancel);

        view.NextStageName.ShouldBeNull();
        view.RewardTags.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------------- fixtures

    private static HomeTab[] Tabs() =>
        [HomeTab.Home, HomeTab.Gear, HomeTab.Talents, HomeTab.Collection, HomeTab.Shop];

    private static HomeBadges OnlyLit(HomeTab tab) => new(
        Home: tab == HomeTab.Home,
        Gear: tab == HomeTab.Gear,
        Talents: tab == HomeTab.Talents,
        Collection: tab == HomeTab.Collection,
        Shop: tab == HomeTab.Shop);

    /// <summary>
    /// Both banks at capacity for a Legend Level 1 row: the authored base, in each, read from the
    /// shipped document rather than from the projection under test.
    /// </summary>
    private static EnergyBanks FullBanks() =>
        new(HomeWorlds.MaxAtStartingLevel, HomeWorlds.MaxAtStartingLevel);
}
