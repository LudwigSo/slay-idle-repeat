using Shouldly;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;
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
    /// and part company with the rules on the first retune. The maximum is floored first (steering
    /// S32): two zeros agree as happily as two right answers.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_takes_every_energy_number_from_the_core_projection()
    {
        var row = HomeWorlds.Row(
            energy: new EnergyBanks(31, 0), energyAnchorUtc: HomeWorlds.Now - HomeWorlds.RegenInterval);

        var view = await HomeWorlds.Screen(row).GetViewModelAsync(Cancel);

        var expected = HomeEnergyView.Project(row, Worlds.Content, HomeWorlds.Now);

        expected.Max.ShouldBeGreaterThan(0, "a floor under the comparison below.");

        view.Energy.ShouldBe(expected.Current);
        view.EnergyMax.ShouldBe(expected.Max);
        view.EnergyCost.ShouldBe(expected.RunCost);
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
    /// 🔴 Reached through a scripted host because no shipped command produces this refusal:
    /// <c>Handlers.StartRun</c> never reads the run cost. See <see cref="ScriptedHomeHost"/> — the
    /// fixture exists precisely so the mapping is tested before the rule that fires it lands
    /// (steering S25).
    /// </remarks>
    [Fact]
    public async Task StartRunAsync_refuses_for_energy_with_the_shortfall_rather_than_throwing()
    {
        var row = HomeWorlds.Row(energy: new EnergyBanks(0, 0), energyAnchorUtc: HomeWorlds.Now);
        var host = ScriptedHomeHost.Refusing(row, RejectionReason.INSUFFICIENT_ENERGY, Worlds.Content);

        var outcome = await HomeWorlds.ScreenOver(host, row)
            .StartRunAsync(HomeWorlds.UnlockedChapter, Cancel);

        outcome.Result.ShouldBe(StartRunResult.InsufficientEnergy);
        outcome.EnergyShortfall.ShouldBe(
            HomeWorlds.RunCost,
            "both banks are empty, so the whole price is the shortfall. A screen answering the " +
            "price instead of the difference would tell a player with 19 of 20 that they need 20.");
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
    /// ⚠️ The stage recommendation and the reward line have no authorised source, and are absent
    /// rather than plausible.
    /// </summary>
    /// <remarks>
    /// <c>ParPowerTuning</c> and the chapter documents are <c>internal</c> to
    /// <c>SlayIdleRepeat.Core</c>, and the reference's reward line names no authored vocabulary at
    /// all. Steering S6: the hole stays greppable rather than being filled with a number that looks
    /// precise. Deleting this case is what records the day a route exists.
    /// </remarks>
    [Fact]
    public async Task GetViewModelAsync_leaves_the_unauthorised_stage_fields_absent()
    {
        var view = await HomeWorlds.Screen(HomeWorlds.Row()).GetViewModelAsync(Cancel);

        view.NextStageId.ShouldBeNull();
        view.NextStageName.ShouldBeNull();
        view.RecommendedPower.ShouldBeNull();
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
