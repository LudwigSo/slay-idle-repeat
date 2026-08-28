using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Server.Auth;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The account-creation display-name policy, over the rules library's own hero-name gate.
/// </summary>
/// <remarks>
/// One filter for one field: account creation and the in-game profile decide names the same way, so
/// this delegates rather than re-deriving a limit or a word list. A caller that supplied no name at
/// all gets the validated default, which passes the same gate as a player's own choice.
/// </remarks>
public sealed class HeroNameDisplayNamePolicy : IDisplayNamePolicy
{
    private readonly ContentSnapshot _content;

    /// <summary>Builds the policy over the loaded content set the word lists live in.</summary>
    /// <param name="content">The server's content snapshot.</param>
    public HeroNameDisplayNamePolicy(ContentSnapshot content) => _content = content;

    /// <summary>The content set the word lists are read from.</summary>
    internal ContentSnapshot Content => _content;

    /// <inheritdoc/>
    public DisplayNameDecision Decide(string? candidate) => throw new NotImplementedException(
        "M5-06 Phase 3: a null candidate takes the rules library's validated default; anything else " +
        "goes through its public hero-name entry point, and a refusal crosses this boundary as the " +
        "refusal value alone — never the matched term.");
}
