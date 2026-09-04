using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A hand-written <see cref="IGameHost"/> for the screens that read state and submit commands,
/// recording what it was asked rather than only what it answered.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StubGameHost"/> answers the one call the application root makes and refuses the other
/// two by name, which is what its own remarks ask a presenter needing them to do: bring its own
/// fake rather than widen that one.
/// </para>
/// <para>
/// 🔒 It records the <em>arguments</em> because two of the facts these screens have to get right are
/// invisible in the answer. A read addressed to a named run can answer <c>NoSuchRun</c> where a read
/// addressed to the player alone returns whatever run they are in — same return type, different
/// question — and a <c>START_RUN</c> submitted against a run id is submitted against a run that does
/// not exist yet. Both look identical from the outside unless the call itself is observed.
/// </para>
/// </remarks>
internal sealed class RecordingGameHost : IGameHost
{
    private readonly OwnStateResult? _read;
    private readonly Exception? _readFailure;

    private OwnStateResult? _laterRead;

    private RejectionReason? _submitRejection;
    private Exception? _submitFailure;
    private int _submitFailuresLeft;
    private RunSnapshot? _acceptedRun;
    private RunSnapshot? _laterAcceptedRun;
    private IReadOnlyList<DomainEvent> _acceptedEvents = [];
    private readonly List<GameCommand> _submitted = [];
    private TaskCompletionSource? _submitGate;

    private RecordingGameHost(OwnStateResult? read, Exception? readFailure)
    {
        _read = read;
        _readFailure = readFailure;
    }

    /// <summary>How many times the state read was called.</summary>
    internal int ReadCallCount { get; private set; }

    /// <summary>The player the last read was addressed to.</summary>
    internal PlayerId? ReadPlayer { get; private set; }

    /// <summary>The run the last read named, or null when it named none.</summary>
    internal RunId? ReadRun { get; private set; }

    /// <summary>The token the last read was handed.</summary>
    internal CancellationToken? ReadToken { get; private set; }

    /// <summary>How many commands were submitted.</summary>
    internal int SubmitCallCount { get; private set; }

    /// <summary>The player the last submission was addressed to.</summary>
    internal PlayerId? SubmitPlayer { get; private set; }

    /// <summary>The run the last submission named, or null when it named none.</summary>
    internal RunId? SubmitRun { get; private set; }

    /// <summary>The last command submitted, or null while none has been.</summary>
    internal GameCommand? SubmitCommand { get; private set; }

    /// <summary>Every command submitted, in the order they were.</summary>
    /// <remarks>
    /// 🔒 Kept alongside <see cref="SubmitCommand"/> rather than instead of it. One screen can
    /// submit more than one command off a single opening — <c>EventPresenter</c> draws the card
    /// itself and then spends it — and "the last command was EVENT_CHOOSE" cannot tell that apart
    /// from a screen that skipped the draw altogether, which is the version the rules layer refuses.
    /// It is also the only thing that can see a draw submitted TWICE, since a screen that drew twice
    /// settles on exactly the same card.
    /// </remarks>
    internal IReadOnlyList<GameCommand> SubmittedCommands => _submitted;

    /// <summary>The token the last submission was handed.</summary>
    internal CancellationToken? SubmitToken { get; private set; }

    /// <summary>A host whose state read answers with the given result.</summary>
    internal static RecordingGameHost Reading(OwnStateResult result) => new(result, readFailure: null);

    /// <summary>A host whose state read answers <c>Found</c> with the given rows.</summary>
    internal static RecordingGameHost Finding(PlayerSnapshot player, RunSnapshot? run = null) =>
        Reading(new OwnStateResult(OwnStateLookup.Found, new OwnStateView(player, run)));

    /// <summary>A host whose state read finds nothing stored for the player.</summary>
    internal static RecordingGameHost FindingNoSuchPlayer() =>
        Reading(new OwnStateResult(OwnStateLookup.NoSuchPlayer, View: null));

    /// <summary>A host whose state read returns a faulted task — how a real async host fails.</summary>
    internal static RecordingGameHost FaultingItsRead(Exception failure) => new(read: null, failure);

