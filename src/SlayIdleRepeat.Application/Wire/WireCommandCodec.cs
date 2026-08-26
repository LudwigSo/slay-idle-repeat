using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>What one decode attempt produced: the typed command, or the rejection it earns.</summary>
/// <remarks>Exactly one of the two members carries the answer; both faults are 200 rejections, never transport failures.</remarks>
public sealed record CommandDecode
{
    private CommandDecode(GameCommand? command, RejectionReason? rejection)
    {
        Command = command;
        Rejection = rejection;
    }

    /// <summary>The typed command, or <c>null</c> when the envelope earned a rejection.</summary>
    public GameCommand? Command { get; }

    /// <summary><c>UNKNOWN_COMMAND_TYPE</c> or <c>MALFORMED_COMMAND</c>, or <c>null</c> on success.</summary>
    public RejectionReason? Rejection { get; }

    internal static CommandDecode Decoded(GameCommand command) => new(command, rejection: null);

    internal static CommandDecode Refused(RejectionReason rejection) => new(command: null, rejection);
}

/// <summary>Turns an envelope's <c>type</c> + <c>payload</c> into the typed <c>GameCommand</c>.</summary>
/// <remarks>
/// <para>
/// The name is resolved against <c>GameRules.CommandTypesByWireName</c> — the dispatch table's own
/// index, so this codec holds no second vocabulary and a registry edit lands here without a mirror
/// edit. A name with no row — including D41's retired <c>USE_REROLL</c> and
/// <c>DICE_FORGE_CHOOSE</c>, which stay retired precisely so an old client's send cannot decode to
/// a different command — is <c>UNKNOWN_COMMAND_TYPE</c>.
/// </para>
/// <para>
/// The payload binds to the command's public constructor, which is the field-level source of truth
/// (14 §2.3). Binding is strict about unknown members — a payload member the command does not
/// declare is <c>MALFORMED_COMMAND</c>, since a silently dropped member is an intent the player
/// expressed and the server ignored. It is NOT strict about missing value-typed members, which the
/// runtime binds to their defaults; the domain refuses the illegal values those produce, which
/// keeps the schema decided in exactly one place.
/// </para>
/// </remarks>
public static class WireCommandCodec
{
    /// <summary>The payload's binding options — strict on unknown members, invariant on enums, ids as bare strings.</summary>
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new GearInstanceIdJsonConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DisallowUnmappedMembers },
        },
    };

    /// <summary>Decodes one envelope into the typed command.</summary>
    /// <param name="envelope">The parsed envelope.</param>
    /// <returns>The typed command, or the 200 rejection the envelope earned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is null.</exception>
    public static CommandDecode Decode(CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!GameRules.CommandTypesByWireName.TryGetValue(envelope.Type, out var commandType))
        {
            return CommandDecode.Refused(RejectionReason.UNKNOWN_COMMAND_TYPE);
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            return CommandDecode.Refused(RejectionReason.MALFORMED_COMMAND);
        }

        try
        {
            var command = (GameCommand?)envelope.Payload.Deserialize(commandType, PayloadOptions);

            return command is null
                ? CommandDecode.Refused(RejectionReason.MALFORMED_COMMAND)
                : CommandDecode.Decoded(command);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            // JsonException: wrong shapes and unknown members. ArgumentException: a command
            // constructor's own guard refusing what bound (a blank id, a null list). Both are the
            // client sending a payload the schema refuses, which is exactly MALFORMED_COMMAND.
            return CommandDecode.Refused(RejectionReason.MALFORMED_COMMAND);
        }
    }

    /// <summary>The registry's index read backwards, built once: wire name by command type.</summary>
    private static readonly Lazy<IReadOnlyDictionary<Type, string>> WireNamesByType = new(() =>
        GameRules.CommandTypesByWireName.ToDictionary(pair => pair.Value, pair => pair.Key));

    /// <summary>The wire type name a command travels under — the dispatch registry's own index, read backwards.</summary>
    /// <param name="command">The typed command.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The registry has no wire name for this type — a command that was never registered cannot have been decoded.</exception>
    public static string WireNameOf(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return WireNamesByType.Value.TryGetValue(command.GetType(), out var name)
            ? name
            : throw new InvalidOperationException(
                "'" + command.GetType().Name + "' has no wire name in the dispatch registry. Every " +
                "decoded command came in THROUGH that registry, so an unregistered one here was " +
                "constructed by a caller the wire never saw — it cannot be stored under a name the " +
                "decoder will resolve back.");
    }

    /// <summary>A command's payload as canonical JSON — the durable half of the record a duplicate is checked against.</summary>
    /// <param name="command">The typed command.</param>
    /// <remarks>
    /// Symmetric with <see cref="Decode"/>'s own options: decoding these bytes yields a command
    /// equal to <paramref name="command"/>, so payload identity stays the one decoded-record
    /// equality rather than gaining a byte-level second definition.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    public static string EncodePayload(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The RUNTIME type: serialising as the abstract base would write no member at all.
        return JsonSerializer.Serialize(command, command.GetType(), PayloadOptions);
    }

    private static void DisallowUnmappedMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            typeInfo.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        }
    }
}
