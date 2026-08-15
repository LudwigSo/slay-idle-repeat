using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 Everything one command handler may touch, assembled by <c>GameRules.Apply</c> — and the reason
/// three of `30` §2.1's five properties are structural rather than remembered.
/// </summary>
/// <remarks>
/// <para>
/// <b>P4 (immutable).</b> <see cref="State"/> is a <b>clone</b> of the slice <c>Apply</c> was handed,
/// rebuilt through each aggregate's <c>ToSnapshot()</c>/<c>Rehydrate()</c> pair. A handler mutates
/// the clone freely; the caller's slice is untouched whatever the handler does, and a <b>rejected</b>
/// command returns the caller's own slice because the clone is simply discarded.
/// </para>
/// <para>
/// <b>The <c>AdvanceTime</c> seam.</b> `30` §2.3 makes <c>AdvanceTime(state, context.NowUtc)</c>
/// <em>"the first step of every command handler"</em>. It is first <b>by construction</b> here and
/// not by convention: a handler never receives the raw slice, only this one, and <c>Apply</c> runs
/// the catch-up before it builds it. Nothing a handler can write runs before it.
/// </para>
/// <para>
/// ⚠️ <b>M1-08 filled the catch-up in, and what a handler is handed therefore changed.</b>
/// <see cref="Player"/>'s Energy banks and accrual anchor are already rolled forward to
/// <c>Context.NowUtc</c>, and its daily and weekly counters are already cleared for any boundary
/// crossed since the last command — so <c>Player.DailyPeriodStartUtc</c> is the game day this
/// command is <em>in</em>, not the one it was last seen in. That matters most to M1-09's
/// <c>BEGIN_SESSION</c>: catch-up <b>clears the counters</b>, so a handler cannot key "first
/// <c>BEGIN_SESSION</c> of the day" off a daily counter it did not itself set, after the catch-up,
/// in the same command. <c>DailyPeriodStartUtc</c> is the one surviving "which game day is this"
/// fact to compare a stored claim marker against.
/// </para>
/// <para>
/// <b>The RNG choke point.</b> <see cref="Rng"/> is the only door to a run's `14` §8.1 streams, and
/// <c>Apply</c> — not the handler — folds its final positions into the <c>Run</c>. See
/// <see cref="RunRngScope"/>.
/// </para>
/// <para>
/// ⚠️ It is a class rather than a record: it is a bag of references handed one way down a call, and
/// value equality over an aggregate mid-mutation would mean nothing.
/// </para>
/// </remarks>
internal sealed class HandlerInput
{
    private readonly RunRngScope? _rng;

    /// <summary>
    /// The meta-draw scope, built on first use. See <see cref="MetaDraws"/> for why it is lazy.
    /// </summary>
    private MetaDrawScope? _metaDraws;

    /// <summary>The run <see cref="OpenRun"/> attached, or <c>null</c> until it is called.</summary>
    private Run? _openedRun;

    internal HandlerInput(WorldSlice state, GameContext context, RunRngScope? rng)
    {
        State = state;
        Context = context;
        _rng = rng;
    }

    /// <summary>
    /// 🔒 The slice this command may write: a clone of <c>Apply</c>'s argument, already advanced to
    /// <c>Context.NowUtc</c>. Mutate it; do not rebuild it.
    /// </summary>
    internal WorldSlice State { get; }

    /// <summary>Everything ambient, as data (`30` §3).</summary>
    internal GameContext Context { get; }

    /// <summary>The player. Always present — `30` §4 makes <c>Run</c> a child of <c>Player</c>.</summary>
    internal Player Player => State.Player;

    /// <summary>
    /// 🔒 M3-15's narrow door: the run <see cref="OpenRun"/> attached, for
    /// <see cref="GameRules.Execute"/> to fold into the slice it returns — or <c>null</c> on every
    /// command but <c>START_RUN</c>, which never calls it.
    /// </summary>
    internal Run? OpenedRun => _openedRun;

