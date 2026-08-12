using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 The four parts of `05` §3.1 that are <b>not</b> this task's, each stated as a seam the tick
/// loop calls at the slot the document puts it in.
/// </summary>
/// <param name="Attack">`05` §4 / §4.1 / §4.3 — <b>M2-09</b>. Slot 4 calls it; see its remarks.</param>
/// <param name="Statuses">`18` §2.3's six status ops — <b>M2-10</b>.</param>
/// <param name="Timeline">`05` §3.1 slots 1 and 2 — <b>M2-10</b>.</param>
/// <param name="Phases">`05` §3.1's phase check, pre-tick 0c and the enrage — <b>M2-12</b>.</param>
/// <param name="Summons">`18` §2.4's <c>SUMMON</c> — the roster half is <b>M2-12</b>'s.</param>
/// <param name="Pets">`05` §3.1 <b>slot 5</b> — pet ability cooldowns. See <see cref="IPetAbilities"/>.</param>
/// <param name="Outcomes">
/// `18` §2.4 / §10.1 E6's <c>RANDOM_OUTCOME</c> winner — <b>M2-12/M2-13</b>. See
/// <see cref="IBossOutcomes"/>.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>Why the defaults are not all refusals.</b> <c>EffectOpSeams.Strict</c> throws on every
/// member, because an op only reaches a seam when authored content asked for it — <em>"M2-09 has not
/// landed"</em> must not read as <em>"this perk does nothing"</em>. Two of the seams here are the
/// other shape: slots 1 and 2 run on <b>every tick of every fight</b>, and the phase check runs after
/// <b>every</b> HP decrease, so a throwing default would make every battle throw rather than making
/// one missing feature loud.
/// </para>
/// <para>
/// So each default below is a refusal <em>at the point content asks for the feature</em>, and a
/// no-op where the loop merely walks past the slot:
/// </para>
/// <list type="bullet">
///   <item><see cref="NoStatusTimeline"/> advances nothing and answers <c>0</c> stacks — correct for
///   a fight with no statuses, and unreachable for a fight with one, because applying a status goes
///   through <see cref="IStatusEngine"/> and <c>UnwiredStatusEngine.Apply</c> throws naming M2-10.
///   That is the loud failure, one layer earlier; <c>StatusHoleTests</c> pins it.</item>
///   <item><see cref="NoBossPhases"/> no-ops the check and <b>throws on a boss</b>: `05` §6.3 gives
///   every boss three phases, so a roster carrying one and a controller that knows no phases is a
///   wiring gap, not a fight.</item>
///   <item><see cref="NoSummons"/> throws on every call. A <c>SUMMON</c> reaching it means content
///   asked, which is <c>EffectOpSeams.Strict</c>'s shape exactly.</item>
///   <item><see cref="NoBossOutcomes"/> throws on every call, for <see cref="NoSummons"/>' reason: a
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
    /// <summary>
    /// 🔒 The seam set M2-08 shipped: <b>every</b> engine absent, the damage pipeline included. Kept
    /// as the base a test builds a partial engine on top of (<c>Strict with { … }</c>), and as the
    /// one way to observe M2-03's refusals from inside a fight.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>No longer what a battle gets by default.</b> `05` §4 landed in M2-09, so
    /// <see cref="BattlePlan.Seams"/> defaults to <see cref="For"/> — a plan that took this set
    /// would refuse the first swing of every fight. The distinction is kept rather than collapsed
    /// because <c>UnwiredAttackPipeline</c> is still <c>EffectOpSeams.Strict</c>'s default for op
    /// resolution <em>outside</em> a battle, where there is no <see cref="BattleServices"/> to build
    /// a real pipeline from.
    /// </remarks>
    internal static BattleSeams Strict { get; } = new(
        UnwiredAttackPipeline.Instance,
        UnwiredStatusEngine.Instance,
        NoStatusTimeline.Instance,
        NoBossPhases.Instance,
        NoSummons.Instance,
        NoPetAbilities.Instance,
        NoBossOutcomes.Instance);

    /// <summary>
    /// 🔒 The seam set a fight gets by default: `05` §4's damage pipeline <b>wired</b>, and the four
    /// engines M2-10 and M2-12 own still refusing where content asks and walking past where the loop
    /// merely steps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a factory rather than a singleton for <see cref="BattleServices"/>' stated reason:
    /// <see cref="AttackPipeline"/> writes <c>Hit</c>/<c>Miss</c>/<c>Crit</c> into <em>this</em>
    /// fight's log, draws from <em>this</em> fight's stream and routes <em>this</em> fight's phase
    /// check, none of which exists until the battle does. A static instance would be one setter away
    /// from pointing at the previous battle's log.
    /// </para>
    /// </remarks>
    /// <param name="services">The battle's log, draw stream, roster, dials and HP routing.</param>
    internal static BattleSeams For(BattleServices services) =>
        Strict with { Attack = new AttackPipeline(services) };
}

