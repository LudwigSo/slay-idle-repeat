namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The six kinds of message the server may send a player.</summary>
/// <remarks>
/// <para>
/// The numeric values are wire values stored in the message row — append, never renumber, never
/// reuse. There is no <c>0</c> member, so a default-valued category is not a category.
/// </para>
/// <para>
/// 🔒 The direction is server to player and there is no other. No member here names a player as a
/// sender, and none ever will: a category is the only vocabulary a message has for saying where it
/// came from, so a seventh member is the whole of what a player-to-player feature would need.
/// </para>
/// <para>
/// 🔒 <see cref="MODERATION"/> and <see cref="ACCOUNT"/> are the record. They never expire and the
/// capacity rule never prunes them — see <see cref="MessageCategories.IsPermanentRecord"/>, which is
/// the one place that pairing is decided.
/// </para>
/// </remarks>
public enum MessageCategory
{
    /// <summary>Ops speaking to everyone: a maintenance window, a known issue, its resolution.</summary>
    ANNOUNCEMENT = 1,

    /// <summary>Ops apologising for an incident. Always carries an attachment.</summary>
    COMPENSATION = 2,

    /// <summary>The system settling something: an event closed, a season paid, a guild disbanded.</summary>
    RECONCILIATION = 3,

    /// <summary>The outcome of a report the player filed, or a sanction applied to them.</summary>
    MODERATION = 4,

    /// <summary>An account fact: a link succeeded, a new device signed in, Plus lapsed.</summary>
    ACCOUNT = 5,

    /// <summary>Something the player reached while they were away.</summary>
    MILESTONE = 6,
}

/// <summary>The rules that read a <see cref="MessageCategory"/> as more than a label.</summary>
/// <remarks>
/// A companion type rather than members on the enum, the same split <c>RejectionReason</c> and
/// <c>RejectionReasons</c> use: the enum is the wire vocabulary and this is what the vocabulary
/// means, so a value appended without a decision here fails at the one place that decides.
/// </remarks>
public static class MessageCategories
{
    /// <summary>Every category, in wire order.</summary>
    public static IReadOnlyList<MessageCategory> All { get; } = new[]
    {
        MessageCategory.ANNOUNCEMENT,
        MessageCategory.COMPENSATION,
        MessageCategory.RECONCILIATION,
        MessageCategory.MODERATION,
        MessageCategory.ACCOUNT,
        MessageCategory.MILESTONE,
    };

    /// <summary>
    /// Whether a message of this category is the record: it never expires, and the capacity rule
    /// never prunes it.
    /// </summary>
    /// <param name="category">The category to judge.</param>
    /// <returns><c>true</c> for the two categories that are the record.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a declared category.</exception>
    /// <remarks>
    /// Exhaustive rather than a two-value set membership test, so a seventh category cannot be
    /// appended without someone deciding which side of this line it falls on. The two answers are
    /// spelled out for the same reason a set literal would not be: the reader sees the whole
    /// vocabulary and its ruling at once.
    /// </remarks>
    public static bool IsPermanentRecord(MessageCategory category) =>
        category switch
        {
            MessageCategory.MODERATION or MessageCategory.ACCOUNT => true,
            MessageCategory.ANNOUNCEMENT
                or MessageCategory.COMPENSATION
                or MessageCategory.RECONCILIATION
                or MessageCategory.MILESTONE => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "A value was appended to MessageCategory without being told whether it is the " +
                "record. A message the player cannot be shown afterwards — a sanction, an account " +
                "event — must never be swept by an expiry job or by the capacity rule, and the " +
                "default for a value nobody ruled on cannot be 'sweepable'."),
        };
}
