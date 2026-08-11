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
    /// The primary-constructor parameter every id in this namespace declares, named once so the
    /// <c>paramName</c> on the refusal cannot drift from the parameter it blames.
    /// </summary>
    /// <remarks>
    /// 🔒 It is <c>Value</c>, not <c>value</c>, and the capital is load-bearing:
    /// <c>CanonicalStateWriter</c> matches a positional record's constructor parameters to its
    /// public properties by <b>case-sensitive</b> name (`14` §16.6). An id whose parameter were
    /// <c>value</c> would have no canonical encoding at all, and would be discovered by whoever
    /// first put one in a snapshot.
    /// </remarks>
    internal const string Parameter = "Value";

    /// <summary>
    /// Returns the identifier, or throws naming the id type that rejected it.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <param name="idType">The simple name of the id type being constructed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or whitespace.</exception>
    internal static string Require(string? value, string idType)
    {
        if (value is null)
        {
            throw new ArgumentNullException(Parameter, Explain(idType, "null"));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(Explain(idType, $"'{value}'"), Parameter);
        }

        return value;
    }

    private static string Explain(string idType, string got) =>
        $"A {idType} must be a non-empty identifier, and this one was {got}. An id names an " +
        "aggregate; a blank one names nothing, so it cannot be refused later by anything except a " +
        "lookup that returns no rows. 30 §11.3 puts that failure here, at the seam, rather than " +
        "three rules deeper.";
}
