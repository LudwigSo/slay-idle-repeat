using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C12 — `15` Part F holds eleven items, in Part F's order, split 2 mechanical / 4
/// mechanical-with-an-uncalibrated-threshold / 5 human.
/// </summary>
/// <remarks>
/// 🔒 Every string comparison here is case-sensitive, and the ones that could be weakened say so
/// explicitly. Shouldly's string <c>ShouldContain</c>/<c>ShouldStartWith</c> default to
/// <c>Case.Insensitive</c> — on a case whose entire purpose is proving text is verbatim, taking the
/// default would be a silent weakening of the only thing the case is for.
/// </remarks>
public sealed class QaChecklistTests
{
    /// <summary>`15` Part F item 1's mechanical measurements plus five human items.</summary>
    private const int ItemsCarryingAHumanGap = 6;

    private const int MechanicalItems = 2;
    private const int UncalibratedThresholdItems = 4;
    private const int HumanItems = 5;

    /// <summary>
    /// 🔒 Steering rule S3. A checklist that can silently shrink passes forever, and this is `15`
    /// Part F's own count — eleven boxes to tick before a batch is accepted.
    /// </summary>
    [Fact]
    public void The_checklist_holds_exactly_the_eleven_items_15_Part_F_lists()
    {
        var checklist = new QaChecklist();

        checklist.Items.Count.ShouldBe(Doc15PartF.ItemCount);
        Doc15PartF.ItemCount.ShouldBe(11);
    }

    [Fact]
    public void The_item_numbers_are_exactly_one_to_eleven_with_no_gaps()
    {
        var checklist = new QaChecklist();

        var numbers = checklist.Items.Select(item => item.ItemNumber).ToArray();

        numbers.ShouldBe(Enumerable.Range(1, Doc15PartF.ItemCount).ToArray());
    }

    /// <summary>
    /// 🔒 Ordinal, case-sensitive, one case per item. Part F's wording is the specification these
    /// checks implement; a paraphrase in <see cref="IQaCheck.ChecklistText"/> would leave a report
    /// claiming to have checked something the doc does not say.
    /// </summary>
    [Theory]
    [InlineData(1, Doc15PartF.Item1)]
    [InlineData(2, Doc15PartF.Item2)]
    [InlineData(3, Doc15PartF.Item3)]
    [InlineData(4, Doc15PartF.Item4)]
    [InlineData(5, Doc15PartF.Item5)]
    [InlineData(6, Doc15PartF.Item6)]
    [InlineData(7, Doc15PartF.Item7)]
    [InlineData(8, Doc15PartF.Item8)]
    [InlineData(9, Doc15PartF.Item9)]
    [InlineData(10, Doc15PartF.Item10)]
    [InlineData(11, Doc15PartF.Item11)]
    public void Each_item_carries_15_Part_Fs_own_line(int itemNumber, string expected)
    {
        var checklist = new QaChecklist();

        var check = checklist.Items[itemNumber - 1];

        check.ItemNumber.ShouldBe(itemNumber);
        check.ChecklistText.ShouldBe(expected);
        string.Equals(check.ChecklistText, expected, StringComparison.Ordinal).ShouldBeTrue(
            "Part F's wording is the specification, so this comparison is ordinal on purpose");
    }

    /// <summary>
    /// 🔒 The constants above are only as good as their agreement with the doc. This reads
    /// <c>game-design/15_ART_DIRECTION_AND_ASSET_MANIFEST.md</c> itself, so an edit to Part F turns
    /// the suite red instead of leaving eleven constants quietly describing an older checklist.
    /// </summary>
    [Fact]
    public void The_verbatim_constants_still_match_the_committed_design_doc()
    {
        var fromDoc = PipelineFiles.Doc15PartFLines();

        fromDoc.Count.ShouldBe(Doc15PartF.ItemCount);
        fromDoc.ToArray().ShouldBe(Doc15PartF.Items.ToArray());
    }

    /// <summary>
    /// 🔒 The split is a deliverable of M8-06, not an implementation detail. Reclassifying a human
    /// item as mechanical is exactly how a batch comes to be accepted by machinery alone, so it
    /// costs a red test and a human's argument.
    /// </summary>
    [Fact]
    public void The_classification_split_is_two_mechanical_four_threshold_bearing_and_five_human()
    {
        var checklist = new QaChecklist();

        checklist.MechanicalCount.ShouldBe(MechanicalItems);
        checklist.UncalibratedThresholdCount.ShouldBe(UncalibratedThresholdItems);
        checklist.HumanCount.ShouldBe(HumanItems);
        (MechanicalItems + UncalibratedThresholdItems + HumanItems).ShouldBe(Doc15PartF.ItemCount);
    }

    /// <summary>
    /// 🔒 The counts above could be satisfied by any assignment of the eleven. This pins which item
    /// is which — including item 8, which is <see cref="QaClassification.Human"/> deliberately and
    /// against the rough expectation this task was dispatched with, because detecting rendered text
    /// needs OCR and OCR needs a model or a native binary.
    /// </summary>
    [Theory]
    [InlineData(1, QaClassification.MechanicalUncalibratedThreshold)]
    [InlineData(2, QaClassification.Human)]
    [InlineData(3, QaClassification.MechanicalUncalibratedThreshold)]
    [InlineData(4, QaClassification.Human)]
    [InlineData(5, QaClassification.MechanicalUncalibratedThreshold)]
    [InlineData(6, QaClassification.MechanicalUncalibratedThreshold)]
    [InlineData(7, QaClassification.Mechanical)]
    [InlineData(8, QaClassification.Human)]
    [InlineData(9, QaClassification.Human)]
    [InlineData(10, QaClassification.Mechanical)]
    [InlineData(11, QaClassification.Human)]
    public void Each_item_is_classified_as_M8_06_decided(
        int itemNumber, QaClassification expected)
    {
        var checklist = new QaChecklist();

        checklist.Items[itemNumber - 1].Classification.ShouldBe(expected);
    }

