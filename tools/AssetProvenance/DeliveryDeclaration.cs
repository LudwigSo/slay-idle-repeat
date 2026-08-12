namespace SlayIdleRepeat.AssetProvenance;

/// <summary>
/// 🔒 The declared state of the delivery set, and the mechanism that makes the declaration expire
/// by itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> M8-09's register holds 974 art + 106 audio slots and <b>zero assets are
/// delivered</b>: the six generation tasks are ⛔ capability-blocked (M8 kickoff, 2026-08-12 — no
/// agent can run a Midjourney or a Suno session, and there is no audio-tool licence at all). So the
/// gate's forward direction, "no delivered asset without a provenance record", quantifies over an
/// empty set today and will pass no matter what it is written to check. Steering <b>S3</b>: a rule
/// whose subject set can silently become empty passes forever.
/// </para>
/// <para>
/// <b>What is done about it, in three parts.</b>
/// </para>
/// <list type="number">
///   <item>The register itself is floored (<see cref="ProvenanceGate.MinimumRegisteredAssets"/> and
///   the canary ids beside it), so the gate is quantifying over 1,080 real rows even while the
///   delivery set is empty. A gate that could not find the register is red, not green.</item>
///   <item>The reverse direction — no record for an unknown or cut asset — is live over the store
///   from the first record ever written, and is proven to bite by cases that drive it with one.</item>
///   <item>This declaration. While <see cref="AwaitingFirstDelivery"/> is true the gate reports
///   <see cref="DeliveryCoverage.AwaitingFirstDelivery"/> rather than "passed", and the moment a
///   single asset appears under <c>assets/</c> the build fails with
///   <see cref="ViolationCode.StaleDeliveryDeclaration"/> until somebody flips it. The empty state
///   is therefore <em>declared</em>, not merely observed, and it cannot be mistaken for a pass over
///   a populated one.</item>
/// </list>
/// <para>
/// 🔒 <b>It fails in both directions</b> (steering S4: an exemption must fail when it stops being
/// true, including when it has been <em>satisfied</em>). Flipping
/// <see cref="AwaitingFirstDelivery"/> to <c>false</c> while nothing is delivered is equally a
/// failure — otherwise the honest way to silence the first direction would be to pre-arm the flag
/// and let it sit there.
/// </para>
/// <para>
/// It is a compiled constant rather than a JSON flag on purpose: the same shape as
/// <c>SnapshotFieldOrderPin</c> and <c>GapRegister</c>, and the same reason — the commit that
/// changes the state of the world is the commit that has to change the declaration, and a reviewer
/// sees it in the diff.
/// </para>
/// </remarks>
public static class DeliveryDeclaration
{
    /// <summary>
    /// 🔒 True while <b>no</b> asset has been delivered. Flip it in the same commit as the first
    /// delivered asset, and not before.
    /// </summary>
    public const bool AwaitingFirstDelivery = true;

    /// <summary>The task that is expected to deliver the first asset and retire this flag.</summary>
    /// <remarks>
    /// M8-02 (the Style Anchor Sheet) gates every other art batch — `15` Part H step 1 — so it is
    /// the first task that can put a file under <c>assets/</c>. It is ⛔ and needs a human
    /// Midjourney session.
    /// </remarks>
    public const string TurnsOn = "M8-02";

    /// <summary>Why the delivery set is empty. Something a later reader can falsify.</summary>
    public const string Reason =
        "Every asset-generating task in M8 is capability-blocked: M8-02/03/04/05/08 need a human " +
        "Midjourney session, and M8-07 has no audio-tool licence at all (M8 kickoff, 2026-08-12). " +
        "M8-10's placeholder set is a build artifact and is never committed, so it does not deliver " +
        "either. Nothing has been generated, so nothing needs a provenance record yet — and this " +
        "flag is what stops that state reading as a clean bill of health.";
}
