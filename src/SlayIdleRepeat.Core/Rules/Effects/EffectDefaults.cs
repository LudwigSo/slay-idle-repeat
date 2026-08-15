using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// What an effect with no <c>trigger</c> and no <c>target</c> means, decided once here so the
/// resolver, ops and tagging layers can't each answer differently.
/// </summary>
/// <remarks>
/// <para>
/// An absent trigger resolves to <c>ALWAYS</c>: the DSL partitions every effect into ALWAYS or
/// triggered, and an effect with no trigger has no kind to fire on, so it falls into the only other
/// bucket. This governs build-time passive effects only — a pet's <c>active</c> block also omits a
/// trigger but is driven by its wrapper's cooldown instead, so <see cref="TriggerInstance"/> still
/// refuses an untriggered effect rather than defaulting it.
/// </para>
/// <para>
/// An absent target resolves to <c>SELF</c>: every authored effect that omits <c>target</c> means
/// the holder, never an enemy, so this is not enforced by the content schema — a malformed effect
/// that should have specified a target will silently resolve against the holder instead of failing
/// validation. One consequence: a drawback effect with no target now counts as self-inflicted for
/// ward-bypass purposes.
/// </para>
/// </remarks>
internal static class EffectDefaults
{
    /// <summary>Ruling 1 — the trigger an effect with none resolves under.</summary>
    internal static EffectTrigger Always { get; } = new() { Kind = TriggerKind.ALWAYS };

    // TriggerOf and IsAlwaysActive have no production caller yet; declared now so the tick loop and
    // aggregation pass consume one ruling instead of each re-deriving it.

    /// <summary>Ruling 2 — the target an effect with none resolves against.</summary>
    internal const EffectTarget AbsentTarget = EffectTarget.SELF;

    /// <summary>The effect's trigger, or <see cref="Always"/> where it authors none (ruling 1).</summary>
    internal static EffectTrigger TriggerOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Trigger ?? Always;
    }

    /// <summary>The effect's trigger kind, or <see cref="TriggerKind.ALWAYS"/> (ruling 1).</summary>
    internal static TriggerKind TriggerKindOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Trigger?.Kind ?? TriggerKind.ALWAYS;
    }

    /// <summary>True when the effect is an <c>ALWAYS</c> passive, re-evaluated every resolution pass.</summary>
    internal static bool IsAlwaysActive(EffectDefinition effect) =>
        TriggerKindOf(effect) == TriggerKind.ALWAYS;

    /// <summary>The effect's target, or <see cref="EffectTarget.SELF"/> (ruling 2).</summary>
    internal static EffectTarget TargetOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Target ?? AbsentTarget;
    }
}
