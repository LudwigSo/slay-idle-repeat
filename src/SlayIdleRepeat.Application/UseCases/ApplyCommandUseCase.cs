using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.UseCases;

/// <summary>One command, addressed to one player and optionally to one run.</summary>
/// <param name="Player">Whose state the command is applied to.</param>
/// <param name="Run">
/// The run the command was addressed to, or <c>null</c> when it was addressed to the player. A
/// non-null value must name the run the player is actually in; the player's own run is still loaded
/// either way, because a player can act outside a run without leaving it.
/// </param>
/// <param name="Command">What the player intends.</param>
public sealed record ApplyCommandRequest(PlayerId Player, RunId? Run, GameCommand Command);

/// <summary>Everything one <see cref="ApplyCommandUseCase"/> call produces.</summary>
/// <remarks>
/// <para>
/// Shaped after the domain's own result and guarded the same way: <see cref="Accepted"/> is exactly
/// "there is no <see cref="Rejection"/>", and a refused outcome carries no events and no dispatch
/// failures, because a refused command changed nothing there could be anything to deliver about.
/// </para>
/// <para>
/// Unlike the domain's result, <see cref="Rejection"/> may carry a reason from either tier. This is
/// where a refusal decided before the domain was invoked lives, and it is the only place in this
/// layer that names one.
/// </para>
/// </remarks>
public sealed record ApplyCommandOutcome
{
    private static readonly IReadOnlyList<DomainEvent> NoEvents = Array.AsReadOnly(Array.Empty<DomainEvent>());

    private static readonly IReadOnlyList<EventDispatchFailure> NoFailures =
        Array.AsReadOnly(Array.Empty<EventDispatchFailure>());

    /// <summary>
    /// The only way to build one, so the two factories below are the only shapes that exist and the
    /// guards they promise cannot be walked around by a caller assembling their own.
    /// </summary>
    private ApplyCommandOutcome(
        RejectionReason? rejection,
        WorldSlice state,
        IReadOnlyList<DomainEvent> events,
        IReadOnlyList<EventDispatchFailure> dispatchFailures)
    {
        Rejection = rejection;
        State = state;
        Events = events;
        DispatchFailures = dispatchFailures;
    }

    /// <summary>Whether the command changed the state. Exactly the negation of "there is a rejection".</summary>
    public bool Accepted => Rejection is null;

    /// <summary>Why the command was refused, or <c>null</c> when it was accepted. Either tier.</summary>
    public RejectionReason? Rejection { get; }

    /// <summary>
    /// The state as it now stands: the applied state on an acceptance, the untouched loaded state on
    /// a refusal.
    /// </summary>
    public WorldSlice State { get; }

    /// <summary>The events the command produced, in order. Empty on a refusal.</summary>
    public IReadOnlyList<DomainEvent> Events { get; }

    /// <summary>The sinks that failed to receive the events. Empty on a refusal, and usually empty otherwise.</summary>
    public IReadOnlyList<EventDispatchFailure> DispatchFailures { get; }

    /// <summary>The outcome of a command that was applied, committed and dispatched.</summary>
    /// <param name="state">The state the domain produced.</param>
    /// <param name="events">The events it produced, already committed.</param>
    /// <param name="dispatchFailures">Whatever failed to receive them.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static ApplyCommandOutcome Accept(
        WorldSlice state,
        IReadOnlyList<DomainEvent> events,
        IReadOnlyList<EventDispatchFailure> dispatchFailures)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(dispatchFailures);

        return new ApplyCommandOutcome(rejection: null, state, events, dispatchFailures);
    }

    /// <summary>The outcome of a command that was refused, before or by the domain.</summary>
    /// <param name="rejection">Why. Either tier.</param>
    /// <param name="unchangedState">The state as it was loaded — nothing was written and nothing dispatched.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unchangedState"/> is null.</exception>
    public static ApplyCommandOutcome Reject(RejectionReason rejection, WorldSlice unchangedState)
    {
        ArgumentNullException.ThrowIfNull(unchangedState);

        return new ApplyCommandOutcome(rejection, unchangedState, NoEvents, NoFailures);
    }
}

/// <summary>What the domain decided about one command, before anything was written anywhere.</summary>
/// <remarks>
/// The half of <see cref="ApplyCommandOutcome"/> that exists before the command is committed, and
/// therefore the half a caller that owns its own commit boundary needs: a caller can render the
/// answer, build the commit from it, and only then let anything durable happen.
/// </remarks>
public sealed record ApplyCommandDecision
{
    private static readonly IReadOnlyList<DomainEvent> NoEvents = Array.AsReadOnly(Array.Empty<DomainEvent>());

    private ApplyCommandDecision(
        RejectionReason? rejection, WorldSlice state, IReadOnlyList<DomainEvent> events)
    {
        Rejection = rejection;
        State = state;
        Events = events;
    }

    /// <summary>Whether the domain accepted it. Exactly the negation of "there is a rejection".</summary>
    public bool Accepted => Rejection is null;

    /// <summary>Why it was refused, or <c>null</c>. Either tier.</summary>
    public RejectionReason? Rejection { get; }

    /// <summary>The state the domain produced, or the untouched loaded state on a refusal.</summary>
    public WorldSlice State { get; }

    /// <summary>The events it produced, in order. Empty on a refusal.</summary>
    public IReadOnlyList<DomainEvent> Events { get; }

    /// <summary>A decision to accept.</summary>
    /// <param name="state">The state the domain produced.</param>
    /// <param name="events">The events it produced.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static ApplyCommandDecision Accept(WorldSlice state, IReadOnlyList<DomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(events);