    /// <summary>
    /// 🔒 The <b>one</b> seam that lets a handler attach the <c>Run</c> it just created onto this
    /// command's result. Exists solely for <c>START_RUN</c> (M3-15).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WorldSlice.Run"/> is <c>init</c>-only — `30` §4.1 makes the slice a pair of
    /// references a handler mutates the <em>aggregates</em> of, never the slice's own shape — so a
    /// handler cannot write <see cref="State"/>'s <c>Run</c> directly, and <c>State</c> itself has no
    /// setter for the same reason. Every one of the other 18 <c>CommandKind.Run</c> rows never needs
    /// this: <see cref="GameRules.Execute"/> refuses them a run-less slice before a handler is ever
    /// called (`30` §4.1's loading defect), so their <c>Run</c> already exists by the time they run.
    /// <c>START_RUN</c> is the one row whose whole job is to create the <c>Run</c> that guard would
    /// otherwise demand, and its dispatch row is the one row marked
    /// <see cref="CommandRegistration.OpensRun"/> — see <c>Execute</c>, which is the only reader of
    /// <see cref="OpenedRun"/> and folds it into the slice it returns exactly where every other run
    /// command's <c>RunRngScope</c> is folded back.
    /// </para>
    /// <para>
    /// ⚠️ <b>Refused twice over</b>, and both refusals are defects rather than rejections: a handler
    /// that reaches this seam with a slice that already carries a <c>Run</c>, or that calls it twice,
    /// has miswired the one command that may call it at all. <c>StartRun.Handle</c> rejects an
    /// <em>already-active</em> run itself, as a domain-tier <c>RejectionReason</c>, before ever
    /// reaching here — the player's request is illegal, not the caller's wiring — so a defect surfacing
    /// here means the handler skipped that check.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// This command's slice already carries a <c>Run</c>, or this seam has already been called once.
    /// </exception>
    internal void OpenRun(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (State.Run is not null)
        {
            throw new InvalidOperationException(
                "HandlerInput.OpenRun was called on a command whose slice ALREADY carries a Run. " +
                "This seam exists for START_RUN (M3-15) to attach the Run it just created onto a " +
                "run-less slice; a slice that already has one means either a handler other than " +
                "START_RUN's reached this seam, or START_RUN's own already-active-run check " +
                "(a RejectionReason, not this exception) was skipped.");
        }

        if (_openedRun is not null)
        {
            throw new InvalidOperationException(
                "HandlerInput.OpenRun was called twice by the same handler. A command opens at most " +
                "one Run; calling this seam a second time would silently discard the first Run it " +
                "attached, which is the same shape as a handler hand-writing an RNG counter — a " +
                "determinism defect Apply's fold exists to make unreachable, not to paper over.");
        }

        _openedRun = run;
    }

    /// <summary>
    /// The run this command acts inside.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This is a <c>CommandKind.Meta</c> command, so the slice carries no run to act on.
    /// </exception>
    internal Run Run => State.Run ?? throw new InvalidOperationException(
        "This command's slice carries no Run. A CommandKind.Run command is never dispatched without " +
        "one — Apply refuses that as a loading defect (30 §4.1) — so reaching this means a " +
        "CommandKind.Meta command tried to act on a run. If the command genuinely acts inside a run, " +
        "its dispatch row is classified wrongly; 14 §2.3 splits the registry 19 run / 30 meta.");

    /// <summary>
    /// 🔒 The run's `14` §8.1 draw streams. Every in-run draw comes from here and nowhere else.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This is a <c>CommandKind.Meta</c> command, which draws from <c>GameContext.CommandSeed</c>
    /// instead.
    /// </exception>
    internal RunRngScope Rng => _rng ?? throw new InvalidOperationException(
        "This command has no RunRngScope. 14 §8.1 and 30 §3 run TWO regimes and this is the meta one: " +
        "an out-of-run draw is Hash64(GameContext.CommandSeed, stream, i) from i = 0 with NO " +
        "persisted counter, because the command is atomic and idempotency replays its stored outcome. " +
        "A scope here would give it a counter the design says it must not have. Draw through " +
        "HandlerInput.MetaDraws. If the command draws from the run's streams, its dispatch row is " +
        "classified CommandKind.Meta and should not be.");

