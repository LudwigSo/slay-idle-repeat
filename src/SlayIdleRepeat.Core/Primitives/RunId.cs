namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The identity of the <c>Run</c> aggregate root (`30` §4) — a child of <c>Player</c>, not a peer.
/// </summary>
/// <param name="Value">The identifier text. Never null, empty or whitespace.</param>
/// <remarks>
/// The same shape, the same guard and the same reasoning as <see cref="PlayerId"/>; read that
/// type's remarks for why it is a <c>readonly record struct</c> and what
/// <c>default(RunId)</c> does. What matters here is that the two are <b>different types</b>: a run
/// command carries a <see cref="RunId"/> in its route (`14` §16.3, <c>POST /run/{runId}/command</c>)
/// and a <see cref="PlayerId"/> in its session, and nothing may pass one where the other belongs.
/// </remarks>
public readonly record struct RunId(string Value)
{
    /// <summary>
    /// The identifier text. Never null, empty or whitespace — except on <c>default(RunId)</c>,
    /// whose backing field no constructor ever assigned.
    /// </summary>
    public string Value { get; } = IdText.Require(Value, nameof(RunId));

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    /// <remarks>
    /// The <c>??</c> is <c>default(RunId)</c>, for the reason spelled out on
    /// <see cref="PlayerId.ToString"/>.
    /// </remarks>
    public override string ToString() => Value ?? $"default({nameof(RunId)})";
}
