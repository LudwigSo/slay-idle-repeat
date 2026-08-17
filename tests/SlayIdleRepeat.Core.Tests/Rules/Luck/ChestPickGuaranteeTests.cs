using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// <c>MG_CHEST_PICK</c>'s gold-tier guarantee: the 4th consecutive miss is forced, a natural gold
/// resets the counter, and the counter is addressed by a key the tuning reader forms.
/// </summary>
public sealed class ChestPickGuaranteeTests
{
    /// <summary>The three authored outcome tiers of the chest pick.</summary>
    private const int TierCount = 3;

    /// <summary>The top tier's index — BRONZE(0), SILVER(1), GOLD(2).</summary>
    private const int GoldTier = TierCount - 1;

    private static LuckTuning Tuning => LuckTuning.Read(LuckDocuments.LuckOnly());

    /// <summary>The counter id, formed the way production forms it — never spelled here.</summary>
    private static string CounterKey =>
        Tuning.CounterKey(SourceClass.MINIGAME, LuckDocuments.ShippedChestPickGuaranteeToken);

    private static DeterministicRng Draws(ulong seed, ulong position = 0) =>
        new(seed, RngStreams.Minigame(0), position);

    private static PityCounters At(int misses) =>
        PityCounters.Empty.With(CounterKey, misses);

    private static ChestPickResolution Resolve(PityCounters counters, ulong seed) =>
        LuckService.ResolveChestPick(
            Tuning,
            LuckDocuments.ShippedChestPickGuaranteeToken,
            counters,
            Draws(seed),
            TierCount);

    // ------------------------------------------------------------------ the exact N

    /// <summary>
    /// The guarantee fires on the <b>4th</b> consecutive miss — a counter of 3 — whatever the draw
    /// would otherwise have said.
    /// </summary>
    /// <remarks>
    /// Swept over many seeds rather than asserted on one: the forced path is "the draw's result is
    /// overridden", so a single seed that happened to roll gold would report success for a resolver
    /// that forces nothing at all.
    /// </remarks>
    [Fact]
    public void The_gold_tier_is_forced_on_the_fourth_consecutive_miss()
    {
        var misses = LuckDocuments.ShippedMinigameChestPickN - 1;

        foreach (var seed in Enumerable.Range(1, 200).Select(i => (ulong)i))
        {
            var resolution = Resolve(At(misses), seed);

            resolution.Tier.ShouldBe(GoldTier, $"seed {seed}");
            resolution.FromPity.ShouldBeTrue($"seed {seed}");
        }
    }

    /// <summary>
    /// It does <b>not</b> fire on the 3rd — the boundary from below, and the case that tells the
    /// guarantee apart from "always gold".
    /// </summary>
    /// <remarks>
    /// Asserted as "some seed is not gold" rather than "no seed is gold": a natural gold is a legal
    /// outcome of an unforced pick, so requiring every seed to miss would be requiring the draw to
    /// be rigged the other way.
    /// </remarks>
    [Fact]
    public void The_gold_tier_is_not_forced_on_the_third_consecutive_miss()
    {
        var misses = LuckDocuments.ShippedMinigameChestPickN - 2;

        var resolutions = Enumerable.Range(1, 200)
            .Select(i => Resolve(At(misses), (ulong)i))
            .ToArray();

        resolutions.ShouldContain(resolution => resolution.Tier != GoldTier);
        resolutions.ShouldAllBe(resolution => !resolution.FromPity);
    }

    /// <summary>The read-only question agrees with the resolution, at both sides of the boundary.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void The_guarantee_question_answers_the_same_boundary(int misses, bool expected)
    {
        ChestPickGuarantee.GuaranteeFires(Tuning.ChestPick, misses).ShouldBe(expected);
    }

    // ------------------------------------------------------------------ counter movement

