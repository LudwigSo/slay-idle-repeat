using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// 🔒 `03` §7a.3 — <c>TILE_TREASURE</c>: one weighted profile draw off `14` §8.1's <c>treasure</c>
/// stream, paid at <c>M(c)</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>No gear, and that is `03` §7a.3's own ruling</b> — <em>"Treasure tiles never drop gear in
/// v1"</em> — rather than a deferral of M4-03's gear system. There is nothing missing here.
/// </remarks>
internal static class TreasureResolver
{
    /// <summary>
    /// 🔒 The `30` §7 attribution token this tile's <c>CurrencyChanged</c> rows are logged under —
    /// the column `21` §8.3 groups <c>income_attribution.csv</c> by. A stable identifier, not a
    /// design number.
    /// </summary>
    internal const string Reason = "treasure_tile";

    /// <summary>Resolves a treasure tile, paying its drawn profile.</summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// One <see cref="CurrencyChanged"/> per non-zero column of the drawn profile, in
    /// Crowns → Enhance Stones → Merge Dust order. ⚠️ A zero column is <b>skipped</b> rather than
    /// moved: `03` §7a.3 authors <c>DUST_TROVE</c> with no Enhance Stones and the other two with no
    /// Merge Dust, and a zero-delta row would be a real, misleading line in `21` §8.3's attribution
    /// log for a currency the profile does not actually pay.
    /// </returns>
    internal static IReadOnlyList<DomainEvent> Resolve(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tuning = TreasureTuning.Read(input.Context.Content);
        var scalars = ChapterScalarTuning.Read(input.Context.Content);

        // One draw, one index, as every 14 §8.1 stream call is. The table is built in the document's
        // own order — WeightedPick walks in order, so reordering it would change what an existing
        // seed pays.
        var table = new (TreasureProfile Item, double Weight)[tuning.Profiles.Count];
        for (var i = 0; i < tuning.Profiles.Count; i++)
        {
            table[i] = (tuning.Profiles[i], tuning.Profiles[i].Weight);
        }

        var profile = input.Rng.Stream(RngStreams.Treasure).WeightedPick(table);
        var chapterId = input.Run.ChapterId;

        var events = new List<DomainEvent>(3);

        // 🔒 ScaleMeta rather than a multiply by the scalar: 03 §7a rounds the scaled AMOUNT to a
        // whole currency unit, never the scalar itself — see ChapterScalarTuning.ScaleMeta.
        Pay(events, input, CurrencyId.CROWNS, scalars.ScaleMeta(profile.Crowns, chapterId));
        Pay(events, input, CurrencyId.ENHANCE_STONES, scalars.ScaleMeta(profile.EnhanceStones, chapterId));
        Pay(events, input, CurrencyId.MERGE_DUST, scalars.ScaleMeta(profile.MergeDust, chapterId));

        return events;
    }

    /// <summary>
    /// Moves one META wallet currency (`10` §1) through <c>Player</c> — treasure pays none of the
    /// run's own <c>GOLD</c>, which is why there is no <c>Run</c> branch here.
    /// </summary>
    private static void Pay(List<DomainEvent> events, HandlerInput input, CurrencyId currency, long amount)
    {
        if (amount != 0)
        {
            events.Add(input.Player.MoveCurrency(currency, amount, Reason));
        }
    }
}
