using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>One fired stat op's live applications on one target, and the duration/stacking bookkeeping that ends it.</summary>
/// <remarks>
/// <para>
/// A firing effect (an enrage buff, a boss's phase-entry stat buff) does not go through ordinary stat
/// aggregation directly — it lands here, on the target it was authored to modify, and the aggregation
/// refresh reads it back as a synthetic entry in the same pass that reads
/// <see cref="BattleActor.StandingEffects"/>.
/// </para>
/// <para>
/// Modelled on <c>StatusInstance</c>: same immutable <see cref="EffectStackSet"/>, same mutable
/// <see cref="EffectApplication"/> bookkeeping, same reapplication contract — reapplying never
/// re-anchors anything, only the duration timer moves.
/// </para>
/// <para>
/// Keyed by the authored effect id, never a synthesised one: a fired stat op already carries its own
/// repository-unique id, which is what lets a failure message name the real effect.
/// </para>
/// </remarks>
internal sealed class TriggeredStatInstance
{
    /// <summary>The firing effect's id — named in every failure.</summary>
    internal required string EffectId { get; init; }

    /// <summary>Which stat op fired.</summary>
    internal required EffectOp Op { get; init; }

    /// <summary>The stat the op names.</summary>
    internal required StatId Stat { get; init; }

    /// <summary>The live applications, combined by the effect's stacking mode. Replaced on every reapplication, since <see cref="EffectStackSet"/> is immutable by design.</summary>
    internal required EffectStackSet Stacks { get; set; }

    /// <summary>The duration bookkeeping — what ends the instance. Rewritten by a reapplication that refreshes, which moves the timer only.</summary>
    internal required EffectApplication Application { get; set; }

    /// <summary>One reapplication: stacking and duration refresh, and nothing else — the same split <c>StatusInstance.Reapply</c> keeps.</summary>
    /// <param name="value">The fire-time, scaled value of this application.</param>
    /// <param name="application">The duration bookkeeping this application would install.</param>
    internal StackApplication Reapply(double value, EffectApplication application)
    {
        var applied = Stacks.Apply(value);

        Stacks = applied.Stacks;

        if (applied.RefreshDuration)
        {
            Application = application;
        }

        return applied;
    }
}
