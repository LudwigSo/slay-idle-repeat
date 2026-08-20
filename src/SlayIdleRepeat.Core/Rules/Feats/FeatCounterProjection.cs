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
/// 🔴 <b>The table covers TWO of the FOUR events that exist today, and the other two are a recorded
/// decision rather than a gap.</b> <c>GearGranted</c> and <c>PityCounterAdvanced</c> are emitted in
/// production and reach no arm here. What either should measure is `16` O29's to decide — the M4
/// kickoff ruling R10 keeps that decision open to M16 in as many words — and inventing an id for
/// them would freeze the vocabulary that ruling protects. Both carry a row on
/// <c>FeatCounterProjectionCoverageRuleTests.ProjectionExemptions</c> with M16-03 as the owner, and
/// that rule fails the build the day a third event arrives with no decision attached, and again the
/// day one of these two is projected and its row is left standing.
/// </para>
/// <para>
/// ⚠️ <b>What that costs, said plainly.</b> Every <c>GearGranted</c> and every
/// <c>PityCounterAdvanced</c> emitted between now and M16 is history no achievement can ever claim,
/// because §12.7 forbids rebuilding a counter after the fact. Where a counter <em>is</em> written,
/// this table is deliberately generous in the other direction — a counter nobody ends up reading is
/// dead weight, and that is the cheaper of the two mistakes.
/// </para>
/// <para>
/// 🔒 <b>The ids are full literals, never composed and never derived from <c>ToString()</c>.</b> A
/// derived id would silently rename every persisted count the day someone renamed an enum member,
/// and a lifetime counter that changes key has lost the thing it exists to keep.
/// </para>
/// <para>
/// ⚠️ <b>This id set is not a published vocabulary.</b> What each achievement measures is still an
/// open decision (`16` O29, M16); these ids are <c>internal</c> and cover exactly what the two
/// events with an arm below support.
/// </para>
/// </remarks>
internal static class FeatCounterProjection
{
    /// <summary>The shared empty result, for the common case of a command whose events advance nothing.</summary>
    private static readonly IReadOnlyList<FeatCounterIncrement> Nothing =
        Array.Empty<FeatCounterIncrement>();

    internal const string DiceRolledCounter = "dice_rolled";

    /// <summary>The counter that counts fixed dice spent — a MOVE that was chosen, never rolled.</summary>
    /// <remarks>
    /// 🔒 Counted apart from <see cref="DiceRolledCounter"/> on purpose: "how many times have you
    /// rolled" and "how many times have you refused to" are two different questions, and one counter
    /// serving both would answer neither.
    /// </remarks>
    internal const string FixedDieUsedCounter = "fixed_die_used";

    /// <summary>Every counter advance the events in one <c>Apply</c> call imply, in the order they happened.</summary>
    /// <param name="events">One command's event list.</param>
    /// <returns>The advances, or an empty list — allocation-free — when the events imply none.</returns>
    /// <remarks>
    /// A <see cref="DomainEvent"/> the switch has no arm for advances nothing. That is the correct
    /// default for an event whose measures have not been decided, and the wrong one for an event
    /// that should have counted — so a new event type is a decision taken here, not an omission.
    /// 🔒 That sentence is now <em>enforced</em> rather than asserted:
    /// <c>FeatCounterProjectionCoverageRuleTests</c> fails the build on a concrete
    /// <see cref="DomainEvent"/> that neither reaches an arm below nor carries an owned exemption
    /// row. It used to be a promise the file made about itself, and two events had already fallen
    /// through it.
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
                    advances.Add(new FeatCounterIncrement(PipsRolledCounterFor(roll.Pips), 1L));

                    break;

                case FixedDieUsed spent:
                    advances ??= new List<FeatCounterIncrement>();
                    advances.Add(new FeatCounterIncrement(FixedDieUsedCounter, 1L));
                    advances.Add(new FeatCounterIncrement(FixedDieUsedCounterFor(spent.Pips), 1L));

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

    /// <summary>The counter that counts rolls showing a particular number of pips.</summary>
    /// <remarks>
    /// A second axis over the same event, and it is not redundant with
    /// <see cref="DiceRolledCounter"/>: how often a specific number came up is a question a total
    /// cannot answer, and it is answerable from today's event. Counting it later is impossible.
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
            "counter can be named for this roll. A zero reads as a DiceRolled a handler built " +
            "without drawing anything."),
    };

    /// <summary>The counter that counts fixed dice spent showing a particular number.</summary>
    /// <remarks>
    /// The second axis over the same event, matching <see cref="PipsRolledCounterFor"/>'s shape for
    /// rolls: which numbers a player chooses to bank and spend is the whole texture of the mechanic,
    /// and it is answerable from today's event or never.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><paramref name="pips"/> is outside the die's range.</exception>
    internal static string FixedDieUsedCounterFor(int pips) => pips switch
    {
        1 => "fixed_die_used_pips_1",
        2 => "fixed_die_used_pips_2",
        3 => "fixed_die_used_pips_3",
        4 => "fixed_die_used_pips_4",
        5 => "fixed_die_used_pips_5",
        6 => "fixed_die_used_pips_6",
        _ => throw new InvalidOperationException(
            "04 §1's die shows 1..6 pips; " + Text(pips) + " is outside that range, so no lifetime " +
            "counter can be named for this move. A zero reads as a FixedDieUsed a handler built " +
            "without spending anything."),
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
