using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Computes the <see cref="ContentVersion"/> stamp: SHA-256 over a canonical serialisation of the
/// loaded documents.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The canonical form is unambiguous by construction — documents in ordinal path order, object
/// members ordinal-sorted, arrays in index order, every string and count length-prefixed, numbers
/// written as a normalised invariant decimal. No locale, no hash-table iteration order and no
/// floating-point formatting can reach it, so the same content stamps identically on a Windows
/// dev box, a Linux CI runner and an Android device. `14` §16.6's snapshot hashing is built on
/// this later; a non-deterministic ordering here would break it silently rather than loudly.
/// </para>
/// <para>
/// The stamp identifies <em>content</em>, not <em>formatting</em>: reindenting a data file does
/// not move it, changing any key or value does. That is the property `14` §6 actually needs —
/// "a replayed command reproduces its original outcome" is about values, and a stamp that moved
/// on whitespace would force a spurious content download after a formatting commit.
/// </para>
/// </remarks>
public static class ContentHashing
{
    /// <summary>
    /// Version of the canonical encoding, mixed into the hash. Bumping it moves every stamp, so it
    /// changes only when the encoding itself changes — deliberately, never as a side effect.
    /// </summary>
    public const byte CanonicalFormatVersion = 1;

    /// <summary>The canonical bytes the stamp is taken over. Exposed so a test can pin the encoding.</summary>
    public static byte[] CanonicalBytes(IEnumerable<ContentDocument> documents) =>
        throw new NotImplementedException();

    /// <summary>The deterministic stamp of a document set.</summary>
    public static ContentVersion Compute(IEnumerable<ContentDocument> documents) =>
        throw new NotImplementedException();
}
