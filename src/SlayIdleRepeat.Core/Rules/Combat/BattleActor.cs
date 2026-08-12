using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// One registered effect instance on one actor, with the two things every trigger moment needs of it
/// without a registry lookup: which kind it fires on, and where it sorts.
/// </summary>
/// <param name="Id">The `18` §3 instance id it is registered under.</param>
/// <param name="EffectId">The authored effect id — `18` §8's ordinal sort key.</param>
/// <param name="Kind">The trigger kind, so a moment can skip it without resolving the instance.</param>
internal readonly record struct HeldInstance(EffectInstanceId Id, string EffectId, TriggerKind Kind)
{
    /// <summary>`18` §8's ordinal effect-id order, tie-broken by the instance id.</summary>
    internal static IComparer<HeldInstance> Order { get; } = new ByEffectThenInstanceId();

    private sealed class ByEffectThenInstanceId : IComparer<HeldInstance>
    {
        public int Compare(HeldInstance left, HeldInstance right)
        {
            var byEffect = EffectOrder.IdComparer.Compare(left.EffectId, right.EffectId);

            return byEffect != 0
                ? byEffect
                : EffectInstanceId.Comparer.Compare(left.Id.Value, right.Id.Value);
        }
    }
}

/// <summary>
/// 🔒 One actor's live state for the length of a fight — the mutable half of <see cref="ActorPlan"/>,
/// and the <see cref="IEffectActorView"/> every `18` §4 condition and `18` §5 target reads.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One view of one battle, not two.</b> <see cref="IEffectActorView"/>'s own remarks are
/// explicit that M2-07's stat block was <em>"expected to IMPLEMENT this, not to restate it"</em>,
/// because <em>"two views of one battle are two chances for <c>ENEMY_COUNT</c> and a stat aggregation
/// to disagree about who is alive"</em>. This is that single view: the loop, the conditions, the
/// targets and the ops all read the same object.
/// </para>
/// <para>
/// 🔒 <b><see cref="IsAlive"/> goes false the moment HP reaches 0, not at slot 6.</b> `05` §3.1 step
/// 6: <em>"An actor whose HP reaches 0 stops acting and being targetable at that moment — only its
/// death <i>resolution</i> (<c>ON_DEATH</c>, removal) waits for this slot."</em> So an enemy killed by
/// the hero at slot 4 is already out of the enemy list when the next enemy in the same slot picks a
/// target, and <see cref="DeathResolved"/> is what slot 6 uses to find the bodies it has not buried.
/// </para>
/// <para>
/// ⚠️ <b>A stateful class under <c>Rules/</c></b>, which `30` §11.4 annotates as <em>"internal,
/// static, stateless calculators"</em> — recorded rather than hidden, on <c>CombatLog</c>'s
/// precedent and for its reason: a 1800-tick loop has to keep HP and a cooldown somewhere. It is
/// per-battle, owned by exactly one caller, never shared and never static.
/// </para>
/// </remarks>
internal sealed class BattleActor : IEffectActorView
{
    private readonly IStatusTimeline _timeline;

    private double _currentHp;

    /// <summary>Builds the live state for one actor from its plan.</summary>
    /// <param name="plan">The actor as it enters the fight.</param>
    /// <param name="timeline">
    /// The `05` §5 status store — <see cref="StatusStacks"/> is a reading of it, not a second copy.
    /// </param>
    internal BattleActor(ActorPlan plan, IStatusTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeline);

        Plan = plan;
        _timeline = timeline;
        Index = plan.Index;
        LogId = plan.LogId;
        TargetPriority = plan.TargetPriority;

        // 🔒 Before `18` §8 has run, an actor's stats ARE its base block. Aggregation replaces this
        // at pre-tick 0a; it is set here so that no window exists in which Stats is null and a
        // reader has to decide what to do about it (steering S6).
        Stats = plan.BaseStats;
        Flow = new CombatFlowState();
        _currentHp = plan.BaseStats[StatId.MAX_HP];

        var standing = new List<EffectDefinition>(plan.Effects.Count);
        foreach (var held in plan.Effects)
        {
            if (held.Effect.Trigger is null)
            {
                standing.Add(held.Effect);
            }
            else if (held.Effect.Trigger.Kind == TriggerKind.PERIODIC)
            {
                HoldsAPeriodic = true;
            }

            StatsDependOnLiveState |=
                held.Effect.Condition is not null || held.Effect.ValueScale is not null;
        }