    /// <summary>
    /// 🔒 The <b>other</b> regime — `14` §8.1's out-of-run draws, over this command's server-issued
    /// <c>CommandSeed</c>. The only door to it, and the mirror of <see cref="Rng"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Built lazily, and the laziness is the design rather than an optimisation.</b> The scope
    /// exists exactly when the command draws, so constructing it eagerly in the constructor would
    /// mean either building one for all forty-nine commands (including the nineteen run rows that
    /// `30` §3 forbids a seed) or moving the "does this command draw" decision into <c>Apply</c> —
    /// which is the dispatch table's business, not the input bag's. Asking for it <em>is</em> the
    /// declaration that this command draws, and the throw below is what makes a ⚄ row whose host
    /// forgot the seed loud instead of silently unseeded.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is a defect, not a rejection</b> — the same line <c>Apply</c> draws for a
    /// <c>CommandKind.Run</c> command whose slice carries no run. A `14` §2.3 ⚄ row arriving without
    /// a seed is a miswired composition root, and answering the player a `14` §16.2 refusal would
    /// tell them a rule said no while leaving the host broken. <c>CommandSeedPinTests</c> pins the
    /// pairing over all forty-nine rows; this is the same invariant where it is actually consumed.
    /// </para>
    /// <para>
    /// 🔒 <b>One scope per command, reused.</b> The field is cached so two reads inside one handler
    /// answer the <em>same</em> scope: a fresh one per read would restart every stream at draw 0, and
    /// a handler that drew twice would get the same value twice with nothing to show for it.
    /// </para>
    /// <para>
    /// 🔒 <b>A <c>CommandKind.Run</c> command is refused outright, and that guard is the mirror of
    /// <see cref="Rng"/>'s.</b> The two regimes are exclusive: a run command's draws are
    /// <c>Hash64(runSeed, s, i)</c> off the run's <b>persisted</b> counters, which <c>Apply</c> folds
    /// back, and `30` §3 forbids it a <c>CommandSeed</c> at all. A run handler that reached this
    /// property would open streams at index 0 with <em>no persisted counter</em> — and nothing else
    /// would notice: <c>GameRules.FoldRngPositions</c> compares positions to catch a
    /// <em>hand-written</em> one and would see none moved, and
    /// <c>DeterministicRng_is_constructed_only_inside_Core_Rng</c> is satisfied because the
    /// construction is inside <c>Core/Rng/</c>. The run would then replay differently for the rest of
    /// its life, exactly as if the handler had opened its own stream. ⚠️ Found by M1-09's
    /// architecture review: this door did not exist before <c>Core/Handlers/</c> had an occupant, and
    /// <c>CommandSeedPinTests</c> pins the <em>host</em> side of the pairing only — it says nothing
    /// about a consumer.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This is a <c>CommandKind.Run</c> command, or its <c>GameContext</c> carries no
    /// <c>CommandSeed</c>.
    /// </exception>
    internal MetaDrawScope MetaDraws =>
        _metaDraws ??= new MetaDrawScope(RequireMetaRegime() ?? throw new InvalidOperationException(
            "This command's GameContext carries no CommandSeed, so it cannot draw. 14 §8.1 and 30 §3 " +
            "run TWO regimes and this is the META one: an out-of-run draw is " +
            "Hash64(GameContext.CommandSeed, stream, i) from i = 0 with no persisted counter, and the " +
            "seed is generated by the SERVER HOST and never by the domain — the invariant behind both " +
            "regimes is that the domain never invents entropy. 14 §2.3 marks exactly nine meta rows ⚄ " +
            "(BEGIN_SESSION, REROLL_QUEST, SPIN_WHEEL, REFORGE_ITEM, RETUNE_ITEM, OPEN_CHEST, " +
            "OPEN_EGG, OPEN_CRATE, START_DUEL) and every one of them must be handed a seed; note 0 is " +
            "a legitimate seed and not an absence. This is a MISWIRED COMPOSITION ROOT, not a player " +
            "asking for something they cannot have: fix the host that built the context. If the " +
            "command genuinely draws nothing, it should not be reading this."));

