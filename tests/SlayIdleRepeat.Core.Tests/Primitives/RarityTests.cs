using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// <see cref="Rarity"/> is the gear/pet/mount ladder, and its <em>ordering</em> is the operation:
/// every pity guarantee in the game is phrased as an at-least.
/// </summary>
/// <remarks>
/// The cross-check against the guarantee rarities <c>tuning/luck.json</c> authors lives in
/// <c>SlayIdleRepeat.Application.Tests</c>, because it reads files off disk and <c>Core.Tests</c> is
/// hermetic. This file pins what the enum itself claims.
/// </remarks>
public sealed class RarityTests
{
    /// <summary>
    /// The five bands with their wire numbers. Stated value by value rather than as a set of names,
    /// because a stored counter key, an event payload and an analytics row all carry a rarity.
    /// </summary>
    [Fact]
    public void The_bands_are_the_five_of_08_section_2_with_their_wire_numbers()
    {
        Enum.GetValues<Rarity>().ShouldBe(new[] { Rarity.C, Rarity.B, Rarity.A, Rarity.S, Rarity.SS });

        ((int)Rarity.C).ShouldBe(1);
        ((int)Rarity.B).ShouldBe(2);
        ((int)Rarity.A).ShouldBe(3);
        ((int)Rarity.S).ShouldBe(4);
        ((int)Rarity.SS).ShouldBe(5);
    }

    /// <summary>
    /// <em>"A-rarity or better"</em> is <c>rarity &gt;= Rarity.A</c> — the whole reason the ladder is
    /// numbered ascending rather than alphabetically or by drop share.
    /// </summary>
    /// <remarks>
    /// A descending ladder would leave every guarantee comparison in the game silently inverted:
    /// <c>24</c> §4.1's "A or better" would admit C and B and refuse S, and every case that asserted
    /// only "a rarity came out" would stay green.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C, false)]
    [InlineData(Rarity.B, false)]
    [InlineData(Rarity.A, true)]
    [InlineData(Rarity.S, true)]
    [InlineData(Rarity.SS, true)]
    public void A_or_better_is_a_comparison_on_the_ladder(Rarity drawn, bool satisfiesA)
    {
        (drawn >= Rarity.A).ShouldBe(
            satisfiesA,
            "24 §4.1's overshoot rule — 'a natural drop that meets or exceeds a guarantee resets that " +
            "counter' — is this comparison and nothing else.");
    }

    /// <summary>The ladder is strictly ascending, so no two bands compare equal.</summary>
    /// <remarks>
    /// Stated as a chain over the <em>named</em> bands rather than over
    /// <see cref="Enum.GetValues{T}()"/>. That method is documented to answer its values already
    /// sorted by binary value, so comparing its result against its own <c>OrderBy</c> is a claim
    /// that cannot fail for any renumbering whatsoever — it would stay green with
    /// <c>SS = 1 … C = 5</c>, which is the exact inversion this ladder exists to prevent.
    /// </remarks>
    [Fact]
    public void The_ladder_is_strictly_ascending()
    {
        (Rarity.C < Rarity.B).ShouldBeTrue();
        (Rarity.B < Rarity.A).ShouldBeTrue();
        (Rarity.A < Rarity.S).ShouldBeTrue();
        (Rarity.S < Rarity.SS).ShouldBeTrue(
            "24 §4.1's guarantees are all 'X or better', which is `>=` on this ladder and nothing " +
            "else. A band out of order inverts every one of them at once.");

        Enum.GetValues<Rarity>().Select(rarity => (int)rarity).ShouldBeUnique();
    }

    /// <summary>
    /// There is no <c>0</c> member, so <c>default(Rarity)</c> cannot read as <see cref="Rarity.C"/>
    /// and turn an uninitialised column into a real grant at the bottom of the ladder.
    /// </summary>
    [Fact]
    public void There_is_no_zero_member_so_an_uninitialised_column_is_not_a_rarity()
    {
        Enum.IsDefined(default(Rarity)).ShouldBeFalse();
        Enum.IsDefined((Rarity)0).ShouldBeFalse();
        Enum.IsDefined((Rarity)6).ShouldBeFalse();

        // …and the predicate the seam uses is not simply "false for everything".
        Enum.IsDefined(Rarity.C).ShouldBeTrue();
        Enum.IsDefined(Rarity.SS).ShouldBeTrue();
    }

    /// <summary>
    /// The names are the tokens the data files author, so a guarantee rarity resolves by name.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> the perk band vocabulary: <c>COMMON/RARE/EPIC/LEGENDARY</c> is a
    /// different ladder over different things, and the two are kept apart rather than unified on a
    /// guess. A token from one must not resolve in the other.
    /// </remarks>
    [Fact]
    public void The_member_names_are_the_tokens_the_data_files_author()
    {
        Enum.GetNames<Rarity>().ShouldBe(new[] { "C", "B", "A", "S", "SS" });

        Enum.TryParse<Rarity>("LEGENDARY", out _).ShouldBeFalse();
        Enum.TryParse<Rarity>("COMMON", out _).ShouldBeFalse();
    }

    /// <summary>The underlying type is <see cref="int"/> — the encodings widen it as one.</summary>
    [Fact]
    public void The_underlying_type_is_int()
    {
        Enum.GetUnderlyingType(typeof(Rarity)).ShouldBe(typeof(int));
    }
}
