namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// The status ids that code is allowed to name — spelled once, here.
/// </summary>
/// <remarks>
/// <para>
/// Only the ids that rules elsewhere name by name, and no others — this is not a transcription of
/// the status table (<c>StatusCatalogue</c> reads that from content). <c>STUN</c> carries a rule no
/// other status has (max duration and a mandatory immunity window, enforced by
/// <c>StunWindow</c>/<c>StatusTimeline</c>); <c>FREEZE</c> is named by the boss phase-3 immunity
/// pair in <c>BossBuiltIns</c>.
/// </para>
/// <para>
/// <c>StatusLogId.Ordinals</c> deliberately does not read from here: that table is a wire format
/// pinned inside <c>LogHash</c>, a genuinely different rule from a naming.
/// </para>
/// </remarks>
internal static class StatusIds
{
    /// <summary><c>STUN</c> — the one status with a rule of its own.</summary>
    internal const string Stun = "STUN";

    /// <summary><c>FREEZE</c> — named by the boss phase-3 immunity pair.</summary>
    internal const string Freeze = "FREEZE";
}
