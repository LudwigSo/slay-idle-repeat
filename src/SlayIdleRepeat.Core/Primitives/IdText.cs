using System.Runtime.CompilerServices;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The one guard behind every identity type in this namespace: an id is text, and blank text is
/// not an id.
/// </summary>
/// <remarks>
/// <para>
/// Shared rather than repeated per id type so there is one guard to prove and one sentence to
/// maintain. The id type's name is passed in, because both guards are otherwise identical and the
/// name is the only thing in the message that tells the reader which seam to open.
/// </para>
/// <para>
/// Ids arrive from outside the domain — a wire envelope, a persisted row — and `30` §11.3 wants a
/// corrupt one to fail <i>at the seam</i> rather than three rules later. A blank id addresses no
/// aggregate at all, so it is refused where it is constructed.
/// </para>
/// <para>
/// ⚠️ The guard cannot cover <c>default(PlayerId)</c>: an id is a struct, and no constructor runs
/// for a struct's default. That is a deliberate trade — see <see cref="PlayerId"/> for why the
/// shape is a struct — and it is why a snapshot slot holding an id is validated on rehydration
/// (`30` §11.3) rather than trusted because the type exists.
/// </para>
/// </remarks>
internal static class IdText
{
    /// <summary>
    /// Returns the identifier, or throws naming the id type that rejected it.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <param name="idType">The simple name of the id type being constructed.</param>
    /// <param name="parameter">
    /// The parameter the refusal blames. 🔒 Never passed by hand — the compiler substitutes the
    /// source text of <paramref name="value"/> at the call site, which is the primary-constructor
    /// parameter itself, so a rename carries the <c>paramName</c> with it. A hand-written constant
    /// here would keep blaming a parameter that no longer exists, and nothing would say so.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or whitespace.</exception>
    internal static string Require(
        string? value,
        string idType,
        [CallerArgumentExpression(nameof(value))] string? parameter = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameter, Explain(idType, "null"));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(Explain(idType, $"'{value}'"), parameter);
        }

        return value;
    }

    private static string Explain(string idType, string got) =>
        $"A {idType} must be a non-empty identifier, and this one was {got}. An id names an " +
        "aggregate; a blank one names nothing, so it cannot be refused later by anything except a " +
        "lookup that returns no rows. 30 §11.3 puts that failure here, at the seam, rather than " +
        "three rules deeper.";
}
