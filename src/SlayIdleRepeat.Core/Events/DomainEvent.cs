namespace SlayIdleRepeat.Core.Events;

/// <summary>The base of the domain event hierarchy (`30` §7).</summary>
/// <param name="Sequence">The event's ordinal within one command's event list.</param>
public abstract record DomainEvent(int Sequence);
