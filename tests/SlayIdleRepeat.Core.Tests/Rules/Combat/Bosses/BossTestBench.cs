using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// Fixtures and doubles for the `17` boss-engine suite — the mechanics `17` actually authors, a
/// driver that puts a boss on an exact HP fraction at an exact tick, and a sampler that reads the
/// trigger registry once per tick.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why the sampler exists at all.</b> <c>TriggerRegistry.Activate</c> is documented as leaving
/// a <b>live</b> instance untouched, so a phase transition that forgot to <c>Deactivate</c> the
/// exiting phase produces a fight in which every mechanic still fires — at the wrong anchor. A test
/// that asserts <em>"the effect fired"</em> therefore passes with and without the de-anchoring and
/// cannot fail. <see cref="InstanceSample"/> carries <c>AnchorTick</c> and <c>NextFiringTick</c>,
/// which are the two readings that can.
/// </para>
/// <para>
/// The doubles implement the real seams and nothing more, on <c>BattleTestBench</c>'s rule: nothing
/// here re-implements `05` §4, `05` §5 or the phase machinery under test.
/// </para>
/// </remarks>
internal static class BossTestBench
{
    /// <summary>The boss under test — `17` §2's teaching boss.</summary>
    internal const string Thornmaw = "BOSS_THORNMAW";

    /// <summary>`17` §9's finale, for the <c>RANDOM_OUTCOME</c> cases.</summary>
    internal const string Dicelord = "BOSS_DICELORD";

    /// <summary>`05` §3 — the tick rate, so a test can write seconds and mean ticks.</summary>
    internal const int TicksPerSecond = CombatLog.TicksPerSecond;

    /// <summary>The tick a span of battle-time seconds lands on.</summary>
    internal static int At(double seconds) => (int)Math.Round(seconds * TicksPerSecond);

    /// <summary>
    /// A boss actor plan. Its log id is <c>CombatActor.Enemy(0)</c>, so a <c>PhaseChange</c>'s slot
    /// contract can be asserted literally.
    /// </summary>
    /// <param name="id">The boss's actor id — also <see cref="BossScript.Id"/>.</param>
    /// <param name="maxHp">Max HP. 1000 makes every `17` §1 threshold a whole number of HP.</param>
    /// <param name="effects">Its holdings, with explicit instance ids (`17` §1's D2 spelling).</param>
    /// <remarks>
    /// ⚠️ <b>ASPD is 0.001 deliberately</b>: these fights are about phases, not swings, and a 1.0-ASPD
    /// roster fills the log with a hit every second. One opening swing lands at tick 0 (`05` §3.1's
    /// <c>attackCooldown = 0</c> for every battle-opening actor) and the next is 1000 s away.
    /// </remarks>
    internal static ActorPlan Boss(string id, double maxHp = 1000.0, params HeldEffect[] effects) =>
        new()
        {
            Id = id,
            Index = CombatActor.FirstEnemy,
            LogId = CombatActor.Enemy(0),
            Side = BattleSide.ENEMY,
            Kind = EffectActorKind.ENEMY,
            BaseStats = BattleTestBench.Stats(maxHp: maxHp, atk: 10.0, aspd: 0.001),
            Level = 1,
            IsBoss = true,
            Effects = effects,
        };

    /// <summary>A plain, non-boss enemy — the negative control for the phase check.</summary>
    /// <param name="index">Its enemy index.</param>
    internal static ActorPlan Minion(int index) =>
        BattleTestBench.Enemy(index, BattleTestBench.Stats(maxHp: 1000.0, aspd: 0.001));

