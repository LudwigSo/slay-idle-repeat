using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

public sealed class EffectDefinitionTests
{
    /// <summary>Value equality is what lets a parity or determinism test compare two builds.</summary>
    [Fact]
    public void Two_effects_with_the_same_parts_are_equal()
    {
        var a = new EffectDefinition { Id = "PK_X", Op = EffectOp.EXTRA_ATTACK, Value = 1 };
        var b = new EffectDefinition { Id = "PK_X", Op = EffectOp.EXTRA_ATTACK, Value = 1 };

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        (a with { Id = "PK_Y" }).ShouldNotBe(b);
    }
}
