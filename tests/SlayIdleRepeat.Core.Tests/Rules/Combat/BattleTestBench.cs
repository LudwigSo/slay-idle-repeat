using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Fixtures and doubles for the `05` §3 tick-engine suite — a roster stated literally, and a damage
/// engine that records what slot 4 asked it for.
/// </summary>
/// <remarks>
/// 🔒 <b>The doubles here implement the real seams and nothing more.</b> M2-09's
/// <c>ResolveAttack</c> is not written; what the tick loop can be held to is <em>what it calls, with
/// what, and in what order</em>, which is exactly what <see cref="RecordingAttackPipeline"/>
/// captures. A double that re-implemented `05` §4 would be a second damage engine to keep in step.
/// </remarks>
internal static class BattleTestBench
{
    /// <summary>An actor block: `05` §1's fourteen stats, with the named ones set.</summary>
    internal static ActorStats Stats(
        double maxHp = 100.0, double atk = 10.0, double aspd = 1.0, double def = 0.0) =>
        StatFixtures.Block(
            (StatId.MAX_HP, maxHp),
            (StatId.ATK, atk),
            (StatId.ASPD, aspd),
            (StatId.DEF, def),
            (StatId.HEAL_PCT, 1.0));

    /// <summary>The hero — index 0, log id 0 (`05` §3.1, <c>CombatActor</c>).</summary>
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

    /// <summary>A plan over the given roster, with `05` §3's PvE bounds and the strict seams.</summary>
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
            Rules = rules ?? CombatRules.PvE,
            RunCounters = new RunTriggerCounters(),
            Seams = seams ?? (static _ => BattleSeams.Strict),
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

/// <summary>
/// A `05` §4 pipeline that records every call and applies a flat, unmitigated hit — enough for a
/// fight to end, and nothing that pretends to be M2-09.
/// </summary>
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

    /// <inheritdoc />
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
    {
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
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId) =>
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

/// <summary>A timeline that records the slots it was called in — `05` §3.1's 1, 2 and 4a.</summary>
internal sealed class RecordingTimeline : IStatusTimeline
{
    /// <summary>Every call, as <c>"slot:actor@tick"</c>, in call order.</summary>
    internal List<string> Calls { get; } = new();

    /// <summary>Actors that answer <c>false</c> to `05` §3.1 slot 4a's "not stunned".</summary>
    internal HashSet<string> Stunned { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick) => Calls.Add($"1:{actor.Id}@{tick}");

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick) => Calls.Add($"2:{actor.Id}@{tick}");

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => !Stunned.Contains(actor.Id);

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;
}

/// <summary>A phase controller that records `05` §3.1's two hooks and can register a phase block.</summary>
internal sealed class RecordingPhases : IBossPhases
{
    private readonly BattleServices _services;
    private readonly EffectDefinition? _phaseEffect;
    private readonly double _threshold;

    private bool _entered;

    /// <param name="services">The battle.</param>
    /// <param name="phaseEffect">
    /// An effect to <c>Register</c> the first time the boss falls below <paramref name="threshold"/> —
    /// a `18` §6 <c>PHASE</c>-scoped block, whose R8 anchor is the tick it is registered on.
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

    /// <summary>The tick the phase block was registered on — its R8 anchor.</summary>
    internal int? AnchoredAt { get; private set; }

    /// <summary>How many times the phase was entered.</summary>
    internal int Entries { get; private set; }

    /// <inheritdoc />
    public void EnterInitialPhase(BattleActor actor, int tick) => Calls.Add($"enter1:{actor.Id}@{tick}");

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
}

/// <summary>A pet-ability slot that records slot 5's calls.</summary>
internal sealed class RecordingPets : IPetAbilities
{
    /// <summary>Every call, as <c>"5:pet@tick"</c>.</summary>
    internal List<string> Calls { get; } = new();

    /// <inheritdoc />
    public void Advance(BattleActor pet, int tick) => Calls.Add($"5:{pet.Id}@{tick}");
}
