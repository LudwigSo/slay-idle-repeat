using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Perks;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>What one slot of a shop's offer is selling.</summary>
/// <param name="Kind">Which of `03` §7's four pools this slot drew from.</param>
/// <param name="ItemId">
/// The id inside that pool — a perk id, a consumable id, a run buff id. <c>null</c> for the Heal
/// slot, which sells a fixed effect rather than a catalogue row.
/// </param>
/// <param name="Rarity">The perk's rarity, for slot 1. <c>null</c> for every other slot.</param>
/// <param name="Price">
/// What it costs in run-local Gold, with the run's own shop-price modifiers already applied — the
/// number <c>SHOP_BUY</c> charges and the client shows.
/// </param>
internal readonly record struct RunShopSlot(
    ShopItemKind Kind, string? ItemId, ShopRarity? Rarity, long Price);

/// <summary>
/// A shop tile's four-slot offer, drawn from the run's own <c>shop</c> stream and re-derivable from
/// the position it was drawn at.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Derived, never persisted.</b> A run stores the stream position its live offer was drawn at
/// (<c>Run.ShopOfferDraw</c>) and nothing else; both the screen and <c>SHOP_BUY</c> re-derive the
/// four rows from it. That is the board's and the shrine's model, and it exists so a stored offer
/// and the seed cannot disagree — a shop that showed one thing and charged for another is exactly
/// the failure a second copy of derived data produces.
/// </para>
/// <para>
/// 🔒 <b>Exactly four draws, in slot order, whatever the pools contain.</b> The number of draws a
/// shop spends is part of the run's determinism: a slot that skipped its draw because its pool was
/// empty would leave every later shop, treasure and cache in the run one index out of step. So each
/// pooled slot always draws, and an empty pool produces a slot with nothing to sell rather than a
/// draw that never happened.
/// </para>
/// <para>
/// The perk slot draws a rarity off `06` §4's stage table and then a row of that rarity. It is the
/// DRAFT's table because `03` §7 says the slot is "rarity-weighted" and authors no table of its own,
/// and inventing a second distribution for the same question is what steering S6 forbids. Perks the
/// run already owns at their top tier are excluded from the row draw — a slot selling something the
/// player cannot benefit from is a slot that is not there.
/// </para>
/// </remarks>
internal static class RunShopOffer
{
    /// <summary>The three consumables of `03` §7.1, in the document's order — slot 2's pool.</summary>
    /// <remarks>
    /// A code list rather than a read of the price table's keys: the price table is a map, whose key
    /// order is not a fact the document states, and drawing an index into it would make the offer
    /// depend on JSON member ordering. Cross-checked against the authored table by
    /// <c>ShopOfferTests</c>.
    /// </remarks>
    internal static IReadOnlyList<string> ConsumablePool { get; } = Array.AsReadOnly(new[]
    {
        Consumables.HealthDraught,
        Consumables.DraftToken,
        Consumables.EscapeRope,
    });

    /// <summary>The slot index of each of `03` §7's four slots.</summary>
    internal const int PerkSlot = 0;

    /// <inheritdoc cref="PerkSlot"/>
    internal const int ConsumableSlot = 1;

    /// <inheritdoc cref="PerkSlot"/>
    internal const int RunBuffSlot = 2;

    /// <inheritdoc cref="PerkSlot"/>
    internal const int HealSlot = 3;

