using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core;

/// <summary>Everything one command handler may touch, assembled by <c>GameRules.Apply</c>.</summary>
/// <remarks>
/// <para>
/// <see cref="State"/> is a clone of the slice <c>Apply</c> was handed, already rolled forward
/// through <c>AdvanceTime</c> before the handler ever sees it — a handler never receives the raw
/// slice. Its energy banks, anchor, and daily/weekly counters already reflect the catch-up, so e.g.
/// <c>Player.DailyPeriodStartUtc</c> is the game day this command is in, not the one it was last
/// seen in — a handler cannot key off a daily counter it hasn't itself reset in the same command.
/// </para>
/// <para><see cref="Rng"/> is the only door to a run's draw streams; <c>Apply</c>, not the handler, folds its final positions back.</para>
/// <para>A class rather than a record: it's a bag of references handed one way down a call, so value equality would mean nothing.</para>
/// </remarks>
internal sealed class HandlerInput
{
    private readonly RunRngScope? _rng;

    private MetaDrawScope? _metaDraws;

    /// <summary>The run <see cref="OpenRun"/> attached, or <c>null</c> until it is called.</summary>
    private Run? _openedRun;

    internal HandlerInput(WorldSlice state, GameContext context, RunRngScope? rng)
    {
        State = state;
        Context = context;
        _rng = rng;
    }

    /// <summary>The slice this command may write: a clone of <c>Apply</c>'s argument, already advanced to <c>Context.NowUtc</c>. Mutate it; do not rebuild it.</summary>
    internal WorldSlice State { get; }

    /// <summary>Everything ambient, as data.</summary>
    internal GameContext Context { get; }

    /// <summary>The player. Always present.</summary>
    internal Player Player => State.Player;

    /// <summary>The run <see cref="OpenRun"/> attached, for <c>Execute</c> to fold into its result — or <c>null</c> on every command but <c>START_RUN</c>.</summary>
    internal Run? OpenedRun => _openedRun;

    /// <summary>
    /// The one seam that lets a handler attach the <c>Run</c> it just created onto this command's
    /// result. Exists solely for <c>START_RUN</c>, since <see cref="WorldSlice.Run"/> is
    /// <c>init</c>-only and cannot be set directly.
    /// </summary>
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

    /// <summary>The run this command acts inside.</summary>
    /// <exception cref="InvalidOperationException">
    /// This is a <c>CommandKind.Meta</c> command, so the slice carries no run to act on.
    /// </exception>
    internal Run Run => State.Run ?? throw new InvalidOperationException(
        "This command's slice carries no Run. A CommandKind.Run command is never dispatched without " +
        "one — Apply refuses that as a loading defect (30 §4.1) — so reaching this means a " +
        "CommandKind.Meta command tried to act on a run. If the command genuinely acts inside a run, " +
        "its dispatch row is classified wrongly; 14 §2.3 splits the registry 19 run / 30 meta.");

    /// <summary>The run's draw streams. Every in-run draw comes from here and nowhere else.</summary>
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

    /// <summary>The out-of-run draw scope, over this command's server-issued <c>CommandSeed</c>. The mirror of <see cref="Rng"/>.</summary>
    /// <remarks>
    /// Built lazily rather than eagerly, since asking for it is itself the declaration that this
    /// command draws — building one unconditionally would mean building it for run commands too,
    /// which must never carry a seed. Cached, so two reads in one handler answer the same scope
    /// rather than each restarting every stream at draw 0. Refused for a <c>CommandKind.Run</c>
    /// command: a run handler drawing here would open streams with no persisted counter, and the
    /// run would silently replay differently for the rest of its life.
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
    /// The <c>CommandSeed</c>, having established this command is in the meta regime at all — or
    /// <c>null</c> when there is no seed, which the caller turns into its own refusal.
    /// </summary>
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

/// <summary>What a handler returns: the events it produced, or the domain-tier reason it refused.</summary>
/// <remarks>
/// Carries no <c>WorldSlice</c> deliberately: a handler mutates the clone it was handed
/// (<see cref="HandlerInput.State"/>) and <c>Apply</c> returns that, so a returned slice of its own
/// could bypass the clone, the RNG fold and the timestamp advance. Events arrive unstamped —
/// <c>Apply</c> alone assigns <c>DomainEvent.Sequence</c>, and an already-stamped event is refused.
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

    /// <summary>The command was refused. <paramref name="rejection"/> must be a domain-tier value.</summary>
    internal static HandlerResult Reject(RejectionReason rejection) => new(rejection, NoEvents);
}
