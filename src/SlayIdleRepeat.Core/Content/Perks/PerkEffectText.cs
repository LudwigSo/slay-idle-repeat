namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// Renders one perk tier's player-facing sentence with the real numbers of that tier's authored
/// Effect DSL data substituted into <see cref="PerkCatalogueEntry.Description"/>'s tokens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generic over the DSL, with zero per-perk branching.</b> Every token names a member of the
/// tier's own effect data and is resolved by reading that member; nothing here keys on a perk id or
/// on an effect id. A renderer that special-cased one perk would be a second catalogue, and the
/// perk it special-cased would be the only one that stayed correct when the data moved.
/// </para>
/// <para>
/// <b>The tier's anchor effect is where every number comes from.</b> It is the first effect in the
/// tier carrying a <c>value</c>, falling back to the tier's first effect when no effect carries one
/// at all — a tier whose whole sentence is about a trigger has no <c>value</c> to anchor on and is
/// still fully described by its one effect. Reading each token off a different effect would let one
/// sentence mix two unrelated effects' numbers and read as a single claim.
/// </para>
/// <para>
/// <b>A token immediately followed by <c>%</c> renders multiplied by 100.</b> The sources behind a
/// percent-suffixed token are authored as fractions and the sources behind a bare one are authored
/// in their display unit, so the suffix in the template is what says which the author meant.
/// </para>
/// <para>
/// 🔒 <b>A token that resolves to nothing is neither substituted nor guessed.</b> The render carries
/// no text at all and names the offending tokens instead, so a caller cannot draw a sentence with a
/// hole in it or with a plausible number invented to fill one. That is why
/// <see cref="PerkEffectTextRender.Text"/> is non-null exactly when
/// <see cref="PerkEffectTextRender.UnresolvedTokens"/> is empty.
/// </para>
/// </remarks>
public static class PerkEffectText
{
    /// <summary>
    /// Renders <paramref name="perkId"/>'s description at <paramref name="tier"/>, or names the
    /// tokens the tier's data cannot answer.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the caller is reading.</param>
    /// <param name="perkId">The <c>PK_*</c> id.</param>
    /// <param name="tier">The internal tier, 1-based.</param>
    /// <exception cref="ArgumentNullException">Either reference argument is null.</exception>
    /// <exception cref="ArgumentException">The catalogue carries no such perk.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The perk authors no such tier.</exception>
    public static PerkEffectTextRender Render(ContentSnapshot content, string perkId, int tier) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: the token renderer is written against the failing cases in " +
            "PerkEffectTextTests and filled in by the implementation phase.");
}

/// <summary>What one render answered: the finished sentence, or the tokens that stopped it.</summary>
/// <remarks>
/// The two halves are mutually exclusive by construction — there is no door that produces a text and
/// a non-empty token list — so a caller reading <see cref="Text"/> is reading a sentence whose every
/// number came from the data.
/// </remarks>
public readonly record struct PerkEffectTextRender
{
    private readonly IReadOnlyList<string>? _unresolved;

    private PerkEffectTextRender(string? text, IReadOnlyList<string> unresolved)
    {
        Text = text;
        _unresolved = unresolved;
    }

    /// <summary>The rendered sentence, or <c>null</c> when a token could not be resolved.</summary>
    public string? Text { get; }

    /// <summary>The tokens the tier's data could not answer, in the order the template names them. Empty on a successful render.</summary>
    /// <exception cref="InvalidOperationException">This is <c>default(PerkEffectTextRender)</c>, which no render returns.</exception>
    public IReadOnlyList<string> UnresolvedTokens => _unresolved ?? throw new InvalidOperationException(
        "This is default(PerkEffectTextRender), which PerkEffectText.Render never answers: it " +
        "carries neither a sentence nor the tokens that stopped one, so a caller cannot tell a " +
        "rendered perk from an unrenderable one. Obtain a render from PerkEffectText.Render.");

    /// <summary>Whether a sentence was produced at all.</summary>
    public bool IsRendered => Text is not null;

    /// <summary>A successful render.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    internal static PerkEffectTextRender Rendered(string text) =>
        new(text ?? throw new ArgumentNullException(nameof(text)), []);

    /// <summary>A refused render, naming the tokens the data could not answer.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tokens"/> is empty — that is a successful render, not a refused one.</exception>
    internal static PerkEffectTextRender Unresolvable(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return tokens.Count == 0
            ? throw new ArgumentException(
                "A refused render names at least one token. An empty list is what a SUCCESSFUL " +
                "render carries, and building one here would produce a value whose Text is null and " +
                "whose token list is empty — the one shape this type exists to make unreachable.",
                nameof(tokens))
            : new PerkEffectTextRender(text: null, tokens);
    }
}
