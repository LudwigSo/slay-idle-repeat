using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The parts of the tick loop that live behind a seam, each called at the slot the loop reaches it.</summary>
/// <param name="Attack">The damage pipeline. Called from the basic-attack slot; see its remarks.</param>
/// <param name="Statuses">The six status ops.</param>
/// <param name="Timeline">Status timers and expiry.</param>
/// <param name="Phases">The phase check, the initial phase entry and the enrage.</param>
/// <param name="Summons">The roster half of <c>SUMMON</c>.</param>
/// <param name="Pets">Pet ability cooldowns. See <see cref="IPetAbilities"/>.</param>
/// <param name="Outcomes">The <c>RANDOM_OUTCOME</c> winner. See <see cref="IBossOutcomes"/>.</param>
/// <remarks>
/// <para>
/// <b>Why the defaults are not all refusals.</b> A throwing default is right for an op that only
/// reaches a seam because authored content asked for it — a missing feature should be loud, not read as
/// "this perk does nothing". But two of the seams here run on every tick of every fight regardless of
/// content (status timers, the phase check after every HP decrease), so a throwing default there would
/// make every battle throw rather than making one missing feature loud.
/// </para>
/// <para>Each default below is therefore a refusal at the point content asks for the feature, and a no-op where the loop merely walks past the slot:</para>
/// <list type="bullet">
///   <item><see cref="NoStatusTimeline"/> advances nothing and answers <c>0</c> stacks — correct for a
///   fight with no statuses; applying one goes through a separate engine that throws first, one layer
///   earlier.</item>
///   <item><see cref="NoBossPhases"/> no-ops the check and throws on a boss — every boss has three
///   phases, so a roster carrying one with no phase controller is a wiring gap, not a fight.</item>
///   <item><see cref="NoSummons"/> throws on every call: a <c>SUMMON</c> reaching it means content asked.</item>
///   <item><see cref="NoBossOutcomes"/> throws on every call, for the same reason: a
///   <c>RANDOM_OUTCOME</c> only reaches it because authored content rolled one.</item>
/// </list>
/// </remarks>
internal sealed record BattleSeams(
    IAttackPipeline Attack,
    IStatusEngine Statuses,
    IStatusTimeline Timeline,
    IBossPhases Phases,
    ISummonSource Summons,
    IPetAbilities Pets,
    IBossOutcomes Outcomes)
{
    /// <summary>Every engine absent, the damage pipeline included. The base a test builds a partial engine on top of.</summary>
    /// <remarks>
    /// No longer what a battle gets by default — once the damage pipeline landed, <see cref="BattlePlan.Seams"/>
    /// defaults to <see cref="For"/> instead, since a plan taking this set would refuse the first swing
    /// of every fight.
    /// </remarks>
    internal static BattleSeams Strict { get; } = new(
        UnwiredAttackPipeline.Instance,
        UnwiredStatusEngine.Instance,
        NoStatusTimeline.Instance,
        NoBossPhases.Instance,
        NoSummons.Instance,
        NoPetAbilities.Instance,
        NoBossOutcomes.Instance);

    /// <summary>The seam set a fight gets by default: the damage pipeline wired, the rest still refusing or walking past where the loop merely steps.</summary>
    /// <remarks>
    /// A factory rather than a singleton: the attack pipeline writes into <em>this</em> fight's log,
    /// draws from <em>this</em> fight's stream and routes <em>this</em> fight's phase check, none of
    /// which exists until the battle does. A static instance would be one setter away from pointing at
    /// the previous battle's log.
    /// </remarks>
    /// <param name="services">The battle's log, draw stream, roster, dials and HP routing.</param>
    internal static BattleSeams For(BattleServices services) =>
        Strict with { Attack = new AttackPipeline(services) };
}

