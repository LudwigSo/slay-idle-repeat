using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// One registered effect instance on one actor, with the two things every trigger moment needs of it
/// without a registry lookup: which kind it fires on, and where it sorts.
/// </summary>
/// <param name="Id">The instance id it is registered under.</param>
/// <param name="EffectId">The authored effect id — the ordinal sort key.</param>
/// <param name="Kind">The trigger kind, so a moment can skip it without resolving the instance.</param>
internal readonly record struct HeldInstance(EffectInstanceId Id, string EffectId, TriggerKind Kind)
{
    /// <summary>Ordinal effect-id order, tie-broken by the instance id.</summary>
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
/// One actor's live state for the length of a fight — the mutable half of <see cref="ActorPlan"/>, and
/// the <see cref="IEffectActorView"/> every condition and target reads.
/// </summary>
/// <remarks>
/// <para>
/// One view of one battle, not two: the loop, the conditions, the targets and the ops all read the
/// same object, so a stat aggregation and a roster scan can never disagree about who is alive.
/// </para>
/// <para>
/// <see cref="IsAlive"/> goes false the moment HP reaches 0, not when death is resolved: an actor whose
/// HP reaches 0 stops acting and being targetable at that moment, and only its death resolution
/// (<c>ON_DEATH</c>, removal) waits for the death-resolution slot. So an enemy killed mid-tick is
/// already out of the enemy list for the next actor's target selection, and <see cref="DeathResolved"/>
/// is what the death-resolution slot uses to find the bodies it has not buried.
/// </para>
/// <para>
/// A stateful class, on <c>CombatLog</c>'s precedent: a 1800-tick loop has to keep HP and a cooldown
/// somewhere. It is per-battle, owned by exactly one caller, never shared and never static.
/// </para>
/// </remarks>
internal sealed class BattleActor : IEffectActorView
{
    private readonly IStatusTimeline _timeline;

    private double _currentHp;

    /// <summary>Builds the live state for one actor from its plan.</summary>
    /// <param name="plan">The actor as it enters the fight.</param>
    /// <param name="timeline">The status store — <see cref="StatusStacks"/> is a reading of it, not a second copy.</param>
    internal BattleActor(ActorPlan plan, IStatusTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeline);

        Plan = plan;
        _timeline = timeline;
        Index = plan.Index;
        LogId = plan.LogId;
        TargetPriority = plan.TargetPriority;

        // Before any effect aggregation has run, an actor's stats ARE its base block, and its
        // post-step-7 Max HP is that block's — set here so no window exists where Aggregated is
        // null and a reader has to decide what to do about it.
        Aggregated = new AggregatedStats(
            plan.BaseStats, plan.BaseStats[StatId.MAX_HP], Array.Empty<string>());
        Flow = new CombatFlowState();
        _currentHp = plan.BaseStats[StatId.MAX_HP];

        var standing = new List<EffectDefinition>(plan.Effects.Count);
        foreach (var held in plan.Effects)
        {
            // 🔴 An ABSENT trigger and an authored ALWAYS one are the same thing — the DSL's own
            // default says so — and reading only the absent half is not a narrower rule but a silent
            // one: every gear stat, affix and set bonus is synthesised with an explicit ALWAYS, so
            // that half of the loadout reached the aggregation pass and none of it reached a fight.
            var subjects = ConditionSubjects.Of(held.Effect.Condition);
            var contextGated = subjects.ReadsTarget || subjects.ReadsAttacker;

            if (EffectDefaults.IsAlwaysActive(held.Effect))
            {
                standing.Add(held.Effect);

                // The conditional standing-effect bucket's early-out: only a STANDING stat op with
                // a target- or attacker-reading gate can make an attack's per-pair re-aggregation
                // differ from the ambient one, so the pair pass is skipped for every actor without
                // one. Stat family only, because aggregation reads nothing else.
                HoldsContextGatedStanding |=
                    held.Effect.Family == EffectOpFamily.STAT && contextGated;
            }
            else if (held.Effect.Trigger!.Kind == TriggerKind.PERIODIC)
            {
                HoldsAPeriodic = true;
            }

            // A context-gated condition is constant-inactive in every ambient context (its subject
            // is never present there), so it cannot make the per-tick answer change — only an
            // ambient condition or a value scale can, and re-aggregating nine actors 1800 times
            // for a constant answer was the whole of the fight's time budget.
            StatsDependOnLiveState |=
                (held.Effect.Condition is not null && !contextGated) ||
                held.Effect.ValueScale is not null;
        }

        StandingEffects = standing;
    }

    /// <summary>Whether this actor holds any <c>PERIODIC</c> at all — the tick loop's early-out.</summary>
    /// <remarks>
    /// Periodic-due resolution allocates a set, a list and a sort per call, asked once per actor on
    /// every tick; an actor holding twenty perks and no periodic would otherwise pay all of that to be
    /// told nothing was due.
    /// </remarks>
    internal bool HoldsAPeriodic { get; private set; }

    /// <summary>The actor as it entered the fight. Never changes.</summary>
    internal ActorPlan Plan { get; }

    /// <inheritdoc />
    public string Id => Plan.Id;

    /// <inheritdoc />
    /// <remarks>
    /// Assigned by <see cref="BattleSimulation"/>, not read from the plan after construction: a summon
    /// takes the end of the enemy index list, which is a roster fact.
    /// </remarks>
    public int Index { get; }

    /// <summary>The combat-log id — what a <see cref="CombatEvent"/> carries in its actor slots.</summary>
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

    /// <summary>The whole result of the last stat aggregation, not just its final block.</summary>
    /// <remarks>
    /// <para>
    /// Holding the record rather than just the final block is what makes
    /// <see cref="PostMultiplierMaxHp"/> reachable at all — a consumer that kept only the final stats
    /// and discarded the wrapper would cap every ward at whatever the final Max HP happened to be.
    /// </para>
    /// <para>
    /// It is replaced on every re-aggregation, never cached at battle start: a boss can gain a stat
    /// multiplier mid-fight, so its post-step-7 Max HP is not a battle constant and a ward cap taken
    /// once at tick 0 would be wrong for the rest of a long boss fight.
    /// </para>
    /// </remarks>
    internal AggregatedStats Aggregated { get; private set; }

    /// <summary>The aggregated block as of the last aggregation — this actor's final stats, re-read at fire time.</summary>
    internal ActorStats Stats => Aggregated.Final;

    /// <summary>
    /// The ward-cap basis — Max HP as it stood after step-7 stat aggregation (post-multiplier,
    /// pre-<c>STAT_SET</c>), re-read from <see cref="Aggregated"/> on every call.
    /// </summary>
    /// <remarks>
    /// A property over the live record rather than a stored double, deliberately: a field would be one
    /// assignment away from being set once at battle start and never refreshed.
    /// </remarks>
    internal double PostMultiplierMaxHp => Aggregated.PostMultiplierMaxHp;

    /// <summary>The absorb pool — one per actor, made of segments.</summary>
    internal WardPool Wards { get; } = new();

    /// <summary>
    /// A <c>STAT_COPY</c> reading — the start-of-tick snapshot, so mutual copies cannot recurse.
    /// Frozen at the top of every tick, before status timers advance.
    /// </summary>
    internal ActorStats StartOfTickStats { get; private set; } = null!;

    /// <summary>Per-actor flow state — charges, saves, buckets, multipliers.</summary>
    internal CombatFlowState Flow { get; }

    /// <summary>
    /// Seconds until this actor's next basic attack. <c>0</c> for every battle-opening actor (the
    /// first basic attack lands on tick 0), and <c>1.0 / ASPD</c> for a summon, which never attacks on
    /// its spawn tick.
    /// </summary>
    internal double AttackCooldown { get; set; }

    /// <summary>The live target priority. Opens at <see cref="ActorPlan.TargetPriority"/> and can be rewritten mid-fight.</summary>
    internal double TargetPriority { get; set; }

    /// <summary>Every effect instance this actor holds, in ascending effect-id order, tie-broken by instance id.</summary>
    /// <remarks>
    /// <para>
    /// The actor → instances map is the loop's, deliberately: the trigger registry is actor-blind,
    /// because the loop's actor order is its own to maintain as actors spawn and die, and a second copy
    /// here would be a second thing to keep in step.
    /// </para>
    /// <para>
    /// Sorted on insert, not on read: there are six or more trigger moments per swing plus one after
    /// every HP change, and re-sorting per moment recomputes the same order thousands of times from a
    /// list that only changes when an effect is registered. The order is still the rule and not the
    /// caller's — <see cref="AddInstance"/> imposes it.
    /// </para>
    /// </remarks>
    internal List<HeldInstance> Instances { get; } = new();

    /// <summary>
    /// The <c>PERIODIC</c> subset of <see cref="Instances"/>, in the same order — the periodic-due
    /// candidate list. See <see cref="HoldsAPeriodic"/>.
    /// </summary>
    internal List<EffectInstanceId> Periodics { get; } = new();

    /// <summary>This actor's live fired stat ops, keyed by the firing effect's id.</summary>
    /// <remarks>
    /// Empty for almost every actor in almost every fight: only a target of a triggered stat mechanic
    /// ever populates it, which is why the refresh path early-outs on the count before walking it.
    /// </remarks>
    internal Dictionary<string, TriggeredStatInstance> TriggeredStatFirings { get; } =
        new(StringComparer.Ordinal);

    /// <summary>Records a registered instance, in ascending effect-id order.</summary>
    /// <param name="id">The instance id it was registered under.</param>
    /// <param name="effect">The authored effect. It carries a trigger, or it would not be registered.</param>
    /// <remarks>
    /// The tie-break is the instance id: one actor can hold two copies of one effect, and a stable sort
    /// would then let registration order decide which one lands first.
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

    /// <summary>The timeout rule's HP fraction — the side with the higher one wins. <c>0</c> once dead; <c>1</c> at full health.</summary>
    internal double HpFraction =>
        MaxHp <= 0.0 ? 0.0 : StatRounding.Round(_currentHp / MaxHp);

    /// <inheritdoc />
    /// <remarks>
    /// Pets are always alive — they cannot be targeted or killed. Stated here rather than left to every
    /// reader, because the roster filters on <c>Kind</c> while the death sweep filters on HP, and a pet
    /// with a zero-MAX_HP stat block would otherwise be a corpse the death sweep tried to bury every tick.
    /// </remarks>
    public bool IsAlive => Kind == EffectActorKind.PET || (_currentHp > 0.0 && !Removed);

    /// <summary>Whether the death sweep has already fired this actor's <c>ON_DEATH</c> and removed it.</summary>
    internal bool DeathResolved { get; private set; }

    /// <summary>Whether the actor has left the fight — removed at death resolution, or despawned.</summary>
    internal bool Removed { get; private set; }

    /// <summary>Whether <c>CLEAR_SUMMONS</c> despawned this actor — distinct from dead, and the distinction matters at the timeout.</summary>
    /// <remarks>
    /// Despawned ≠ killed: no <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death explosions, no rewards —
    /// so a despawned summon keeps its HP rather than dropping to 0. The timeout is decided on the
    /// side's remaining HP fraction, and a despawned actor has none to count: counting it at
    /// <c>max/max</c> would hand the timeout to whichever side had its summons cleared. Without this
    /// flag the two are indistinguishable, because both set <see cref="Removed"/> and <see cref="DeathResolved"/>.
    /// </remarks>
    internal bool Despawned { get; private set; }

    /// <summary>Whether this actor's aggregation reads live state and must be re-run every tick rather than only when marked stale.</summary>
    internal bool StatsDependOnLiveState { get; set; }

    /// <summary>
    /// Whether any standing stat op carries a context gate — the flag behind the per-pair
    /// re-aggregation an attack resolution asks for. False for every actor whose swings must stay
    /// byte-identical to the ambient block's.
    /// </summary>
    internal bool HoldsContextGatedStanding { get; private set; }

    /// <summary>The actor's untriggered effects — the standing modifiers aggregation reads. Fixed for the fight.</summary>
    /// <remarks>
    /// Materialised once rather than filtered per aggregation: a state-dependent actor re-aggregates on
    /// every one of 1800 ticks, and rebuilding a constant list each time was measurable against the
    /// fight's time budget.
    /// </remarks>
    internal IReadOnlyList<EffectDefinition> StandingEffects { get; }

    /// <summary>Whether this actor's aggregation is out of date.</summary>
    internal bool StatsAreStale { get; private set; } = true;

    /// <inheritdoc />
    public int StatusStacks(string statusId) => _timeline.StacksOn(this, statusId);

    /// <summary>
    /// Replaces the aggregated block. Called by <see cref="BattleSimulation"/> only, at battle start
    /// and whenever <see cref="StatsAreStale"/>.
    /// </summary>
    /// <remarks>
    /// HP is clamped, never scaled: a re-based Max HP moves the ward cap with it, but nothing says
    /// current HP should move too, so a shrinking Max HP clips current HP down to it and a growing one
    /// leaves current HP alone. Scaling it would heal the actor for free every time a buff landed.
    /// </remarks>
    /// <returns>
    /// Whether the new block clipped current HP. That is an HP decrease, so the caller owes it the
    /// phase check and <c>ON_LOW_HP</c> exactly as it owes them to a hit.
    /// </returns>
    internal bool SetStats(AggregatedStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        Aggregated = stats;
        StatsAreStale = false;

        var maxHp = stats.Final[StatId.MAX_HP];
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

    /// <summary>Freezes <see cref="StartOfTickStats"/> — the top of every tick, before status timers advance.</summary>
    internal void FreezeStartOfTick() => StartOfTickStats = Stats;

    /// <summary>Sets current HP, clamped to <c>0..MaxHp</c> and rounded to four decimals.</summary>
    /// <remarks>
    /// The clamp is here and nowhere else: HP is kept at or above 0 by construction, and this is that
    /// construction. Letting HP go negative would also make <see cref="HpFraction"/> negative and hand
    /// the timeout to the wrong side.
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

        // Math.Max on the ceiling, because Math.Clamp throws when min > max: there is no floor under
        // MAX_HP, so a percent reduction below -100% can reach here, and an argument-order exception
        // out of the BCL would report as a bug in this method rather than in the aggregation upstream.
        _currentHp = StatRounding.Round(Math.Clamp(value, 0.0, Math.Max(0.0, MaxHp)));
    }

    /// <summary>Records that the death sweep has resolved this actor's death and removed it.</summary>
    internal void MarkDeathResolved()
    {
        DeathResolved = true;
        Removed = true;
    }

    /// <summary><c>CLEAR_SUMMONS</c> — despawned ≠ killed: no <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death explosions, no rewards.</summary>
    internal void Despawn()
    {
        Removed = true;
        DeathResolved = true;
        Despawned = true;
    }

    /// <summary>The roster's own actor behind an effect view — the one statement of that cast and of its diagnosis.</summary>
    /// <remarks>
    /// A battle's roster is built out of <see cref="BattleActor"/>s exclusively; a foreign view reaching
    /// a combat rule would mean a second roster exists, which is exactly the defect that would let two
    /// roster-scanning conditions disagree about the same battle.
    /// </remarks>
    /// <param name="view">The view a combat rule was handed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The view is not this battle's actor type.</exception>
    internal static BattleActor Of(IEffectActorView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return view as BattleActor ??
            throw new InvalidOperationException(
                $"A {view.GetType().Name} reached `05` §4's combat rules rather than a " +
                $"{nameof(BattleActor)}. A battle has one roster and one view of it " +
                "(IEffectActorView); a second implementation means the HP, the ward pool and the " +
                "flow state being read and written belong to a different fight — which is exactly " +
                "how ENEMY_COUNT and ALL_ENEMIES come to disagree about the same one.");
    }

    /// <summary>The actor as a failure message reads.</summary>
    public override string ToString() =>
        $"{Id} (#{Index.ToString(CultureInfo.InvariantCulture)}, {Kind}, {Side}, " +
        $"{_currentHp.ToString("R", CultureInfo.InvariantCulture)}/" +
        $"{MaxHp.ToString("R", CultureInfo.InvariantCulture)} HP)";
}
