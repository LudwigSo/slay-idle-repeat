namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>Where battle logs live for replay: an id in, bytes out.</summary>
/// <remarks>
/// <para>
/// 🔒 The port speaks a battle-log id and bytes, and NOTHING else — no storage location, no naming
/// scheme, no link, no size or content hint. Two very different backings share this exact surface,
/// and each derives everything it needs from the id's text alone. A member that surfaced either
/// backing's own vocabulary here would make the other one impossible rather than merely awkward.
/// </para>
/// <para>
/// <see cref="PutAsync"/> completing means the log is accepted for storage; a deployment may write
/// behind a queue, so acceptance and readability are allowed to be two moments. Battle-log loss
/// never blocks progress: a caller must treat a missing log as a replay that cannot be offered,
/// never as a failed command. The shared suite's settle hook is where a queued implementation makes
/// "accepted" become "readable" for the round-trip cases.
/// </para>
/// </remarks>
public interface IBattleLogStore
{
    /// <summary>Accepts one battle log for storage under its id. A second put to the same id replaces the first.</summary>
    /// <param name="id">The log's identity.</param>
    /// <param name="log">The log's bytes, exactly as produced. May be empty.</param>
    /// <param name="ct">Cancellation.</param>
    Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct);

    /// <summary>The stored log's bytes, byte-identical to what was put, or <c>null</c> when nothing is stored under this id.</summary>
    /// <param name="id">The log's identity.</param>
    /// <param name="ct">Cancellation.</param>
    Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct);
}