    /// <summary>
    /// Draws one offer off <paramref name="stream"/>, consuming exactly
    /// <see cref="DrawsPerOffer"/> draw indices.
    /// </summary>
    /// <param name="run">The run standing at the shop — read for its chapter, stage, owned perks and price modifiers.</param>
    /// <param name="content">The version-stamped snapshot the pools and prices are read from.</param>
    /// <param name="stream">
    /// The <c>shop</c> stream, positioned where this offer begins. 🔒 The SAME method serves both
    /// callers — the one stocking a shop, which passes the run's live stream and lets the draws
    /// commit, and the one re-reading an offer already drawn, which passes a stream reopened at the
    /// recorded position and never folds it back. One method, because a second derivation is how a
    /// shop ends up showing one thing and charging for another.
    /// </param>
    /// <returns>The four slots, in slot order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static IReadOnlyList<RunShopSlot> Draw(
        RunShopContext shop, ContentSnapshot content, DeterministicRng stream)
    {
        ArgumentNullException.ThrowIfNull(shop);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stream);

        var tuning = ShopTuning.Read(content);

        var slots = new List<RunShopSlot>(4)
        {
            PerkRow(shop, content, tuning, stream),
            ConsumableRow(shop, content, tuning, stream),
            RunBuffRow(shop, content, tuning, stream),
            HealRow(shop, content, tuning),
        };

        return slots;
    }

    /// <inheritdoc cref="Draw(RunShopContext, ContentSnapshot, DeterministicRng)"/>
    internal static IReadOnlyList<RunShopSlot> Draw(
        Run run, ContentSnapshot content, DeterministicRng stream) =>
        Draw(RunShopContext.Of(run), content, stream);

    /// <summary>
    /// Re-derives the offer a shop opened at <paramref name="drawPosition"/> is showing.
    /// </summary>
    /// <remarks>
    /// Reopens the stream and never folds it back, on <c>ShrineView</c>'s precedent: these are draws
    /// the run has ALREADY spent. Whoever advances the stream is whoever opened or refreshed the shop.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static IReadOnlyList<RunShopSlot> DrawAt(Run run, ContentSnapshot content, ulong drawPosition)
    {
        ArgumentNullException.ThrowIfNull(run);

        return DrawAt(RunShopContext.Of(run), content, drawPosition);
    }

    /// <inheritdoc cref="DrawAt(Run, ContentSnapshot, ulong)"/>
    internal static IReadOnlyList<RunShopSlot> DrawAt(
        RunShopContext shop, ContentSnapshot content, ulong drawPosition)
    {
        ArgumentNullException.ThrowIfNull(shop);

        return Draw(
            shop, content, DeterministicRng.OpenAt(shop.RunSeed, RngStreams.Shop, drawPosition));
    }

    /// <summary>
    /// How many draw indices one offer spends: a rarity and a row for the perk slot, one each for
    /// the consumable and run-buff slots, and none at all for the Heal, which is always available.
    /// </summary>
    /// <remarks>
    /// A named constant AND an assertion about <see cref="Draw"/>, pinned together by
    /// <c>ShopOfferTests</c>: a refresh restocks by drawing the next block off the live stream, so
    /// if the two ever disagreed the refreshed shop would overlap the offer it just replaced.
    /// </remarks>
    internal const int DrawsPerOffer = 4;

    /// <summary>
    /// The stage index (`03` §7's 0/1/2) the run is being priced at.
    /// </summary>
    /// <remarks>
    /// Read off the PENDING TILE's stage, not a stage field on the run — there is none — and clamped
    /// into the formula's range. A shop tile can only be pending on a real stage, but the boss node
    /// carries a stage value of its own that is outside 1..3, and a clamp is what stops a
    /// hypothetical shop reached from there throwing out of a pricing formula.
    /// </remarks>
    internal static int StageIndexOf(int pendingTileStage) =>
        Math.Clamp(pendingTileStage - 1, ShopPricing.MinStageIndex, ShopPricing.MaxStageIndex);

    private static RunShopSlot PerkRow(
        RunShopContext shop, ContentSnapshot content, ShopTuning tuning, DeterministicRng stream)
    {
        // The rarity is drawn first and ALWAYS, so the number of draws is fixed regardless of what
        // the catalogue holds — see the type remarks.
        var weights = DraftRarityWeights.For(shop.StageIndex + 1, isElite: false, isBoss: false);
        var rarity = WeightedPick(weights, stream);
        var rows = Buyable(PerkCatalogue.Read(content), rarity, shop);

        // The row draw is spent whether or not there is anything to draw, for the same reason.
        var index = stream.Range(0, Math.Max(1, rows.Count));

        return rows.Count == 0
            ? new RunShopSlot(ShopItemKind.PERK, ItemId: null, ShopRarityOf(rarity), Price: 0)
            : new RunShopSlot(
                ShopItemKind.PERK,
                rows[index].Id,
                ShopRarityOf(rarity),
                PriceOf(shop, content, ShopItemKind.PERK, key: null, ShopRarityOf(rarity), tuning));
    }

    private static RunShopSlot ConsumableRow(
        RunShopContext shop, ContentSnapshot content, ShopTuning tuning, DeterministicRng stream)
    {
        var id = ConsumablePool[stream.Range(0, ConsumablePool.Count)];

        return new RunShopSlot(
            ShopItemKind.CONSUMABLE,
            id,
            Rarity: null,
            PriceOf(shop, content, ShopItemKind.CONSUMABLE, id, rarity: null, tuning));
    }

    private static RunShopSlot RunBuffRow(
        RunShopContext shop, ContentSnapshot content, ShopTuning tuning, DeterministicRng stream)
    {
        var pool = tuning.RunBuffs;
        var index = stream.Range(0, Math.Max(1, pool.Count));

        return pool.Count == 0
            ? new RunShopSlot(ShopItemKind.RUN_BUFF, ItemId: null, Rarity: null, Price: 0)
            : new RunShopSlot(
                ShopItemKind.RUN_BUFF,
                pool[index].Id,
                Rarity: null,
                PriceOf(shop, content, ShopItemKind.RUN_BUFF, pool[index].Id, rarity: null, tuning));
    }

    /// <summary>Slot 4 — "always available" (`03` §7), so it draws nothing at all.</summary>
    private static RunShopSlot HealRow(
        RunShopContext shop, ContentSnapshot content, ShopTuning tuning) =>
        new(
            ShopItemKind.HEAL,
            ItemId: null,
            Rarity: null,
            PriceOf(shop, content, ShopItemKind.HEAL, key: null, rarity: null, tuning));

    private static long PriceOf(
        RunShopContext shop,
        ContentSnapshot content,
        ShopItemKind kind,
        string? key,
        ShopRarity? rarity,
        ShopTuning tuning) =>
        RunModifierTotals.ScaleShopPrice(
            shop.ShrineBuffs,
            shop.Curses,
            content,
            ShopPricing.Price(kind, key, rarity, shop.StageIndex, shop.ChapterId, tuning));

    /// <summary>
    /// The catalogue rows of one rarity this run could still benefit from: everything it does not
    /// already own at the top tier that perk authors.
    /// </summary>
    private static IReadOnlyList<PerkCatalogueEntry> Buyable(
        PerkCatalogue catalogue, PerkRarity rarity, RunShopContext shop)
    {
        var rows = new List<PerkCatalogueEntry>();

        foreach (var row in catalogue.OfRarity(rarity))
        {
            if (shop.OwnedPerkTiers.TierOf(row.Id) < row.TierCount)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>One weighted pick over the rarity table, in the table's own order.</summary>
    /// <remarks>
    /// Spends exactly one draw whatever the table looks like, which is what keeps the offer's draw
    /// count fixed. The weights are authored as <c>double</c> and summed in the table's order so
    /// client and server accumulate identically.
    /// </remarks>
    private static PerkRarity WeightedPick(
        IReadOnlyList<(PerkRarity Rarity, double Weight)> weights, DeterministicRng stream)
    {
        var total = 0.0;

        foreach (var (_, weight) in weights)
        {
            total += weight;
        }

        // Scaled to an integer draw rather than a fractional one: DeterministicRng's contract is one
        // index per call, and a double draw would be a second sampling shape in the same stream.
        var roll = stream.Range(0, (int)Math.Max(1, Math.Round(total, MidpointRounding.ToEven)));
        var running = 0.0;

        foreach (var (candidate, weight) in weights)
        {
            running += weight;

            if (roll < running)
            {
                return candidate;
            }
        }

        // Unreachable while the weights sum to what the roll was bounded by; returns the last row
        // rather than throwing, because a rounding edge must not brick a shop.
        return weights[^1].Rarity;
    }

    /// <summary>The pricing enum's spelling of a catalogue rarity.</summary>
    /// <remarks>
    /// Two enums for one concept, and this is the one seam between them: <see cref="ShopRarity"/> is
    /// the pricing table's key and predates the perk catalogue, which has its own
    /// <see cref="PerkRarity"/>. Mapped by name rather than by ordinal, so a member added to either
    /// in a different position cannot silently reprice a band.
    /// </remarks>
    internal static ShopRarity ShopRarityOf(PerkRarity rarity) => rarity switch
    {
        PerkRarity.Common => ShopRarity.COMMON,
        PerkRarity.Rare => ShopRarity.RARE,
        PerkRarity.Epic => ShopRarity.EPIC,
        PerkRarity.Legendary => ShopRarity.LEGENDARY,
        _ => throw new ArgumentOutOfRangeException(
            nameof(rarity), rarity, "06 §1.1 authors exactly four perk rarities."),
    };
}
