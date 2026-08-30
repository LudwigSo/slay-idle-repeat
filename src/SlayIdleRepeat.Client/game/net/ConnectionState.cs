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
/// What is left is three: the last attempt worked, or it did not and nothing is drawn yet, or it did
/// not and the pill is up. The middle one exists because the pill has a threshold — a blink of
/// failure that recovers inside it must never flash anything at a player — and because a player's
/// actions are already unavailable during it, which is a different fact from the pill being visible.
/// </para>
/// </remarks>
public enum ConnectionState
{
    /// <summary>The last attempt succeeded. The only state in which a server-backed action is available.</summary>
    Connected = 1,

    /// <summary>
    /// An attempt has failed and less than the pill threshold has elapsed since. Server-backed
    /// actions are already unavailable; nothing at all is drawn yet.
    /// </summary>
    Waiting = 2,

    /// <summary>Failing for at least the pill threshold. The pill is drawn.</summary>
    Reconnecting = 3,
}
