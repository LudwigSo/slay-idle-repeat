using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// <c>HomeEnergyView</c> — the four numbers the Home screen's energy pill draws, and the only
/// sanctioned route out of <c>Core</c> for any of them.
/// </summary>
/// <remarks>
/// 🔒 <b>The load-bearing claim is that every number is READ, not restated.</b> The maximum, the
/// countdown and the run cost are all functions of <c>tuning/progression.json</c>, and a projection
/// carrying its own copy would agree with the shipped file today and disagree with it on the first
/// retune — which is why <see cref="A_retuned_energy_block_moves_all_three_derived_numbers"/> varies
/// the document rather than the row, and why nothing here compares against a bare literal that the
/// hermetic fixture does not also state.
/// </remarks>
public sealed class HomeEnergyViewTests
{
    /// <summary>The instant every case projects at unless it is about the anchor.</summary>
    private static readonly DateTimeOffset Now = PlayerSnapshots.Midmorning;

    private static readonly TimeSpan RegenInterval =
        TimeSpan.FromMinutes(ProgressionDocuments.ShippedRegenMinutesPerPoint);

    // ------------------------------------------------------------------------ the maximum

    /// <summary>
    /// The maximum is the tuning's, at the row's Legend Level: the base plus the per-level
    /// increment, stopped at the cap.
    /// </summary>
    /// <remarks>
    /// Level 1 is the starting player, 41 is where the cap first binds — the increment counts levels
    /// GAINED — and 42 is one past it, so a projection that had dropped the cap reads 202 there.
    /// </remarks>
    [Theory]
    [InlineData(1, 120)]
    [InlineData(41, 200)]
    [InlineData(42, 200)]
    public void Max_is_the_tuning_maximum_at_the_rows_legend_level(int legendLevel, int expected) =>
        Project(Row(legendLevel: legendLevel)).Max.ShouldBe(expected);

    // ------------------------------------------------------------------------ the main bar

    /// <summary>The bar is what it holds after the time since the anchor has accrued into it.</summary>
    /// <remarks>
    /// Three whole intervals since the anchor, so a projection that ignored the anchor answers 10
    /// and one that accrued to the instant rather than in whole units answers 13.5 rounded some way.
    /// </remarks>
    [Fact]
    public void Current_is_the_main_bar_after_accrual_to_the_instant_asked_about() =>
        Project(Row(energy: new EnergyBanks(10, 0), anchor: Now - (3 * RegenInterval)))
            .Current.ShouldBe(
                13,
                "three whole regeneration intervals have passed since the anchor, so three points " +
                "have accrued into a bar that held ten.");

    /// <summary>
    /// An anchor in the future is a clock that moved backwards, and this seam clamps it rather than
    /// throwing.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>EnergyMath.Accrue</c> throws on a negative span BY DESIGN, so that a persisted anchor
    /// stuck in the future cannot go undetected in a command. This is a READ, and a screen is not
    /// the place to surface that fault: the pill would take the whole Home screen down with it. The
    /// clamp is therefore here, and it is the difference between a stale number and a crash.
    /// </remarks>
    [Fact]
    public void Current_is_the_stored_bar_when_the_anchor_is_in_the_future() =>
        Project(Row(energy: new EnergyBanks(37, 0), anchor: Now + TimeSpan.FromHours(1)))
            .Current.ShouldBe(
                37,
                "nothing has accrued, because no time has passed. A projection that handed the " +
                "negative span straight to EnergyMath.Accrue throws out of the read instead.");

    // -------------------------------------------------------------------- the countdown

    /// <summary>Both banks full is nothing accruing anywhere, and the pill hides its caption.</summary>
    /// <remarks>
    /// 🔒 Zero is the instruction to hide the caption, so it must mean "nothing is coming" and
    /// nothing else. The bar alone being full is <see cref="RefillIn_stays_positive_when_the_bar_is_full_but_the_reserve_is_not"/>,
    /// where a countdown is still running into the Reserve.
    /// </remarks>
    [Fact]
    public void RefillIn_is_zero_when_both_banks_are_full() =>
        Project(Row(energy: Full(), anchor: Now)).RefillIn.ShouldBe(
            TimeSpan.Zero,
            "both banks are at capacity, so no elapsed time can add a point anywhere and the " +
            "caption has nothing to count down to.");

