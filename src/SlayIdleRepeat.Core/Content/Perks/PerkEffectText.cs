using System.Globalization;
using System.Text;

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
    /// <summary>The member the anchor is chosen by, and the left half of the <c>{cap}</c> product.</summary>
    private const string ValueMember = "value";

    /// <summary>How a number is written: trailing zeros trimmed, four decimal places at most.</summary>
    /// <remarks>
    /// The four places are the format's, not a rounding step: a rendered sentence is text a player
    /// reads and never an accumulation point, so nothing here restates the determinism rule that
    /// governs those. The authored value is carried to the formatter exactly as the document holds
    /// it, in <c>decimal</c>, and only the printed digits are limited.
    /// </remarks>
    private const string NumberFormat = "0.####";

    /// <summary>What a percent-suffixed token multiplies its authored fraction by.</summary>
    private const decimal PercentScale = 100m;

    private const char TokenOpen = '{';
    private const char TokenClose = '}';
    private const char PercentSuffix = '%';

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
    public static PerkEffectTextRender Render(ContentSnapshot content, string perkId, int tier)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(perkId);

        var perk = PerkTierEffects.Row(content, perkId);

        return Substitute(
            PerkTierEffects.Description(perk, perkId),
            AnchorOf(PerkTierEffects.Of(perk, perkId, tier)));
    }

    /// <summary>
    /// The one effect every token in this tier's sentence is read off: the first carrying a
    /// <c>value</c>, or the tier's first effect when none does. <c>null</c> for a tier authoring no
    /// effects at all, which can answer no token.
    /// </summary>
    private static ContentValue? AnchorOf(IReadOnlyList<ContentValue> effects)
    {
        foreach (var effect in effects)
        {
            if (Number(effect, ValueMember) is not null)
            {
                return effect;
            }
        }

        return effects.Count > 0 ? effects[0] : null;
    }

    /// <summary>
    /// Walks the template once, substituting what the anchor answers and collecting what it cannot.
    /// </summary>
    /// <remarks>
    /// The walk finishes even after a token goes unanswered, and the text it built is then thrown
    /// away: the whole token list is what a caller needs in order to say which numbers are missing,
    /// and stopping at the first would name one of two.
    /// </remarks>
    private static PerkEffectTextRender Substitute(string template, ContentValue? anchor)
    {
        var text = new StringBuilder(template.Length);
        var unresolved = new List<string>();
        var read = 0;

        while (read < template.Length)
        {
            var open = template.IndexOf(TokenOpen, read);
            var close = open < 0 ? -1 : template.IndexOf(TokenClose, open + 1);

            if (close < 0)
            {
                text.Append(template, read, template.Length - read);
                break;
            }

            text.Append(template, read, open - read);

            var token = template[(open + 1)..close];
            var percentSuffixed = close + 1 < template.Length && template[close + 1] == PercentSuffix;
            var source = anchor is null ? null : Source(anchor, token);

            if (source is { } number)
            {
                text.Append(Format(percentSuffixed ? number * PercentScale : number));
            }
            else if (!unresolved.Contains(token, StringComparer.Ordinal))
            {
                unresolved.Add(token);
            }

            read = close + 1;
        }

        return unresolved.Count > 0
            ? PerkEffectTextRender.Unresolvable(unresolved)
            : PerkEffectTextRender.Rendered(text.ToString());
    }

    /// <summary>
    /// What one token names on the anchor effect, or <c>null</c> when the data does not carry it.
    /// </summary>
    /// <remarks>
    /// Keyed on the token, which is the name of an effect MEMBER — never on a perk or an effect id.
    /// <c>{cap}</c> is a product rather than a lookup because the schema's own scaling is
    /// <c>value × steps</c> with the steps capped, so the effective value at the top of the scale is
    /// the authored value multiplied by the cap and not the cap on its own.
    /// </remarks>
    private static decimal? Source(ContentValue anchor, string token) => token switch
    {
        ValueMember => Number(anchor, ValueMember),
        "duration" => Number(anchor, "duration", "seconds"),
        "everyNth" => Number(anchor, "trigger", "everyNth"),
        "interval" => Number(anchor, "trigger", "interval"),
        "sourceCapPct" => Number(anchor, "sourceCapPct"),
        "cap" => Number(anchor, ValueMember) * Number(anchor, "valueScale", "cap"),
        _ => null,
    };

    /// <summary>
    /// The number at <paramref name="path"/> under <paramref name="root"/>, or <c>null</c> where any
    /// step of the path is absent, authored as a deliberate null, or holds something else.
    /// </summary>
    private static decimal? Number(ContentValue root, params string[] path)
    {
        var value = root;

        foreach (var member in path)
        {
            if (!value.TryGetMember(member, out var next) || next is null || next.IsUnauthorised)
            {
                return null;
            }

            value = next;
        }

        return value.Kind == ContentValueKind.Number ? value.AsNumber() : null;
    }

    private static string Format(decimal value) =>
        value.ToString(NumberFormat, CultureInfo.InvariantCulture);
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

