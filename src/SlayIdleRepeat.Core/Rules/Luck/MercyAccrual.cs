namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// The bad-luck bank: tokens that accrue on outcomes the player did not want and are spent on the
/// one they did, so a long enough dry streak ends in a chosen item rather than in another draw.
/// </summary>
/// <remarks>
/// <para>
/// A counter, not a wallet currency. It is granted by the luck system, spent only inside it, and
/// never moves through the currency ledger — which is why nothing here names a currency, emits a
/// currency event, or is reachable from the shop.
/// </para>
/// <para>
/// Redemption is refused rather than clamped when the bank is short. A partial redemption would
/// spend tokens for nothing, and the player's own screen shows the balance, so "not enough" is a
/// state the caller can and must check first.
/// </para>
/// </remarks>
internal static class MercyAccrual
{
    /// <summary>The bank after an accruing event.</summary>
    /// <param name="held">Tokens held before the event. Never negative.</param>
    /// <param name="grant">Tokens the event grants. Never negative; zero is legal.</param>
    /// <returns>Tokens held after the event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    /// <exception cref="OverflowException">The bank would exceed <see cref="int.MaxValue"/>.</exception>
    internal static int Accrue(int held, int grant) => throw new NotImplementedException();

    /// <summary>Whether the bank covers a redemption.</summary>
    /// <param name="held">Tokens held. Never negative.</param>
    /// <param name="cost">The authored cost of the redemption. At least 1.</param>
    /// <returns><see langword="true"/> when the redemption can be made.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="held"/> is negative, or <paramref name="cost"/> is below 1.
    /// </exception>
    internal static bool CanRedeem(int held, int cost) => throw new NotImplementedException();

    /// <summary>The bank after a redemption.</summary>
    /// <param name="held">Tokens held. Never negative.</param>
    /// <param name="cost">The authored cost of the redemption. At least 1.</param>
    /// <returns>Tokens held after the redemption.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="held"/> is negative, or <paramref name="cost"/> is below 1.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bank does not cover the cost. Ask <see cref="CanRedeem"/> first — a short bank is
    /// refused rather than clamped.
    /// </exception>
    internal static int Redeem(int held, int cost) => throw new NotImplementedException();
}
