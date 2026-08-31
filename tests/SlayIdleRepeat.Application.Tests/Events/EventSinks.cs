using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Application.Tests.Events;

/// <summary>A sink that keeps every batch it was handed, in the order it was handed them.</summary>
internal sealed class RecordingSink : IDomainEventSink
{
    private readonly List<DispatchedEvents> _batches = [];
    private readonly List<string>? _order;

    /// <summary>Builds a sink.</summary>
    /// <param name="name">How this sink names itself in <paramref name="order"/>.</param>
    /// <param name="order">A log several sinks share, so delivery order across them is observable.</param>
    internal RecordingSink(string name = "recording", List<string>? order = null)
    {
        Name = name;
        _order = order;
    }

    /// <summary>This sink's name.</summary>
    internal string Name { get; }

    /// <summary>Every batch delivered here, in delivery order.</summary>
    internal IReadOnlyList<DispatchedEvents> Batches => _batches;

    /// <inheritdoc/>
    public Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct)
    {
        _batches.Add(batch);
        _order?.Add(Name);

        return Task.CompletedTask;
    }
}

/// <summary>A sink that throws before it returns a task — the shape a synchronous fault takes.</summary>
internal sealed class ThrowingSink : IDomainEventSink
{
    /// <summary>What it throws, so a case can look for it on the outcome rather than for "something failed".</summary>
    internal const string Message = "this sink is down";

    /// <summary>How many batches reached it.</summary>
    internal int Calls { get; private set; }

    /// <inheritdoc/>
    public Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct)
    {
        Calls++;

        throw new InvalidOperationException(Message);
    }
}

/// <summary>A sink that returns a faulted task — the other shape a failing sink takes.</summary>
/// <remarks>
/// Kept beside <see cref="ThrowingSink"/> deliberately: a dispatcher that starts every sink before
/// awaiting any of them handles one of the two and not the other, and the difference is invisible
/// with only one in the suite.
/// </remarks>
internal sealed class FaultingSink : IDomainEventSink
{
    /// <inheritdoc cref="ThrowingSink.Message"/>
    internal const string Message = "this sink faulted its task";

    /// <inheritdoc/>
    public Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct) =>
        Task.FromException(new InvalidOperationException(Message));
}