    /// <summary>
    /// Makes the second and every later read answer with a different state from the first.
    /// </summary>
    /// <remarks>
    /// 🔒 A moving store, which is the only fixture a re-read can be proved against: a host that
    /// answered the same thing twice would satisfy a screen that re-read and a screen that never
    /// did. Left unset, every read answers alike and nothing about the existing cases changes.
    /// </remarks>
    /// <param name="player">The row the store holds by the time it is asked again.</param>
    /// <param name="run">Whatever run that row carries by then.</param>
    internal RecordingGameHost ThenFinding(PlayerSnapshot player, RunSnapshot? run = null)
    {
        _laterRead = new OwnStateResult(OwnStateLookup.Found, new OwnStateView(player, run));

        return this;
    }

    /// <summary>
    /// Makes every command this host is handed come back refused, carrying the given reason.
    /// </summary>
    /// <remarks>
    /// A refusal is an ANSWER here, not a thrown failure: <c>ApplyCommandUseCase</c> refuses a
    /// command that reached it by returning an outcome, and a fake that threw would exercise the
    /// caller's catch instead of the branch that reads the outcome. The read is configured
    /// separately, because a refused submission needs a profile that was found first.
    /// </remarks>
    /// <param name="rejection">Why. Whichever tier's reason the case is about.</param>
    internal RecordingGameHost RefusingCommands(RejectionReason rejection)
    {
        _submitRejection = rejection;

        return this;
    }

    /// <summary>
    /// Makes every command this host is handed come back as a faulted task — how a real host fails
    /// when the call itself does not complete, as opposed to completing with a refusal.
    /// </summary>
    /// <remarks>
    /// Told apart from <see cref="RefusingCommands"/> on purpose: a refusal is an answer the caller
    /// reads, a fault is an exception the caller catches, and a screen can handle one and drop the
    /// other. The read is configured separately, because a submission only happens after a profile
    /// was found.
    /// </remarks>
    /// <param name="failure">What the submission fails with.</param>
    /// <param name="times">
    /// How many submissions fault before the host starts answering normally. Every one by default.
    /// <para>
    /// A finite count is what lets a case ask the question a permanently faulting host cannot: a
    /// screen that latched "the host did not answer" and never cleared it prints that sentence under
    /// the next command's real answer, and nothing about a host that faults forever can tell the two
    /// apart.
    /// </para>
    /// </param>
    internal RecordingGameHost FaultingItsCommands(Exception failure, int times = int.MaxValue)
    {
        _submitFailure = failure;
        _submitFailuresLeft = times;

        return this;
    }

    /// <summary>
    /// Makes an accepted command hand back a run in the given state — which is how a real host
    /// answers, and the only way a screen that redraws from the outcome can be exercised at all.
    /// </summary>
    /// <remarks>
    /// 🔒 Rehydrated through the aggregate rather than smuggled in as a row, because that is the
    /// only construction path the domain has and it validates: a fixture that bypassed it could
    /// hand a screen a run the game could never produce, and the screen would then be proven
    /// against a state that does not exist.
    /// </remarks>
    /// <param name="run">The row the command's outcome carries back.</param>
    internal RecordingGameHost AcceptingInto(RunSnapshot run)
    {
        _acceptedRun = run;

        return this;
    }

    /// <summary>
    /// Makes the second and every later accepted command hand back a different run from the first.
    /// </summary>
    /// <remarks>
    /// 🔒 A moving store on the SUBMIT side, and <see cref="ThenFinding"/>'s exact counterpart: a
    /// screen that submits two commands in sequence — <c>EventPresenter</c>'s draw and then its
    /// choice — decides the second from the run the first came back with, and a host that answered
    /// both alike would satisfy a screen that read the moved run and a screen that never did. Left
    /// unset, every acceptance answers alike and nothing about the existing cases changes.
    /// </remarks>
    /// <param name="run">The row the second and later commands hand back.</param>
    internal RecordingGameHost ThenAcceptingInto(RunSnapshot run)
    {
        _laterAcceptedRun = run;

        return this;
    }

