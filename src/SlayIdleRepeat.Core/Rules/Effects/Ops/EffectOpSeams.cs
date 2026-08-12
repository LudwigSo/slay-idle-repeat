using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 The six seams `18` §2's ops resolve through, and the strict default set that refuses every one
/// of them by name.
/// </summary>
/// <param name="Values">`18` §1.1's <c>valueScale</c> — <b>M2-06</b>.</param>
/// <param name="Attack">`05` §4 / §4.1 / §4.3 — <b>M2-09</b>.</param>
/// <param name="Statuses">`05` §5 — <b>M2-10</b>.</param>
/// <param name="Flow">`18` §2.4's actor flow state — <b>M2-08</b>.</param>
/// <param name="Stats">`18` §2.4's <c>STAT_COPY</c> reading — <b>M2-08</b> over M2-07's block.</param>
/// <param name="RunQueue">`18` §2.5's queue — emitted by <b>M2-08</b>, drained by <b>M3</b>.</param>
/// <remarks>
/// <para>
/// 🔒 <b>Why the ops own no state and reach nothing directly.</b> R17 puts
/// <c>Rules.Effects</c> at the bottom of the intra-<c>Rules</c> layering
/// (<c>Rules.Combat → Rules.Stats → Rules.Effects</c>), so nothing here may name
/// <c>ActorStats</c>, <c>StatCaps</c>, <c>CombatEvent</c> or <c>CombatLog</c>. Every op therefore
/// computes its number from the DSL and hands it to a seam whose implementation lives one layer up.
/// That is not a workaround for the layering — it is what makes `05` §3.1 step 7's
/// <em>"appended at the moment each state change occurs"</em> hold: the op calls, the simulator
/// logs, and no plan is accumulated and flushed later.
/// </para>
/// <para>
/// 🔒 <b>Every default throws, and none no-ops.</b> Same shape M2-07 used for
/// <c>StatAggregationSeams.Strict</c>, for the same reason: a silent no-op turns "M2-09 has not
/// landed" into "this perk does nothing", which is a balance bug rather than an error, and the
/// balance harness would attribute it to the content. Each message names the task that owns the
/// member.
/// </para>
/// </remarks>
internal sealed record EffectOpSeams(
    IScaledValueReader Values,
    IAttackPipeline Attack,
    IStatusEngine Statuses,
    ICombatFlowSink Flow,
    IResolvedStatReader Stats,
    IRunEffectQueue RunQueue)
{
    /// <summary>
    /// 🔒 The seam set M2-03 ships: `18` §1.1's unscaled authored value, and a refusal — naming the
    /// owning task — for everything the combat engine has not yet built.
    /// </summary>
    internal static EffectOpSeams Strict { get; } = new(
        AuthoredScaledValue.Instance,
        UnwiredAttackPipeline.Instance,
        UnwiredStatusEngine.Instance,
        UnwiredCombatFlow.Instance,
        UnwiredStatReader.Instance,
        UnwiredRunQueue.Instance);
}

/// <summary>
/// 🔒 `18` §1.1 — the effect's <c>value</c> after <c>valueScale</c>, and <b>nothing else</b>. The
/// seam <b>M2-06</b> implements.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The M2-03 / M2-06 split, stated once so the two tasks cannot both implement it.</b> An
/// effect's magnitude is two multiplications in a fixed order:
/// </para>
/// <code>
/// authored value  ──× steps──▶  scaled value  ──× basis──▶  the number the op applies
///                   (18 §1.1)                   (18 §2.2)
///                    M2-06                        M2-03
/// </code>
/// <para>
/// <c>valueScale</c> reads live state (<em>"any condition function from §4"</em>) and is M2-06's;
/// <c>valueMode</c> says what the result is <em>a multiple of</em>, which is a property of the op —
/// `05` §4.2 makes <c>DAMAGE</c>'s value an <c>AttackMultiplier</c> and `18` §2.2 makes
/// <c>HEAL_LEECH</c>'s a fraction of damage dealt — and is M2-03's. Composing them in the other
/// order would scale a fraction-of-Max-HP by a step count taken against the pre-scaled value.
/// </para>
/// </remarks>
internal interface IScaledValueReader
{
    /// <summary>`18` §1.1's <c>effectiveValue = value × steps</c>, before any `18` §2.2 value mode.</summary>
    double ScaledValue(EffectDefinition effect);
}

