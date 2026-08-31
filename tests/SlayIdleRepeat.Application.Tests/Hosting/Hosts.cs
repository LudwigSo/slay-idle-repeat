using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
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
    /// <param name="entitlements">
    /// The subscription entitlement. Defaults to the absence factory a composition root has to name
    /// too — a case that passes one of its own is asking whether this argument reaches the domain at
    /// all, which is not answerable while every host in the suite is composed the same way.
    /// </param>
    /// <param name="flags">The kill switches. Defaults to the absence factory.</param>
    /// <param name="messages">
    /// The inbox store. Defaults to none, which is what the shipped client composes — a case about a
    /// command that reads the inbox passes one, and gets the same load the server's use case does.
    /// </param>
    internal static InProcessGameHost Over(
        ILocalCachePort cache,
        IClockPort? clock = null,
        IIdGeneratorPort? ids = null,
        ContentSnapshot? content = null,
        IReadOnlyList<IDomainEventSink>? sinks = null,
        Entitlements? entitlements = null,
        FeatureFlags? flags = null,
        IMessageRepository? messages = null) =>
        new(
            cache,
            clock ?? new AdjustableClock(),
            ids ?? new CountingIdGenerator(),
            content ?? Worlds.Content,
            entitlements ?? LocalHostAmbience.NoSubscriptionResolved(),
            flags ?? LocalHostAmbience.NoRemoteConfigResolved(),
            sinks ?? [],
            messages);
}
