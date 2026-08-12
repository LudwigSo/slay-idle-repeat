namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// What an actor <em>is</em>, to the extent `18` §5 needs to know: the three roles its target tokens
/// distinguish.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a taxonomy of content. It exists because three `18` §5 tokens key on the
/// role and nothing else: <c>ALL_PETS</c> selects <see cref="PET"/> on the holder's side, the enemy
/// tokens exclude <see cref="PET"/> (`05` §3.2: <em>"Pets cannot be targeted or killed"</em>), and
/// <see cref="HERO"/> is what tells a side that <em>can</em> hold pets from one that cannot.
/// </para>
/// <para>
/// Elite, boss and summon are <b>not</b> members here: `18` §4 reads them as three independent
/// booleans on <see cref="IEffectActorView"/>, and a boss that summons is both. Folding them into one
/// enum would make <c>TARGET_IS_BOSS</c> and <c>ATTACKER_IS_SUMMON</c> mutually exclusive, which the
/// document does not say.
/// </para>
/// <para>
/// 🔒 No <c>0</c> member, as every DSL enum.
/// </para>
/// </remarks>
internal enum EffectActorKind
{
    /// <summary>A hero. One per side at most; in a duel there is one on each (`05` §3.3).</summary>
    HERO = 1,

    /// <summary>
    /// A pet. Never basic-attacks, never targetable, never killable (`05` §3.2) — it appears in the
    /// roster only so <c>ALL_PETS</c> and the pet auras have something to name.
    /// </summary>
    PET = 2,

    /// <summary>An enemy, elite, boss or summon.</summary>
    ENEMY = 3,
}
