namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The FTUE beat a player's tutorial has reached: <c>beatId ∈ B0…B10 plus B6b</c>, twelve values, in script order.</summary>
/// <remarks>
/// Lives on the <c>Player</c> aggregate as <c>ftueProgress { completedAtUtc | null, beatId }</c>. The
/// list is closed by the authored script rather than by a patch, so it is an enum for the same
/// reasons <see cref="CurrencyId"/> is one.
/// <para>
/// It lives in <c>Primitives/</c> rather than beside the aggregate in <c>Model/Player/</c> because a
/// public enum under <c>Core/Model/</c> fails the accessibility-boundary test on its compiler-generated,
/// public, mutable <c>value__</c> backing field.
/// </para>
/// <para>
/// The numeric values are wire values: <c>CanonicalStateWriter</c> bakes them into every
/// <c>stateHash</c>. Append, never renumber, never reuse. There is deliberately no <c>0</c> member,
/// so <c>default(FtueBeat)</c> cannot read as "the player is at beat 0, about to enter their name" —
/// <c>Player.Rehydrate</c> refuses the zero.
/// </para>
/// <para>
/// <see cref="B6B"/> is spelled with a capital because C# has no case-only distinction to protect,
/// and it sits between <see cref="B6"/> and <see cref="B7"/> because that is where the script puts it.
/// </para>
/// <para>
/// There is no <c>COMPLETE</c> member: the tutorial completes by setting <c>completedAtUtc</c> once
/// beat 10's spend commits, so completion is a timestamp rather than a thirteenth beat — a member for
/// it would give <c>(B10, null)</c> and <c>(COMPLETE, null)</c> two spellings of one state.
/// </para>
/// </remarks>
public enum FtueBeat
{
    /// <summary>Beat 0: name entry.</summary>
    B0 = 1,

    /// <summary>Beat 1.</summary>
    B1 = 2,

    /// <summary>Beat 2. Skip becomes available after this beat.</summary>
    B2 = 3,

    /// <summary>Beat 3.</summary>
    B3 = 4,

    /// <summary>Beat 4: the Treasure tile.</summary>
    B4 = 5,

    /// <summary>Beat 5: the tutorial shop.</summary>
    B5 = 6,

    /// <summary>Beat 6: the elite.</summary>
    B6 = 7,

    /// <summary>Beat 6b.</summary>
    B6B = 8,

    /// <summary>Beat 7: the mini-boss.</summary>
    B7 = 9,

    /// <summary>Beat 8: the tally.</summary>
    B8 = 10,

    /// <summary>Beat 9: Home, the forced equip.</summary>
    B9 = 11,

    /// <summary>Beat 10: the forced Talent Point spend. The tutorial completes when this beat's spend commits.</summary>
    B10 = 12,
}
