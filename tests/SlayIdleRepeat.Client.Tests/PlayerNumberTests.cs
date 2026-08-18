using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// How every number a player reads is written: shortened above ten thousand, written out below it,
/// and written out again while it is being held.
/// </summary>
/// <remarks>
/// 🔴 The boundary is stated from BOTH sides, which is the whole of what makes this suite worth
/// anything. A rule that only ever asserted "12400 reads as 12.4k" is satisfied by a formatter that
/// shortens everything, including the single-digit Gold a first stage actually pays out; a rule that
/// only asserted "60 reads as 60" is satisfied by one that shortens nothing at all, which is the
/// state this build was in before this file existed.
/// </remarks>
public sealed class PlayerNumberTests
{
    /// <summary>Below and at the boundary a number is written out in full.</summary>
    /// <remarks>
    /// Ten thousand itself is on this side of the line: the rule shortens what is ABOVE ten
    /// thousand, and the reroll price, a stage's Gold and every number this build shows today all
    /// sit here — a screen that shortened them would be rounding two-digit sums.
    /// </remarks>
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(60L, "60")]
    [InlineData(9_999L, "9999")]
    [InlineData(10_000L, "10000")]
    public void A_number_up_to_ten_thousand_is_written_out(long value, string expected) =>
        PlayerNumber.Abbreviated(value).ShouldBe(expected);

    /// <summary>And past it, it is shortened — the design's own two examples included.</summary>
    [Theory]
    [InlineData(10_001L, "10.0k")]
    [InlineData(12_400L, "12.4k")]
    [InlineData(999_999L, "999.9k")]
    [InlineData(3_100_000L, "3.1M")]
    [InlineData(4_500_000_000L, "4.5B")]
    [InlineData(7_200_000_000_000L, "7.2T")]
    public void A_number_above_ten_thousand_is_shortened(long value, string expected) =>
        PlayerNumber.Abbreviated(value).ShouldBe(expected);

    /// <summary>
    /// 🔒 Shortened DOWNWARD. A price rounded up reads as more than it costs and a balance rounded
    /// up reads as more than the player has — and the second sends somebody to a control they cannot
    /// afford.
    /// </summary>
    [Theory]
    [InlineData(12_999L, "12.9k")]
    [InlineData(3_199_999L, "3.1M")]
    public void A_shortened_number_never_reads_as_more_than_it_is(long value, string expected) =>
        PlayerNumber.Abbreviated(value).ShouldBe(expected);

    /// <summary>The exact value is always available, and is the plain digits.</summary>
    [Theory]
    [InlineData(60L, "60")]
    [InlineData(12_400L, "12400")]
    [InlineData(3_100_000L, "3100000")]
    public void The_exact_value_is_the_digits_and_nothing_else(long value, string expected) =>
        PlayerNumber.Full(value).ShouldBe(expected);

    /// <summary>
    /// 🔒 The whole long range answers, including the one value whose magnitude has no positive
    /// counterpart.
    /// </summary>
    /// <remarks>
    /// No screen shows a negative Gold balance today and none should. This is here because the
    /// shortening flips the sign to divide, and a flip written in <c>long</c> overflows on exactly
    /// one input — which would be an arithmetic crash on a screen rather than a wrong number.
    /// </remarks>
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(-12_400L)]
    public void An_extreme_value_is_answered_rather_than_thrown(long value)
    {
        PlayerNumber.Abbreviated(value).Length.ShouldBeGreaterThan(0);
        PlayerNumber.Full(value).ShouldBe(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A negative number keeps its sign in front of the shortened magnitude.</summary>
    [Fact]
    public void A_negative_number_keeps_its_sign() =>
        PlayerNumber.Abbreviated(-12_400L).ShouldBe("-12.4k");
}