/// <summary>
/// 🔒 `05` §3.1 <b>slots 1 and 2</b> — status timers, DoT/HoT cadence and expiry. The seam
/// <b>M2-10</b> implements.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THE WIRING CONTRACT — WHAT M2-10 INHERITS</b> ═══
/// </para>
/// <para>
/// The tick loop calls exactly three members, at exactly these points, and nothing else about
/// statuses is the loop's:
/// </para>
/// <list type="number">
///   <item>
///     <b>Slot 1, once per actor, in `05` §3.1 actor order</b> (hero, pets in slot order, enemies by
///     index) — <see cref="AdvanceTimers"/>. `05` §3.1: <em>"Status timers advance by TICK. Every
///     DoT/HoT instance whose cadence boundary falls on this tick applies its tick."</em> The
///     cadence rule is 🔒 and is M2-10's whole: one instance per <c>statusId</c> per target, ticking
///     <em>"on the 20th simulation tick after first application, and every 20 ticks thereafter"</em>,
///     never re-anchored by reapplication.
///   </item>
///   <item>
///     <b>Slot 2, once per actor, same order</b> — <see cref="ExpireDue"/>, <em>"in ascending
///     effect-id order. A DoT expiring exactly on a cadence boundary deals that tick first, then
///     expires. Emit <c>StatusExpired</c>."</em>
///   </item>
///   <item>
///     <b>Slot 4a, per swing</b> — <see cref="CanAct"/>. `05` §3.1: <em>"if attackCooldown &lt;= 0
///     and actor.alive <b>and not stunned</b>"</em>. `05` §5's <c>STUN</c> is <em>"cannot act for D
///     s. Max 1.5 s per application, with a 3 s immunity window after"</em>; the immunity window is
///     M2-10's, and the loop asks only whether the actor may swing.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>Three obligations the loop cannot check and M2-10 must honour.</b>
/// </para>
/// <list type="bullet">
///   <item><b>Every DoT HP change routes the phase check.</b> `05` §3.1 is explicit that a DoT tick
///   runs <em>"ward absorption (§4 step 9 …) and the phase check"</em>. The loop has already run its
///   own check for the attacks it drove; a DoT landing inside slot 1 is M2-10's to route through
///   <see cref="IBossPhases.AfterHpDecrease"/>.</item>
///   <item><b><c>ON_LOW_HP</c> after every HP change</b>, DoT ticks included — it is a
///   <em>crossing</em>, so a skipped observation is a firing lost (<c>TriggerRegistry</c>).</item>
///   <item><b>HoT ticks go through <c>IAttackPipeline.Heal</c></b> (`05` §4.3), not through the
///   damage path, so <c>HEAL%</c> and <c>ON_HEAL</c> apply.</item>
/// </list>
/// </remarks>
internal interface IStatusTimeline
{
    /// <summary>`05` §3.1 slot 1 — advance this actor's status timers by <c>TICK</c> and apply any
    /// DoT/HoT cadence tick that lands on <paramref name="tick"/>.</summary>
    void AdvanceTimers(BattleActor actor, int tick);

