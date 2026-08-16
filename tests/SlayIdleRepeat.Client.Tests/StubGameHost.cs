using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A hand-written <see cref="IGameHost"/> that answers the one call the application root makes.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than mocked, on the same grounds every port in this repository has a
/// hand-written fake: there is no mocking library here and adding one to stand in for three
/// methods would be a dependency bought for nothing. The two members the root never touches
/// refuse loudly instead of returning a plausible blank, so a presenter that grows a call to
/// one of them fails on the spot rather than on a value nobody chose.
/// </para>
/// <para>
/// 🔒 Failure comes in two shapes and they are separate factories on purpose. A real host's
/// <c>OpenProfileAsync</c> is an <c>async</c> method, so it hands back a <b>faulted task</b>
/// and never throws before returning one; a stub that only throws synchronously models the
/// shape production cannot produce, and a presenter that inspects the task instead of
/// awaiting it would pass against it and hang against the real thing.
/// </para>
/// </remarks>
internal sealed class StubGameHost : IGameHost
{
    private readonly PlayerId _player;
    private readonly Exception? _failure;
    private readonly bool _throwsBeforeReturning;

    private StubGameHost(PlayerId player, Exception? failure, bool throwsBeforeReturning)
    {
        _player = player;
        _failure = failure;
        _throwsBeforeReturning = throwsBeforeReturning;
    }

    /// <summary>
    /// The token the last <see cref="OpenProfileAsync"/> call was handed, or null while it has
    /// not been called at all.
    /// </summary>
    internal CancellationToken? ReceivedToken { get; private set; }

    /// <summary>A host that opens a profile and returns the given id.</summary>
    internal static StubGameHost Opening(PlayerId player) =>
        new(player, failure: null, throwsBeforeReturning: false);

    /// <summary>A host whose profile open throws before it has returned a task at all.</summary>
    internal static StubGameHost ThrowingBeforeReturning(Exception failure) =>
        new(default, failure, throwsBeforeReturning: true);

    /// <summary>A host whose profile open returns a faulted task — how a real async host fails.</summary>
    internal static StubGameHost FaultingItsTask(Exception failure) =>
        new(default, failure, throwsBeforeReturning: false);

    /// <inheritdoc/>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct)
    {
        ReceivedToken = ct;

        if (_failure is null)
        {
            return Task.FromResult(_player);
        }

        return _throwsBeforeReturning ? throw _failure : Task.FromException<PlayerId>(_failure);
    }

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