/// <summary>Status timers, DoT/HoT cadence and expiry.</summary>
/// <remarks>
/// <para>The tick loop calls exactly three members, at exactly these points, and nothing else about statuses is the loop's:</para>
/// <list type="number">
///   <item>
///     <b>Once per actor, in actor order, before expiries</b> — <see cref="AdvanceTimers"/>. Status
///     timers advance by one tick; every DoT/HoT instance whose cadence boundary falls on this tick
///     applies its tick. One instance per status per target, ticking on the 20th tick after first
///     application and every 20 ticks thereafter, never re-anchored by reapplication.
///   </item>
///   <item>
///     <b>Once per actor, same order, right after</b> — <see cref="ExpireDue"/>, in ascending
///     effect-id order. A DoT expiring exactly on a cadence boundary deals that tick first, then expires.
///   </item>
///   <item>
///     <b>Per swing</b> — <see cref="CanAct"/>: cooldown ready, alive, and not stunned. A stun's
///     immunity window after expiry is this engine's; the loop asks only whether the actor may swing.
///   </item>
/// </list>
/// <para>Three obligations the loop cannot check and this engine must honour:</para>
/// <list type="bullet">
///   <item><b>Every DoT HP change routes the phase check</b> — a DoT tick runs ward absorption and the
///   phase check exactly as an attack does.</item>
///   <item><b><c>ON_LOW_HP</c> after every HP change</b>, DoT ticks included — it is a crossing, so a
///   skipped observation is a firing lost.</item>
///   <item><b>HoT ticks go through the heal path</b>, not the damage path, so heal percent and
///   <c>ON_HEAL</c> apply.</item>
/// </list>
/// </remarks>
internal interface IStatusTimeline
{
    /// <summary>Advance this actor's status timers by one tick and apply any DoT/HoT cadence tick that lands on <paramref name="tick"/>.</summary>
    void AdvanceTimers(BattleActor actor, int tick);

    /// <summary>Expire this actor's statuses whose duration reached 0, in ascending effect-id order.</summary>
    void ExpireDue(BattleActor actor, int tick);

    /// <summary>Whether the actor may act — alive, cooldown ready, and not stunned.</summary>
    bool CanAct(BattleActor actor);

    /// <summary>How many stacks of a status the actor carries.</summary>
    int StacksOn(BattleActor actor, string statusId);

    /// <summary>The active stat modifiers this actor's live statuses contribute to its aggregation.</summary>
    /// <remarks>
    /// <para>
    /// Half of the status vocabulary is stat modifiers (freeze, weaken, sunder, and so on), and a
    /// status that never reached the aggregation would do nothing at all. The main aggregation path
    /// reads an actor's untriggered standing effects plus copy-percent buckets, and a live status is
    /// neither of those — the buckets were the near alternative and are the wrong home twice over: they
    /// carry no duration, so nothing would ever expire a debuff, and they are scoped to a narrower read
    /// that would start seeing them too.
    /// </para>
    /// <para>
    /// Returns synthetic stat-add definitions under ids no authored effect can take. Empty for an actor
    /// carrying no stat-modifying status, which is every actor in every fight until one lands.
    /// </para>
    /// </remarks>
    IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor);
}