    /// <summary>A hero big enough that nothing in these fights kills it.</summary>
    internal static ActorPlan Hero() =>
        BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 100_000.0, aspd: 0.001));

    /// <summary>`05` §3's bounds, shortened so a phase test does not run 1800 ticks.</summary>
    /// <param name="maxTicks">How many ticks the fight may run.</param>
    internal static CombatRules Rules(int maxTicks = 200) =>
        new(maxTicks, OnKillTriggersFire: true, IsPvp: false);

    /// <summary>A held effect under the `17` §1 D2 phase-instance spelling.</summary>
    internal static HeldEffect InPhase(string bossId, int phase, EffectDefinition effect) =>
        new(effect, BossBuiltIns.PhaseInstance(bossId, phase, effect.Id));

    /// <summary>A held built-in under the `17` §1 D2 built-in spelling.</summary>
    internal static HeldEffect BuiltIn(string bossId, EffectDefinition effect) =>
        new(effect, BossBuiltIns.BuiltInInstance(bossId, effect.Id));

    /// <summary>`17` §2 — Thornmaw phase 2's <c>PERIODIC 8 s</c> Root, telegraphed 1.2 s.</summary>
    internal static EffectDefinition Root() => new()
    {
        Id = "BOSS_THORNMAW_P2_ROOT",
        Op = EffectOp.APPLY_STATUS,
        StatusId = "SLOW",
        Value = 0.4,
        Target = EffectTarget.LOWEST_HP_ENEMY,
        Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 8.0 },
    };

    /// <summary>`17` §9 — the Dicelord's phase-3 <em>All In</em>: 300% ATK, telegraphed 1.5 s.</summary>
    internal static EffectDefinition AllIn() => new()
    {
        Id = "BOSS_DICELORD_P3_ALL_IN",
        Op = EffectOp.DAMAGE,
        Value = 3.0,
        Target = EffectTarget.LOWEST_HP_ENEMY,
        Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 8.0 },
    };

    /// <summary>
    /// An <c>ON_PHASE_ENTER</c> mechanic whose firing this suite can observe without standing in for
    /// M2-09 — an <c>APPLY_STATUS</c>, which reaches <see cref="RecordingStatuses"/>.
    /// </summary>
    /// <param name="id">The effect id. Chosen so that effect-id order and phase order disagree.</param>
    /// <param name="phase">The phase whose entry fires it.</param>
    internal static EffectDefinition OnPhaseEnter(string id, int phase) => new()
    {
        Id = id,
        Op = EffectOp.APPLY_STATUS,
        StatusId = "RAGE",
        Value = 0.3,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger { Kind = TriggerKind.ON_PHASE_ENTER, Phase = phase },
    };

    /// <summary>
    /// `17` §6's Rimehold Core, as `17` §11's <em>"<c>Core</c> state flag for damage-amplification
    /// states"</em>: <c>DAMAGE_TAKEN_MULT ×1.6</c> on the boss's own phase-2 entry.
    /// </summary>
    internal static EffectDefinition RimeholdCore() => new()
    {
        Id = "BOSS_RIMEHOLD_P2_CORE",
        Op = EffectOp.DAMAGE_TAKEN_MULT,
        Value = 1.6,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger { Kind = TriggerKind.ON_PHASE_ENTER, Phase = 2 },
        Duration = new EffectDuration { Scope = DurationScope.PHASE },
    };

    /// <summary>A <c>SUMMON</c> mechanic — `17` §2's phase-3 adds.</summary>
    /// <param name="id">The effect id.</param>
    /// <param name="maxAlive">`18` §2.4's <c>maxAlive</c>, which `17` §1 caps at 3.</param>
    internal static EffectDefinition Summon(string id, int maxAlive = BossAdds.MaxAlive) => new()
    {
        Id = id,
        Op = EffectOp.SUMMON,
        Archetype = nameof(EnemyArchetype.SWARM),
        Value = 2.0,
        MaxAlive = maxAlive,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger { Kind = TriggerKind.ON_PHASE_ENTER, Phase = 3 },
    };

    /// <summary>`17` §9 — <em>Roll of Fate</em>, phase 1's three equally weighted outcomes.</summary>
    internal static EffectDefinition RollOfFateP1() => new()
    {
        Id = "BOSS_DICELORD_ROLL_OF_FATE_P1",
        Op = EffectOp.RANDOM_OUTCOME,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 10.0 },
        Outcomes = new[]
        {
            new RandomOutcomeEntry(FateBossAtk, 2.0),
            new RandomOutcomeEntry(FateHeroAtk, 2.0),
            new RandomOutcomeEntry(FateBothAspd, 2.0),
        },
    };

    /// <summary>`17` §9 — phase 2's two-row <c>4/2</c> table. The same op, no branch.</summary>
    internal static EffectDefinition RollOfFateP2() => new()
    {
        Id = "BOSS_DICELORD_ROLL_OF_FATE_P2",
        Op = EffectOp.RANDOM_OUTCOME,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 14.0 },
        Outcomes = new[]
        {
            new RandomOutcomeEntry(FateBossAtk, 4.0),
            new RandomOutcomeEntry(FateBothAspd, 2.0),
        },
    };

    /// <summary>`17` §9's <em>1–2: boss gains ATK +25% for 8 s</em>.</summary>
    internal const string FateBossAtk = "BOSS_DICELORD_FATE_BOSS_ATK";

    /// <summary>`17` §9's <em>3–4: hero gains ATK +25% for 8 s</em>.</summary>
    internal const string FateHeroAtk = "BOSS_DICELORD_FATE_HERO_ATK";

    /// <summary>`17` §9's <em>5–6: both gain ASPD +30% for 8 s</em>.</summary>
    internal const string FateBothAspd = "BOSS_DICELORD_FATE_BOTH_ASPD";

    /// <summary>One <c>Roll of Fate</c> outcome row, as an effect with no trigger of its own.</summary>
    /// <param name="id">The row's effect id.</param>
    internal static EffectDefinition FateOutcome(string id) => new()
    {
        Id = id,
        Op = EffectOp.APPLY_STATUS,
        StatusId = "RAGE",
        Value = 0.25,
        Target = EffectTarget.SELF,
        Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 8.0 },
    };

    /// <summary>An effect lookup in the shape <see cref="BossEncounterRequest.Effects"/> takes.</summary>
    /// <param name="effects">The authored effects, keyed by their `18` §8 ids.</param>
    internal static IReadOnlyDictionary<string, EffectDefinition> Lookup(
        params EffectDefinition[] effects) =>
        effects.ToDictionary(e => e.Id, e => e, StringComparer.Ordinal);

    /// <summary>One phase block.</summary>
    /// <param name="phase">1, 2 or 3.</param>
    /// <param name="mechanics">Its mechanics, by effect id.</param>
    internal static BossPhaseBlock Block(int phase, params BossMechanic[] mechanics) =>
        new() { Phase = phase, Mechanics = mechanics };

    /// <summary>A three-block script, in `17` §1.2's shape.</summary>
    /// <param name="id">The boss id.</param>
    /// <param name="blocks">The blocks. Pass fewer or out of order for a negative case.</param>
    internal static BossScript Script(string id, params BossPhaseBlock[] blocks) =>
        new()
        {
            Id = id,
            Coefficients = ThornmawCoefficients,
            Phases = blocks,
        };

    /// <summary>`17` §1.2's Thornmaw row.</summary>
    internal static BossCoefficients ThornmawCoefficients { get; } = new(2.40, 0.80, 0.80, 0.70);

    /// <summary>
    /// `17` §1.2's baseline secondaries — CRIT 0.05, CDMG 0.50, DODGE 0, LS 0 — carried on an
    /// <c>ArchetypeRow</c>, whose four coefficients the builder replaces.
    /// </summary>
    internal static ArchetypeRow Baseline { get; } = new(
        EnemyArchetype.GRUNT, 1.00, 1.00, 1.00, 1.00, 0.05, 0.50, 0.00, 0.00, 1, ArchetypeOnHit.None);

    /// <summary>A request in the shape M2-13 will build one.</summary>
    /// <param name="script">The boss script.</param>
    /// <param name="effects">The lookup its mechanic ids resolve against.</param>
    /// <param name="power">`02` §4.3's <c>EnemyPower(i)</c>, <c>StageMult.Boss</c> already inside it.</param>
    /// <param name="firstClear">`17` §1's first-clear flag.</param>
    internal static BossEncounterRequest Request(
        BossScript script,
        IReadOnlyDictionary<string, EffectDefinition> effects,
        double power = 10_000.0,
        bool firstClear = false) =>
        new()
        {
            Script = script,
            Effects = effects,
            Power = power,
            Level = 10,
            Index = CombatActor.FirstEnemy,
            LogId = CombatActor.Enemy(0),
            Baseline = Baseline,
            Derivation = EnemyFixtures.Constants(),
            FirstClear = firstClear,
        };

    /// <summary>`05` §6.1's rows and §6's constants, read from the shipped document's shape.</summary>
    internal static EnemyCatalogue Catalogue() => EnemyCatalogue.Read(EnemyFixtures.Snapshot());

    /// <summary>
    /// One scripted boss fight: the real <see cref="BossPhaseController"/>, a
    /// <see cref="BossDriver"/> holding the HP script and the per-tick registry readings, and a
    /// damage engine that deals nothing so the only HP changes in the fight are the scripted ones.
    /// </summary>
    /// <param name="roster">The actors, in `05` §3.1 index order.</param>
    /// <param name="encounters">The controller's encounters — one per boss it knows.</param>
    /// <param name="watched">The instance ids <see cref="BossDriver"/> samples every tick.</param>
    /// <param name="script">The HP script: <c>(tick, actorId, hpFraction)</c>.</param>
    /// <param name="statuses">The status engine, when a test needs to read what fired.</param>
    /// <param name="summons">`18` §2.4's roster half, when a phase block summons.</param>
    /// <param name="outcomes">
    /// E6's resolver, built from the battle — a factory rather than a value because
    /// <see cref="BossOutcomes"/> needs the fight's own <see cref="BattleServices"/>, which does not
    /// exist until the simulation does.
    /// </param>
    /// <param name="maxTicks">`05` §3's bound, shortened.</param>
    internal static BossRun Run(
        IReadOnlyList<ActorPlan> roster,
        IReadOnlyList<BossEncounter> encounters,
        IReadOnlyList<EffectInstanceId> watched,
        IReadOnlyList<(int Tick, string ActorId, double Fraction)> script,
        RecordingStatuses? statuses = null,
        ISummonSource? summons = null,
        Func<BattleServices, IBossOutcomes>? outcomes = null,
        int maxTicks = 200)
    {
        BossPhaseController? controller = null;
        BossDriver? driver = null;
        var recorded = statuses ?? new RecordingStatuses();

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            roster,
            services =>
            {
                controller = new BossPhaseController(services, encounters.ToArray());
                driver = new BossDriver(services, watched, script.ToArray());

                var seams = BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(services, damage: 0.0),
                    Statuses = recorded,
                    Timeline = driver,
                    Phases = controller,
                };

                return seams with
                {
                    Summons = summons ?? seams.Summons,
                    Outcomes = outcomes is null ? seams.Outcomes : outcomes(services),
                };
            },
            rules: Rules(maxTicks)));

        return new BossRun(result, controller!, driver!, recorded);
    }

    /// <summary>The <c>PhaseChange</c> events of a finished log, in emission order.</summary>
    /// <param name="log">A completed combat log.</param>
    internal static IReadOnlyList<CombatEvent> PhaseChanges(IReadOnlyList<CombatEvent> log) =>
        log.Where(e => e.Type == CombatEventType.PhaseChange).ToArray();

    /// <summary>The <c>Telegraph</c> events of a finished log, in emission order.</summary>
    /// <param name="log">A completed combat log.</param>
    internal static IReadOnlyList<CombatEvent> Telegraphs(IReadOnlyList<CombatEvent> log) =>
        log.Where(e => e.Type == CombatEventType.Telegraph).ToArray();
}

