namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// Which of the two producers of `14` §16.2 a <see cref="RejectionReason"/> belongs to.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is <b>not</b> a wire type. The tier is a property of the catalogue, not of any
/// envelope: `14` §16.2 puts it in a table column so a reader can tell which layer decides a
/// value, and `30` §2 turns that column into a rule — <c>Apply</c> returns
/// <see cref="Domain"/> values and no others. It exists in code because M1-06 has to be able to
/// refuse a handler that returns a <see cref="Transport"/> value, and a comment cannot be
/// refused.
/// </para>
/// <para>
/// Numbered from 1 for the same reason <see cref="RejectionReason"/> is: an uninitialised field
/// must not read as a real tier.
/// </para>
/// </remarks>
public enum RejectionReasonTier
{
    /// <summary>
    /// Decided by the server host / <c>Application</c> layer before the domain is invoked. A
    /// transport-tier value never appears in a <c>CommandResult</c> (`30` §8).
    /// </summary>
    Transport = 1,

    /// <summary>
    /// Returned by <c>GameRules.Apply</c> as <c>CommandResult.Rejection</c> (`30` §2) — a
    /// legal-move failure, decided by the rules against real state.
    /// </summary>
    Domain = 2,
}