    /// <summary>A miss advances the counter by one.</summary>
    [Fact]
    public void A_miss_advances_the_counter()
    {
        var missing = Enumerable.Range(1, 200)
            .Select(i => Resolve(At(1), (ulong)i))
            .First(resolution => resolution.Tier != GoldTier);

        missing.Counter.Key.ShouldBe(CounterKey);
        missing.Counter.Value.ShouldBe(2);
    }

    /// <summary>A <b>natural</b> gold resets the counter exactly as a forced one does.</summary>
    /// <remarks>
    /// The half a "reset when pity fires" implementation gets wrong: a player who rolls gold on the
    /// third pick has had their gold chest, and a counter that kept climbing would hand them a
    /// second one a pick later.
    /// </remarks>
    [Fact]
    public void A_natural_gold_resets_the_counter()
    {
        var natural = Enumerable.Range(1, 200)
            .Select(i => Resolve(At(1), (ulong)i))
            .First(resolution => resolution.Tier == GoldTier);

        natural.FromPity.ShouldBeFalse();
        natural.Counter.Value.ShouldBe(0);
    }

    /// <summary>The forced pick resets it too.</summary>
    [Fact]
    public void The_forced_gold_resets_the_counter()
    {
        Resolve(At(LuckDocuments.ShippedMinigameChestPickN - 1), seed: 7).Counter.Value.ShouldBe(0);
    }

    // ------------------------------------------------------------------ the draw budget

    /// <summary>
    /// Forced or not, a resolution consumes exactly one draw index — so a resumed stream lands in the
    /// same place either way.
    /// </summary>
    [Fact]
    public void A_forced_pick_and_a_natural_one_consume_the_same_single_draw()
    {
        var natural = Draws(seed: 99);
        var forced = Draws(seed: 99);

        LuckService.ResolveChestPick(
            Tuning, LuckDocuments.ShippedChestPickGuaranteeToken, PityCounters.Empty, natural, TierCount);
        LuckService.ResolveChestPick(
            Tuning,
            LuckDocuments.ShippedChestPickGuaranteeToken,
            At(LuckDocuments.ShippedMinigameChestPickN - 1),
            forced,
            TierCount);

        natural.Position.ShouldBe(1UL);
        forced.Position.ShouldBe(1UL);
    }

    // ------------------------------------------------------------------ the counter key

    /// <summary>
    /// The counter id is formed by the tuning reader from the authored key and the guarantee token.
    /// </summary>
    /// <remarks>
    /// The chest-pick guarantee is an outcome <em>tier</em>, not a band on the gear rarity ladder, so
    /// the rarity overload cannot express it. The formation point widened rather than the caller
    /// spelling its own key — which is what keeps a re-authored key from silently pointing the
    /// counter somewhere nobody writes.
    /// </remarks>
    [Fact]
    public void The_counter_key_is_the_authored_key_paired_with_the_guarantee_token()
    {
        CounterKey.ShouldBe(
            LuckDocuments.ShippedMinigameCounterKey +
            LuckTuning.CounterKeySeparator +
            LuckDocuments.ShippedChestPickGuaranteeToken);
    }

    /// <summary>A blank guarantee token is refused rather than forming a key ending in the separator.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_guarantee_token_is_refused(string guarantee)
    {
        Should.Throw<ArgumentException>(() => Tuning.CounterKey(SourceClass.MINIGAME, guarantee));
    }

    /// <summary>
    /// A guarantee token carrying the separator is refused rather than forming an ambiguous key.
    /// </summary>
    /// <remarks>
    /// 🔒 The invariant the rarity overload got for free and this one does not. A key is one authored
    /// key paired with one guarantee, and both halves used to be closed vocabularies the separator
    /// could not appear in. The right half is now an authored token, constrained by a schema in a
    /// different document — so the formation point checks it rather than inheriting it. Without this,
    /// <c>"GO:LD"</c> and an authored key of <c>minigame.chestpick.go</c> would form the same id, and
    /// two guarantees sharing one counter reads to the player as a counter that reset itself.
    /// </remarks>
    [Theory]
    [InlineData("GO:LD")]
    [InlineData(":GOLD")]
    [InlineData("GOLD:")]
    public void A_guarantee_token_carrying_the_separator_is_refused(string guarantee)
    {
        Should.Throw<ArgumentException>(() => Tuning.CounterKey(SourceClass.MINIGAME, guarantee))
            .Message.ShouldContain(
                guarantee,
                Case.Sensitive,
                "the refusal has to name the token it rejected — several argument checks guard this " +
                "overload, and a bare 'bad guarantee' leaves the caller unable to tell which.");
    }