/// <summary>The boss phase check, the initial phase entry, and the enrage.</summary>
/// <remarks>
/// <para>The tick loop's contract with this interface:</para>
/// <list type="number">
///   <item>
///     <b>Once per boss, before the battle-start events</b> — <see cref="EnterInitialPhase"/>. The
///     boss's phase 1 counts as entered: fire its <c>ON_PHASE_ENTER(1)</c> effects, and register the
///     phase-1 block on the battle's trigger registry at tick 0.
///   </item>
///   <item>
///     <b>After every HP decrease of any actor</b> — <see cref="AfterHpDecrease"/>. The check runs
///     immediately after every boss HP decrease (attack, DoT tick, thorns, true damage), once ward
///     absorption and the floor are settled. It is handed every actor rather than only bosses, because
///     "is this a boss" is the controller's question and a loop that pre-filtered would be a second copy
///     of it.
///   </item>
///   <item>
///     <b>Phases never revert, and a burst crosses them in order.</b> While the boss's HP is below the
///     next threshold, enter the next phase in order — 70% to 20% fires phase 2's entry, then phase 3's,
///     both inside one call. Healing back above a threshold does not re-enter, and phase-scoped effects
///     are deactivated at each exit.
///   </item>
///   <item>
///     <b><c>SYS_ENRAGE</c> is not a phase and does not belong here.</b> It is a periodic effect with
///     its own start delay, registered at battle start like any other battle-start effect and fired
///     through the ordinary periodic-due path. Putting it on this interface would re-anchor it at a
///     phase entry instead of battle start.
///   </item>
///   <item>
///     <b>Once per actor per tick, between the expiry and periodic slots</b> — <see cref="AdvanceTick"/>,
///     the telegraph pass. Added because the tick loop makes no per-tick call into this interface
///     otherwise, and a wind-up must be emitted ahead of the firing it announces — nothing at the firing
///     itself can raise it. See the member's own remarks for the alternative that was rejected.
///   </item>
///   <item>
///     <b>Telegraphs and the first-clear extension both belong here</b> too. A wind-up is emitted
///     ahead of the mechanic that is about to land. The first-clear extension widens phase 1's HP band
///     by 20% rather than changing the tick cap or adding HP — see <c>BossPhaseRules</c>.
///   </item>
/// </list>
/// </remarks>
internal interface IBossPhases
{
    /// <summary>The boss's phase 1 counts as entered.</summary>
    void EnterInitialPhase(BattleActor actor, int tick);

    /// <summary>The phase check, after an HP decrease that has already settled ward absorption and the floor.</summary>
    void AfterHpDecrease(BattleActor actor, int tick);

    /// <summary>The per-tick telegraph pass, once per actor in actor order.</summary>
    /// <param name="actor">The actor the loop reached. Most calls are not a boss's.</param>
    /// <param name="tick">The tick being run.</param>
    /// <remarks>
    /// <para>
    /// <b>Why the loop gained a slot rather than the boss gaining a synthetic periodic pulse.</b> A
    /// fabricated effect id would enter the battle's effect table, whose positions are already inside
    /// every committed log hash — and would therefore shift indices already committed to. This call
    /// site costs four lines and perturbs nothing.
    /// </para>
    /// <para><see cref="NoBossPhases"/> no-ops it: a fight with no boss has no wind-up to announce, and its log is byte-identical with and without this slot.</para>
    /// </remarks>
    void AdvanceTick(BattleActor actor, int tick);

    /// <summary>
    /// The phase the boss is in, so a phase-scoped effect can end when the boss exits the phase in
    /// which it was applied. <c>null</c> outside a boss fight, and <c>null</c> for a boss whose phase 1
    /// has not been entered yet.
    /// </summary>
    /// <param name="actor">The boss being asked about.</param>
    /// <remarks>
    /// <para>
    /// Without this member the phase scope silently did nothing: every boss aura is phase-scoped, and
    /// the duration evaluator has implemented the boundary for a long time, but its two inputs had no
    /// source and every caller left them <c>null</c> — so the scope fell back to its "outside a boss
    /// fight" behaviour <em>inside</em> boss fights too, and a phase-2 aura survived into phase 3 with
    /// nothing going red.
    /// </para>
    /// <para>
    /// A reading, never a transition: it must not consult HP. Phases never revert, so the current phase
    /// is what the controller has entered, and a boss healed back above a threshold is still in the
    /// phase it reached. An implementation that recomputed from HP would end a phase-3 aura on a heal.
    /// </para>
    /// </remarks>
    int? CurrentPhase(BattleActor actor);
}

