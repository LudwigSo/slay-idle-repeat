using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary><c>TILE_TREASURE</c>: one weighted profile draw off the <c>treasure</c> stream.</summary>
/// <remarks>No gear, by design — treasure tiles never drop gear. There is nothing missing here.</remarks>
internal static class TreasureResolver
{
    /// <summary>
    /// The attribution token this tile's <c>CurrencyChanged</c> rows are logged under. A stable
    /// identifier, not a design number.
    /// </summary>
    internal const string Reason = "treasure_tile";

    /// <summary>Resolves a treasure tile, paying its drawn profile.</summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// One <see cref="CurrencyChanged"/> per non-zero column of the drawn profile, in
    /// Crowns → Enhance Stones → Merge Dust order. A zero column is skipped rather than moved: some
    /// profiles pay no Enhance Stones or no Merge Dust, and a zero-delta row would be a real,
    /// misleading line in the attribution log for a currency the profile does not actually pay.
    /// </returns>
    internal static IReadOnlyList<DomainEvent> Resolve(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tuning = TreasureTuning.Read(input.Context.Content);
        var scalars = ChapterScalarTuning.Read(input.Context.Content);

        // One draw, one index. The table is built in the document's own order — WeightedPick walks
        // in order, so reordering it would change what an existing seed pays.
        var table = new (TreasureProfile Item, double Weight)[tuning.Profiles.Count];
        for (var i = 0; i < tuning.Profiles.Count; i++)
        {
            table[i] = (tuning.Profiles[i], tuning.Profiles[i].Weight);
        }

        var profile = input.Rng.Stream(RngStreams.Treasure).WeightedPick(table);
        var chapterId = input.Run.ChapterId;

        var events = new List<DomainEvent>(3);

        // ScaleMeta rather than a multiply by the scalar: the scaled amount rounds to a whole
        // currency unit, never the scalar itself — see ChapterScalarTuning.ScaleMeta.
        Pay(events, input, CurrencyId.CROWNS, scalars.ScaleMeta(profile.Crowns, chapterId));
        Pay(events, input, CurrencyId.ENHANCE_STONES, scalars.ScaleMeta(profile.EnhanceStones, chapterId));
        Pay(events, input, CurrencyId.MERGE_DUST, scalars.ScaleMeta(profile.MergeDust, chapterId));

        return events;
    }

    /// <summary>
    /// Moves one META wallet currency through <c>Player</c> — treasure pays none of the run's own
    /// <c>GOLD</c>, which is why there is no <c>Run</c> branch here.
    /// </summary>
    private static void Pay(List<DomainEvent> events, HandlerInput input, CurrencyId currency, long amount)
    {
        if (amount != 0)
        {
            events.Add(input.Player.MoveCurrency(currency, amount, Reason));
        }
    }
}
