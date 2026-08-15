namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The base of every fault raised while <em>reading</em> a <see cref="ContentSnapshot"/>.
/// </summary>
/// <remarks>
/// A rule that reads a tunable which does not exist, holds the wrong type, or was never
/// authorised has hit a content or programming defect, and the only safe behaviour is to stop —
/// a silent default in the read path would produce a plausible, wrong economy that nothing would
/// ever flag.
/// </remarks>
public abstract class ContentException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    protected ContentException(string message)
        : base(message)
    {
    }
}

/// <summary>Raised when a content reference names a document or path that is not in the snapshot.</summary>
public sealed class MissingContentException : ContentException
{
    /// <summary>Creates the exception for the given reference.</summary>
    public MissingContentException(string reference, string detail)
        : base($"Content reference '{reference}' resolves to nothing: {detail}")
    {
        Reference = reference;
    }

    /// <summary>The reference that resolved to nothing.</summary>
    public string Reference { get; }
}

/// <summary>
/// Raised when a rule reads a leaf that is <see cref="ContentValueKind.Unauthorised"/> — a
/// deliberate <c>null</c> in the data files, meaning no value was authorised there.
/// </summary>
/// <remarks>
/// This is the loudest failure in the content pipeline on purpose. The alternative — reading
/// zero — is invisible: it produces numbers, the simulator grades them, and nobody ever learns
/// that a curve nobody authored was silently treated as flat.
/// </remarks>
public sealed class UnauthorisedTunableException : ContentException
{
    /// <summary>Creates the exception for the given reference.</summary>
    public UnauthorisedTunableException(string reference)
        : base($"Content reference '{reference}' is null, which means the design docs do not " +
               "authorise a value here (game-data/README.md). It is not zero and it is " +
               "not a default. Author the value, or do not read it.")
    {
        Reference = reference;
    }

    /// <summary>The reference whose value was never authorised.</summary>
    public string Reference { get; }
}

/// <summary>
/// Raised when a tunable is present, authorised and of the right type, but holds a value the
/// rule reading it cannot work with — a Max Energy of zero, a cap below the base, a regeneration
/// interval that is not a positive span.
/// </summary>
/// <remarks>
/// The fourth member of the family: <see cref="MissingContentException"/> is "nothing there",
/// <see cref="UnauthorisedTunableException"/> is "a deliberate <c>null</c>",
/// <see cref="ContentTypeMismatchException"/> is "the wrong kind", and this is "the right kind, an
/// impossible value". The <paramref name="detail"/> is the reading rule's, not this type's — only
/// the rule knows what range is actually valid.
/// </remarks>
public sealed class InvalidTunableException : ContentException
{
    /// <summary>Creates the exception for the reference that holds the unusable value.</summary>
    /// <param name="reference">The content reference, e.g. <c>tuning/progression.json#/energy/baseMax</c>.</param>
    /// <param name="detail">What is wrong with it, and which document authorises what instead.</param>
    public InvalidTunableException(string reference, string detail)
        : base($"Content reference '{reference}' is authorised but unusable: {detail}")
    {
        Reference = reference;
    }

    /// <summary>The reference whose value the rule reading it cannot work with.</summary>
    public string Reference { get; }
}

/// <summary>Raised when a content value is read as a type it does not hold.</summary>
public sealed class ContentTypeMismatchException : ContentException
{
    /// <summary>Creates the exception for the given reference.</summary>
    public ContentTypeMismatchException(string reference, ContentValueKind actual, string expected)
        : base($"Content reference '{reference}' holds {actual} but was read as {expected}.")
    {
        Reference = reference;
        Actual = actual;
        Expected = expected;
    }

    /// <summary>The reference that was read.</summary>
    public string Reference { get; }

    /// <summary>The kind the value actually holds.</summary>
    public ContentValueKind Actual { get; }

    /// <summary>The kind the caller asked for.</summary>
    public string Expected { get; }
}
