namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The eight wallet currencies, transcribed in <c>game-data/tuning/currencies.json</c> under <c>wallet</c>.</summary>
/// <remarks>
/// An enum rather than a content-driven string id: the set is closed, so a payout can be an exhaustive
/// <c>switch</c>, <c>CanonicalStateWriter</c> hashes it deterministically, and a currency mutation can be
/// recognised by its field type. The numeric values are wire values baked into every <c>stateHash</c> —
/// append, never renumber, never reuse. There is no <c>0</c> member, so an uninitialised field cannot
/// silently read as <see cref="GOLD"/>.
/// <para>
/// <c>BEAST_MARKS</c> and <c>SET_TOKENS</c> are deliberately absent: they are non-wallet counters, and a
/// wallet slot for them would start emitting <c>CurrencyChanged</c> as if they were income.
/// </para>
/// <para><see cref="GOLD"/> is run-scoped; the other seven are player-scoped.</para>
/// </remarks>
public enum CurrencyId
{
    /// <summary>Run-scoped soft currency — the in-run shop, spent or lost when the run ends.</summary>
    GOLD = 1,

    /// <summary>The meta soft currency: merging, pet levelling, the Crowns shop tab.</summary>
    CROWNS = 2,

    /// <summary>The premium currency.</summary>
    SOUL_SHARDS = 3,

    /// <summary>The run gate. Scope <c>GATE</c> rather than <c>META</c>.</summary>
    ENERGY = 4,

    /// <summary>Gear enhancement material.</summary>
    ENHANCE_STONES = 5,

    /// <summary>Gear merge material.</summary>
    MERGE_DUST = 6,

    /// <summary>Pet feed.</summary>
    BEAST_FEED = 7,

    /// <summary>The PvP currency.</summary>
    HONOR = 8,
}
