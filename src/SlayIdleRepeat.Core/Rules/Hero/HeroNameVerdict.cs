using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>What the name rule decided about one candidate.</summary>
/// <param name="Name">The accepted name, or <see langword="null"/> when it was refused.</param>
/// <param name="Refusal">Which check fired, or <see langword="null"/> when it was accepted.</param>
/// <param name="Match">
/// The authored term the candidate matched, or <see langword="null"/> for every refusal that is not a
/// word-list match. Carried so a diagnosis says which word fired rather than only that some word did.
/// </param>
/// <remarks>
/// A value, not a boolean: refusing a name for its length, for a control character, for the English
/// list and for the German list are four different facts, and a caller that could only see "refused"
/// could not tell three of them from the fourth — nor could a test.
/// </remarks>
internal readonly record struct HeroNameVerdict(HeroName? Name, HeroNameRefusal? Refusal, string? Match)
{
    /// <summary>Whether the candidate may be used.</summary>
    internal bool Accepted => Name is not null;
}
