using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <c>tuning/drops.json#/acquisitionRates</c>: the four rates the in-run grant path reads.
/// </summary>
public sealed class GearAcquisitionTuningTests
{
    private static ContentSnapshot Shipped => ShippedHarness.Content;

    /// <summary>Each rate the reader answers is the leaf the document holds at that pointer.</summary>
    /// <remarks>
    /// Compared against the document rather than a transcribed number, so a retune cannot leave the
    /// engine on the old value with nothing going red.
    /// </remarks>
    [Fact]
    public void Every_authored_acquisition_rate_is_the_documents_own_leaf()
    {
        var rates = GearAcquisitionTuning.Read(Shipped);

        rates.EliteKillItems.ShouldBe(
            Shipped.ReadInt32(GearAcquisitionTuning.EliteKillItemsReference),
            "how many items an Elite kill drops is authored, not chosen by the engine.");

        rates.BossKillItemsMin.ShouldBe(
            Shipped.ReadInt32(GearAcquisitionTuning.BossKillItemsMinReference),
            "the floor of a Boss kill's drop range is authored.");

        rates.BossKillItemsMax.ShouldBe(
            Shipped.ReadInt32(GearAcquisitionTuning.BossKillItemsMaxReference),
            "and so is its ceiling — a reader that answered the floor for both would silently make " +
            "every Boss pay the minimum.");

        rates.NormalEnemyChance.ShouldBe(
            Shipped.ReadDouble(GearAcquisitionTuning.NormalEnemyChanceReference),
            "the per-kill chance an ordinary enemy drops anything is the one number in this block a " +
            "player feels directly, and it belongs to the balance pass rather than to the engine.");
    }

    // ------------------------------------------------------------------------ the reader's refusals

    /// <summary>An authored item count below one is refused, and the refusal names which count.</summary>
    [Theory]
    [InlineData(GearAcquisitionTuning.EliteKillItemsReference, 0)]
    [InlineData(GearAcquisitionTuning.BossKillItemsMinReference, 0)]
    [InlineData(GearAcquisitionTuning.BossKillItemsMaxReference, 0)]
    [InlineData(GearAcquisitionTuning.EliteKillItemsReference, -1)]
    public void A_kill_kind_that_drops_fewer_than_one_item_is_refused(string reference, int authored)
    {
        var thrown = Should.Throw<InvalidTunableException>(
            () => GearAcquisitionTuning.Read(Rates((reference, ContentValue.Number(authored)))));

        thrown.Reference.ShouldBe(
            reference,
            "the refusal names the leaf that was authored, not the block it sits in — three counts " +
            "share this guard and only one of them moved.");
        thrown.Message.ShouldContain("at least one item", Case.Sensitive);
    }

    /// <summary>A Boss range whose ceiling sits below its floor is refused, and the ceiling is blamed.</summary>
    [Fact]
    public void A_boss_range_whose_ceiling_is_below_its_floor_is_refused()
    {
        var pastTheCeiling = Shipped.ReadInt32(GearAcquisitionTuning.BossKillItemsMaxReference) + 1;

        var thrown = Should.Throw<InvalidTunableException>(() => GearAcquisitionTuning.Read(
            Rates((GearAcquisitionTuning.BossKillItemsMinReference,
                   ContentValue.Number(pastTheCeiling)))));

        thrown.Reference.ShouldBe(GearAcquisitionTuning.BossKillItemsMaxReference);
        thrown.Message.ShouldContain("an empty range", Case.Sensitive);
    }

    /// <summary>A Boss ceiling at the largest representable count is refused at the read.</summary>
    /// <remarks>
    /// The count is drawn over a half-open range, so the draw asks for one past the ceiling — which
    /// wraps. Refused here, where the document is still nameable, rather than at the draw, where the
    /// wrapped range would blame the roll for what an author wrote.
    /// </remarks>
    [Fact]
    public void A_boss_ceiling_with_no_number_past_it_is_refused()
    {
        var thrown = Should.Throw<InvalidTunableException>(() => GearAcquisitionTuning.Read(
            Rates((GearAcquisitionTuning.BossKillItemsMaxReference,
                   ContentValue.Number(int.MaxValue)))));

        thrown.Reference.ShouldBe(GearAcquisitionTuning.BossKillItemsMaxReference);
        thrown.Message.ShouldContain(
            "no number one past",
            Case.Sensitive,
            "this arm and the inverted-range arm both blame the ceiling, so the message is what " +
            "tells a reader which of the two fired.");
    }

    /// <summary>A per-kill chance outside zero-to-one is refused.</summary>
    /// <remarks>
    /// Outside that interval the leaf stops being a probability: the comparison it feeds either never
    /// holds or always does, so an ordinary kill silently becomes a guaranteed drop or a dead one.
    /// </remarks>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void A_per_kill_chance_outside_zero_to_one_is_refused(double authored)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => GearAcquisitionTuning.Read(
            Rates((GearAcquisitionTuning.NormalEnemyChanceReference,
                   ContentValue.Number((decimal)authored)))));

        thrown.Reference.ShouldBe(GearAcquisitionTuning.NormalEnemyChanceReference);
        thrown.Message.ShouldContain("outside 0..1", Case.Sensitive);
    }

    /// <summary>Both ends of the interval are accepted — the guard is outside-it, not near-it.</summary>
    /// <remarks>
    /// The boundary control on the case above. Without it the guard could be authored as
    /// <c>&lt;= 0 or &gt;= 1</c> and every arm would still pass, while a design that wanted a kind of
    /// kill to drop on every kill would be refused for authoring exactly that.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void A_per_kill_chance_at_either_end_of_the_interval_is_accepted(double authored)
    {
        GearAcquisitionTuning.Read(
                Rates((GearAcquisitionTuning.NormalEnemyChanceReference,
                       ContentValue.Number((decimal)authored))))
            .NormalEnemyChance.ShouldBe(authored);
    }

    /// <summary>
    /// The shipped acquisition-rate block with the named leaves replaced.
    /// </summary>
    /// <remarks>
    /// Every leaf not being probed is taken from the document that ships, so a case authors exactly
    /// the one value it is about and the reader reaches the guard under test rather than an earlier
    /// one. The leaf name is derived from the pointer the reader itself declares, so a re-pointed
    /// block moves this fixture with it instead of leaving it building a document nobody reads.
    /// </remarks>
    private static ContentSnapshot Rates(params (string Reference, ContentValue Authored)[] authored)
    {
        var block = new[]
        {
            GearAcquisitionTuning.EliteKillItemsReference,
            GearAcquisitionTuning.BossKillItemsMinReference,
            GearAcquisitionTuning.BossKillItemsMaxReference,
            GearAcquisitionTuning.NormalEnemyChanceReference,
        };

        var members = block
            .Select(reference => (
                Name: Leaf(reference),
                Value: authored.FirstOrDefault(a => a.Reference == reference) is { Reference: not null } probe
                    ? probe.Authored
                    : Shipped.Read(reference)))
            .Append((Name: Leaf(GearAcquisitionTuning.TreasureTileChanceReference),
                     Value: ContentValue.Unauthorised))
            .ToArray();

        return new ContentSnapshot(
            ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
            [
                new ContentDocument(
                    GearAcquisitionTuning.DocumentPath,
                    InRunIncomeDocuments.Obj((Leaf(GearAcquisitionTuning.BlockReference),
                                              InRunIncomeDocuments.Obj(members)))),
            ]);
    }

    /// <summary>The last segment of a content pointer — the member name the document holds it under.</summary>
    private static string Leaf(string reference) => reference[(reference.LastIndexOf('/') + 1)..];
}
