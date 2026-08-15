using Shouldly;
using SlayIdleRepeat.Core.Model;
using Xunit;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 M3-06 — <c>Run.UpsertPerkTier</c> and <c>Run.DraftedPerks</c>: `06` §1.1's tier-upgrade
/// bookkeeping on the aggregate itself.
/// </summary>
public sealed class RunDraftedPerksTests
{
    private static RunAggregate NewRun() => RunAggregate.Rehydrate(RunSnapshots.Valid).Value;

    [Fact]
    public void A_fresh_grant_is_owned_at_Tier_I()
    {
        var run = NewRun();

        run.UpsertPerkTier("PK_TEST", 1);

        run.DraftedPerks.TierOf("PK_TEST").ShouldBe(1);
    }

    [Fact]
    public void An_owned_perk_can_be_upgraded_one_tier_at_a_time()
    {
        var run = NewRun();
        run.UpsertPerkTier("PK_TEST", 1);

        run.UpsertPerkTier("PK_TEST", 2);
        run.UpsertPerkTier("PK_TEST", 3);

        run.DraftedPerks.TierOf("PK_TEST").ShouldBe(3);
    }

    [Fact]
    public void An_unowned_perk_queried_for_its_tier_answers_zero()
    {
        var run = NewRun();

        run.DraftedPerks.TierOf("PK_NEVER_DRAFTED").ShouldBe(0);
    }

    // ------------------------------------------------------------------ the guard, mutated on purpose (S1)

    [Fact]
    public void Skipping_a_tier_is_refused()
    {
        var run = NewRun();
        run.UpsertPerkTier("PK_TEST", 1);

        Should.Throw<ArgumentOutOfRangeException>(() => run.UpsertPerkTier("PK_TEST", 3));
    }

    [Fact]
    public void Granting_an_owned_perk_at_Tier_I_again_is_refused()
    {
        var run = NewRun();
        run.UpsertPerkTier("PK_TEST", 1);

        Should.Throw<ArgumentOutOfRangeException>(() => run.UpsertPerkTier("PK_TEST", 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void A_tier_outside_1_3_is_refused_even_for_a_fresh_grant(int tier)
    {
        var run = NewRun();

        Should.Throw<ArgumentOutOfRangeException>(() => run.UpsertPerkTier("PK_TEST", tier));
    }

    [Fact]
    public void A_blank_perk_id_is_refused()
    {
        var run = NewRun();

        Should.Throw<ArgumentException>(() => run.UpsertPerkTier("  ", 1));
    }

    // ------------------------------------------------------------------ MarkDraftPending's widened signature

    [Fact]
    public void DraftBattleKindValue_and_DraftBattleStage_are_readable_only_while_pending()
    {
        var run = NewRun();

        Should.Throw<InvalidOperationException>(() => run.DraftBattleKindValue);
        Should.Throw<InvalidOperationException>(() => run.DraftBattleStage);

        run.MarkDraftPending((int)SlayIdleRepeat.Core.Rules.Board.TileKind.Elite, 2);

        run.DraftBattleKindValue.ShouldBe((int)SlayIdleRepeat.Core.Rules.Board.TileKind.Elite);
        run.DraftBattleStage.ShouldBe(2);

        run.ClearDraftPending();

        Should.Throw<InvalidOperationException>(() => run.DraftBattleKindValue);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)]
    public void MarkDraftPending_refuses_a_stage_outside_1_2_3_or_BossStage(int badStage)
    {
        var run = NewRun();

        Should.Throw<ArgumentOutOfRangeException>(() => run.MarkDraftPending(1, badStage));
    }

    [Fact]
    public void MarkDraftPending_accepts_BossStage_zero()
    {
        var run = NewRun();

        Should.NotThrow(() => run.MarkDraftPending((int)SlayIdleRepeat.Core.Rules.Board.TileKind.Boss, 0));
        run.DraftBattleStage.ShouldBe(0);
    }

    [Fact]
    public void MarkDraftPending_refuses_a_negative_battle_kind()
    {
        var run = NewRun();

        Should.Throw<ArgumentOutOfRangeException>(() => run.MarkDraftPending(-1, 1));
    }
}
