using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>MINIGAME_SUBMIT</c> handler: applies the server-authority split (server-rolled vs.
/// client-asserted-with-legality-check) and the chapter-scaled reward for all four minigames.
/// </summary>
/// <remarks>
/// <para>
/// In order: refuse an unknown <c>MinigameId</c>; refuse a second submission at the run's current
/// position (exactly one submission per tile, against the tile-instance proxy
/// <c>Run.ResolvedMinigames</c>); decide the outcome tier — server-rolled minigames draw their own
/// from the run's RNG stream and ignore the command's claim, client-asserted minigames validate the
/// claim is a real tier and trust it; apply the chapter-scaled reward, record the resolution, and
/// clear the pending tile.
/// </para>
/// <para>
/// Rate limiting is not checked here: it needs ambient request-cadence state the domain is not
/// allowed to hold, so it belongs to the transport layer instead.
/// </para>
/// <para>
/// The server-rolled draw is a uniform pick over the tier count. That reproduces the authored
/// three-chest 1-in-3 rate exactly; the dice-duel minigame has no authored distribution yet, so the
/// same uniform draw is used as the least-invented default until a real simulation exists. The chest
/// pick draws through the luck façade instead, because it is the one minigame that carries a pity
/// counter — the tier and the counter movement both come back from there, and the counter is
/// player-scoped and lifetime rather than anything the run holds. Gear-grant routing is still
/// absent, and depends on a system that does not exist yet.
/// </para>
/// <para>
/// Every reward column is applied, the Reroll Charge included — the run carries a grant counter
/// now, and it is the same one the Campfire and the Reroll Token write into. Gold is scaled by the
/// run's own Gold modifiers on the way in, at the income site rather than in the reward table.
/// </para>
/// </remarks>
internal static class MinigameSubmit
{
    /// <summary>Income-attribution token minigame reward <c>CurrencyChanged</c> rows are logged under.</summary>
    private const string RewardReason = "minigame_reward";

    /// <summary>Applies <c>MINIGAME_SUBMIT</c>.</summary>
    /// <param name="command">The claimed minigame id and outcome tier.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> for an unknown <c>MinigameId</c>, a tier outside
    /// its table, or a second submission at this run's current position; otherwise accepted, with the
    /// chapter-scaled reward's <c>CurrencyChanged</c> events and the tile's resolution recorded.
    /// </returns>
    internal static HandlerResult Handle(MinigameSubmitCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (!MinigameCatalogue.IsKnown(command.MinigameId))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var run = input.Run;

        if (run.HasResolvedMinigameAt(run.Position))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var tuning = MinigameRewardTuning.Read(input.Context.Content);
        var tierCount = tuning.TierCount(command.MinigameId);

        int tier;
        PityCounterChange? counterMoved = null;

        if (MinigameCatalogue.IsServerRolled(command.MinigameId))
        {
            // command.Result is a client claim this branch never reads. One RNG stream per resolved
            // instance in the run, server-rolled or not.
            var stream = input.Rng.Stream(RngStreams.Minigame(run.ResolvedMinigames.Count));

            if (string.Equals(command.MinigameId, MinigameCatalogue.ChestPick, StringComparison.Ordinal))
            {
                // The one minigame that carries a counter. The other three are skill-scaled, and
                // advancing this counter on any of them would make the gold chest farmable through
                // whichever of them is cheapest.
                var resolution = LuckService.ResolveChestPick(
                    LuckTuning.Read(input.Context.Content),
                    tuning.OutcomeName(command.MinigameId, LuckService.ChestPickTopTier(tierCount)),
                    input.Player.PityCounters,
                    stream,
                    tierCount);

                tier = resolution.Tier;
                counterMoved = resolution.Counter;
            }
            else
            {
                tier = stream.Range(0, tierCount);
            }
        }
        else
        {
            // Client-asserted: the only legality question is whether the claimed tier is one of the
            // authored rows for this minigame.
            if (command.Result < 0 || command.Result >= tierCount)
            {
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            tier = command.Result;
        }

        var reward = tuning.RewardFor(command.MinigameId, tier, run.ChapterId);
        var events = new List<DomainEvent>(5);

        // Gold is the run's own currency and moves through Run; the other three columns are wallet
        // currencies on Player. A zero column is skipped rather than moved, to avoid a misleading
        // zero-delta row for a currency this tier doesn't actually pay.
        var gold = RunModifierTotals.ScaleGoldIncome(run, input.Context.Content, reward.Gold);

        if (gold != 0)
        {
            events.Add(run.MoveCurrency(CurrencyId.GOLD, gold, RewardReason));
        }

        if (reward.Crowns != 0)
        {
            events.Add(input.Player.MoveCurrency(CurrencyId.CROWNS, reward.Crowns, RewardReason));
        }

        if (reward.BeastFeed != 0)
        {
            events.Add(input.Player.MoveCurrency(CurrencyId.BEAST_FEED, reward.BeastFeed, RewardReason));
        }

        if (reward.EnhanceStones != 0)
        {
            events.Add(input.Player.MoveCurrency(CurrencyId.ENHANCE_STONES, reward.EnhanceStones, RewardReason));
        }

        // The counter is stored only once the pick has been paid for: a resolution reports what it
        // would leave behind, and this is where that becomes state.
        if (counterMoved is { } moved)
        {
            input.Player.SetPityCounter(moved.Key, moved.Value);
            events.Add(new PityCounterAdvanced(DomainEvent.UnstampedSequence, moved.Key, moved.Value));
        }

        // 🔒 The dice duel's own reward, and the shape of it is the point: winning a game ABOUT dice
        // pays a die you get to choose the number on. `03` §6.1's "Win 2-0" row is where it is
        // authored; every other row pays none.
        if (reward.FixedDice > 0)
        {
            // Narrowed after the check, not before: the reward column is a long because every other
            // column is, and a grant count is an int — the check is what makes the cast safe.
            run.GrantFixedDieChoices((int)Math.Min(reward.FixedDice, int.MaxValue));
        }

        // After the rows, and only on a path that has already passed the legality gate: two of the
        // four minigames draw their tier here and ignore the client's claim, and no persisted field
        // records the draw — so this is the only thing that can tell a screen which row it was paid
        // from. Raised last of the reward events so a reader meets the movements and then their cause.
        events.Add(new MinigameResolved(
            DomainEvent.UnstampedSequence,
            command.MinigameId,
            tier,
            tuning.OutcomeName(command.MinigameId, tier)));

        run.RecordMinigameResolution(run.Position, command.MinigameId);

        // Must be cleared here or ROLL_DICE can never legally fire again: RecordMinigameResolution
        // only touches the tile-instance proxy, not the pending-tile fields.
        run.ClearPendingTile();

        return HandlerResult.Accept(events);
    }
}
