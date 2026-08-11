using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// Reads <c>Hash64ReferenceVectors.json</c> — the committed determinism pin of `14` §8.0 —
/// out of this assembly's embedded resources.
/// </summary>
/// <remarks>
/// The rows in that file were generated from a second, independent XXH64 implementation
/// (<c>System.IO.Hashing.XxHash64</c>) and are the expectations, not the output of the code
/// under test. Nothing here re-derives a hash: it only parses.
/// </remarks>
internal static class ReferenceVectors
{
    private const string ResourceName = "SlayIdleRepeat.Core.Tests.Rng.Hash64ReferenceVectors.json";

    private static readonly JsonDocument Document = Load();

    /// <summary>Rows from the reference implementation's own sanity check, over its generated buffer.</summary>
    internal static IReadOnlyList<PublishedRow> Published { get; } = ReadPublished();

    /// <summary>The three short ASCII vectors that circulate with every port of xxHash.</summary>
    internal static IReadOnlyList<AsciiRow> PublishedAscii { get; } = ReadAscii();

    /// <summary>Rows over the canonical argument encoding of `14` §8.0.</summary>
    internal static IReadOnlyList<CanonicalRow> Canonical { get; } = ReadCanonical();

    /// <summary>Rows over <see cref="DeterministicRng"/>'s accessors.</summary>
    internal static IReadOnlyList<RngRow> Draws { get; } = ReadDraws();

    /// <summary>
    /// The sanity-check buffer the xxHash CLI builds, rebuilt from the two primes the
    /// committed file carries. Only the generator expression itself lives in code.
    /// </summary>
    internal static byte[] SanityBuffer(int length)
    {
        var generator = Document.RootElement
            .GetProperty("xxh64PublishedKnownAnswerVectors")
            .GetProperty("bufferGenerator");

        var byteGen = ulong.Parse(generator.GetProperty("prime32").GetString()!, CultureInfo.InvariantCulture);
        var prime64 = ulong.Parse(generator.GetProperty("prime64").GetString()!, CultureInfo.InvariantCulture);

        var buffer = new byte[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = (byte)(byteGen >> 56);
            byteGen *= prime64;
        }

        return buffer;
    }

