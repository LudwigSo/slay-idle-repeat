namespace SlayIdleRepeat.AssetProvenance;

/// <summary>The declared state of the delivery set, and the mechanism that makes the declaration expire by itself.</summary>
/// <remarks>
/// <para>
/// The register holds over a thousand art and audio slots and zero assets are delivered today, so
/// the gate's forward direction ("no delivered asset without a provenance record") quantifies over
/// an empty set and would pass no matter what it checks. A rule whose subject set can silently
/// become empty passes forever, so the empty state is declared here rather than merely observed:
/// while <see cref="AwaitingFirstDelivery"/> is true the gate reports
/// <see cref="DeliveryCoverage.AwaitingFirstDelivery"/> instead of "passed", and the moment a single
/// asset appears under <c>assets/</c> the build fails with
/// <see cref="ViolationCode.StaleDeliveryDeclaration"/> until somebody flips it.
/// </para>
/// <para>
/// It fails in both directions: flipping <see cref="AwaitingFirstDelivery"/> to false while nothing
/// is delivered is equally a failure, so the flag cannot be pre-armed and left inert.
/// </para>
/// </remarks>
public static class DeliveryDeclaration
{
    /// <summary>True while no asset has been delivered. Flip it in the same commit as the first delivered asset, and not before.</summary>
    public const bool AwaitingFirstDelivery = true;

    /// <summary>The task that is expected to deliver the first asset and retire this flag.</summary>
    public const string TurnsOn = "M8-02";

    /// <summary>Why the delivery set is empty. Something a later reader can falsify.</summary>
    public const string Reason =
        "Every asset-generating task in M8 is capability-blocked: M8-02/03/04/05/08 need a human " +
        "Midjourney session, and M8-07 has no audio-tool licence at all (M8 kickoff, 2026-08-12). " +
        "M8-10's placeholder set is a build artifact and is never committed, so it does not deliver " +
        "either. Nothing has been generated, so nothing needs a provenance record yet — and this " +
        "flag is what stops that state reading as a clean bill of health.";
}
