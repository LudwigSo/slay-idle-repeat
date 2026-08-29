using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// The v1 attachment vocabulary: what this build grants, and every kind the design set names that it
/// refuses BY NAME rather than by silently dropping.
/// </summary>
public sealed class MailAttachmentKindsTests
{
    private static MailAttachmentResolution Resolve(string type, long amount = 1) =>
        MailAttachmentKinds.Resolve(new MailAttachment(type, amount));

    [Theory]
    [InlineData("CROWNS", CurrencyId.CROWNS)]
    [InlineData("SOUL_SHARDS", CurrencyId.SOUL_SHARDS)]
    [InlineData("ENHANCE_STONES", CurrencyId.ENHANCE_STONES)]
    [InlineData("MERGE_DUST", CurrencyId.MERGE_DUST)]
    [InlineData("BEAST_FEED", CurrencyId.BEAST_FEED)]
    [InlineData("HONOR", CurrencyId.HONOR)]
    public void Every_wallet_currency_is_grantable(string type, CurrencyId currency)
    {
        var resolved = Resolve(type);

        resolved.IsGrantable.ShouldBeTrue();
        resolved.Kind.ShouldBe(MailAttachmentGrantKind.WALLET_CURRENCY);
        resolved.Currency.ShouldBe(currency);
    }

    [Fact]
    public void Energy_is_grantable_and_is_not_a_wallet_currency()
    {
        var resolved = Resolve("ENERGY");

        resolved.Kind.ShouldBe(
            MailAttachmentGrantKind.ENERGY,
            "Energy is held in two banks with a cap and an overflow, not as a wallet balance — a " +
            "kind that routed it through the wallet would write past the bar's maximum.");
        resolved.Currency.ShouldBeNull();
    }

    [Fact]
    public void Gold_is_refused_because_it_is_run_scoped()
    {
        Resolve("GOLD").Refusal.ShouldBe(
            MailAttachmentRefusal.RUN_SCOPED_CURRENCY,
            "Gold has no meta balance. Granting it outside a run has nowhere to land, and granting " +
            "it INTO a run would pay a reward into state the next run discards.");
    }

    [Theory]
    [InlineData("CHEST")]
    [InlineData("EGG")]
    [InlineData("CRATE")]
    public void A_container_is_refused_because_the_shelf_is_not_built(string type)
    {
        Resolve(type).Refusal.ShouldBe(
            MailAttachmentRefusal.CONTAINER_SHELF_ABSENT,
            "a container claims as an UNOPENED container onto a shelf. Pre-opening it here would " +
            "read pity and Focus at claim time, which is the one thing a container must not do — " +
            "and there is no shelf to put it on.");
    }

    [Fact]
    public void Gear_is_refused_because_a_rolled_reward_needs_a_seeded_draw()
    {
        Resolve("GEAR").Refusal.ShouldBe(
            MailAttachmentRefusal.SEEDED_GEAR_GRANT_ABSENT,
            "an item is rolled, and the nightly auto-grant job holds no per-command seed to roll " +
            "with — so a gear attachment would be a second, unreproducible source of loot.");
    }

    [Theory]
    [InlineData("SET_TOKENS")]
    [InlineData("BEAST_MARKS")]
    public void A_non_wallet_counter_is_refused_because_it_has_no_home(string type)
    {
        Resolve(type).Refusal.ShouldBe(
            MailAttachmentRefusal.NO_WALLET_HOME,
            "these are deliberately absent from CurrencyId: a wallet slot for them would start " +
            "reporting them as income.");
    }

    [Fact]
    public void An_unknown_type_is_refused_by_name_rather_than_ignored()
    {
        Resolve("SPARKLES").Refusal.ShouldBe(
            MailAttachmentRefusal.UNKNOWN_TYPE,
            "a stored row may name a type this build does not know. Skipping it silently would pay " +
            "a message less than it says it owes and still stamp it claimed.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_amount_is_refused_before_the_type_is_read(long amount)
    {
        Resolve("SOUL_SHARDS", amount).Refusal.ShouldBe(
            MailAttachmentRefusal.NON_POSITIVE_AMOUNT,
            "judged first, so an authored zero is reported as the authoring fault it is rather than " +
            "as whichever type happened to be beside it.");
    }

    [Fact]
    public void The_grantable_vocabulary_is_exactly_the_types_that_resolve()
    {
        MailAttachmentKinds.Grantable.ShouldAllBe(
            type => MailAttachmentKinds.Resolve(new MailAttachment(type, 1)).IsGrantable,
            "the published list and the resolver are the same answer. A list that named a type the " +
            "resolver refuses would offer an operator a send the claim path then holds for ever.");

        MailAttachmentKinds.Grantable.Count.ShouldBe(
            7,
            "the six wallet currencies plus Energy, stated as a number so a vocabulary that quietly " +
            "shrank to one still-grantable type could not satisfy the assertion above.");
    }
}