/// <summary>The one effect a <c>RANDOM_OUTCOME</c>'s single draw picked, handed over by id.</summary>
/// <remarks>
/// <para>
/// <b>Why the op cannot simply fire the winner itself.</b> The intra-<c>Rules</c> layering forbids
/// <c>Rules/Effects/Ops/</c> from naming a boss type, so the op does what the bottom layer can do —
/// validate the table and take exactly one weighted RNG pick — and names the winner across a sink
/// interface; <see cref="BattleSimulation"/> routes it here.
/// </para>
/// <para>The split mirrors <see cref="ISummonSource"/>'s exactly: the op knows what was asked for, the roster half knows what that is.</para>
/// </remarks>
internal interface IBossOutcomes
{
    /// <summary>Fires the single effect the roll drew.</summary>
    /// <param name="holder">The actor whose effect rolled.</param>
    /// <param name="chosenEffectId">
    /// The id of the one effect that fires — a sibling reference the same owning boss script declares,
    /// never an embedded effect. That is what makes the outcomes mutually exclusive.
    /// </param>
    /// <param name="sourceEffectId">The <c>RANDOM_OUTCOME</c> effect's own id, for the failure message.</param>
    void Resolve(BattleActor holder, string chosenEffectId, string sourceEffectId);
}

/// <summary>The actor a <c>SUMMON</c> spawns. The roster half of the op; the entry half is the tick loop's.</summary>
/// <remarks>
/// <para>
/// The split follows what each task knows. The entry rules — end of the enemy index list, a full
/// attack cooldown so summons never attack on their spawn tick, targetable at the next evaluation — are
/// the loop's. What a <c>SUMMON</c> actually spawns is a content-level derivation reached through boss
/// authoring, and neither lives in the tick loop.
/// </para>
/// <para>
/// The plan it returns carries no index and no log id — both are the roster's, and
/// <see cref="BattleSimulation"/> assigns them; a summon takes the next free id and never reuses a dead
/// one, since the log is the replay and two actors sharing an id would draw the second resuming the
/// first's HP bar.
/// </para>
/// </remarks>
internal interface ISummonSource
{
    /// <summary>The actor a <c>SUMMON</c> of <paramref name="archetype"/> spawns, without an <see cref="ActorPlan.Index"/> or <see cref="ActorPlan.LogId"/>.</summary>
    /// <param name="summoner">The actor whose effect fired — <c>OWNER</c>'s subject.</param>
    /// <param name="archetype">The archetype name, as the op authors it.</param>
    /// <param name="sourceEffectId">The effect id, for the failure message.</param>
    ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId);
}

/// <summary>Pet ability cooldowns advance; ready abilities fire, pets in slot order.</summary>
/// <remarks>
/// <para>
/// This interface fixes that the slot exists and is fifth in the tick loop; what fires in it is a
/// later milestone's.
/// </para>
/// <para>
/// This slot fires nothing of its own: a pet active carries no trigger — the ability's cooldown is the
/// wrapper's — so a pet ability raises only the on-hit trigger family, through the damage it resolves. A
/// pet's damage is a percentage of the hero's ATK but uses the pet's own CRIT, and a pet's targeted
/// ability selects the highest current HP enemy — see <see cref="TargetSelection.ForPetAbility"/>.
/// </para>
/// </remarks>
internal interface IPetAbilities
{
    /// <summary>The pet-ability slot, for one pet, called in slot order.</summary>
    void Advance(BattleActor pet, int tick);
}

/// <summary>The status timeline shipped with no statuses: nothing advances and nothing expires.</summary>
internal sealed class NoStatusTimeline : IStatusTimeline
{
    /// <summary>The single instance.</summary>
    internal static NoStatusTimeline Instance { get; } = new();

    private NoStatusTimeline()
    {
    }

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => true;

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];
}

/// <summary>The phase controller shipped with no phases: correct for a fight with no boss, a refusal for one that has one.</summary>
internal sealed class NoBossPhases : IBossPhases
{
    /// <summary>The single instance.</summary>
    internal static NoBossPhases Instance { get; } = new();

    private NoBossPhases()
    {
    }

