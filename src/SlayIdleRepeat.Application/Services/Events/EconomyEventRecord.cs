using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Events;

/// <summary>One row of the append-only economy event log: a domain event enriched with the identity the domain deliberately does not carry.</summary>
/// <param name="Player">Whose command produced it.</param>
/// <param name="Run">The run it happened in, or <c>null</c> for a meta command's event.</param>
/// <param name="CommandId">The command that produced it.</param>
/// <param name="Sequence">The event's ordinal within its command's list — <see cref="DomainEvent.Sequence"/>, not the wire counter.</param>
/// <param name="OccurredAtUtc">The command's instant — the domain's <c>NowUtc</c>, stamped at enrichment.</param>
/// <param name="EventType">The event's wire type name.</param>
/// <param name="PayloadJson">The event in the wire's own event dialect.</param>
public sealed record EconomyEventRecord(
    PlayerId Player,
    RunId? Run,
    CommandId CommandId,
    int Sequence,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string PayloadJson);

/// <summary>Turns one accepted command's domain events into economy-log rows.</summary>
/// <remarks>
/// Domain events carry no player id and no timestamp by rule — time and identity enter the domain
/// through the context, so they re-enter the log the same way: from the caller, here, at append
/// time. Serialisation reuses the wire's one event dialect, never a second one.
/// </remarks>
public static class EconomyEventEnricher
{
    /// <summary>The rows for one command's events, in event order.</summary>
    /// <param name="player">Whose command it was.</param>
    /// <param name="run">The run it acted on, or <c>null</c> for a meta command.</param>
    /// <param name="commandId">The command.</param>
    /// <param name="occurredAtUtc">The command's instant — the same <c>NowUtc</c> the domain applied under.</param>
    /// <param name="events">The command's events, in order. Never null; may be empty.</param>
    public static IReadOnlyList<EconomyEventRecord> Enrich(
        PlayerId player,
        RunId? run,
        CommandId commandId,
        DateTimeOffset occurredAtUtc,
        IReadOnlyList<DomainEvent> events) =>
        throw new NotImplementedException("M5-05 phase 3 implements the enricher.");
}
