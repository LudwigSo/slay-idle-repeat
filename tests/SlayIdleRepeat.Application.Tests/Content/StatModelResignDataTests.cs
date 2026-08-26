using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `16` D46's re-sign rule over the shipped content set: with <c>DR_PCT</c> a damage-taken
/// multiplier off base 1.0, every <c>STAT_ADD_PCT DR_PCT</c> clause authors its value negative, so
/// aggregation lands the multiplier below 1.0 — "+70% damage reduction" is a 0.30 multiplier, not
/// a 1.70 one.
/// </summary>
/// <remarks>
/// The id lists are the subject-set floor: a clause silently vanishing from the catalogue would
/// otherwise make the sign sweep pass over nothing.
/// </remarks>
public sealed class StatModelResignDataTests
{
    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void Every_shipped_perk_DR_PCT_percent_clause_authors_a_negative_value()
    {
        var clauses = StatAddPctClauses("content/perks/perks.json", "DR_PCT");

        clauses.Keys.ShouldBe(
            [
                "PK_IRONHIDE_T1", "PK_IRONHIDE_T2", "PK_IRONHIDE_T3",
                "PK_BULWARK_T1A", "PK_BULWARK_T2A", "PK_BULWARK_T3A",
            ],
            ignoreOrder: true);

        foreach (var (id, value) in clauses)
        {
            value.ShouldBeLessThan(0.0, $"{id} authors a damage-taken increase under D46's model");
        }
    }

    [Fact]
    public void Every_shipped_boss_DR_PCT_percent_clause_authors_a_negative_value()
    {
        var clauses = StatAddPctClauses("content/bosses/bosses.json", "DR_PCT");

        clauses.Keys.ShouldBe(
            ["BOSS_OSSUARY_KING_P2_OSSIFY_DR", "BOSS_CINDERMAW_P2_ERUPTION_DR"],
            ignoreOrder: true);

        foreach (var (id, value) in clauses)
        {
            value.ShouldBeLessThan(0.0, $"{id} authors a damage-taken increase under D46's model");
        }
    }

    /// <summary>
    /// The negative control for the re-sign rule: <c>DMG%</c> clauses need no edit — base 1.0
    /// through <c>(1 + Σpct)</c> consumed bare already means what "+X% damage" says.
    /// </summary>
    [Fact]
    public void DMG_PCT_percent_clauses_stay_positive()
    {
        var clauses = StatAddPctClauses("content/perks/perks.json", "DMG_PCT");

        clauses.Keys.ShouldBe(
            ["PK_THUNDERCLAP_T1", "PK_THUNDERCLAP_T2", "PK_THUNDERCLAP_T3"],
            ignoreOrder: true);

        foreach (var (id, value) in clauses)
        {
            value.ShouldBeGreaterThan(0.0, id);
        }
    }

    /// <summary>
    /// The damage-reduction affix is a flat add onto base 1.0 now, so its authored roll range
    /// re-signs too: −0.08 .. −0.02 is "2 to 8 percentage points less damage taken".
    /// </summary>
    [Fact]
    public void The_damage_reduction_affix_rolls_a_negative_range()
    {
        var data = Data();

        var affixes = data.Read("tuning/drops.json#/affixPool/affixes").Items;
        var row = affixes.Single(a => TextOf(a, "id") == "AFX_DAMAGE_REDUCTION");

        row.TryGetMember("min", out var min).ShouldBeTrue();
        row.TryGetMember("max", out var max).ShouldBeTrue();

        min!.AsDouble().ShouldBe(-0.08);
        max!.AsDouble().ShouldBe(-0.02);
    }

    /// <summary>
    /// Warding Light contributes a <c>STAT_ADD_PCT DR_PCT</c> run buff, so its magnitude re-signs
    /// with the rest: −6% damage taken, not +6%.
    /// </summary>
    [Fact]
    public void The_warding_light_shrine_magnitude_is_negative()
    {
        var pool = Data().Read("tuning/currencies.json#/inRunIncome/shrineBuffPool/buffs").Items;
        var row = pool.Single(b => TextOf(b, "id") == "SHR_DR");

        row.TryGetMember("magnitude", out var magnitude).ShouldBeTrue();
        magnitude!.AsDouble().ShouldBe(-0.06);
    }

    /// <summary>Every <c>STAT_ADD_PCT</c> clause on <paramref name="stat"/> in the document, by effect id.</summary>
    private static IReadOnlyDictionary<string, double> StatAddPctClauses(string document, string stat)
    {
        var found = new Dictionary<string, double>(StringComparer.Ordinal);

        Collect(Data().GetDocument(document).Root, stat, found);

        return found;
    }

    private static void Collect(ContentValue value, string stat, Dictionary<string, double> found)
    {
        switch (value.Kind)
        {
            case ContentValueKind.Object:
                if (value.TryGetMember("op", out var op) && op!.Kind == ContentValueKind.Text &&
                    op.AsText() == "STAT_ADD_PCT" &&
                    value.TryGetMember("stat", out var target) && target!.Kind == ContentValueKind.Text &&
                    target.AsText() == stat &&
                    value.TryGetMember("value", out var authored) &&
                    value.TryGetMember("id", out var id))
                {
                    found[id!.AsText()] = authored!.AsDouble();
                }

                foreach (var name in value.MemberNames)
                {
                    value.TryGetMember(name, out var member);
                    Collect(member!, stat, found);
                }

                break;

            case ContentValueKind.Array:
                foreach (var item in value.Items)
                {
                    Collect(item, stat, found);
                }

                break;
        }
    }

    private static string TextOf(ContentValue row, string member)
    {
        row.TryGetMember(member, out var value).ShouldBeTrue(member);

        return value!.AsText(member);
    }
}
