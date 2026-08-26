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
/// rendered invariantly by the caller — this type carries text, it formats nothing.
/// </param>
public sealed record AnalyticsEvent(string Name, IReadOnlyDictionary<string, string> Properties);
