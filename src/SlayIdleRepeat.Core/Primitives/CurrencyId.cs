namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 The eight wallet currencies `10` §1 fixes, transcribed in <c>game-data/tuning/currencies.json</c>
/// under <c>wallet</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>An enum rather than a content-driven string id</b>, because the set is closed by a 🔒 design
/// decision rather than authored per patch. Three things follow from that and none of them would
/// hold for a string: a payout can be written as an exhaustive <c>switch</c> the compiler checks;
/// <c>CanonicalStateWriter</c> already encodes an enum and already imposes an ascending order on an
/// enum-keyed map (`14` §16.6), so a wallet hashes deterministically with no extra rule; and
/// <c>DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged</c> can recognise a currency
/// field by its type instead of guessing from its name.
/// </para>
/// <para>
/// 🔒 <b>The numbers are wire values.</b> Same rule as <see cref="RejectionReason"/>: the encoder
/// writes the number, never the name, so renumbering rewrites every <c>stateHash</c> that has ever
/// carried a wallet. Append, never renumber, never reuse. No <c>0</c> member, so an uninitialised
/// field cannot read as <see cref="GOLD"/> and credit the wrong balance.
/// </para>
/// <para>
/// ⚠️ <b>What is deliberately absent.</b> <c>BEAST_MARKS</c> and <c>SET_TOKENS</c> are
/// <c>nonWalletCounters</c> (`10` §1.1), not currencies — a counter with a wallet slot would start
/// emitting <c>CurrencyChanged</c> and land in the income-attribution report (`21` §8.3) as income
/// it never was.
/// </para>
/// <para>
/// ⚠️ `16` O10 asks whether Merge Dust and Enhance Stones merge into one currency, taking this list
/// from eight to seven. It is scheduled for a post-playtest review in <b>M18</b> and is not
/// pre-empted here.
/// </para>
/// <para>
/// Note that <see cref="GOLD"/> is <c>RUN</c>-scoped while the other seven are player-scoped
/// (`10` §1, recorded as milestone assumption A3). That splits the <i>storage</i> between the
/// <c>Run</c> and <c>Player</c> aggregates in M1-04/M1-05; it does not split this enum, which is
/// the vocabulary both use.
/// </para>
/// </remarks>
public enum CurrencyId
{
    /// <summary>Run-scoped soft currency — the in-run shop, spent or lost when the run ends.</summary>
    GOLD = 1,

    /// <summary>The meta soft currency: merging, pet levelling, the Crowns shop tab.</summary>
    CROWNS = 2,

    /// <summary>The premium currency (`10` §2).</summary>
    SOUL_SHARDS = 3,

    /// <summary>
    /// The run gate (`10` §3 — 20 per run). Scope <c>GATE</c> rather than <c>META</c>. The
    /// overflow bank that receives what the main bar cannot hold is `28` Part C, and it is
    /// M1-10's, not a second currency.
    /// </summary>
    ENERGY = 4,

    /// <summary>Gear enhancement material.</summary>
    ENHANCE_STONES = 5,

    /// <summary>Gear merge material.</summary>
    MERGE_DUST = 6,

    /// <summary>Pet feed.</summary>
    BEAST_FEED = 7,

    /// <summary>The PvP currency (`11` §7's Honor Shop).</summary>
    HONOR = 8,
}