/// <summary>What one `05` §4 <c>ResolveAttack</c> produced.</summary>
/// <param name="Missed">The defender dodged (`05` §4 step 1). Everything else is then zero/false.</param>
/// <param name="Crit">The hit critted (step 4).</param>
/// <param name="Blocked">The hit was blocked and halved (step 5).</param>
/// <param name="Basis">
/// 🔒 `05` §4 step 8's <em>"on-damage basis"</em> — the post-mitigation, post-floor hit
/// <b>before</b> ward absorption. Lifesteal and thorns read this, not <see cref="HpLost"/>.
/// </param>
/// <param name="HpLost">What actually came off HP after absorption (step 9).</param>
internal readonly record struct AttackResolution(
    bool Missed, bool Crit, bool Blocked, double Basis, double HpLost);

/// <summary>
/// 🔒 `05` §4, §4.1 and §4.3 — the damage, ward and healing engine every `18` §2.2 op routes into.
/// The seam <b>M2-09</b> implements.
/// </summary>
/// <remarks>
/// <para>
/// `05` §4.2 is the routing table this interface exists to make executable, and each member below is
/// one of its rows. The ops decide <em>the number</em>; this decides <em>what happens to it</em> —
/// which is where dodge, mitigation, crit, block, DR, the floor and wards live, none of which the
/// DSL layer may know about.
/// </para>
/// </remarks>
internal interface IAttackPipeline
{
    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE</c>: <em>"the full <c>ResolveAttack</c> pipeline — dodge,
    /// mitigation, crit, block, DR, floor, wards"</em>.
    /// </summary>
    /// <param name="attacker">
    /// The source actor. `05` §4.2: <em>"the source's stats are used; a pet ability uses the hero's
    /// ATK but the <b>pet's</b> own CRIT (0 unless granted)"</em> — a rule about how M2-09 builds
    /// this actor's stat block, not one the DSL layer can apply.
    /// </param>
    /// <param name="defender">The actor taking the hit.</param>
    /// <param name="attackMultiplier">
    /// 🔒 `05` §4 / §4.2 — <em>"a DSL <c>DAMAGE</c> op invoking this pipeline: the op's <c>value</c>
    /// <b>is</b> the AttackMultiplier for that resolved attack"</em>. Not a damage amount.
    /// </param>
    /// <param name="sourceEffectId">
    /// The `18` §8 effect id, for the log and for `05` §4's ascending-effect-id orderings.
    /// </param>
    AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId);

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_TRUE</c>: <em>"bypasses everything: no dodge, mitigation, crit,
    /// block, DR, <c>DAMAGE_TAKEN_MULT</c>, floor or wards. HP is reduced directly; the phase check
    /// still runs; no lifesteal or thorns."</em>
    /// </summary>
    /// <param name="amount">The HP to remove, rounded to 4 dp (`05` §1.1).</param>
    void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId);

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_MAXHP_PCT</c>: <em>"no dodge, crit, block, mitigation or floor;
    /// <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> <b>do</b> apply; wards absorb; no lifesteal or
    /// thorns."</em>
    /// </summary>
    /// <param name="bypassesWards">
    /// 🔒 `05` §4.1's bypass list <b>(b)</b> — <em>"self-inflicted costs (cursed-perk drawbacks such
    /// as <c>CP_BLOOD_PRICE</c> / <c>CP_TIMEBOUND</c>) … wards must not silently delete perk
    /// drawbacks"</em>. The marker is `18` §7.5's reserved <c>drawback</c> tag; the op reads it
    /// (<see cref="EffectTagging"/>) and states it here rather than M2-09 re-deriving it, so that
    /// one reading of the tag serves the whole engine.
    /// </param>
    void DealMaxHpPctDamage(
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId);

    /// <summary>
    /// 🔒 `05` §4.3 — <c>Heal()</c>: <em>"healed = min(amount × target.HEALPct, MaxHP − HP)"</em>,
    /// with the overheal discarded unless an effect consumes it. `05` §4.2 routes both <c>HEAL</c>
    /// and <c>HEAL_LEECH</c> here, and <em>"<c>HEAL%</c> applies"</em>.
    /// </summary>
    /// <param name="amount">The pre-<c>HEAL%</c> amount, rounded to 4 dp.</param>
    void Heal(IEffectActorView target, double amount, string sourceEffectId);

    /// <summary>
    /// 🔒 `05` §4.2 — <c>SHIELD</c>: <em>"a ward grant (§4.1)"</em>.
    /// </summary>
    /// <param name="sourceCapPct">
    /// `18` §2.2 — <em>"the total unbroken ward contributed by that effect instance is clamped at
    /// <c>sourceCapPct × Max HP</c>"</em> (<c>PK_TRANSFUSION</c>, 20%). <c>null</c> where the effect
    /// authors none, which is not the same as 0 and must not be coerced to one.
    /// </param>
    /// <param name="sourceEffectId">
    /// 🔒 `05` §4.1 makes this part of the ward <em>segment</em> (<c>{amount, expiresAt?,
    /// sourceEffectId}</c>) — it is what a per-instance <paramref name="sourceCapPct"/> is measured
    /// against, so it is not merely a log label here.
    /// </param>
    void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId);

    /// <summary>
    /// 🔒 `05` §4.2 — <c>REFLECT</c>: <em>"adds to <c>THORN</c> for its duration"</em>. R4: `18`
    /// §2.2 gives the meaning (<em>"return a % of incoming damage"</em>) and `05` §4.2 gives the
    /// implementation; they are one rule, not a contradiction.
    /// </summary>
    /// <param name="fraction">The addition to <c>THORN</c>, which `05` §1 types as a fraction.</param>
    void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId);
}