        return new ApplyCommandDecision(rejection: null, state, events);
    }

    /// <summary>A decision to refuse, from either tier.</summary>
    /// <param name="rejection">Why.</param>
    /// <param name="unchangedState">The state as it was loaded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unchangedState"/> is null.</exception>
    public static ApplyCommandDecision Reject(RejectionReason rejection, WorldSlice unchangedState)
    {
        ArgumentNullException.ThrowIfNull(unchangedState);

        return new ApplyCommandDecision(rejection, unchangedState, NoEvents);
    }
}

/// <summary>The write side: load the slice, apply the command, commit it, then deliver its events.</summary>
/// <remarks>
/// <para>
/// The four steps are this layer's entire job and it decides no game rule along the way. The one
/// decision it does make is an addressing one: a command addressed to a run the player is not in is
/// refused here with <see cref="RejectionReason.RUN_NOT_FOUND"/>, before the domain is invoked,
/// because that reason is a transport-tier one the domain is not allowed to return and the domain
/// would instead be handed the wrong run to act on.
/// </para>
/// <para>
/// The order is load, guard, apply, commit, dispatch, and the guards leave the method rather than
/// falling through: a refused command reaches neither the commit nor the delivery, which is what
/// makes "a refusal changes nothing and tells nobody" a property of the one code path instead of a
/// promise repeated at each step.
/// </para>
/// <para>
/// The ambient values a rule reads arrive as an argument. Building them here would put this layer in
/// charge of a seed whose presence depends on which kind of command arrived, which is a decision the
/// caller has already made and this seam cannot re-derive.
/// </para>
/// </remarks>
public sealed class ApplyCommandUseCase
{
    private readonly WorldSliceStore _store;
    private readonly DomainEventDispatcher _dispatcher;
    private readonly InboxCommandSupport? _inbox;

    /// <summary>Builds the use case over the store it commits through and the dispatcher it delivers through.</summary>
    /// <param name="store">Where state is loaded from and committed to.</param>
    /// <param name="dispatcher">Where an accepted command's events go.</param>
    /// <param name="inbox">
    /// The inbox seam, or <c>null</c> on a process with no message store. A null one loads no inbox,
    /// so the one command that reads it fails as the loading defect it is rather than telling a
    /// player their rewards are gone.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dispatcher"/> is null.</exception>
    public ApplyCommandUseCase(
        WorldSliceStore store, DomainEventDispatcher dispatcher, InboxCommandSupport? inbox = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _store = store;
        _dispatcher = dispatcher;
        _inbox = inbox;
    }

    /// <summary>Applies one command.</summary>
    /// <param name="request">Who, which run, and what.</param>
    /// <param name="context">Everything ambient the rules read, already resolved by the caller.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Whether it was accepted, the resulting state, its events, and any delivery failures.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The player has no stored state, or the stored state does not load. Neither is a player asking
    /// for something they cannot have.
    /// </exception>
    public async Task<ApplyCommandOutcome> ExecuteAsync(
        ApplyCommandRequest request, GameContext context, CancellationToken ct)
    {
        var decision = await DecideAsync(request, context, ct).ConfigureAwait(false);

        // Branches on whether there is a rejection, never on which one it is: routing a refusal is
        // this layer's job and deciding one is the domain's, and a switch here would be the second
        // place the same situation is answered.
        if (!decision.Accepted)
        {
            return ApplyCommandOutcome.Reject(decision.Rejection!.Value, decision.State);
        }

        await _store.SaveAsync(decision.State, ct).ConfigureAwait(false);

        var failures = await PublishAsync(request, decision, ct).ConfigureAwait(false);

        return ApplyCommandOutcome.Accept(decision.State, decision.Events, failures);
    }

    /// <summary>Decides one command: load, guard, apply. Writes nothing, anywhere.</summary>
    /// <param name="request">Who, which run, and what.</param>
    /// <param name="context">Everything ambient the rules read, already resolved by the caller.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Whether it was accepted, the resulting state, and its events.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">The player has no stored state, or the stored state does not load.</exception>
    /// <remarks>
    /// Split out so a caller that owns a transaction boundary can render the answer and build its
    /// commit before anything durable happens. <see cref="ExecuteAsync"/> is this plus the store's
    /// own save plus <see cref="PublishAsync"/>, unchanged.
    /// </remarks>
    public async Task<ApplyCommandDecision> DecideAsync(
        ApplyCommandRequest request, GameContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var slice = await _store.LoadAsync(request.Player, context.Content, ct).ConfigureAwait(false);

        if (request.Run is { } addressed && slice.Run?.Id != addressed)
        {
            return ApplyCommandDecision.Reject(RejectionReason.RUN_NOT_FOUND, slice);
        }

        if (_inbox is { } inbox && InboxCommandSupport.Reads(request.Command))
        {
            slice = slice with { Inbox = await inbox.LoadAsync(request.Player, ct).ConfigureAwait(false) };
        }

        var result = GameRules.Apply(slice, request.Command, context);

        return result.Accepted
            ? ApplyCommandDecision.Accept(result.NewState, result.Events)
            : ApplyCommandDecision.Reject(result.Rejection!.Value, result.NewState);
    }

    /// <summary>Delivers an accepted command's events to the sinks, after it has been committed.</summary>
    /// <param name="request">Who and what — the identity the domain's events do not carry.</param>
    /// <param name="decision">What was decided and already committed.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The sinks that failed to receive them. Empty for a refusal.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public async Task<IReadOnlyList<EventDispatchFailure>> PublishAsync(
        ApplyCommandRequest request, ApplyCommandDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        if (!decision.Accepted)
        {
            return Array.Empty<EventDispatchFailure>();
        }

        var batch = new DispatchedEvents(request.Player, request.Command, decision.State, decision.Events);

        return await _dispatcher.DispatchAsync(batch, ct).ConfigureAwait(false);
    }
}
