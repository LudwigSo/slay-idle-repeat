using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// 🔒 `03` §7a — the four readers M3-03's tile resolvers depend on:
/// <see cref="ChapterScalarTuning"/>, <see cref="TreasureTuning"/>, <see cref="CacheTuning"/> and
/// <see cref="ShrineTuning"/>, over <c>tuning/currencies.json</c>.
/// </summary>
public sealed class InRunIncomeTuningTests
{
    private static readonly ContentSnapshot Shipped = InRunIncomeDocuments.Shipped;

    // ------------------------------------------------------------------ ChapterScalarTuning

    /// <summary>
    /// 🔒 `03` §7a — <c>M(c) = metaGrowth^(c-1)</c>, as the <b>unrounded real multiplier</b> it is.
    /// </summary>
    /// <remarks>
    /// The expectations are computed from 1.35 rather than restated as literals per row, because a
    /// literal table would keep passing after the growth base moved. Chapter 1 IS asserted as the
    /// literal 1, because <c>x^0 = 1</c> is the one row that does not depend on the base — it is the
    /// reason every in-run income table can be authored at its chapter-1 value.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void The_meta_scalar_is_metaGrowth_to_the_chapter_minus_one(int chapterId)
    {
        ChapterScalarTuning.Read(Shipped)
            .MetaScalar(chapterId)
            .ShouldBe(Math.Pow(1.35, chapterId - 1), 1e-12);
    }

    /// <summary>…and chapter 1 is exactly 1, the one row that does not depend on the base.</summary>
    [Fact]
    public void Chapter_one_scales_by_exactly_one()
    {
        var tuning = ChapterScalarTuning.Read(Shipped);

        tuning.MetaScalar(1).ShouldBe(1.0);
        tuning.GoldScalar(1).ShouldBe(1.0);
    }

    /// <summary>🔒 …and the gold curve is the same shape over its own, faster base.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_gold_scalar_is_goldGrowth_to_the_chapter_minus_one(int chapterId)
    {
        ChapterScalarTuning.Read(Shipped)
            .GoldScalar(chapterId)
            .ShouldBe(Math.Pow(1.55, chapterId - 1), 1e-12);
    }

    /// <summary>
    /// 🔒 `03` §7a — the rounding lands on the scaled <b>amount</b>, never on the scalar.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is the regression test for a real defect.</b> <c>Scalar</c> used to return a
    /// <c>long</c>, quantising <c>M(c)</c> itself: with the shipped 1.35 that made
    /// <c>round(1.35) = 1</c>, so chapter 2 paid <em>exactly</em> what chapter 1 paid and the growth
    /// curve only moved in steps. The assertions below are the shape that catches it — 100 at
    /// chapter 2 is 135, not 100, and Gold's 100 is 155, not 200.
    /// </remarks>
    [Theory]
    [InlineData(1, 100L, 100L)]
    [InlineData(2, 100L, 135L)]
    [InlineData(3, 100L, 182L)]
    [InlineData(2, 7L, 9L)]
    public void A_meta_amount_is_scaled_then_rounded(int chapterId, long amount, long expected)
    {
        ChapterScalarTuning.Read(Shipped).ScaleMeta(amount, chapterId).ShouldBe(expected);
    }

    /// <inheritdoc cref="A_meta_amount_is_scaled_then_rounded"/>
    [Theory]
    [InlineData(1, 100L, 100L)]
    [InlineData(2, 100L, 155L)]
    [InlineData(3, 100L, 240L)]
    public void A_gold_amount_is_scaled_then_rounded(int chapterId, long amount, long expected)
    {
        ChapterScalarTuning.Read(Shipped).ScaleGold(amount, chapterId).ShouldBe(expected);
    }

    /// <summary>A negative amount — an event card that charges — scales by the same curve.</summary>
    [Fact]
    public void A_negative_amount_scales_by_the_same_curve()
    {
        ChapterScalarTuning.Read(Shipped).ScaleMeta(-100L, 2).ShouldBe(-135L);
    }

