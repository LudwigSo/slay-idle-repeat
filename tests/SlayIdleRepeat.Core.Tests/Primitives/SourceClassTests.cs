using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// <see cref="SourceClass"/> is the closed vocabulary of ten <c>24</c> §3 authors, and its numbers
/// are wire values.
/// </summary>
/// <remarks>
/// The cross-check against <c>tuning/luck.json</c>'s own <c>sourceClasses</c> ids lives in
/// <c>SlayIdleRepeat.Application.Tests</c>, because it reads files off disk and <c>Core.Tests</c> is
/// hermetic. This file pins what the enum itself claims.
/// </remarks>
public sealed class SourceClassTests
{
    /// <summary>
    /// The ten classes, in <c>24</c> §3's table order, with the numbers a stored counter, an event
    /// payload and an analytics row all carry. Stated value by value rather than as a set of names,
    /// because the numbers are what travels: renumbering re-labels every historical row in existence.
    /// </summary>
    [Fact]
    public void The_classes_are_the_ten_of_24_section_3_with_their_wire_numbers()
    {
        Enum.GetValues<SourceClass>().ShouldBe(new[]
        {
            SourceClass.CHEST_STANDARD,
            SourceClass.CHEST_PREMIUM,
            SourceClass.CHEST_APEX,
            SourceClass.DROP_RUN,
            SourceClass.EGG_PET,
            SourceClass.CRATE_MOUNT,
            SourceClass.ENHANCE,
            SourceClass.DRAFT,
            SourceClass.WHEEL,
            SourceClass.MINIGAME,
        });

        ((int)SourceClass.CHEST_STANDARD).ShouldBe(1);
        ((int)SourceClass.CHEST_PREMIUM).ShouldBe(2);
        ((int)SourceClass.CHEST_APEX).ShouldBe(3);
        ((int)SourceClass.DROP_RUN).ShouldBe(4);
        ((int)SourceClass.EGG_PET).ShouldBe(5);
        ((int)SourceClass.CRATE_MOUNT).ShouldBe(6);
        ((int)SourceClass.ENHANCE).ShouldBe(7);
        ((int)SourceClass.DRAFT).ShouldBe(8);
        ((int)SourceClass.WHEEL).ShouldBe(9);
        ((int)SourceClass.MINIGAME).ShouldBe(10);
    }

    /// <summary>
    /// There is no <c>0</c> member, so <c>default(SourceClass)</c> cannot read as
    /// <see cref="SourceClass.CHEST_STANDARD"/> and quietly advance the ten-chest ladder from an
    /// uninitialised column.
    /// </summary>
    [Fact]
    public void There_is_no_zero_member_so_an_uninitialised_column_is_not_a_class()
    {
        Enum.IsDefined(default(SourceClass)).ShouldBeFalse();
        Enum.IsDefined((SourceClass)0).ShouldBeFalse();
        Enum.IsDefined((SourceClass)11).ShouldBeFalse();

        // …and the predicate the seam uses is not simply "false for everything".
        Enum.IsDefined(SourceClass.CHEST_STANDARD).ShouldBeTrue();
        Enum.IsDefined(SourceClass.MINIGAME).ShouldBeTrue();
    }

    /// <summary>
    /// The names are the ids the data file authors, so the registry maps a row to a member by name.
    /// </summary>
    /// <remarks>
    /// <c>24</c> §3's 📐 puts class membership in <c>luck.json</c>; the reader resolves an authored
    /// <c>id</c> against these names, so a rename here is a content break rather than a refactor.
    /// </remarks>
    [Fact]
    public void The_member_names_are_the_ids_the_data_file_authors()
    {
        Enum.GetNames<SourceClass>().ShouldBe(new[]
        {
            "CHEST_STANDARD", "CHEST_PREMIUM", "CHEST_APEX", "DROP_RUN", "EGG_PET",
            "CRATE_MOUNT", "ENHANCE", "DRAFT", "WHEEL", "MINIGAME",
        });
    }

    /// <summary>
    /// The underlying type is <see cref="int"/>, which is what <c>Hash64Argument</c> sign-extends and
    /// <c>CanonicalStateWriter</c> widens. A change here is a change to both encodings.
    /// </summary>
    [Fact]
    public void The_underlying_type_is_int()
    {
        Enum.GetUnderlyingType(typeof(SourceClass)).ShouldBe(typeof(int));
    }

    /// <summary>
    /// It lives in <c>Primitives/</c> and not under <c>Core/Content/</c> or <c>Core/Model/</c>, and
    /// that is measured rather than stylistic: both the tuning reader and the luck rules name it, and
    /// a public enum under <c>Core/Model/</c> fails the mutation-boundary check on its
    /// compiler-generated <c>value__</c> field.
    /// </summary>
    [Fact]
    public void It_lives_in_Primitives_because_both_Content_and_Rules_name_it()
    {
        typeof(SourceClass).Namespace.ShouldBe(typeof(DifficultyTier).Namespace);
        typeof(SourceClass).Namespace.ShouldBe("SlayIdleRepeat.Core.Primitives");
    }
}
