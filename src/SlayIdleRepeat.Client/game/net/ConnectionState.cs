namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Whether the client is reaching the server, as three facts rather than as five pictures.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The connection specification names five presentations, and they are not five states.</b>
/// Modelling them one-to-one would have produced an enum whose members overlap and whose transitions
/// cannot be written, so the split is drawn deliberately and stated here rather than left to be
/// re-derived from the presenter:
/// </para>
/// <para>
/// <b>"Offline, read-only" is a DERIVED property, not a member.</b> It holds in every state other
/// than <see cref="Connected"/> — that is its whole definition — so a fourth member would be a
/// second spelling of "not connected", and the two could disagree. A server-backed action is
/// available in <see cref="Connected"/> and in no other state; everything the dimmed buttons, the
/// cloud-slash glyph and the inline toast key off is that one predicate.
/// </para>
/// <para>
/// <b>"Resynced" and "Run resumed" are transient ANNOUNCEMENTS, not states.</b> Both are things that
/// have just happened, shown for a fixed duration and then gone, while the connection carries on
/// being whatever it already was — a resync flash happens <em>in</em> <see cref="Connected"/>, not
/// instead of it. As enum members they would have to be left and re-entered on a timer, which makes
/// the timer part of the connection model and leaves no honest answer to "what is the connection
/// doing?" while one is on screen.
/// </para>
/// <para>
/// What is left is three: the last attempt worked, or it did not and no pill is drawn yet, or it did
/// not and the pill is up. The middle one exists because the pill has a threshold — a blink of
/// failure that recovers inside it must not flash a pill at a player — and because a player's
/// actions are already unavailable during it, which is a different fact from the pill being visible.
/// </para>
/// </remarks>
public enum ConnectionState
{
    /// <summary>The last attempt succeeded. The only state in which a server-backed action is available.</summary>
    Connected = 1,

    /// <summary>
    /// An attempt has failed and less than the pill threshold has elapsed since. Server-backed
    /// actions are already unavailable, and the offline affordance is already drawn on them.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>"Nothing is drawn yet" is true of the PILL and of nothing else, and the difference is a
    /// live question rather than a settled one.</b> The offline presentation keys off
    /// <c>ServerActionsAvailable</c>, which is false here — so for up to the threshold every
    /// server-backed control on screen is already at 40% and already carries the cloud-slash mark,
    /// with no pill to say why, and all of it may vanish again a moment later. That is a bigger
    /// change to a screen than the pill this state exists to withhold. The threshold was written for
    /// the pill; whether the dim belongs behind it too — and whether a press inside the window
    /// should be queued rather than refused, which is what the command queue is for — is not
    /// something the specification answers.
    /// </remarks>
    Waiting = 2,

    /// <summary>Failing for at least the pill threshold. The pill is drawn.</summary>
    Reconnecting = 3,
}
