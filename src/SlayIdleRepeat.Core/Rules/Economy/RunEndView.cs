using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>Why a run is at its end, as the screen that closes it draws the difference.</summary>
public enum RunEndKind
{
    /// <summary>The Boss is dead. The only outcome that pays the victory bonus.</summary>
    Victory = 1,

    /// <summary>The hero is at zero hit points and the fight is still pending.</summary>
    Death = 2,

    /// <summary>Neither — the player is choosing to stop a run that could still go on.</summary>
    Abandoned = 3,
}

/// <summary>
/// The run-end moment as one projection: why the run is over, whether a revive is still on the table,
/// and what the payout will be.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One projection for S13 and S14 because <c>02</c> §6 makes them one moment.</b> Its step 3 is
/// explicit: <em>"If declined or already used, go to <c>RUN_RESULTS</c> with the Death completion
/// multiplier."</em> The revive offer and the tally are the same instant seen before and after one
/// decision, and splitting them would mean two reads of the same row that could disagree about which
/// outcome the multiplier is being taken from.
/// </para>
/// <para>
/// 🔒 <b>The payout is the same arithmetic <c>END_RUN</c> will apply, not a preview of it.</b>
/// <c>RunRewardMath.FinalPayoutFor</c> answers both, so the number a player is shown is the number they
/// are paid. A screen that multiplied the banked figures itself would be a second implementation of
/// <c>FinalPayout = Banked × CompletionMultiplier × AdDoubleMultiplier</c>, and the two would part
/// company the first time a multiplier was retuned — with the player believing the screen.
/// </para>
/// <para>
/// 🔒 <b>Both the banked and the final figures are carried, because the multiplier is the point.</b>
/// <c>24</c> §9's S14 row wants the completion multiplier legible, and a screen handed only the final
/// number cannot show what was lost to dying. Handing it only the banked one would be worse: it would
/// promise a payout the player does not get.
/// </para>
/// <para>
/// ⚠️ <b>The ad-doubled figure is deliberately absent.</b> <c>AD_DOUBLE_RUN_REWARDS</c> is an ad reward
/// and <c>CLAIM_AD_REWARD</c> is deferred to M15-03, so no screen may show a doubled total it cannot
/// grant. <c>watchedAd: false</c> is passed explicitly rather than defaulted, so the day M15-03 lands
/// there is one call site to change and it is already named.
/// </para>
/// </remarks>
public sealed class RunEndView
{
    /// <summary>🔒 No ad has been watched, because no command can grant an ad reward yet.</summary>
    private const bool NoAdWatched = false;

    private RunEndView(
        RunEndKind kind,
        bool reviveOffered,
        long bankedLegendXp,
        long bankedSoulShards,
        long payoutLegendXp,
        long payoutSoulShards,
        int itemsAtOrAboveFloorBand,
        IReadOnlyList<RunEndCounterView> dropCounters)
    {
        Kind = kind;
        ReviveOffered = reviveOffered;
        BankedLegendXp = bankedLegendXp;
        BankedSoulShards = bankedSoulShards;
        PayoutLegendXp = payoutLegendXp;
        PayoutSoulShards = payoutSoulShards;
        ItemsAtOrAboveFloorBand = itemsAtOrAboveFloorBand;
        DropCounters = dropCounters;
    }

    /// <summary>Why the run is at its end.</summary>
    public RunEndKind Kind { get; }

    /// <summary>
    /// Whether <c>REVIVE</c> is still available — the run died and has not used its one revive.
    /// </summary>
    /// <remarks>
    /// 🔒 The same three facts <c>Handlers.Revive</c> checks, read rather than restated: the run is
    /// in progress at zero hit points with its fight still pending, and the placement's use count is
    /// zero. <c>02</c> §6's limit is <em>once per run, hard</em>, and it is counted on the run rather
    /// than on the player so a second run gets its own.
    /// <para>
    /// ⚠️ This says the RULES will accept a revive. Whether the player can reach one is a separate
    /// question the screen answers with its entitlement — <c>02</c> §6 gives Plus an instant no-ad
    /// revive, and the ad path that would serve everyone else is M15-03's.
    /// </para>
    /// </remarks>
    public bool ReviveOffered { get; }

    /// <summary>Legend XP the run banked, before the completion multiplier.</summary>
    public long BankedLegendXp { get; }

    /// <summary>Soul Shards the run banked, before the completion multiplier.</summary>
    public long BankedSoulShards { get; }

    /// <summary>Legend XP the player will actually be paid.</summary>
    public long PayoutLegendXp { get; }

    /// <summary>Soul Shards the player will actually be paid.</summary>
    public long PayoutSoulShards { get; }

    /// <summary>
    /// How many items at or above the session floor's band this run produced — <c>24</c> §4.3 D3.
    /// </summary>
    /// <remarks>
    /// Carried as the count rather than as "the floor fires": whether it fires also depends on the
    /// day's allowance, which is the player's rather than the run's, and <c>EndRun</c> is where the two
    /// meet. What a screen can honestly say from here is what this run produced.
    /// </remarks>
    public int ItemsAtOrAboveFloorBand { get; }