/// <summary>What one scripted boss fight produced.</summary>
/// <param name="Result">The finished simulation.</param>
/// <param name="Controller">The controller the fight ran on.</param>
/// <param name="Driver">The driver, with its per-tick registry readings.</param>
/// <param name="Statuses">The status engine, with what `18` §2.3's ops asked it for.</param>
internal sealed record BossRun(
    SimulationResult Result,
    BossPhaseController Controller,
    BossDriver Driver,
    RecordingStatuses Statuses);

/// <summary>One reading of one <c>TriggerInstance</c>, taken at the top of one tick.</summary>
/// <param name="Tick">The tick the reading was taken on.</param>
/// <param name="Instance">The instance id.</param>
/// <param name="IsRegistered">Whether the registry knows it at all.</param>
/// <param name="IsActive">`18` §6's live flag — <c>false</c> after a <c>Deactivate</c>.</param>
/// <param name="AnchorTick">🔒 R8's anchor. The reading a de-anchoring test cannot do without.</param>
/// <param name="NextFiringTick">The next tick a <c>PERIODIC</c> is due on.</param>
internal readonly record struct InstanceSample(
    int Tick, string Instance, bool IsRegistered, bool IsActive, int? AnchorTick, int? NextFiringTick);

/// <summary>
/// 🔒 The suite's driver: it puts a named actor on an exact HP fraction at an exact tick and routes
/// `05` §3.1's phase check, and it samples the trigger registry once per tick.
/// </summary>
/// <remarks>
/// <para>
/// Both jobs live on <see cref="IStatusTimeline"/> because a battle has exactly one, and because
/// slot 1 is where a DoT-driven HP change legitimately lands — <c>BattleSeams</c> states that M2-10
/// owes <c>IBossPhases.AfterHpDecrease</c> for exactly this case. Driving HP through the attack
/// pipeline instead would tie every scripted change to a swing landing.
/// </para>
/// <para>
/// The sample is taken <b>before</b> the tick's scripted change, so the reading at tick <c>t</c> is
/// the state a phase entry at tick <c>t − 1</c> left behind.
/// </para>
/// </remarks>
internal sealed class BossDriver : IStatusTimeline
{
    private readonly BattleServices _services;
    private readonly List<(int Tick, string ActorId, double Fraction)> _script;
    private readonly List<EffectInstanceId> _watched;

