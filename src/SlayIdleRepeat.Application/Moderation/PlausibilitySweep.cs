using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Application.Moderation;

/// <summary>What one sweep did.</summary>
/// <param name="AccountsObserved">How many accounts were read.</param>
/// <param name="DeltasMeasured">How many of those had a previous observation to measure against.</param>
/// <param name="FlagsRaised">How many review entries were raised. Zero while no threshold is authored.</param>
public sealed record PlausibilitySweepResult(int AccountsObserved, int DeltasMeasured, int FlagsRaised);

/// <summary>
/// The plausibility monitor's one pass: read every account's cumulative measures, measure each
/// against its previous reading, raise a review entry for anything outside the envelope, and store
/// the new reading for next time.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a plain class with one method rather than a background service: the loop and the
/// interval are the host's, and everything worth testing is here, driven directly. The host wrapper
/// does nothing but call this on a timer.
/// </para>
/// <para>
/// 🔒 <b>It never sanctions and it never bans.</b> The only thing it can produce is an OPEN queue
/// entry for a human. Escalation is a human decision, and the envelope this judges against ships
/// with no thresholds at all, so a shipped deployment raises nothing.
/// </para>
/// <para>
/// A cheap backstop for exploits in the game's own rules, and nothing more: because every outcome
/// that matters is computed by the server, there is no such thing as an implausible ghost or an
/// impossible save to catch here. Building a statistical model nobody authored would be building
/// the wrong thing thoroughly.
/// </para>
/// <para>
/// An account's FIRST reading measures nothing, on purpose. A cumulative total is not a trajectory,
/// and treating a lifetime balance as one tick's gain would flag every existing player the moment
/// the sweep was first switched on.
/// </para>
/// </remarks>
public sealed class PlausibilitySweep
{
    /// <summary>What a raised entry's identity is spelled with.</summary>
    private const string EntryIdPrefix = "REV_";

    private readonly IModerationStore _store;
    private readonly IClockPort _clock;
    private readonly IIdGeneratorPort _ids;
    private readonly PlausibilityEnvelope _envelope;

    /// <summary>Composes the sweep.</summary>
    /// <param name="store">Where observations, queue entries and sanctions live.</param>
    /// <param name="clock">The instant each pass observes at.</param>
    /// <param name="ids">The source of queue-entry identities.</param>
    /// <param name="envelope">What counts as outside the envelope. Shipped unauthored.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public PlausibilitySweep(
        IModerationStore store, IClockPort clock, IIdGeneratorPort ids, PlausibilityEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(envelope);

        _store = store;
        _clock = clock;
        _ids = ids;
        _envelope = envelope;
    }

    /// <summary>Runs one pass.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<PlausibilitySweepResult> RunOnceAsync(CancellationToken ct)
    {
        var at = _clock.UtcNow;
        var accounts = await _store.ObserveAccountsAsync(at, ct).ConfigureAwait(false);

        var measured = 0;
        var raised = 0;

        foreach (var current in accounts)
        {
            var previous = await _store
                .ReadPreviousObservationAsync(current.Player, ct)
                .ConfigureAwait(false);

            if (previous is not null)
            {
                measured++;

                foreach (var flag in _envelope.Breaches(PlausibilityDelta.Between(previous, current)))
                {
                    // One entry per breached measure, not one per account: a reviewer closes
                    // findings, and two trajectories are two things to check.
                    await _store
                        .RaiseReviewAsync(
                            ReviewQueueEntry.Raise(
                                EntryIdPrefix + _ids.NewGuid().ToString("N"),
                                ReviewSource.PLAUSIBILITY_SWEEP,
                                current.Player,
                                flag.Reason,
                                at),
                            ct)
                        .ConfigureAwait(false);

                    raised++;
                }
            }

            // Recorded whether or not anything was flagged: the window the NEXT pass measures runs
            // from this reading, and skipping it would silently widen it into a stale average.
            await _store.RecordObservationAsync(current, ct).ConfigureAwait(false);
        }

        return new PlausibilitySweepResult(accounts.Count, measured, raised);
    }

    /// <summary>Refreshes a standing snapshot from the sanctions on record.</summary>
    /// <param name="snapshot">The snapshot the request path reads.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <remarks>
    /// Rides the same background pass as the sweep because both read the same store on the same
    /// cadence, and a second timer for one set lookup would be a second thing to configure wrongly.
    /// </remarks>
    public async Task RefreshStandingAsync(AccountStandingSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sanctions = await _store.ReadSanctionsAsync(ct).ConfigureAwait(false);

        snapshot.Replace(sanctions, _clock.UtcNow);
    }
}
