namespace SlayIdleRepeat.Application.Moderation;

/// <summary>Which trajectory a flag is about.</summary>
public enum PlausibilityMeasure
{
    /// <summary>Wallet currency gained per day.</summary>
    CURRENCY_PER_DAY = 1,

    /// <summary>Legend XP gained per day.</summary>
    LEGEND_XP_PER_DAY = 2,

    /// <summary>Battles whose client-reported hash disagreed with the server's recomputation, per day.</summary>
    BATTLE_HASH_MISMATCHES_PER_DAY = 3,
}

/// <summary>The statistical envelope the sweep judges an account's trajectory against.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every threshold is <c>null</c>, and null means "nobody has authored this number yet" — not
/// "no limit" and not "use a default".</b> No design document states a plausible currency, XP or
/// mismatch rate for this game, and a number invented here would look exactly as authoritative as a
/// measured one while flagging real players. So the envelope ships empty and
/// <see cref="Breaches"/> returns nothing at all, by construction.
/// </para>
/// <para>
/// This is the whole reason the sweep is described as a skeleton. It observes, it stores, it
/// computes the deltas, and it flags <b>nothing</b> until an operator authors a threshold from real
/// production data. <see cref="AuthoredThresholds"/> is what the composition root reads to say so
/// out loud at startup, so a deployment can never quietly believe the sweep is watching.
/// </para>
/// <para>
/// Thresholds are integers per day and compared without division — see <see cref="Breaches"/> — so
/// a short observation window cannot round a rate into or out of a flag.
/// </para>
/// </remarks>
/// <param name="MaxCurrencyPerDay">Wallet currency per day above which an account is flagged. <c>null</c> = unauthored.</param>
/// <param name="MaxLegendXpPerDay">Legend XP per day above which an account is flagged. <c>null</c> = unauthored.</param>
/// <param name="MaxBattleHashMismatchesPerDay">Battle-hash mismatches per day above which an account is flagged. <c>null</c> = unauthored.</param>
public sealed record PlausibilityEnvelope(
    long? MaxCurrencyPerDay = null,
    long? MaxLegendXpPerDay = null,
    long? MaxBattleHashMismatchesPerDay = null)
{
    /// <summary>🔒 The shipped envelope: every threshold unauthored, so the sweep flags nothing.</summary>
    public static readonly PlausibilityEnvelope Unauthored = new();

    /// <summary>How many of the three thresholds carry a number. Zero on the shipped envelope.</summary>
    public int AuthoredThresholds => throw new NotImplementedException();

    /// <summary>Every threshold this delta exceeds. Empty when the delta is fine — and always empty while no threshold is authored.</summary>
    /// <param name="delta">The account's movement since its previous observation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="delta"/> is null.</exception>
    /// <remarks>
    /// The comparison is <c>gained × oneDay &gt; threshold × elapsed</c> rather than a per-day rate,
    /// so it is exact integer arithmetic at every window length: a 90-second window and a 30-day
    /// window are judged by the same inequality with no rounding between them. A delta whose window
    /// is not positive is judged by nothing — two observations at one instant carry no rate.
    /// <para>
    /// 🔒 The product is taken in <see cref="Int128"/>. A day is 8.64 × 10¹¹ ticks, so a <c>long</c>
    /// product overflows above roughly ten million gained — and a wrapped product goes NEGATIVE,
    /// which reads as "well inside the envelope". The one account the sweep exists to notice is
    /// exactly the one whose numbers are large enough to wrap.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PlausibilityFlag> Breaches(PlausibilityDelta delta) =>
        throw new NotImplementedException();
}

/// <summary>One breached threshold, in enough detail for a reviewer to re-derive it.</summary>
/// <param name="Measure">Which trajectory.</param>
/// <param name="Gained">How much the account gained over the window.</param>
/// <param name="Window">How long the window was.</param>
/// <param name="ThresholdPerDay">The authored per-day threshold that was exceeded.</param>
public sealed record PlausibilityFlag(
    PlausibilityMeasure Measure,
    long Gained,
    TimeSpan Window,
    long ThresholdPerDay)
{
    /// <summary>The flag as the review queue's reason text — what, how much, over how long, against what.</summary>
    /// <remarks>
    /// It names <see cref="Measure"/> verbatim, because the queue entry the reviewer opens carries
    /// the text and not this record: a reason that said only "over the envelope" would leave them
    /// unable to tell which trajectory tripped.
    /// </remarks>
    public string Reason => throw new NotImplementedException();
}
