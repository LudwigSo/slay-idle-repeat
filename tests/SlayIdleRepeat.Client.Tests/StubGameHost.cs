using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A hand-written <see cref="IGameHost"/> that answers the one call the application root makes.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked, on the same grounds every port in this repository has a
/// hand-written fake: there is no mocking library here and adding one to stand in for three
/// methods would be a dependency bought for nothing. The two members the root never touches
/// refuse loudly instead of returning a plausible blank, so a presenter that grows a call to
/// one of them fails on the spot rather than on a value nobody chose.
/// </remarks>
internal sealed class StubGameHost : IGameHost
{
    private readonly PlayerId _player;
    private readonly Exception? _failure;

    private StubGameHost(PlayerId player, Exception? failure)
    {
        _player = player;
        _failure = failure;
    }

    /// <summary>A host that opens a profile and returns the given id.</summary>
    internal static StubGameHost Opening(PlayerId player) => new(player, failure: null);

    /// <summary>A host whose profile open throws.</summary>
    internal static StubGameHost Failing(Exception failure) => new(default, failure);

    /// <inheritdoc/>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct) =>
        _failure is null ? Task.FromResult(_player) : throw _failure;

    /// <inheritdoc/>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player,
        RunId? run,
        GameCommand command,
        CancellationToken ct) =>
        throw new NotSupportedException(
            "The application root composes and opens a profile; it submits no command. A presenter " +
            "reaching this needs its own fake, not a widened one here.");

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct) =>
        throw new NotSupportedException(
            "The application root composes and opens a profile; it reads no state. A presenter " +
            "reaching this needs its own fake, not a widened one here.");
}
