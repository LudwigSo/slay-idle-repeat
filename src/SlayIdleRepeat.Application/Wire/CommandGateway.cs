using System.Globalization;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>What the transport writes back: the HTTP status and the exact body.</summary>
/// <param name="StatusCode">The GATEWAY emits only <c>200</c> (every in-protocol answer) and <c>400</c> (a body that is not an envelope); the endpoint handler in front of it reuses this shape for the refusals that never reach the gateway — 401/403 from the principal seam, 404 for an unroutable segment. The remaining statuses in 14 §16.2's table belong to infrastructure and the host's own fault handling.</param>
/// <param name="Body">The response body. Empty on every non-200, which carry no contract shape.</param>
public sealed record GatewayReply(int StatusCode, string Body);

/// <summary>
/// The command endpoints' whole pipeline behind the HTTP surface: parse, version, decode, route,
/// throttle, kill-switch, sequence, dispatch, respond, record — 14 §2.3 and §16.1–16.3, in one
/// testable seam with no ASP.NET in sight.
/// </summary>
/// <remarks>
/// <para>
/// The order of the checks is part of the contract. Everything up to the ledger is stateless and
/// consumes no sequence number — a client refused for protocol version, an unknown or malformed
/// command, mis-routing, throttle or a kill switch retries the same envelope after fixing its end.
/// From the ledger on, every dispatched command consumes its sequence and is recorded, acceptances
/// and refusals alike: a 200-rejection is a decided, recorded answer (14 §16.2), and recording it
/// is what lets a duplicate of it replay instead of re-deciding.
/// </para>
/// <para>
/// The scope is the endpoint's: everything on <c>POST /player/command</c> — the meta commands and
/// <c>START_RUN</c> — sequences on the player's lifetime counter; everything on
/// <c>POST /run/{runId}/command</c> sequences on that run, whose counter the accepted
/// <c>START_RUN</c> opens at 0 so the first run command is the expected <c>last + 1 = 1</c>.
/// One PLAYER's commands are processed one at a time across both scopes (the striped gate below),
/// because both scopes write the same stored player row; different players never wait on each
/// other except by stripe collision.
/// </para>
/// <para>
/// This is also where a command's ambient values are issued, because the endpoint owns the
/// <c>GameContext</c>: the instant from <c>IClockPort</c> unchanged, a <c>CommandSeed</c> for
/// exactly the commands <c>GameRules.RequiresCommandSeed</c> names, and — the wire half of the run
/// allocator — an <c>AllocatedRunId</c> from <c>IIdGeneratorPort</c> for exactly the command
/// <c>GameRules.OpensRun</c> names.
/// </para>
/// <para>
/// ⚠️ <c>CONTENT_VERSION_MISMATCH</c> is the one transport-tier reason with no arm here: the
/// envelope carries no content hash and no per-run/session content pinning exists yet — both are
/// M5-09's, and the arm lands beside the version check when the pin does.
/// </para>
/// </remarks>
public sealed class CommandGateway
{
    /// <summary>What a minted run identity is spelled with — the sibling of the host's <c>PLAYER_</c> prefix.</summary>
    private const string RunIdPrefix = "RUN_";

    /// <summary>How many gates the striped pool holds. Fixed, so the pool's memory is too.</summary>
    private const int GateStripes = 256;

    private readonly ApplyCommandUseCase _apply;
    private readonly IClockPort _clock;
    private readonly IIdGeneratorPort _ids;
    private readonly ContentSnapshot _content;
    private readonly Entitlements _entitlements;
    private readonly Func<FeatureFlags> _currentFlags;
    private readonly ICommandLedgerStore _ledger;
    private readonly ICommandThrottle _throttle;

