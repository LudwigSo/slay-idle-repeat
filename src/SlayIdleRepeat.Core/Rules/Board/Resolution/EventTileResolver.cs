using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// <c>TILE_EVENT</c>, in its two halves: <c>RESOLVE_TILE</c> draws the card, and
/// <c>EVENT_CHOOSE</c> resolves the option the player picked.
/// </summary>
/// <remarks>
/// <para>
/// Two draws per event, both on the <c>events</c> stream — one to pick the card and one to pick
/// the chosen option's outcome — and they happen in different commands. That is what the
/// pending-card id on <c>Run</c> exists for: the card is decided when the player lands, and it
/// must not be re-drawn when they choose, or a client could reroll an unwanted card by
/// resubmitting.
/// </para>
/// <para>
/// The option's cost is not paid here. <see cref="ResolveChoice"/> only draws and applies the
/// outcome; affordability and the debit belong to the handler, which is the layer that can answer
/// a player <c>INSUFFICIENT_FUNDS</c> instead of throwing.
/// </para>
/// </remarks>
internal static class EventTileResolver
{
    /// <inheritdoc cref="TreasureResolver.Reason"/>
    internal const string Reason = "event_card_outcome";

    /// <summary>
    /// Draws which card this event tile shows — one draw on the <c>events</c> stream, uniform over
    /// the cards this run's chapter can see.
    /// </summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <param name="catalogue">The authored cards, read from this command's content snapshot.</param>
    /// <returns>The drawn card's id, for <c>Run.SetPendingEventCard</c>.</returns>
    /// <remarks>
    /// Uniform, not weighted: no per-card draw weight is authored anywhere, only chapter banding,
    /// so a uniform pick over the eligible set is the transcription. Inventing a weight column here
    /// would be a distribution nothing specifies.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The run's chapter has no eligible card.</exception>
    internal static string DrawCard(HandlerInput input, EventCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalogue);

        var chapterId = input.Run.ChapterId;
        var eligible = catalogue.AvailableIn(chapterId);

        if (eligible.Count == 0)
        {
            throw new InvalidOperationException(
                "19 Part A authors no event card available in chapter " + Text(chapterId) +
                ". Its three bands are chapters 1-3, 3-6 and 6-8 and together they cover every " +
                "chapter 02 §1 runs, so an empty set here means the content file lost a band.");
        }