/// <summary>
/// 🔒 `05` §5's twelve statuses — the six `18` §2.3 ops. The seam <b>M2-10</b> implements.
/// </summary>
internal interface IStatusEngine
{
    /// <summary>`18` §2.3 — <c>APPLY_STATUS</c>. <paramref name="potency"/> is the status's own X.</summary>
    void Apply(
        IEffectActorView target, string statusId, double potency, EffectDuration? duration,
        EffectStacking? stacking, string sourceEffectId);

    /// <summary>`18` §2.3 — <c>REMOVE_STATUS</c>, the <c>statusId</c> form.</summary>
    void Remove(IEffectActorView target, string statusId, string sourceEffectId);

    /// <summary>
    /// `18` §2.3 — <c>REMOVE_STATUS</c>, the <c>statusTag</c> form: every status on the target
    /// carrying the label. 🔒 A <see cref="StatusTag"/>, never an <see cref="AuthorTag"/>.
    /// </summary>
    void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId);

    /// <summary>`18` §2.3 — <c>EXTEND_STATUS</c>: <paramref name="seconds"/> added to a live status.</summary>
    void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId);

    /// <summary>`18` §2.3 — <c>IMMUNE_STATUS</c>.</summary>
    void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId);

    /// <summary>
    /// `18` §2.3 — <c>STATUS_POWER_PCT</c>: <em>"scale the potency of statuses <b>this actor
    /// applies</b>"</em> — outgoing.
    /// </summary>
    void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId);

    /// <summary>
    /// `18` §2.3 — <c>STATUS_DURATION_PCT</c>: <em>"scale duration of statuses <b>applied to this
    /// actor</b>"</em> — incoming. 🔒 The opposite direction from
    /// <see cref="ScaleOutgoingPower"/>, and the two rows of §2.3 are the only place that is said.
    /// </summary>
    void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId);
}

