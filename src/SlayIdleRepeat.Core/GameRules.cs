using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 `30` §2 — the transition function. <em>"The whole domain reduces to one signature. Everything
/// else in this document is detail about its arguments."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The five properties of `30` §2.1, and where each one actually lives</b> — because a
/// property nothing enforces is a paragraph:
/// </para>
/// <list type="table">
///   <item><term><b>P1 · Pure</b></term><description>No I/O, no clock, no ambient randomness. Time
///   arrives as <c>GameContext.NowUtc</c> and randomness as the run's committed seed or
///   <c>GameContext.CommandSeed</c> (`30` §3). Enforced by
///   <c>DomainPurityTests.Domain_has_no_ambient_time_or_randomness</c> and
///   <c>AmbientApiTests</c>. The one static this type holds is <see cref="Dispatch"/>, which is
///   built once and never written again — a table, not state.</description></item>
///   <item><term><b>P2 · Synchronous</b></term><description>Enforced assembly-wide by
///   <c>DomainPurityTests.Domain_is_synchronous</c>; async in the domain is always a symptom of
///   hidden I/O.</description></item>
///   <item><term><b>P3 · Total</b></term><description>Every command on every state returns a
///   result. An unregistered command type is <c>ILLEGAL_STATE</c>, not an exception. ⚠️ The
///   converse is drawn deliberately: an <b>exception</b> out of <see cref="Apply"/> means the
///   <em>caller</em> or the <em>domain</em> is wrong — a null argument, a slice missing the run its
///   command needs, a handler that hand-wrote an RNG counter — never that the player asked for
///   something they cannot have.</description></item>
///   <item><term><b>P4 · Immutable</b></term><description>The slice is <b>cloned</b> before the
///   handler runs (see <see cref="Clone"/>), so the caller's aggregates are untouched whatever the
///   handler does, and a rejection returns the caller's own slice.</description></item>
///   <item><term><b>P5 · Complete</b></term><description>Enforced by
///   <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c>: a concrete <c>GameCommand</c>
///   subtype named by neither this type nor anything under <c>Core/Handlers/</c> fails the
///   build.</description></item>
/// </list>
/// <para>
/// 🔒 <b>A façade, not a god function</b> (`30` §2.2). <see cref="Apply"/> owns the five things that
/// are true of <em>every</em> command — clone, catch up, dispatch, fold the RNG counters back, stamp
/// the events — and owns no game rule at all. Everything a specific command means is in its handler.
/// </para>
/// <para>
/// ⚠️ <b>The dispatch table is empty in M1-06, and that is the landing order rather than an
/// oversight.</b> M1-02 declares the 49 concrete commands of `14` §2.3 and adds one row here for
/// each. <c>Every_command_type_is_handled_by_Apply</c> fails the build for any concrete subtype no
/// row names, so the base type and the table have to land <b>before</b> the vocabulary: with zero
/// subtypes the rule quantifies over nothing and stays green, which is exactly why this task is
/// first.
/// </para>
/// </remarks>
public static class GameRules
{
    /// <summary>
    /// 🔒 `30` §2.2's dispatch table — <b>the</b> place a command is bound to its handler, its
    /// `14` §2.3 wire name and its <see cref="CommandKind"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>M1-02 adds 49 rows below and nothing else moves.</b> One row is one call:
    /// </para>
    /// <code>
    /// private static readonly CommandDispatch Dispatch = new CommandDispatch()
    ///     .Deferred&lt;RollDiceCommand&gt;("ROLL_DICE", CommandKind.Run, "M3-02")
    ///     .Handled&lt;BeginSessionCommand&gt;("BEGIN_SESSION", CommandKind.Meta, BeginSession.Handle);
    /// </code>
    /// <para>
    /// A command whose <em>system</em> arrives later is registered with <c>Deferred</c> and rejects
    /// with <c>ILLEGAL_STATE</c> naming its milestone; swapping it to <c>Handled</c> on the day that
    /// milestone lands is a one-line edit. Every <c>Deferred</c> row also carries a
    /// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c> entry, which is what makes the deferral
    /// expire by itself instead of waiting to be noticed.
    /// </para>
    /// <para>
    /// ⚠️ <b>Handlers stay out of <c>Core/Handlers/</c> until M1-09 puts the first real one there.</b>
    /// That namespace is a <c>SubjectSetFloorTests.Pending</c> entry owned by M1-09, and populating
    /// it with dispatch plumbing would move the entry — and the two rules keyed on it — a milestone
    /// early, over types that are not handlers.
    /// </para>
    /// </remarks>
    private static readonly CommandDispatch Dispatch = new();