/// <summary>
/// The authored rows a perk's effect data is read out of: the perk itself, its description template,
/// and the effects of one of its tiers.
/// </summary>
/// <remarks>
/// Shared rather than restated. <see cref="PerkEffectText"/> reads a tier's effects to substitute its
/// numbers and the draft projection reads the same effects to find the statuses an offered option and
/// an owned perk both name; two walks of the same pointers would be two chances to disagree about
/// which tier's data a card is describing. <c>PerkCatalogue</c> deliberately stops at
/// <see cref="PerkCatalogueEntry.TierCount"/>, so the effect tree has no other reader.
/// </remarks>
internal static class PerkTierEffects
{
    private const string IdMember = "id";
    private const string DescriptionMember = "description";
    private const string TiersMember = "tiers";
    private const string TierMember = "tier";
    private const string EffectsMember = "effects";

    /// <summary>The authored row for <paramref name="perkId"/>.</summary>
    /// <exception cref="ArgumentException">The catalogue carries no such perk.</exception>
    internal static ContentValue Row(ContentSnapshot content, string perkId) =>
        TryRow(content, perkId, out var perk)
            ? perk
            : throw new ArgumentException(
                "06 §3 authors no perk '" + perkId + "'. A perk id reaching here that the catalogue " +
                "does not carry means a Run persisted an owned-perk id this content version no " +
                "longer has — a content rollback across a live run, not a player input.",
                nameof(perkId));

    /// <summary>The authored row for <paramref name="perkId"/>, or false when this version has none.</summary>
    internal static bool TryRow(ContentSnapshot content, string perkId, out ContentValue row)
    {
        foreach (var perk in content.Read(PerkCatalogue.PerksReference).Items)
        {
            if (perk.TryGetMember(IdMember, out var id) &&
                id is { Kind: ContentValueKind.Text } &&
                string.Equals(id.AsText(), perkId, StringComparison.Ordinal))
            {
                row = perk;

                return true;
            }
        }

        row = ContentValue.Unauthorised;

        return false;
    }

    /// <summary>The perk's description template — the sentence its numbers are substituted into.</summary>
    /// <exception cref="ArgumentException">The row authors no description.</exception>
    internal static string Description(ContentValue perk, string perkId) =>
        perk.TryGetMember(DescriptionMember, out var description) &&
        description is { Kind: ContentValueKind.Text }
            ? description.AsText()
            : throw new ArgumentException(
                "'" + perkId + "' authors no description, so there is no sentence to render at all.",
                nameof(perkId));

    /// <summary>The effects one tier of a perk authors, in the document's order.</summary>
    /// <remarks>
    /// Matched on the authored <c>tier</c> number rather than on the array index, so a tier list
    /// written out of order still answers the tier the caller asked for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The perk authors no such tier.</exception>
    internal static IReadOnlyList<ContentValue> Of(ContentValue perk, string perkId, int tier)
    {
        if (perk.TryGetMember(TiersMember, out var tiers) && tiers is not null)
        {
            foreach (var row in tiers.Items)
            {
                if (row.TryGetMember(TierMember, out var number) &&
                    number is { Kind: ContentValueKind.Number } &&
                    number.AsNumber() == tier)
                {
                    return row.TryGetMember(EffectsMember, out var effects) && effects is not null
                        ? effects.Items
                        : [];
                }
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(tier),
            tier,
            "'" + perkId + "' authors no tier " + tier.ToString(CultureInfo.InvariantCulture) +
            ". 06 §1.1 numbers a perk's internal tiers from 1.");
    }
}