    private int _sampledTick = -1;

    /// <param name="services">The battle.</param>
    /// <param name="watched">The instance ids to sample every tick.</param>
    /// <param name="script">
    /// <c>(tick, actorId, hpFraction)</c> — the actor's HP is set to that fraction of its Max HP at
    /// the top of that tick, and <c>BattleServices.AfterHpDecrease</c> is routed.
    /// </param>
    internal BossDriver(
        BattleServices services,
        IEnumerable<EffectInstanceId> watched,
        params (int Tick, string ActorId, double Fraction)[] script)
    {
        _services = services;
        _watched = watched.ToList();
        _script = script.ToList();
    }

    /// <summary>Every reading taken, in tick order.</summary>
    internal List<InstanceSample> Samples { get; } = new();

    /// <summary>
    /// The fight's roster, so a test can read live actor state — `18` §2.4's
    /// <c>CombatFlowState</c>, which no seam surfaces.
    /// </summary>
    internal IReadOnlyList<BattleActor> Actors => _services.Actors;

    /// <summary>The reading of one instance at one tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="instance">The instance id, as a string.</param>
    internal InstanceSample At(int tick, string instance) =>
        Samples.Single(s => s.Tick == tick && string.Equals(s.Instance, instance, StringComparison.Ordinal));

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        if (_sampledTick != tick)
        {
            _sampledTick = tick;
            Sample(tick);
        }

