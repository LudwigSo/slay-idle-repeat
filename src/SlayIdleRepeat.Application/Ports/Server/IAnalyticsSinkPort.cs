using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>
/// Recording that a product fact happened, attributed to one player, for the analytics backend.
/// </summary>
/// <remarks>
/// <para>
/// Fire-and-forget and buffered: <see cref="Track"/> returns before anything reaches a backend,
/// and a call that could not be delivered is dropped, never surfaced. Analytics is a side channel
/// over commands that have already committed — a delivery failure it reported would turn a command
/// that provably happened into an error.
/// </para>
/// <para>
/// 🔒 <b>No method surfaces a vendor failure as an exception.</b> A backend that is down, slow or
/// refusing is the adapter's problem at its own edge; the only throws here are for bad arguments.
/// </para>
/// <para>
/// 🔒 The event vocabulary is closed and owned by the application layer: implementations transport
/// <see cref="AnalyticsEvent"/> rows as given and invent none of their own.
/// </para>
/// </remarks>
public interface IAnalyticsSinkPort
{
    /// <summary>Records one event against the player it belongs to.</summary>
    /// <param name="player">Whose behaviour the event describes.</param>
    /// <param name="analyticsEvent">What happened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="analyticsEvent"/> is null.</exception>
    void Track(PlayerId player, AnalyticsEvent analyticsEvent);
}

/// <summary>One analytics fact: a name from the emitted vocabulary and its properties.</summary>
/// <param name="Name">
/// The event name. Lower snake case — it must match <c>^[a-z][a-z0-9_]*$</c>; anything else is an
/// <see cref="ArgumentException"/> at construction, so no casing or spacing variant of one fact can
/// ever reach a backend as a second fact.
/// </param>
/// <param name="Properties">
/// The event's properties. Never null (empty is fine), every key non-blank, and every value already
/// rendered invariantly by the caller — this type carries text, it formats nothing. Copied on
/// construction, so an event is safe to hand to a background sender whatever the caller does next.
/// </param>
public sealed record AnalyticsEvent(string Name, IReadOnlyDictionary<string, string> Properties)
{
    /// <inheritdoc cref="AnalyticsEvent"/>
    public string Name { get; } = ValidName(Name);

    /// <inheritdoc cref="AnalyticsEvent"/>
    public IReadOnlyDictionary<string, string> Properties { get; } = ValidProperties(Properties);

    private static string ValidName(string name)
    {
        if (name is null || !IsLowerSnake(name))
        {
            throw new ArgumentException(
                $"'{name}' is not an analytics event name. The name must match ^[a-z][a-z0-9_]*$, "
                + "or one fact reaches the backend as several casing/spacing variants.",
                nameof(Name));
        }

        return name;
    }

    private static bool IsLowerSnake(string name)
    {
        if (name.Length == 0 || name[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in name)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string> ValidProperties(
        IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties, nameof(Properties));

        // Copied while validating, in one pass: an event is queued here and serialised later on a
        // flush thread, so a caller that kept its dictionary and went on writing to it would
        // otherwise be mutating a live batch — and the validation above it would already be stale.
        var owned = new Dictionary<string, string>(properties.Count, StringComparer.Ordinal);

        foreach (var (key, value) in properties)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A property key is blank. A blank key names nothing, so its value would arrive "
                    + "at the backend unaddressable.",
                    nameof(Properties));
            }

            owned[key] = value ?? throw new ArgumentException(
                $"Property '{key}' carries no value. The port is fire-and-forget and buffered, so a "
                + "null would surface at the backend on a flush thread with nothing left to trace it "
                + "back to the call that queued it.",
                nameof(Properties));
        }

        return owned;
    }
}
