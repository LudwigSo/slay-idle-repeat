using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The composition root these cases run under: the shipped content, the in-memory fakes, and the two
/// ambience factories a real composition root would have to name too.
/// </summary>
/// <remarks>
/// Every argument the host requires is passed here, so a case that wants a different clock, generator
/// or sink list states only that one. No real adapter is ever wired: the file-backed cache's pairing
/// with the store is a shared contract suite's subject, not this suite's.
/// </remarks>
internal static class Hosts
{
    /// <summary>A host over one cache, defaulting everything a case is not about.</summary>
    /// <param name="cache">Where the profile and its runs are stored.</param>
    /// <param name="clock">The clock. Defaults to a fresh <see cref="AdjustableClock"/>.</param>
    /// <param name="ids">The generator. Defaults to a fresh <see cref="CountingIdGenerator"/>.</param>
    /// <param name="content">The content set. Defaults to the shipped one.</param>
    /// <param name="sinks">Where events go. Defaults to none at all.</param>
    internal static InProcessGameHost Over(
        ILocalCachePort cache,
        IClockPort? clock = null,
        IIdGeneratorPort? ids = null,
        ContentSnapshot? content = null,
        IReadOnlyList<IDomainEventSink>? sinks = null) =>
        new(
            cache,
            clock ?? new AdjustableClock(),
            ids ?? new CountingIdGenerator(),
            content ?? Worlds.Content,
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved(),
            sinks ?? []);
}
