using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>The catalogue behind 18 §8 step 1's source list — the collector walks it, so a drifted row is a source the resolver silently stops collecting from.</summary>
public sealed class EffectSourceCatalogueTests
{
    /// <summary>
    /// Duplicated from the production constant on purpose: read from
    /// <see cref="EffectSourceCatalogue.Step1SourceList"/> the test would compare the catalogue with
    /// itself.
    /// </summary>
    private const string DocumentSentence =
        "gear → affixes → set bonuses → talents → pet auras → mount → run buffs → shrine buffs → " +
        "curses → perks (in draft order)";

    [Fact]
    public void The_ten_sources_are_18_8_step_1_in_its_own_order()
    {
        var rebuilt = string.Join(
            EffectSourceCatalogue.PhraseSeparator,
            EffectSourceCatalogue.Rows.Select(r => r.Phrase));

        rebuilt.ShouldBe(
            DocumentSentence,
            "18 §8 step 1 names ten sources in one order. The collector walks this catalogue, so a " +
            "row that drifts from the document is a source the resolver silently stops collecting.");

        EffectSourceCatalogue.Step1SourceList.ShouldBe(
            DocumentSentence, "the production constant must agree with the document too, or it could " +
                              "be edited to match a drifted catalogue");
    }

    /// <summary>
    /// <see cref="EffectResolutionOrder"/>'s same-id tiebreak compares <see cref="EffectSourceKind"/>
    /// ordinals, so a member missing from the catalogue would still sort — into an undefined position.
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

        kinds.Select(k => (int)k).ShouldBe(
            EffectSourceCatalogue.Rows.Select(r => (int)r.Kind),
            "the enum's declaration order matches, so Enum.GetValues agrees with the catalogue");
    }

    /// <summary>A kind outside the ten has no row, and says so rather than answering.</summary>
    [Fact]
    public void A_kind_outside_the_ten_has_no_row()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => EffectSourceCatalogue.RowFor((EffectSourceKind)0));
        Should.Throw<ArgumentOutOfRangeException>(() => EffectSourceCatalogue.RowFor((EffectSourceKind)11));
    }
}
