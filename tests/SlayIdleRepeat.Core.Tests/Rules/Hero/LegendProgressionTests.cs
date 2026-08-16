using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="LegendProgression"/> — what reconciling a Legend Level against a lifetime XP total
/// answers, and what the level-ups grant.
/// </summary>
public sealed class LegendProgressionTests
{
    private static readonly ContentSnapshot Content = ProgressionDocuments.Shipped;

    private static readonly LegendCurveTuning Curve = LegendCurveTuning.Read(Content);

    private static readonly LegendTuning Range = LegendTuning.Read(Content);

    /// <summary>A player standing where their XP puts them gains nothing — the normal case.</summary>
    [Fact]
    public void A_player_already_at_their_level_gains_nothing()
    {
        var banks = new EnergyBanks(3, 0);

        var levelUp = LegendProgression.Reconcile(1, 0L, banks, Content);

        levelUp.Occurred.ShouldBeFalse();
        levelUp.TalentPointsGranted.ShouldBe(0L);
        levelUp.Banks.ShouldBe(banks, "no level-up, no refill: the banks come back untouched.");
    }

    /// <summary>Enough XP for one level grants one level and one Talent Point.</summary>
    [Fact]
    public void One_level_grants_one_Talent_Point()
    {
        var levelUp = LegendProgression.Reconcile(1, Needed(2), new EnergyBanks(0, 0), Content);

        levelUp.ToLevel.ShouldBe(2);
        levelUp.LevelsGained.ShouldBe(1);
        levelUp.TalentPointsGranted.ShouldBe(ProgressionDocuments.ShippedTalentPointsPerLevel);
    }

    /// <summary>
    /// Several levels at once grant a point per level, not one point for the whole reconciliation.
    /// </summary>
    /// <remarks>
    /// The case a run that banks a large payout actually produces, and the one a per-command
    /// increment would get wrong: `07` §1.1 grants a point PER LEVEL, so four levels owe four points.
    /// </remarks>
    [Fact]
    public void Several_levels_at_once_grant_a_point_each()
    {
        var levelUp = LegendProgression.Reconcile(1, Needed(5), new EnergyBanks(0, 0), Content);

        levelUp.LevelsGained.ShouldBe(4);
        levelUp.TalentPointsGranted.ShouldBe(4L * ProgressionDocuments.ShippedTalentPointsPerLevel);
    }

    /// <summary>
    /// 🔒 A level-up refills Energy to full at the <b>new</b> level's maximum.
    /// </summary>
    /// <remarks>
    /// `10` §3.1 refills to full and `10` §3 grows Max Energy with the level, so refilling against
    /// the old level would leave the player short by exactly what the level-up just gave them —
    /// which is why the expectation here is computed at the level AFTER, and the assertion below it
    /// pins that the two are different numbers.
    /// </remarks>
    [Fact]
    public void A_level_up_refills_Energy_to_the_new_levels_maximum()
    {
        var energy = EnergyTuning.Read(Content);

        var levelUp = LegendProgression.Reconcile(1, Needed(5), new EnergyBanks(0, 0), Content);

        energy.MaxEnergyAt(levelUp.ToLevel).ShouldBeGreaterThan(
            energy.MaxEnergyAt(1),
            "the fixture only discriminates while the two maxima differ.");

        levelUp.Banks.Energy.ShouldBe(energy.MaxEnergyAt(levelUp.ToLevel));
    }

    /// <summary>The Reserve is not filled by a level-up: `28` C2 gives it overflow only.</summary>
    [Fact]
    public void A_level_up_fills_the_bar_and_not_the_Reserve()
    {
        var levelUp = LegendProgression.Reconcile(1, Needed(3), new EnergyBanks(0, 0), Content);

        levelUp.Banks.Reserve.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 A steeper curve never takes a level back: the derived level is a floor to rise to.
    /// </summary>
    /// <remarks>
    /// The exponent is the pacing dial and is expected to move. A player at level 40 under the
    /// shipped curve derives to a lower level under a steeper one, and applying that would take back
    /// their Talent Points, their Max Energy and every gate they had passed. The second assertion is
    /// what makes the first mean something: it shows the derived level really is lower.
    /// </remarks>
    [Fact]
    public void A_steeper_curve_never_lowers_a_level()
    {
        var xp = (long)Math.Ceiling(LegendLevelCurve.CumulativeXpTo(40, Curve, Range));

        var steeperContent = ProgressionDocuments.With(legendXpExponent: ContentValue.Number(1.15m));
        var steeper = LegendCurveTuning.Read(steeperContent);

        LegendLevelCurve.LevelFor(xp, steeper, Range).ShouldBeLessThan(
            40, "the fixture only discriminates while the steeper curve really does derive lower.");

        var levelUp = LegendProgression.Reconcile(40, xp, new EnergyBanks(0, 0), steeperContent);

        levelUp.ToLevel.ShouldBe(40);
        levelUp.Occurred.ShouldBeFalse();
        levelUp.TalentPointsGranted.ShouldBe(0L);
    }

    /// <summary>The cap is a cap: XP past it grants no level and no point.</summary>
    [Fact]
    public void XP_past_the_cap_grants_nothing_further()
    {
        var levelUp = LegendProgression.Reconcile(
            Range.Maximum, long.MaxValue / 2, new EnergyBanks(0, 0), Content);

        levelUp.Occurred.ShouldBeFalse();
    }

    /// <summary>
    /// Reconciling twice over the same total grants once — the property that makes it safe on every
    /// command.
    /// </summary>
    [Fact]
    public void Reconciling_the_same_total_twice_grants_only_once()
    {
        var xp = Needed(3);
        var first = LegendProgression.Reconcile(1, xp, new EnergyBanks(0, 0), Content);
        var second = LegendProgression.Reconcile(first.ToLevel, xp, first.Banks, Content);

        second.Occurred.ShouldBeFalse();
        second.TalentPointsGranted.ShouldBe(0L);
    }

    /// <summary>The Talent Point rate is read from the document rather than assumed to be one.</summary>
    /// <remarks>
    /// The shipped rate is 1, which is also the literal anybody would write — so a grant that
    /// ignored the tunable would pass every case above. A content set whose rate is not 1 is what
    /// discriminates.
    /// </remarks>
    [Fact]
    public void The_Talent_Point_rate_is_read_from_the_document()
    {
        var generous = ProgressionDocuments.With(talentPointsPerLevel: ContentValue.Number(3));

        LegendProgression.Reconcile(1, Needed(5), new EnergyBanks(0, 0), generous)
            .TalentPointsGranted.ShouldBe(12L);
    }

    /// <summary>The lifetime total a level costs, as a whole number of banked XP.</summary>
    private static long Needed(int level) =>
        (long)Math.Ceiling(LegendLevelCurve.CumulativeXpTo(level, Curve, Range));
}
