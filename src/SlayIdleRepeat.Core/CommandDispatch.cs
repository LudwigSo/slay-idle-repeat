using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Commands;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 `30` §2.2 — the table behind the façade. <em>"Internally <c>Apply</c> dispatches to per-command
/// handlers. It is a façade, not a god function."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The shape exists so that M1-02 can add 49 rows without reshaping anything.</b> One row is
/// one call — <see cref="Handled{TCommand}"/> for a command whose handler exists, or
/// <see cref="Deferred{TCommand}"/> for one whose system arrives in a later milestone — and the
/// registration is where a command's `14` §2.3 <b>wire name</b> and its <see cref="CommandKind"/>
/// are declared. Nothing about the table changes as rows are added, and swapping a row from
/// <c>Deferred</c> to <c>Handled</c> when M1-09 or M3 lands its handler is a one-line edit.
/// </para>
/// <para>
/// 🔒 <b>Keyed on the <em>runtime</em> command type, not on a wire string.</b> The wire name is a
/// transport concern (`14` §2.3): by the time <c>Apply</c> is called the envelope has already been
/// parsed and a typed command built, and re-deriving the type from a string inside the domain would
/// put a parser in <c>Core</c>. The wire name is carried anyway because it is the one thing about a
/// command that has no other home — see <c>GameCommand</c>'s remarks for why it is not a member of
/// the command itself.
/// </para>
/// <para>
/// ⚠️ <b>A command type that is registered twice, or a wire name that is, is a defect at type
/// initialisation</b> — a static constructor failure, which surfaces as
/// <c>TypeInitializationException</c> on the first call to <c>Apply</c>. That is loud and immediate,
/// and it is the right moment: `14` §2.3's registry is one vocabulary, and two rows claiming one
/// name would make the wire ambiguous for as long as the process ran.
/// </para>
/// </remarks>
internal sealed class CommandDispatch
{
    private readonly Dictionary<Type, CommandRegistration> _byType = [];
    private readonly Dictionary<string, CommandRegistration> _byWireName = new(StringComparer.Ordinal);

    /// <summary>Every registered command type, by its `14` §2.3 wire name.</summary>
    /// <remarks>
    /// A live read-only view, so it cannot be cast back to the dictionary behind it. It is the
    /// single declared source of the type↔wire-name mapping: <c>CommandSeedPin</c> in
    /// <c>SlayIdleRepeat.Core.Tests</c> reads it instead of guessing a name from a type name, and
    /// the M5 wire envelope will read it rather than declare a second table.
    /// </remarks>
    internal IReadOnlyDictionary<string, Type> TypesByWireName { get; }

    /// <summary>Every registration, in the order it was declared.</summary>
    internal IReadOnlyList<CommandRegistration> Registrations => _byType.Values.ToArray();

    internal CommandDispatch() =>
        TypesByWireName = new WireNameView(_byWireName);

    /// <summary>
    /// Registers a command whose handler exists.
    /// </summary>
    /// <typeparam name="TCommand">The concrete command type.</typeparam>
    /// <param name="wireName">🔒 `14` §2.3's <c>SCREAMING_SNAKE</c> id. The one place it is declared.</param>
    /// <param name="kind">Whether the command runs inside a run — see <see cref="CommandKind"/>.</param>
    /// <param name="handler">The handler. Receives the cloned, time-advanced slice.</param>
    internal CommandDispatch Handled<TCommand>(string wireName, CommandKind kind, CommandHandler<TCommand> handler)
        where TCommand : GameCommand
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Add(new CommandRegistration(
            typeof(TCommand),
            wireName,
            kind,
            (command, input) => handler((TCommand)command, input),
            DeferredTo: null));
    }

    /// <summary>
    /// 🔒 Registers a command whose <b>system</b> has not been built yet: it dispatches to
    /// <c>ILLEGAL_STATE</c>, naming the milestone that will implement it.
    /// </summary>
    /// <typeparam name="TCommand">The concrete command type.</typeparam>
    /// <param name="wireName">🔒 `14` §2.3's <c>SCREAMING_SNAKE</c> id.</param>
    /// <param name="kind">Whether the command runs inside a run.</param>
    /// <param name="owner">The milestone task that implements it, e.g. <c>M3-15</c>.</param>
    /// <remarks>
    /// <para>
    /// 🔒 <b><c>ILLEGAL_STATE</c>, and not a new enum value.</b> `14` §16.2's catalogue is a wire
    /// contract whose values <em>"may be appended, never renamed or reused"</em>, and a
    /// <c>NOT_IMPLEMENTED</c> appended to it would be a permanent row describing a temporary
    /// condition — visible to every client, forever, for the sake of a state that lasts one
    /// milestone. <c>ILLEGAL_STATE</c> is the domain-tier catch-all and it is accurate: a command
    /// whose system does not exist cannot be legal in any state.
    /// </para>
    /// <para>
    /// 🔒 <b>The rejection is not the whole mechanism.</b> A rejection nobody reads is
    /// indistinguishable from a rule, so every deferred command also carries a
    /// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c> entry naming its owning milestone — the
    /// register is what fails the build when the deferral expires. This overload records the owner
    /// on the registration so the two cannot say different things.
    /// </para>
    /// </remarks>
    internal CommandDispatch Deferred<TCommand>(string wireName, CommandKind kind, string owner)
        where TCommand : GameCommand
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException(
                "A deferred command names the milestone task that implements it (M3-15, M4-09). " +
                "Without an owner the ILLEGAL_STATE it dispatches to is indistinguishable from a " +
                "rule that refused the player, and the matching GapRegister entry has nothing to " +
                "agree with.",
                nameof(owner));
        }

        return Add(new CommandRegistration(
            typeof(TCommand),
            wireName,
            kind,
            Handler: null,
            DeferredTo: owner));
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

        if (!_byType.TryAdd(registration.CommandType, registration))
        {
            throw new InvalidOperationException(
                registration.CommandType.FullName + " is registered twice. 30 §2.2 puts one handler " +
                "behind one command; two rows would make which rule runs depend on declaration order.");
        }

        if (!_byWireName.TryAdd(registration.WireName, registration))
        {
            throw new InvalidOperationException(
                "The wire name '" + registration.WireName + "' is registered twice — by " +
                _byWireName[registration.WireName].CommandType.FullName + " and by " +
                registration.CommandType.FullName + ". 14 §2.3 is ONE vocabulary: two types claiming " +
                "one name make the envelope ambiguous in both directions.");
        }

        return this;
    }

    /// <summary>
    /// 🔒 `14` §2.3's ids are <c>SCREAMING_SNAKE</c>. Checked rather than trusted, because this is
    /// the one place the name is declared and a typo here is a wire-contract break nothing else sees.
    /// </summary>
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

    /// <summary>
    /// A read-only projection of the wire-name index onto the command <see cref="Type"/>s, live over
    /// the table so a row added by a later registration is visible without rebuilding it.
    /// </summary>
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