    /// <summary>`05` §3.1 slot 2 — expire this actor's statuses whose duration reached 0, in
    /// ascending effect-id order.</summary>
    void ExpireDue(BattleActor actor, int tick);

    /// <summary>`05` §3.1 slot 4a's <em>"and not stunned"</em>.</summary>
    bool CanAct(BattleActor actor);

    /// <summary>
    /// How many stacks of a `05` §5 status the actor carries — the single reading behind `18` §4's
    /// <c>STATUS_STACKS</c> and <c>HAS_STATUS</c>.
    /// </summary>
    int StacksOn(BattleActor actor, string statusId);

    /// <summary>
    /// 🔒 `18` §8 step 1 — the <b>active</b> stat modifiers this actor's live `05` §5 statuses
    /// contribute to its aggregation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The fourth member, added by M2-10, and the wiring contract above is why it had to be.</b>
    /// That contract lists three calls and says <em>"nothing else about statuses is the loop's"</em> —
    /// which is true of the tick <em>order</em>, and this is not a slot. It is `18` §8: half of
    /// §5's twelve are stat modifiers (<c>FREEZE</c> −50% ASPD, <c>WEAKEN</c> −X% ATK,
    /// <c>SUNDER</c> −X% DEF, <c>SPORE</c> −X% healing received, <c>RAGE</c> +X% ATK, <c>HASTE</c>
    /// +X% ASPD) and a status that never reaches the aggregation does nothing at all.
    /// </para>
    /// <para>
    /// 🔒 <b>Why it could not go anywhere else.</b> <c>BattleSimulation.RefreshStats</c> aggregates
    /// an actor's <b>untriggered standing</b> effects plus `18` §2.4's <c>STAT_COPY</c> percent
    /// buckets, and records in its own comment that the other half of `18` §8 step 1 — <em>"a
    /// triggered effect that has fired and whose duration has not ended"</em> — is not wired on that
    /// branch. A live status is exactly one of those. The <c>STAT_COPY</c> buckets on
    /// <c>CombatFlowState</c> were the near alternative and are the wrong home twice over: they carry
    /// no duration, so nothing would ever expire a <c>FREEZE</c>, and R13 scopes them to
    /// <c>STAT_COPY</c>, whose <c>HIGHEST_PCT_BONUS</c> reading would start seeing debuffs.
    /// </para>
    /// <para>
    /// Returns synthetic <c>STAT_ADD_PCT</c> definitions under ids no authored effect can take, on
    /// the precedent <c>RefreshStats</c> already set for the <c>STAT_COPY</c> buckets. Empty for an
    /// actor carrying no stat-modifying status, which is every actor in every fight until one lands.
    /// </para>
    /// </remarks>
    IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor);
}