    /// <summary>
    /// A class that authors no counter key still refuses to form one through the new overload.
    /// </summary>
    /// <remarks>
    /// The negative control on the widening: the overload exists so the chest pick can be keyed, not
    /// so any class can be. <c>DRAFT</c> authors an explicit null because its counters are run-scoped.
    /// </remarks>
    [Fact]
    public void A_class_with_no_authored_key_still_forms_none()
    {
        Should.Throw<InvalidTunableException>(() => Tuning.CounterKey(SourceClass.DRAFT, "GOLD"));
    }

    /// <summary>The authored top-tier outcome token is read out of the reward table, not transcribed.</summary>
    /// <remarks>
    /// 🔒 The identity under the key above. A test that formed the key from its own literal would
    /// agree with itself; the guarantee token is authored in <c>currencies.json</c> beside the reward
    /// columns, and this is where the two are pinned together.
    /// </remarks>
    [Fact]
    public void The_guarantee_token_is_the_authored_top_tier_outcome_name()
    {
        var rewards = MinigameRewardTuning.Read(TuningDocuments.Shipped);

        rewards.OutcomeName(MinigameCatalogue.ChestPick, ChestPickGuarantee.TopTier(TierCount))
            .ShouldBe(LuckDocuments.ShippedChestPickGuaranteeToken);
    }

    /// <summary>The top tier of a three-row table is the last one, and a table of none is a defect.</summary>
    [Fact]
    public void The_top_tier_is_the_last_authored_row()
    {
        ChestPickGuarantee.TopTier(TierCount).ShouldBe(GoldTier);
        Should.Throw<ArgumentOutOfRangeException>(() => ChestPickGuarantee.TopTier(0));
    }

    // ------------------------------------------------------------------ the two authored counts

    /// <summary>
    /// A reward table whose row count disagrees with the authored chest count is refused rather than
    /// drawn against.
    /// </summary>
    /// <remarks>
    /// The chest pick's tier space is authored twice — <c>currencies.json</c>'s reward rows and
    /// <c>luck.json</c>'s <c>chestCount</c> — and this resolver is the only place the two meet. Both
    /// directions are probed: a table that lost a row and one that gained one are different content
    /// mistakes, and a guard written as a one-sided comparison catches only one of them.
    /// </remarks>
    [Theory]
    [InlineData(TierCount - 1)]
    [InlineData(TierCount + 1)]
    public void A_reward_table_that_disagrees_with_the_authored_chest_count_is_refused(int tierCount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LuckService.ResolveChestPick(
            Tuning,
            LuckDocuments.ShippedChestPickGuaranteeToken,
            PityCounters.Empty,
            Draws(seed: 11),
            tierCount));
    }

    /// <summary>
    /// 🔒 The negative control: the shipped pair agrees, so the guard must let it through and leave
    /// the stream where a resolution leaves it.
    /// </summary>
    [Fact]
    public void The_shipped_reward_table_and_chest_count_agree()
    {
        Tuning.ChestPick.ChestCount.ShouldBe(TierCount);

        var draws = Draws(seed: 11);

        Should.NotThrow(() => LuckService.ResolveChestPick(
            Tuning, LuckDocuments.ShippedChestPickGuaranteeToken, PityCounters.Empty, draws, TierCount));

        draws.Position.ShouldBe(1UL);
    }
}
