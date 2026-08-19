using Shouldly;
using SlayIdleRepeat.Core.Events;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>The pity-counter event: the one thing it refuses. What it means is LuckService's suite to pin.</summary>
public sealed class PityCounterAdvancedTests
{
    private const string ChestA = "chest.standard:A";

    [Fact]
    public void A_counter_movement_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new PityCounterAdvanced(7, ChestA, 1);

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the " +
            "same ordinal while the derived record still read back correctly.");
    }

    /// <summary>A movement with no key is unattributable: the client reconciles its counters off this stream.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void A_counter_movement_with_no_key_is_refused(string? key)
    {
        var thrown = Should.Throw<ArgumentException>(() => new PityCounterAdvanced(1, key!, 1));

        thrown.ParamName.ShouldBe("Key");

        // Pins which rule fired, not just that an ArgumentException was thrown — several guards in
        // Core throw one, and only this one is this rule's.
        thrown.Message.ShouldContain("LuckTuning", Case.Sensitive);
        thrown.Message.ShouldContain("counterKey", Case.Sensitive);
    }
}
