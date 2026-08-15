using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Feats;

/// <summary>The closed table binding a domain event to the lifetime counters it advances.</summary>
/// <remarks>
/// <para>
/// Feats are evaluated from lifetime counters, and the counters are advanced from the event list
/// <c>GameRules.Apply</c> already returns rather than from a hook added to each rule that could
/// contribute. That is the whole architectural point: a Feat added later becomes a query over a
/// counter, not thirty new call sites in thirty handlers — and a handler that forgets to call one
/// of those hooks is a category of bug that cannot exist here.
/// </para>
/// <para>
/// 🔒 <b>The table covers only the events that exist today, and that is a boundary rather than an
/// omission.</b> Two event types are declared: a die roll and a currency movement. Everything else
/// a Feat could measure — enemies defeated, tiles travelled, gear merged, duels won — needs an
/// event nothing emits yet, so a row for it would be a projection over a payload nobody has
/// designed. When those events land, they add rows here; nothing else moves.
/// </para>
/// <para>
/// 🔒 <b>The ids fan out from two closed enums that already exist and are written as literals, not
/// derived from <c>ToString()</c>.</b> A derived id would rename every player's persisted counter
/// the day someone renamed an enum member, and a lifetime counter that changes key has lost the
/// history it exists to keep. The switches are exhaustive and throw on an undefined member, so a
/// seventh face or a ninth currency is a build-time decision here rather than a silently uncounted
/// one.
/// </para>
/// </remarks>
internal static class FeatCounterProjection
{
    /// <summary>The shared empty result, for the common case of a command whose events advance nothing.</summary>
    private static readonly IReadOnlyList<FeatCounterIncrement> Nothing =
        Array.Empty<FeatCounterIncrement>();

    /// <summary>Every counter advance the events in one <c>Apply</c> call imply, in the order they happened.</summary>
    /// <param name="events">One command's event list, already stamped.</param>
    /// <returns>The advances, or an empty list — allocation-free — when the events imply none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An event carries a value no counter can be named for.</exception>
    internal static IReadOnlyList<FeatCounterIncrement> Project(IReadOnlyList<DomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return Nothing;
    }

    /// <summary>The counter that counts every roll, whatever it landed on.</summary>
    internal const string DiceRolledCounter = "dice_rolled";

    /// <summary>The counter that counts rolls of one face kind.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="kind"/> is not a defined face kind.</exception>
    internal static string DiceRolledCounterFor(DieFaceKind kind) =>
        throw new InvalidOperationException("not yet implemented");

    /// <summary>The counter that accumulates one currency's lifetime income, or its lifetime spend.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="currency"/> is not a defined currency.</exception>
    internal static string CurrencyCounterFor(CurrencyId currency, bool earned) =>
        throw new InvalidOperationException("not yet implemented");
}
