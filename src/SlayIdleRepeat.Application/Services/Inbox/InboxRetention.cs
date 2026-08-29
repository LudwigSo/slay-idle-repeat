using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>How long a message lives, and which messages a full inbox may drop.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>The two numbers are operational, not balance</b>, and they are constants here rather than
/// entries in <c>game-data/tuning/</c> for the reason M5-14's rate limits are: a tunable is a number
/// the economy simulator and the balance harness reason about, and neither has anything to say about
/// how many notices a player keeps. Retention and capacity are live-service settings authored once
/// and changed by an operator, and putting them in the content set would make every change a content
/// release.
/// </para>
/// <para>
/// 🔒 <b>Nothing here can destroy a reward.</b> The two record categories are exempt from both rules
/// outright, and a message still holding unclaimed attachments is never prunable at any age — so the
/// worst a full inbox can do is stay full. That is a deliberate reading of the two sentences the
/// design set puts side by side: capacity prunes, and unclaimed attachments are never destroyed.
/// Where they meet, the reward wins and the inbox is allowed to exceed its cap.
/// </para>
/// </remarks>
public static class InboxRetention
{
    /// <summary>How long an expiring message lives.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>How many messages a player's inbox holds before the capacity rule prunes.</summary>
    public const int Capacity = 50;

    /// <summary>When a message of this category, sent now, expires.</summary>
    /// <param name="category">The message's category.</param>
    /// <param name="createdAtUtc">When it is being sent.</param>
    /// <returns>The expiry instant, or <c>null</c> for a category that is the record.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not a declared one.</exception>
    public static DateTimeOffset? ExpiryFor(MessageCategory category, DateTimeOffset createdAtUtc) =>
        MessageCategories.IsPermanentRecord(category) ? null : createdAtUtc + Window;

    /// <summary>
    /// The messages a full inbox should drop to come back under <see cref="Capacity"/>, in the order
    /// they should go.
    /// </summary>
    /// <param name="stored">Everything currently in the player's inbox.</param>
    /// <returns>The ids to delete. Empty when the inbox is inside its capacity or nothing may go.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stored"/> is null.</exception>
    /// <remarks>
    /// The order is the design set's own: the messages carrying nothing go first, oldest first,
    /// then messages whose attachments have already been paid. A message still owing a reward is
    /// never in the list, whatever its age, and neither is a record message — so an inbox of fifty
    /// unclaimed compensations prunes nothing and grows, which is the correct answer.
    /// </remarks>
    public static IReadOnlyList<MessageId> ToPrune(IReadOnlyList<PlayerMessage> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var over = stored.Count - Capacity;

        if (over <= 0)
        {
            return Array.Empty<MessageId>();
        }

        return stored
            .Where(IsPrunable)
            // The two ranks are the whole ordering rule: an empty-handed message before one that has
            // already been paid, and inside each rank the oldest first.
            .OrderBy(m => m.Attachments.Count == 0 ? 0 : 1)
            .ThenBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id.Value, StringComparer.Ordinal)
            .Take(over)
            .Select(m => m.Id)
            .ToArray();
    }

    /// <summary>Whether this message may be dropped to make room.</summary>
    /// <param name="message">The message to judge.</param>
    /// <returns><c>true</c> when dropping it destroys neither a record nor an unpaid reward.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static bool IsPrunable(PlayerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return !message.IsPermanentRecord &&
               (message.Attachments.Count == 0 || message.ClaimedAtUtc is not null);
    }
}