    /// <summary>
    /// The striped gate pool, keyed by PLAYER — not by sequencing scope. A player's run and player
    /// scopes both load-modify-save the same stored player row, so two scopes running concurrently
    /// for one player would let the last save silently erase the other command's whole effect while
    /// the ledger records both as accepted. One gate per player serialises the row; two players
    /// never wait on each other except by stripe collision, which only serialises and never skews.
    /// Fixed-size, so a stream of invented identities cannot grow it.
    /// </summary>
    private readonly SemaphoreSlim[] _playerGates =
        Enumerable.Range(0, GateStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>Composes the gateway over everything it needs, every piece stated by the caller.</summary>
    /// <param name="apply">The write side every dispatched command goes through.</param>
    /// <param name="clock">The instant every command is applied at.</param>
    /// <param name="ids">The source of run identities and per-command seeds.</param>
    /// <param name="content">The loaded, validated content set every command reads.</param>
    /// <param name="entitlements">The subscription entitlement the composition root resolved.</param>
    /// <param name="currentFlags">The kill switches' live source; the composition root's reloading config swaps what it answers. Read exactly once per submitted command, so the gate and the <c>GameContext</c> always see the same snapshot.</param>
    /// <param name="ledger">Where sequencing state and idempotency records live.</param>
    /// <param name="throttle">The per-player application-level limit.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public CommandGateway(
        ApplyCommandUseCase apply,
        IClockPort clock,
        IIdGeneratorPort ids,
        ContentSnapshot content,
        Entitlements entitlements,
        Func<FeatureFlags> currentFlags,
        ICommandLedgerStore ledger,
        ICommandThrottle throttle)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(currentFlags);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(throttle);

