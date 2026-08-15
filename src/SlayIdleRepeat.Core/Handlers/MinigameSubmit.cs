using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-03c, `03` §6 — the <c>MINIGAME_SUBMIT</c> handler: `03` §6.2's server-authority split
/// (server-rolled vs. client-asserted-with-legality-check) and `03` §6.1's chapter-scaled reward
/// application, for all four minigames.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What this handler owns, in order.</b> (1) refuse an unknown <c>MinigameId</c>; (2) refuse a
/// second submission at the run's current position (`03` §6.2's "exactly one submission per tile",
/// against the tile-instance proxy <c>Run.ResolvedMinigames</c> — see that field's remarks for why
/// position stands in); (3) decide the outcome tier — server-rolled minigames draw their own from
/// the run's `14` §8.1 <c>minigame:{index}</c> stream and ignore <paramref name="command"/>'s
/// claim; client-asserted minigames validate the claim is a real tier and trust it; (4) apply `03`
/// §6.1's chapter-scaled reward and record the resolution.
/// </para>
/// <para>
/// ⚠️ <b>Legality item (c) — rate limits — is not checked here, and that is a scope boundary, not
/// an oversight.</b> `03` §6.2 lists it beside the tier and duplicate-submission checks, but a rate
/// limit needs ambient wall-clock/request-cadence state `30` §2.1's <b>P1</b> (pure, no ambient
/// clock) forbids the domain from holding — <c>GameContext.NowUtc</c> is one instant, not a request
/// history. `14` §16.2's <c>RATE_LIMITED</c> is explicitly a <b>transport-tier</b> value, "produced
/// by the server host / Application layer" and "never returned by GameRules.Apply" — the same split
/// `RejectionReason`'s own remarks draw for every other transport-tier value. M5's server layer is
/// where a request-cadence history can legitimately live.
/// </para>
/// <para>
/// ⚠️ <b>The server-rolled draw is a uniform pick over the tier count, and that is a recorded
/// assumption, not a re-derived design number (steering S6).</b> `24` §4.9 authors exactly one of
/// the two server-rolled minigames' base rates — <em>"the three-chest pick is a pure 1-in-3"</em> for
/// <see cref="MinigameCatalogue.ChestPick"/>, which a uniform draw over its three tiers reproduces
/// exactly, since one of the three shuffled chests is each tier. <see cref="MinigameCatalogue.DiceDuel"/>
/// has no authored distribution anywhere in the design documents — `03` §6's own row says the duel
/// uses "the player's actual upgraded die faces", a simulation M3-04's die-face system does not exist
/// yet to run — so the same uniform draw is extended to it as the least-invented default until M3-04
/// lands a real simulation, rather than a fabricated win-rate table. Both draws consume the run's
/// reserved `14` §8.1 <c>minigame:{index}</c> stream either way, so replacing the distribution later
/// changes no other stream's replay.
/// </para>
/// <para>
/// ⚠️ <b>`24` §4.9's gold-tier pity guarantee — "every 4th consecutive miss" — is not applied here.</b>
/// It is <c>LuckService</c>'s (`24` §3, M4-01), which does not exist yet ("🔒 <c>LuckService</c>
/// lands here, before any grant path exists" — <c>IMPLEMENTATION_TRACKER.md</c>'s M4 goal). This
/// handler pays every server-rolled tier at its plain uniform odds; the guarantee is M4-01/M4-02's
/// to layer on once the pity-counter mechanism exists (`GapRegister`'s <c>PityCounters</c> entry).
/// </para>
/// <para>
/// ⚠️ <b>`24` §3's <c>CHEST_STANDARD</c> gear-grant routing is verified inert for this handler and
/// says so rather than silently not calling it.</b> §3's table lists "<c>MG_CHEST_PICK</c> rewards
/// that grant gear" as one of several sources of the <c>CHEST_STANDARD</c> luck class — but none of
/// `03` §6.1's four reward rows grants gear; all four are Gold/Crowns/Beast Feed/Enhance Stones/
/// Reroll Charges. That clause describes a reward shape `03` §6.1's authored table does not contain,
/// not a call this handler is missing.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Content.MinigameReward.RerollCharges"/> is read from `03` §6.1's
/// <c>MG_DICE_DUEL</c> 2-0 row and deliberately not granted.</b> <c>Run</c> carries no reroll-charge
/// field — <c>UseRerollCommand</c>/<c>ROLL_DICE</c>'s reroll economy is M3-04's — so there is nowhere
/// in <c>Core</c> to put it today. Every other column of that row (Gold, Crowns) is still applied.
/// </para>
/// </remarks>
internal static class MinigameSubmit
{
    /// <summary>
    /// 🔒 Recorded assumption — the `30` §7 attribution token minigame reward
    /// <c>CurrencyChanged</c> rows are logged under, matching <c>GameRules.EnergyRegenReason</c>'s
    /// and <c>Handlers.BeginSession.DailyRefillReason</c>'s shape: a stable identifier, not a design
    /// number, reported here to get its letter at integration.
    /// </summary>
    private const string RewardReason = "minigame_reward";