    /// <summary>
    /// Leaves every submission genuinely in flight until <see cref="ReleaseSubmissions"/> is called.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The only fixture a double-submit latch can be proved against.</b> Left unset this host
    /// answers synchronously, so a first call runs to completion before it returns and the second
    /// press finds the screen already settled — refused by whatever stage guard the screen has,
    /// whether the latch was taken before the await or after it, or not taken at all. Measured: with
    /// <c>CampfirePresenter</c>'s latch deliberately moved to after its await, the suite's own
    /// double-tap case still passed. A paused submission is what makes the second press arrive while
    /// the first is really outstanding, which is the state the latch exists for.
    /// <para>
    /// 🔒 A case that pauses MUST release <b>before</b> it awaits anything, or a screen that failed
    /// to latch hangs the suite instead of failing it: start both calls, release, then await. The
    /// second press has already been made and turned away by then, so nothing is lost by releasing
    /// early — and a case that waits first has no failure to report, only a runner that stopped.
    /// </para>
    /// </remarks>
    internal RecordingGameHost PausingItsCommands()
    {
        _submitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return this;
    }

    /// <summary>Lets every paused submission answer.</summary>
    internal void ReleaseSubmissions() => _submitGate?.TrySetResult();

    /// <summary>Makes an accepted command hand back the given events, in order.</summary>
    /// <remarks>
    /// The events are how a rolled face reaches a screen at all — no persisted field carries one —
    /// so a case about what the board shows after a roll is a case about this list.
    /// </remarks>
    /// <param name="events">What the command produced.</param>
    internal RecordingGameHost Emitting(params DomainEvent[] events)
    {
        _acceptedEvents = events;

        return this;
    }

    /// <inheritdoc/>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct) =>
        throw new NotSupportedException(
            "These screens run after boot has already opened the profile and are handed its id. A " +
            "presenter reopening it has made the cold start pay for the same read twice.");

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct)
    {
        ReadCallCount++;
        ReadPlayer = player;
        ReadRun = run;
        ReadToken = ct;

        if (_readFailure is { } failure)
        {
            return Task.FromException<OwnStateResult>(failure);
        }

        return Task.FromResult(ReadCallCount > 1 && _laterRead is { } later ? later : _read!);
    }

    /// <inheritdoc/>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player,
        RunId? run,
        GameCommand command,
        CancellationToken ct)
    {
        SubmitCallCount++;
        SubmitPlayer = player;
        SubmitRun = run;
        SubmitCommand = command;
        SubmitToken = ct;
        _submitted.Add(command);

        // Recorded synchronously whatever the gate says: what a screen submitted is a fact about the
        // call, and a case that pauses one still has to be able to assert on it while it is paused.
        // The ordinal travels with the answer rather than being re-read, so a paused submission still
        // gets the row its own position in the sequence earns.
        var ordinal = SubmitCallCount;

        return _submitGate is { } gate
            ? AnsweringWhenReleased(gate, player, ordinal)
            : Answer(player, ordinal);
    }

    private async Task<ApplyCommandOutcome> AnsweringWhenReleased(
        TaskCompletionSource gate, PlayerId player, int ordinal)
    {
        await gate.Task.ConfigureAwait(false);

        return await Answer(player, ordinal).ConfigureAwait(false);
    }

    private Task<ApplyCommandOutcome> Answer(PlayerId player, int ordinal)
    {
        if (_submitFailure is { } failure && _submitFailuresLeft > 0)
        {
            _submitFailuresLeft--;

            return Task.FromException<ApplyCommandOutcome>(failure);
        }

        var unchanged = PlayerState.EmptySlice(player);

        if (_submitRejection is { } rejection)
        {
            return Task.FromResult(ApplyCommandOutcome.Reject(rejection, unchanged));
        }

        var accepted = ordinal > 1 && _laterAcceptedRun is { } later ? later : _acceptedRun;

        return Task.FromResult(
            ApplyCommandOutcome.Accept(PlayerState.SliceWith(unchanged, accepted), _acceptedEvents, []));
    }
}