    /// <summary>The one canonical-encoding row with this id.</summary>
    internal static CanonicalRow Row(string id) =>
        Canonical.Single(row => row.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>The one draw row with this id.</summary>
    internal static RngRow DrawRow(string id) =>
        Draws.Single(row => row.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>Every canonical row id, as xUnit theory data.</summary>
    internal static TheoryData<string> CanonicalIds()
    {
        var data = new TheoryData<string>();
        foreach (var row in Canonical)
        {
            data.Add(row.Id);
        }

        return data;
    }

    /// <summary>Every draw row id, as xUnit theory data.</summary>
    internal static TheoryData<string> DrawIds()
    {
        var data = new TheoryData<string>();
        foreach (var row in Draws)
        {
            data.Add(row.Id);
        }

        return data;
    }

    /// <summary>Every published sanity row, as xUnit theory data.</summary>
    internal static TheoryData<int, ulong, ulong> PublishedRows()
    {
        var data = new TheoryData<int, ulong, ulong>();
        foreach (var row in Published)
        {
            data.Add(row.Length, row.Seed, row.Hash);
        }

        return data;
    }

    /// <summary>Every short ASCII vector, as xUnit theory data.</summary>
    internal static TheoryData<string, ulong> AsciiRows()
    {
        var data = new TheoryData<string, ulong>();
        foreach (var row in PublishedAscii)
        {
            data.Add(row.Input, row.Hash);
        }

        return data;
    }

    private static JsonDocument Load()
    {
        using var stream = typeof(ReferenceVectors).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The determinism pin '{ResourceName}' is not embedded in " +
                $"{typeof(ReferenceVectors).Assembly.GetName().Name}. Available: " +
                string.Join(", ", typeof(ReferenceVectors).Assembly.GetManifestResourceNames()));

        return JsonDocument.Parse(stream);
    }

    private static ulong ParseHex(string text) =>
        ulong.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static IReadOnlyList<PublishedRow> ReadPublished() =>
        Document.RootElement
            .GetProperty("xxh64PublishedKnownAnswerVectors")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(element => new PublishedRow(
                element.GetProperty("length").GetInt32(),
                ParseHex(element.GetProperty("seed").GetString()!),
                ParseHex(element.GetProperty("hash").GetString()!)))
            .ToArray();

    private static IReadOnlyList<AsciiRow> ReadAscii() =>
        Document.RootElement
            .GetProperty("xxh64PublishedKnownAnswerVectors")
            .GetProperty("asciiRows")
            .EnumerateArray()
            .Select(element => new AsciiRow(
                element.GetProperty("input").GetString()!,
                ParseHex(element.GetProperty("hash").GetString()!)))
            .ToArray();

    private static IReadOnlyList<CanonicalRow> ReadCanonical() =>
        Document.RootElement
            .GetProperty("canonicalEncodingVectors")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(element => new CanonicalRow(
                element.GetProperty("id").GetString()!,
                element.GetProperty("why").GetString()!,
                element.GetProperty("encodedByteLength").GetInt32(),
                element.GetProperty("args").EnumerateArray().Select(ReadArgument).ToArray(),
                element.GetProperty("args").EnumerateArray().Select(a => a.GetProperty("type").GetString()!).ToArray(),
                ParseHex(element.GetProperty("hash").GetString()!)))
            .ToArray();

    private static IReadOnlyList<RngRow> ReadDraws() =>
        Document.RootElement
            .GetProperty("deterministicRngVectors")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(element => new RngRow(
                element.GetProperty("id").GetString()!,
                ParseHex(element.GetProperty("seed").GetString()!),
                element.GetProperty("stream").GetString()!,
                ulong.Parse(element.GetProperty("position").GetString()!, CultureInfo.InvariantCulture),
                ParseHex(element.GetProperty("draw").GetString()!),
                (uint)ulong.Parse(element.GetProperty("nextUInt").GetString()!.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                ParseHex(element.GetProperty("nextDoubleBits").GetString()!),
                element.GetProperty("rangeMinInclusive").GetInt32(),
                element.GetProperty("rangeMaxExclusive").GetInt32(),
                element.GetProperty("range").GetInt32()))
            .ToArray();

    /// <summary>
    /// Turns one JSON argument into the <see cref="Hash64Argument"/> the production API takes.
    /// The enum rows name one of the eight <see cref="ReferenceEnums"/> types so the widening
    /// rule is pinned for every underlying integral type.
    /// </summary>
    private static Hash64Argument ReadArgument(JsonElement element)
    {
        var type = element.GetProperty("type").GetString()!;
        var value = element.GetProperty("value").GetString()!;

        return type switch
        {
            "ulong" => ulong.Parse(value, CultureInfo.InvariantCulture),
            "long" => long.Parse(value, CultureInfo.InvariantCulture),
            "int" => int.Parse(value, CultureInfo.InvariantCulture),
            "string" => value,
            "enum" => ReadEnum(element.GetProperty("enum").GetString()!, value),
            _ => throw new InvalidOperationException($"Unknown reference-vector argument type '{type}'."),
        };
    }

    private static Hash64Argument ReadEnum(string enumName, string value)
    {
        var enumType = typeof(ReferenceEnums).GetNestedType(enumName, BindingFlags.Public)
            ?? throw new InvalidOperationException($"The reference table names an unknown test enum '{enumName}'.");

        var underlying = Type.GetTypeCode(Enum.GetUnderlyingType(enumType));

        // A ulong-backed enum can hold values no long can, so it is parsed unsigned.
        object boxed = underlying == TypeCode.UInt64
            ? Enum.ToObject(enumType, ulong.Parse(value, CultureInfo.InvariantCulture))
            : Enum.ToObject(enumType, long.Parse(value, CultureInfo.InvariantCulture));

        return (Enum)boxed;
    }

    internal sealed record PublishedRow(int Length, ulong Seed, ulong Hash);

    internal sealed record AsciiRow(string Input, ulong Hash);

    internal sealed record CanonicalRow(
        string Id,
        string Why,
        int EncodedByteLength,
        IReadOnlyList<Hash64Argument> Arguments,
        IReadOnlyList<string> ArgumentTypes,
        ulong Hash);

    internal sealed record RngRow(
        string Id,
        ulong Seed,
        string Stream,
        ulong Position,
        ulong Draw,
        uint NextUInt,
        ulong NextDoubleBits,
        int RangeMinInclusive,
        int RangeMaxExclusive,
        int Range);
}

/// <summary>
/// One enum per underlying integral type, so the "widened to 64 bits; signed ones
/// sign-extended" rule of `14` §8.0 is pinned for all eight. The values come from the
/// reference table, not from these declarations — the members exist only to give each type
/// a shape.
/// </summary>
internal static class ReferenceEnums
{
    public enum SByteEnum : sbyte
    {
        Zero = 0,
    }

    public enum ByteEnum : byte
    {
        Zero = 0,
    }

    public enum Int16Enum : short
    {
        Zero = 0,
    }

    public enum UInt16Enum : ushort
    {
        Zero = 0,
    }

    public enum Int32Enum
    {
        Zero = 0,
    }

    public enum UInt32Enum : uint
    {
        Zero = 0,
    }

    public enum Int64Enum : long
    {
        Zero = 0,
    }

    public enum UInt64Enum : ulong
    {
        Zero = 0,
    }
}
