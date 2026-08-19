using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <c>LuckTuning</c>'s view of the two blocks M4-01b wires: <c>#/draft</c> and
/// <c>#/minigame/chestPick</c>.
/// </summary>
/// <remarks>
/// The reader is what the rules see, and it is a different claim from "the document authors these
/// numbers" — <c>Application.Tests</c> makes that one against the shipped file. A reader that
/// resolved the wrong pointer would satisfy the document pin and hand the rules a default.
/// </remarks>
public sealed class LuckTuningDraftTests
{
    private static LuckTuning Read() => LuckTuning.Read(LuckDocuments.LuckOnly());

    // ------------------------------------------------------------------ #/draft

    /// <summary>Every authored dial of the five <c>DRAFT</c> rules reaches the reader.</summary>
    [Fact]
    public void The_draft_block_is_read_whole()
    {
        var draft = Read().Draft;

        draft.LegendaryPityDraftNumber.ShouldBe(LuckDocuments.ShippedDraftLegendaryPityN);
        draft.ConsecutiveDraftsWithoutAboveCommon.ShouldBe(LuckDocuments.ShippedDraftQualityFloorN);
        draft.QualityFloorRarityAtLeast.ShouldBe(PerkRarity.Rare);
        draft.NeverDraftedWeightMultiplier.ShouldBe(LuckDocuments.ShippedDraftCodexBiasMultiplier);
        draft.MaxBiasSelectedOptions.ShouldBe(LuckDocuments.ShippedDraftMaxBiasSelectedOptions);
        draft.OwnedUpgradeBias.ShouldBe(LuckDocuments.ShippedDraftOwnedUpgradeBias);
        draft.ConsecutiveDraftsWithoutOwnedUpgrade.ShouldBe(LuckDocuments.ShippedDraftUpgradeFamineN);
    }

    /// <summary>The anti-brick block is read, enabled, and forces the Sustain category.</summary>
    /// <remarks>
    /// Keyless by design: neither design document authors a number for it, so the block carries only
    /// whether the rule stands and which category it forces. Authoring a stage index here would be a
    /// tunable nobody wrote.
    /// </remarks>
    [Fact]
    public void The_sustain_anti_brick_block_is_read()
    {
        var antiBrick = Read().Draft.SustainAntiBrick;

        antiBrick.Enabled.ShouldBeTrue();
        antiBrick.ForceCategory.ShouldBe(PerkCategory.Sustain);
    }

    /// <summary>A draft number below 1 is refused rather than defaulted.</summary>
    /// <remarks>
    /// There is no zeroth draft to force, and a rung of zero would force every draft of the run.
    /// </remarks>
    [Fact]
    public void A_draft_guarantee_below_one_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            LuckTuning.Read(LuckDocuments.LuckOnly(draftLegendaryPityNumber: ContentValue.Number(0))));
    }

    /// <summary>The reader resolves the category token whatever case it is authored in.</summary>
    /// <remarks>
    /// 🔒 The schema's enum is the ONLY guard on the document's spelling — the reader itself accepts
    /// any casing. Whoever tightens the reader should delete this case in the same commit.
    /// </remarks>
    [Theory]
    [InlineData("SUSTAIN")]
    [InlineData("Sustain")]
    [InlineData("sustain")]
    public void The_reader_resolves_a_category_token_whatever_its_case(string authored)
    {
        LuckTuning.Read(LuckDocuments.LuckOnly(draftSustainForceCategory: ContentValue.Text(authored)))
            .Draft.SustainAntiBrick.ForceCategory.ShouldBe(PerkCategory.Sustain);
    }

    /// <summary>A force category the perk vocabulary does not declare is refused.</summary>
    [Fact]
    public void An_unknown_force_category_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            LuckTuning.Read(LuckDocuments.LuckOnly(
                draftSustainForceCategory: ContentValue.Text("NOT_A_CATEGORY"))));
    }

    // ------------------------------------------------------------------ #/minigame/chestPick

    /// <summary>The chest-pick block is read whole.</summary>
    [Fact]
    public void The_chest_pick_block_is_read_whole()
    {
        var chestPick = Read().ChestPick;

        chestPick.ChestCount.ShouldBe(LuckDocuments.ShippedMinigameChestCount);
        chestPick.GoldTierChests.ShouldBe(LuckDocuments.ShippedMinigameGoldTierChests);
        chestPick.GuaranteeOnNthPick.ShouldBe(LuckDocuments.ShippedMinigameChestPickN);
    }

    /// <summary>A guarantee of zero picks is refused rather than defaulted.</summary>
    [Fact]
    public void A_chest_pick_guarantee_below_one_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            LuckTuning.Read(LuckDocuments.LuckOnly(
                minigameGuaranteeOnNthPick: ContentValue.Number(0))));
    }
}
