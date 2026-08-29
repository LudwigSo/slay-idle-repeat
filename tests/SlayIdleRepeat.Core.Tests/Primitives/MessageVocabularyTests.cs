using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>The six categories, what they mean for expiry, and the identity a grant is idempotent on.</summary>
public sealed class MessageVocabularyTests
{
    [Fact]
    public void The_vocabulary_is_the_six_the_design_set_names()
    {
        MessageCategories.All.ShouldBe(
            new[]
            {
                MessageCategory.ANNOUNCEMENT,
                MessageCategory.COMPENSATION,
                MessageCategory.RECONCILIATION,
                MessageCategory.MODERATION,
                MessageCategory.ACCOUNT,
                MessageCategory.MILESTONE,
            },
            "six, by identity and in wire order. A count would be satisfied by a seventh added to " +
            "cover one dropped, and the numeric values are stored in every message row.");

        MessageCategories.All.ShouldBe(
            Enum.GetValues<MessageCategory>(),
            "…and the published list is the enum itself, so a value appended without being listed " +
            "here cannot go unnoticed.");
    }

    [Theory]
    [InlineData(MessageCategory.MODERATION)]
    [InlineData(MessageCategory.ACCOUNT)]
    public void Moderation_and_account_messages_are_the_record(MessageCategory category)
    {
        MessageCategories.IsPermanentRecord(category).ShouldBeTrue(
            "these are never auto-deleted and never pruned by the capacity rule. A sanction notice " +
            "the player can no longer see is a sanction they were never told about.");
    }

    [Theory]
    [InlineData(MessageCategory.ANNOUNCEMENT)]
    [InlineData(MessageCategory.COMPENSATION)]
    [InlineData(MessageCategory.RECONCILIATION)]
    [InlineData(MessageCategory.MILESTONE)]
    public void Every_other_category_expires(MessageCategory category)
    {
        MessageCategories.IsPermanentRecord(category).ShouldBeFalse(
            "the negative control: a predicate that answered true for everything would satisfy the " +
            "two cases above and make every message permanent.");
    }

    [Fact]
    public void A_category_nobody_ruled_on_is_refused_rather_than_treated_as_sweepable()
    {
        var undeclared = (MessageCategory)99;

        Should.Throw<ArgumentOutOfRangeException>(() => MessageCategories.IsPermanentRecord(undeclared))
            .Message.ShouldContain(
                "without being told whether it is the record",
                Case.Sensitive,
                "the default for an unruled value cannot be 'sweepable' — that is how a new " +
                "category of permanent record gets deleted by a job that never heard of it.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_message_id_is_refused(string value)
    {
        Should.Throw<ArgumentException>(() => new MessageId(value));
    }

    [Fact]
    public void A_null_message_id_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new MessageId(null!));
    }

    [Fact]
    public void A_default_message_id_reads_as_one_rather_than_as_null()
    {
        default(MessageId).ToString().ShouldBe(
            "default(MessageId)",
            "a bare `=> Value` would return null from a method every caller types as non-null, and " +
            "the marker cannot be mistaken for a real id.");
    }

    [Fact]
    public void A_blank_attachment_type_is_refused()
    {
        Should.Throw<ArgumentException>(() => new MailAttachment("  ", 1));
    }
}
