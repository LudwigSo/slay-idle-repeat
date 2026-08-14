using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// 🔒 `10` §7 / `02` §2 — <see cref="DifficultyTier"/> is a closed vocabulary of three, and its
/// numbers are wire values <b>and</b> seed inputs.
/// </summary>
/// <remarks>
/// The cross-check against the authored data (<c>progression.json</c>, <c>par_power.json</c>) lives
/// in <c>SlayIdleRepeat.Application.Tests</c>, because it reads files off disk and <c>Core.Tests</c>
/// is hermetic. This file pins what the enum itself claims.
/// </remarks>
public sealed class DifficultyTierTests
{
    /// <summary>
    /// 🔒 The three tiers `10` §7 names, with the numbers <c>CanonicalStateWriter</c> writes and
    /// <c>SeedDerivation.RunSeed</c> hashes.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated value by value rather than as a set of names, because the <b>numbers</b> are what
    /// travels. Renumbering rewrites every <c>stateHash</c> that has ever carried a run <em>and</em>
    /// re-seeds every run in existence — a player resuming a run would find a different board under
    /// it. Append, never renumber, never reuse.
    /// </remarks>
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
    /// 🔒 There is no <c>0</c> member, so <c>default(DifficultyTier)</c> cannot read as
    /// <see cref="DifficultyTier.NORMAL"/> and quietly seed a Mythic run as an easy one.
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="CurrencyId"/> and <see cref="FtueBeat"/>: an uninitialised column
    /// reads as zero, and zero must not be a legal tier. <c>Run.Rehydrate</c> is the seam that
    /// refuses it.
    /// </remarks>
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
    /// 🔒 It lives in <c>Primitives/</c> and not under <c>Core/Model/</c>, and that is measured rather
    /// than stylistic.
    /// </summary>
    /// <remarks>
    /// A public enum under <c>Core/Model/</c> fails
    /// <c>AccessibilityBoundaryTests.Apply_is_the_only_public_mutation</c> on its compiler-generated
    /// <c>value__</c> field — public, neither <c>literal</c> nor <c>initonly</c>, and carrying no
    /// <c>[CompilerGenerated]</c>. This pins the placement so a later tidy-up move turns red here rather
    /// than in the architecture suite with no explanation.
    /// </remarks>
    [Fact]
    public void It_lives_in_Primitives_because_a_public_enum_under_Model_fails_the_mutation_rule()
    {
        typeof(DifficultyTier).Namespace.ShouldBe(typeof(FtueBeat).Namespace);
        typeof(DifficultyTier).Namespace.ShouldBe("SlayIdleRepeat.Core.Primitives");
    }
}
