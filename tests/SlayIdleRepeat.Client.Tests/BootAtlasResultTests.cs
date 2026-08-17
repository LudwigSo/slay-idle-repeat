using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The atlas read's two honest answers: it was there, or it was not and here is why.
/// </summary>
public sealed class BootAtlasResultTests
{
    private const string AbsenceReason = "no atlas manifest under artifacts/placeholders";

    [Fact]
    public void Loaded_is_available()
    {
        var result = BootAtlasResult.Loaded(atlasCount: 2, placementCount: 47);

        result.IsAvailable.ShouldBeTrue(
            "this is the flag every caller branches on, including the one deciding whether to draw " +
            "real artwork or a placeholder rectangle.");
    }

    [Fact]
    public void Loaded_carries_the_counts_it_was_given()
    {
        var result = BootAtlasResult.Loaded(atlasCount: 2, placementCount: 47);

        result.AtlasCount.ShouldBe(
            2,
            "the counts are what makes 'loaded' checkable rather than a boolean somebody set. Two " +
            "pages and forty-seven placements is a different fact from an atlas that parsed to " +
            "nothing, and both would satisfy IsAvailable on their own.");
        result.PlacementCount.ShouldBe(47, "the placement count, likewise, is the half that says the pages hold anything");
    }

    [Fact]
    public void Absent_is_not_available()
    {
        var result = BootAtlasResult.Absent(AbsenceReason);

        result.IsAvailable.ShouldBeFalse(
            "no atlas has been generated in any checkout of this repository, so absent is the " +
            "ordinary state rather than the exceptional one. It still has to be distinguishable " +
            "from a loaded atlas, or the game draws from pages that do not exist.");
    }

    [Fact]
    public void Absent_carries_the_reason_it_was_given()
    {
        var result = BootAtlasResult.Absent(AbsenceReason);

        result.Detail.ShouldBe(
            AbsenceReason,
            "'the atlas is missing' is not actionable; 'nothing has been generated under " +
            "artifacts/placeholders' is. The reason is the only part of an absent result anybody " +
            "can act on, and a fixed string would read identically for every cause.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Absent_rejects_a_blank_reason(string reason)
    {
        Should.Throw<ArgumentException>(() => BootAtlasResult.Absent(reason))
              .ParamName.ShouldBe(
                  "detail",
                  "an absent atlas with no stated reason is indistinguishable from one nobody looked " +
                  "for. Absence is deliberately not a failure here, which means the reason is the " +
                  "ONLY trace it leaves — a blank one turns a named non-fatal outcome into silence.");
    }
}
