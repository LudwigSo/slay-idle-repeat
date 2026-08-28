namespace SlayIdleRepeat.Application.Moderation;

/// <summary>The sanctions ladder, in escalation order, plus the display-name outcome.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Only one rung is wired.</b> <see cref="ACCOUNT_ACTION"/> is enforced on the wire — a player
/// carrying an active one is refused with HTTP 403 before any command is read. Every other value is
/// <b>data</b>: the moderation schema records it, and the milestone that owns the surface it acts on
/// is the one that starts honouring it. Each member says which milestone that is. A value recorded
/// today therefore changes nothing a player can observe, and that is deliberate rather than
/// unfinished: inventing enforcement for a ladder nobody can yet review would be an automatic
/// action, which the anti-cheat design forbids outright.
/// </para>
/// <para>
/// Escalation is a human decision made in the review queue, never a computation. Nothing in this
/// assembly promotes one rung to the next.
/// </para>
/// </remarks>
public enum SanctionKind
{
    /// <summary>
    /// The first rung: the account's entries stop appearing on the ladder while the account itself
    /// plays on, unaware. ⚠️ DATA ONLY — no leaderboard exists yet; the leaderboard milestone owns
    /// the read that must filter on it.
    /// </summary>
    SHADOW_EXCLUDE_LADDER = 1,

    /// <summary>
    /// The second rung: the account's PvP rating returns to its floor. ⚠️ DATA ONLY — no rating
    /// exists yet; the duel milestone owns the reset.
    /// </summary>
    RATING_RESET = 2,

    /// <summary>
    /// The last rung, and the only one with wire enforcement: the account is locked. A request from
    /// a locked account is refused with HTTP 403 and shown the account-state screen.
    /// </summary>
    ACCOUNT_ACTION = 3,

    /// <summary>
    /// Not a rung of the ladder but its own outcome: an offensive display name is forced back to the
    /// default every new account starts with. Display names are not unique, so a reset collides with
    /// nothing. ⚠️ DATA ONLY — the name-report queue and the forced rename are the naming
    /// milestone's. The default it resets to is <c>HeroNameRule.Default</c>'s, which is internal to
    /// the rules library and deliberately NOT re-spelled here: two copies of that word would be two
    /// answers to what a reset account is called.
    /// </summary>
    NAME_RESET = 4,
}
