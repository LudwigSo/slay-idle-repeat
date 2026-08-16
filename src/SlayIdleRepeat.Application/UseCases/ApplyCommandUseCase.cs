using SlayIdleRepeat.Application.Services.Events;
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
    /// <summary>Whether the command changed the state. Exactly the negation of "there is a rejection".</summary>
    public bool Accepted => throw new NotImplementedException();

    /// <summary>Why the command was refused, or <c>null</c> when it was accepted. Either tier.</summary>
    public RejectionReason? Rejection => throw new NotImplementedException();

    /// <summary>
    /// The state as it now stands: the applied state on an acceptance, the untouched loaded state on
    /// a refusal.
    /// </summary>
    public WorldSlice State => throw new NotImplementedException();

    /// <summary>The events the command produced, in order. Empty on a refusal.</summary>
    public IReadOnlyList<DomainEvent> Events => throw new NotImplementedException();

    /// <summary>The sinks that failed to receive the events. Empty on a refusal, and usually empty otherwise.</summary>
    public IReadOnlyList<EventDispatchFailure> DispatchFailures => throw new NotImplementedException();

    /// <summary>The outcome of a command that was applied, committed and dispatched.</summary>
    /// <param name="state">The state the domain produced.</param>
    /// <param name="events">The events it produced, already committed.</param>
    /// <param name="dispatchFailures">Whatever failed to receive them.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static ApplyCommandOutcome Accept(
        WorldSlice state,
        IReadOnlyList<DomainEvent> events,
        IReadOnlyList<EventDispatchFailure> dispatchFailures) => throw new NotImplementedException();

    /// <summary>The outcome of a command that was refused, before or by the domain.</summary>
    /// <param name="rejection">Why. Either tier.</param>
    /// <param name="unchangedState">The state as it was loaded — nothing was written and nothing dispatched.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unchangedState"/> is null.</exception>
    public static ApplyCommandOutcome Reject(RejectionReason rejection, WorldSlice unchangedState) =>
        throw new NotImplementedException();
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
    /// <summary>Builds the use case over the store it commits through and the dispatcher it delivers through.</summary>
    /// <param name="store">Where state is loaded from and committed to.</param>
    /// <param name="dispatcher">Where an accepted command's events go.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ApplyCommandUseCase(WorldSliceStore store, DomainEventDispatcher dispatcher) =>
        throw new NotImplementedException();

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
    public Task<ApplyCommandOutcome> ExecuteAsync(
        ApplyCommandRequest request, GameContext context, CancellationToken ct) =>
        throw new NotImplementedException();
}