/// <summary>
/// 🔒 `05` §3.1's <b>phase check</b>, pre-tick <b>0c</b> and the <b>70 s enrage</b>. The seam
/// <b>M2-12</b> implements.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THE WIRING CONTRACT — WHAT M2-12 INHERITS</b> ═══
/// </para>
/// <list type="number">
///   <item>
///     <b>Pre-tick 0c</b> — <see cref="EnterInitialPhase"/>, once per boss, after 0b's
///     <c>ON_BATTLE_START</c> sweep and before 0d's <c>BattleStart</c>. `05` §3.1: <em>"The boss's
///     phase 1 counts as entered: fire its <c>ON_PHASE_ENTER(1)</c> effects."</em> Register the
///     phase-1 block on the battle's <c>TriggerRegistry</c> at tick 0 — that is its R8 anchor — and
///     evaluate an <c>ON_PHASE_ENTER</c> occurrence with <c>Phase = 1</c>.
///   </item>
///   <item>
///     <b>After every HP decrease of any actor</b> — <see cref="AfterHpDecrease"/>. `05` §3.1 is 🔒
///     that the check runs <em>"immediately after <b>every</b> boss HP decrease (attack, DoT tick,
///     thorns, true damage), once ward absorption and the floor are settled"</em>. The loop calls it
///     for the HP changes it drives; M2-09 calls it from inside `05` §4 step 9 and M2-10 from a DoT
///     tick. It is handed <em>every</em> actor rather than only bosses, because "is this a boss" is
///     the controller's question and a loop that pre-filtered would be a second copy of it.
///   </item>
///   <item>
///     <b>Phases never revert, and a burst crosses them in order.</b> <em>"while <c>currentPhase &lt;
///     PhaseFor(hp)</c>, enter the next phase <b>in order</b>"</em> — 70% to 20% fires phase 2's
///     entry, then phase 3's, both inside one call. Healing back above a threshold does not
///     re-enter, and `PHASE`-scoped effects (`18` §6) are <c>Deactivate</c>d at each exit.
///   </item>
///   <item>
///     🔒 <b><c>SYS_ENRAGE</c> is not a phase and does not belong here.</b> It is
///     <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> with <c>BATTLE</c> scope (`17` §1), so it is
///     registered at pre-tick 0a like any other battle-start effect and fires through
///     <c>TriggerRegistry.PeriodicDue</c> in <b>slot 3</b>. R8 anchors it at battle start, which is
///     what makes <c>startDelay: 70.0</c> mean 70 s of battle. Putting it on this interface would
///     re-anchor it at a phase entry and the boss would enrage 70 s after reaching 66% HP.
///   </item>
///   <item>
///     <b>Once per actor per tick, between slots 2 and 3</b> — <see cref="AdvanceTick"/>, `17` §1's
///     telegraph pass. 🔒 <b>Added by M2-12</b>, because `05` §3.1's eight slots make no per-tick
///     call into this interface and a wind-up is emitted <em>ahead</em> of the firing it announces,
///     so nothing at the firing can raise it. See the member's own remarks for the alternative that
///     was rejected.
///   </item>
///   <item>
///     <b>Telegraphs and the first-clear extension are M2-12's too</b>, and neither is a call the
///     loop makes: `17` §1's wind-up is emitted through <c>CombatLog.AppendTelegraph</c> by
///     <see cref="AdvanceTick"/>, ahead of the mechanic that is about to land.
///     <para>
///     🔴 <b>ERRATUM, corrected in place.</b> This paragraph previously read that the first-clear
///     extension <em>"changes <c>CombatRules.MaxTicks</c> before the fight starts"</em>. It does
///     not, and it cannot: <c>CombatRules.PvE.MaxTicks</c> already equals <c>CombatLog.MaxTicks</c>
///     (1800) and <c>BattlePlan.Validated</c> throws above it, so there is no headroom — and
///     extending the fight would lengthen the <em>whole</em> fight rather than phase 1. `17` §1 says
///     <em>"phase 1 lasts 20% longer"</em>; a phase is an <b>HP band</b>, and at constant DPS its
///     duration is proportional to its width, so the first clear widens the band by 20% and moves
///     the phase-2 boundary from 0.66 to 0.5920. No tunable, no extra HP, no <c>MaxTicks</c> change.
///     See <c>BossPhaseRules</c>.
///     </para>
///   </item>
/// </list>
/// </remarks>
internal interface IBossPhases
{
    /// <summary>Pre-tick 0c — the boss's phase 1 counts as entered.</summary>
    void EnterInitialPhase(BattleActor actor, int tick);

    /// <summary>
    /// 🔒 `05` §3.1's phase check, after an HP decrease that has already settled ward absorption and
    /// the floor.
    /// </summary>
    void AfterHpDecrease(BattleActor actor, int tick);