        foreach (var step in _script)
        {
            if (step.Tick != tick || !string.Equals(step.ActorId, actor.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var before = actor.CurrentHp;
            actor.SetCurrentHp(step.Fraction * actor.MaxHp);

            // 🔒 Routed only on a DECREASE. `05` §3.1's phase check is owed to an HP decrease, and a
            // driver that routed a heal too would let a "phases never revert" test pass because the
            // controller was never asked rather than because it answered correctly.
            if (actor.CurrentHp < before)
            {
                _services.AfterHpDecrease(actor);
            }
        }
    }

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => true;

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;

    private void Sample(int tick)
    {
        foreach (var id in _watched)
        {
            if (!_services.Triggers.IsRegistered(id))
            {
                Samples.Add(new InstanceSample(tick, id.Value!, false, false, null, null));
                continue;
            }

            var instance = _services.Triggers[id];
            Samples.Add(new InstanceSample(
                tick, id.Value!, true, instance.IsActive, instance.AnchorTick, instance.NextFiringTick));
        }
    }
}

/// <summary>A status engine that records what `18` §2.3's ops asked it for, in call order.</summary>
internal sealed class RecordingStatuses : IStatusEngine
{
    /// <summary>Every call, as <c>"member:statusId(sourceEffectId)"</c>.</summary>
    internal List<string> Calls { get; } = new();

