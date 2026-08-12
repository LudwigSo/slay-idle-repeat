using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Values;

/// <summary>🔒 `18` §2.2 — what an effect's <c>value</c> is a multiple of.</summary>
internal static class ValueModeEvaluator
{
    /// <summary>`18` §2.2: <em>"<c>valueMode</c>: <c>ATK_MULT</c> (default)"</em>.</summary>
    internal const ValueMode DamageAndHealingDefault = ValueMode.ATK_MULT;

    /// <summary>The amount <paramref name="value"/> denotes under <paramref name="mode"/>.</summary>
    internal static double Resolve(
        ValueMode mode, double value, ValueModeSubjects subjects, string effectId) =>
        throw new NotImplementedException();
}

/// <summary>The subjects `18` §2.2's eight modes read.</summary>
internal readonly record struct ValueModeSubjects
{
    /// <summary>The effect's source actor.</summary>
    internal IEffectActorView? Source { get; init; }

    /// <summary>The actor the effect is landing on.</summary>
    internal IEffectActorView? Target { get; init; }

    /// <summary>The source's final resolved ATK.</summary>
    internal double? SourceAttack { get; init; }

    /// <summary>The damage just dealt.</summary>
    internal double? DamageDealt { get; init; }

    /// <summary>`05` §4.3's <c>healed</c>.</summary>
    internal double? HealAmount { get; init; }

    /// <summary>`05` §4.3's <c>overheal</c>.</summary>
    internal double? OverhealAmount { get; init; }
}
