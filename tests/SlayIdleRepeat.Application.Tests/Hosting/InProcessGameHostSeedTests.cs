using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The seed a meta command is issued has to be <em>fresh</em>, not merely present. A host that hands
/// every drawing command one constant is accepted everywhere, commits everywhere and reports nothing:
/// the whole defect is that every draw for the life of the host comes out the same.
/// </summary>
/// <remarks>
/// <para>
/// Read through <c>ENHANCE</c>, the one handled command whose single draw decides an outcome the
/// persisted row records. The item stands at the level whose authored success rate is a coin flip, so
/// the attempt lands or does not purely on the draw — everything else about the sends is equal,
/// including the stones each of them spends.
/// </para>
/// <para>
/// <b>Both shapes are needed, and neither alone is evidence.</b> <see cref="IIdGeneratorPort"/>
/// promises that the whole identifier is unique and says nothing about any half of it, so a generator
/// may vary its trailing bytes — the shipped fake counts there behind a fixed prefix — or its leading
/// ones, and both conform. A host reading one half draws one constant under the generator that varies
/// the other, and only the case built on that generator can see it.
/// </para>
/// <para>
/// Asserted as "the outcomes are not all the same" rather than as named values per draw, deliberately:
/// which seed wins is a fact about how the halves are combined, and the arithmetic of that fold is the
/// host's own business. What is not its business is issuing a seed that never moves.
/// </para>
/// </remarks>
public sealed class InProcessGameHostSeedTests
{
    /// <summary>The eight bytes a trailing-half generator repeats on every draw — the shipped fake's prefix.</summary>
    private const ulong SharedLeadingHalf = 0x1122_3344_5566_7788UL;

    /// <summary>The eight bytes a leading-half generator repeats on every draw.</summary>
    private const ulong SharedTrailingHalf = 0x99AA_BBCC_DDEE_FF00UL;

    /// <summary>
    /// How many commands each case sends. Enough that a host drawing genuinely fresh seeds reaches
    /// both faces of the coin, and every one of them is a command the constant-seed host answers
    /// identically.
    /// </summary>
    private const int DrawsInspected = 8;

    /// <summary>
    /// The enhancement level whose next attempt is authored as a coin flip. The level is this case's
    /// choice; the rate at it is the content's, and the case checks it is still a flip.
    /// </summary>
    private const int CoinFlipLevel = 10;

    private static readonly GearInstanceId TheItem = new("GEARINST_COINFLIP");

    // ══════════════════════════════════════════════════ the seed moves, under either shape

    /// <summary>
    /// Commands sent under a generator shaped like the shipped fake — one constant prefix, a counter
    /// after it — do not all reach the same draw.
    /// </summary>
    /// <remarks>
    /// This is the shape a host folding only the leading eight bytes reads as one seed for its whole
    /// life: that prefix never moves, so every drawing command in the process rolls the identical die
    /// and nothing refuses, logs or reports it.
    /// </remarks>
    [Fact]
    public async Task SubmitAsync_moves_the_seed_when_the_generator_varies_only_its_trailing_bytes()
    {
        await TheDrawsDisagree(
            ShapedIdGenerator.VaryingTheTrailingBytes(SharedLeadingHalf, Counted()),
            "its trailing");
    }

    /// <summary>…and neither do commands under a generator that varies the other half instead.</summary>
    /// <remarks>
    /// The mirror image, and it is what makes the pair evidence rather than an accident: a host
    /// folding only the trailing eight bytes satisfies the case above and fails here.
    /// </remarks>
    [Fact]
    public async Task SubmitAsync_moves_the_seed_when_the_generator_varies_only_its_leading_bytes()
    {
        await TheDrawsDisagree(
            ShapedIdGenerator.VaryingTheLeadingBytes(SharedTrailingHalf, Counted()),
            "its leading");
    }

    // ═════════════════════════════════════════════════════════════ the negative control

