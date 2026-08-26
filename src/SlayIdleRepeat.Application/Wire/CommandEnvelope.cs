using System.Text.Json;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>One parsed command request — 14 §2.3's envelope, past the 400 boundary.</summary>
/// <param name="ProtocolVersion">The envelope/lifecycle version the client speaks (14 §16.1).</param>
/// <param name="CommandId">The client-generated idempotency key.</param>
/// <param name="Sequence">The per-scope command counter — per run on the run endpoint, per player on the player endpoint (14 §16.3).</param>
/// <param name="Type">The wire command name, exactly as sent. Resolved against the registry by <see cref="WireCommandCodec"/>, never here.</param>
/// <param name="Payload">The command's payload object, detached from its parse. An absent payload arrives as an empty object.</param>
/// <remarks>
/// Existence of an instance means exactly one thing: the body was a parseable envelope, so every
/// answer from here on is an HTTP 200 — an accepted outcome or a rejection envelope. It deliberately
/// carries the version and type UNVALIDATED: an unsupported version and an unknown type are both
/// in-protocol conversations (<c>PROTOCOL_VERSION_UNSUPPORTED</c>, <c>UNKNOWN_COMMAND_TYPE</c>),
/// and validating them at the parse would collapse them into the 400 that is reserved for a body
/// the server could not read at all.
/// </remarks>
public sealed record CommandEnvelope(
    int ProtocolVersion,
    CommandId CommandId,
    long Sequence,
    string Type,
    JsonElement Payload);
