using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>The hero-name filter, as the one entry point everything outside Core reaches it through.</summary>
/// <remarks>
/// The rule itself is internal and so is the constructor of the name it mints, so account creation and
/// the in-process host had no way to call it at all. This is that way: it takes a content snapshot
/// rather than a loaded lexicon, because the word lists are content and a caller outside Core cannot
/// see the type they load into.
/// </remarks>
public static class HeroNames
{
    /// <summary>Decides a candidate name against the word lists the snapshot carries.</summary>
    /// <param name="candidate">The name as the player typed it. <see langword="null"/> is refused, not thrown.</param>
    /// <param name="content">The content set the word lists are read from.</param>
    /// <returns>The accepted name, or which check refused it.</returns>
    public static HeroNameDecision Decide(string? candidate, ContentSnapshot content) =>
        throw new NotImplementedException(
            "HeroNames.Decide has no body yet. It runs the candidate through the internal name rule " +
            "over the word lists this snapshot carries and answers the accepted name or the refusal " +
            "— and never the matched term.");

    /// <summary>The name a player who has not chosen one carries, through the same gate as a chosen one.</summary>
    /// <param name="content">The content set the word lists are read from.</param>
    /// <returns>The validated default.</returns>
    public static HeroName Default(ContentSnapshot content) =>
        throw new NotImplementedException(
            "HeroNames.Default has no body yet. It answers the authored default name, validated " +
            "through the same filter a player's own choice passes.");
}
