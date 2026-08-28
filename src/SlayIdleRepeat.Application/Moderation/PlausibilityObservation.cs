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
/// throw away the ability to recompute one over a different window.
/// </para>
/// <para>
/// ⚠️ <b><see cref="WalletTotal"/> is a balance, not an income.</b> It falls whenever the player
/// spends, so the movement between two readings is NET, and therefore a lower bound on what the
/// account actually earned — an account that farms a fortune and spends it all reads as flat. That
/// is an accepted weakness of a cheap backstop, not an oversight: the signal it does catch is the
/// one that matters, a balance climbing faster than the game can produce. The append-only economy
/// log is where a gross-income measure would come from, and the task that gives this job a durable
/// store is the one that could reach it.
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
/// <param name="CurrencyGained">Wallet total gained over the window. Signed — spending is a fall.</param>
/// <param name="LegendXpGained">Legend XP gained over the window. Signed; a fall is a storage fault.</param>
/// <param name="BattleHashMismatchesGained">Mismatches added over the window. Signed; a fall is a storage fault.</param>
/// <remarks>
/// 🔒 <b>Every gain is signed, and a fall is never turned into a flag.</b> A falling wallet is
/// ordinary — the player spent. A falling XP or mismatch tally is a storage fault, and the honest
/// response to one is to measure it as the negative number it is: the breach test compares against a
/// non-negative threshold, so a negative movement can never trip one. Clamping a fault to zero would
/// hide it, and throwing on one would let a single corrupt row stop the whole sweep.
/// </remarks>
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
        PlausibilityObservation previous, PlausibilityObservation current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (!previous.Player.Equals(current.Player))
        {
            throw new ArgumentException(
                $"'{previous.Player}' and '{current.Player}' are two accounts, and the movement "
                + "between them is not a trajectory — it is a subtraction of one player's history "
                + "from another's.",
                nameof(current));
        }

        if (current.ObservedAtUtc < previous.ObservedAtUtc)
        {
            throw new ArgumentException(
                $"the later reading is stamped {current.ObservedAtUtc:O}, before the earlier one's "
                + $"{previous.ObservedAtUtc:O}. A negative window inverts every rate computed from it.",
                nameof(current));
        }

        return new PlausibilityDelta(
            current.Player,
            current.ObservedAtUtc - previous.ObservedAtUtc,
            current.WalletTotal - previous.WalletTotal,
            current.LegendXp - previous.LegendXp,
            (long)current.BattleHashMismatches - previous.BattleHashMismatches);
    }
}
