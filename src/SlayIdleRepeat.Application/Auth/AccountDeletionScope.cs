namespace SlayIdleRepeat.Application.Auth;

/// <summary>One family of rows the hard-delete sweep clears for a deleted account.</summary>
/// <param name="Name">The family's name, as the sweep and its record spell it.</param>
/// <remarks>
/// A list of rows rather than a query, so a system that starts storing something about a player is
/// added here by whoever adds it — and the sweep that already exists picks it up — instead of the
/// deletion silently continuing to cover only what someone wrote a <c>DELETE</c> for once.
/// </remarks>
public readonly record struct AccountDeletionScope(string Name)
{
    /// <summary>Every family the sweep clears, in the order it clears them.</summary>
    /// <remarks>
    /// The auth rows go last: they are what a request still in flight authenticates against, so
    /// clearing them first would leave the rest of the sweep running for an account nothing can
    /// identify any more. Guild membership and contribution anonymisation are deliberately absent —
    /// they belong to the milestone that owns the guild schema, and nothing here keys on a guild.
    /// </remarks>
    public static IReadOnlyList<AccountDeletionScope> All { get; } =
    [
        new("player rows"),
        new("run state"),
        new("messages"),
        new("battle logs"),
        new("economy events"),
        new("cache keys"),
        new("auth rows"),
    ];
}
