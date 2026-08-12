using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

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
    }

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

    /// <summary>The `18` §3 instance ids of every effect this actor holds, in registration order.</summary>
    /// <remarks>
    /// 🔒 The actor → instances map is the loop's, deliberately: <c>TriggerRegistry</c> is actor-blind
    /// <em>"because `05` §3.1's actor order is the loop's to maintain as actors spawn and die, and a
    /// second copy of it here would be a second thing to keep in step"</em>.
    /// </remarks>
    internal List<EffectInstanceId> Instances { get; } = new();

    /// <inheritdoc />
    public double CurrentHp => _currentHp;

    /// <inheritdoc />
    public double MaxHp => Stats[StatId.MAX_HP];

    /// <summary>
    /// 🔒 `05` §3's timeout rule — <em>"the side with the higher <b>remaining HP fraction</b>
    /// wins"</em>. <c>0</c> once dead; <c>1</c> at full health.
    /// </summary>
    internal double HpFraction =>
        MaxHp <= 0.0 ? 0.0 : Math.Round(_currentHp / MaxHp, StatRounding.Decimals);

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
    internal void SetStats(ActorStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        Stats = stats;
        StatsAreStale = false;

        var maxHp = stats[StatId.MAX_HP];
        if (_currentHp > maxHp)
        {
            _currentHp = maxHp;
        }
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

        _currentHp = Math.Round(Math.Clamp(value, 0.0, MaxHp), StatRounding.Decimals) + 0.0;
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
    }

    /// <summary>The actor as a failure message reads.</summary>
    public override string ToString() =>
        $"{Id} (#{Index.ToString(CultureInfo.InvariantCulture)}, {Kind}, {Side}, " +
        $"{_currentHp.ToString("R", CultureInfo.InvariantCulture)}/" +
        $"{MaxHp.ToString("R", CultureInfo.InvariantCulture)} HP)";
}
