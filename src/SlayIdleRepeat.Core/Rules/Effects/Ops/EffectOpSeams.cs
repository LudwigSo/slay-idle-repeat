using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>The six seams the ops resolve through, and the strict default set that refuses every one of them by name.</summary>
/// <param name="Values">The <c>valueScale</c> reader.</param>
/// <param name="Attack">The damage/ward/healing engine.</param>
/// <param name="Statuses">The status engine.</param>
/// <param name="Flow">Actor flow state.</param>
/// <param name="Stats"><c>STAT_COPY</c>'s reading of the aggregated stat block.</param>
/// <param name="RunQueue">The run/board op queue.</param>
/// <param name="TriggeredStats">The four basic stat ops, when a trigger fires them rather than aggregation collecting them.</param>
/// <remarks>
/// <para>
/// Ops own no state and reach nothing directly: the intra-<c>Rules</c> layering keeps
/// <c>Rules.Effects</c> at the bottom, so nothing here may name <c>ActorStats</c>, <c>CombatEvent</c>
/// or <c>CombatLog</c>. Every op computes its number from the DSL and hands it to a seam whose
/// implementation lives one layer up — which is also what keeps each state change logged the moment
/// it happens, rather than accumulated into a plan and flushed later.
/// </para>
/// <para>
/// Every default throws, and none no-ops — a silent no-op would turn "the engine isn't wired yet"
/// into "this perk does nothing", a balance bug rather than a visible error.
/// </para>
/// </remarks>
internal sealed record EffectOpSeams(
    IScaledValueReader Values,
    IAttackPipeline Attack,
    IStatusEngine Statuses,
    ICombatFlowSink Flow,
    IResolvedStatReader Stats,
    IRunEffectQueue RunQueue,
    ITriggeredStatSink TriggeredStats)
{
    /// <summary>The unwired seam set: an unscaled authored value, and a refusal — naming the owning member — for everything not yet built.</summary>
    internal static EffectOpSeams Strict { get; } = new(
        AuthoredScaledValue.Instance,
        UnwiredAttackPipeline.Instance,
        UnwiredStatusEngine.Instance,
        UnwiredCombatFlow.Instance,
        UnwiredStatReader.Instance,
        UnwiredRunQueue.Instance,
        UnwiredTriggeredStatSink.Instance);
}

/// <summary>The four basic stat ops, the moment a trigger fires one rather than aggregation collecting it as an ALWAYS passive.</summary>
/// <remarks>
/// <para>
/// An ALWAYS passive is read straight off standing effects by aggregation and never reaches
/// <see cref="EffectOpResolver"/>. A triggered stat op reaches the resolver like any other firing
/// effect and stays collected for as long as its own duration hasn't ended — recording that is
/// per-target, per-effect state that the effects layer isn't allowed to hold directly, so it's
/// reached through this seam instead, one layer up.
/// </para>
/// <para>
/// Not <see cref="EffectOp.STAT_CONVERT"/> or <c>STAT_CAP_OVERRIDE</c> — those remain aggregation-only,
/// never fired through a trigger, because their arithmetic needs a post-aggregation value only
/// aggregation has in hand.
/// </para>
/// </remarks>
internal interface ITriggeredStatSink
{
    /// <summary>Records one firing of a basic stat op onto every actor its target token resolved to.</summary>
    /// <param name="targets">The op's resolved targets — <see cref="OpTargets.Resolve"/>'s result. May differ from the holder.</param>
    /// <param name="op">Which of the four ops fired.</param>
    /// <param name="stat">The stat it names.</param>
    /// <param name="value">The fire-time, scaled value of this application.</param>
    /// <param name="duration">The effect's duration block, or <c>null</c> for none authored.</param>
    /// <param name="stacking">The effect's own stacking block, or <c>null</c> to take the default.</param>
    /// <param name="sourceEffectId">The firing effect's id — never synthesised.</param>
    void Apply(
        IReadOnlyList<IEffectActorView> targets, EffectOp op, StatId stat, double value,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId);
}

/// <summary>The effect's <c>value</c> after <c>valueScale</c>, and nothing else.</summary>
/// <remarks>
/// An effect's magnitude is two multiplications in a fixed order: authored value × steps (this
/// reader, reading live state) → × basis (the op, applying a per-op meaning). Composing them in the
/// other order would scale a fraction-of-Max-HP by a step count taken against the pre-scaled value.
/// </remarks>
internal interface IScaledValueReader
{
    /// <summary>The effective value: <c>value × steps</c>, before any op-specific value mode.</summary>
    double ScaledValue(EffectDefinition effect);
}

