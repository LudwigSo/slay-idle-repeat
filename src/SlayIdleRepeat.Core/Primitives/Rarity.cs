namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The item rarity ladder: <c>C</c>, <c>B</c>, <c>A</c>, <c>S</c>, <c>SS</c>.</summary>
/// <remarks>
/// <para>
/// Ordered ascending on purpose, so <em>"A or better"</em> is <c>rarity &gt;= Rarity.A</c> and a
/// rarity floor is a comparison rather than a lookup table. Every pity guarantee in the game is
/// phrased as an at-least, so the ordering is the operation.
/// </para>
/// <para>
/// Not <c>PerkRarity</c> and not <c>ShopRarity</c>: those two name the four perk bands
/// (COMMON/RARE/EPIC/LEGENDARY), which are a different ladder over different things. This one is
/// the gear/pet/mount ladder the guarantee tables are authored in, and the two vocabularies are
/// deliberately kept apart rather than unified on a guess.
/// </para>
/// <para>
/// The numeric values are wire values — a stored counter key, an event payload and an analytics row
/// all carry a rarity — and they are also the ordering. Append, never renumber, never reuse.
/// </para>
/// <para>
/// There is deliberately no <c>0</c> member, so <c>default(Rarity)</c> cannot read as
/// <see cref="C"/> and turn an uninitialised column into a real grant at the bottom of the ladder.
/// </para>
/// </remarks>
public enum Rarity
{
    /// <summary>The bottom of the ladder. Rolls no affixes.</summary>
    C = 1,

    /// <summary>The second band.</summary>
    B = 2,

    /// <summary>The third band, and the one most hard-pity guarantees start at.</summary>
    A = 3,

    /// <summary>The fourth band.</summary>
    S = 4,

    /// <summary>The top of the ladder, and the target of every soft-pity curve that has one.</summary>
    SS = 5,
}