    /// <inheritdoc />
    public void EnterInitialPhase(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        throw new EffectContextException(
            actor.Id,
            "it is a boss and no phase controller was supplied — `05` §6.3's three phases are M2-12's",
            "`05` §3.1's pre-tick 0c fires the boss's ON_PHASE_ENTER(1) effects and the phase check " +
            "runs after every HP decrease thereafter. Running a boss without them is a fight with its " +
            "mechanics silently deleted, which the balance harness would read as the boss being weak. " +
            "Pass a BattleSeams with a real IBossPhases.");
    }

    /// <inheritdoc />
    public void AfterHpDecrease(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// A no-op, not a refusal: a fight with no boss has no wind-up to announce, and the loop walks this
    /// slot on every tick. <see cref="EnterInitialPhase"/> is where a roster carrying a boss becomes a
    /// wiring gap.
    /// </remarks>
    public void AdvanceTick(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>null</c>, which is correct rather than merely quiet: a fight this controller runs has no
    /// phases at all, so a phase-scoped effect in it behaves as battle-scoped. A roster that does carry
    /// a boss never gets this far — <see cref="EnterInitialPhase"/> refuses it first.
    /// </remarks>
    public int? CurrentPhase(BattleActor actor) => null;
}

/// <summary>The outcome resolver shipped with none, stated as a refusal.</summary>
/// <remarks>
/// A refusal rather than a no-op: an op only reaches a seam because authored content asked for it, and
/// a silently dropped outcome would make a boss's roll-of-fate mechanic a die with no faces.
/// </remarks>
internal sealed class NoBossOutcomes : IBossOutcomes
{
    /// <summary>The single instance.</summary>
    internal static NoBossOutcomes Instance { get; } = new();

    private NoBossOutcomes()
    {
    }

    /// <inheritdoc />
    public void Resolve(BattleActor holder, string chosenEffectId, string sourceEffectId) =>
        throw new EffectContextException(
            sourceEffectId,
            $"its RANDOM_OUTCOME drew '{chosenEffectId}' and no boss outcome resolver was supplied",
            "`18` §10.1 E6 hands this seam ONE effect id per roll — a SIBLING id the same boss " +
            "script declares, never an embedded effect — and resolving it against the boss's own " +
            "holdings is M2-12's boss " +
            "engine — the scripts that author the tables are M2-13's. Firing nothing would make " +
            "`17` §9's Roll of Fate a d6 with no faces. Pass a BattleSeams with a real IBossOutcomes.");
}

/// <summary>The pet-ability slot shipped with no pet ability authored to fire in it.</summary>
/// <remarks>
/// <para>
/// A no-op rather than a refusal: pets are optional and this slot runs on every tick of every fight, so
/// a fight with no pet abilities is the ordinary case, not a wiring gap.
/// </para>
/// </remarks>
internal sealed class NoPetAbilities : IPetAbilities
{
    /// <summary>The single instance.</summary>
    internal static NoPetAbilities Instance { get; } = new();

    private NoPetAbilities()
    {
    }

    /// <inheritdoc />
    public void Advance(BattleActor pet, int tick)
    {
    }
}

/// <summary>The summon roster shipped with none, stated as a refusal.</summary>
internal sealed class NoSummons : ISummonSource
{
    /// <summary>The single instance.</summary>
    internal static NoSummons Instance { get; } = new();

    private NoSummons()
    {
    }

    /// <inheritdoc />
    public ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId) =>
        throw new EffectContextException(
            sourceEffectId,
            $"it summons '{archetype}' and no summon source was supplied",
            "`05` §3.1 gives M2-08 the ENTRY rules — end of the enemy index list, a full 1.0/ASPD " +
            "cooldown, targetable at the next evaluation — and they are implemented. What an archetype " +
            "spawns is `05` §6.1's derivation reached through M2-12's boss authoring. Returning nothing " +
            "would delete Rimehold's shards and Thornmaw's phase-3 adds from their fights.");
}
