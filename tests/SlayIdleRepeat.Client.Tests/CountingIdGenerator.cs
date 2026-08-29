using System.Globalization;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// An <see cref="IIdGeneratorPort"/> that mints a counted, readable identifier and remembers how
/// many it has minted.
/// </summary>
/// <remarks>
/// 🔒 <see cref="Minted"/> is exposed because the claims these fixtures make are about minting
/// exactly once. "The retry carried the same command id" is satisfied by a queue that reminted the
/// id and by one that did not, if the two ids happen to compare equal — counting the mints is what
/// tells them apart.
/// </remarks>
internal sealed class CountingIdGenerator : IIdGeneratorPort
{
    /// <summary>Reads in a failure message as an id rather than as a bare number.</summary>
    private const string CommandIdPrefix = "CMD_";

    private int _minted;

    private CountingIdGenerator()
    {
    }

    /// <summary>A generator whose first command id is <c>CMD_1</c>.</summary>
    internal static CountingIdGenerator Counting() => new();

    /// <summary>How many identifiers have been handed out.</summary>
    internal int Minted => _minted;

    /// <summary>The identifier this generator will hand out on its <paramref name="ordinal"/>th call.</summary>
    /// <param name="ordinal">One-based.</param>
    internal static string CommandIdNumber(int ordinal) =>
        CommandIdPrefix + ordinal.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    /// <remarks>Never <see cref="Guid.Empty"/>: the counter is incremented before it is written in.</remarks>
    public Guid NewGuid()
    {
        _minted++;

        return new Guid(_minted, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
    }

    /// <inheritdoc/>
    public string NewCommandId()
    {
        _minted++;

        return CommandIdNumber(_minted);
    }
}