    /// <summary>One guid handed out repeatedly produces one state, byte for byte.</summary>
    /// <remarks>
    /// Without it, "the outcomes were not all alike" above is equally what a host with any ambient
    /// non-determinism in it would produce — a clock read, a stray guid, an unordered map — and the
    /// disagreement would say nothing about the seed. With it, the outcome is a function of the guid
    /// alone, so a disagreement can only be the seed folded out of it.
    /// </remarks>
    [Fact]
    public async Task SubmitAsync_reaches_one_state_every_time_the_same_guid_is_issued()
    {
        var repeated = Enumerable.Repeat(1UL, DrawsInspected).ToArray();

        var rows = await Attempts(
            ShapedIdGenerator.VaryingTheTrailingBytes(SharedLeadingHalf, repeated));

        rows.Select(Worlds.Hash).Distinct(StringComparer.Ordinal).Count().ShouldBe(
            1,
            "one guid reached " + DrawsInspected + " commands and they did not all land on one " +
            "state, so something other than the seed steers this draw and the cases above are " +
            "reading that instead of the seed.");

        var landed = Landed(rows);

        (landed == 0 || landed == DrawsInspected).ShouldBeTrue(
            landed + " of " + DrawsInspected + " attempts landed under one guid. The field the cases " +
            "above read has to be the field that agreed with itself here, or this controls something " +
            "else than they assert.");
    }

    // ═════════════════════════════════════════════════════════════════ the shared body

    /// <summary>
    /// <see cref="DrawsInspected"/> attempts, each from its own copy of one starting row, sharing one
    /// generator — so the guid each command is issued is the only thing that differs between them.
    /// </summary>
    /// <param name="ids">The generator, already laid out with the shape under test.</param>
    /// <param name="half">Which half of its guids that generator moves, for the failure message.</param>
    private static async Task TheDrawsDisagree(IIdGeneratorPort ids, string half)
    {
        var rows = await Attempts(ids);

        Landed(rows).ShouldBeInRange(
            1,
            DrawsInspected - 1,
            DrawsInspected + " commands under a generator that moves " + half + " bytes all reached " +
            "the same draw. Every one of them was accepted and every one of them was paid for, so " +
            "the host is issuing one seed for its whole life and the only thing that reports it is " +
            "a player watching the same die come up forever.");
    }

    /// <summary>How many of the attempts landed. The rest failed; nothing else can have happened.</summary>
    private static int Landed(IReadOnlyList<StoredSlice> rows) =>
        rows.Count(row => Item(row).EnhanceLevel == CoinFlipLevel + 1);

    /// <summary>Sends one <c>ENHANCE</c> per guid the generator was laid out with, each to its own host.</summary>
    /// <remarks>
    /// A fresh cache per command rather than one host sending several: after an attempt the item's
    /// level and mercy counter have moved, and the next attempt would then be drawn against a
    /// different rate — which is a second reason for two outcomes to differ besides the seed.
    /// </remarks>
    private static async Task<IReadOnlyList<StoredSlice>> Attempts(IIdGeneratorPort ids)
    {
        Worlds.Content.ReadDouble(AuthoredRateAtCoinFlipLevel).ShouldBeInRange(
            double.Epsilon,
            1.0 - double.Epsilon,
            "the attempt every case here reads the seed through is authored as certain or " +
            "impossible, so its outcome no longer turns on the draw and nothing below can tell two " +
            "seeds apart.");

        var rows = new List<StoredSlice>(DrawsInspected);

        for (var sent = 0; sent < DrawsInspected; sent++)
        {
            rows.Add(await Attempt(ids));
        }

        return rows;
    }