    /// <summary>
    /// 🔒 `17` §1 / §11 — the per-tick telegraph pass, once per actor in `05` §3.1 actor order,
    /// between slots 2 and 3.
    /// </summary>
    /// <param name="actor">The actor the loop reached. Most calls are not a boss's.</param>
    /// <param name="tick">The tick being run.</param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why the loop gained a slot rather than the boss gaining a pulse.</b> The alternative was
    /// a synthetic <c>PERIODIC</c> built-in attached to every boss, so that slot 3 pumped the
    /// controller with no engine edit at all. It was rejected: a fabricated effect id would enter the
    /// battle's effect table — which is built from the opening roster and whose <b>positions</b> are
    /// `05` §7's <c>RunEffectQueued</c> and <c>Telegraph</c> indices — and would therefore shift
    /// indices that are already inside every committed <c>LogHash</c>. This call site costs four
    /// lines and perturbs nothing.
    /// </para>
    /// <para>
    /// <see cref="NoBossPhases"/> no-ops it, which is correct rather than lenient: a fight with no
    /// boss has no wind-up to announce, and the log of such a fight is byte-identical with and
    /// without this slot.
    /// </para>
    /// </remarks>
    void AdvanceTick(BattleActor actor, int tick);

    /// <summary>
    /// 🔒 `18` §6 — the phase the boss is <b>in</b>, so that a <c>PHASE</c>-scoped effect can end
    /// <em>"when the boss exits the phase in which the effect was applied"</em>. <c>null</c> outside
    /// a boss fight, and <c>null</c> for a boss whose phase 1 has not been entered yet.
    /// </summary>
    /// <param name="actor">The boss being asked about.</param>
    /// <remarks>
    /// <para>
    /// 🔴 <b>R3 — without this member the scope silently did nothing.</b> `18` §6 makes every boss
    /// <c>AURA</c> <c>PHASE</c>-scoped, and <c>DurationEvaluator</c> has implemented the rule since
    /// M2-06: it reads <c>EffectApplication.AppliedInPhase</c> and <c>DurationProbe.CurrentPhase</c>
    /// and ends the effect the moment the second exceeds the first. Both were left <c>null</c> by
    /// every caller, because nothing in the battle could answer the question — so `18` §6's
    /// <em>"outside a boss fight it behaves as <c>BATTLE</c>"</em> fallback was taken <b>inside</b>
    /// boss fights too, and a phase-2 aura survived into phase 3 with nothing going red. This is the
    /// smallest thing that closes it: one reading, on the seam that already owns the phase.
    /// </para>
    /// <para>
    /// 🔒 <b>A reading, never a transition.</b> It must not consult HP: `05` §3.1's <em>"phases never
    /// revert"</em> means the current phase is what the controller has <em>entered</em>, and a boss
    /// healed back above 66% is still in the phase it reached. An implementation that recomputed
    /// from HP would end a phase-3 aura on a heal.
    /// </para>
    /// </remarks>
    int? CurrentPhase(BattleActor actor);
}

