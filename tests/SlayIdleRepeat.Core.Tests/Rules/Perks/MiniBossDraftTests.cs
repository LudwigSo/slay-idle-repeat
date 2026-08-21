using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Perks;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Perks;

/// <summary>
/// What a mini-boss draft actually offers, read through <c>DraftView</c> — the outermost seam that
/// can see it. The weight table itself is pinned in <see cref="DraftRarityWeightsTests"/>; what is
/// asserted here is that the table reaches the offer, which a rarity band keyed on the wrong tile
/// kind would not.
/// </summary>
public sealed class MiniBossDraftTests
{
    /// <summary>The draft's tuning over a catalogue that populates every band in four categories.</summary>
    private static readonly ContentSnapshot Content = OverDeepBands();

    [Theory]
    [InlineData(1UL, 1)]
    [InlineData(2UL, 1)]
    [InlineData(1337UL, 1)]
    [InlineData(0xDEADBEEFUL, 1)]
    [InlineData(1UL, 2)]
    [InlineData(2UL, 2)]
    [InlineData(1337UL, 2)]
    [InlineData(0xDEADBEEFUL, 2)]
    public void A_miniboss_draft_offers_nothing_below_Epic(ulong runSeed, int stage)
    {
        var run = RunSnapshots.With(
            runSeed: runSeed,
            draftPending: true,
            draftBattleKind: (int)TileKind.MiniBoss,
            draftBattleStage: stage);

        var view = DraftView.Project(run, Content).ShouldNotBeNull();

        view.Options.Count.ShouldBe(3);
        view.Options
            .Where(option => option.Rarity < PerkRarity.Epic)
            .Select(option => option.PerkId + " (" + option.Rarity + ")")
            .ShouldBeEmpty("the mini-boss band is Epic 60 / Legendary 40, so no Common or Rare " +
                           "weight exists for a slot to draw.");
    }

    /// <summary>
    /// The control that makes the case above mean something: the same catalogue, drafted after an
    /// ordinary Enemy kill, does put Commons and Rares on offer. Without this, an epic+ claim would
    /// also hold over a catalogue whose lower bands were simply unreachable.
    /// </summary>
    [Fact]
    public void The_same_catalogue_after_an_ordinary_kill_still_offers_below_Epic()
    {
        var offered = new ulong[] { 1UL, 2UL, 1337UL, 0xDEADBEEFUL }
            .Select(seed => RunSnapshots.With(
                runSeed: seed,
                draftPending: true,
                draftBattleKind: (int)TileKind.Enemy,
                draftBattleStage: 1))
            .SelectMany(run => DraftView.Project(run, Content)!.Options)
            .Select(option => option.Rarity)
            .ToArray();

        offered.ShouldContain(
            rarity => rarity < PerkRarity.Epic,
            "the fixture catalogue's Common and Rare rows are reachable by a stage table, so the " +
            "epic+ case is about the weights rather than about an empty band.");
    }

    /// <summary>
    /// The draft's tuning documents with the deep-band perk catalogue laid over them. The pity
    /// registry the draft draws under is already in the baseline, so appending it again would be the
    /// same path twice, which <see cref="ContentSnapshot"/> refuses.
    /// </summary>
    private static ContentSnapshot OverDeepBands()
    {
        var baseline = InRunIncomeDocuments.Shipped;

        return new ContentSnapshot(
            baseline.Version,
            baseline.DocumentPaths
                    .Select(baseline.GetDocument)
                    .Append(PerkDocuments.DeepBandsDocument));
    }
}