/// <summary>
/// 🔒 `18` §2.4's per-actor flow state — charges, summons, priorities and the two death saves. The
/// seam <b>M2-08</b> implements.
/// </summary>
internal interface ICombatFlowSink
{
    /// <summary>`18` §2.4 — <c>EXTRA_ATTACK</c>: <em>"perform an additional attack immediately"</em>.</summary>
    void ExtraAttack(IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>ATTACK_MULT_NEXT</c>. 🔒 `05` §4: the charges are
    /// <em>"consumed in ascending effect-id order"</em>, which is why the id travels with them.
    /// </summary>
    void GrantAttackMultiplierCharges(
        IEffectActorView holder, double multiplier, int charges, string sourceEffectId);

    /// <summary>`18` §2.4 — <c>FORCE_CRIT_NEXT</c>: the next <paramref name="charges"/> attacks always crit.</summary>
    void GrantForcedCritCharges(IEffectActorView holder, int charges, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>REDUCE_COOLDOWN</c>: <em>"reduce pet/boss ability cooldowns"</em> by
    /// <paramref name="fraction"/> of their remaining time.
    /// </summary>
    void ReduceCooldowns(IEffectActorView target, double fraction, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>SURVIVE_LETHAL</c>: arms a save that leaves the actor at
    /// <paramref name="hp"/>. 🔒 `05` §3.1: it <em>"fires at most its authored <c>once</c> count per
    /// battle"</em>, and `18` §3 is explicit that the actor never died, so no <c>ON_REVIVE</c>.
    /// </summary>
    void ArmSurviveLethal(IEffectActorView holder, double hp, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>REVIVE</c>: arms a return from 0 HP at <paramref name="hp"/>. Unlike
    /// <see cref="ArmSurviveLethal"/> this <b>does</b> fire <c>ON_REVIVE</c> (`18` §3).
    /// </summary>
    void ArmRevive(IEffectActorView holder, double hp, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 / §7.8 — <c>SUMMON</c>: spawn <paramref name="count"/> of
    /// <paramref name="archetype"/>, at most <paramref name="maxAlive"/> alive at once.
    /// </summary>
    void Summon(
        IEffectActorView summoner, string archetype, int count, int? maxAlive, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>CLEAR_SUMMONS</c>: <em>"despawn all living summons owned by the target. Despawned
    /// ≠ killed: no <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death explosions, no rewards."</em>
    /// </summary>
    void ClearSummons(IEffectActorView owner, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>SET_TARGET_PRIORITY</c>. `05` §3.2: the default is <c>0</c>, <c>-1</c>
    /// deprioritises and <c>+1</c> forces focus, ties broken by lowest current HP.
    /// </summary>
    void SetTargetPriority(IEffectActorView target, double priority, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>DAMAGE_TAKEN_MULT</c>. 🔒 `05` §4 step 6 takes the <b>product</b> of all
    /// active ones in ascending effect-id order, which is why they accumulate rather than replace.
    /// </summary>
    void AddDamageTakenMultiplier(
        IEffectActorView target, double multiplier, EffectDuration? duration, string sourceEffectId);

    /// <summary>
    /// `18` §2.4 — <c>STAT_COPY</c>'s write: <em>"onto the <b>holder</b> as a percent-bucket add for
    /// <c>duration</c>"</em>, i.e. a <c>STAT_ADD_PCT</c> that `18` §8 step 5 will pick up.
    /// </summary>
    /// <remarks>
    /// 🔒 R13 — the holder, <b>not</b> the effect's <c>target</c>. See
    /// <see cref="StatCopyOp"/> for the inversion and why it is not a bug to be fixed.
    /// </remarks>
    void AddPercentBucket(
        IEffectActorView holder, StatId stat, double fraction, EffectDuration? duration, string sourceEffectId);
}

/// <summary>
/// 🔒 `18` §2.4's <c>STAT_COPY</c> reading: an actor's <b>final resolved</b> stat, as of the
/// start-of-tick snapshot. The seam <b>M2-08</b> implements over M2-07's aggregated block.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The snapshot is this interface's contract, not the op's.</b> `18` §2.4:
/// <em>"reads the start-of-tick snapshot, so mutual copies cannot recurse"</em>. An op cannot
/// enforce that — it would have to know how the simulator stores stats — so the obligation is stated
/// here and pinned by
/// <c>StatCopyOpTests.Two_actors_copying_each_other_both_read_the_start_of_tick_snapshot</c>, which
/// runs the real op against a frozen reader and shows neither copy sees the other's output.
/// </para>
/// <para>
/// It is a <em>reading</em> seam rather than a direct use of M2-07's <c>ActorStats</c> because R17
/// forbids <c>Rules.Effects</c> naming <c>Rules.Stats</c>. <see cref="StatId"/> is
/// <c>Core.Content</c> and is below both.
/// </para>
/// </remarks>
internal interface IResolvedStatReader
{
    /// <summary>The actor's post-`18` §8 value for a stat, from the start-of-tick snapshot.</summary>
    double FinalStat(IEffectActorView actor, StatId stat);

    /// <summary>
    /// `18` §2.4's <c>HIGHEST_PCT_BONUS</c> — <em>"whichever stat carries the largest percent bucket
    /// at copy time"</em> (Cogitator's Recalibrate, `17` §7).
    /// </summary>
    StatId HighestPercentBonusStat(IEffectActorView actor);
}

/// <summary>
/// 🔒 `18` §2.5 — the queue a combat trigger's run/board op is appended to. <b>M2-08</b> translates
/// each call into `05` §7's <c>RunEffectQueued</c>; <b>M3</b> drains it.
/// </summary>
/// <remarks>
/// <para>
/// `18` §2.5: <em>"these are resolved by the run controller, never by the combat simulator … the
/// simulator still never resolves it: it appends a <c>RunEffectQueued</c> event to the combat log
/// and the run controller applies the queued ops in log order when the battle resolves."</em>
/// </para>
/// <para>
/// 🔒 <b>This seam is where "declared but not resolved" is made mechanical.</b> The thirteen §2.5
/// ops have no resolver in M2 and are not supposed to get one — A4. What they do have is a boundary:
/// they must be well-formed, they must validate, and reaching one from a combat trigger must produce
/// exactly this call and no mutation anywhere else.
/// </para>
/// </remarks>
internal interface IRunEffectQueue
{
    /// <summary>Queues one `18` §2.5 op for the run controller.</summary>
    /// <param name="effect">The authored effect. Every argument it carries is already on it.</param>
    /// <param name="source">The actor whose effect fired — `05` §7's <c>SourceId</c>.</param>
    /// <param name="argument">
    /// `05` §7 — the op's <b>one</b> runtime-resolved scalar, or <c>0</c> when every argument is
    /// authored. A <c>CombatEvent</c> has exactly one slot for it; an op needing two cannot be
    /// smuggled through by packing them.
    /// </param>
    void Queue(EffectDefinition effect, IEffectActorView source, double argument);
}

// ══════════════════════════════════════════════════════════════════ strict defaults

/// <summary>
/// The `18` §1.1 reader M2-03 ships: the authored <c>value</c>, unscaled — and a refusal naming
/// M2-06 the moment a <c>valueScale</c> arrives.
/// </summary>
/// <remarks>
/// The same shape, and the same reasoning, as M2-07's <c>AuthoredEffectValue</c>: `18` §1.1
/// evaluates <c>fn</c> against live state, which this layer does not hold, and
/// <c>ValueScale.EffectiveValue</c> already owns the arithmetic. ⚠️ It does <b>not</b> refuse a
/// <c>valueMode</c> — that half is M2-03's own and is applied per op by <see cref="OpValue"/>.
/// </remarks>
internal sealed class AuthoredScaledValue : IScaledValueReader
{
    /// <summary>The single instance.</summary>
    internal static AuthoredScaledValue Instance { get; } = new();

    private AuthoredScaledValue()
    {
    }

    /// <inheritdoc />
    public double ScaledValue(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.ValueScale is not null)
        {
            throw new EffectContextException(
                effect.Id,
                "it carries a valueScale and this reader evaluates none",
                "18 §1.1's effectiveValue = value x steps reads a §4 condition function against live " +
                "state, which is M2-06's evaluator; ValueScale.EffectiveValue already owns the " +
                "arithmetic. Pass an EffectOpSeams with a real IScaledValueReader.");
        }

        return effect.Value ?? throw new EffectContextException(
            effect.Id,
            $"it is a {effect.Op} with no value",
            "18 §1 makes value the effect's magnitude. Treating an absent one as 0 would make a " +
            "DAMAGE deal nothing and a DAMAGE_TAKEN_MULT delete all incoming damage, both silently " +
            "(steering S6).");
    }
}

/// <summary>The `05` §4 engine M2-03 ships: none, stated as a refusal naming M2-09.</summary>
internal sealed class UnwiredAttackPipeline : IAttackPipeline
{
    /// <summary>The single instance.</summary>
    internal static UnwiredAttackPipeline Instance { get; } = new();

    private UnwiredAttackPipeline()
    {
    }

    /// <inheritdoc />
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ResolveAttack), "05 §4's ten-step damage pipeline");

    /// <inheritdoc />
    public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(DealTrueDamage), "05 §4.2's DAMAGE_TRUE row");

    /// <inheritdoc />
    public void DealMaxHpPctDamage(
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(DealMaxHpPctDamage), "05 §4.2's DAMAGE_MAXHP_PCT row");

    /// <inheritdoc />
    public void Heal(IEffectActorView target, double amount, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(Heal), "05 §4.3's Heal()");

    /// <inheritdoc />
    public void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(GrantWard), "05 §4.1's ward pool");

    /// <inheritdoc />
    public void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(AddThorns), "05 §4.2's REFLECT row");

    private static EffectContextException Unwired(string effectId, string member, string what) =>
        new(effectId,
            $"{member} is not wired — {what} is M2-09's",
            "M2-03 resolves the op and computes its number; the engine that applies it lands in " +
            "wave 6. A no-op default would turn 'M2-09 has not landed' into 'this perk does " +
            "nothing', which the balance harness would read as a content problem. Pass an " +
            "EffectOpSeams with a real IAttackPipeline.");
}

/// <summary>The `05` §5 status engine M2-03 ships: none, stated as a refusal naming M2-10.</summary>
internal sealed class UnwiredStatusEngine : IStatusEngine
{
    /// <summary>The single instance.</summary>
    internal static UnwiredStatusEngine Instance { get; } = new();

    private UnwiredStatusEngine()
    {
    }

    /// <inheritdoc />
    public void Apply(
        IEffectActorView target, string statusId, double potency, EffectDuration? duration,
        EffectStacking? stacking, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(Apply));

    /// <inheritdoc />
    public void Remove(IEffectActorView target, string statusId, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(Remove));

    /// <inheritdoc />
    public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(RemoveByTag));

    /// <inheritdoc />
    public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(Extend));

