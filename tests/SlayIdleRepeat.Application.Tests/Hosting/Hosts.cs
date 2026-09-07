using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

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
    /// <summary>
    /// Opens the profile and grants it the day's free Energy refill, so it can pay for a run.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>A run costs Energy</b> — <c>Handlers.StartRun</c> charges <c>EnergyTuning.RunCost</c> —
    /// and a freshly opened profile holds none in either bank, because every currency movement has
    /// to be attributed by a <c>CurrencyChanged</c> and a starting balance would be one no row
    /// explains. <c>BEGIN_SESSION</c>'s first-login refill is the command that grants it, and this
    /// helper sends it exactly where a real launch would: once, before the first run.
    /// </remarks>
    /// <param name="host">The host to open the profile on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The refill was refused.</exception>
    internal static async Task<PlayerId> OpenFundedProfileAsync(InProcessGameHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var funded = await host.SubmitAsync(
            player, null, new BeginSessionCommand("0.0.0-fixture", "fixture-content-hash"), Worlds.Cancel);

        return funded.Accepted
            ? player
            : throw new InvalidOperationException(
                "the fixture's BEGIN_SESSION was refused " + funded.Rejection +
                ", so the profile holds no Energy and every run it starts is refused for the price.");
    }

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
