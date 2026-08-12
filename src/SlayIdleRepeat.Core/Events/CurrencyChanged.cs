using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Events;

/// <summary>A currency movement (`30` §7).</summary>
public sealed record CurrencyChanged(int Sequence, CurrencyId Id, long Delta, string Reason)
    : DomainEvent(Sequence);
