using FluentAssertions;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// `14` §6 / `SlayIdleRepeat.Data/README.md` — the value tree `Core` reads content out of.
/// The load-bearing property here is that a JSON <c>null</c> is a <em>kind</em>, not a value:
/// 98 leaves in the shipped tuning files are unauthorised holes, and a silent zero in any of them
/// would produce a plausible, wrong economy.
/// </summary>
public sealed class ContentValueTests
{
    [Fact]
    public void Unauthorised_reports_its_own_kind_rather_than_a_number()
    {
        var value = ContentValue.Unauthorised;

        value.Kind.Should().Be(ContentValueKind.Unauthorised);
        value.IsUnauthorised.Should().BeTrue();
    }

    [Fact]
    public void AsNumber_throws_UnauthorisedTunableException_for_an_unauthorised_value()
    {
        var value = ContentValue.Unauthorised;

        var act = () => value.AsNumber();

        act.Should().Throw<UnauthorisedTunableException>();
    }

    [Fact]
    public void AsInt32_throws_UnauthorisedTunableException_for_an_unauthorised_value()
    {
        var value = ContentValue.Unauthorised;

        var act = () => value.AsInt32();

        act.Should().Throw<UnauthorisedTunableException>();
    }

    [Fact]
    public void AsText_throws_UnauthorisedTunableException_for_an_unauthorised_value()
    {
        var value = ContentValue.Unauthorised;

        var act = () => value.AsText();

        act.Should().Throw<UnauthorisedTunableException>();
    }

    [Fact]
    public void AsBoolean_throws_UnauthorisedTunableException_for_an_unauthorised_value()
    {
        var value = ContentValue.Unauthorised;

        var act = () => value.AsBoolean();

        act.Should().Throw<UnauthorisedTunableException>();
    }

    [Fact]
    public void Object_orders_its_member_names_ordinally_regardless_of_construction_order()
    {
        var value = ContentValue.Object(
        [
            new("zeta", ContentValue.Number(1m)),
            new("Alpha", ContentValue.Number(2m)),
            new("_doc", ContentValue.Text("x")),
            new("alpha", ContentValue.Number(3m)),
        ]);

        value.MemberNames.Should().Equal("Alpha", "_doc", "alpha", "zeta");
    }

    [Fact]
    public void Object_throws_when_the_same_member_name_is_supplied_twice()
    {
        var act = () => ContentValue.Object(
        [
            new("inputCount", ContentValue.Number(3m)),
            new("inputCount", ContentValue.Number(4m)),
        ]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Array_preserves_item_order_because_array_order_is_semantic()
    {
        var value = ContentValue.Array([ContentValue.Number(2m), ContentValue.Number(3m), ContentValue.Number(4m)]);

        value.Items.Select(i => i.AsInt32()).Should().Equal(2, 3, 4);
    }

    [Fact]
    public void TryGetMember_returns_false_and_no_value_for_an_absent_name()
    {
        var value = ContentValue.Object([new("present", ContentValue.Number(1m))]);

        var found = value.TryGetMember("absent", out var member);

        found.Should().BeFalse();
        member.Should().BeNull();
    }

    [Fact]
    public void TryGetMember_returns_the_unauthorised_value_rather_than_reporting_it_absent()
    {
        var value = ContentValue.Object([new("perLevelSuccessRate", ContentValue.Unauthorised)]);

        var found = value.TryGetMember("perLevelSuccessRate", out var member);

        found.Should().BeTrue();
        member!.IsUnauthorised.Should().BeTrue();
    }

    [Fact]
    public void AsNumber_throws_ContentTypeMismatchException_when_the_value_is_text()
    {
        var value = ContentValue.Text("MERGE_DUST");

        var act = () => value.AsNumber();

        act.Should().Throw<ContentTypeMismatchException>();
    }

    [Fact]
    public void AsInt32_throws_ContentTypeMismatchException_for_a_number_with_a_fractional_part()
    {
        var value = ContentValue.Number(0.07m);

        var act = () => value.AsInt32();

        act.Should().Throw<ContentTypeMismatchException>();
    }

    [Fact]
    public void AsNumber_keeps_the_exact_decimal_the_data_file_wrote()
    {
        var value = ContentValue.Number(0.15m);

        value.AsNumber().Should().Be(0.15m);
    }

    [Fact]
    public void Equals_is_structural_over_nested_objects_and_arrays()
    {
        var left = ContentValue.Object(
        [
            new("stoneCostPerLevel", ContentValue.Array([ContentValue.Number(2m), ContentValue.Number(3m)])),
            new("perLevelSuccessRate", ContentValue.Unauthorised),
        ]);
        var right = ContentValue.Object(
        [
            new("perLevelSuccessRate", ContentValue.Unauthorised),
            new("stoneCostPerLevel", ContentValue.Array([ContentValue.Number(2m), ContentValue.Number(3m)])),
        ]);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Equals_distinguishes_an_unauthorised_leaf_from_a_zero()
    {
        var unauthorised = ContentValue.Object([new("frontierChapterBonus", ContentValue.Unauthorised)]);
        var zero = ContentValue.Object([new("frontierChapterBonus", ContentValue.Number(0m))]);

        unauthorised.Should().NotBe(zero);
    }

    [Fact]
    public void Equals_distinguishes_arrays_that_differ_only_in_order()
    {
        var ascending = ContentValue.Array([ContentValue.Number(2m), ContentValue.Number(3m)]);
        var descending = ContentValue.Array([ContentValue.Number(3m), ContentValue.Number(2m)]);

        ascending.Should().NotBe(descending);
    }
}