    /// <summary>The <c>IMMUNE_STATUS</c> grants — `17` §1's phase-3 immunity.</summary>
    internal List<(string Target, string StatusId, DurationScope? Scope, string SourceEffectId)> Immunities
    { get; } = new();

    /// <summary>The effect ids that applied a status, in call order.</summary>
    internal List<string> Applied { get; } = new();

    /// <inheritdoc />
    public void Apply(
        IEffectActorView target, string statusId, double potency, EffectDuration? duration,
        EffectStacking? stacking, string sourceEffectId)
    {
        Calls.Add($"Apply:{statusId}({sourceEffectId})");
        Applied.Add(sourceEffectId);
    }

    /// <inheritdoc />
    public void Remove(IEffectActorView target, string statusId, string sourceEffectId) =>
        Calls.Add($"Remove:{statusId}({sourceEffectId})");

    /// <inheritdoc />
    public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId) =>
        Calls.Add($"RemoveByTag:{tag}({sourceEffectId})");

    /// <inheritdoc />
    public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId) =>
        Calls.Add($"Extend:{statusId}({sourceEffectId})");

    /// <inheritdoc />
    public void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId)
    {
        Calls.Add($"GrantImmunity:{statusId}({sourceEffectId})");
        Immunities.Add((target.Id, statusId, duration?.Scope, sourceEffectId));
    }

    /// <inheritdoc />
    public void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        Calls.Add($"ScaleOutgoingPower({sourceEffectId})");

    /// <inheritdoc />
    public void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        Calls.Add($"ScaleIncomingDuration({sourceEffectId})");
}

/// <summary>An <see cref="IBossOutcomes"/> that records the ids <c>RANDOM_OUTCOME</c> handed it.</summary>
internal sealed class RecordingOutcomes : IBossOutcomes
{
    /// <summary>Every resolution, in call order.</summary>
    internal List<(string Holder, string ChosenEffectId, string SourceEffectId)> Resolutions { get; } = new();

    /// <inheritdoc />
    public void Resolve(BattleActor holder, string chosenEffectId, string sourceEffectId) =>
        Resolutions.Add((holder.Id, chosenEffectId, sourceEffectId));
}

/// <summary>An <see cref="ISummonSource"/> that records what was asked for and spawns a fixed add.</summary>
internal sealed class RecordingSummons : ISummonSource
{
    private readonly ActorPlan _add;

    /// <param name="add">The plan to return — without an index or a log id, as the seam requires.</param>
    internal RecordingSummons(ActorPlan add) => _add = add;

    /// <summary>Every spawn, in call order.</summary>
    internal List<(string Summoner, string Archetype, string SourceEffectId)> Spawns { get; } = new();

    /// <inheritdoc />
    public ActorPlan Spawn(BattleActor summoner, string archetype, string sourceEffectId)
    {
        Spawns.Add((summoner.Id, archetype, sourceEffectId));

        return _add with { Id = $"{_add.Id}_{Spawns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
    }
}