    /// <inheritdoc />
    public void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(GrantImmunity));

    /// <inheritdoc />
    public void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ScaleOutgoingPower));

    /// <inheritdoc />
    public void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ScaleIncomingDuration));

    private static EffectContextException Unwired(string effectId, string member) =>
        new(effectId,
            $"{member} is not wired — 05 §5's twelve statuses and their DoT/HoT cadence are M2-10's",
            "M2-03 resolves 18 §2.3's six ops and computes each one's potency, duration and target; " +
            "the status engine that holds them lands in wave 7. Pass an EffectOpSeams with a real " +
            "IStatusEngine.");
}

/// <summary>The `18` §2.4 flow state M2-03 ships: none, stated as a refusal naming M2-08.</summary>
internal sealed class UnwiredCombatFlow : ICombatFlowSink
{
    /// <summary>The single instance.</summary>
    internal static UnwiredCombatFlow Instance { get; } = new();

    private UnwiredCombatFlow()
    {
    }

    /// <inheritdoc />
    public void ExtraAttack(
        IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ExtraAttack));

    /// <inheritdoc />
    public void GrantAttackMultiplierCharges(
        IEffectActorView holder, double multiplier, int charges, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(GrantAttackMultiplierCharges));

    /// <inheritdoc />
    public void GrantForcedCritCharges(IEffectActorView holder, int charges, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(GrantForcedCritCharges));

    /// <inheritdoc />
    public void ReduceCooldowns(IEffectActorView target, double fraction, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ReduceCooldowns));

    /// <inheritdoc />
    public void ArmSurviveLethal(IEffectActorView holder, double hp, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ArmSurviveLethal));

    /// <inheritdoc />
    public void ArmRevive(IEffectActorView holder, double hp, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ArmRevive));

    /// <inheritdoc />
    public void Summon(
        IEffectActorView summoner, string archetype, int count, int? maxAlive, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(Summon));

    /// <inheritdoc />
    public void ClearSummons(IEffectActorView owner, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(ClearSummons));

    /// <inheritdoc />
    public void SetTargetPriority(IEffectActorView target, double priority, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(SetTargetPriority));

    /// <inheritdoc />
    public void AddDamageTakenMultiplier(
        IEffectActorView target, double multiplier, EffectDuration? duration, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(AddDamageTakenMultiplier));

    /// <inheritdoc />
    public void AddPercentBucket(
        IEffectActorView holder, StatId stat, double fraction, EffectDuration? duration, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(AddPercentBucket));

    private static EffectContextException Unwired(string effectId, string member) =>
        new(effectId,
            $"{member} is not wired — 18 §2.4's flow state lives on the actor, and the tick loop that " +
            "holds it is M2-08's",
            "M2-03 resolves the op and computes its number; wave 6 gives it somewhere to land. Pass " +
            "an EffectOpSeams with a real ICombatFlowSink.");
}