/// <summary>
/// 🔒 `14` §2.3's two halves of the command registry: 19 commands that act inside a run and 30 that
/// act outside one (M1 kickoff, 2026-08-11).
/// </summary>
/// <remarks>
/// <para>
/// It is on the <b>registration</b> rather than on the command because both things it decides are
/// <c>Apply</c>'s business, not the command's:
/// </para>
/// <list type="bullet">
///   <item>a <see cref="Run"/> command is handed a <c>RunRngScope</c> over the run's committed
///   `14` §8.1 counters; a <see cref="Meta"/> command is handed none, because out-of-run draws come
///   from <c>GameContext.CommandSeed</c> with no persisted counter (`30` §3);</item>
///   <item>only a <see cref="Run"/> command slides the run's `14` §16.3 sliding 48-hour TTL.
///   <c>Player.LastAppliedAtUtc</c> advances on every accepted command, but a meta command must not
///   keep a run alive because its owner opened the shop — which is precisely why M1-05 put a second
///   <c>LastAppliedAtUtc</c> on <c>Run</c>.</item>
/// </list>
/// <para>
/// ⚠️ There is deliberately no zero member: an unclassified command would silently become whichever
/// one the enum defaulted to, and both defaults are wrong in a way nothing downstream could notice.
/// </para>
/// </remarks>
internal enum CommandKind
{
    /// <summary>Acts inside a run. The slice carries the <c>Run</c>; draws come from its `14` §8.1 streams.</summary>
    Run = 1,

    /// <summary>Acts outside a run. Draws, if any, come from <c>GameContext.CommandSeed</c> (`30` §3).</summary>
    Meta = 2,
}

/// <summary>What a handler is given and what it returns.</summary>
/// <remarks>
/// The command arrives already narrowed to <typeparamref name="TCommand"/> —
/// <c>CommandDispatch.Handled</c> holds the cast, once, at the one place the type is known
/// statically, so no handler opens with one.
/// </remarks>
/// <typeparam name="TCommand">The concrete command this handler applies.</typeparam>
internal delegate HandlerResult CommandHandler<in TCommand>(TCommand command, HandlerInput input)
    where TCommand : GameCommand;

/// <summary>One row of the dispatch table.</summary>
/// <param name="CommandType">The concrete <c>GameCommand</c> subtype.</param>
/// <param name="WireName">🔒 `14` §2.3's <c>SCREAMING_SNAKE</c> id, declared here and nowhere else.</param>
/// <param name="Kind">Whether the command acts inside a run.</param>
/// <param name="Handler">The handler, or <c>null</c> when the command is deferred.</param>
/// <param name="DeferredTo">
/// The milestone task that will implement it, or <c>null</c> when it is handled today. Exactly one
/// of this and <paramref name="Handler"/> is non-null.
/// </param>
internal sealed record CommandRegistration(
    Type CommandType,
    string WireName,
    CommandKind Kind,
    Func<GameCommand, HandlerInput, HandlerResult>? Handler,
    string? DeferredTo)
{
    /// <summary>Whether this command's system exists yet.</summary>
    internal bool IsHandled => Handler is not null;
}
