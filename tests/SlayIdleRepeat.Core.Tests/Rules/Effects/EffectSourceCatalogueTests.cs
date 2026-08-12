using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// 🔒 The floor under `18` §8 step 1's source list — <b>stated against the document's own sentence,
/// not against a second transcription of it.</b>
/// </summary>
/// <remarks>
/// <para>
/// Steering S3: a rule whose subject set can silently empty passes forever. The collector walks
/// <see cref="EffectSourceCatalogue.Rows"/>, so if a row is dropped, renamed or reordered the
/// collector goes on working and simply stops collecting from that source — a build silently missing
/// every gear affix, with nothing red. These are the rules that make each of those a failure.
/// </para>
/// <para>
/// 🔒 <b>The comparison is against the literal quotation.</b> Asserting "there are ten rows" or
/// listing the ten expected names in the test would be a second copy of the same list, and the two
/// copies would agree with each other while both drifted from `18`. Rebuilding the document's
/// sentence out of the rows and comparing it with the sentence is what makes the spec the authority.
/// </para>
/// </remarks>
public sealed class EffectSourceCatalogueTests
{
    /// <summary>
    /// 🔒 `18` §8 step 1, verbatim — the sentence the catalogue is checked against. Duplicated from
    /// the production constant <b>on purpose</b>: if it were read from
    /// <see cref="EffectSourceCatalogue.Step1SourceList"/> the test would compare the catalogue with
    /// itself, and editing the constant would keep it green.
    /// </summary>
    private const string DocumentSentence =
        "gear → affixes → set bonuses → talents → pet auras → mount → run buffs → shrine buffs → " +
        "curses → perks (in draft order)";

    /// <summary>
    /// 🔒 The ten rows, joined in declaration order, ARE `18` §8 step 1's source list. Fails on a
    /// dropped row, an added one, a renamed one and a reordered one.
    /// </summary>
    [Fact]
    public void The_ten_sources_are_18_8_step_1_in_its_own_order()
    {
        var rebuilt = string.Join(
            EffectSourceCatalogue.PhraseSeparator,
            EffectSourceCatalogue.Rows.Select(r => r.Phrase));

        // 🔒 Shouldly's string ShouldBe is ordinal and case-sensitive by default — unlike
        //    ShouldContain/ShouldStartWith, which default to Case.Insensitive.
        rebuilt.ShouldBe(
            DocumentSentence,
            "18 §8 step 1 names ten sources in one order. The collector walks this catalogue, so a " +
            "row that drifts from the document is a source the resolver silently stops collecting.");

        // 🔒 And the production constant agrees with the document too — otherwise the constant could
        //    be edited to match a drifted catalogue and this rule would still pass.
        EffectSourceCatalogue.Step1SourceList.ShouldBe(DocumentSentence);
    }

    /// <summary>
    /// 🔒 The enum and the catalogue are one list. <see cref="EffectResolutionOrder"/>'s tiebreak
    /// compares <see cref="EffectSourceKind"/> <em>ordinals</em>, so a member missing from the
    /// catalogue would still sort — into a position no document states.
    /// </summary>
    [Fact]
    public void Every_EffectSourceKind_has_a_row_and_the_ordinals_are_18_8_step_1s_positions()
    {
        var kinds = Enum.GetValues<EffectSourceKind>();

        kinds.Length.ShouldBe(
            EffectSourceCatalogue.Rows.Count,
            "every EffectSourceKind is one of 18 §8 step 1's ten sources and vice versa");

        for (var i = 0; i < EffectSourceCatalogue.Rows.Count; i++)
        {
            var row = EffectSourceCatalogue.Rows[i];

            ((int)row.Kind).ShouldBe(
                i + 1,
                $"{row.Kind} is source {i + 1} of 18 §8 step 1 ('{row.Phrase}'), and " +
                "EffectResolutionOrder's same-id tiebreak compares these ordinals — a wrong one is a " +
                "documented rule resolving in an undocumented order");
        }

        // The enum's own declaration order matches, so `foreach (var k in Enum.GetValues<...>())`
        // anywhere else agrees with the catalogue.
        kinds.Select(k => (int)k).ShouldBe(EffectSourceCatalogue.Rows.Select(r => (int)r.Kind));
    }

    /// <summary>
    /// 🔒 Every source that cannot yield anything yet names the milestone that lands it AND the
    /// <c>SubjectSetFloorTests.Pending</c> subject whose arrival expires the deferral (steering S4).
    /// A pending source with no expiry is a hole nobody is pointed at.
    /// </summary>
    [Fact]
    public void Every_pending_source_names_its_milestone_and_its_expiry_subject()
    {
        var offenders = EffectSourceCatalogue.Rows
            .Where(r => r.IsPending)
            .Where(r => string.IsNullOrWhiteSpace(r.OwningMilestone) ||
                        string.IsNullOrWhiteSpace(r.PendingSubject))
            .Select(r => $"{r.Kind} is pending but names no milestone or no expiry subject")
            .ToArray();

        offenders.ShouldBeEmpty();

        // 🔒 Floored, or the assertion above passes over an empty set the day somebody marks every
        //    source available (steering S3). Nine of the ten are pending as M2-02 lands; the floor is
        //    below that so wiring one is not a test edit, and the day it reaches zero this fails and
        //    whoever wired the last source has to delete this rule deliberately.
        EffectSourceCatalogue.Rows.Count(r => r.IsPending).ShouldBeGreaterThanOrEqualTo(
            1,
            "if no source is pending any more, every one of 18 §8 step 1's ten has a data model and " +
            "this rule — and the Pending entries it guards — should be removed in that commit");
    }

    /// <summary>
    /// 🔒 The expiry subjects are distinct. Two sources keyed on one name would share one
    /// <c>Pending</c> entry, and deleting it when the first arrived would silently untrack the second.
    /// </summary>
    [Fact]
    public void The_pending_expiry_subjects_are_distinct()
    {
        var subjects = EffectSourceCatalogue.Rows
            .Where(r => r.IsPending)
            .Select(r => r.PendingSubject!)
            .ToArray();

        subjects.Distinct(StringComparer.Ordinal).Count().ShouldBe(subjects.Length);
    }

    /// <summary>A kind outside the ten has no row, and says so rather than answering.</summary>
    [Fact]
    public void A_kind_outside_the_ten_has_no_row()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => EffectSourceCatalogue.RowFor((EffectSourceKind)0));
        Should.Throw<ArgumentOutOfRangeException>(() => EffectSourceCatalogue.RowFor((EffectSourceKind)11));
    }
}