/// <summary>The `18` §2.4 stat reading M2-03 ships: none, stated as a refusal.</summary>
internal sealed class UnwiredStatReader : IResolvedStatReader
{
    /// <summary>The single instance.</summary>
    internal static UnwiredStatReader Instance { get; } = new();

    private UnwiredStatReader()
    {
    }

    /// <inheritdoc />
    public double FinalStat(IEffectActorView actor, StatId stat) => throw Unwired(nameof(FinalStat));

    /// <inheritdoc />
    public StatId HighestPercentBonusStat(IEffectActorView actor) =>
        throw Unwired(nameof(HighestPercentBonusStat));

    private static EffectContextException Unwired(string member) =>
        new(nameof(EffectOp.STAT_COPY),
            $"{member} is not wired — 18 §2.4 reads the START-OF-TICK snapshot of an actor's final " +
            "resolved stats, which only the tick loop holds",
            "R17 forbids Rules.Effects naming Rules.Stats, so the op reads through this seam rather " +
            "than through M2-07's ActorStats directly; M2-08 supplies it. Returning 0 would make " +
            "every copy copy nothing.");
}

/// <summary>The `18` §2.5 queue M2-03 ships: none, stated as a refusal naming M2-08.</summary>
internal sealed class UnwiredRunQueue : IRunEffectQueue
{
    /// <summary>The single instance.</summary>
    internal static UnwiredRunQueue Instance { get; } = new();

    private UnwiredRunQueue()
    {
    }

    /// <inheritdoc />
    public void Queue(EffectDefinition effect, IEffectActorView source, double argument)
    {
        ArgumentNullException.ThrowIfNull(effect);

        throw new EffectContextException(
            effect.Id,
            $"{effect.Op} is a 18 §2.5 run/board op and no queue was supplied",
            "18 §2.5: these are 'resolved by the run controller, never by the combat simulator' — " +
            "the simulator appends a RunEffectQueued event (05 §7) and M3 drains it. M2-08 supplies " +
            "the queue. Resolving it here would be a guess at both the run controller and M1-05's " +
            "Run aggregate.");
    }
}
