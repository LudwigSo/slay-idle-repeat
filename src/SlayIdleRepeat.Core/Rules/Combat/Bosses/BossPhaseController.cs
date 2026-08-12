using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>TEMPORARY S1 PROBE PROTOTYPE — reverted immediately after the mutant run.</summary>
internal sealed class BossPhaseController : IBossPhases
{
    private readonly BattleServices _services;
    private readonly IReadOnlyList<BossEncounter> _encounters;
    private readonly Dictionary<string, int> _phase = new(StringComparer.Ordinal);

    /// <summary>Builds it.</summary>
    /// <param name="services">The battle.</param>
    /// <param name="encounters">Its bosses.</param>
    internal BossPhaseController(BattleServices services, params BossEncounter[] encounters)
    {
        _services = services;
        _encounters = encounters;
    }

    /// <summary>The current phase.</summary>
    /// <param name="bossId">The boss.</param>
    internal int CurrentPhaseOf(string bossId) => _phase.TryGetValue(bossId, out var p) ? p : 0;

    /// <inheritdoc />
    public void EnterInitialPhase(BattleActor actor, int tick)
    {
        var encounter = Encounter(actor.Id) ?? throw new EffectContextException(
            actor.Id, "no encounter", "wiring gap");

        foreach (var (id, phase) in encounter.PhaseOfInstance)
        {
            if (phase > BossPhaseRules.FirstPhase && _services.Triggers.IsRegistered(id))
            {
                _services.Triggers.Deactivate(id);
            }
        }

        Enter(actor, encounter, BossPhaseRules.FirstPhase, tick);
    }

    /// <inheritdoc />
    public void AfterHpDecrease(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public void AdvanceTick(BattleActor actor, int tick)
    {
    }

    private BossEncounter? Encounter(string id) =>
        _encounters.FirstOrDefault(e => string.Equals(e.BossId, id, StringComparison.Ordinal));

    private void Enter(BattleActor boss, BossEncounter encounter, int phase, int tick)
    {
        foreach (var (id, block) in encounter.PhaseOfInstance)
        {
            if (block == phase && _services.Triggers.IsRegistered(id))
            {
                _services.Triggers.Activate(id, tick, boss.HpFraction);
            }
        }

        _phase[boss.Id] = phase;

        _services.Log.Append(
            tick, CombatEventType.PhaseChange, boss.LogId, boss.LogId, phase);

        _services.Triggers.EvaluateAll(
            boss.Instances.Where(i => i.Kind == TriggerKind.ON_PHASE_ENTER).Select(i => i.Id),
            new TriggerOccurrence
            {
                Kind = TriggerKind.ON_PHASE_ENTER,
                Tick = tick,
                Phase = phase,
                HpFraction = boss.HpFraction,
            },
            _services.Rng);
    }
}
