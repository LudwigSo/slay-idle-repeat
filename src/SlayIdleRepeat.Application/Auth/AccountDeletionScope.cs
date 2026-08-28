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
    public static IReadOnlyList<AccountDeletionScope> All =>
        throw new NotImplementedException(
            "AccountDeletionScope.All has no body yet. It enumerates the row families a hard delete " +
            "clears: the player rows, run state, messages, battle logs, economy events, cache keys " +
            "and the auth rows themselves.");
}
