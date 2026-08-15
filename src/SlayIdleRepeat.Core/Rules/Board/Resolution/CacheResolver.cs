using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// 🔒 `03` §7a.4 — <c>TILE_CACHE</c>: one draw decides Pet Egg or Beast Feed.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>An egg hit pays nothing today, and that is a scope boundary rather than a lost reward.</b>
/// §7a.4 says the cache <em>"pays one Pet Egg instead of the Feed"</em>, and a Pet Egg is a
/// container — <c>GapRegister</c>'s <c>ContainerShelf</c> entry, M4-02's, together with
/// <c>OPEN_EGG</c>, which is still a <c>Deferred</c> dispatch row. There is nowhere in <c>Core</c>
/// to put one. Granting Beast Feed on the egg branch instead would silently delete the egg from the
/// design and make the tile's odds a lie; paying nothing is the honest answer, and the draw is still
/// taken so the stream advances identically to the day M4-02 fills the branch in.
/// </para>
/// <para>
/// 🔒 <b>The draw happens on the <c>drops</c> stream, and that is a recorded assumption.</b>
/// `14` §8.1's registry is closed and authors no cache stream; <c>drops</c> — <em>"gear, currency and
/// material drops"</em> — is the closest existing semantic fit for a tile whose whole job is a
/// material drop. Adding a registry row would be a protocol change (`14` §2.3 puts stream names on
/// the wire), which this task is not authorised to make.
/// </para>
/// </remarks>
internal static class CacheResolver
{
    /// <inheritdoc cref="TreasureResolver.Reason"/>
    internal const string Reason = "cache_tile";

    /// <summary>Resolves a cache tile.</summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// The Beast Feed payment, or <b>no events at all</b> on an egg hit — see this type's remarks for
    /// why an empty result is correct there rather than a resolver that forgot to pay.
    /// </returns>
    internal static IReadOnlyList<DomainEvent> Resolve(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tuning = CacheTuning.Read(input.Context.Content);

        // 🔒 Exactly ONE draw either way. NextDouble rather than Range: the tunable is a probability
        // in [0,1], so comparing a unit-interval draw against it needs no scaling and no rounding
        // decision, and a rate of 0 or 1 lands exactly on "never" / "always" (the draw is strictly
        // below 1, so `< 1.0` is always true and `< 0.0` never is).
        var isEgg = input.Rng.Stream(RngStreams.Drops).NextDouble() < tuning.EggChance;

        if (isEgg)
        {
            return Array.Empty<DomainEvent>();
        }

        var scalars = ChapterScalarTuning.Read(input.Context.Content);

        // 🔒 ScaleMeta rather than a multiply by the scalar: 03 §7a rounds the scaled AMOUNT to a
        // whole currency unit, never the scalar itself — see ChapterScalarTuning.ScaleMeta.
        var feed = scalars.ScaleMeta(tuning.BeastFeedBase, input.Run.ChapterId);

        return feed == 0
            ? Array.Empty<DomainEvent>()
            : new DomainEvent[] { input.Player.MoveCurrency(CurrencyId.BEAST_FEED, feed, Reason) };
    }
}
