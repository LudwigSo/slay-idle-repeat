using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>Condition trees: the combinators' construction rules.</summary>
public sealed class EffectConditionTests
{
    /// <summary>
    /// An empty <c>all</c> is vacuously true and an empty <c>any</c> vacuously false, so a
    /// combinator that lost its operands would gate nothing while still reading as a gate.
    /// </summary>
    [Theory]
    [InlineData("all")]
    [InlineData("any")]
    public void A_combinator_with_no_operands_is_rejected(string combinator)
    {
        var thrown = Should.Throw<ArgumentException>(
            () => combinator == "all" ? EffectCondition.All() : EffectCondition.Any());

        thrown.Message.ShouldContain("gates nothing", Case.Sensitive);
    }

    [Fact]
    public void A_null_term_or_operand_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => EffectCondition.Of(null!));
        Should.Throw<ArgumentNullException>(() => EffectCondition.Not(null!));
        Should.Throw<ArgumentNullException>(() => EffectCondition.All(null!));
    }
}
