using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// <see cref="DifficultyTier"/> is a closed vocabulary of three, and its numbers are wire values
/// and seed inputs.
/// </summary>
/// <remarks>
/// The cross-check against the authored data lives in <c>SlayIdleRepeat.Application.Tests</c>,
/// because it reads files off disk and <c>Core.Tests</c> is hermetic. This file pins what the
/// enum itself claims.
/// </remarks>
public sealed class DifficultyTierTests
{
    /// <summary>
    /// The three tiers, with the numbers <c>CanonicalStateWriter</c> writes and
    /// <c>SeedDerivation.RunSeed</c> hashes. Stated value by value rather than as a set of names,
    /// because the numbers are what travels: renumbering rewrites every state hash that has ever
    /// carried a run and re-seeds every run in existence.
    /// </summary>
    [Fact]
    public void The_tiers_are_the_three_of_10_section_7_with_their_wire_numbers()
    {
        Enum.GetValues<DifficultyTier>().ShouldBe(new[]
        {
            DifficultyTier.NORMAL,
            DifficultyTier.HEROIC,
            DifficultyTier.MYTHIC,
        });

        ((int)DifficultyTier.NORMAL).ShouldBe(1);
        ((int)DifficultyTier.HEROIC).ShouldBe(2);
        ((int)DifficultyTier.MYTHIC).ShouldBe(3);
    }

    /// <summary>
    /// There is no <c>0</c> member, so <c>default(DifficultyTier)</c> cannot read as
    /// <see cref="DifficultyTier.NORMAL"/> and quietly seed a Mythic run as an easy one.
    /// </summary>
    [Fact]
    public void There_is_no_zero_member_so_an_uninitialised_column_is_not_a_tier()
    {
        Enum.IsDefined(default(DifficultyTier)).ShouldBeFalse();
        Enum.IsDefined((DifficultyTier)0).ShouldBeFalse();
        Enum.IsDefined((DifficultyTier)4).ShouldBeFalse();

        // …and the predicate the seam uses is not simply "false for everything".
        Enum.IsDefined(DifficultyTier.NORMAL).ShouldBeTrue();
        Enum.IsDefined(DifficultyTier.MYTHIC).ShouldBeTrue();
    }

    /// <summary>
    /// The underlying type is <see cref="int"/>, which is what <c>Hash64Argument</c> sign-extends and
    /// <c>CanonicalStateWriter</c> widens. A change here is a change to both encodings.
    /// </summary>
    [Fact]
    public void The_underlying_type_is_int()
    {
        Enum.GetUnderlyingType(typeof(DifficultyTier)).ShouldBe(typeof(int));
    }

    /// <summary>
    /// It lives in <c>Primitives/</c> and not under <c>Core/Model/</c>, and that is measured rather
    /// than stylistic: a public enum under <c>Core/Model/</c> fails the mutation-boundary check on
    /// its compiler-generated <c>value__</c> field.
    /// </summary>
    [Fact]
    public void It_lives_in_Primitives_because_a_public_enum_under_Model_fails_the_mutation_rule()
    {
        typeof(DifficultyTier).Namespace.ShouldBe(typeof(FtueBeat).Namespace);
        typeof(DifficultyTier).Namespace.ShouldBe("SlayIdleRepeat.Core.Primitives");
    }
}
