using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// Stocking a shop: spend one offer's worth of the run's <c>shop</c> stream, and answer the position
/// that offer begins at.
/// </summary>
/// <remarks>
/// 🔒 <b>The draws are SPENT, not skipped.</b> A stocking could have written a position onto the run
/// and left the stream where it was — and the next refresh would then draw the same block again,
/// handing back the offer the player had just rejected. So the stocking draws the offer for real,
/// through the run's own scope, and <c>GameRules</c> folds the counter back like any other draw. It
/// is also why nothing here writes a stream position by hand: a hand-written counter is a
/// determinism defect <c>GameRules.FoldRngPositions</c> refuses outright.
/// </remarks>
internal static class ShopStocking
{
    /// <summary>Draws a fresh offer and answers the stream position it begins at.</summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>The position to record on the run, from which the offer is re-derivable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    internal static ulong Draw(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var stream = input.Rng.Stream(RngStreams.Shop);
        var position = stream.Position;

        // Drawn rather than counted past: this is the one call that decides what the shop sells, and
        // running it here is what guarantees the position recorded below re-derives to exactly it.
        _ = RunShopOffer.Draw(input.Run, input.Context.Content, stream);

        return position;
    }
}