    /// <summary>
    /// 🔒 A human item with no stated gap is a hole in the report rather than a declared one: the
    /// batch would show ten items concluded and one blank, and nobody would know what was owed.
    /// </summary>
    [Fact]
    public void Every_human_item_names_the_judgement_this_project_does_not_make()
    {
        var checklist = new QaChecklist();

        var human = checklist.OfClassification(QaClassification.Human);

        human.Count.ShouldBe(HumanItems);
        human.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.HumanGap));
    }

    /// <summary>
    /// 🔒 Item 1 is mechanised and still owes a human gap. `15` §A4's acceptance test is a sentence
    /// about a person's ability to recognise a character, and four pixel measurements do not perform
    /// it — so a mechanical pass on item 1 must never be reportable as "§A4 passed".
    /// </summary>
    [Fact]
    public void Item_1_names_a_human_gap_quoting_15_A4_even_though_it_is_mechanised()
    {
        var checklist = new QaChecklist();

        var item1 = checklist.Items[0];

        item1.Classification.ShouldBe(QaClassification.MechanicalUncalibratedThreshold);
        item1.HumanGap.ShouldNotBeNull();
        item1.HumanGap.ShouldContain(Doc15PartF.SilhouetteAcceptanceSentence, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The other side of the same coin: the five fully mechanical-or-threshold items that are NOT
    /// item 1 declare no gap, so "six gaps" stays a statement about six specific items rather than
    /// a number that drifts as somebody adds caveats.
    /// </summary>
    [Fact]
    public void Exactly_six_items_carry_a_human_gap_and_the_checklist_surfaces_all_six()
    {
        var checklist = new QaChecklist();

        var carrying = checklist.Items.Where(item => item.HumanGap is not null).ToArray();

        carrying.Length.ShouldBe(ItemsCarryingAHumanGap);
        carrying.Select(item => item.ItemNumber).ToArray().ShouldBe([1, 2, 4, 8, 9, 11]);
        checklist.HumanGaps.Count.ShouldBe(ItemsCarryingAHumanGap);
        checklist.HumanGaps.ShouldAllBe(gap => !string.IsNullOrWhiteSpace(gap));
    }

    [Fact]
    public void Every_item_cites_the_section_of_15_its_substance_comes_from()
    {
        var checklist = new QaChecklist();

        var references = checklist.Items.Select(item => item.DocReference).ToArray();

        references.Length.ShouldBe(Doc15PartF.ItemCount);
        references.ShouldAllBe(reference => reference.StartsWith("15 ", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 <b>The A5 assertion.</b> A human item that could return <see cref="QaVerdict.Pass"/> is a
    /// heuristic wearing the checklist's clothes. Item 8 is the one that matters most: it ships a
    /// corner-opacity proxy, and the proxy must not be able to conclude the item.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void A_human_item_returns_HumanGapOnly_and_never_Pass(int itemNumber)
    {
        var checklist = new QaChecklist();
        var subject = QaSubjects.For(
            SyntheticAsset.ChibiCutOut().Image,
            ManifestRows.NonBiomeUiIcon,
            TestSpecs.WithTargetSize(ManifestRows.NonBiomeUiIcon, SyntheticAsset.Canvas, SyntheticAsset.Canvas));

        var outcome = checklist.Items[itemNumber - 1].Evaluate(subject);

        outcome.ItemNumber.ShouldBe(itemNumber);
        outcome.Verdict.ShouldBe(QaVerdict.HumanGapOnly);
        outcome.HumanGap.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 🔒 Assumption A5 as a return value: a full eleven-item run always includes five human items,
    /// so the machinery cannot reach <see cref="QaDecision.Accepted"/> no matter how clean the
    /// asset is. This is the property that stops "the pipeline accepted 942 assets" being read as
    /// "942 assets passed Part F".
    /// </summary>
    [Fact]
    public void A_full_run_is_never_accepted_by_machinery_alone_and_surfaces_every_gap()
    {
        var checklist = new QaChecklist();
        var subject = QaSubjects.For(
            SyntheticAsset.ChibiCutOut().Image,
            ManifestRows.NonBiomeUiIcon,
            TestSpecs.WithTargetSize(ManifestRows.NonBiomeUiIcon, SyntheticAsset.Canvas, SyntheticAsset.Canvas));

        var result = checklist.Evaluate(subject);

        result.Outcomes.Count.ShouldBe(Doc15PartF.ItemCount);
        result.Accepted.ShouldBeFalse();
        result.Decision.ShouldNotBe(QaDecision.Accepted);
        result.HumanGaps.Count.ShouldBe(ItemsCarryingAHumanGap);

        // 🔒 Why it is not accepted, and not merely that it is not. Whatever this particular
        // subject does to the six mechanical items, the five human ones must come back
        // HumanGapOnly — that is what makes acceptance unreachable for EVERY asset rather than
        // for this one. Without naming them, a run in which item 10 happened to fail would satisfy
        // the three assertions above even if all five human items had returned Pass.
        result.Outcomes
            .Where(outcome => outcome.Verdict == QaVerdict.HumanGapOnly)
            .Select(outcome => outcome.ItemNumber)
            .ToArray()
            .ShouldBe([2, 4, 8, 9, 11]);
    }
}
