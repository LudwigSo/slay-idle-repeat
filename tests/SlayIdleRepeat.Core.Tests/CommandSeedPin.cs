using System.Globalization;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 The `14` §8.1 / `30` §3 <c>CommandSeed</c> invariant: <b>a command carries a server-issued
/// <c>CommandSeed</c> exactly when it draws out-of-run randomness, and <c>GameContext.CommandSeed</c>
/// is <c>null</c> on every other command.</b>
/// </summary>
/// <remarks>
/// Two regimes. <b>Run draws</b> are <c>Hash64(runSeed, stream, i)</c> off the aggregate's committed
/// seed and persisted counters — state, not ambience — so <c>CommandSeed</c> must be <c>null</c>.
/// <b>Meta draws</b> are <c>Hash64(CommandSeed, stream, i)</c> from <c>i = 0</c> with no persisted
/// counter; the command is atomic and idempotency replays its stored outcome, so a meta draw cannot
/// be re-rolled by resubmission.
/// <para>
/// ⚠️ Still not asserted: the invariant where a <see cref="GameContext"/> is actually <em>paired</em>
/// with a command — nothing checks at <c>Apply</c> time that this context's seed matches this
/// command's classification. <b>Owner: M1-09</b>, the first task with a real pairing to check.
/// </para>
/// </remarks>
internal static class CommandSeedPin
{
    /// <summary>What a violation means, said plainly, so a failure is not "fixed" in the test.</summary>
    internal const string Consequence =
        "CommandSeed is server-issued and META-ONLY (14 §8.1, 30 §3). The domain never invents " +
        "entropy: an in-run draw comes from the Run aggregate's committed runSeed and its persisted " +
        "per-stream counter, and a meta draw comes from this seed at i = 0 with no counter. Fix the " +
        "composition root that built the context, never this rule.";

    /// <summary>
    /// 🔒 The nine meta commands that draw randomness and therefore carry a <c>CommandSeed</c>.
    /// </summary>
    /// <remarks>
    /// <b>The 19 run-command names are deliberately not enumerated here</b>: the sweeps read the run
    /// half off the dispatch table's <c>CommandKind</c>, so transcribing it would give those rules a
    /// second source and let the two agree with each other instead of with the registry.
    /// </remarks>
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

    /// <summary>The namespace `30` §11.4 reserves for the command vocabulary.</summary>
    internal const string CommandsNamespace = "SlayIdleRepeat.Core.Commands";

    /// <summary>Every <b>concrete</b> non-nested command type.</summary>
    /// <remarks>
    /// 🔒 Concrete, because a base with no wire name and no seed is not a command anybody sends —
    /// counting it would wake this file's rules over a vocabulary of one abstraction.
    /// <para>
    /// 🔒 This is a namespace filter, and a namespace filter goes quiet on a move rather than red, so
    /// the backstops matter: <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c> reads the
    /// assembly with Cecil wherever the type is declared, and <c>CommandVocabularyTests</c> pins the
    /// registry against a hand-transcribed list. ⚠️ <c>SubjectSetFloorTests</c>' row is <b>not</b> one
    /// of them — it watches only that the namespace exists.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<Type> CommandTypes { get; } = ConcreteTypesUnder(CommandsNamespace);

    /// <summary>
    /// Every non-nested, non-abstract type declared in <c>Core</c> under a namespace or below it.
    /// </summary>
    /// <remarks>
    /// Exposed rather than inlined so the self-tests can prove this half is not the vacuity source.
    /// <para>
    /// ⚠️ Accessibility is deliberately <b>not</b> filtered: `30` §11.2 makes handlers and rules
    /// internal, so an internal command vocabulary is plausible — and an <c>IsPublic</c> filter would
    /// silence the rule and its tripwire together, on the same accessibility choice.
    /// </para>
    /// </remarks>
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
    /// 🔒 The `14` §2.3 wire name a command type <b>declares</b>, or <c>null</c> when no dispatch row
    /// names the type.
    /// </summary>
    /// <remarks>
    /// 🔒 Read, not guessed. This was a heuristic — strip <c>Command</c>, split on capitals, upper-case
    /// — which turned <c>OpenPvPCommand</c> into <c>OPEN_PV_P</c>. The dispatch table now declares the
    /// name, so a heuristic beside it would be a second answer to a question that has one.
    /// <para>
    /// ⚠️ A type declaring no row answers <c>null</c>: <c>Every_command_type_is_handled_by_Apply</c>
    /// fails the build for that, and duplicating the complaint would be a second mechanism for one
    /// rule.
    /// </para>
    /// </remarks>
    /// <param name="commandType">A concrete <c>GameCommand</c> subtype.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commandType"/> is null.</exception>
    internal static string? WireNameOf(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        return GameRules.RegistrationFor(commandType)?.WireName;
    }

    /// <summary>
    /// 🔒 The invariant itself: everything wrong with pairing <paramref name="commandName"/> with
    /// <paramref name="context"/>'s <c>CommandSeed</c>. Empty means the pairing is legal.
    /// </summary>
    /// <remarks>
    /// The two failures carry distinct wording: "a run command was handed a seed" and "a meta draw was
    /// handed none" are opposite defects with opposite fixes, and a test pinning only "it failed"
    /// would pass while the wrong one fired.
    /// </remarks>
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