    /// <summary>A second before the next point, the countdown reads a second.</summary>
    /// <remarks>
    /// One interval minus one second since the anchor: no whole unit has accrued yet, so the answer
    /// is the second that is left. A projection counting from the anchor forwards rather than to the
    /// next boundary would answer one interval minus one second.
    /// </remarks>
    [Fact]
    public void RefillIn_is_one_second_when_the_next_point_is_a_second_away() =>
        Project(Row(energy: new EnergyBanks(0, 0), anchor: Now - RegenInterval + TimeSpan.FromSeconds(1)))
            .RefillIn.ShouldBe(TimeSpan.FromSeconds(1));

    /// <summary>
    /// An anchor far in the past never leaves the countdown negative: it counts to the NEXT
    /// boundary, not from a boundary long gone.
    /// </summary>
    /// <remarks>
    /// Ten hours is an exact multiple of the four-minute interval, which is the hostile case: the
    /// remainder is nothing, and the next point is a whole interval away rather than zero away. Ten
    /// hours at one point per four minutes is 150 points, which fills the 120-point bar and leaves
    /// 30 in the Reserve — so the Reserve is not full and the countdown is genuinely still running.
    /// </remarks>
    [Fact]
    public void RefillIn_stays_positive_when_the_bar_is_full_but_the_reserve_is_not()
    {
        var view = Project(Row(energy: new EnergyBanks(0, 0), anchor: Now - TimeSpan.FromHours(10)));

        view.RefillIn.ShouldBe(
            RegenInterval,
            "the elapsed span lands exactly on a boundary, so the next point is a whole interval " +
            "away. A projection subtracting the elapsed time from the interval answers zero or a " +
            "negative span here, and the pill hides a caption that is still counting.");
    }

    // ------------------------------------------------------------------------ the run cost

    /// <summary>
    /// 🔒 All three derived numbers move with the document, because none of them is written here.
    /// </summary>
    /// <remarks>
    /// The one discriminating retune: a base of 60 with no per-level growth, a one-minute interval
    /// and a cost of 7. A projection carrying the shipped 120 / four minutes / 20 answers the
    /// shipped numbers against a document that says otherwise, and every reader believes it.
    /// </remarks>
    [Fact]
    public void A_retuned_energy_block_moves_all_three_derived_numbers()
    {
        var retuned = ProgressionDocuments.With(
            baseMax: ContentValue.Number(60),
            perLegendLevel: ContentValue.Number(0),
            regenMinutesPerPoint: ContentValue.Number(1),
            runCost: ContentValue.Number(7));

        var view = HomeEnergyView.Project(
            Row(energy: new EnergyBanks(0, 0), anchor: Now - TimeSpan.FromSeconds(30)), retuned, Now);

        view.Max.ShouldBe(60, "the retuned document authors a base of 60 and no per-level growth.");
        view.RunCost.ShouldBe(7, "the retuned document authors a run cost of 7.");
        view.RefillIn.ShouldBe(
            TimeSpan.FromSeconds(30),
            "the retuned interval is one minute and half of it has passed since the anchor.");
    }

    // -------------------------------------------------------------------------------- fixtures

    /// <summary>Both banks at capacity for a Legend Level 1 row: 120 in each.</summary>
    private static EnergyBanks Full() =>
        new(ProgressionDocuments.ShippedBaseMax, ProgressionDocuments.ShippedBaseMax);

    private static PlayerSnapshot Row(
        int legendLevel = 1, EnergyBanks? energy = null, DateTimeOffset? anchor = null) =>
        PlayerSnapshots.With(
            legendLevel: legendLevel,
            energy: energy ?? new EnergyBanks(0, 0),
            energyAnchorUtc: anchor ?? Now);

    private static HomeEnergyView Project(PlayerSnapshot row) =>
        HomeEnergyView.Project(row, ProgressionDocuments.Shipped, Now);
}