    /// <summary>
    /// 🔒 Every registered command type, by the `14` §2.3 wire name its dispatch row declares — the
    /// <b>single</b> declared source of that mapping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because the alternative was a guess. M1-07 needed a type→wire-name mapping to pin
    /// the <c>CommandSeed</c> invariant and had to build a documented <em>heuristic</em>
    /// (<c>SpinWheelCommand</c> → <c>SPIN_WHEEL</c>, which reads <c>OpenPvPCommand</c> as
    /// <c>OPEN_PV_P</c>) because no authored scheme existed — carried-forward item 4. Declaring the
    /// name on the registration and reading it here retires the heuristic: the mapping is now
    /// stated, not inferred, and it is stated in the one place that also has to know the command's
    /// handler and kind, so the three cannot drift apart.
    /// </para>
    /// <para>
    /// ⚠️ <c>internal</c> until something outside <c>Core</c> needs it. Today's consumer is
    /// <c>SlayIdleRepeat.Core.Tests.CommandSeedPin</c>, which the `30` §11.3 <c>InternalsVisibleTo</c>
    /// grant already reaches; M5-03's wire envelope is the first caller that will need it public,
    /// and it should read this rather than declare a second table (`30` §11.6).
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<string, Type> CommandTypesByWireName => Dispatch.TypesByWireName;

    /// <summary>The dispatch row for a command type, or <c>null</c> when no row names it.</summary>
    /// <remarks>Exposed for the domain suite, which drives the table's decisions directly.</remarks>
    internal static CommandRegistration? RegistrationFor(Type commandType) => Dispatch.For(commandType);

