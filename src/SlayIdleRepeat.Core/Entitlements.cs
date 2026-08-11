namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 The player's subscription entitlement, as a read-only value the composition root resolved:
/// <c>{ HasPlus, ExpiresAtUtc }</c> (`30` §3).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 `30` §3: <em>"The domain may read <c>HasPlus</c> only to resolve ad-reward auto-grant caps —
/// never to alter a stat, a rate or a drop."</em> `12` §3.2 says the same thing from the other
/// side: there is no <c>if (isSubscriber)</c> anywhere in the game, because the Plus promise is an
/// adapter swap chosen once in a composition root. Two architecture rules hold the line —
/// <c>Entitlements</c> is unreachable from every type under <c>Core/Rules/</c> and from the power
/// computation of `29` §3, with exactly one licensed exception (the ad-grant cap rule); and no
/// entitlement branch may appear outside <c>Server/Composition/</c> or <c>Client/Composition/</c>.
/// </para>
/// <para>
/// ⚠️ <b>This type is deliberately not a <c>record</c>, and it declares no equality.</b> A record's
/// synthesized <c>Equals(Entitlements?)</c> loads <c>&lt;HasPlus&gt;k__BackingField</c> and then
/// branches, which is precisely the shape the IL half of
/// <c>No_entitlement_branch_outside_a_composition_root</c> catches — "reads the flag and then
/// branches". Making this a record turns the architecture suite red, and the only ways to make it
/// green again would be to widen that rule's single licensed exemption (every extra exemption is
/// another place Plus can alter a drop) or to stop the domain reading the entitlement at all.
/// Nothing in the domain compares two <c>Entitlements</c>; if something ever needs to, the
/// comparison belongs at the composition root, where the branch is allowed.
/// </para>
/// <para>
/// <see cref="ExpiresAtUtc"/> rides on the value rather than being recomputed because `12` §2.2
/// makes Plus expiry a time-dependent rule, and a rule that recomputed it would need a clock. The
/// composition root resolves <see cref="HasPlus"/> against its own <c>NowUtc</c> before building
/// the context; the domain stores what it was told and re-derives nothing.
/// </para>
/// </remarks>
public sealed class Entitlements
{
    /// <summary>Creates the entitlement value the composition root resolved for this command.</summary>
    /// <param name="hasPlus">Whether Plus is active, as the server decided.</param>
    /// <param name="expiresAtUtc">When Plus lapses, or <c>null</c> when there is nothing to lapse.</param>
    public Entitlements(bool hasPlus, DateTimeOffset? expiresAtUtc)
    {
        HasPlus = hasPlus;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    /// 🔒 Whether Plus is active. Readable by exactly one rule — the ad-reward auto-grant cap
    /// (`30` §3, `12` §4.3) — and by nothing else in the domain, ever.
    /// </summary>
    public bool HasPlus { get; }

    /// <summary>
    /// When the current Plus term lapses, or <c>null</c> when none is known. Never coerced to a
    /// default: an absent expiry is absent, and a reader that needs one must say what it wants.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; }
}