    /// <summary>
    /// The <c>DROP_RUN</c> counters as <c>24</c> §9's S14 row requires them in the tally footer.
    /// </summary>
    public IReadOnlyList<RunEndCounterView> DropCounters { get; }

    /// <summary>Projects the run-end moment from the persisted rows.</summary>
    /// <param name="player">The player's row — where the lifetime counters live.</param>
    /// <param name="run">The run's row.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="MissingContentException">A document the projection reads is absent.</exception>
    public static RunEndView Project(
        PlayerSnapshot player, RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        // 🔒 The same selection END_RUN makes, taken from RunRewardMath rather than restated: the
        // outcome is what picks the completion multiplier, so two implementations would be two
        // multipliers and this screen would promise a number the payout did not honour.
        var outcome = RunRewardMath.OutcomeFor(
            run.BossDefeated, run.PendingTileKind >= 0 ? run.PendingTileStage : null);

        var payout = RunRewardMath.FinalPayoutFor(
            run.BankedLegendXp, run.BankedSoulShards, outcome, NoAdWatched, content);

        return new RunEndView(
            KindOf(run),
            ReviveIsStillOnTheTable(run),
            run.BankedLegendXp,
            run.BankedSoulShards,
            payout.LegendXp,
            payout.SoulShards,
            run.ItemsAtOrAboveFloorBand,
            Counters(player, content));
    }

    /// <summary>Why this run is over, read off the row rather than told to the projection.</summary>
    /// <remarks>
    /// 🔒 Derived, because a caller that passed the outcome could pass the wrong one — and the outcome
    /// selects the completion multiplier, so a wrong one is a wrong payout shown to the player. A dead
    /// boss is a victory; zero hit points is a death; anything else is a run being abandoned while it
    /// could still go on.
    /// </remarks>
    private static RunEndKind KindOf(RunSnapshot run) =>
        run.BossDefeated ? RunEndKind.Victory
        : run.CurrentHp == 0 ? RunEndKind.Death
        : RunEndKind.Abandoned;

    /// <summary>Whether the rules would accept a <c>REVIVE</c> right now.</summary>
    private static bool ReviveIsStillOnTheTable(RunSnapshot run) =>
        run.Phase == RunPhase.InProgress &&
        run.CurrentHp == 0 &&
        run.PendingTileKind >= 0 &&
        UseCount(run, ReviveTuning.PlacementId) == 0;

    private static long UseCount(RunSnapshot run, string placement) =>
        run.AdUses is { } uses && uses.TryGetValue(placement, out var used) ? used : 0;

    /// <summary>The <c>DROP_RUN</c> class's counters, each with the rung it is counting towards.</summary>
    /// <remarks>
    /// 🔒 <c>24</c> §1.1's Visibility rule again — every counter shown to the player always, as a plain
    /// sentence with a real number — and its Disclosure rule puts every <c>N</c> in §4 on its class's own
    /// screen. §9 names S14 for <c>DROP_RUN</c>'s, so they leave <c>Core</c> here or they are not shown.
    /// The arithmetic is the rules layer's for the reason the draft's is: a countdown computed in a
    /// presenter is a second authority over a store-policy disclosure.
    /// </remarks>
    private static IReadOnlyList<RunEndCounterView> Counters(
        PlayerSnapshot player, ContentSnapshot content)
    {
        var luck = LuckTuning.Read(content);
        var dropRun = DropRunTuning.Read(content);

        // The two authored dry-streak breakers, in the order 24 §4.3 states them.
        var breakers = new[] { dropRun.EliteMercy, dropRun.BossMercy };
        var counters = new RunEndCounterView[breakers.Length];

        for (var index = 0; index < counters.Length; index++)
        {
            var breaker = breakers[index];

            // 🔒 Formed through LuckTuning, which is the one place a counter key is spelled. A key
            // assembled here would be a second spelling, and the two would diverge silently — a
            // counter read under the wrong name reports zero, which is indistinguishable from a player
            // who has just had their guarantee.
            var key = luck.CounterKey(SourceClass.DROP_RUN, breaker.ForceRarityAtLeast);
            var stood = Counter(player, key);

            counters[index] = new RunEndCounterView(
                key,
                stood,
                breaker.ForceOnNthKill,
                Math.Max(1, breaker.ForceOnNthKill - stood));
        }

        return Array.AsReadOnly(counters);
    }

    private static int Counter(PlayerSnapshot player, string key) =>
        player.PityCounters is { } counters && counters.TryGetValue(key, out var stood) ? stood : 0;
}

/// <summary>
/// One <c>DROP_RUN</c> counter in the tally footer, as <c>24</c> §9's S14 row requires it.
/// </summary>
/// <param name="Key">The authored counter key, which is also its identity on the profile.</param>
/// <param name="DropsStood">Where the counter stands right now. Never negative.</param>
/// <param name="ForcedOnDrop">
/// The authored rung — the <c>N</c> `24` §1.1's Disclosure rule requires stated in-game.
/// </param>
/// <param name="DropsUntilForced">
/// How many drops including the next one before the guarantee fires. <b>One</b> means the next drop is
/// the forced one; it never reads zero, for the reason the draft's countdown never does.
/// </param>
public sealed record RunEndCounterView(
    string Key, int DropsStood, int ForcedOnDrop, int DropsUntilForced);