    /// <summary>`03` §6 — applies <c>MINIGAME_SUBMIT</c>.</summary>
    /// <param name="command">The claimed minigame id and outcome tier.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> for an unknown <c>MinigameId</c>, a tier outside
    /// `03` §6.1's table for it, or a second submission at this run's current position; otherwise
    /// accepted, with the chapter-scaled reward's <c>CurrencyChanged</c> events and the tile's
    /// resolution recorded.
    /// </returns>
    internal static HandlerResult Handle(MinigameSubmitCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        // 🔒 1 · An id outside 03 §6's four is illegal regardless of anything else about the
        // command — checked first and before the run is even read, the same "refuse before
        // anything is spent" shape StartRun.Handle's remarks name.
        if (!MinigameCatalogue.IsKnown(command.MinigameId))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var run = input.Run;

        // 🔒 2 · 03 §6.2's "exactly one submission per tile". See Run.ResolvedMinigames' remarks for
        // why the run's current position is the tile-instance proxy this task has to use.
        if (run.HasResolvedMinigameAt(run.Position))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var tuning = MinigameRewardTuning.Read(input.Context.Content);
        var tierCount = tuning.TierCount(command.MinigameId);

        int tier;
        if (MinigameCatalogue.IsServerRolled(command.MinigameId))
        {
            // 🔒 03 §6.2 — server-rolled: command.Result is a client claim this branch never reads.
            // One 14 §8.1 minigame:{index} stream per resolved instance in the run, server-rolled or
            // not — ResolvedMinigames.Count is the next unused index, the same "next ordinal, folded
            // back by Apply" shape RunRngScope.BeginBattle's battleIndex already uses.
            var stream = input.Rng.Stream(RngStreams.Minigame(run.ResolvedMinigames.Count));
            tier = stream.Range(0, tierCount);
        }
        else
        {
            // 🔒 03 §6.2 — client-asserted: the ONLY legality question is whether the claimed tier
            // is one of 03 §6.1's authored rows for this minigame. A genuine skill input is trusted
            // once it passes that check (the documented 14 §2.1 exception).
            if (command.Result < 0 || command.Result >= tierCount)
            {
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            tier = command.Result;
        }

        var reward = tuning.RewardFor(command.MinigameId, tier, run.ChapterId);
        var events = new List<DomainEvent>(4);

        // 🔒 GOLD is the run's own currency (10 §1, assumption A3) and moves through Run; the other
        // three columns are META wallet currencies (10 §1) and move through Player — see
        // Player.WalletCurrencies. A zero column is skipped rather than moved: a CurrencyChanged
        // with Delta 0 would be a real, misleading row in 21 §8.3's income_attribution.csv for a
        // currency this tier does not actually pay.
        if (reward.Gold != 0)
        {
            events.Add(run.MoveCurrency(CurrencyId.GOLD, reward.Gold, RewardReason));
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

        // reward.RerollCharges is deliberately not spent — see this type's remarks.
        run.RecordMinigameResolution(run.Position, command.MinigameId);

        return HandlerResult.Accept(events);
    }
}