        return eligible[input.Rng.Stream(RngStreams.Events).Range(0, eligible.Count)].Id;
    }

    /// <summary>
    /// Draws and applies one outcome of the option the player chose — one draw on the <c>events</c>
    /// stream.
    /// </summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <param name="card">The card <c>Run.PendingEventCardId</c> named.</param>
    /// <param name="choiceIndex">Which option, by its position in <c>card.Options</c>.</param>
    /// <returns>
    /// Every <see cref="CurrencyChanged"/> the drawn outcome's effects produced, in effect order.
    /// Empty when the outcome moves no currency — the common case, since most outcomes are
    /// <see cref="EventEffectOp.Unsupported"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="choiceIndex"/> is outside the card's options. A defect, not a rejection: the
    /// handler answers the player <c>ILLEGAL_STATE</c> for an out-of-range choice before ever
    /// reaching here, so an index arriving at this method is a miswired caller rather than a player
    /// asking for an option that does not exist.
    /// </exception>
    internal static IReadOnlyList<DomainEvent> ResolveChoice(
        HandlerInput input, EventCard card, int choiceIndex)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(card);

        if (choiceIndex < 0 || choiceIndex >= card.Options.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(choiceIndex),
                choiceIndex,
                "'" + card.Id + "' offers " + Text(card.Options.Count) + " option(s), indexed from " +
                "0. The EVENT_CHOOSE handler refuses an out-of-range index as ILLEGAL_STATE before " +
                "this method is reached, so arriving here with one is a miswired caller.");
        }

        var option = card.Options[choiceIndex];

        var table = new (EventOutcome Item, double Weight)[option.Outcomes.Count];
        for (var i = 0; i < option.Outcomes.Count; i++)
        {
            table[i] = (option.Outcomes[i], option.Outcomes[i].Weight);
        }

        var outcome = input.Rng.Stream(RngStreams.Events).WeightedPick(table);
        var events = new List<DomainEvent>(outcome.Effects.Count);

        // Read ONCE for the whole outcome, and only when an effect actually scales — the read
        // resolves JSON pointers and re-runs validation, so doing it per effect inside the loop
        // below would repeat that work for every scaled row of a multi-effect outcome.
        var scalars = NeedsChapterScalars(outcome.Effects)
            ? ChapterScalarTuning.Read(input.Context.Content)
            : null;

        foreach (var effect in outcome.Effects)
        {
            Apply(input, effect, scalars, events);
        }

        return events;
    }

    /// <summary>
    /// Whether any effect of this outcome is a chapter-scaled currency grant, and therefore whether
    /// the chapter scalars have to be read at all. Most outcomes do not scale.
    /// </summary>
    private static bool NeedsChapterScalars(IReadOnlyList<EventEffect> effects)
    {
        for (var i = 0; i < effects.Count; i++)
        {
            if (effects[i].Op == EventEffectOp.Currency && effects[i].ChapterScaled)
            {
                return true;
            }
        }

        return false;
    }

    private static void Apply(
        HandlerInput input, EventEffect effect, ChapterScalarTuning? scalars, List<DomainEvent> events)
    {
        switch (effect.Op)
        {
            case EventEffectOp.Currency:
                PayCurrency(input, effect, scalars, events);
                return;

            case EventEffectOp.HpPct:
                ApplyHpChange(input.Run, effect.HpPct!.Value);
                return;

            case EventEffectOp.CurseReward:
            {
                // The SAME narrow table CurseTileResolver pays from, called rather than copied —
                // two switches over the same ids would be two places to get the numbers wrong. The
                // same limit applies: the reward is paid, the curse is NOT applied.
                var (currency, amount) = CurseRewards.For(effect.CurseId!);
                Move(input, currency, amount, events);
                return;
            }

            case EventEffectOp.None:
            case EventEffectOp.Unsupported:
                // Nothing happens, and for UNSUPPORTED that is correct rather than a missing branch:
                // the effect names a mechanic Core cannot execute yet. Inventing a substitute payout
                // would be indistinguishable downstream from a real reward.
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(effect),
                    effect.Op,
                    "The event effect vocabulary grew an op this resolver does not handle. It is a " +
                    "CLOSED five-op set (board_events.schema.json): a new op is a new branch here, " +
                    "not a new content row, and falling through silently would make it a no-op that " +
                    "looks authored.");
        }
    }

    private static void PayCurrency(
        HandlerInput input, EventEffect effect, ChapterScalarTuning? scalars, List<DomainEvent> events)
    {
        var amount = effect.Amount!.Value;

        if (effect.ChapterScaled)
        {
            // ScaleMeta rather than a multiply by the scalar: the scaled amount rounds to a whole
            // currency unit, never the scalar itself — see ChapterScalarTuning.ScaleMeta. `scalars`
            // is non-null exactly when this branch is reachable.
            amount = scalars!.ScaleMeta(amount, input.Run.ChapterId);
        }

        Move(input, effect.Currency!.Value, amount, events);
    }

    /// <summary>
    /// <c>GOLD</c> is the run's own currency and moves through <c>Run</c>; the other seven are META
    /// wallet currencies and move through <c>Player</c>. A zero amount is skipped rather than moved
    /// — a <c>CurrencyChanged</c> with delta 0 would be a real, misleading row in the attribution
    /// log.
    /// </summary>
    private static void Move(HandlerInput input, CurrencyId currency, long amount, List<DomainEvent> events)
    {
        if (amount == 0)
        {
            return;
        }

        events.Add(currency == CurrencyId.GOLD
            ? input.Run.MoveCurrency(currency, amount, Reason)
            : input.Player.MoveCurrency(currency, amount, Reason));
    }

    /// <summary>
    /// Applies an <see cref="EventEffectOp.HpPct"/> — a signed share of Max HP, clamped into
    /// <c>0..MaxHp</c> here rather than by the aggregate.
    /// </summary>
    private static void ApplyHpChange(Run run, double share)
    {
        var delta = (int)Math.Round(run.MaxHp * share, MidpointRounding.ToEven);
        var current = Math.Clamp(run.CurrentHp + delta, 0, run.MaxHp);

        run.SetHitPoints(current, run.MaxHp);
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