        StandingEffects = standing;
    }

    /// <summary>
    /// Whether this actor holds any <c>PERIODIC</c> at all — slot 3's early-out.
    /// </summary>
    /// <remarks>
    /// <c>TriggerRegistry.PeriodicDue</c> allocates a set, a list and a sort per call, and slot 3
    /// asks it once per actor on every one of 1800 ticks. An actor holding twenty perks and no
    /// periodic paid all of that to be told nothing was due.
    /// </remarks>
    internal bool HoldsAPeriodic { get; private set; }

    /// <summary>The actor as it entered the fight. Never changes.</summary>
    internal ActorPlan Plan { get; }

    /// <inheritdoc />
    public string Id => Plan.Id;

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 Assigned by <see cref="BattleSimulation"/> and not read from the plan after construction: a
    /// summon takes <em>"the end of the enemy index list"</em> (`05` §3.1), which is a roster fact.
    /// </remarks>
    public int Index { get; }

    /// <summary>The `05` §7 log id — what a <see cref="CombatEvent"/> carries in its actor slots.</summary>
    internal byte LogId { get; }

    /// <inheritdoc />
    public BattleSide Side => Plan.Side;

    /// <inheritdoc />
    public EffectActorKind Kind => Plan.Kind;

    /// <inheritdoc />
    public bool IsElite => Plan.IsElite;

    /// <inheritdoc />
    public bool IsBoss => Plan.IsBoss;

    /// <inheritdoc />
    public bool IsSummon => Plan.IsSummon;

    /// <inheritdoc />
    public string? OwnerId => Plan.OwnerId;

    /// <summary>
    /// 🔒 `18` §8's aggregated block as of the last aggregation — <b>this actor's final stats</b>,
    /// re-read at fire time by everything that fires (`05` §3.1 slot 4a reads ASPD here).
    /// </summary>
    internal ActorStats Stats { get; private set; }

    /// <summary>
    /// 🔒 `18` §2.4's <c>STAT_COPY</c> reading — <em>"the start-of-tick snapshot, so mutual copies
    /// cannot recurse"</em>. Frozen at the top of every tick, before slot 1.
    /// </summary>
    internal ActorStats StartOfTickStats { get; private set; } = null!;

    /// <summary>`18` §2.4's per-actor flow state — charges, saves, buckets, multipliers.</summary>
    internal CombatFlowState Flow { get; }

    /// <summary>
    /// 🔒 `05` §3.1 slot 4 — seconds until this actor's next basic attack. <c>0</c> for every
    /// battle-opening actor (pre-tick 0a: <em>"the first basic attack lands on tick 0"</em>), and
    /// <c>1.0 / ASPD</c> for a summon, which <em>"never attacks on its spawn tick"</em>.
    /// </summary>
    internal double AttackCooldown { get; set; }

    /// <summary>
    /// 🔒 `05` §3.2's live <c>targetPriority</c>. Opens at <see cref="ActorPlan.TargetPriority"/>
    /// and is rewritten by `18` §2.4's <c>SET_TARGET_PRIORITY</c>.
    /// </summary>
    internal double TargetPriority { get; set; }

    /// <summary>
    /// 🔒 Every effect instance this actor holds, kept in `05` §3.1's <b>ascending effect-id
    /// order</b>, tie-broken by instance id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The actor → instances map is the loop's, deliberately: <c>TriggerRegistry</c> is actor-blind
    /// <em>"because `05` §3.1's actor order is the loop's to maintain as actors spawn and die, and a
    /// second copy of it here would be a second thing to keep in step"</em>.
    /// </para>
    /// <para>
    /// 🔒 <b>Sorted on insert, not on read.</b> `05` §3.1 keys on ascending effect-id order at every
    /// trigger moment, and there are six or more per swing plus one after every HP change — M2-09's
    /// hits and M2-10's DoT ticks included. Re-sorting per moment is the same answer computed
    /// thousands of times from a list that only changes when an effect is registered. The order is
    /// still the rule and not the caller's: <see cref="AddInstance"/> imposes it.
    /// </para>
    /// </remarks>
    internal List<HeldInstance> Instances { get; } = new();

    /// <summary>
    /// The <c>PERIODIC</c> subset of <see cref="Instances"/>, in the same order — slot 3's candidate
    /// list. See <see cref="HoldsAPeriodic"/>.
    /// </summary>
    internal List<EffectInstanceId> Periodics { get; } = new();

    /// <summary>
    /// Records a registered instance, in `05` §3.1's ascending effect-id order.
    /// </summary>
    /// <param name="id">The instance id it was registered under.</param>
    /// <param name="effect">The authored effect. It carries a trigger, or it would not be registered.</param>
    /// <remarks>
    /// 🔒 The tie-break is the instance id, and it is load-bearing for
    /// <c>TriggerRegistry.Ordered</c>'s reason: one actor can hold two copies of one effect (`18`
    /// §3), and a stable sort would then let registration order decide which ward lands first.
    /// </remarks>
    internal void AddInstance(EffectInstanceId id, EffectDefinition effect)
    {
        var kind = effect.Trigger!.Kind;
        var held = new HeldInstance(id, effect.Id, kind);

        var at = Instances.Count;
        while (at > 0 && HeldInstance.Order.Compare(Instances[at - 1], held) > 0)
        {
            at--;
        }

        Instances.Insert(at, held);

        if (kind != TriggerKind.PERIODIC)
        {
            return;
        }

        HoldsAPeriodic = true;
        Periodics.Clear();
        foreach (var instance in Instances)
        {
            if (instance.Kind == TriggerKind.PERIODIC)
            {
                Periodics.Add(instance.Id);
            }
        }
    }

    /// <inheritdoc />
    public double CurrentHp => _currentHp;

    /// <inheritdoc />
    public double MaxHp => Stats[StatId.MAX_HP];

    /// <summary>
    /// 🔒 `05` §3's timeout rule — <em>"the side with the higher <b>remaining HP fraction</b>
    /// wins"</em>. <c>0</c> once dead; <c>1</c> at full health.
    /// </summary>
    internal double HpFraction =>
        MaxHp <= 0.0 ? 0.0 : StatRounding.Round(_currentHp / MaxHp);

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 <b>Pets are always alive.</b> `05` §3.2: <em>"Pets cannot be targeted or killed."</em> The
    /// rule is stated here rather than left to every reader, because <c>BattleRoster</c> filters on
    /// <c>Kind</c> while `05` §3.1's slot 6 filters on HP, and a pet with a stat block whose MAX_HP
    /// happened to be 0 would otherwise be a corpse the death sweep tried to bury every tick.
    /// </remarks>
    public bool IsAlive => Kind == EffectActorKind.PET || (_currentHp > 0.0 && !Removed);

    /// <summary>Whether slot 6 has already fired this actor's <c>ON_DEATH</c> and removed it.</summary>
    internal bool DeathResolved { get; private set; }

    /// <summary>Whether the actor has left the fight — removed at slot 6, or despawned.</summary>
    internal bool Removed { get; private set; }

    /// <summary>
    /// 🔒 Whether <c>CLEAR_SUMMONS</c> despawned this actor — <b>distinct from dead</b>, and the
    /// distinction is load-bearing at the timeout.
    /// </summary>
    /// <remarks>
    /// `18` §2.4: <em>"despawned ≠ killed: no <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death
    /// explosions, no rewards"</em> — so a despawned summon keeps its HP rather than dropping to 0.
    /// `05` §3's timeout is decided on the side's <em>remaining</em> HP fraction, and an actor that
    /// left the fight has none: counting a killed one at <c>0/max</c> is right, and counting a
    /// despawned one at <c>max/max</c> would hand the timeout to the side whose summons were
    /// cleared. Without this flag the two are indistinguishable, because both set
    /// <see cref="Removed"/> and <see cref="DeathResolved"/>.
    /// </remarks>
    internal bool Despawned { get; private set; }

    /// <summary>
    /// Whether this actor's `18` §8 aggregation reads live state — a <c>valueScale</c> or a `18` §4
    /// condition — and must therefore be re-run every tick rather than only when marked stale.
    /// </summary>
    internal bool StatsDependOnLiveState { get; set; }

    /// <summary>
    /// 🔒 The actor's <b>untriggered</b> effects — the standing modifiers `18` §8 aggregates. Fixed
    /// for the fight, because <see cref="ActorPlan.Effects"/> is.
    /// </summary>
    /// <remarks>
    /// Materialised once rather than filtered per aggregation: <see cref="BattleSimulation"/>
    /// re-aggregates a state-dependent actor on every one of 1800 ticks, and rebuilding a constant
    /// list each time was measurable against `05`'s &lt; 5 ms budget. See
    /// <c>BattleSimulation.RefreshStats</c> for why triggered effects are excluded.
    /// </remarks>
    internal IReadOnlyList<EffectDefinition> StandingEffects { get; }

    /// <summary>
    /// Whether this actor's `18` §8 aggregation is out of date. See
    /// <see cref="BattleSimulation"/> for when it is raised and why the re-aggregation is not
    /// unconditional.
    /// </summary>
    internal bool StatsAreStale { get; private set; } = true;

    /// <inheritdoc />
    public int StatusStacks(string statusId) => _timeline.StacksOn(this, statusId);

    /// <summary>
    /// Replaces the aggregated block. Called by <see cref="BattleSimulation"/> only, at pre-tick 0a
    /// and whenever <see cref="StatsAreStale"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>HP is clamped, never scaled.</b> `18` §9.1's <c>CP_GLASS_HEART</c> re-bases Max HP
    /// mid-fight and `05` §4.1 is explicit that the ward cap follows the new value; nothing in `05`
    /// or `18` says current HP moves with it, so a shrinking Max HP clips current HP down to it and a
    /// growing one leaves current HP alone. Scaling it would heal the actor for free every time a
    /// buff landed.
    /// </remarks>
    /// <returns>
    /// 🔒 Whether the new block <b>clipped current HP</b>. That is an HP <em>decrease</em>, so
    /// <see cref="BattleSimulation"/> owes it `05` §3.1's phase check and <c>ON_LOW_HP</c> exactly as
    /// it owes them to a hit — a boss re-based below 66% by <c>CP_GLASS_HEART</c> must enter phase 2
    /// there and not on whatever unrelated swing lands next.
    /// </returns>
    internal bool SetStats(ActorStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        Stats = stats;
        StatsAreStale = false;

        var maxHp = stats[StatId.MAX_HP];
        if (_currentHp <= maxHp)
        {
            return false;
        }

        // Through SetCurrentHp, not by assignment: the NaN guard, the 4-dp rounding and the
        // negative-zero normalisation are that method's and must not have a second, weaker path.
        SetCurrentHp(maxHp);

        return true;
    }

    /// <summary>Marks the aggregation out of date — any change to the effects that feed it.</summary>
    internal void InvalidateStats() => StatsAreStale = true;

    /// <summary>Freezes <see cref="StartOfTickStats"/> — the top of every tick, before slot 1.</summary>
    internal void FreezeStartOfTick() => StartOfTickStats = Stats;

    /// <summary>
    /// Sets current HP, clamped to <c>0..MaxHp</c> and rounded to `05` §1.1's four decimals.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The clamp is here and nowhere else.</b> <c>CombatLog.Complete</c> refuses a negative
    /// <c>HeroHpRemaining</c> on the grounds that <em>"`05` §4's damage pipeline and §4.3's healing
    /// keep HP at or above 0 by construction"</em> — this method is that construction. Letting HP go
    /// negative would also make <see cref="HpFraction"/> negative and hand `05` §3's timeout to the
    /// wrong side.
    /// </remarks>
    internal void SetCurrentHp(double value)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                $"HP of '{Id}' was set to NaN. `05` §4's pipeline produces real quantities; a NaN is an " +
                "overflow or a divide-by-zero upstream, and it compares false against every bound so " +
                "the clamp below would pass it straight through.");
        }

        // Math.Max on the ceiling, because Math.Clamp throws when min > max: `05` §1 declares no
        // floor under MAX_HP, so a STAT_ADD_PCT below -100% reaches here, and an argument-order
        // exception out of the BCL would report as a bug in this method rather than in the
        // aggregation that produced the block.
        _currentHp = StatRounding.Round(Math.Clamp(value, 0.0, Math.Max(0.0, MaxHp)));
    }

    /// <summary>Records that slot 6 has resolved this actor's death and removed it.</summary>
    internal void MarkDeathResolved()
    {
        DeathResolved = true;
        Removed = true;
    }

    /// <summary>
    /// `18` §2.4's <c>CLEAR_SUMMONS</c> — <em>"despawned ≠ killed: no <c>ON_DEATH</c>, no
    /// <c>ON_KILL</c>, no on-death explosions, no rewards."</em>
    /// </summary>
    internal void Despawn()
    {
        Removed = true;
        DeathResolved = true;
        Despawned = true;
    }

    /// <summary>The actor as a failure message reads.</summary>
    public override string ToString() =>
        $"{Id} (#{Index.ToString(CultureInfo.InvariantCulture)}, {Kind}, {Side}, " +
        $"{_currentHp.ToString("R", CultureInfo.InvariantCulture)}/" +
        $"{MaxHp.ToString("R", CultureInfo.InvariantCulture)} HP)";
}
