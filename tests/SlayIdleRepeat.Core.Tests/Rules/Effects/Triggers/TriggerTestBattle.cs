using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>The hand-driven tick source the trigger predicates are proved against — and the shape of it is the point.</summary>
/// <remarks>
/// Every method takes the tick as an argument and nothing here holds a clock, a timer or a loop. That
/// is the property the wiring contract rests on: if these tests could only be written against a
/// driver that owned the clock, the predicates would not be callable from a loop this task did not
/// write.
/// <para>The effects are realistic authored ones rather than abstract fixtures, so a failure names the boss mechanic it broke.</para>
/// </remarks>
internal static class TriggerTestBattle
{
    /// <summary>The tick rate, so a test can write seconds and mean ticks.</summary>
    internal const int TicksPerSecond = 20;

    /// <summary>The tick a span of battle-time seconds lands on.</summary>
    internal static int At(double seconds) => (int)Math.Round(seconds * TicksPerSecond);

    /// <summary>An effect with the given id and trigger. The op is inert unless a test needs one.</summary>
    internal static EffectDefinition Effect(
        string id,
        EffectTrigger trigger,
        EffectOp op = EffectOp.STAT_ADD_PCT) =>
        new()
        {
            Id = id,
            Op = op,
            Trigger = trigger,
            Target = EffectTarget.SELF,
            Value = 1.0,
        };

    /// <summary>
    /// The built-in enrage — <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> →
    /// <c>STAT_MULT ATK ×1.08</c>, <c>BATTLE</c> scope. Present on every boss, phase-scoped on none.
    /// </summary>
    internal static EffectDefinition SysEnrage() =>
        new()
        {
            Id = "SYS_ENRAGE",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 1.08,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0, StartDelay = 70.0 },
            Target = EffectTarget.SELF,
            Duration = new EffectDuration { Scope = DurationScope.BATTLE },
            Stacking = new EffectStacking { Mode = StackingMode.MULTIPLICATIVE },
        };

    /// <summary>Thornmaw phase 2's <c>PERIODIC 8s</c> Root. No <c>startDelay</c>.</summary>
    internal static EffectDefinition ThornmawRoot() =>
        Effect(
            "BOSS_THORNMAW_P2_ROOT",
            new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 8.0 },
            EffectOp.APPLY_STATUS);

    /// <summary>Thornmaw phase 3's <c>PERIODIC 12s</c> swarm summon.</summary>
    internal static EffectDefinition ThornmawBloomSummon() =>
        Effect(
            "BOSS_THORNMAW_P3_SUMMON",
            new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 12.0 },
            EffectOp.SUMMON);

    /// <summary>Ossify's <c>PERIODIC 14s</c> ward, Ossuary King phase 2.</summary>
    internal static EffectDefinition Ossify() =>
        Effect(
            "BOSS_OSSUARY_KING_OSSIFY_WARD",
            new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 14.0 },
            EffectOp.SHIELD);

    /// <summary>Rise Again — spelled as <c>ON_LOW_HP</c> since there is no 24th trigger.</summary>
    internal static EffectDefinition RiseAgain() =>
        Effect(
            "BOSS_OSSUARY_KING_P3_RISE_AGAIN",
            new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.01, Once = true },
            EffectOp.REVIVE);

    /// <summary>The Dicelord's Scramble: a combat trigger carrying a run/board op. The one sanctioned case of the combat-context exception.</summary>
    internal static EffectDefinition Scramble() =>
        new()
        {
            Id = "BOSS_DICELORD_P2_SCRAMBLE",
            Op = EffectOp.MODIFY_DIE_FACE,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 14.0 },
            Target = EffectTarget.RUN,
            FaceIndex = DieFaceIndex.At(1),
            NewFace = new DieFaceSpec("Void"),
        };

    /// <summary><c>PK_FLURRY</c>: an extra attack on every 5th attack.</summary>
    internal static EffectDefinition Flurry(string id = "PK_FLURRY_T1") =>
        Effect(
            id,
            new EffectTrigger { Kind = TriggerKind.ON_ATTACK, EveryNth = 5 },
            EffectOp.EXTRA_ATTACK);

    /// <summary><c>PK_MIDAS</c>: gold on every 6th enemy killed, counted over the run.</summary>
    internal static EffectDefinition Midas(string id = "PK_MIDAS_T1") =>
        Effect(
            id,
            new EffectTrigger { Kind = TriggerKind.ON_KILL, EveryNth = 6 },
            EffectOp.GRANT_CURRENCY);

    /// <summary>The default hero — index 0, full HP.</summary>
    internal static EffectTestActor Hero() => EffectTestBattle.Hero();

    /// <summary>A boss — an enemy that <c>TARGET_IS_BOSS</c> answers true for.</summary>
    internal static EffectTestActor Boss(double currentHp = 1000, double maxHp = 1000) =>
        EffectTestBattle.Enemy("BOSS", 1, currentHp, maxHp) with { IsBoss = true };

    /// <summary>An id, spelled the way the wiring contract asks for one.</summary>
    internal static EffectInstanceId Instance(string value) => EffectInstanceId.Of(value);

    /// <summary>A registry over a fresh run — the shape the run controller hands one battle.</summary>
    internal static TriggerRegistry Registry(out RunTriggerCounters counters)
    {
        counters = new RunTriggerCounters();
        return new TriggerRegistry(counters);
    }

    /// <summary>A registry whose run counters the test does not need to inspect.</summary>
    internal static TriggerRegistry Registry() => new(new RunTriggerCounters());

    /// <summary>A moment, stated literally.</summary>
    internal static TriggerOccurrence Moment(TriggerKind kind, int tick) =>
        new() { Kind = kind, Tick = tick };
}

/// <summary>
/// The test double for <see cref="IRunEffectSink"/> — an adapter over
/// <c>CombatLog.AppendRunEffectQueued</c>, without the combat log this layer may not name.
/// </summary>
internal sealed class RecordingRunEffectSink : IRunEffectSink
{
    /// <summary>Everything queued, in the order it was queued — the log order required.</summary>
    internal List<(int Tick, string SourceId, string EffectId, double Argument)> Queued { get; } = [];

    /// <inheritdoc />
    public void QueueRunEffect(int tick, IEffectActorView source, EffectDefinition effect, double argument)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(effect);

        Queued.Add((tick, source.Id, effect.Id, argument));
    }
}
