using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Rules.Perks;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>PICK_PERK</c> handler: takes one of the three drafted options and closes the draft.
/// </summary>
/// <remarks>
/// <para>
/// The draft is regenerated, never persisted: the three options a client is shown are re-derived
/// here from the run's committed draft-stream position, so drafting stays byte-identical for a given
/// seed. <c>Handlers.RerollDraft</c> reaches <see cref="GenerateCurrentOptions"/> here rather than
/// carrying a second copy, since every type under <c>Core/Handlers/</c> is expected to be a
/// registered command handler and a shared non-handler type would not be one.
/// </para>
/// <para>
/// The three run-scoped draft counters move when a draft is <em>taken</em>, not when one is drawn:
/// a reroll redraws the options and must not push a guarantee closer, or the guarantee would be
/// purchasable with Gold.
/// </para>
/// </remarks>
internal static class PickPerk
{
    /// <summary>Applies <c>PICK_PERK</c>.</summary>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no draft is pending or the index names no
    /// option; otherwise accepted.
    /// </returns>
    internal static HandlerResult Handle(PickPerkCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.DraftPending)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var options = GenerateCurrentOptions(input, run, out var demand);

        if (command.OptionIndex < 0 || command.OptionIndex >= options.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var chosen = options[command.OptionIndex];

        MoveDraftCounters(run, options, demand);

        run.UpsertPerkTier(chosen.PerkId, chosen.NewTier);
        run.ClearDraftPending();

        return HandlerResult.Accept();
    }

    /// <summary>Stores the run's three draft counters as the closing offering leaves them.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than where the options are drawn, because the counters count the drafts a run
    /// actually <em>picked from</em>. That excludes the two other ways a draft ends, for two
    /// different reasons: a reroll draws a set nobody was ever offered the chance to take, and a
    /// counter a reroll moved would let a player push a guarantee towards themselves with Gold; a
    /// skip takes no option at all, so there is no pick to count — it pays the player rather than
    /// costing them, so the purchasability argument never arises for it.
    /// </para>
    /// <para>
    /// The demand is the one the options were drawn under, read before the pick is applied — the
    /// counters are about the draft as it was offered, not about what taking it left the run holding.
    /// </para>
    /// </remarks>
    private static void MoveDraftCounters(
        Run run, IReadOnlyList<DraftOption> options, DraftDemand demand)
    {
        var legendary = false;
        var aboveCommon = false;
        var ownedUpgrade = false;

        foreach (var option in options)
        {
            legendary |= option.Rarity == PerkRarity.Legendary;
            aboveCommon |= option.Rarity > PerkRarity.Common;
            ownedUpgrade |= option.IsUpgrade;
        }

        var moved = LuckService.DraftCountersAfter(
            new DraftCounters(
                run.DraftsSinceLegendaryOffered,
                run.DraftsWithoutAboveCommon,
                run.DraftsWithoutOwnedUpgrade),
            new DraftOffering(legendary, aboveCommon, ownedUpgrade),
            demand);

        run.SetDraftCounters(
            moved.DraftsSinceLegendaryOffered,
            moved.DraftsWithoutAboveCommon,
            moved.DraftsWithoutOwnedUpgrade);
    }

    /// <summary>
    /// The three options currently on offer, drawn from <paramref name="input"/>'s <c>draft</c>
    /// stream at its current position, under whichever <c>DRAFT</c> guarantees the luck façade says
    /// this draft owes — see this type's remarks for why <c>REROLL_DRAFT</c> shares this method.
    /// </summary>
    /// <param name="input">The command's world slice, content snapshot and draw streams.</param>
    /// <param name="run">The run the draft belongs to.</param>
    /// <param name="demand">
    /// What the run owned and where it stood as these options were drawn. Answered out rather than
    /// recomputed by the caller: the counter move reads the same facts the guarantees resolved
    /// against, and answering the catalogue twice per command would be a second chance for the two
    /// to disagree as much as it would be a second parse.
    /// </param>
    internal static IReadOnlyList<DraftOption> GenerateCurrentOptions(
        HandlerInput input, Run run, out DraftDemand demand)
    {
        var catalogue = PerkCatalogue.Read(input.Context.Content);
        var tuning = LuckTuning.Read(input.Context.Content);
        var owned = run.DraftedPerks;
        var rng = input.Rng.Stream(RngStreams.Draft);

        var battleKind = (TileKind)run.DraftBattleKindValue;
        var isElite = battleKind == TileKind.Elite;
        var isBoss = battleKind == TileKind.Boss;

        demand = Demand(catalogue, owned, run.DraftBattleStage, isBoss);

        var forces = LuckService.ResolveDraft(
            tuning,
            new DraftCounters(
                run.DraftsSinceLegendaryOffered,
                run.DraftsWithoutAboveCommon,
                run.DraftsWithoutOwnedUpgrade),
            demand,
            PerkDraftEngine.OptionCount);

        return PerkDraftEngine.GenerateOptions(
            new DraftRequest(
                catalogue,
                owned,
                tuning,
                DraftRarityWeights.For(run.DraftBattleStage, isElite, isBoss),
                forces,
                // ⚠️ The run's own drafted perks, which is a genuine SUBSET of "ever drafted": no
                // player-lifetime Codex exists yet and M4-11 owns building one. The rule is exact
                // against whatever set it is handed; the set is the incomplete half.
                owned.Tiers.Keys.ToHashSet(StringComparer.Ordinal)),
            rng);
    }

    /// <summary>What the run owns and where it stands, as the <c>DRAFT</c> guarantees read it.</summary>
    /// <remarks>
    /// Both facts are about the run's own perks and are answered against the catalogue rather than
    /// stored: a perk's category and its top tier are content, and caching either on the run would be
    /// a second copy of the catalogue that a content version could silently outdate.
    /// </remarks>
    private static DraftDemand Demand(
        PerkCatalogue catalogue, DraftedPerks owned, int stage, bool isBoss)
    {
        var ownsSustain = false;
        var ownsNonMaxed = false;

        foreach (var (perkId, tier) in owned.Tiers)
        {
            // A run can outlive a content version that dropped a perk; an id the catalogue no longer
            // carries is neither a Sustain perk nor an upgradable one, and is not a reason to throw.
            if (!catalogue.Contains(perkId))
            {
                continue;
            }

            var perk = catalogue.Find(perkId);

            ownsSustain |= perk.Category == PerkCategory.Sustain;
            ownsNonMaxed |= tier < perk.TierCount;
        }

        return new DraftDemand(stage, isBoss, ownsSustain, ownsNonMaxed);
    }
}