    /// <summary>
    /// 🔒 `30` §2 — the one public way to change state in this game.
    /// </summary>
    /// <param name="state">The aggregates this command may read or write (`30` §4.1).</param>
    /// <param name="command">What the player intends (`14` §2.3).</param>
    /// <param name="context">Everything ambient, passed as data (`30` §3).</param>
    /// <returns>
    /// Whether the command was accepted, the domain-tier reason if it was not, the complete
    /// resulting state, and the events it produced.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// 🔒 A <b>defect</b>, never a refusal: the slice does not carry the run its command acts on, an
    /// aggregate does not round-trip through its own snapshot, a handler hand-wrote an RNG stream
    /// position, or a handler stamped an event's <c>Sequence</c> itself.
    /// </exception>
    public static CommandResult Apply(WorldSlice state, GameCommand command, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var registration = Dispatch.For(command.GetType());

        // 🔒 P3 (total). An unregistered command type does NOT throw — it is refused, with the
        // domain-tier catch-all. 14 §16.2's UNKNOWN_COMMAND_TYPE is a TRANSPORT value: the server
        // refuses a wire name it has no row for before the domain is ever invoked, and 30 §2
        // forbids Apply from returning one. Every_command_type_is_handled_by_Apply is what keeps
        // this arm unreachable in practice — it fails the build for any concrete command no row
        // names — so this is the answer for a caller that built a command type by hand.
        if (registration is null)
        {
            return CommandResult.Reject(RejectionReason.ILLEGAL_STATE, state);
        }

        // 🔒 A run command whose slice carries no run is a LOADING defect, not a rejection. 30 §4.1
        // makes "loading the right slice" the Application layer's job, and 14 §16.2's RUN_NOT_FOUND
        // is a transport-tier value that never reaches Apply. Answering ILLEGAL_STATE here would
        // tell the player a rule refused them and leave the miswired caller running.
        if (registration.Kind == CommandKind.Run && state.Run is null)
        {
            throw new InvalidOperationException(
                "'" + registration.WireName + "' is a CommandKind.Run command and this WorldSlice " +
                "carries no Run. 30 §4.1 makes loading the right slice the Application layer's job; " +
                "14 §16.2's RUN_NOT_FOUND is a TRANSPORT-tier value, refused before the domain is " +
                "invoked, so Apply may not return it (30 §2). This is a miswired caller, not a " +
                "player asking for something they cannot have.");
        }

        // P4. Everything from here works on a copy; the caller's slice is never written to.
        var working = Clone(state, context.Content);

        // 🔒 30 §2.3's AdvanceTime, FIRST BY CONSTRUCTION. The handler never receives the raw slice
        // — only the one this produced — so nothing a handler can write runs before the catch-up.
        AdvanceTime(working, context);

        var rng = registration.Kind == CommandKind.Run
            ? new RunRngScope(working.Run!.RunSeed, working.Run.RngStreamPositions)
            : null;

        var handled = registration.IsHandled
            ? registration.Handler!(command, new HandlerInput(working, context, rng))
            : Unimplemented(registration);

        if (!handled.Accepted)
        {
            // 🔒 The working copy is DISCARDED, so a refused command provably changed nothing —
            // including any draws its handler took before the rule refused it. 14 §8.1 needs that:
            // a run that consumed draw indices on a rejected command would replay differently.
            return CommandResult.Reject(handled.Rejection!.Value, state);
        }

        FoldRngPositions(state.Run, working.Run, rng, registration);
        MarkApplied(working, context.NowUtc, registration.Kind);

        return CommandResult.Accept(working, Stamp(handled.Events));
    }

    /// <summary>
    /// 🔒 `30` §2.1's <b>P4</b>, made structural: a deep copy of the slice, through each aggregate's
    /// own <c>ToSnapshot()</c>/<c>Rehydrate()</c> pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a copy is not optional.</b> `30` §11.2 makes the aggregates classes with <c>internal</c>
    /// mutators — a handler changes state by calling them — so "no in-place mutation of the input"
    /// can only be true if the handler is given something other than the input. Three things fall
    /// out of doing it here rather than per handler: a <b>rejection</b> is provably state-free
    /// (the copy is discarded and the caller's slice returned); the Application layer may keep the
    /// state it loaded across a refused command; and every accepted command proves, on the way in,
    /// that the aggregate round-trips through the shape it is persisted as.
    /// </para>
    /// <para>
    /// 🔒 <b>A failed rehydration here is a defect and is raised as one.</b> `30` §11.3 puts
    /// validation at the seam so <em>"a corrupt row fails loudly rather than silently three rules
    /// later"</em>; a slice already in memory that cannot round-trip is an aggregate that was
    /// mutated into a state its own invariants refuse, which is a rule that is wrong — not a player
    /// who asked for too much.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cost, stated.</b> This is two snapshot builds and two validated rehydrations per
    /// command, which is real work on M1-11's 180-simulated-day budget. It is paid deliberately: the
    /// alternative is either mutable aggregates handed straight to handlers (P4 gone) or a second,
    /// unvalidated <c>Clone()</c> on every aggregate (a second construction path beside the one
    /// `30` §11.3 sanctions). If the budget ever needs it, the place to look is the snapshot
    /// copying, not this seam.
    /// </para>
    /// </remarks>
    private static WorldSlice Clone(WorldSlice state, ContentSnapshot content)
    {
        var player = Player.Rehydrate(state.Player.ToSnapshot(), content);

        if (player.IsFailure)
        {
            throw new InvalidOperationException(RoundTripFailure("Player", player.Error));
        }

        if (state.Run is null)
        {
            return new WorldSlice(player.Value, null);
        }

        var run = Run.Rehydrate(state.Run.ToSnapshot());

        return run.IsSuccess
            ? new WorldSlice(player.Value, run.Value)
            : throw new InvalidOperationException(RoundTripFailure("Run", run.Error));
    }

