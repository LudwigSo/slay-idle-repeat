using System.Globalization;
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
/// 🔒 <b>The ids are written as full literals, never composed and never derived from
/// <see cref="Enum.ToString()"/>.</b> A counter id is the key a player's whole history is stored
/// under, so a derived one would silently rename every persisted count the day someone renamed an
/// enum member — and a lifetime counter that changes key has lost the thing it exists to keep. Full
/// literals also mean every id is greppable as it appears in storage. The switches are exhaustive
/// and refuse an undefined member, so a seventh face or a ninth currency is a decision taken here
/// rather than a quietly uncounted one.
/// </para>
/// <para>
/// ⚠️ <b>This id set is not a published vocabulary.</b> What each Feat measures is an open decision
/// owned by the milestone that ships the feature; these ids are <c>internal</c>, cover exactly what
/// today's two events can support, and are deliberately not a contract anything outside
/// <c>Core</c> can name.
/// </para>
/// </remarks>
internal static class FeatCounterProjection
{
    /// <summary>The shared empty result, for the common case of a command whose events advance nothing.</summary>
    private static readonly IReadOnlyList<FeatCounterIncrement> Nothing =
        Array.Empty<FeatCounterIncrement>();

    /// <summary>The counter that counts every roll, whatever it landed on.</summary>
    internal const string DiceRolledCounter = "dice_rolled";

    /// <summary>Every counter advance the events in one <c>Apply</c> call imply, in the order they happened.</summary>
    /// <param name="events">One command's event list.</param>
    /// <returns>The advances, or an empty list — allocation-free — when the events imply none.</returns>
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
                    break;

                // A zero delta is explicitly permitted and is not a movement — a clamp that had
                // nothing left to give still publishes a row.
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
            "DieFace, which is a handler that built a DiceRolled around a face it never resolved: " +
            "that event is an animation frame and an economy row with nothing in it either, so it " +
            "is refused as a defect rather than counted as some other face."),
    };

    /// <summary>The counter that accumulates one currency's lifetime income, or its lifetime spend.</summary>
    /// <param name="currency">The currency that moved.</param>
    /// <param name="earned">
    /// <see langword="true"/> for income, <see langword="false"/> for a spend. Two counters rather
    /// than one signed total: the catalogue asks both questions separately of different currencies,
    /// and a net figure could not answer either.
    /// </param>
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
    /// <remarks>
    /// <c>long.MinValue</c> has no positive counterpart, so it is refused with the reason instead of
    /// escaping as a bare <see cref="OverflowException"/> from <c>Apply</c>.
    /// </remarks>
    private static long Magnitude(long delta) =>
        delta == long.MinValue
            ? throw new InvalidOperationException(
                "A currency movement of " + Text(delta) + " has no positive magnitude to count, so " +
                "no lifetime counter can record it. A spend that large is a rule that computed its " +
                "delta by negating an unchecked sum, not a player emptying a wallet.")
            : Math.Abs(delta);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
