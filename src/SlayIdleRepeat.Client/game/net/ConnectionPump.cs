using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Drives the reconnect ladder one frame at a time: the thing that turns a composed connection into
/// a live one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>No <c>async</c> method, no continuation, no <c>async void</c>.</b> This is driven from the
/// engine's per-frame callback, where an exception escaping a void async method is unobservable and
/// fatal. <see cref="Advance"/> starts at most one task, drains the previous one's outcome on the
/// next frame, and never awaits.
/// </para>
/// <para>
/// 🔴 <c>ReconnectManager.Follow</c> is deliberately never called from here. The only run id this
/// arm holds is the local host's, which the server has never minted — pointing the ladder at it
/// would turn every resync into a refusal. It acquires a caller when the presenters submit through
/// the wire.
/// </para>
/// </remarks>
public sealed class ConnectionPump
{
    /// <summary>Wires the pump over the ladder, the session, the mirror and its cache.</summary>
    /// <param name="connection">The ladder being climbed.</param>
    /// <param name="session">How a session is opened and reopened.</param>
    /// <param name="mirror">The local copy the pump persists.</param>
    /// <param name="cache">Where the mirror is stored between runs.</param>
    /// <param name="clock">The only sanctioned source of "now".</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ConnectionPump(
        ReconnectManager connection,
        SessionOpener session,
        StateMirror mirror,
        MirrorCache cache,
        IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(clock);
    }

    /// <summary>Whether <see cref="Stop"/> has been called.</summary>
    public bool Stopped => throw new NotImplementedException();

    /// <summary>The last fault a driven task produced, or null. Observed, never swallowed.</summary>
    public Exception? LastFault => throw new NotImplementedException();

    /// <summary>How many driven tasks have faulted.</summary>
    public int FaultCount => throw new NotImplementedException();

    /// <summary>One frame. Never awaits, never throws.</summary>
    /// <param name="ct">The application's shutdown token.</param>
    public void Advance(CancellationToken ct) => throw new NotImplementedException();

    /// <summary>Stops the pump for good.</summary>
    public void Stop() => throw new NotImplementedException();
}
