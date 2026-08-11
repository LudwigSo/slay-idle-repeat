namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The base of every fault raised while <em>reading</em> a <see cref="ContentSnapshot"/>.
/// </summary>
/// <remarks>
/// `30` §2.1's "an illegal move is data, never an exception" governs <em>player commands</em>.
/// These are not commands: a rule that reads a tunable which does not exist, holds the wrong
/// type, or was never authorised is a content or programming defect, and the only safe
/// behaviour is to stop. `14` §6's whole point is that the economy is data — a silent default
/// in the read path would produce a plausible, wrong economy that nothing would ever flag.
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
/// 🔒 Raised when a rule reads a leaf that is <see cref="ContentValueKind.Unauthorised"/> —
/// a <c>null</c> in the data files, which `game-data/README.md` defines as
/// <em>"the design docs do not authorise a value here"</em>.
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
