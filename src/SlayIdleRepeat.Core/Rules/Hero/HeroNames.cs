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
    public static HeroNameDecision Decide(string? candidate, ContentSnapshot content)
    {
        var verdict = HeroNameRule.Validate(candidate, ProfanityLexicon.Read(content));

        // The verdict's Match is dropped here rather than one layer up, so there is no shape holding
        // the term on this side of the seam for a later caller to reach for.
        return new HeroNameDecision(verdict.Name, verdict.Refusal);
    }

    /// <summary>The name a player who has not chosen one carries, through the same gate as a chosen one.</summary>
    /// <param name="content">The content set the word lists are read from.</param>
    /// <returns>The validated default.</returns>
    /// <exception cref="InvalidOperationException">
    /// The word lists this snapshot carries refuse the authored default. Every account that has not
    /// chosen a name carries it, so that is a data defect worth stopping on rather than waving past.
    /// </exception>
    public static HeroName Default(ContentSnapshot content) =>
        HeroNameRule.Default(ProfanityLexicon.Read(content));
}
