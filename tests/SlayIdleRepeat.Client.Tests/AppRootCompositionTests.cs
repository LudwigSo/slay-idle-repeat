using Shouldly;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Net;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The root's own factories: what a composed build hands the application root to run each frame.
/// </summary>
/// <remarks>
/// 🔴 M7-02 shipped the connection overlay unexercised — a presenter that was composed in no build
/// and a ladder nothing polled. These cases are the composition half of closing that: they say the
/// server arm produces a driver and the local arm produces none.
/// </remarks>
public sealed class AppRootCompositionTests : IDisposable
{
    private const string SourceLocale = "en";

    private readonly string _cacheRoot = RepoPaths.ScratchCacheRoot();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    /// <summary>
    /// 🔒 Both arms are driven, because one cannot tell a decision from a constant — a factory that
    /// answered null always would satisfy the local arm and a factory that answered non-null always
    /// would satisfy the server one.
    /// </summary>
    [Fact]
    public void CreateConnectionPump_answers_a_pump_on_the_server_arm_and_none_on_the_local_one()
    {
        AppRootComposition.CreateConnectionPump(
            ComposeOn(ClientArm.ServerSessionWithLocalPresenterSurface, StubWireSeams.Unreached()))
                          .ShouldNotBeNull(
                              "the ladder is composed and nothing advances it, which is exactly the " +
                              "state M7-02 shipped: an overlay that can be instantiated, a poll " +
                              "nobody calls, and therefore a connection that never moves off " +
                              "Connected however dead the server is.");

        AppRootComposition.CreateConnectionPump(ComposeOn(ClientArm.InProcessLocalHost, wire: null))
                          .ShouldBeNull(
                              "an in-process host has no connection to advance, so a pump here would " +
                              "run every frame of every shipped build over a ladder, a session and a " +
                              "mirror that were never built.");
    }

    /// <summary>
    /// ⚠️ <b>Nothing in this tier can prove the root scene actually drives the pump, or that the
    /// overlay is instantiated.</b>
    /// </summary>
    /// <remarks>
    /// Both reach GodotSharp, and a GodotSharp call from the unit tier kills the test host. What
    /// proves them instead is a headless run against an address with nothing listening, watching
    /// stdout for <c>SIR_CONNECTION_OVERLAY state=Connected</c>, then <c>state=Waiting</c> and
    /// <c>state=Reconnecting</c> once the pill threshold elapses, alongside a
    /// <c>SIR_BOOT_COLDSTART … stage=Ready</c> that says the boot finished anyway. This case exists
    /// to say so where somebody looking for the missing assertion will find it — the same shape
    /// <c>The_wiring_of_the_content_source_is_proved_by_an_export_and_not_here</c> already has.
    /// </remarks>
    [Fact]
    public void The_pump_being_driven_each_frame_is_proved_by_a_headless_run_and_not_here()
    {
        typeof(ConnectionPump)
            .GetMethod(nameof(ConnectionPump.Advance))
            .ShouldNotBeNull(
                "the driver has to exist and be public for the root scene's per-frame callback to " +
                "call it; whether the callback calls it is proved by the marker lines a headless " +
                "run prints, not by this tier.");
    }

    private ComposedClient ComposeOn(ClientArm arm, ClientWireSeams? wire) =>
        ClientComposition.Compose(
            cacheDirectoryPath: _cacheRoot,
            contentSource: new LocalFileContentSource(RepoPaths.ContentDataRoot),
            entitlements: LocalHostAmbience.NoSubscriptionResolved(),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved(),
            localeTag: SourceLocale,
            arm: arm,
            wire: wire);
}
