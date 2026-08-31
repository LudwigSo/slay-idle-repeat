using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>One command a player issued, waiting for the server to answer it.</summary>
/// <param name="Id">The idempotency key, minted once and never reminted.</param>
/// <param name="Sequence">The scope's counter value this command took.</param>
/// <param name="Command">The command itself.</param>
/// <param name="Run">The run it is addressed to, or null for the player scope.</param>
public sealed record PendingCommand(CommandId Id, long Sequence, GameCommand Command, RunId? Run);

/// <summary>
/// The commands a player has issued that the server has not answered yet, in the order they were
/// issued.
/// </summary>
/// <remarks>
/// <para>
/// It exists so input is accepted while the connection is down. A tap that cannot reach the server
/// is not refused and not silently dropped; it is queued here and sent when the connection returns.
/// </para>
/// <para>
/// 🔒 <b>A retry resends the same <c>commandId</c> and the same sequence.</b> That is the whole of
/// the idempotency protocol: the server matches a repeat against its record of the first attempt and
/// replays the outcome instead of applying the command twice. A queue that reminted either value on
/// a retry would turn every dropped answer into a duplicated command, which is exactly what the id
/// exists to prevent — so the values are assigned once, at enqueue, and are read-only afterwards.
/// </para>
/// <para>
/// 🔒 <b>Two counters, because the server keeps two.</b> The run endpoint and the player endpoint
/// are separate scopes with separate sequence counters, so one shared counter here would send a
/// player command numbered from the run's traffic. Each scope is counted from the first command this
/// queue issues into it.
/// </para>
/// <para>
/// ⚠️ It does NOT learn where the server's own counters stand. A resync reads a run's last consumed
/// sequence, and nothing here consumes that yet — the wiring pass that owns the composition seam is
/// where the two meet, and until then this queue's counters are its own.
/// </para>
/// </remarks>
public sealed class CommandQueue
{
    /// <summary>The key the player scope is counted under — no run, and no run id can spell it.</summary>
    private const string PlayerScopeKey = "";

    private readonly IIdGeneratorPort _ids;
    private readonly List<PendingCommand> _pending = [];
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);

    /// <summary>Builds the queue over the one sanctioned source of a fresh command id.</summary>
    /// <param name="ids">Where a command id comes from. Never an ambient generator.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is null.</exception>
    public CommandQueue(IIdGeneratorPort ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        _ids = ids;
    }

    /// <summary>How many commands are waiting.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Everything waiting, oldest first.</summary>
    public IReadOnlyList<PendingCommand> Pending => _pending;

    /// <summary>The oldest waiting command, or null when nothing is waiting.</summary>
    public PendingCommand? Head => _pending.Count == 0 ? null : _pending[0];

    /// <summary>Accepts a command, mints its id and takes the next sequence in its scope.</summary>
    /// <param name="command">What the player asked for.</param>
    /// <param name="run">The run it acts on, or null for the player scope.</param>
    /// <returns>The queued entry, carrying the two values every later attempt must reuse.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public PendingCommand Enqueue(GameCommand command, RunId? run)
    {
        ArgumentNullException.ThrowIfNull(command);

        var scope = run?.Value ?? PlayerScopeKey;
        var sequence = _sequences.TryGetValue(scope, out var last) ? last + 1 : 1;

        _sequences[scope] = sequence;

        var queued = new PendingCommand(new CommandId(_ids.NewCommandId()), sequence, command, run);

        _pending.Add(queued);

        return queued;
    }

    /// <summary>Drops the command the server has answered.</summary>
    /// <param name="id">The id the answer was for.</param>
    /// <exception cref="InvalidOperationException">
    /// Nothing is waiting, or <paramref name="id"/> is not the oldest waiting command.
    /// </exception>
    /// <remarks>
    /// 🔒 Refused loudly rather than searched for. The queue's order is the order the player issued
    /// the commands in and the order the server must apply them in; acknowledging out of the middle
    /// would leave an older command behind a newer one, and the two would then reach the server
    /// reversed. A caller that has an answer to a command that is not the head has a bug upstream,
    /// and a silent reorder here would hide it behind a wrong game state.
    /// </remarks>
    public void Acknowledge(CommandId id)
    {
        if (_pending.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nothing is queued, so '{id}' cannot be acknowledged. An acknowledgement with no " +
                "pending command means an answer arrived twice, or a command was dropped without " +
                "being sent.");
        }

        if (!_pending[0].Id.Equals(id))
        {
            throw new InvalidOperationException(
                $"'{id}' is not the oldest queued command — '{_pending[0].Id}' is. Commands are sent " +
                "and answered in the order the player issued them, so acknowledging out of order " +
                "would leave an older command behind a newer one and apply the two reversed.");
        }

        _pending.RemoveAt(0);
    }
}