    /// <summary>
    /// 🔒 An amount whose scaled value leaves 64-bit range is refused rather than cast — an
    /// out-of-range double cast is undefined and would turn an overflowing grant into a debt.
    /// </summary>
    [Fact]
    public void An_amount_that_overflows_the_scaled_range_is_refused()
    {
        var tuning = ChapterScalarTuning.Read(Shipped);

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.ScaleMeta(long.MaxValue, 8));
    }

    /// <summary>
    /// 🔒 The scalar is genuinely READ, not hardcoded — a different growth base gives a different
    /// answer at the same chapter.
    /// </summary>
    /// <remarks>
    /// ⚠️ This test exists because <c>M(1)</c> is 1 whatever the base is, so every chapter-1
    /// assertion above would pass just as well against <c>return 1;</c>.
    /// </remarks>
    [Fact]
    public void A_different_growth_base_moves_the_scalar()
    {
        var doubling = InRunIncomeDocuments.With(metaGrowth: ContentValue.Number(2m));

        ChapterScalarTuning.Read(doubling).MetaScalar(4).ShouldBe(8.0, 1e-12);
        ChapterScalarTuning.Read(Shipped).MetaScalar(4).ShouldBe(Math.Pow(1.35, 3), 1e-12);
    }

    // ⚠️ There is deliberately NO test for a non-finite growth base, and its absence is recorded
    // rather than an oversight: ContentValue backs every number with a decimal, so ReadDouble cannot
    // produce a NaN or an infinity and the reader's IsFinite arm is unreachable from content. It is
    // a consistency guard matching the three sibling readers — see ChapterScalarTuning.ReadGrowth —
    // and a test that could only reach it by bypassing the content seam would be asserting against a
    // state the game cannot be in.

    /// <summary>A chapter below `02` §1's floor of 1 has no exponent to raise the base to.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_chapter_below_one_is_refused(int chapterId)
    {
        var tuning = ChapterScalarTuning.Read(Shipped);

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.MetaScalar(chapterId));
        Should.Throw<ArgumentOutOfRangeException>(() => tuning.GoldScalar(chapterId));
    }

    /// <summary>…and the negative control: chapter 1 is accepted.</summary>
    [Fact]
    public void Chapter_one_is_accepted()
    {
        Should.NotThrow(() => ChapterScalarTuning.Read(Shipped).MetaScalar(1));
    }

    /// <summary>
    /// 🔒 A rounding mode this reader does not implement is refused rather than silently treated as
    /// the one it does.
    /// </summary>
    [Fact]
    public void An_unimplemented_rounding_mode_is_refused()
    {
        var floored = InRunIncomeDocuments.With(roundingMode: ContentValue.Text("FLOOR"));

        Should.Throw<InvalidTunableException>(() => ChapterScalarTuning.Read(floored))
            .Message.ShouldContain("FLOOR", Case.Sensitive);
    }

    /// <summary>A growth base of zero or below makes every chapter's scalar meaningless.</summary>
    [Fact]
    public void A_non_positive_growth_base_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            ChapterScalarTuning.Read(InRunIncomeDocuments.With(metaGrowth: ContentValue.Number(0m))));

        Should.Throw<InvalidTunableException>(() =>
            ChapterScalarTuning.Read(InRunIncomeDocuments.With(goldGrowth: ContentValue.Number(-1m))));
    }

    // ------------------------------------------------------------------ CampfireTuning

    /// <summary>🔒 `03` §2 — the campfire's rest heal comes from the document.</summary>
    [Fact]
    public void The_campfire_heal_comes_from_the_currencies_document()
    {
        CampfireTuning.Read(Shipped).HealPctMaxHp.ShouldBe(0.4);
    }

    /// <summary>
    /// 🔒 …and is genuinely READ rather than the constant it used to be.
    /// </summary>
    /// <remarks>
    /// ⚠️ This test is the point of the whole block: the rest heal shipped as a
    /// <c>const double HealPctMaxHp = 0.40</c> on <c>CampfireResolver</c> until M3-03's review moved
    /// it into <c>#/inRunIncome/campfire</c> under `21` §3.1. Asserting only the shipped 0.4 would
    /// pass just as well against the constant.
    /// </remarks>
    [Fact]
    public void A_different_authored_campfire_heal_is_honoured()
    {
        var half = InRunIncomeDocuments.With(campfireHeal: ContentValue.Number(0.5m));

        CampfireTuning.Read(half).HealPctMaxHp.ShouldBe(0.5);
    }

    /// <summary>
    /// 🔒 A rest that heals nothing, or more than a full bar, is refused. ⚠️ Zero is refused here
    /// where the cache's egg rate accepts it — see <see cref="CampfireTuning"/> for the asymmetry.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void A_campfire_heal_outside_the_unit_interval_is_refused(decimal heal)
    {
        Should.Throw<InvalidTunableException>(() =>
            CampfireTuning.Read(InRunIncomeDocuments.With(campfireHeal: ContentValue.Number(heal))));
    }

    /// <summary>…and a full-bar heal is accepted: the resolver clamps at Max HP anyway.</summary>
    [Fact]
    public void A_full_bar_campfire_heal_is_accepted()
    {
        var full = InRunIncomeDocuments.With(campfireHeal: ContentValue.Number(1m));

        CampfireTuning.Read(full).HealPctMaxHp.ShouldBe(1.0);
    }

    // ------------------------------------------------------------------ TreasureTuning

    /// <summary>🔒 `03` §7a.3's three profiles, in the document's own order and with its own numbers.</summary>
    [Fact]
    public void Every_treasure_profile_comes_from_the_currencies_document()
    {
        var profiles = TreasureTuning.Read(Shipped).Profiles;

        profiles.Count.ShouldBe(InRunIncomeDocuments.ShippedTreasureProfiles.Length);

        for (var i = 0; i < profiles.Count; i++)
        {
            var (id, weight, crowns, stones, dust) = InRunIncomeDocuments.ShippedTreasureProfiles[i];

            profiles[i].Id.ShouldBe(id);
            profiles[i].Weight.ShouldBe((double)weight);
            profiles[i].Crowns.ShouldBe(crowns);
            profiles[i].EnhanceStones.ShouldBe(stones);
            profiles[i].MergeDust.ShouldBe(dust);
        }
    }

    /// <summary>
    /// 🔒 The order is preserved, not sorted — <c>WeightedPick</c> walks a table in order, so a
    /// reader that re-ordered these would change what every existing run seed pays.
    /// </summary>
    [Fact]
    public void The_treasure_profiles_keep_the_documents_order()
    {
        TreasureTuning.Read(Shipped).Profiles.Select(p => p.Id)
            .ShouldBe(["COIN_HOARD", "STONE_CACHE", "DUST_TROVE"]);
    }

    /// <summary>A zero column is authored and legal — <c>DUST_TROVE</c> pays no Enhance Stones.</summary>
    [Fact]
    public void A_zero_payout_column_is_accepted()
    {
        var dustTrove = TreasureTuning.Read(Shipped).Profiles.Single(p => p.Id == "DUST_TROVE");

        dustTrove.EnhanceStones.ShouldBe(0);
        dustTrove.MergeDust.ShouldBe(10);
    }

    /// <summary>A negative payout would make a treasure tile charge the player.</summary>
    [Fact]
    public void A_negative_treasure_payout_is_refused()
    {
        var charging = InRunIncomeDocuments.With(treasureProfiles: ContentValue.Array([
            InRunIncomeDocuments.Obj(
                ("id", ContentValue.Text("COIN_HOARD")),
                ("weight", ContentValue.Number(55m)),
                ("crowns", ContentValue.Number(-40m)),
                ("enhanceStones", ContentValue.Number(0m)),
                ("mergeDust", ContentValue.Number(0m))),
        ]));

        Should.Throw<InvalidTunableException>(() => TreasureTuning.Read(charging));
    }

    /// <summary>A table where every weight is zero can never be drawn from.</summary>
    [Fact]
    public void An_all_zero_weight_treasure_table_is_refused()
    {
        var unreachable = InRunIncomeDocuments.With(treasureProfiles: ContentValue.Array([
            InRunIncomeDocuments.Obj(
                ("id", ContentValue.Text("COIN_HOARD")),
                ("weight", ContentValue.Number(0m)),
                ("crowns", ContentValue.Number(40m)),
                ("enhanceStones", ContentValue.Number(0m)),
                ("mergeDust", ContentValue.Number(0m))),
        ]));

        Should.Throw<InvalidTunableException>(() => TreasureTuning.Read(unreachable));
    }

    /// <summary>An empty profile array leaves the tile with nothing to draw.</summary>
    [Fact]
    public void An_empty_treasure_profile_array_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            TreasureTuning.Read(InRunIncomeDocuments.With(treasureProfiles: ContentValue.EmptyArray)));
    }

    // ------------------------------------------------------------------ CacheTuning

    /// <summary>🔒 `03` §7a.4's two numbers come from the document.</summary>
    [Fact]
    public void Every_cache_number_comes_from_the_currencies_document()
    {
        var tuning = CacheTuning.Read(Shipped);

        tuning.BeastFeedBase.ShouldBe(InRunIncomeDocuments.ShippedBeastFeedBase);
        tuning.EggChance.ShouldBe((double)InRunIncomeDocuments.ShippedEggChance);
    }

    /// <summary>An egg rate outside <c>[0,1]</c> is not a probability.</summary>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void An_egg_rate_outside_the_unit_interval_is_refused(double rate)
    {
        Should.Throw<InvalidTunableException>(() =>
            CacheTuning.Read(InRunIncomeDocuments.With(eggChance: ContentValue.Number((decimal)rate))));
    }

    /// <summary>
    /// …and the negative control, which is not a formality: 0 and 1 are how content switches the egg
    /// off and how a test pins the egg branch, so refusing them would break both.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void The_ends_of_the_unit_interval_are_accepted(double rate)
    {
        CacheTuning.Read(InRunIncomeDocuments.With(eggChance: ContentValue.Number((decimal)rate)))
            .EggChance.ShouldBe(rate);
    }

    /// <summary>A negative base would make a cache charge the player Beast Feed.</summary>
    [Fact]
    public void A_negative_beast_feed_base_is_refused()
    {
        var charging = InRunIncomeDocuments.CurrenciesRoot(InRunIncomeDocuments.Obj(
            ("inRunIncome", InRunIncomeDocuments.Obj(
                ("cache", InRunIncomeDocuments.Obj(
                    ("beastFeedBase", ContentValue.Number(-25m)),
                    ("eggChance", ContentValue.Number(0.06m))))))));

        Should.Throw<InvalidTunableException>(() => CacheTuning.Read(charging));
    }

    // ------------------------------------------------------------------ ShrineTuning

    /// <summary>🔒 `03` §7a.5's pool of ten, in the document's own order and with its own numbers.</summary>
    [Fact]
    public void Every_shrine_buff_comes_from_the_currencies_document()
    {
        var tuning = ShrineTuning.Read(Shipped);

        tuning.OptionsOffered.ShouldBe(InRunIncomeDocuments.ShippedOptionsOffered);
        tuning.Buffs.Count.ShouldBe(InRunIncomeDocuments.ShippedShrineBuffs.Length);

        for (var i = 0; i < tuning.Buffs.Count; i++)
        {
            var (id, stat, magnitude, heal) = InRunIncomeDocuments.ShippedShrineBuffs[i];

            tuning.Buffs[i].Id.ShouldBe(id);
            tuning.Buffs[i].Stat.ShouldBe(stat);
            tuning.Buffs[i].Magnitude.ShouldBe(magnitude is null ? null : (double)magnitude.Value);
            tuning.Buffs[i].ImmediateHealPctMaxHp.ShouldBe(heal is null ? null : (double)heal.Value);
        }
    }

    /// <summary>
    /// 🔒 <c>SHR_HEAL</c>'s authored <c>null</c> stat is carried through as <c>null</c>, never
    /// defaulted to a zero magnitude that would read as "a buff of nothing".
    /// </summary>
    [Fact]
    public void The_heal_only_row_carries_its_authored_nulls()
    {
        var heal = ShrineTuning.Read(Shipped).Buffs.Single(b => b.Id == "SHR_HEAL");

        heal.Stat.ShouldBeNull();
        heal.Magnitude.ShouldBeNull();
        heal.ImmediateHealPctMaxHp.ShouldBe(0.4);
    }

    /// <summary>🔒 …and <c>SHR_HP</c> carries BOTH halves, which is why the heal is not a row flag.</summary>
    [Fact]
    public void The_max_hp_row_carries_a_stat_and_a_heal()
    {
        var hp = ShrineTuning.Read(Shipped).Buffs.Single(b => b.Id == "SHR_HP");

        hp.Stat.ShouldBe("MAX_HP");
        hp.Magnitude.ShouldBe(0.18);
        hp.ImmediateHealPctMaxHp.ShouldBe(0.18);
    }

    /// <summary>The eight stat-only rows carry no heal at all, rather than a zero one.</summary>
    [Fact]
    public void A_stat_only_row_carries_no_heal()
    {
        ShrineTuning.Read(Shipped).Buffs.Single(b => b.Id == "SHR_ATK")
            .ImmediateHealPctMaxHp.ShouldBeNull();
    }

    /// <summary>A row that authors neither a stat buff nor a heal does nothing at all.</summary>
    [Fact]
    public void A_buff_that_does_nothing_is_refused()
    {
        // Two rows, not one: a single-row pool would trip the "smaller than the options offered"
        // refusal first and this test would pass on the wrong failure.
        var empty = InRunIncomeDocuments.With(shrineBuffs: ContentValue.Array([
            InRunIncomeDocuments.Obj(
                ("id", ContentValue.Text("SHR_ATK")),
                ("displayName", ContentValue.Text("loc.shrine.atk.name")),
                ("stat", ContentValue.Text("ATK")),
                ("magnitude", ContentValue.Number(0.12m))),
            InRunIncomeDocuments.Obj(
                ("id", ContentValue.Text("SHR_NOTHING")),
                ("displayName", ContentValue.Text("loc.shrine.nothing.name")),
                ("stat", ContentValue.Unauthorised),
                ("magnitude", ContentValue.Unauthorised)),
        ]));

        Should.Throw<InvalidTunableException>(() => ShrineTuning.Read(empty))
            .Message.ShouldContain("SHR_NOTHING", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A pool smaller than the number of DISTINCT options offered cannot supply them without
    /// repeating one — the failure the sampling-without-replacement draw exists to prevent.
    /// </summary>
    [Fact]
    public void A_pool_too_small_for_the_distinct_options_is_refused()
    {
        var thin = InRunIncomeDocuments.With(shrineBuffs: ContentValue.Array([
            InRunIncomeDocuments.Obj(
                ("id", ContentValue.Text("SHR_ATK")),
                ("displayName", ContentValue.Text("loc.shrine.atk.name")),
                ("stat", ContentValue.Text("ATK")),
                ("magnitude", ContentValue.Number(0.12m))),
        ]));

        Should.Throw<InvalidTunableException>(() => ShrineTuning.Read(thin));
    }

    /// <summary>An immediate heal outside <c>[0,1]</c> is not a share of Max HP.</summary>
    [Fact]
    public void An_immediate_heal_above_a_full_bar_is_refused()
    {
        var overheal = InRunIncomeDocuments.With(shrineBuffs: ContentValue.Array(
            InRunIncomeDocuments.ShippedShrineBuffs.Select((b, i) => InRunIncomeDocuments.Obj(
                ("id", ContentValue.Text(b.Id)),
                ("displayName", ContentValue.Text("loc.x.name")),
                ("stat", ContentValue.Text("ATK")),
                ("magnitude", ContentValue.Number(0.1m)),
                ("immediateHealPctMaxHp", ContentValue.Number(i == 0 ? 1.5m : 0.1m))))));

        Should.Throw<InvalidTunableException>(() => ShrineTuning.Read(overheal));
    }

    /// <summary>A shrine that offers no options is not a shrine.</summary>
    [Fact]
    public void A_zero_option_shrine_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            ShrineTuning.Read(InRunIncomeDocuments.With(optionsOffered: ContentValue.Number(0m))));
    }
}
