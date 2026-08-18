using SlayIdleRepeat.Core.Commands;

namespace SlayIdleRepeat.Core;

/// <summary>The dispatch table behind <c>GameRules.Apply</c>: one row per command.</summary>
/// <remarks>
/// Keyed on the runtime command type, not the wire string — by the time <c>Apply</c> runs, the
/// envelope has already been parsed into a typed command. The wire name is carried on the row
/// anyway since nothing else declares it. A command type or wire name registered twice is a
/// defect at type initialisation, surfacing as <c>TypeInitializationException</c> on the first
/// call to <c>Apply</c> — loud and immediate, since two rows claiming one name would make the
/// wire ambiguous for the life of the process.
/// </remarks>
internal sealed class CommandDispatch
{
    private readonly Dictionary<Type, CommandRegistration> _byType = [];
    private readonly Dictionary<string, CommandRegistration> _byWireName = new(StringComparer.Ordinal);

    /// <summary>Every registered command type, by wire name. A live view, not castable back to the source dictionary.</summary>
    internal IReadOnlyDictionary<string, Type> TypesByWireName { get; }

    internal CommandDispatch() =>
        TypesByWireName = new WireNameView(_byWireName);

    /// <summary>Registers a command whose handler exists.</summary>
    /// <typeparam name="TCommand">The concrete command type.</typeparam>
    /// <param name="wireName">The SCREAMING_SNAKE wire id — declared here and nowhere else.</param>
    /// <param name="kind">Whether the command runs inside a run — see <see cref="CommandKind"/>.</param>
    /// <param name="handler">The handler. Receives the cloned, time-advanced slice.</param>
    /// <param name="opensRun">
    /// <c>true</c> only for <c>START_RUN</c>: the one command that creates the run its own
    /// <c>CommandKind.Run</c> would otherwise require to already exist. Every other row leaves
    /// this <c>false</c> and is refused by <see cref="GameRules.Execute"/> both on a run-less slice
    /// and on a slice whose run has ended.
    /// </param>
    /// <param name="changesHeroBuild">
    /// 🔒 <c>true</c> for every command that can change what the hero's <c>ActorStats</c> compose to
    /// — today <c>EQUIP</c>, <c>UNEQUIP</c>, <c>MERGE</c>, <c>ENHANCE</c> and <c>SALVAGE</c>. It is what
    /// <see cref="GameRules.Execute"/>'s battle gate reads to refuse them while a fight is open, and it
    /// lives on the REGISTRATION rather than as a type list inside that gate so a sixth gear command
    /// has to answer the question in the table it is already editing. See
    /// <c>GameRules.BattlePendingRefusesAStockChange</c> for why the refusal exists at all.
    /// </param>
    internal CommandDispatch Handled<TCommand>(
        string wireName,
        CommandKind kind,
        CommandHandler<TCommand> handler,
        bool opensRun = false,
        bool changesHeroBuild = false)
        where TCommand : GameCommand
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Add(new CommandRegistration(
            typeof(TCommand),
            wireName,
            kind,
            (command, input) => handler((TCommand)command, input),
            DeferredTo: null,
            OpensRun: opensRun,
            ChangesHeroBuild: changesHeroBuild));
    }

    /// <summary>
    /// Registers a command whose system hasn't been built yet: it dispatches to
    /// <c>ILLEGAL_STATE</c> rather than a new enum value, since the wire catalogue may only be
    /// appended to and a <c>NOT_IMPLEMENTED</c> entry would outlive the milestone it describes.
    /// </summary>
    /// <typeparam name="TCommand">The concrete command type.</typeparam>
    /// <param name="wireName">The SCREAMING_SNAKE wire id.</param>
    /// <param name="kind">Whether the command runs inside a run.</param>
    /// <param name="owner">
    /// The milestone task that implements it (required: a silent rejection is indistinguishable
    /// from a rule, so the row must name what will turn it into a real handler).
    /// </param>
    internal CommandDispatch Deferred<TCommand>(string wireName, CommandKind kind, string owner)
        where TCommand : GameCommand
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException(
                "A deferred command names the milestone task that implements it (M3-15, M4-09). " +
                "Without an owner the ILLEGAL_STATE it dispatches to is indistinguishable from a " +
                "rule that refused the player, and this row is the only place the deferral's expiry " +
                "is written down.",
                nameof(owner));
        }

        return Add(new CommandRegistration(
            typeof(TCommand),
            wireName,
            kind,
            Handler: null,
            DeferredTo: owner,
            OpensRun: false));
    }

    /// <summary>The registration for a command type, or <c>null</c> when nothing registered it.</summary>
    internal CommandRegistration? For(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        return _byType.GetValueOrDefault(commandType);
    }

    private CommandDispatch Add(CommandRegistration registration)
    {
        RequireWireName(registration.WireName);

        if (!Enum.IsDefined(registration.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(registration),
                registration.Kind,
                "A command is either a RUN command or a META command (14 §2.3, 19 + 30). The kind " +
                "decides whether Apply builds a RunRngScope and whether the run's 14 §16.3 sliding " +
                "TTL moves, so an undefined one is not a default to fall back on.");
        }

        // Enforced once here rather than re-checked on every command's path: the flag exempts a row
        // from both of Execute's run guards — the run-less one and the ended-run one — and the
        // second of those clears the slice's finished run. A meta row carrying it would drop a run
        // it is not even allowed to write, and would slip past the ownership check that exists to
        // catch exactly that, since there would be no run left to compare.
        if (registration.OpensRun && registration.Kind != CommandKind.Run)
        {
            throw new ArgumentException(
                "'" + registration.WireName + "' is registered CommandKind.Meta and opensRun. Only a " +
                "run command can open a run: the flag is what lets a row act on a slice carrying no " +
                "run, and what lets it discard a run that has ended. A meta command is dispatched " +
                "with the player's run in the slice precisely so it can READ it, and marking one " +
                "here would silently drop that run from the result while the ownership check that " +
                "guards it found nothing left to compare.",
                nameof(registration));
        }

        // Both refusals run before either index is written, so a row never ends up half-registered
        // for code that catches the throw and keeps using the table.
        if (_byType.ContainsKey(registration.CommandType))
        {
            throw new InvalidOperationException(
                registration.CommandType.FullName + " is registered twice. 30 §2.2 puts one handler " +
                "behind one command; two rows would make which rule runs depend on declaration order.");
        }

        if (_byWireName.TryGetValue(registration.WireName, out var claimant))
        {
            throw new InvalidOperationException(
                "The wire name '" + registration.WireName + "' is registered twice — by " +
                claimant.CommandType.FullName + " and by " + registration.CommandType.FullName +
                ". 14 §2.3 is ONE vocabulary: two types claiming one name make the envelope " +
                "ambiguous in both directions.");
        }

        _byType.Add(registration.CommandType, registration);
        _byWireName.Add(registration.WireName, registration);

        return this;
    }

    /// <summary>Wire ids are SCREAMING_SNAKE. Checked here since this is the one place a name is declared.</summary>
    private static void RequireWireName(string wireName)
    {
        if (string.IsNullOrEmpty(wireName))
        {
            throw new ArgumentException(
                "A command registration declares 14 §2.3's wire name. It is the id the envelope " +
                "carries and the id every rule keyed on a command name matches against.",
                nameof(wireName));
        }

        foreach (var character in wireName)
        {
            if (character is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
            {
                continue;
            }

            throw new ArgumentException(
                "'" + wireName + "' is not a 14 §2.3 wire name. Those are SCREAMING_SNAKE: capitals, " +
                "digits and underscores only (ROLL_DICE, BEGIN_SESSION). A name spelled any other way " +
                "is a different string on the wire from the one the registry lists, and the mismatch " +
                "surfaces as an unknown command type on a client nobody can reproduce it on.",
                nameof(wireName));
        }
    }

    /// <summary>A read-only, live projection of the wire-name index onto command <see cref="Type"/>s.</summary>
    private sealed class WireNameView(IReadOnlyDictionary<string, CommandRegistration> source)
        : IReadOnlyDictionary<string, Type>
    {
        public IEnumerable<string> Keys => source.Keys;

        public IEnumerable<Type> Values => source.Values.Select(r => r.CommandType);

        public int Count => source.Count;

        public Type this[string key] => source[key].CommandType;

        public bool ContainsKey(string key) => source.ContainsKey(key);

        public bool TryGetValue(string key, out Type value)
        {
            if (source.TryGetValue(key, out var registration))
            {
                value = registration.CommandType;
                return true;
            }

            value = null!;
            return false;
        }

        public IEnumerator<KeyValuePair<string, Type>> GetEnumerator() =>
            source.Select(pair => new KeyValuePair<string, Type>(pair.Key, pair.Value.CommandType))
                  .GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>The two halves of the command registry: commands that act inside a run, and ones that don't.</summary>
/// <remarks>
/// Decided on the registration rather than the command since both effects are <c>Apply</c>'s
/// business: a <see cref="Run"/> command gets a <c>RunRngScope</c> over the run's committed
/// counters and slides the run's sliding TTL; a <see cref="Meta"/> command gets neither — its
/// draws, if any, come from <c>GameContext.CommandSeed</c>, and it must not keep a run alive just
/// because its owner opened the shop. No zero member, so an unclassified command can't silently
/// default to either behaviour.
/// </remarks>
internal enum CommandKind
{
    /// <summary>Acts inside a run. The slice carries the <c>Run</c>; draws come from its RNG streams.</summary>
    Run = 1,

    /// <summary>Acts outside a run. Draws, if any, come from <c>GameContext.CommandSeed</c>.</summary>
    Meta = 2,
}

/// <summary>What a handler is given and what it returns.</summary>
/// <remarks>
/// The command arrives already narrowed to <typeparamref name="TCommand"/> —
/// <c>CommandDispatch.Handled</c> holds the cast, once, so no handler opens with one.
/// </remarks>
/// <typeparam name="TCommand">The concrete command this handler applies.</typeparam>
internal delegate HandlerResult CommandHandler<in TCommand>(TCommand command, HandlerInput input)
    where TCommand : GameCommand;

/// <summary>One row of the dispatch table.</summary>
/// <param name="CommandType">The concrete <c>GameCommand</c> subtype.</param>
/// <param name="WireName">The SCREAMING_SNAKE wire id, declared here and nowhere else.</param>
/// <param name="Kind">Whether the command acts inside a run.</param>
/// <param name="Handler">The handler, or <c>null</c> when the command is deferred.</param>
/// <param name="DeferredTo">
/// The milestone task that will implement it, or <c>null</c> when it is handled today. Exactly one
/// of this and <paramref name="Handler"/> is non-null.
/// </param>
/// <param name="OpensRun">
/// <c>true</c> for exactly one row, <c>START_RUN</c> — the one command allowed to run on a
/// run-less slice, and on a slice whose run has ended, since its job is to create the run its own
/// kind would otherwise require.
/// </param>
/// <param name="ChangesHeroBuild">
/// 🔒 <c>true</c> for every command that can change what the hero's <c>ActorStats</c> compose to, so
/// <c>GameRules.Execute</c> can refuse it while a battle is open. A fact about the command, kept
/// beside <paramref name="OpensRun"/> rather than as a type list inside the gate that reads it: both
/// are questions the table answers about a row, and a gear command added without answering this one
/// would reopen the hole silently.
/// </param>
internal sealed record CommandRegistration(
    Type CommandType,
    string WireName,
    CommandKind Kind,
    Func<GameCommand, HandlerInput, HandlerResult>? Handler,
    string? DeferredTo,
    bool OpensRun = false,
    bool ChangesHeroBuild = false)
{
    /// <summary>Whether this command's system exists yet.</summary>
    internal bool IsHandled => Handler is not null;
}
