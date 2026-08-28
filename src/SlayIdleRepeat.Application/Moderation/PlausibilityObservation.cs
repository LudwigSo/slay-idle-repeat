using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Moderation;

/// <summary>One account's cumulative anti-cheat measures, read at one instant.</summary>
/// <param name="Player">The account.</param>
/// <param name="ObservedAtUtc">When the measures were read.</param>
/// <param name="WalletTotal">Every player-scoped wallet currency added together.</param>
/// <param name="LegendXp">Lifetime Legend XP.</param>
/// <param name="BattleHashMismatches">The lifetime anti-cheat tally.</param>
/// <remarks>
/// <para>
/// Cumulative, never a rate: a rate needs two observations, and storing the rate instead would
/// throw away the ability to recompute one over a different window. All three measures only ever
/// grow, which is what makes a negative delta a data fault rather than a plausible reading.
/// </para>
/// <para>
/// The wallet is summed rather than tracked per currency deliberately. No document authors a
/// per-currency plausibility limit, and inventing six thresholds where none is authored would be
/// six invented numbers instead of one — the sum is the coarsest honest measure, and the reviewer
/// the flag reaches has the account in front of them.
/// </para>
/// </remarks>
public sealed record PlausibilityObservation(
    PlayerId Player,
    DateTimeOffset ObservedAtUtc,
    long WalletTotal,
    long LegendXp,
    int BattleHashMismatches);

/// <summary>How far an account moved between two observations.</summary>
/// <param name="Player">The account.</param>
/// <param name="Window">How long the two observations are apart. Never negative.</param>
/// <param name="CurrencyGained">Wallet total gained over the window.</param>
/// <param name="LegendXpGained">Legend XP gained over the window.</param>
/// <param name="BattleHashMismatchesGained">Mismatches added over the window.</param>
public sealed record PlausibilityDelta(
    PlayerId Player,
    TimeSpan Window,
    long CurrencyGained,
    long LegendXpGained,
    long BattleHashMismatchesGained)
{
    /// <summary>The movement from <paramref name="previous"/> to <paramref name="current"/>.</summary>
    /// <param name="previous">The earlier observation.</param>
    /// <param name="current">The later observation.</param>
    /// <exception cref="ArgumentNullException">Either observation is null.</exception>
    /// <exception cref="ArgumentException">
    /// The two observations are of different accounts, or <paramref name="current"/> is earlier than
    /// <paramref name="previous"/>. Both are miswiring, not data a rate could be computed from.
    /// </exception>
    public static PlausibilityDelta Between(
        PlausibilityObservation previous, PlausibilityObservation current) =>
        throw new NotImplementedException();
}
