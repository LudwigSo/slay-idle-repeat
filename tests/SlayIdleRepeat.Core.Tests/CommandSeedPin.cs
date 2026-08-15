using System.Globalization;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The <c>CommandSeed</c> invariant: a command carries a server-issued <c>CommandSeed</c> exactly
/// when it draws out-of-run randomness; <c>GameContext.CommandSeed</c> is <c>null</c> on every other
/// command. Run draws hash off the aggregate's committed seed and persisted per-stream counters, so
/// they need no seed of their own; meta draws are atomic (idempotency replays the stored outcome) and
/// hash off this seed from <c>i = 0</c> with no persisted counter.
/// </summary>
/// <remarks>
/// Not yet asserted: that a <see cref="GameContext"/> is actually paired with a command — nothing
/// checks at <c>Apply</c> time that this context's seed matches this command's classification.
/// </remarks>
internal static class CommandSeedPin
{
    internal const string Consequence =
        "CommandSeed is server-issued and META-ONLY (14 §8.1, 30 §3). The domain never invents " +
        "entropy: an in-run draw comes from the Run aggregate's committed runSeed and its persisted " +
        "per-stream counter, and a meta draw comes from this seed at i = 0 with no counter. Fix the " +
        "composition root that built the context, never this rule.";

    /// <summary>
    /// The nine meta commands that draw randomness and therefore carry a <c>CommandSeed</c>. The run
    /// commands are deliberately not enumerated anywhere here — sweeps read that half off the dispatch
    /// table's <c>CommandKind</c> so the two sources can't agree with each other instead of the registry.
    /// </summary>
    internal static IReadOnlySet<string> SeedBearingMetaCommands { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "BEGIN_SESSION",
            "REROLL_QUEST",
            "SPIN_WHEEL",
            "REFORGE_ITEM",
            "RETUNE_ITEM",
            "OPEN_CHEST",
            "OPEN_EGG",
            "OPEN_CRATE",
            "START_DUEL",
        };

    internal const string CommandsNamespace = "SlayIdleRepeat.Core.Commands";

    /// <summary>
    /// Every concrete non-nested command type. Filtered by namespace only, not accessibility — handlers
    /// and rules are internal here, so an <c>IsPublic</c> filter would silence the rule entirely.
    /// </summary>
    internal static IReadOnlyList<Type> CommandTypes { get; } = ConcreteTypesUnder(CommandsNamespace);

    /// <summary>Every non-nested, non-abstract type declared in <c>Core</c> under a namespace or below it.</summary>
    internal static IReadOnlyList<Type> ConcreteTypesUnder(string namespacePrefix) =>
        typeof(GameContext).Assembly
            .GetTypes()
            .Where(t => !t.IsNested && !t.IsAbstract)
            .Where(t => IsUnder(t.Namespace, namespacePrefix))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>True when <paramref name="candidate"/> is <paramref name="prefix"/> or below it.</summary>
    internal static bool IsUnder(string? candidate, string prefix) =>
        candidate is not null &&
        (candidate.Equals(prefix, StringComparison.Ordinal) ||
         candidate.StartsWith(prefix + ".", StringComparison.Ordinal));

    /// <summary>
    /// The wire name a command type declares on its dispatch row, or <c>null</c> when no row names it.
    /// Read off the registration rather than derived from the type name, which used to produce
    /// mangled names like <c>OpenPvPCommand</c> → <c>OPEN_PV_P</c>.
    /// </summary>
    internal static string? WireNameOf(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        return GameRules.RegistrationFor(commandType)?.WireName;
    }

    /// <summary>
    /// Everything wrong with pairing <paramref name="commandName"/> with <paramref name="context"/>'s
    /// <c>CommandSeed</c>; empty means the pairing is legal. The two failure messages are worded
    /// distinctly since they are opposite defects with opposite fixes.
    /// </summary>
    internal static IReadOnlyList<string> Violations(string commandName, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(commandName);
        ArgumentNullException.ThrowIfNull(context);

        var draws = SeedBearingMetaCommands.Contains(commandName);

        if (draws && context.CommandSeed is null)
        {
            return new[]
            {
                $"{commandName} draws out-of-run randomness, so GameContext.CommandSeed must carry " +
                $"the server-issued seed — it is null. {Consequence}",
            };
        }

        if (!draws && context.CommandSeed is not null)
        {
            return new[]
            {
                $"{commandName} draws no out-of-run randomness, so GameContext.CommandSeed must be " +
                $"null — it is {context.CommandSeed.Value.ToString(CultureInfo.InvariantCulture)}. " +
                Consequence,
            };
        }

        return Array.Empty<string>();
    }
}
