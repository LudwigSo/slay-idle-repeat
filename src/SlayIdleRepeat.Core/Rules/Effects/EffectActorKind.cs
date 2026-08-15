namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>What an actor <em>is</em>, to the extent target/condition tokens need to know.</summary>
/// <remarks>
/// Elite, boss and summon are deliberately not members here — they're independent booleans on
/// <see cref="IEffectActorView"/> instead, since a boss that summons is both, and folding them into
/// this enum would make them mutually exclusive. No <c>0</c> member.
/// </remarks>
internal enum EffectActorKind
{
    /// <summary>A hero. One per side at most; in a duel there is one on each.</summary>
    HERO = 1,

    /// <summary>A pet. Never basic-attacks, never targetable, never killable.</summary>
    PET = 2,

    /// <summary>An enemy, elite, boss or summon.</summary>
    ENEMY = 3,
}
