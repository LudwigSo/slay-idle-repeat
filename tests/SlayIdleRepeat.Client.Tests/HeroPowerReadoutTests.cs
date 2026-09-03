using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The power tile's source: the one derived number the Home screen shows, read through the rules'
/// own calculator over the rules' own hero, and every way that reading can have no answer.
/// </summary>
/// <remarks>
/// Over the checkout's own content, because the hero curve, the gear catalogue and the power model
/// are what the reading is a function of — a fixture content set would be a second copy of all three.
/// </remarks>
public sealed class HeroPowerReadoutTests
{
    private static readonly PlayerId Profile = new("PLAYER_power_7d10");

    /// <summary>The top of the authored curve (`05` §2: Legend Level 1..200).</summary>
    private const int TopOfCurve = 200;

    private const string PowerModelDocument = "tuning/power_model.json";

    [Fact]
    public void Read_computes_a_positive_index_for_a_row_at_the_top_of_the_authored_curve()
    {
        var reading = Readout().Read(PlayerState.Rehydratable(Profile, TopOfCurve), run: null);

        reading.Standing.ShouldBe(HeroPowerStanding.Computed, "level 200 is authored, so there is a hero to measure.");
        reading.PowerIndex.ShouldNotBeNull().ShouldBeGreaterThan(
            0.0, "sqrt(EffectiveHP × DPS) of a hero with any health and any attack is above zero.");
    }

    /// <summary>The other half of the S1 pair: one level past the curve is no hero at all.</summary>
    [Fact]
    public void Read_reports_LegendLevelOutsideCurve_one_level_past_the_authored_curve()
    {
        var reading = Readout().Read(PlayerState.Rehydratable(Profile, TopOfCurve + 1), run: null);

        reading.Standing.ShouldBe(
            HeroPowerStanding.LegendLevelOutsideCurve,
            "the base curve is authored to 200 and refuses 201 by name. Read as a row fault instead, " +
            "the tile would blame the profile for a level the curve simply has not been extended to.");
        reading.PowerIndex.ShouldBeNull("no curve point, no stat block, no number.");
    }

    /// <summary>
    /// <c>PlayerState.Player</c> leaves the wallet, inventory and loadout at defaults the domain
    /// refuses, which is exactly the row this standing exists for.
    /// </summary>
    [Fact]
    public void Read_reports_RowNotRehydratable_for_a_row_the_domain_refuses()
    {
        var reading = Readout().Read(PlayerState.Player(Profile), run: null);

        reading.Standing.ShouldBe(
            HeroPowerStanding.RowNotRehydratable,
            "a row the aggregate will not accept has no hero in it. Letting the refusal out would take " +
            "the whole Home read down for a tile that could simply be empty.");
        reading.PowerIndex.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 The reading is over <c>Stats</c>, not <c>BaseStats</c>. A blade in hand contributes effects,
    /// and only the aggregated block shows them — a readout over the base block reports the same
    /// number naked as armed and this case is the one that notices.
    /// </summary>
    [Fact]
    public void Read_counts_worn_gear_rather_than_the_bare_curve()
    {
        var bare = PlayerState.Rehydratable(Profile, legendLevel: 40);
        var armed = WearingABlade(bare);

        var bareIndex = Readout().Read(bare, run: null).PowerIndex.ShouldNotBeNull();
        var armedIndex = Readout().Read(armed, run: null).PowerIndex.ShouldNotBeNull();

        armedIndex.ShouldBeGreaterThan(
            bareIndex,
            "a weapon adds attack, and attack is in DPS. A readout that could not see the difference " +
            "is reading the curve alone, and the tile would never move when the player equips anything.");
    }

    /// <summary>
    /// The shipped content set with the power model taken out — every other document intact, so the
    /// hero still composes and the failure is the calculator's read and nothing earlier.
    /// </summary>
    [Fact]
    public void Read_reports_ContentUnavailable_when_the_content_set_has_no_power_model()
    {
        var reading = new HeroPowerReadout(Without(BootContent.Shipped, PowerModelDocument))
            .Read(PlayerState.Rehydratable(Profile, TopOfCurve), run: null);

        reading.Standing.ShouldBe(
            HeroPowerStanding.ContentUnavailable,
            "a content set missing the model has no weights to evaluate against. That is a fact " +
            "about the content, not about the row or the level, and the tile has to be able to say so.");
        reading.PowerIndex.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------- fixtures

    private static HeroPowerReadout Readout() => new(BootContent.Shipped);

    private static PlayerSnapshot WearingABlade(PlayerSnapshot row)
    {
        var blade = new GearInstanceId("blade");

        return row with
        {
            Inventory = new InventorySnapshot(
                0,
                [
                    new GearInstanceSnapshot(
                        blade,
                        DefId: "GEAR_WEAPON_BLADE",
                        GearSlot.WEAPON,
                        GearFamily.BLADE,
                        Rarity.C,
                        ChapterOrigin: 1,
                        Quality: 0.5,
                        EnhanceLevel: 0,
                        EnhanceFailures: 0,
                        Affixes: [],
                        Locked: false),
                ],
                []),
            Loadout = new LoadoutSnapshot(new Dictionary<GearSlot, GearInstanceId> { [GearSlot.WEAPON] = blade }),
        };
    }

    private static ContentSnapshot Without(ContentSnapshot content, string documentPath) =>
        new(
            content.Version,
            content.DocumentPaths
                .Where(path => !string.Equals(path, documentPath, StringComparison.Ordinal))
                .Select(content.GetDocument)
                .ToArray());
}
