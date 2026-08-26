using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Fixtures and doubles for the tick-engine suite: a roster stated literally, and a damage engine
/// that records what it was asked for rather than re-implementing damage resolution.
/// </summary>
internal static class BattleTestBench
{
    /// <summary>An actor block with the named stats set.</summary>
    internal static ActorStats Stats(
        double maxHp = 100.0, double atk = 10.0, double aspd = 1.0, double def = 0.0) =>
        StatFixtures.Block(
            (StatId.MAX_HP, maxHp),
            (StatId.ATK, atk),
            (StatId.ASPD, aspd),
            (StatId.DEF, def),
            (StatId.HEAL_PCT, 1.0));

    /// <summary>The hero — index 0, log id 0.</summary>
    internal static ActorPlan Hero(
        ActorStats? stats = null, int level = 1, params HeldEffect[] effects) =>
        new()
        {
            Id = "HERO",
            Index = 0,
            LogId = CombatActor.Hero,
            Side = BattleSide.HERO,
            Kind = EffectActorKind.HERO,
            BaseStats = stats ?? Stats(),
            Level = level,
            Effects = effects,
        };

    /// <summary>A pet in slot <paramref name="slot"/> — untargetable, unkillable, never attacks.</summary>
    internal static ActorPlan Pet(int slot, params HeldEffect[] effects) =>
        new()
        {
            Id = $"PET_{slot}",
            Index = 1 + slot,
            LogId = CombatActor.Pet(slot),
            Side = BattleSide.HERO,
            Kind = EffectActorKind.PET,
            BaseStats = Stats(),
            Level = 1,
            Effects = effects,
        };

    /// <summary>An enemy at <paramref name="index"/> in the enemy list.</summary>
    internal static ActorPlan Enemy(
        int index,
        ActorStats? stats = null,
        double targetPriority = 0.0,
        int level = 1,
        bool isBoss = false,
        params HeldEffect[] effects) =>
        new()
        {
            Id = $"ENEMY_{index}",
            Index = CombatActor.FirstEnemy + index,
            LogId = CombatActor.Enemy(index),
            Side = BattleSide.ENEMY,
            Kind = EffectActorKind.ENEMY,
            BaseStats = stats ?? Stats(),
            Level = level,
            TargetPriority = targetPriority,
            IsBoss = isBoss,
            Effects = effects,
        };

    /// <summary>
    /// A plan over the given roster, with PvE bounds and — unless overridden —
    /// <see cref="BattleSeams.For"/>'s real pipeline.
    /// </summary>
    internal static BattlePlan Plan(
        IEnumerable<ActorPlan> actors,
        BattleSeamFactory? seams = null,
        CombatRules? rules = null,
        ulong battleSeed = 0xC0FFEE_1234_5678UL) =>
        new()
        {
            BattleSeed = battleSeed,
            Actors = actors.ToArray(),
            Caps = StatFixtures.Caps(),
            Mitigation = StatFixtures.Mitigation(),
            WardCapPct = StatFixtures.WardCapPct,
            Rules = rules ?? CombatRules.PvE,
            RunCounters = new RunTriggerCounters(),
            Seams = seams ?? (static services => BattleSeams.For(services)),
        };

    /// <summary>An effect, with everything optional left absent.</summary>
    internal static EffectDefinition Effect(
        string id,
        EffectOp op,
        EffectTrigger? trigger = null,
        double? value = null,
        StatId? stat = null,
        EffectTarget? target = null) =>
        new()
        {
            Id = id,
            Op = op,
            Trigger = trigger,
            Value = value,
            Stat = stat is { } s ? StatSelector.Of(s) : null,
            Target = target,
        };
}

/// <summary>An attack pipeline that records every call and applies a flat, unmitigated hit.</summary>
internal sealed class RecordingAttackPipeline : IAttackPipeline
{
    private readonly double _damage;
    private readonly BattleServices _services;

    internal RecordingAttackPipeline(BattleServices services, double damage = 10.0)
    {
        _services = services;
        _damage = damage;
    }

    /// <summary>Every <c>ResolveAttack</c> the loop made, in call order.</summary>
    internal List<(int Tick, string Attacker, string Defender, double Multiplier, string SourceEffectId)> Swings
    { get; } = new();

    /// <summary>
    /// Run at the top of every <c>ResolveAttack</c>, for a case that has to do something from inside
    /// the running tick loop rather than before or after it.
    /// </summary>
    /// <remarks>
    /// 🔒 The seam exists because some things a case wants to observe are only legal mid-fight:
    /// <c>AdmitSummon</c> writes an <c>ActorSpawned</c> to the log, and <c>Complete</c> seals the log —
    /// so a summon admitted through a captured <c>BattleServices</c> after <c>Simulate</c> returned is
    /// refused, correctly. This is the hook that puts such a case where a real <c>SUMMON</c> op runs.
    /// </remarks>
    internal Action? BeforeSwing { get; set; }