/// <summary>What one <c>ResolveAttack</c> produced.</summary>
/// <param name="Missed">The defender dodged. Everything else is then zero/false.</param>
/// <param name="Crit">The hit critted.</param>
/// <param name="Blocked">The hit was blocked and halved.</param>
/// <param name="Basis">
/// The post-mitigation, post-floor hit before ward absorption. Lifesteal and thorns read this, not
/// <see cref="HpLost"/>.
/// </param>
/// <param name="HpLost">What actually came off HP after absorption.</param>
internal readonly record struct AttackResolution(
    bool Missed, bool Crit, bool Blocked, double Basis, double HpLost);

/// <summary>The damage, ward and healing engine every damage/heal op routes into.</summary>
/// <remarks>
/// The ops decide the number; this decides what happens to it — dodge, mitigation, crit, block, DR,
/// the floor and wards all live here, none of which the DSL layer may know about.
/// </remarks>
internal interface IAttackPipeline
{
    /// <summary><c>DAMAGE</c>: the full pipeline — dodge, mitigation, crit, block, DR, floor, wards.</summary>
    /// <param name="attacker">The source actor.</param>
    /// <param name="defender">The actor taking the hit.</param>
    /// <param name="attackMultiplier">The op's value, used directly as the AttackMultiplier for this resolved attack.</param>
    /// <param name="sourceEffectId">The effect id, for the log and for ascending-effect-id orderings.</param>
    AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId);

    /// <summary><c>DAMAGE_TRUE</c>: bypasses everything — no dodge, mitigation, crit, block, DR, floor or wards. No lifesteal or thorns.</summary>
    /// <param name="amount">The HP to remove, rounded to 4 dp.</param>
    void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId);

    /// <summary><c>DAMAGE_MAXHP_PCT</c>: no dodge, crit, block, mitigation or floor; DR% and DAMAGE_TAKEN_MULT do apply; wards absorb; no lifesteal or thorns.</summary>
    /// <param name="bypassesWards">
    /// True for a self-inflicted cost (e.g. a cursed-perk drawback) — wards must not silently delete
    /// perk drawbacks. The op reads and reports this rather than the pipeline re-deriving it.
    /// </param>
    void DealMaxHpPctDamage(
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId);

    /// <summary><c>Heal()</c>: <c>healed = min(amount × target.HEALPct, MaxHP − HP)</c>, overheal discarded unless an effect consumes it. Routes both <c>HEAL</c> and <c>HEAL_LEECH</c>.</summary>
    /// <param name="amount">The pre-HEAL% amount, rounded to 4 dp.</param>
    void Heal(IEffectActorView target, double amount, string sourceEffectId);

    /// <summary><c>SHIELD</c>: a ward grant.</summary>
    /// <param name="sourceCapPct">
    /// The total unbroken ward this effect instance may contribute, as a fraction of Max HP.
    /// <c>null</c> where the effect authors none — not the same as 0 and must not be coerced to one.
    /// </param>
    /// <param name="sourceEffectId">Part of the ward segment itself, not just a log label — what a per-instance cap is measured against.</param>
    void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId);

    /// <summary><c>REFLECT</c>: adds to <c>THORN</c> for its duration.</summary>
    /// <param name="fraction">The addition to <c>THORN</c>, as a fraction.</param>
    void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId);
}

/// <summary>The twelve statuses — the six status ops.</summary>
internal interface IStatusEngine
{
    /// <summary><c>APPLY_STATUS</c>. <paramref name="potency"/> is the status's own X.</summary>
    /// <param name="applier">
    /// The actor whose effect fired. Needed because some status potencies are stated against the
    /// applier's own stats at application time (e.g. a percent of the applier's ATK), and the
    /// applier can be dead by the time a later cadence tick lands.
    /// </param>
    /// <param name="target">The actor receiving the status.</param>
    /// <param name="statusId">One of the twelve statuses.</param>
    /// <param name="potency">The status's X, after <c>valueScale</c>.</param>
    /// <param name="duration">The application's duration, or <c>null</c>.</param>
    /// <param name="stacking">The effect's own stacking block, or <c>null</c> to take the status's default.</param>
    /// <param name="sourceEffectId">The effect id, for ascending-effect-id orderings.</param>
    void Apply(
        IEffectActorView applier, IEffectActorView target, string statusId, double potency,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId);

    /// <summary>Whether <paramref name="statusId"/> carries its own fixed potency (e.g. a status with a fixed stat penalty), rather than taking one from the applying effect's value.</summary>
    /// <remarks>
    /// Lets the op layer learn this before deciding whether to demand a value at all, rather than
    /// after a generic "no value" guard has already thrown.
    /// </remarks>
    /// <param name="statusId">One of the twelve statuses.</param>
    bool HasFixedPotency(string statusId);