    /// <summary>
    /// 🔒 `30` §2.3's lazy catch-up seam — <em>"the first step of every command handler is
    /// <c>AdvanceTime(state, context.NowUtc)</c>"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>M1-08 writes the catch-up; M1-06 writes the seam, and the seam is the deliverable.</b>
    /// What is settled here is <em>where</em> it runs and that nothing can run before it: a handler
    /// is only ever handed the slice this has already been called on, so "first" is a property of
    /// the call graph rather than a convention every future handler has to remember. What is
    /// <b>not</b> settled here is what it does — §2.3 lists Energy regeneration accrual, the 05:00
    /// UTC daily resets, weekly boundaries, Plus expiry and event-window state, and several of those
    /// act on state no milestone has authored yet (quest expiry, ad caps, dungeon entries, daily
    /// shop stock). Writing a partial catch-up now would be a rule that looks complete and silently
    /// skips four boundaries (steering <b>S6</b>).
    /// </para>
    /// <para>
    /// 🔒 It takes the whole <see cref="WorldSlice"/> and the whole <see cref="GameContext"/> rather
    /// than <c>(player, nowUtc)</c>: §2.3's boundaries are read off both aggregates
    /// (<c>Player.EnergyAnchorUtc</c>, <c>Player.DailyPeriodStartUtc</c>,
    /// <c>Player.WeeklyPeriodStartUtc</c>, <c>Run.LastAppliedAtUtc</c>) and several of them read
    /// tunables out of <c>GameContext.Content</c>. A narrower signature would have to widen on the
    /// commit that implements it, which is a change to every call site — of which there is
    /// deliberately exactly one.
    /// </para>
    /// <para>
    /// ⚠️ M1-04 recorded the one thing M1-08 must not forget: clamp a negative span with
    /// <c>Math.Max(TimeSpan.Zero, now − anchor)</c>, because a host clock that went backwards would
    /// otherwise accrue a negative amount of Energy.
    /// </para>
    /// </remarks>
    private static void AdvanceTime(WorldSlice state, GameContext context)
    {
        _ = state;
        _ = context;

        // M1-08. Deliberately empty: see the remarks. The call site is the deliverable.
    }

