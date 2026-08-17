using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>A generator that counts what was drawn through it.</summary>
/// <remarks>
/// A decorator rather than a second fake, on <c>RecordingCache</c>'s precedent: the shipped fake still
/// mints every identifier, and only the tally is this class's own. Fresh entropy is the observable
/// difference between a command that is issued a seed and one that is not, and the port is where it
/// enters — so counting here is how a case tells the two apart.
/// </remarks>
internal sealed class RecordingIdGenerator : IIdGeneratorPort
{
    private readonly IIdGeneratorPort _inner;

    /// <summary>Wraps a generator.</summary>
    /// <param name="inner">The generator that actually mints the identifiers.</param>
    internal RecordingIdGenerator(IIdGeneratorPort inner) => _inner = inner;

    /// <summary>How many guids have been drawn through this generator.</summary>
    internal int Guids { get; private set; }

    /// <summary>How many command ids have been drawn through this generator.</summary>
    internal int CommandIds { get; private set; }

    /// <inheritdoc/>
    public Guid NewGuid()
    {
        Guids++;

        return _inner.NewGuid();
    }

    /// <inheritdoc/>
    public string NewCommandId()
    {
        CommandIds++;

        return _inner.NewCommandId();
    }
}
