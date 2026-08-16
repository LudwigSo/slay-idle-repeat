namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>What produced an in-run gear drop, and therefore which dry-streak breaker protects it.</summary>
/// <remarks>
/// <para>
/// Three members, because the in-run drop class has exactly three live sources: an ordinary enemy
/// kill, an Elite kill and a boss kill. Elites and bosses each carry their own dry-streak breaker
/// over their own counter; ordinary kills carry none, which is why the third member exists rather
/// than being modelled as "no trigger".
/// </para>
/// <para>
/// ⚠️ <b>Treasure tiles are deliberately absent.</b> They never drop gear — the tile's own payout
/// rules say so and the resolver that pays them repeats it — even though the source-class table
/// still lists them under this class. That row is stale, and adding a member for it here would make
/// the stale row look decided.
/// </para>
/// </remarks>
internal enum RunDropTrigger
{
    /// <summary>An ordinary enemy kill. Protected by no dry-streak breaker.</summary>
    NORMAL_ENEMY,

    /// <summary>An Elite kill. Protected by the elite dry-streak breaker.</summary>
    ELITE,

    /// <summary>A boss kill. Protected by the boss dry-streak breaker.</summary>
    BOSS,
}
