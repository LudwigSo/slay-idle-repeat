namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// Which of a battle's two sides an actor fights for — the axis every `18` §5 enemy token is
/// resolved across.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Two sides, not "the hero and the monsters".</b> `05` §3.3 rules a Ghost Duel to be
/// <em>"the same code path with two hero-shaped sides"</em>, so the opposing hero of a duel occupies
/// <see cref="ENEMY"/>. That is what makes `05` §3.3's <em>"<c>ENEMY_COUNT</c> is always 1"</em> and
/// its <em>"target-conditional effects read the opposing hero"</em> fall out of the same code that
/// serves a five-enemy PvE pack, rather than needing a duel-shaped branch.
/// </para>
/// <para>
/// 🔒 <b>This is the seam M2-08 should reuse rather than restate.</b> The tick loop needs the same
/// two-sided roster, and two enums that must agree is the duplicated-vocabulary failure `18`'s
/// single-DSL rule exists to prevent.
/// </para>
/// <para>
/// 🔒 Wire-safe numbering, as every DSL enum: no <c>0</c> member, so an uninitialised field cannot
/// read as a real side.
/// </para>
/// </remarks>
internal enum BattleSide
{
    /// <summary>The hero's side — hero, pets, and in a duel the attacking hero (`05` §3.3).</summary>
    HERO = 1,

    /// <summary>
    /// The opposing side — enemies, elites, bosses and summons in PvE; the defending hero and its
    /// pets in a duel (`05` §3.3).
    /// </summary>
    ENEMY = 2,
}
