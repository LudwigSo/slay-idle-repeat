using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Luck;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// Everything one draft is drawn against that comes off the run rather than off the draw stream.
/// </summary>
/// <remarks>
/// A value object with two factories rather than a parameter list, because the run's draft is read
/// from two shapes — the aggregate inside a command, the snapshot outside one — and the whole point
/// of <see cref="CurrentDraft"/> is that those two readings never disagree. Held side by side, a
/// field one factory forgot is a field the other still carries; spread over three call sites it is a
/// screen quietly showing a draft drawn under different counters than the command applies.
/// </remarks>
/// <param name="Content">The version-stamped snapshot the draft is drawn against.</param>
/// <param name="Owned">The run's currently-owned perks and their tiers.</param>
/// <param name="BattleKind">The tile kind of the battle that opened this draft.</param>
/// <param name="Stage">The stage that battle belonged to.</param>
/// <param name="Counters">The run's three draft counters, as the guarantees read them.</param>
internal readonly record struct DraftStanding(
    ContentSnapshot Content,
    DraftedPerks Owned,
    TileKind BattleKind,
    int Stage,
    DraftCounters Counters)
{
    /// <summary>Where the run stands, read off the aggregate a command is holding.</summary>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    internal static DraftStanding Of(RunAggregate run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        return new DraftStanding(
            content,
            run.DraftedPerks,
            (TileKind)run.DraftBattleKindValue,
            run.DraftBattleStage,
            new DraftCounters(
                run.DraftsSinceLegendaryOffered,
                run.DraftsWithoutAboveCommon,
                run.DraftsWithoutOwnedUpgrade));
    }

    /// <summary>The same standing, read off the persisted row a projection is handed.</summary>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    internal static DraftStanding Of(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        return new DraftStanding(
            content,
            new DraftedPerks(run.OwnedPerkTiers ?? NoPerks),
            (TileKind)run.DraftBattleKind,
            run.DraftBattleStage,
            new DraftCounters(
                run.DraftsSinceLegendaryOffered,
                run.DraftsWithoutAboveCommon,
                run.DraftsWithoutOwnedUpgrade));
    }

    /// <summary>A run that has drafted nothing — the snapshot's absent map read as owning none.</summary>
    private static IReadOnlyDictionary<string, int> NoPerks { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

/// <summary>
/// The three options a run has on offer right now, drawn off its committed <c>draft</c> stream
/// position.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The one derivation, called by everything that needs it.</b> The options are regenerated on
/// every command and never persisted, so <c>PICK_PERK</c>, <c>REROLL_DRAFT</c> and the screen's
/// projection all have to arrive at the same three. A second copy of this — however faithful the day
/// it was written — is a second chance for them to disagree, and the disagreement a player sees is
/// taking a perk the card they pressed never showed.
/// </para>
/// <para>
/// The stream is a parameter rather than opened here: a command draws from the scope
/// <c>GameRules.Apply</c> folds back, and a projection reopens the same stream at the same committed
/// position and folds nothing. Which of the two is drawing is the caller's fact, and the draw is
/// identical either way.
/// </para>
/// </remarks>
internal static class CurrentDraft
{
    /// <summary>
    /// Draws the offer, under whichever <c>DRAFT</c> guarantees the luck façade says this draft owes.
    /// </summary>
    /// <param name="standing">What the run owns and where it stands — see <see cref="DraftStanding"/>.</param>
    /// <param name="rng">The <c>draft</c> stream, at the position the run committed it at.</param>
    /// <param name="demand">
    /// What the run owned and where it stood as these options were drawn. Answered out rather than
    /// recomputed by the caller: the counter move reads the same facts the guarantees resolved
    /// against, and answering the catalogue twice per command would be a second chance for the two
    /// to disagree as much as it would be a second parse.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null, or the standing carries no content.</exception>
    internal static IReadOnlyList<DraftOption> Draw(
        DraftStanding standing, DeterministicRng rng, out DraftDemand demand)
    {
        ArgumentNullException.ThrowIfNull(standing.Content);
        ArgumentNullException.ThrowIfNull(standing.Owned);
        ArgumentNullException.ThrowIfNull(rng);

        var catalogue = PerkCatalogue.Read(standing.Content);
        var tuning = LuckTuning.Read(standing.Content);
        var owned = standing.Owned;

        var isElite = standing.BattleKind == TileKind.Elite;
        var isBoss = standing.BattleKind == TileKind.Boss;

        demand = Demand(catalogue, owned, standing.Stage, isBoss);

        var forces = LuckService.ResolveDraft(
            tuning, standing.Counters, demand, PerkDraftEngine.OptionCount);

        return PerkDraftEngine.GenerateOptions(
            new DraftRequest(
                catalogue,
                owned,
                tuning,
                DraftRarityWeights.For(standing.Stage, isElite, isBoss),
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
