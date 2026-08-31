using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>One processed command's durable record — enough to replay it byte-identically and to detect a conflict.</summary>
/// <param name="CommandId">The idempotency key, scoped to the scope the record is stored under.</param>
/// <param name="Sequence">The sequence the command consumed in its scope.</param>
/// <param name="CommandType">The command's wire type name — one half of the payload's identity.</param>
/// <param name="PayloadJson">The command's payload as canonical JSON — the other half. Retained so a known command id sent with a different payload can be answered <c>IDEMPOTENCY_CONFLICT</c> instead of replayed.</param>
/// <param name="ResponseBody">The exact response body the first processing produced. A duplicate replays these bytes, never a recomputation.</param>
/// <param name="OpensScope">The run scope key this command's acceptance opened, or <c>null</c>. The durable audit of which run a command created — the scope itself was opened by the same commit that stored this record, so nothing reads this to repair anything.</param>
/// <remarks>
/// The payload travels as (type, JSON) rather than as a typed command because a durable store must
/// hold what it can write down; the one codec (<c>WireCommandCodec</c>) re-decodes it on read, so
/// payload equality stays the single decoded-record equality the gateway already uses.
/// </remarks>
public sealed record RecordedCommandOutcome(
    CommandId CommandId,
    long Sequence,
    string CommandType,
    string PayloadJson,
    string ResponseBody,
    string? OpensScope = null)
{
    /// <summary>The command's wire type name. Never null or blank.</summary>
    public string CommandType { get; } = RequireText(CommandType, nameof(CommandType));

    /// <summary>The command's payload as canonical JSON. Never null or blank.</summary>
    public string PayloadJson { get; } = RequireText(PayloadJson, nameof(PayloadJson));

    /// <summary>The exact first response body. Never null or blank.</summary>
    public string ResponseBody { get; } = RequireText(ResponseBody, nameof(ResponseBody));

    private static string RequireText(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "A recorded outcome with a blank " + name + " cannot replay or conflict-check anything: " +
                "the record exists precisely to hold these bytes.",
                name)
            : value;
}
