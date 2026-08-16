using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Hosting;

/// <summary>The game, played entirely in this process: the composed object behind <see cref="IGameHost"/>.</summary>
/// <remarks>
/// <para>
/// It builds the store, the dispatcher and the two use cases itself — that construction is the
/// composition, and this is the only place those four are named. It names no concrete adapter: which
/// cache, clock and generator are wired is the composition root's decision, not this type's.
/// </para>
/// <para>
/// The host is the server for this process, so issuing a command's ambient values is its job: the
/// instant comes from the clock unchanged, and a per-command seed is drawn — only for the commands
/// that need one — from the id generator, the sanctioned source of fresh entropy here.
/// </para>
/// </remarks>
public sealed class InProcessGameHost : IGameHost
{
    /// <summary>Composes a host over everything it needs, every piece of it stated by the caller.</summary>
    /// <param name="cache">Where the local profile and its runs are stored.</param>
    /// <param name="clock">The instant every command is applied at.</param>
    /// <param name="ids">The source of the profile's identity and of each meta command's seed.</param>
    /// <param name="content">The loaded, validated content set every command reads.</param>
    /// <param name="entitlements">The subscription entitlement, resolved once at boot.</param>
    /// <param name="flags">The kill switches, resolved once at boot.</param>
    /// <param name="sinks">Where an accepted command's events go, in delivery order. May be empty.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// Nothing is defaulted. A composition root that cannot say which entitlement or which flags it is
    /// running under is one that has not decided, and a default here would decide for it silently.
    /// </remarks>
    public InProcessGameHost(
        ILocalCachePort cache,
        IClockPort clock,
        IIdGeneratorPort ids,
        ContentSnapshot content,
        Entitlements entitlements,
        FeatureFlags flags,
        IReadOnlyList<IDomainEventSink> sinks) => throw new NotImplementedException();

    /// <inheritdoc/>
    /// <remarks>
    /// The profile row is committed before the pointer that names it. A crash between the two leaks
    /// an unreferenced row and the next launch mints a fresh profile; the reverse leaves a pointer to
    /// a row that does not exist, and every later command fails on the load — a bricked install.
    /// </remarks>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct) => throw new NotImplementedException();

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No profile row is stored for <paramref name="player"/>, or the stored row does not load.
    /// Neither is a player asking for something they cannot have.
    /// </exception>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player, RunId? run, GameCommand command, CancellationToken ct) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct) =>
        throw new NotImplementedException();
}
