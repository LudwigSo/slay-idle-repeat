namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The read-only view of a combat actor that `18` §4's conditions and `18` §5's targets need — and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Narrow on purpose.</b> Every member below is here because a named clause of `18` §4 or §5
/// reads it. This is <em>not</em> the actor stat block (M2-07, <c>Rules/Stats/</c>) and must not grow
/// into one: the DSL's condition and target layer has no business knowing an actor's ATK, its attack
/// cooldown or its <c>targetPriority</c> (`05` §3.2's hero target selection is M2-08's, and is a
/// different mechanism from <c>LOWEST_HP_ENEMY</c>/<c>HIGHEST_HP_ENEMY</c>).
/// </para>
/// <para>
/// Read-only, because `18` §4 is explicit: <em>"Conditions gate an effect without changing when it is
/// evaluated. All are pure functions of current state."</em> There is no mutating member here, and
/// <c>ConditionPurityRuleTests</c> enforces mechanically that nothing under
/// <c>Rules/Effects/Conditions/</c> mutates anything.
/// </para>
/// </remarks>
internal interface IEffectActorView
{
    /// <summary>The actor's stable identity, used in failure messages and to resolve <c>OWNER</c>.</summary>
    string Id { get; }

    /// <summary>
    /// 🔒 `05` §3.1's fixed actor index — <em>"hero, pets in slot order, enemies by index"</em> — the
    /// one actor ordering the design documents authorise.
    /// </summary>
    /// <remarks>
    /// It is on this interface rather than implied by roster position because it is load-bearing
    /// beyond ordering: it is the tie-break for <c>LOWEST_HP_ENEMY</c> and <c>HIGHEST_HP_ENEMY</c>,
    /// and the candidate order <c>RANDOM_ENEMY</c> draws over. `18` §5 authors no tie-break, and a
    /// tie-break that varied with roster construction would be a `14` §8.2 determinism break, so the
    /// index is reused rather than a new rule invented (steering S6).
    /// </remarks>
    int Index { get; }

    /// <summary>Which side the actor fights for. `18` §5's enemy tokens are relative to this.</summary>
    BattleSide Side { get; }

    /// <summary>Hero, pet or enemy — the three roles `18` §5's tokens distinguish.</summary>
    EffectActorKind Kind { get; }

    /// <summary>
    /// Whether the actor is still in the fight. `05` §3.1 step 6: <em>"An actor whose HP reaches 0
    /// stops acting and being targetable at that moment"</em> — so every `18` §5 enemy token and
    /// <c>ENEMY_COUNT</c> read the living only.
    /// </summary>
    bool IsAlive { get; }

    /// <summary>Current HP. Drives <c>SELF_HP_PCT</c>, <c>TARGET_HP_PCT</c> and the HP-ordered targets.</summary>
    double CurrentHp { get; }

    /// <summary>Maximum HP — the denominator of <c>SELF_HP_PCT</c> and <c>TARGET_HP_PCT</c>.</summary>
    double MaxHp { get; }

    /// <summary>`05` §6.2's elite modifier. Read by <c>TARGET_IS_ELITE</c> and <c>ATTACKER_IS_ELITE</c>.</summary>
    bool IsElite { get; }

    /// <summary>`05` §6.3's boss. Read by <c>TARGET_IS_BOSS</c> and <c>ATTACKER_IS_BOSS</c>.</summary>
    bool IsBoss { get; }

    /// <summary>
    /// Whether the actor was spawned by a <c>SUMMON</c> op (`18` §2.4). Read by
    /// <c>ATTACKER_IS_SUMMON</c>, and the gate on <c>OWNER</c> — `18` §5: <em>"On an actor that is
    /// not a summon, the effect is skipped."</em>
    /// </summary>
    bool IsSummon { get; }

    /// <summary>
    /// The <see cref="Id"/> of this actor's summoner — <c>OWNER</c>'s subject. `18` §5's worked case
    /// is <em>"a sporeling's owner is Sporequeen"</em>. <c>null</c> on an actor that was not summoned.
    /// </summary>
    string? OwnerId { get; }

    /// <summary>
    /// How many stacks of the given `05` §5 status this actor carries; <c>0</c> when it carries none.
    /// </summary>
    /// <remarks>
    /// The single reading behind both <c>STATUS_STACKS</c> and <c>HAS_STATUS</c> — the latter is
    /// <c>stacks &gt; 0</c>. Two members would be two chances for an implementation to answer
    /// <c>HAS_STATUS = true</c> at <c>STATUS_STACKS = 0</c>.
    /// </remarks>
    /// <param name="statusId">A status id from `05` §5. Compared ordinally.</param>
    int StatusStacks(string statusId);
}
