using System.Text.Json;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>What one parse attempt produced: the envelope, or the reason the body is not one.</summary>
/// <remarks>
/// Exactly one of the two members is non-null. A fault here is 14 §16.2's HTTP 400 — "body is not
/// a parseable envelope at all" — and never a rejection envelope, which is reserved for bodies the
/// server could read.
/// </remarks>
public sealed record EnvelopeParse
{
    private EnvelopeParse(CommandEnvelope? envelope, string? fault)
    {
        Envelope = envelope;
        Fault = fault;
    }

    /// <summary>The parsed envelope, or <c>null</c> when the body is not one.</summary>
    public CommandEnvelope? Envelope { get; }

    /// <summary>Why the body is not an envelope, or <c>null</c> when it is. Diagnostic only — a 400 carries no body.</summary>
    public string? Fault { get; }

    internal static EnvelopeParse Parsed(CommandEnvelope envelope) => new(envelope, fault: null);

    internal static EnvelopeParse NotAnEnvelope(string fault) => new(envelope: null, fault);
}

/// <summary>Turns a request body into a <see cref="CommandEnvelope"/>, or says why it is not one.</summary>
/// <remarks>
/// <para>
/// The 400 boundary, drawn once: JSON parse plus required-field presence — <c>protocolVersion</c>,
/// <c>commandId</c>, <c>sequence</c> and <c>type</c> must be present and of their envelope kind.
/// Everything past that boundary — an unsupported version, an unknown type, a payload the command's
/// schema refuses — is an in-protocol conversation and answers as a 200 rejection downstream.
/// </para>
/// <para>
/// Unknown top-level members are ignored, not refused: the envelope may grow a field in a later
/// protocol version, and a server that 400s on the unknown would break the very skew window
/// 14 §16.1 exists to keep open. The payload is the opposite case — its schema is the command's,
/// versioned by the registry — and <see cref="WireCommandCodec"/> is strict about it there.
/// </para>
/// </remarks>
public static class EnvelopeParser
{
    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement;

    /// <summary>Parses one request body.</summary>
    /// <param name="body">The request body text.</param>
    /// <returns>The envelope, or the fault that makes the body a 400.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null — a missing body is the host adapter's fault, not a client's.</exception>
    public static EnvelopeParse Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            return EnvelopeParse.NotAnEnvelope("The body is not JSON: " + exception.Message);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return EnvelopeParse.NotAnEnvelope(
                    "The body is JSON but not an object; an envelope is an object with " +
                    "protocolVersion, commandId, sequence and type.");
            }

            if (!TryReadInt32(root, "protocolVersion", out var protocolVersion, out var versionFault))
            {
                return EnvelopeParse.NotAnEnvelope(versionFault);
            }

            if (!root.TryGetProperty("commandId", out var commandIdElement) ||
                commandIdElement.ValueKind != JsonValueKind.String ||
                !CommandId.IsWellFormed(commandIdElement.GetString()))
            {
                return EnvelopeParse.NotAnEnvelope(
                    "'commandId' is missing, not a string, or not a usable idempotency key " +
                    "(non-empty, no whitespace, no control characters).");
            }

            if (!TryReadInt64(root, "sequence", out var sequence, out var sequenceFault))
            {
                return EnvelopeParse.NotAnEnvelope(sequenceFault);
            }

            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(typeElement.GetString()))
            {
                return EnvelopeParse.NotAnEnvelope("'type' is missing, not a string, or empty.");
            }

            // Cloned so the element survives the document's disposal. An absent payload reads as an
            // empty object — the registry's payload-less commands are sent both ways by reasonable
            // clients, and the two spell the same intent.
            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone()
                : EmptyPayload;

            return EnvelopeParse.Parsed(new CommandEnvelope(
                protocolVersion,
                new CommandId(commandIdElement.GetString()!),
                sequence,
                typeElement.GetString()!,
                payload));
        }
    }

    private static bool TryReadInt32(JsonElement root, string name, out int value, out string fault)
    {
        value = 0;

        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out value))
        {
            fault = $"'{name}' is missing or not an integer.";
            return false;
        }

        fault = string.Empty;
        return true;
    }

    private static bool TryReadInt64(JsonElement root, string name, out long value, out string fault)
    {
        value = 0;

        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out value))
        {
            fault = $"'{name}' is missing or not an integer.";
            return false;
        }

        fault = string.Empty;
        return true;
    }
}