        _apply = apply;
        _clock = clock;
        _ids = ids;
        _content = content;
        _entitlements = entitlements;
        _currentFlags = currentFlags;
        _ledger = ledger;
        _throttle = throttle;
    }

    /// <summary><c>POST /run/{runId}/command</c> — a run command, sequenced on that run.</summary>
    /// <param name="player">The authenticated player.</param>
    /// <param name="run">The run named in the route.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public Task<GatewayReply> SubmitRunCommandAsync(PlayerId player, RunId run, string body, CancellationToken ct) =>
        SubmitAsync(player, run, body, ct);

    /// <summary><c>POST /player/command</c> — a meta command or <c>START_RUN</c>, sequenced on the player's lifetime counter.</summary>
    /// <param name="player">The authenticated player.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public Task<GatewayReply> SubmitPlayerCommandAsync(PlayerId player, string body, CancellationToken ct) =>
        SubmitAsync(player, routedRun: null, body, ct);

    private async Task<GatewayReply> SubmitAsync(
        PlayerId player, RunId? routedRun, string body, CancellationToken ct)
    {
        var parse = EnvelopeParser.Parse(body);
        if (parse.Envelope is not { } envelope)
        {
            // 14 §16.2's 400: the body is not a parseable envelope at all. No contract body — the
            // client bug is reported through telemetry, not negotiated on the wire.
            return new GatewayReply(400, string.Empty);
        }

        // 14 §16.1: {N, N−1} and nothing else — the wire enforcement of one version of skew.
        if (envelope.ProtocolVersion != WireProtocol.PROTOCOL_VERSION &&
            envelope.ProtocolVersion != WireProtocol.PROTOCOL_VERSION - 1)
        {
            return Rejection(envelope, RejectionReason.PROTOCOL_VERSION_UNSUPPORTED);
        }

        var decode = WireCommandCodec.Decode(envelope);
        if (decode.Command is not { } command)
        {
            return Rejection(envelope, decode.Rejection!.Value);
        }

        // 14 §2.3's endpoint split, answered by the dispatch table's two public doors: a command
        // belongs on the player endpoint exactly when it is meta or it opens a run. A command on
        // the wrong endpoint fails the endpoint's schema — the registry lists it elsewhere.
        var belongsOnPlayerEndpoint = GameRules.RequiresCommandSeed(command) || GameRules.OpensRun(command);
        if (belongsOnPlayerEndpoint != routedRun is null)
        {
            return Rejection(envelope, RejectionReason.MALFORMED_COMMAND);
        }

        if (_throttle.ShouldReject(player))
        {
            return Rejection(envelope, RejectionReason.RATE_LIMITED);
        }

        // The ONE flags read of this submit: the gate and the GameContext below must judge from
        // the same snapshot, or a reload between them would half-apply a kill switch.
        var flags = _currentFlags()
            ?? throw new InvalidOperationException(
                "The currentFlags source answered null; the composition root must always resolve a FeatureFlags value.");
        if (FeatureGate.IsDisabled(command, flags))
        {
            return Rejection(envelope, RejectionReason.FEATURE_DISABLED);
        }

        var scope = routedRun is { } addressed
            ? RunScope(player, addressed)
            : CommandScopes.ForPlayer(player);

        // The run-scope existence check runs BEFORE the gate: an invented run id must cost nothing
        // held and nothing kept. It is only a fast path — the same check runs again under the gate,
        // where it is authoritative — and it cannot mis-answer a legitimate command, because the
        // scope is opened before the START_RUN reply that first names the run id is returned.
        if (routedRun is not null &&
            await _ledger.ReadLastSequenceAsync(scope, ct).ConfigureAwait(false) is null)
        {
            return Rejection(envelope, RejectionReason.RUN_NOT_FOUND);
        }

        var gate = _playerGates[(StringComparer.Ordinal.GetHashCode(player.Value) & int.MaxValue) % GateStripes];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SubmitSequencedAsync(player, routedRun, scope, envelope, command, flags, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The half that runs under the scope's gate: the 14 §16.3 rules, the dispatch, and the record.</summary>
    private async Task<GatewayReply> SubmitSequencedAsync(
        PlayerId player,
        RunId? routedRun,
        string scope,
        CommandEnvelope envelope,
        GameCommand command,
        FeatureFlags flags,
        CancellationToken ct)
    {
        var last = await _ledger.ReadLastSequenceAsync(scope, ct).ConfigureAwait(false);

        // A run scope exists only once its START_RUN opened it, so an unknown one answers what it
        // is: no run state exists for this runId — never an HTTP 404. The player scope is the
        // opposite: the lifetime counter starts at 0 the moment the player does.
        if (last is null && routedRun is not null)
        {
            return Rejection(envelope, RejectionReason.RUN_NOT_FOUND);
        }

        var stored = await _ledger.ReadRecordAsync(scope, envelope.CommandId, ct).ConfigureAwait(false);
        if (stored is not null)
        {
            // A duplicate is the SAME exchange — same id, same sequence, same intent — and replays
            // the stored bytes. A known id carrying anything else is a client whose model of the
            // conversation is wrong, told so rather than half-obeyed.
            if (stored.Sequence != envelope.Sequence || !stored.Command.Equals(command))
            {
                return Rejection(envelope, RejectionReason.IDEMPOTENCY_CONFLICT);
            }

            // The replay repairs the record-then-open seam: appending the acceptance and opening
            // the new run's scope are two store calls, and a durable backing may fail between them.
            // Without this, a START_RUN whose open never landed would replay a runId every command
            // answers RUN_NOT_FOUND to, forever. OpenScopeAsync is idempotent, so the common case
            // — the scope already open — re-opens nothing.
            if (stored.OpensRunScope is { } openedScope)
            {
                await _ledger.OpenScopeAsync(openedScope, ct).ConfigureAwait(false);
            }

            return new GatewayReply(200, stored.ResponseBody);
        }

        var expected = (last ?? 0) + 1;
        if (envelope.Sequence < expected)
        {
            return Rejection(envelope, RejectionReason.SEQUENCE_STALE);
        }

        if (envelope.Sequence > expected)
        {
            return Rejection(envelope, RejectionReason.SEQUENCE_GAP);
        }

        var opensRun = GameRules.OpensRun(command);

        var context = new GameContext(
            _clock.UtcNow,
            GameRules.RequiresCommandSeed(command) ? CommandSeedSource.Fresh(_ids) : null,
            _content,
            _entitlements,
            flags,
            opensRun ? MintRunId() : null);

        var outcome = await _apply
            .ExecuteAsync(new ApplyCommandRequest(player, routedRun, command), context, ct)
            .ConfigureAwait(false);

        var runScoped = routedRun is not null || opensRun;
        var response = Respond(envelope, outcome, runScoped);
        var responseBody = WireJson.Render(response);

        var opensRunScope = outcome.Accepted && opensRun && outcome.State.Run is { } opened
            ? RunScope(player, opened.Id)
            : null;

        await _ledger
            .AppendAsync(
                scope,
                new LedgerRecord(envelope.CommandId, envelope.Sequence, command, responseBody, opensRunScope),
                ct)
            .ConfigureAwait(false);

        // The other half of "the run's sequence starts at 1": the new run's own scope opens at 0,
        // after the record above so a replayed START_RUN can repair a missing open — the record
        // carries the scope key for exactly that.
        if (opensRunScope is not null)
        {
            await _ledger.OpenScopeAsync(opensRunScope, ct).ConfigureAwait(false);
        }

        return new GatewayReply(200, responseBody);
    }

    /// <summary>
    /// A run's sequencing scope, qualified by its OWNER: a run is a child of its player, so a
    /// foreign player naming another's run id builds a scope no START_RUN ever opened and answers
    /// <c>RUN_NOT_FOUND</c> before any state is read — instead of consuming the owner's next
    /// sequence with a dispatched rejection recorded into the owner's own idempotency space.
    /// </summary>
    private static string RunScope(PlayerId player, RunId run) =>
        CommandScopes.ForRun(player, run);

    /// <summary>Builds the response envelope for a dispatched command — accepted or domain-refused.</summary>
    /// <remarks>
    /// The hash follows the endpoint split (see <see cref="WireProjections"/>): a run-scoped
    /// command hashes player-then-run, a meta command the player alone — with the one seam that a
    /// run-scoped outcome whose state carries no run (a refused <c>START_RUN</c> on a run-less
    /// player) hashes the player alone, there being no run snapshot on either end to hash.
    /// </remarks>
    private static CommandResponse Respond(
        CommandEnvelope envelope, ApplyCommandOutcome outcome, bool runScoped)
    {
        var player = outcome.State.Player.ToSnapshot();
        var run = runScoped ? outcome.State.Run?.ToSnapshot() : null;

        var stateHash = run is null
            ? WireProjections.HashPlayerAlone(player)
            : WireProjections.HashPlayerAndRun(player, run);

        if (outcome.Rejection is { } rejection)
        {
            return CommandResponse.RejectedWith(envelope.Sequence, rejection, stateHash);
        }

        // The outcome's run-side fields ride run-scoped commands only: a meta command may read the
        // run but cannot write it, so echoing it there would be a second copy of unchanged state.
        var acceptedOutcome = new AcceptedCommandOutcome(
            RunId: run?.Id,
            Run: run is null ? null : WireProjections.Of(run),
            RngStreamStates: run?.RngStreamPositions,
            BattleSeed: run is not null && RunBattle.HasOpenBattle(run) ? BattleSeedWireForm(run) : null,
            Events: outcome.Events);

        return CommandResponse.Accepted(
            envelope.Sequence, acceptedOutcome, WireProjections.Of(player), stateHash);
    }

    /// <summary>14 §2.3's <c>battleSeed</c> spelling: <c>0x</c> + 16 lowercase hex characters.</summary>
    private static string BattleSeedWireForm(RunSnapshot run) =>
        "0x" + RunBattle.SeedOf(run).ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>A pre-dispatch rejection: no state was read, so it carries no state hash.</summary>
    private static GatewayReply Rejection(CommandEnvelope envelope, RejectionReason reason) =>
        new(200, WireJson.Render(CommandResponse.RejectedWith(envelope.Sequence, reason, stateHash: null)));

    /// <summary>The wire half of the run allocator: a fresh, opaque run identity from the sanctioned generator.</summary>
    private RunId MintRunId() => new(RunIdPrefix + _ids.NewGuid().ToString("N"));
}