    /// <inheritdoc />
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
    {
        BeforeSwing?.Invoke();

        Swings.Add((_services.Tick, attacker.Id, defender.Id, attackMultiplier, sourceEffectId));

        var target = (BattleActor)defender;
        var dealt = Math.Round(_damage * attackMultiplier, 4);

        target.SetCurrentHp(target.CurrentHp - dealt);
        _services.Log.Append(
            _services.Tick, CombatEventType.Hit, ((BattleActor)attacker).LogId, target.LogId, dealt);
        _services.AfterHpDecrease(target);

        return new AttackResolution(Missed: false, Crit: false, Blocked: false, dealt, dealt);
    }

    /// <inheritdoc />
    public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = (BattleActor)target;
        actor.SetCurrentHp(actor.CurrentHp - amount);
        _services.AfterHpDecrease(actor);
    }

    /// <inheritdoc />
    public void DealMaxHpPctDamage(
        IEffectActorView target,
        double amount,
        bool bypassesWards,
        string sourceEffectId,
        IEffectActorView? source) =>
        DealTrueDamage(target, amount, sourceEffectId);

    /// <inheritdoc />
    public void Heal(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = (BattleActor)target;
        actor.SetCurrentHp(actor.CurrentHp + amount);
    }

    /// <inheritdoc />
    public void GrantWard(IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId)
    {
    }

    /// <inheritdoc />
    public void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId)
    {
    }
}

/// <summary>A timeline that records the tick-loop slots it was called in.</summary>
internal sealed class RecordingTimeline : IStatusTimeline
{
    /// <summary>Every call, as <c>"slot:actor@tick"</c>, in call order.</summary>
    internal List<string> Calls { get; } = new();

    /// <summary>Actors that are stunned and cannot act.</summary>
    internal HashSet<string> Stunned { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick) => Calls.Add($"1:{actor.Id}@{tick}");

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick) => Calls.Add($"2:{actor.Id}@{tick}");

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => !Stunned.Contains(actor.Id);

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];
}

/// <summary>A phase controller that records its two hooks and can register a phase block.</summary>
internal sealed class RecordingPhases : IBossPhases
{
    private readonly BattleServices _services;
    private readonly EffectDefinition? _phaseEffect;
    private readonly double _threshold;

    private bool _entered;

    /// <param name="services">The battle.</param>
    /// <param name="phaseEffect">
    /// An effect to <c>Register</c> the first time the boss falls below <paramref name="threshold"/>.
    /// </param>
    /// <param name="threshold">The HP fraction the phase entry keys on.</param>
    internal RecordingPhases(
        BattleServices services, EffectDefinition? phaseEffect = null, double threshold = 0.66)
    {
        _services = services;
        _phaseEffect = phaseEffect;
        _threshold = threshold;
    }

    /// <summary>Every call, as <c>"hook:actor@tick"</c>.</summary>
    internal List<string> Calls { get; } = new();

    /// <summary>Every slot-2a call, as <c>"2a:actor@tick"</c>.</summary>
    internal List<string> Ticks { get; } = new();

    /// <summary>The tick the phase block was registered on — its R8 anchor.</summary>
    internal int? AnchoredAt { get; private set; }

    /// <summary>How many times the phase was entered.</summary>
    internal int Entries { get; private set; }

    /// <inheritdoc />
    public void EnterInitialPhase(BattleActor actor, int tick) => Calls.Add($"enter1:{actor.Id}@{tick}");

    /// <inheritdoc />
    /// <remarks>
    /// Recorded separately from <see cref="Calls"/>: it fires once per actor on every tick, so
    /// folding it in would bury the two hooks this double exists to observe.
    /// </remarks>
    public void AdvanceTick(BattleActor actor, int tick) => Ticks.Add($"2a:{actor.Id}@{tick}");

    /// <inheritdoc />
    public void AfterHpDecrease(BattleActor actor, int tick)
    {
        Calls.Add($"check:{actor.Id}@{tick}");

        if (_phaseEffect is null || _entered || !actor.IsBoss || actor.HpFraction > _threshold)
        {
            return;
        }

        _entered = true;
        Entries++;
        AnchoredAt = tick;

        var id = EffectInstanceId.Of($"{actor.Id}#phase2");
        _services.Triggers.Register(id, _phaseEffect, tick, actor.HpFraction);
        actor.AddInstance(id, _phaseEffect);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This double models one threshold, answering phase 2 once crossed and phase 1 before —
    /// deliberately not a copy of <c>BossPhaseController</c>'s full multi-band machine.
    /// </remarks>
    public int? CurrentPhase(BattleActor actor) => actor.IsBoss ? (_entered ? 2 : 1) : null;
}

/// <summary>A pet-ability slot that records slot 5's calls.</summary>
internal sealed class RecordingPets : IPetAbilities
{
    /// <summary>Every call, as <c>"5:pet@tick"</c>.</summary>
    internal List<string> Calls { get; } = new();

    /// <inheritdoc />
    public void Advance(BattleActor pet, int tick) => Calls.Add($"5:{pet.Id}@{tick}");
}
