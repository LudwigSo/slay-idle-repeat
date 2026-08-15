namespace SlayIdleRepeat.Core.Primitives;

/// <summary>Which of the two producers a <see cref="RejectionReason"/> belongs to.</summary>
/// <remarks>
/// Not a wire type — the tier is a property of the catalogue, not of any envelope. It exists in
/// code so a handler that returns a <see cref="Transport"/> value where a <see cref="Domain"/> one
/// is expected can be refused, rather than relying on a comment.
/// </remarks>
public enum RejectionReasonTier
{
    /// <summary>Decided by the server host / <c>Application</c> layer before the domain is invoked. Never appears in a <c>CommandResult</c>.</summary>
    Transport = 1,

    /// <summary>Returned by <c>GameRules.Apply</c> as <c>CommandResult.Rejection</c> — a legal-move failure, decided by the rules against real state.</summary>
    Domain = 2,
}
