using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Perks;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Perks;

/// <summary>
/// A row's pool tag decides whether the draft may offer it. The rule is "a row this engine cannot
/// honour is not in the standard pool" — a catalogue may legitimately carry an effect whose value
/// reads state no fight composition supplies, and offering such a row ends the run at the next
/// battle rather than doing nothing.
/// </summary>
/// <remarks>
/// 🔴 Both halves are stated, because a tag filter is the shape that goes vacuous quietly: if the
/// token were ever renamed the pool would empty and the failure would surface as some unrelated
/// draft refusal. So the withheld row is asserted absent AND the standard rows are asserted present.
/// </remarks>
public sealed class DraftPoolTagTests
{
    private static readonly ulong[] Seeds =
        Enumerable.Range(1, 40).Select(offset => (ulong)offset).ToArray();

    private static PerkCatalogue Catalogue =>
        PerkCatalogue.Read(PerkDocuments.OneRowWithheldFromTheStandardPool);

    [Fact]
    public void A_row_outside_the_standard_pool_is_never_offered()
    {
        var offered = Offered();

        offered.ShouldNotContain(
            PerkDocuments.WithheldRow,
            "the row carries a pool tag this draft does not draw from, so no seed may reach it.");
    }

    /// <summary>
    /// The floor that keeps the case above from passing over an empty pool: every OTHER row of the
    /// same catalogue is still reachable, so the filter removed exactly the one row and not the draw.
    /// </summary>
    [Fact]
    public void Every_standard_row_of_the_same_catalogue_is_still_offered()
    {
        var offered = Offered();

        offered.ShouldNotBeEmpty("an empty pool would satisfy the absence above and nothing else.");
        offered.ShouldBe(
            PerkDocuments.AllIds.ToHashSet(StringComparer.Ordinal),
            ignoreOrder: true,
            "the catalogue holds one row more than it offers. Every standard row reachable and the " +
            "withheld one not is what makes this a filter on the tag rather than on the pool's size.");
    }

    /// <summary>
    /// …and the shipped-shape fixture, whose every row is standard, offers all of them — so the
    /// filter is a no-op for a catalogue that authors no withheld row at all.
    /// </summary>
    [Fact]
    public void A_catalogue_whose_every_row_is_standard_offers_all_of_them()
    {
        var catalogue = PerkCatalogue.Read(PerkDocuments.Shipped);

        Offered(catalogue).ShouldBe(
            PerkDocuments.AllIds.ToHashSet(StringComparer.Ordinal),
            ignoreOrder: true,
            "no row of this fixture is withheld, so introducing the tag filter changed nothing here.");
    }

    private static IReadOnlySet<string> Offered() => Offered(Catalogue);

    /// <summary>Every perk id any seed of the sweep puts on offer to a run owning nothing.</summary>
    private static IReadOnlySet<string> Offered(PerkCatalogue catalogue) =>
        Seeds.SelectMany(seed => PerkDraftEngine.GenerateOptions(
                  new DraftRequest(
                      catalogue,
                      new DraftedPerks(new ReadOnlyDictionary<string, int>(
                          new Dictionary<string, int>(StringComparer.Ordinal))),
                      PerkDraftEngineTests.Tuning,
                      DraftRarityWeights.For(stage: 1, TileKind.Enemy),
                      PerkDraftEngineTests.Unforced,
                      PerkDraftEngineTests.NothingEverDrafted),
                  new DeterministicRng(seed, RngStreams.Draft)))
             .Select(option => option.PerkId)
             .ToHashSet(StringComparer.Ordinal);
}
