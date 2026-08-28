using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The host-side check behind <see cref="RejectionReason.CONTENT_VERSION_MISMATCH"/>.</summary>
/// <remarks>
/// <para>
/// Asked before dispatch, so a client playing against content the server no longer serves is turned
/// back before any rule reads a tunable — the value is transport-tier and never reaches the domain.
/// </para>
/// <para>
/// ⚠️ <b>Only <see cref="BeginSessionCommand"/> carries a content hash.</b> Nothing else on the wire
/// does, so every other command answers <c>null</c> here — not because it is exempt, but because it
/// has no hash to disagree with. The session begin is where the client states which content set it
/// loaded, and it is the one place the disagreement can be surfaced early enough for the client to
/// go and fetch the right one.
/// </para>
/// <para>
/// A malformed stamp is a mismatch, not a malformed command: the client that sent a truncated,
/// upper-cased or empty hash is in exactly the position of one that sent an outdated one — it needs
/// to re-fetch content — and answering <c>MALFORMED_COMMAND</c> would send it to look for a bug in
/// its envelope instead.
/// </para>
/// </remarks>
public static class ContentVersionCheck
{
    /// <summary>Whether this command states which content set the client loaded.</summary>
    /// <param name="command">The typed command.</param>
    /// <returns><c>true</c> only for the session begin.</returns>
    /// <remarks>
    /// Asked before the expected version is looked up, so a command that makes no claim costs no
    /// store read. It lives beside <see cref="Check"/> rather than in the caller so that "which
    /// commands carry a hash" is answered in exactly one place.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static bool MakesAContentClaim(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command is BeginSessionCommand;
    }

    /// <summary>Whether this command disagrees with the content the server is serving.</summary>
    /// <param name="command">The typed command.</param>
    /// <param name="expected">The version the server expects this caller to be on.</param>
    /// <returns>
    /// <see cref="RejectionReason.CONTENT_VERSION_MISMATCH"/> when the command is a session begin
    /// whose hash is not exactly <paramref name="expected"/>; <c>null</c> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static RejectionReason? Check(GameCommand command, ContentVersion expected)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(expected);

        // Ordinal against the stamp's own text: the wire form is the bare 64 lowercase hex
        // characters, so an upper-cased or prefixed spelling is a different claim, not the same
        // one written differently.
        return command is BeginSessionCommand session &&
               !string.Equals(session.ContentHash, expected.Value, StringComparison.Ordinal)
            ? RejectionReason.CONTENT_VERSION_MISMATCH
            : null;
    }
}