/// <summary>
/// 🔒 `18` §2.4 / §10.1 <b>E6</b> — the <b>one</b> effect a <c>RANDOM_OUTCOME</c>'s single draw
/// picked, handed over by id. The seam <b>M2-12</b> implements and <b>M2-13</b> authors against.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THE OP CANNOT SIMPLY FIRE THE WINNER ITSELF</b> ═══
/// </para>
/// <para>
/// R17 fixes the intra-<c>Rules</c> layering as
/// <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c> and <c>IntraRulesLayeringRuleTests</c> fails
/// the build on a violation, so <c>Rules/Effects/Ops/</c> may not name a <c>Rules.Combat.Bosses</c>
/// type. The op therefore does what the bottom layer can do — validate the table and take exactly
/// one <c>DeterministicRng.WeightedPick</c> — and names the winner across
/// <c>ICombatFlowSink.RandomOutcome</c>; <see cref="BattleSimulation"/> routes it here.
/// </para>
/// <para>
/// The split mirrors <see cref="ISummonSource"/>'s exactly: the op knows <em>what was asked for</em>,
/// the roster half knows <em>what that is</em>.
/// </para>
/// </remarks>
internal interface IBossOutcomes
{
    /// <summary>Fires the single effect the roll drew.</summary>
    /// <param name="holder">The actor whose effect rolled — `17` §9's Dicelord.</param>
    /// <param name="chosenEffectId">
    /// 🔒 The `18` §8 id of the <b>one</b> effect that fires. A <b>sibling</b> reference — an id the
    /// same owning boss script declares — never an embedded effect
    /// (<c>RandomOutcomeEntry</c> states why). That is what makes the outcomes mutually exclusive:
    /// one call per roll, one effect per call.
    /// </param>
    /// <param name="sourceEffectId">The <c>RANDOM_OUTCOME</c> effect's own id, for the failure message.</param>
    void Resolve(BattleActor holder, string chosenEffectId, string sourceEffectId);
}

/// <summary>
/// 🔒 `18` §2.4's <c>SUMMON</c> — the actor a summon spawns. The <b>roster</b> half of the op; the
/// <b>entry</b> half (`05` §3.1) is this task's, on <see cref="BattleSimulation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The split follows what each task knows. `05` §3.1 fixes the entry rules and they are the loop's:
/// <em>"Summons enter at the end of the enemy index list with a full attack cooldown
/// (<c>1.0 / ASPD</c> — they never attack on their spawn tick) and become targetable at the next
/// targeting evaluation."</em> What a <c>SUMMON</c> spawns — a `05` §6.1 archetype's derived
/// statline, at the encounter's power and level — is M2-11's derivation reached through M2-12's boss
/// authoring, and neither is in the tick loop.
/// </para>
/// <para>
/// 🔒 <b>The plan it returns carries no index and no log id.</b> Both are the roster's, and
/// <see cref="BattleSimulation"/> assigns them: <c>CombatActor</c> is 🔒 that a summon
/// <em>"takes the next free id and never reuses a dead one"</em>, because the log is the replay and
/// two actors sharing an id would draw the second one resuming the first one's HP bar.
/// </para>
/// </remarks>
internal interface ISummonSource
{
    /// <summary>
    /// The actor a <c>SUMMON</c> of <paramref name="archetype"/> spawns, without an
    /// <see cref="ActorPlan.Index"/> or <see cref="ActorPlan.LogId"/>.
    /// </summary>
    /// <param name="summoner">The actor whose effect fired — <c>OWNER</c>'s subject.</param>
    /// <param name="archetype">`05` §6.1's archetype name, as the op authors it.</param>
    /// <param name="sourceEffectId">The `18` §8 effect id, for the failure message.</param>
    ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId);
}

/// <summary>
/// 🔒 `05` §3.1 <b>slot 5</b> — <em>"Pet ability cooldowns advance; ready abilities fire, pets in
/// slot order."</em>
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>No M2 task owns this, and that is recorded rather than papered over</b> (steering S6). The
/// milestone's remaining tasks are M2-09 (damage), M2-10 (statuses), M2-12/M2-13 (bosses) and M2-14
/// (duels); `07` §2.1's pet actives belong to the hero/pet milestone. What `05` §3.1 fixes is that
/// the slot <b>exists and is fifth</b>, and this seam is that slot — empty, named, and greppable.
/// </para>
/// <para>
/// 🔒 <b>What slot 5 does not do, from <c>TriggerRegistry</c>:</b> <em>"Slot 5 fires nothing of its
/// own. `18` §7.7's pet actives carry no trigger — the ability's cooldown is the wrapper's — so a pet
/// ability raises only the <c>ON_HIT</c> family of slot 4, through the damage it resolves."</em> Two
/// further rules bound the implementer: a pet's damage is a percentage of the <b>hero's</b> ATK but
/// uses the <b>pet's</b> own CRIT (`05` §4.2), and a pet's <em>targeted</em> ability selects the
/// <b>highest current HP</b> enemy — <see cref="TargetSelection.ForPetAbility"/>, which is
/// implemented and tested here.
/// </para>
/// </remarks>
internal interface IPetAbilities
{
    /// <summary>`05` §3.1 slot 5, for one pet, called in slot order.</summary>
    void Advance(BattleActor pet, int tick);
}

