using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <c>tuning/drops.json#/acquisitionRates</c>: the four rates the in-run grant path reads, and the
/// fifth that is deliberately not authored.
/// </summary>
/// <remarks>
/// Read off the real <c>game-data/</c> tree rather than a hermetic mirror, because the claim below is
/// specifically about the document that ships — a mirror would keep answering the old value after the
/// authored one moved, which is the failure this pair exists to catch.
/// </remarks>
public sealed class GearAcquisitionTuningTests
{
    private static ContentSnapshot Shipped => ShippedHarness.Content;

    /// <summary>Each rate the reader answers is the leaf the document holds at that pointer.</summary>
    /// <remarks>
    /// Compared against the document rather than against a transcribed number: a case that spelled
    /// the rate itself would keep passing after the design set moved it, and the engine would stay on
    /// the old one with nothing going red.
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

    /// <summary>The treasure-tile chance is left unauthored, and stays that way.</summary>
    /// <remarks>
    /// 🔒 Two documents disagree about it — one lists a treasure-tile gear chance, the later
    /// single-source ruling says treasure tiles never drop gear in v1 — so the hole is left where a
    /// grep can find it rather than filled with either reading. This case is what forces the decision
    /// to be taken by a person: the day a number appears at that pointer it goes red, and a treasure
    /// tile that owes gear has no path to grant it.
    /// </remarks>
    [Fact]
    public void The_treasure_tile_chance_is_left_unauthored()
    {
        Shipped.IsAuthorised(GearAcquisitionTuning.BlockReference).ShouldBeTrue(
            "the acquisition-rate block itself is gone, so the assertion below would be passing " +
            "because the whole block moved rather than because the hole is still a hole.");

        Shipped.Read(GearAcquisitionTuning.TreasureTileChanceReference).Kind.ShouldBe(
            ContentValueKind.Unauthorised,
            "a value has been authored for treasureTileChance. A JSON null loads as Unauthorised " +
            "precisely so no reader can mistake it for zero, and nothing in the drop path can grant " +
            "gear from a treasure tile — RunDropTrigger names ordinary, Elite and Boss kills and " +
            "nothing else. Decide the rule before authoring the number.");
    }
}
