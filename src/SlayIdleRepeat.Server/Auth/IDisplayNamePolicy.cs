using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>A decided display name: the accepted text, or which check refused it.</summary>
/// <param name="Name">The accepted name, or <c>null</c> on a refusal.</param>
/// <param name="Refusal">Which check fired, or <c>null</c> when the name stands.</param>
/// <remarks>
/// 🔒 There is no matched-term field, and there must never be one. The refusal vocabulary exists so
/// a rejected name can be explained without quoting the term back — and this shape crosses an API
/// boundary, where echoing it would publish the word list one probe at a time.
/// </remarks>
public sealed record DisplayNameDecision(string? Name, HeroNameRefusal? Refusal)
{
    /// <summary>A name that passed every check.</summary>
    public static DisplayNameDecision Accepted(string name) => new(name, null);

    /// <summary>A name one named check refused.</summary>
    public static DisplayNameDecision Refused(HeroNameRefusal refusal) => new(null, refusal);
}

/// <summary>The hero-name filter as the account-creation endpoint sees it.</summary>
/// <remarks>
/// The rule itself lives in the rules library and is reached through its public entry point; this
/// seam exists so the endpoint handler stays a plain function with no content set in its signature.
/// </remarks>
public interface IDisplayNamePolicy
{
    /// <summary>Decides a candidate name.</summary>
    /// <param name="candidate">
    /// The name as sent. <c>null</c> means the caller supplied none and gets the default; an empty
    /// or blank string is a name the caller DID supply, and is refused like any other bad one.
    /// </param>
    DisplayNameDecision Decide(string? candidate);
}
