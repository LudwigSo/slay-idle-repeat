using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// Reading an authored token as a member of a closed vocabulary, and rendering a number for a
/// diagnostic — the two things every tuning reader in this namespace does, stated once.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the parse is subtle and a second copy of it would eventually be a laxer copy.
/// <c>Enum.TryParse</c> accepts three spellings that are not a member <em>name</em>, and all three
/// land on a member <see cref="Enum.IsDefined{TEnum}(TEnum)"/> then reports as real:
/// </para>
/// <list type="bullet">
/// <item>the underlying wire value — an authored <c>"3"</c> would load as a real band, a wire value
/// leaking into a place the documents spell with a name;</item>
/// <item>a comma-separated list, combined bitwise even for a non-flags enum — an authored
/// <c>"C, B"</c> is <c>1 | 2</c> and would load, silently, as a third member;</item>
/// <item>surrounding whitespace, trimmed away — an authored <c>" SS"</c> is not the token the schema
/// enumerates, and accepting it would let two spellings of one member exist.</item>
/// </list>
/// <para>
/// Each is refused <em>before</em> the parse rather than after it.
/// </para>
/// </remarks>
internal static class AuthoredToken
{
    /// <summary>
    /// Reads a leaf as exactly one member's name, or refuses it naming the whole vocabulary.
    /// </summary>
    /// <typeparam name="TEnum">The closed vocabulary.</typeparam>
    /// <param name="content">The snapshot being read.</param>
    /// <param name="reference">The pointer the token is authored at.</param>
    /// <param name="what">
    /// What the vocabulary <em>is</em>, in the reader's own words — "an equipment slot", "a family
    /// axis" — so the refusal reads as a sentence rather than as a type name.
    /// </param>
    /// <returns>The member.</returns>
    /// <exception cref="InvalidTunableException">The token is not exactly one member's name.</exception>
    internal static TEnum Parse<TEnum>(ContentSnapshot content, string reference, string what)
        where TEnum : struct, Enum
    {
        var authored = content.ReadText(reference);

        return TryParse<TEnum>(authored, out var parsed)
            ? parsed
            : throw new InvalidTunableException(
                reference,
                $"'{authored}' is not {what}. The authored set is {Names<TEnum>()}, and a token " +
                "outside it is a value this engine has no branch for — accepting it would mean " +
                "choosing one of the members on the author's behalf.");
    }

    /// <summary>Whether a token is exactly one member's name, by name and case only.</summary>
    /// <typeparam name="TEnum">The closed vocabulary.</typeparam>
    /// <param name="authored">The token the document spells.</param>
    /// <param name="parsed">The member, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the token is exactly one member's name.</returns>
    internal static bool TryParse<TEnum>(string authored, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;

        return !string.IsNullOrEmpty(authored) &&
            !char.IsAsciiDigit(authored[0]) && authored[0] != '-' && authored[0] != '+' &&
            !authored.Contains(',') &&
            !char.IsWhiteSpace(authored[0]) && !char.IsWhiteSpace(authored[^1]) &&
            Enum.TryParse(authored, ignoreCase: false, out parsed) &&
            Enum.IsDefined(parsed);
    }

    /// <summary>A closed vocabulary's names, for a failure message that shows the whole table.</summary>
    /// <typeparam name="TEnum">The closed vocabulary.</typeparam>
    /// <returns>The names, comma-separated, in declaration order.</returns>
    internal static string Names<TEnum>()
        where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>());

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The invariant rendering.</returns>
    internal static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The invariant rendering.</returns>
    internal static string Render(double value) => value.ToString(CultureInfo.InvariantCulture);
}
