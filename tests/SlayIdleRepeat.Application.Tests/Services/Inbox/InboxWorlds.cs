using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>Inbox fixtures for the Application tier: messages, stores and the template catalogue.</summary>
internal static class InboxWorlds
{
    /// <summary>The player every fixture message belongs to.</summary>
    internal static readonly PlayerId Player = new("PLAYER_inbox");

    /// <summary>An authored template that carries an attachment, with its two parameters.</summary>
    internal const string CompensationTemplate = "loc.mail.compensation.outage.body";

    /// <summary>An authored template that carries none.</summary>
    internal const string AnnouncementTemplate = "loc.mail.announcement.issue_resolved.body";

    /// <summary>The compensation template's parameters, filled with well-typed values.</summary>
    internal static IReadOnlyDictionary<string, string> CompensationParams() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dateUtc"] = "2026-03-14",
            ["hours"] = "3",
        };

    /// <summary>The catalogue as the shipped content set authors it.</summary>
    internal static MailTemplateCatalogue Catalogue() => MailTemplateCatalogue.Read(Worlds.Content);

    /// <summary>A clock reading the suite's start instant, for a store whose expiry must be real.</summary>
    internal static AdjustableClock Clock(DateTimeOffset? at = null)
    {
        var clock = new AdjustableClock();
        clock.Set(at ?? Worlds.Start);

        return clock;
    }

    /// <summary>One stored message.</summary>
    internal static PlayerMessage Message(
        string id = "MSG_1",
        PlayerId? player = null,
        MessageCategory category = MessageCategory.COMPENSATION,
        IReadOnlyList<MailAttachment>? attachments = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? claimedAtUtc = null,
        bool neverExpires = false)
    {
        var created = createdAtUtc ?? Worlds.Start.AddDays(-1);

        return new PlayerMessage(
            new MessageId(id),
            player ?? Player,
            category,
            CompensationTemplate,
            CompensationParams(),
            attachments ?? new[] { new MailAttachment("SOUL_SHARDS", 500) },
            created,
            // A separate flag rather than a null expiry, because the default IS a null: passing
            // `expiresAtUtc: null` would fall through to the 30-day default and the cases about
            // permanence would be testing an ordinary message.
            neverExpires ? null : expiresAtUtc ?? created.AddDays(30),
            ReadAtUtc: null,
            claimedAtUtc);
    }
}