    /// <summary><c>REMOVE_STATUS</c>, the <c>statusId</c> form.</summary>
    void Remove(IEffectActorView target, string statusId, string sourceEffectId);

    /// <summary><c>REMOVE_STATUS</c>, the <c>statusTag</c> form: every status on the target carrying the label.</summary>
    void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId);

    /// <summary><c>EXTEND_STATUS</c>: <paramref name="seconds"/> added to a live status.</summary>
    void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId);

    /// <summary><c>IMMUNE_STATUS</c>.</summary>
    void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId);

    /// <summary><c>STATUS_POWER_PCT</c>: scale the potency of statuses this actor applies (outgoing).</summary>
    void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId);

    /// <summary><c>STATUS_DURATION_PCT</c>: scale duration of statuses applied to this actor (incoming).</summary>
    void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId);
}

/// <summary>Per-actor flow state — charges, summons, priorities and the two death saves.</summary>
internal interface ICombatFlowSink
{
    /// <summary><c>EXTRA_ATTACK</c>: perform an additional attack immediately.</summary>
    void ExtraAttack(IEffectActorView attacker, IEffectActorView target, int attacks, string sourceEffectId);

    /// <summary><c>ATTACK_MULT_NEXT</c>. The id travels with the charges since they're consumed in ascending effect-id order.</summary>
    void GrantAttackMultiplierCharges(
        IEffectActorView holder, double multiplier, int charges, string sourceEffectId);

    /// <summary><c>FORCE_CRIT_NEXT</c>: the next <paramref name="charges"/> attacks always crit.</summary>
    void GrantForcedCritCharges(IEffectActorView holder, int charges, string sourceEffectId);

    /// <summary><c>REDUCE_COOLDOWN</c>: reduce pet/boss ability cooldowns by <paramref name="fraction"/> of their remaining time.</summary>
    void ReduceCooldowns(IEffectActorView target, double fraction, string sourceEffectId);

    /// <summary><c>SURVIVE_LETHAL</c>: arms a save that leaves the actor at <paramref name="hp"/>. Does not fire <c>ON_REVIVE</c> — the actor never died.</summary>
    void ArmSurviveLethal(IEffectActorView holder, double hp, string sourceEffectId);

    /// <summary><c>REVIVE</c>: arms a return from 0 HP at <paramref name="hp"/>. Unlike <see cref="ArmSurviveLethal"/> this does fire <c>ON_REVIVE</c>.</summary>
    void ArmRevive(IEffectActorView holder, double hp, string sourceEffectId);

    /// <summary><c>SUMMON</c>: spawn <paramref name="count"/> of <paramref name="archetype"/>, at most <paramref name="maxAlive"/> alive at once.</summary>
    void Summon(
        IEffectActorView summoner, string archetype, int count, int? maxAlive, string sourceEffectId);

    /// <summary><c>RANDOM_OUTCOME</c>'s single winner: the effect the op's one draw picked, handed over by id.</summary>
    /// <param name="holder">The actor whose effect rolled.</param>
    /// <param name="chosenEffectId">
    /// The id of the one effect that fires — a sibling reference, never an embedded effect.
    /// Resolving it is the engine's, which is what keeps the outcomes mutually exclusive.
    /// </param>
    /// <param name="sourceEffectId">The <c>RANDOM_OUTCOME</c> effect's own id, for the log.</param>
    void RandomOutcome(IEffectActorView holder, string chosenEffectId, string sourceEffectId);

    /// <summary><c>CLEAR_SUMMONS</c>: despawn all living summons owned by the target. Despawned, not killed — no death triggers, explosions or rewards.</summary>
    void ClearSummons(IEffectActorView owner, string sourceEffectId);

    /// <summary><c>SET_TARGET_PRIORITY</c>. Default 0, -1 deprioritises, +1 forces focus, ties broken by lowest current HP.</summary>
    void SetTargetPriority(IEffectActorView target, double priority, string sourceEffectId);

    /// <summary><c>DAMAGE_TAKEN_MULT</c>. Accumulates as a product of all active ones in ascending effect-id order, rather than replacing.</summary>
    void AddDamageTakenMultiplier(
        IEffectActorView target, double multiplier, EffectDuration? duration, string sourceEffectId);