    /// <summary>
    /// 🔒 The RNG write-back (M1 kickoff decision 5): <b><c>Apply</c></b> folds the scope's final
    /// positions into the run, and refuses a run whose positions moved underneath the handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the comparison is against the <em>caller's</em> run.</b> The working copy starts life
    /// with exactly the positions the caller's run carries, and <c>Run.CommitStreamPositions</c> is
    /// the only thing that can change them. So a difference here means one thing only: the handler
    /// called that seam itself. That is a determinism defect rather than a rejection — the scope's
    /// positions and the hand-written ones disagree about how many draws this command took, and
    /// whichever is stored, some later draw repeats a sequence the player has already played
    /// (`14` §8.1).
    /// </para>
    /// <para>
    /// ⚠️ M1-05's seam already refuses the <em>partial</em> version of the same mistake: it takes the
    /// whole map and rejects a dropped key, so a handler that wanted to hand-write one position would
    /// have to reconstruct the entire committed set to get that far. This check is what catches the
    /// handler that did.
    /// </para>
    /// <para>
    /// The fold itself is unconditional on an accepted run command, which is what makes "a handler
    /// that drew and forgot to write the counter back" <b>unexpressible</b> rather than merely
    /// caught: there is nothing for a handler to forget.
    /// </para>
    /// </remarks>
    private static void FoldRngPositions(
        Run? original, Run? working, RunRngScope? rng, CommandRegistration registration)
    {
        if (rng is null || working is null)
        {
            return;
        }

        var committed = original!.RngStreamPositions;
        var carried = working.RngStreamPositions;

        if (!SamePositions(committed, carried))
        {
            throw new InvalidOperationException(
                "The handler for '" + registration.WireName + "' wrote the run's 14 §8.1 stream " +
                "positions itself. Apply owns that write (M1 kickoff decision 5): it opens a " +
                "RunRngScope over the run's committed seed and counters, hands it to the handler, and " +
                "folds the scope's final positions back — so the persisted counter always equals the " +
                "number of draws actually taken. A hand-written position and the scope's disagree " +
                "about that count, and whichever is stored, some later draw repeats a sequence the " +
                "player has already played. That is a DETERMINISM DEFECT, not a rejection: a " +
                "RejectionReason would hand the corrupt scope back to the player as a polite 'no' " +
                "and leave the run in it. Draw through HandlerInput.Rng and write nothing.");
        }

        working.CommitStreamPositions(rng.FinalPositions());
    }

    /// <summary>
    /// `14` §16.3 — records that a command was accepted, which is what the TTLs slide from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The player always; the run only for a run command.</b> That asymmetry is the whole
    /// reason M1-05 put a second <c>LastAppliedAtUtc</c> on <c>Run</c>:
    /// <c>Player.LastAppliedAtUtc</c> advances on meta commands too, so sliding the run's 48-hour
    /// TTL off it would keep a run alive because its owner opened the shop.
    /// </para>
    /// <para>
    /// 🔒 <b>On acceptance, and only on acceptance.</b> A refused command changed nothing, so it
    /// must not extend a TTL either — otherwise a client could hold a run open indefinitely by
    /// sending commands it knows will be refused.
    /// </para>
    /// <para>
    /// ⚠️ It is written here rather than left to M1-08 because M1-08's catch-up rolls forward
    /// <em>from</em> these anchors: an <c>Apply</c> that never advanced them would have every command
    /// re-accrue from the same instant forever.
    /// </para>
    /// </remarks>
    private static void MarkApplied(WorldSlice state, DateTimeOffset nowUtc, CommandKind kind)
    {
        state.Player.MarkApplied(nowUtc);

        if (kind == CommandKind.Run)
        {
            state.Run!.MarkApplied(nowUtc);
        }
    }

