using System.Globalization;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Feats;

/// <summary>The closed table binding a domain event to the lifetime counters it advances.</summary>
/// <remarks>
/// <para>
/// The counters are advanced from the event list <c>GameRules.Apply</c> already returns rather than
/// from a hook in each rule that could contribute, so a new achievement becomes a query over a
/// counter instead of a call site in every handler that might feed it.
/// </para>
/// <para>
/// 🔒 <b>The table covers only the events that exist today.</b> Anything measuring something no
/// event reports yet would be a projection over a payload nobody has designed; those rows land with
/// their events. What is here is deliberately generous in the other direction — a counter nobody
/// ends up reading is dead weight, while a counter that was never written is history no achievement
/// can ever claim, and nothing may be rebuilt after the fact.
/// </para>
/// <para>
/// 🔒 <b>The ids are full literals, never composed and never derived from <c>ToString()</c>.</b> A
/// derived id would silently rename every persisted count the day someone renamed an enum member,
/// and a lifetime counter that changes key has lost the thing it exists to keep.
/// </para>
/// <para>
/// ⚠️ <b>This id set is not a published vocabulary.</b> What each achievement measures is still an
/// open decision; these ids are <c>internal</c> and cover exactly what today's two events support.
/// </para>
/// </remarks>
internal static class FeatCounterProjection
{
    /// <summary>The shared empty result, for the common case of a command whose events advance nothing.</summary>
    private static readonly IReadOnlyList<FeatCounterIncrement> Nothing =
        Array.Empty<FeatCounterIncrement>();

    internal const string DiceRolledCounter = "dice_rolled";

    /// <summary>Every counter advance the events in one <c>Apply</c> call imply, in the order they happened.</summary>
    /// <param name="events">One command's event list.</param>
    /// <returns>The advances, or an empty list — allocation-free — when the events imply none.</returns>
    /// <remarks>
    /// A <see cref="DomainEvent"/> the switch has no arm for advances nothing. That is the correct
    /// default for an event whose measures have not been decided, and the wrong one for an event
    /// that should have counted — so a new event type is a decision taken here, not an omission.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An event carries a value no counter can be named for.</exception>
    internal static IReadOnlyList<FeatCounterIncrement> Project(IReadOnlyList<DomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        List<FeatCounterIncrement>? advances = null;

        foreach (var produced in events)
        {
            switch (produced)
            {
                case DiceRolled roll:
                    advances ??= new List<FeatCounterIncrement>();
                    advances.Add(new FeatCounterIncrement(DiceRolledCounter, 1L));
                    advances.Add(new FeatCounterIncrement(DiceRolledCounterFor(roll.Face.Kind), 1L));

                    if (roll.Face.Kind == DieFaceKind.Pip)
                    {
                        advances.Add(new FeatCounterIncrement(PipsRolledCounterFor(roll.Face.Value), 1L));
                    }

                    break;

                // Delta 0 is a legal CurrencyChanged — a clamp with nothing left to give — but it is
                // not a movement, so it registers nothing.
                case CurrencyChanged movement when movement.Delta != 0L:
                    advances ??= new List<FeatCounterIncrement>();
                    advances.Add(new FeatCounterIncrement(
                        CurrencyCounterFor(movement.Id, earned: movement.Delta > 0L),
                        Magnitude(movement.Delta)));
                    break;
            }
        }

        return advances ?? Nothing;
    }

    /// <summary>The counter that counts rolls of one face kind.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="kind"/> is not a defined face kind.</exception>
    internal static string DiceRolledCounterFor(DieFaceKind kind) => kind switch
    {
        DieFaceKind.Pip => "dice_rolled_pip",
        DieFaceKind.Star => "dice_rolled_star",
        DieFaceKind.Surge => "dice_rolled_surge",
        DieFaceKind.Fortune => "dice_rolled_fortune",
        DieFaceKind.Void => "dice_rolled_void",
        DieFaceKind.Chain => "dice_rolled_chain",
        _ => throw new InvalidOperationException(
            "04 §1 fixes DieFaceKind at six named members; " + Text((int)kind) + " is not one of " +
            "them, so no lifetime counter can be named for this roll. A zero reads as an UNSET " +
            "DieFace — a handler that built a DiceRolled around a face it never resolved, which is " +
            "an animation frame and an economy row with nothing in them either."),
    };

    /// <summary>The counter that counts rolls showing a particular number of pips.</summary>
    /// <remarks>
    /// A second axis over the same event, and it is not redundant with
    /// <see cref="DiceRolledCounterFor"/>: how often a specific number came up is a question a face
    /// kind cannot answer, and it is answerable from today's event. Counting it later is impossible.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><paramref name="pips"/> is outside the die's range.</exception>
    internal static string PipsRolledCounterFor(int pips) => pips switch
    {
        1 => "dice_rolled_pips_1",
        2 => "dice_rolled_pips_2",
        3 => "dice_rolled_pips_3",
        4 => "dice_rolled_pips_4",
        5 => "dice_rolled_pips_5",
        6 => "dice_rolled_pips_6",
        _ => throw new InvalidOperationException(
            "04 §1's die shows 1..6 pips; " + Text(pips) + " is outside that range, so no lifetime " +
            "counter can be named for this roll. A zero reads as a Pip face built past DieFace.Pip's " +
            "own validation."),
    };

    /// <summary>The counter that accumulates one currency's lifetime income, or its lifetime spend.</summary>
    /// <param name="currency">The currency that moved.</param>
    /// <param name="earned"><see langword="true"/> for income, <see langword="false"/> for a spend.</param>
    /// <remarks>Two counters rather than one signed total: a net figure answers neither question.</remarks>
    /// <exception cref="InvalidOperationException"><paramref name="currency"/> is not a defined currency.</exception>
    internal static string CurrencyCounterFor(CurrencyId currency, bool earned) => currency switch
    {
        CurrencyId.GOLD => earned ? "currency_earned_gold" : "currency_spent_gold",
        CurrencyId.CROWNS => earned ? "currency_earned_crowns" : "currency_spent_crowns",
        CurrencyId.SOUL_SHARDS => earned ? "currency_earned_soul_shards" : "currency_spent_soul_shards",
        CurrencyId.ENERGY => earned ? "currency_earned_energy" : "currency_spent_energy",
        CurrencyId.ENHANCE_STONES => earned ? "currency_earned_enhance_stones" : "currency_spent_enhance_stones",
        CurrencyId.MERGE_DUST => earned ? "currency_earned_merge_dust" : "currency_spent_merge_dust",
        CurrencyId.BEAST_FEED => earned ? "currency_earned_beast_feed" : "currency_spent_beast_feed",
        CurrencyId.HONOR => earned ? "currency_earned_honor" : "currency_spent_honor",
        _ => throw new InvalidOperationException(
            "10 §1 fixes eight currencies and " + Text((int)currency) + " is not one of them, so no " +
            "lifetime counter can be named for this movement. An undefined CurrencyId is an " +
            "uninitialised field, not a balance."),
    };

    /// <summary>How much a movement moved, as an amount rather than a direction.</summary>
    private static long Magnitude(long delta) =>
        delta == long.MinValue

            // No positive counterpart, so it is refused with a reason rather than escaping Apply as
            // a bare OverflowException out of Math.Abs.
            ? throw new InvalidOperationException(
                "A currency movement of " + Text(delta) + " has no positive magnitude to count. A " +
                "spend that large is a rule that negated an unchecked sum, not a player emptying a " +
                "wallet.")
            : Math.Abs(delta);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