    /// <summary>
    /// 🔒 The <c>CommandSeed</c>, having established that this command is in the <b>meta</b> regime
    /// at all — or <c>null</c> when there is no seed, which the caller turns into its own refusal.
    /// </summary>
    /// <remarks>
    /// The two failures carry distinct messages on purpose (steering <b>S2</b>): "a run command
    /// reached for the meta regime" and "a meta draw was handed no seed" are opposite defects with
    /// opposite fixes — one is a handler in the wrong regime, the other a host that forgot the seed —
    /// and a single message would send whoever hit it to the wrong file.
    /// </remarks>
    private ulong? RequireMetaRegime() =>
        _rng is null
            ? Context.CommandSeed
            : throw new InvalidOperationException(
                "This is a CommandKind.Run command and it may not draw from GameContext.CommandSeed. " +
                "14 §8.1 runs TWO regimes and they are EXCLUSIVE: a run draw is " +
                "Hash64(runSeed, stream, i) off the Run aggregate's committed seed and its PERSISTED " +
                "per-stream counters, which Apply folds back so the counter always equals the number " +
                "of draws taken. The meta regime has no counter at all — so drawing here would " +
                "consume indices nothing records, Apply would fold the RunRngScope's unchanged " +
                "positions back, and the run would replay differently for the rest of its life with " +
                "nothing going red. 30 §3 additionally gives a run command NO CommandSeed, so this " +
                "would draw from a seed the host is forbidden to send. Draw through HandlerInput.Rng. " +
                "If the command genuinely acts outside a run, its dispatch row is classified " +
                "CommandKind.Run and should not be.");
}

/// <summary>
/// What a handler returns: the events it produced, or the domain-tier reason it refused.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>It carries no <c>WorldSlice</c>, deliberately.</b> A handler mutates the clone it was
/// handed (<see cref="HandlerInput.State"/>) and <c>Apply</c> returns that. A handler that could
/// return a slice of its own could return an aggregate that never went through the clone, never went
/// through the RNG fold, and never had its `14` §16.3 timestamp advanced — three invariants that are
/// <c>Apply</c>'s to hold, silently bypassed by a return value.
/// </para>
/// <para>
/// ⚠️ <b>Events arrive <em>unstamped</em>.</b> <c>DomainEvent.Sequence</c> is the event's ordinal
/// within one <c>CommandResult</c>'s list, and only <c>Apply</c> knows the list. A handler builds
/// its events with <c>DomainEvent.UnstampedSequence</c> and <c>Apply</c> stamps them; an event that
/// arrives already stamped is refused, because a producer that assigned its own ordinal has made the
/// economy log's order (`14` §7.1) and the animation script's (`14` §2.4) two different things.
/// </para>
/// </remarks>
internal readonly record struct HandlerResult
{
    private static readonly ReadOnlyCollection<DomainEvent> NoEvents =
        Array.AsReadOnly(Array.Empty<DomainEvent>());

    private readonly IReadOnlyList<DomainEvent>? _events;

    private HandlerResult(RejectionReason? rejection, IReadOnlyList<DomainEvent> events)
    {
        Rejection = rejection;
        _events = events;
    }

    /// <summary>The domain-tier reason the command was refused, or <c>null</c> when it was accepted.</summary>
    internal RejectionReason? Rejection { get; }

    /// <summary>The unstamped events the handler produced. Empty on a rejection.</summary>
    /// <exception cref="InvalidOperationException">This is <c>default(HandlerResult)</c>.</exception>
    internal IReadOnlyList<DomainEvent> Events => _events ?? throw new InvalidOperationException(
        "This is default(HandlerResult), which no handler can legitimately return: it carries no " +
        "rejection and no event list, so Apply cannot tell an accepted command from a refused one. " +
        "Return HandlerResult.Accept(...) or HandlerResult.Reject(...).");

    /// <summary>Whether the handler accepted the command.</summary>
    internal bool Accepted => Rejection is null;

    /// <summary>The command was accepted and produced no events.</summary>
    internal static HandlerResult Accept() => new(rejection: null, NoEvents);

    /// <summary>The command was accepted and produced these events, in order, unstamped.</summary>
    /// <param name="events">The events. Never null; may be empty.</param>
    internal static HandlerResult Accept(params DomainEvent[] events) =>
        new(rejection: null, Array.AsReadOnly(events ?? throw new ArgumentNullException(nameof(events))));

    /// <inheritdoc cref="Accept(DomainEvent[])"/>
    internal static HandlerResult Accept(IReadOnlyList<DomainEvent> events) =>
        new(rejection: null, events ?? throw new ArgumentNullException(nameof(events)));

    /// <summary>
    /// 🔒 The command was refused. <paramref name="rejection"/> must be a domain-tier value of
    /// `14` §16.2 — <c>CommandResult</c> refuses a transport-tier one outright (`30` §2).
    /// </summary>
    internal static HandlerResult Reject(RejectionReason rejection) => new(rejection, NoEvents);
}