/// <summary>
/// The `05` §3.1 slot 1/2 timeline M2-08 ships: no statuses exist, so nothing advances and nothing
/// expires. See <see cref="BattleSeams"/> for why this is a no-op rather than a refusal.
/// </summary>
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

/// <summary>
/// The `05` §6.3 phase controller M2-08 ships: no phases, which is correct for every fight that has
/// no boss — and a refusal naming M2-12 for every fight that has one.
/// </summary>
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
    /// 🔒 A <b>no-op</b>, not a refusal, and it is the one member of this class that is right rather
    /// than merely loud: a fight with no boss has no `17` §1 wind-up to announce, and the loop walks
    /// this slot on every one of 1800 ticks. <see cref="EnterInitialPhase"/> is where a roster
    /// carrying a boss becomes a wiring gap.
    /// </remarks>
    public void AdvanceTick(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 <c>null</c>, which is `18` §6's own answer for <em>"outside a boss fight"</em> and is
    /// therefore correct rather than merely quiet: a fight this controller runs has no phases at
    /// all, so a <c>PHASE</c>-scoped effect in it behaves as <c>BATTLE</c> exactly as §6 says. A
    /// roster that <em>does</em> carry a boss never gets this far —
    /// <see cref="EnterInitialPhase"/> refuses it at pre-tick 0c.
    /// </remarks>
    public int? CurrentPhase(BattleActor actor) => null;
}

/// <summary>
/// The `18` §10.1 E6 outcome resolver M2-08 ships: none, stated as a refusal naming M2-12/M2-13.
/// </summary>
/// <remarks>
/// A refusal rather than a no-op, on <see cref="NoSummons"/>' reasoning: an op only reaches a seam
/// because authored content asked for it, and a silently dropped outcome would make `17` §9's
/// <em>Roll of Fate</em> a d6 with no faces — a boss that rolls, visibly, and does nothing.
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

/// <summary>
/// The `05` §3.1 slot 5 M2-08 ships: the slot runs and no pet ability is authored to fire in it.
/// </summary>
/// <remarks>
/// <para>
/// A no-op rather than a refusal, for <see cref="NoStatusTimeline"/>'s reason: `05` §3 makes pets
/// optional (<em>"Pets (0–3)"</em>) and slot 5 runs on every tick of every fight. A fight with no pet
/// abilities is the ordinary case, not a wiring gap — and today it is the <em>only</em> case, because
/// `18` §7.7's pet active is a wrapper holding an effect list plus a cooldown and no such wrapper
/// type exists anywhere in the repository.
/// </para>
/// <para>
/// 🔒 <b>THE DEFERRAL IS REGISTERED, and this is the pointer to it.</b> No M2 task owns slot 5, so
/// the obligation is recorded where the repository's one expiring register can fire on it —
/// <c>SubjectSetFloorTests.Pending</c>, under the name <c>PetAbility</c> (steering S4). Read that
/// entry before changing this class: it explains why the name is an inference and asks whoever picks
/// the milestone up to <em>rename</em> the entry rather than delete it if they choose another. This
/// remark exists because a note addressed to a future milestone is worthless in a test file that
/// milestone will never open — the precedent <c>DurationScopes</c> set.
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

/// <summary>The `18` §2.4 summon roster M2-08 ships: none, stated as a refusal naming M2-12.</summary>
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
