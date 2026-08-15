namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>The read-only view of a combat actor that conditions and targets need — and nothing else.</summary>
/// <remarks>
/// <para>
/// Narrow on purpose, and not the actor stat block: the condition/target layer has no business
/// knowing an actor's ATK, attack cooldown or target priority (hero target selection is a separate
/// mechanism from <c>LOWEST_HP_ENEMY</c>/<c>HIGHEST_HP_ENEMY</c>). The stat block is expected to
/// implement this interface, not duplicate a second actor abstraction over the same roster — two
/// views of one battle are two chances for <c>ENEMY_COUNT</c> and stat aggregation to disagree about
/// who is alive.
/// </para>
/// <para>
/// Read-only, because conditions gate an effect without changing when it's evaluated — pure
/// functions of current state — enforced mechanically by <c>ConditionPurityRuleTests</c>.
/// </para>
/// </remarks>
internal interface IEffectActorView
{
    /// <summary>The actor's stable identity, used in failure messages and to resolve <c>OWNER</c>.</summary>
    /// <remarks>
    /// Unique within one battle roster. <c>OWNER</c> matches <see cref="OwnerId"/> against this and
    /// takes the first hit, so two actors sharing an id would give a summoned unit the wrong owner —
    /// which matters because a single archetype draw can spawn several units at once.
    /// </remarks>
    string Id { get; }

    /// <summary>The actor's fixed index — hero, pets in slot order, enemies by index.</summary>
    /// <remarks>
    /// Load-bearing beyond ordering: it's the tie-break for <c>LOWEST_HP_ENEMY</c> and
    /// <c>HIGHEST_HP_ENEMY</c>, and the candidate order <c>RANDOM_ENEMY</c> draws over. A tie-break
    /// that varied with roster construction would be a determinism break.
    /// </remarks>
    int Index { get; }

    /// <summary>Which side the actor fights for. The enemy tokens are relative to this.</summary>
    BattleSide Side { get; }

    /// <summary>Hero, pet or enemy.</summary>
    EffectActorKind Kind { get; }

    /// <summary>Whether the actor is still in the fight. Every enemy token and <c>ENEMY_COUNT</c> read the living only.</summary>
    bool IsAlive { get; }

    /// <summary>Current HP. Drives <c>SELF_HP_PCT</c>, <c>TARGET_HP_PCT</c> and the HP-ordered targets.</summary>
    double CurrentHp { get; }

    /// <summary>Maximum HP — the denominator of <c>SELF_HP_PCT</c> and <c>TARGET_HP_PCT</c>.</summary>
    double MaxHp { get; }

    /// <summary>Elite modifier. Read by <c>TARGET_IS_ELITE</c> and <c>ATTACKER_IS_ELITE</c>.</summary>
    bool IsElite { get; }

    /// <summary>Boss flag. Read by <c>TARGET_IS_BOSS</c> and <c>ATTACKER_IS_BOSS</c>.</summary>
    bool IsBoss { get; }

    /// <summary>Whether the actor was spawned by a <c>SUMMON</c> op. Also the gate on <c>OWNER</c>, which skips a non-summon.</summary>
    bool IsSummon { get; }

    /// <summary>The <see cref="Id"/> of this actor's summoner — <c>OWNER</c>'s subject. <c>null</c> on an actor that was not summoned.</summary>
    string? OwnerId { get; }

    /// <summary>How many stacks of the given status this actor carries; <c>0</c> when it carries none.</summary>
    /// <remarks>
    /// The single reading behind both <c>STATUS_STACKS</c> and <c>HAS_STATUS</c> (<c>stacks &gt; 0</c>),
    /// so an implementation can't answer <c>HAS_STATUS = true</c> at <c>STATUS_STACKS = 0</c>.
    /// </remarks>
    /// <param name="statusId">A status id. Compared ordinally.</param>
    int StatusStacks(string statusId);
}
