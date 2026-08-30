using System.Text.Json;
using System.Text.Json.Serialization;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>A body the wire's own reader could not make sense of.</summary>
/// <remarks>
/// Named and greppable rather than a bare <c>JsonException</c>, because the caller has to tell it
/// from the two failures beside it: a transport that could not answer and a server that refused. A
/// body that arrived and could not be read is a defect on one side of the wire, not a state to
/// retry into.
/// </remarks>
public sealed class WireParseException : Exception
{
    /// <summary>Builds the failure.</summary>
    /// <param name="message">What could not be read.</param>
    /// <param name="inner">The reader's own failure, when there was one.</param>
    public WireParseException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>The one JSON rendering of a response envelope.</summary>
/// <remarks>
/// <para>
/// One options instance, one entry point: idempotency replays a stored outcome byte-identically
/// (14 §16.3), and the stored bytes are the ones this method produced — a second serialisation of
/// "the same" response is the drift that guarantee exists to rule out. camelCase to match 14 §2.3's
/// examples, enums by name (the rejection <c>reason</c> is its enum name), nulls omitted so the
/// accepted and rejection halves of <see cref="CommandResponse"/> do not shadow each other.
/// </para>
/// <para>
/// Deliberately not exposed as options for callers to serialise other things with: a response body
/// is the only thing this class renders. A future wire body that must speak the same dialect —
/// M5-07's <c>GET /run/{id}/state</c> answer is the known one — becomes a second <c>Render</c>
/// overload HERE, never a second converter roster somewhere else.
/// </para>
/// </remarks>
public static class WireJson
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new GearInstanceIdJsonConverter(),
            new PlayerIdJsonConverter(),
            new RunIdJsonConverter(),
            new DomainEventWireConverter(),
            new VerbatimJsonConverter(),
        },
    };

    /// <summary>A second options instance for the event converter's inner pass — everything above except the converter itself, which would otherwise recurse.</summary>
    private static readonly JsonSerializerOptions EventMemberOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new GearInstanceIdJsonConverter(),
            new PlayerIdJsonConverter(),
            new RunIdJsonConverter(),
        },
    };

    /// <summary>Renders one response envelope to the body its exchange stores and replays.</summary>
    /// <param name="response">The response.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is null.</exception>
    public static string Render(CommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return JsonSerializer.Serialize(response, ResponseOptions);
    }

    /// <summary>Renders the run-state answer — the same dialect, from the same options instance.</summary>
    /// <param name="response">The state answer.</param>
    /// <remarks>
    /// The overload the type remarks above commission. Its embedded outcome envelopes are already
    /// JSON and ride out untouched — <see cref="VerbatimJsonConverter"/> is what makes that true, and
    /// without it they are re-encoded rather than copied — so the bytes a client recovers by
    /// reconnecting are the bytes the first processing stored.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is null.</exception>
    public static string Render(RunStateResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return JsonSerializer.Serialize(response, ResponseOptions);
    }

    /// <summary>Renders one domain event in the wire's event dialect — the economy log's row payload speaks the same dialect as the response envelope.</summary>
    /// <param name="event">The event.</param>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> is null.</exception>
    public static string RenderEvent(DomainEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return JsonSerializer.Serialize(@event, ResponseOptions);
    }

    // ------------------------------------------------------------------------------- the parse half
    //
    // The reading side of the same dialect, here for the reason the type remarks give for the second
    // Render overload: one options instance, one place the dialect is spelled. It could not live in
    // the HTTP adapter in any case — the PlayerId and RunId converters are internal to this assembly.

    /// <summary>Reads one command response body — the answer to either command endpoint.</summary>
    /// <param name="body">The response body, verbatim.</param>
    /// <returns>The outcome as a client reads it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="WireParseException">The body is not a command response.</exception>
    /// <remarks>
    /// 🔒 An unknown <c>reason</c> is NOT a parse failure. <c>RejectionReason</c>'s own contract is
    /// that a client seeing a value it does not know treats it as a generic rejection and resyncs,
    /// so it arrives as a refusal with no reason rather than as an exception — a client that
    /// crashed on a reason the server added would be a client the server could never extend.
    /// </remarks>
    public static WireCommandResult ParseCommandResponse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var document = Parse(body, "a command response");

        return ReadCommandResponse(document.RootElement);
    }

    /// <summary>Reads one run-state body — the answer to <c>GET /run/{runId}/state</c>.</summary>
    /// <param name="body">The response body, verbatim.</param>
    /// <returns>Where the run stands, and what was missed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="WireParseException">The body is not a run-state answer.</exception>
    public static WireRunState ParseRunState(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var document = Parse(body, "a run state");
        var root = document.RootElement;

        var missed = new List<WireCommandResult>();

        if (root.TryGetProperty("missedOutcomes", out var outcomes))
        {
            if (outcomes.ValueKind != JsonValueKind.Array)
            {
                throw new WireParseException("'missedOutcomes' is an array of response envelopes.");
            }

            // The same reader, because these ARE command responses: the bytes the first processing
            // stored, embedded verbatim.
            missed.AddRange(outcomes.EnumerateArray().Select(ReadCommandResponse));
        }

        return new WireRunState(
            new RunId(RequiredText(root, "runId")),
            OptionalInteger(root, "sequence"),
            RequiredInteger(root, "sinceSequence"),
            RequiredProjection<PlayerWireProjection>(root, "profile"),
            RequiredProjection<RunWireProjection>(root, "run"),
            RequiredText(root, "stateHash"),
            missed,
            // Absent means false: the marker's presence is the whole signal, and the renderer omits
            // it rather than spelling it false.
            root.TryGetProperty("resyncFull", out var resync) && resync.ValueKind == JsonValueKind.True);
    }

    /// <summary>Reads one session body — the answer to <c>POST /auth/session</c> and <c>POST /auth/refresh</c>.</summary>
    /// <param name="body">The response body, verbatim.</param>
    /// <returns>The open token family.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="WireParseException">The body is not a session.</exception>
    /// <remarks>
    /// ⚠️ The auth bodies are the one place the camelCase policy above does NOT apply: the handler
    /// serialises anonymous objects at default options, so these member names are literal spellings
    /// rather than a policy's output. They are read exactly as written for that reason.
    /// </remarks>
    public static WireSession ParseSession(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var document = Parse(body, "a session");
        var root = document.RootElement;

        return new WireSession(
            new PlayerId(RequiredText(root, "playerId")),
            RequiredText(root, "accessToken"),
            RequiredInteger(root, "accessExpiresInSeconds"),
            RequiredInteger(root, "renewAfterSeconds"),
            RequiredText(root, "refreshToken"),
            RequiredInteger(root, "refreshExpiresInSeconds"));
    }

    /// <summary>Reads one device-registration body — the answer to <c>POST /auth/device</c>.</summary>
    /// <param name="body">The response body, verbatim.</param>
    /// <returns>The minted account and its credential.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="WireParseException">The body is not a device registration.</exception>
    /// <remarks>The literal-spelling note on <see cref="ParseSession"/> applies here too.</remarks>
    public static WireDeviceRegistration ParseDeviceRegistration(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var document = Parse(body, "a device registration");
        var root = document.RootElement;

        return new WireDeviceRegistration(
            RequiredText(root, "deviceId"),
            RequiredText(root, "deviceSecret"),
            new PlayerId(RequiredText(root, "playerId")),
            RequiredText(root, "displayName"));
    }

    private static WireCommandResult ReadCommandResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new WireParseException("a command response is a JSON object.");
        }

        var rejected = root.TryGetProperty("rejected", out var flag) && flag.ValueKind == JsonValueKind.True;
        var sequence = RequiredInteger(root, "sequence");
        var stateHash = OptionalText(root, "stateHash");

        if (rejected)
        {
            return new WireCommandResult(
                sequence, Accepted: false, ReadReason(root), RunId: null, Profile: null, Run: null,
                RngStreamStates: null, BattleSeed: null, stateHash);
        }

        // Not a rejection, so it has to be the accepted shape in full. An envelope carrying neither
        // half is a body this reader could not make sense of, and defaulting it would hand the
        // mirror an empty state that reads exactly like a real one.
        var outcome = RequiredObject(root, "outcome");

        return new WireCommandResult(
            sequence,
            Accepted: true,
            Rejection: null,
            OptionalText(outcome, "runId") is { } run ? new RunId(run) : null,
            RequiredProjection<PlayerWireProjection>(root, "profile"),
            outcome.TryGetProperty("run", out var runProjection) && runProjection.ValueKind == JsonValueKind.Object
                ? Deserialise<RunWireProjection>(runProjection, "outcome.run")
                : null,
            outcome.TryGetProperty("rngStreamStates", out var streams) && streams.ValueKind == JsonValueKind.Object
                ? Deserialise<Dictionary<string, ulong>>(streams, "outcome.rngStreamStates")
                : null,
            OptionalText(outcome, "battleSeed"),
            stateHash ?? throw new WireParseException(
                "an accepted response carries the hash of the state it produced; without it the "
                + "mirror cannot check itself against the server at all."));
    }

    private static RejectionReason? ReadReason(JsonElement root)
    {
        if (OptionalText(root, "reason") is not { } name)
        {
            return null;
        }

        // Matched against the NAMES, never Enum.TryParse: that also accepts the numeric spelling,
        // so a server sending "8" would silently become RATE_LIMITED here.
        return Array.IndexOf(Enum.GetNames<RejectionReason>(), name) >= 0
            ? Enum.Parse<RejectionReason>(name)
            : null;
    }

    private static JsonDocument Parse(string body, string what)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new WireParseException($"the body is not JSON, so it cannot be {what}.", ex);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new WireParseException($"{what} is a JSON object.");
        }

        return document;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.Object
            ? member
            : throw new WireParseException($"'{name}' is missing or is not an object.");

    private static T RequiredProjection<T>(JsonElement parent, string name) =>
        Deserialise<T>(RequiredObject(parent, name), name);

    private static T Deserialise<T>(JsonElement element, string name)
    {
        try
        {
            return element.Deserialize<T>(ResponseOptions)
                   ?? throw new WireParseException($"'{name}' read as nothing at all.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            throw new WireParseException($"'{name}' is not the shape this dialect writes.", ex);
        }
    }

    private static string RequiredText(JsonElement parent, string name) =>
        OptionalText(parent, name)
        ?? throw new WireParseException($"'{name}' is missing, or is not a non-empty string.");

    private static string? OptionalText(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String
            ? member.GetString() is { Length: > 0 } text ? text : null
            : null;

    private static long RequiredInteger(JsonElement parent, string name) =>
        OptionalInteger(parent, name)
        ?? throw new WireParseException($"'{name}' is missing, or is not a 64-bit integer.");

    private static long? OptionalInteger(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var member) &&
        member.ValueKind == JsonValueKind.Number &&
        member.TryGetInt64(out var value)
            ? value
            : null;

    /// <summary>
    /// JSON that is already JSON: copied through byte for byte instead of being read apart and
    /// written again.
    /// </summary>
    /// <remarks>
    /// Serialising a <see cref="JsonElement"/> the ordinary way re-encodes every string it holds
    /// through the writer's escaper, and the escaper is not the writer that produced them: a
    /// timestamp whose UTC offset the date writer emitted as a literal plus, bypassing escaping,
    /// comes back out with that plus rewritten as its six-character unicode escape. Same value,
    /// different bytes — and the one place this type embeds an element is the stored envelopes a
    /// reconnecting client is handed, which are promised to BE the stored bytes. Writing the raw
    /// text makes them so.
    /// </remarks>
    private sealed class VerbatimJsonConverter : JsonConverter<JsonElement>
    {
        /// <inheritdoc/>
        public override JsonElement Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonElement.ParseValue(ref reader);

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.GetRawText());
    }

    /// <summary>
    /// A domain event on the wire: <c>{"type": "&lt;event record name&gt;", …members}</c> — the
    /// runtime type flattened beside a discriminator, so the client's replay can dispatch on it.
    /// </summary>
    /// <remarks>
    /// Write-only: events flow server-to-client and nothing parses one back into the hierarchy
    /// here. Serialising the RUNTIME type matters — the declared type is the abstract base, which
    /// has one member and would flatten every event to its sequence number.
    /// </remarks>
    private sealed class DomainEventWireConverter : JsonConverter<DomainEvent>
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert) =>
            typeof(DomainEvent).IsAssignableFrom(typeToConvert);

        /// <inheritdoc/>
        public override DomainEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(
                "Domain events are written to the wire, never read back from it: the server is " +
                "their only producer (30 §7), and a client-supplied event would be an unvalidated " +
                "door into the four consumers of the list.");

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, DomainEvent value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.GetType().Name);

            using var members = JsonSerializer.SerializeToDocument(value, value.GetType(), EventMemberOptions);
            foreach (var member in members.RootElement.EnumerateObject())
            {
                member.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