    /// <summary>One <c>ENHANCE</c>, sent to a host over a cache holding nothing but the coin-flip row.</summary>
    /// <remarks>
    /// The profile is written straight into the cache rather than opened through the host, so every
    /// guid the generator hands out goes to a command seed — minting a profile identity would consume
    /// the first one and leave the commands reading bytes this case never laid out.
    /// </remarks>
    private static async Task<StoredSlice> Attempt(IIdGeneratorPort ids)
    {
        var (cache, player) = AtTheCoinFlip();
        var host = Hosts.Over(cache, ids: ids);

        var outcome = await host.SubmitAsync(player, null, new EnhanceCommand(TheItem), Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue(
            "the enhancement attempt was refused " + outcome.Rejection + ", so this case is reading " +
            "a command that never drew.");

        var read = await host.ReadOwnStateAsync(player, null, Worlds.Cancel);

        read.Lookup.ShouldBe(OwnStateLookup.Found, "the row that just acted was not found.");

        // Acceptance and the stone charge are the symptom, and a host issuing one constant reaches
        // both unscathed. Only the item below can tell the draws apart.
        read.View!.Player.Wallet[CurrencyId.ENHANCE_STONES].ShouldBe(
            0L, "the attempt was accepted without charging for it, so it is not the attempt this case names.");

        return new StoredSlice(read.View.Player, read.View.Run);
    }

    /// <summary>The one item the coin-flip row holds, as it now stands.</summary>
    private static GearInstanceSnapshot Item(StoredSlice slice) => slice.Player.Inventory!.Stored[0];

    /// <summary>Draw ordinals counted from one, the way a counting generator hands them out.</summary>
    private static ulong[] Counted() =>
        Enumerable.Range(1, DrawsInspected).Select(ordinal => (ulong)ordinal).ToArray();

    /// <summary>
    /// A cache holding one player who owns one enhanceable item and exactly the stones one attempt
    /// on it costs. Deterministic, so two calls produce two rows that differ in nothing.
    /// </summary>
    private static (InMemoryLocalCache Cache, PlayerId Player) AtTheCoinFlip()
    {
        var id = new PlayerId("PLAYER_COINFLIP");
        var starting = Player.CreateStarting(id, id.Value, Worlds.Start, Worlds.Content, Stock());

        if (starting.IsFailure)
        {
            throw new InvalidOperationException(
                "The fixture's starting row does not rehydrate: " + starting.Error +
                " Fix the fixture; do not weaken the cases.");
        }

        var row = starting.Value.ToSnapshot();

        var wallet = new Dictionary<CurrencyId, long>(row.Wallet)
        {
            [CurrencyId.ENHANCE_STONES] = Worlds.Content.ReadInt64(AuthoredCostAtCoinFlipLevel),
        };

        var cache = new InMemoryLocalCache().Seed(
            SliceKeys.ForPlayer(id),
            SnapshotCodec.EncodeSlice(new StoredSlice(row with { Wallet = wallet }, null)));

        return (cache, id);
    }

    /// <summary>One item, its base row read off the catalogue so its slot and family agree with its id.</summary>
    private static InventorySnapshot Stock() =>
        new(
            ExpansionsPurchased: 0,
            [
                new GearInstanceSnapshot(
                    TheItem,
                    Worlds.Content.ReadText("content/gear/gear.json#/slots/0/families/0/id"),
                    Enum.Parse<GearSlot>(Worlds.Content.ReadText("content/gear/gear.json#/slots/0/slot")),
                    Enum.Parse<GearFamily>(
                        Worlds.Content.ReadText("content/gear/gear.json#/slots/0/families/0/family")),
                    Rarity.C,
                    ChapterOrigin: 1,
                    Quality: 0.5,
                    EnhanceLevel: CoinFlipLevel,
                    EnhanceFailures: 0,
                    Affixes: [],
                    Locked: false),
            ],
            []);

    /// <summary>The success rate an attempt off <see cref="CoinFlipLevel"/> is drawn against.</summary>
    /// <remarks>
    /// Both ladders are authored per level from one upwards, so the attempt that takes an item off
    /// <see cref="CoinFlipLevel"/> reads the entry at that ordinal.
    /// </remarks>
    private static readonly string AuthoredRateAtCoinFlipLevel =
        "tuning/forge.json#/enhance/perLevelSuccessRate/" +
        CoinFlipLevel.ToString(CultureInfo.InvariantCulture);

    /// <summary>What that attempt costs in Enhance Stones.</summary>
    private static readonly string AuthoredCostAtCoinFlipLevel =
        "tuning/forge.json#/enhance/stoneCostPerLevel/" +
        CoinFlipLevel.ToString(CultureInfo.InvariantCulture);
}
