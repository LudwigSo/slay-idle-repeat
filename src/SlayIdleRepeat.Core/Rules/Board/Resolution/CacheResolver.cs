using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary><c>TILE_CACHE</c>: one draw decides Pet Egg or Beast Feed.</summary>
/// <remarks>
/// <para>
/// An egg hit pays nothing today — a scope boundary, not a lost reward. A Pet Egg is a container
/// type that does not yet exist in <c>Core</c>. Granting Beast Feed on the egg branch instead would
/// silently delete the egg from the design and make the tile's odds a lie, so paying nothing is the
/// honest answer; the draw is still taken so the stream advances identically once the egg branch is
/// filled in.
/// </para>
/// <para>
/// The draw happens on the <c>drops</c> stream — the closest existing semantic fit for a tile
/// whose whole job is a material drop; there is no dedicated cache stream.
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

        // Exactly one draw either way. NextDouble rather than Range: the tunable is a probability
        // in [0,1], so comparing a unit-interval draw against it needs no scaling, and a rate of 0
        // or 1 lands exactly on "never" / "always".
        var isEgg = input.Rng.Stream(RngStreams.Drops).NextDouble() < tuning.EggChance;

        if (isEgg)
        {
            return Array.Empty<DomainEvent>();
        }

        var scalars = ChapterScalarTuning.Read(input.Context.Content);

        // ScaleMeta rather than a multiply by the scalar: the scaled amount rounds to a whole
        // currency unit, never the scalar itself — see ChapterScalarTuning.ScaleMeta.
        var feed = scalars.ScaleMeta(tuning.BeastFeedBase, input.Run.ChapterId);

        return feed == 0
            ? Array.Empty<DomainEvent>()
            : new DomainEvent[] { input.Player.MoveCurrency(CurrencyId.BEAST_FEED, feed, Reason) };
    }
}
