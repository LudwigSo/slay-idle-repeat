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

    /// <summary>The quality floor's band is a <b>perk</b> band, not a gear rarity.</summary>
    /// <remarks>
    /// 🔴 Written first as <c>ShouldBeOfType&lt;PerkRarity&gt;()</c>, which the field's declared type
    /// makes true whatever the reader does — a test that could not fail. The claim that carries
    /// weight is about the two <em>vocabularies</em>: the authored token resolves in the perk ladder
    /// and in no other, so a reader that parsed it against the gear ladder would refuse the shipped
    /// document at load and take the whole game down rather than one draft.
    /// </remarks>
    [Fact]
    public void The_quality_floor_band_is_read_from_the_perk_ladder()
    {
        var authored = LuckDocuments.ShippedDraftQualityFloorRarity;

        Enum.GetNames<PerkRarity>().ShouldContain(
            name => name.Equals(authored, StringComparison.OrdinalIgnoreCase));
        Enum.GetNames<SlayIdleRepeat.Core.Primitives.Rarity>().ShouldNotContain(
            name => name.Equals(authored, StringComparison.OrdinalIgnoreCase),
            "the gear ladder is C/B/A/S/SS — a token that resolved in both vocabularies would let a " +
            "retune of one silently re-point the other.");

        Read().Draft.QualityFloorRarityAtLeast.ShouldBe(PerkRarity.Rare);
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
    /// 🔒 Recorded as a checked fact rather than left as an assumption, because
    /// <c>LuckSchemaTests</c> rejects the PascalCase spelling at build time and its stated reason
    /// depends on this: the reader converts the authored <c>SCREAMING_SNAKE</c> token to the enum's
    /// declared name before parsing, and that conversion lower-cases everything after a word
    /// boundary. So the schema's enum is the ONLY guard on the document's spelling — widen it and
    /// nothing in Core objects. Whoever tightens the reader should delete this case in the same
    /// commit rather than discover it here.
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
