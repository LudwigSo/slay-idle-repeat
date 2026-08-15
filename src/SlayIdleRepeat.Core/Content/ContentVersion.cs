namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The version stamp of a <see cref="ContentSnapshot"/>: a deterministic hash of the content it
/// was built from, never a hand-bumped number.
/// </summary>
/// <remarks>
/// A hand-bumped integer cannot reliably drive replay: somebody forgets to bump it exactly once
/// and every replay afterwards is quietly wrong. A hash makes that failure mode unavailable. It
/// is computed by the load path over a canonical serialisation of the loaded documents — Core
/// references nothing, so it holds the stamp rather than computing it.
/// </remarks>
public sealed class ContentVersion : IEquatable<ContentVersion>
{
    /// <summary>The number of hex characters in a stamp: SHA-256, lowercase.</summary>
    public const int HexLength = 64;

    private ContentVersion(string value) => Value = value;

    /// <summary>The full stamp, 64 lowercase hex characters.</summary>
    public string Value { get; }

    /// <summary>The first 12 characters of <see cref="Value"/> — enough for a log line.</summary>
    public string Short => Value[..12];

    /// <summary>Creates a stamp from 64 lowercase hex characters. Anything else throws.</summary>
    public static ContentVersion FromHex(string hex) =>
        TryFromHex(hex, out var version)
            ? version!
            : throw new FormatException(
                $"'{hex}' is not a content version stamp. A stamp is exactly {HexLength} lowercase " +
                "hex characters — the SHA-256 of the canonical content, never a hand-written label.");

    /// <summary>Creates a stamp from 64 lowercase hex characters, or returns false.</summary>
    public static bool TryFromHex(string hex, out ContentVersion? version)
    {
        version = null;
        if (hex is null || hex.Length != HexLength)
        {
            return false;
        }

        foreach (var character in hex)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        version = new ContentVersion(hex);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(ContentVersion? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentVersion);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}
