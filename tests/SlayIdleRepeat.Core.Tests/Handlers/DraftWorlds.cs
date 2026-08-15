using System.Collections.Generic;
using System.Linq;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using SlayIdleRepeat.Core.Tests.Model;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <see cref="WorldSlice"/> and <see cref="GameContext"/> fixtures for M3-06's
/// <c>PICK_PERK</c>/<c>REROLL_DRAFT</c>/<c>SKIP_DRAFT</c> suite: a run with a draft pending, over a
/// content set that carries everything <see cref="TileWorlds"/> does plus
/// <see cref="PerkDocuments"/>' small hermetic perk catalogue.
/// </summary>
internal static class DraftWorlds
{
    internal static readonly DateTimeOffset NowUtc = TileWorlds.NowUtc;

    /// <summary>The context every draft handler test runs against.</summary>
    internal static GameContext Context { get; } = TileWorlds.ContextOver(WithPerks());

    private static ContentSnapshot WithPerks()
    {
        var baseline = InRunIncomeDocuments.Shipped;

        var documents = baseline.DocumentPaths
            .Select(baseline.GetDocument)
            .Append(PerkDocuments.Document);

        return new ContentSnapshot(baseline.Version, documents);
    }

    /// <summary>
    /// A slice whose run has a draft pending, opened by a battle of <paramref name="battleKind"/> at
    /// <paramref name="stage"/>, with GOLD and owned perks as given.
    /// </summary>
    internal static WorldSlice DraftPendingOn(
        TileKind battleKind = TileKind.Enemy,
        int stage = 1,
        long gold = 0,
        IReadOnlyDictionary<string, int>? ownedPerkTiers = null)
    {
        var snapshot = RunSnapshots.With(
            draftPending: true,
            draftBattleKind: (int)battleKind,
            draftBattleStage: stage,
            gold: gold,
            ownedPerkTiers: ownedPerkTiers);

        var run = RunAggregate.Rehydrate(snapshot);

        return run.IsSuccess
            ? new WorldSlice(Worlds.NewPlayer(), run.Value)
            : throw new InvalidOperationException("The fixture RunSnapshot does not rehydrate: " + run.Error);
    }
}
