using System.Text.Json;
using System.Text.Json.Serialization;
using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Application.Wire;

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
    /// JSON and ride out untouched, so the bytes a client replays are the bytes the first processing
    /// stored.
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