    /// <summary><c>STAT_COPY</c>'s write: onto the holder as a percent-bucket add for <c>duration</c>, i.e. a <c>STAT_ADD_PCT</c> aggregation will pick up.</summary>
    /// <remarks>The holder, not the effect's <c>target</c> — see <see cref="StatCopyOp"/> for why that inversion is intentional.</remarks>
    void AddPercentBucket(
        IEffectActorView holder, StatId stat, double fraction, EffectDuration? duration, string sourceEffectId);
}

/// <summary><c>STAT_COPY</c> reading: an actor's final resolved stat, as of the start-of-tick snapshot.</summary>
/// <remarks>
/// The snapshot is this interface's contract, not the op's: reads the start-of-tick snapshot so
/// mutual copies can't recurse. An op can't enforce that itself — it would have to know how the
/// simulator stores stats — so the obligation is stated here instead.
/// </remarks>
internal interface IResolvedStatReader
{
    /// <summary>The actor's post-aggregation value for a stat, from the start-of-tick snapshot.</summary>
    double FinalStat(IEffectActorView actor, StatId stat);

    /// <summary><c>HIGHEST_PCT_BONUS</c> — whichever stat carries the largest percent bucket at copy time.</summary>
    StatId HighestPercentBonusStat(IEffectActorView actor);
}

/// <summary>The queue a combat trigger's run/board op is appended to. Translated into a queued event; drained by the run controller.</summary>
/// <remarks>
/// These ops are resolved by the run controller, never by the combat simulator — the simulator only
/// ever appends an event to the combat log. This seam is where "declared but not resolved" is made
/// mechanical: an op reaching here must be well-formed and must produce exactly this call and no
/// mutation anywhere else.
/// </remarks>
internal interface IRunEffectQueue
{
    /// <summary>Queues one run/board op for the run controller.</summary>
    /// <param name="effect">The authored effect. Every argument it carries is already on it.</param>
    /// <param name="source">The actor whose effect fired.</param>
    /// <param name="argument">
    /// The op's one runtime-resolved scalar, or <c>0</c> when every argument is authored. A combat
    /// event has exactly one slot for it; an op needing two cannot be smuggled through by packing them.
    /// </param>
    void Queue(EffectDefinition effect, IEffectActorView source, double argument);
}

// ══════════════════════════════════════════════════════════════════ strict defaults

/// <summary>The unscaled reader: the authored <c>value</c>, and a refusal the moment a <c>valueScale</c> arrives.</summary>
/// <remarks>
/// Refusing rather than evaluating, since <c>valueScale</c> reads live state this layer doesn't
/// hold. Does not refuse a <c>valueMode</c> — that's applied per op by <see cref="OpValue"/>.
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

/// <summary>The attack pipeline, unwired: every member refuses, naming what it would have done.</summary>
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

/// <summary>The status engine, unwired: every member refuses.</summary>
internal sealed class UnwiredStatusEngine : IStatusEngine
{
    /// <summary>The single instance.</summary>
    internal static UnwiredStatusEngine Instance { get; } = new();

    private UnwiredStatusEngine()
    {
    }

    /// <inheritdoc />
    public void Apply(
        IEffectActorView applier, IEffectActorView target, string statusId, double potency,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(Apply));

    /// <inheritdoc />
    public bool HasFixedPotency(string statusId) =>
        throw Unwired(nameof(EffectOp.APPLY_STATUS), nameof(HasFixedPotency));

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

/// <summary>The combat-flow sink, unwired: every member refuses.</summary>
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
    public void RandomOutcome(IEffectActorView holder, string chosenEffectId, string sourceEffectId) =>
        throw Unwired(sourceEffectId, nameof(RandomOutcome));

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

/// <summary>The resolved-stat reader, unwired: every member refuses.</summary>
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

/// <summary>The triggered-stat sink, unwired: refuses.</summary>
internal sealed class UnwiredTriggeredStatSink : ITriggeredStatSink
{
    /// <summary>The single instance.</summary>
    internal static UnwiredTriggeredStatSink Instance { get; } = new();

    private UnwiredTriggeredStatSink()
    {
    }

    /// <inheritdoc />
    public void Apply(
        IReadOnlyList<IEffectActorView> targets, EffectOp op, StatId stat, double value,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId) =>
        throw new EffectContextException(
            sourceEffectId,
            $"{nameof(ITriggeredStatSink.Apply)} is not wired — a FIRED 18 §2.1 stat op's `18` §6 " +
            "duration is M2-R1's",
            "R17 forbids Rules.Effects holding actor state, so the per-target stack set this op needs " +
            "lives one layer up, on BattleActor.TriggeredStatFirings. Pass an EffectOpSeams with a " +
            "real ITriggeredStatSink.");
}

/// <summary>The run/board op queue, unwired: refuses.</summary>
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
