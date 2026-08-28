using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>Inbox fixtures: messages, views and the slices that carry them.</summary>
/// <remarks>
/// Everything a case varies is a named parameter with a default, so a case reads as the ONE thing
/// it is about. The defaults describe the ordinary message — a compensation, unclaimed, holding
/// Soul Shards, live for another month.
/// </remarks>
internal static class InboxWorlds
{
    /// <summary>The attachment type every default message carries.</summary>
    internal const string DefaultCurrency = "SOUL_SHARDS";

    /// <summary>How much of it.</summary>
    internal const long DefaultAmount = 500;

    /// <summary>One message.</summary>
    internal static InboxMessage Message(
        string id = "MSG_1",
        MessageCategory category = MessageCategory.COMPENSATION,
        IReadOnlyList<MailAttachment>? attachments = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? claimedAtUtc = null) =>
        new(new MessageId(id),
            category,
            "loc.mail.compensation.outage.body",
            attachments ?? new[] { new MailAttachment(DefaultCurrency, DefaultAmount) },
            createdAtUtc ?? Worlds.NowUtc.AddDays(-1),
            Worlds.NowUtc.AddDays(29),
            claimedAtUtc);

    /// <summary>A view over the given messages.</summary>
    internal static InboxView View(params InboxMessage[] messages) => new(messages);

    /// <summary>A meta slice carrying an inbox — the shape the Application layer loads for a claim.</summary>
    internal static WorldSlice WithInbox(InboxView inbox, PlayerSnapshot? player = null) =>
        new(Worlds.Rehydrated(player ?? PlayerSnapshots.Valid), null, inbox);

    /// <summary>A player whose Energy bar and Reserve are exactly where a case needs them.</summary>
    internal static PlayerSnapshot EnergyAt(int bar, int reserve) =>
        PlayerSnapshots.With(energy: new EnergyBanks(bar, reserve));
}
