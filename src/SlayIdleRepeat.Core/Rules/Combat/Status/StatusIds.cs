namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 The `05` §5 status ids that <b>code</b> is allowed to name — spelled once, here.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why this type exists.</b> M2 ended with the same two words declared as constants in two
/// places: <c>StatusTimeline</c> held a <c>private const string StunId = "STUN"</c> whose own doc
/// claimed to be <em>"named once rather than spelled at four call sites"</em>, and
/// <c>BossBuiltIns</c> held <c>StunStatusId</c> and <c>FreezeStatusId</c> beside it — same assembly,
/// same namespace root, no visibility barrier between them. Two "stated once" constants for one
/// string is the drift shape steering S2 is about: the next reader fixes one of them.
/// </para>
/// <para>
/// 🔒 <b>Only the ids a document rules on by name, and no others.</b> This is not a transcription of
/// `05` §5's table — <c>content/statuses.json</c> is that, and <c>StatusCatalogue</c> reads it.
/// `18`'s headnote forbids per-content code, and <c>StatusCatalogueRuleTests</c> enforces it: a type
/// naming a status id has to point at the sentence that fixes the name. Two sentences do.
/// </para>
/// <list type="bullet">
///   <item>
///   <c>STUN</c> — `05` §5 gives it a rule no other status has: <em>"Max 1.5 s per application, with
///   a 3 s immunity window after"</em>, followed by <em>"stun immunity is mandatory"</em>. That is
///   <c>StunWindow</c>'s and <c>StatusTimeline</c>'s.
///   </item>
///   <item>
///   <c>FREEZE</c> — `17` §1: <em>"Bosses are immune to <c>STUN</c> and <c>FREEZE</c> in phase
///   3"</em>, which `17` §11 makes universal (<em>"implemented once, applied to all bosses"</em>).
///   That is <c>BossBuiltIns</c>'.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b><c>StatusLogId.Ordinals</c> does NOT read from here, deliberately.</b> That table also
/// spells these words, and it is a genuinely different rule: `05` §7's <c>dataId</c> map is a
/// <b>wire format</b> pinned inside <c>LogHash</c>, so its entries and their order are a compatibility
/// surface rather than a naming. Pointing it at this type would invite an edit here to look
/// harmless when it is a log-hash break.
/// </para>
/// </remarks>
internal static class StatusIds
{
    /// <summary>🔒 `05` §5's <c>STUN</c> — the one status the section gives a rule of its own.</summary>
    internal const string Stun = "STUN";

    /// <summary>🔒 `05` §5's <c>FREEZE</c> — named by `17` §1's phase-3 immunity pair.</summary>
    internal const string Freeze = "FREEZE";
}