    /// <summary>
    /// 🔒 Stamps each event with its ordinal within <b>this</b> <c>CommandResult</c>'s list — the
    /// ruling M1-03 recorded when it authored <c>DomainEvent(int Sequence)</c> and left the assigner
    /// to M1-06.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It orders one command's output and nothing wider.</b> `14` §2.4 replays the list as the
    /// animation script and `14` §7.1 appends it to the economy log; both need the order the rules
    /// produced them in. ⚠️ It is <b>not</b> `14` §16.3's wire <c>sequence</c>, which is the
    /// per-run/per-player <b>command</b> counter on the request envelope and lives in
    /// <c>SlayIdleRepeat.Contracts</c>. Two different numbers, one word.
    /// </para>
    /// <para>
    /// 🔒 <b>From 1, not from 0</b>, and that is what makes <c>DomainEvent.UnstampedSequence</c>
    /// (which is 0) mean something: M1-03's remarks require <c>Apply</c> to be able to tell an
    /// unstamped event from a first one, and a 0-based stamp would make the two identical.
    /// </para>
    /// <para>
    /// 🔒 <b>An event that arrives already stamped is refused.</b> The ordinal is <c>Apply</c>'s to
    /// assign — <em>"never by a constructor, and never by a caller"</em> — and a handler that
    /// assigned its own has decided where in a list it does not yet know the shape of its event
    /// belongs. Overwriting it silently would let that pass unnoticed until the economy log and the
    /// animation script disagreed about the order of one command's effects.
    /// </para>
    /// <para>
    /// The rewrite is <c>e with { Sequence = n }</c>, which reaches every subtype through the
    /// abstract record's virtual <c>&lt;Clone&gt;$</c> — M1-03 verified that and deliberately left
    /// <c>Sequence</c> as <c>init</c> for this.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<DomainEvent> Stamp(IReadOnlyList<DomainEvent> events)
    {
        if (events.Count == 0)
        {
            return events;
        }

        var stamped = new DomainEvent[events.Count];

        for (var i = 0; i < events.Count; i++)
        {
            var produced = events[i];

            if (produced is null)
            {
                throw new InvalidOperationException(
                    "A handler returned a null event at position " + Text(i) + ". The event list is " +
                    "14 §2.4's animation script and 14 §7.1's economy log; a hole in it is a row " +
                    "neither can read.");
            }

            if (produced.Sequence != DomainEvent.UnstampedSequence)
            {
                throw new InvalidOperationException(
                    "A handler returned " + produced.GetType().Name + " already stamped with " +
                    "Sequence " + Text(produced.Sequence) + ". The ordinal is the event's position within " +
                    "ONE Apply call's list and is assigned HERE — never by a constructor and never " +
                    "by a caller (30 §7). A handler that stamps its own has decided a position in a " +
                    "list whose shape it does not know, and the economy log (14 §7.1) and the " +
                    "animation script (14 §2.4) would stop agreeing about the order of one command's " +
                    "effects. Build events with DomainEvent.UnstampedSequence.");
            }

            stamped[i] = produced with { Sequence = i + 1 };
        }

        return Array.AsReadOnly(stamped);
    }

    /// <summary>
    /// The answer for a command that is registered but whose <b>system</b> arrives in a later
    /// milestone.
    /// </summary>
    /// <remarks>
    /// See <c>CommandDispatch.Deferred</c> for why this is <c>ILLEGAL_STATE</c> and not a new
    /// `14` §16.2 value, and why every deferred row also carries a <c>GapRegister</c> entry.
    /// </remarks>
    private static HandlerResult Unimplemented(CommandRegistration registration)
    {
        _ = registration.DeferredTo;

        return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>Whether two stream-position maps carry exactly the same rows.</summary>
    private static bool SamePositions(
        IReadOnlyDictionary<string, ulong> left, IReadOnlyDictionary<string, ulong> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (streamName, position) in left)
        {
            if (!right.TryGetValue(streamName, out var other) || other != position)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 🔒 Renders a number with <see cref="CultureInfo.InvariantCulture"/>, for the reason
    /// <c>Player</c> and <c>Run</c> each have one: a bare interpolation renders <c>1.234</c> on a
    /// German laptop and <c>1,234</c> in the container — two diagnostics for one defect, and a
    /// message a reader cannot grep. `14` §8.2 wants <c>Core</c> reading identically everywhere, and
    /// <c>AmbientApiTests.Core_and_Application_contain_no_culture_sensitive_formatting</c> fails the
    /// build without it.
    /// </summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string RoundTripFailure(string aggregate, string error) =>
        "The " + aggregate + " in this WorldSlice does not round-trip through its own snapshot: " +
        error + " 30 §2.1's P4 makes Apply copy the slice before a handler touches it — through " +
        "ToSnapshot()/Rehydrate(), which is the one validated construction path 30 §11.3 sanctions " +
        "— so an aggregate that cannot be rebuilt from its own persisted shape is a rule that " +
        "mutated it into a state its invariants refuse, or a caller that built it by hand. Either " +
        "way it is a defect and not a player who asked for too much: the same state would fail on " +
        "the way into Postgres, one command later, with nothing left to say which rule wrote it.";
}
