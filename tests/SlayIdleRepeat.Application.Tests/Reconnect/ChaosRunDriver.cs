using System.Globalization;
using System.Text.Json;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Application.Tests.Reconnect;

/// <summary>The four ways this suite kills a connection at one command boundary.</summary>
/// <remarks>
/// Public only because it names the cases of a theory, and a theory's parameters have to be at
/// least as reachable as the test method xunit calls. Everything else in this fixture is internal.
/// </remarks>
public enum ChaosFault
{
    /// <summary>Nothing is injected; the boundary is one ordinary exchange.</summary>
    None = 0,

    /// <summary>The connection dies before the commit: the command never happened, so the retry re-executes.</summary>
    DropBeforeCommit = 1,

    /// <summary>The connection dies after the commit: the command happened, so the retry must replay.</summary>
    DropAfterCommit = 2,

    /// <summary>After-commit drop, and then the retry's own reply is lost in flight — the retry rule applied twice.</summary>
    FaultOnTheRetryToo = 3,

    /// <summary>After-commit drop, and then the reconnect read rather than a blind retry.</summary>
    ReconnectAndResync = 4,
}

/// <summary>Which fault a run's client meets at each command boundary.</summary>
internal interface IFaultSchedule
{
    /// <summary>The fault to inject at <paramref name="boundaryIndex"/>, counted from 0.</summary>
    /// <param name="boundaryIndex">The boundary, <c>START_RUN</c> being 0.</param>
    ChaosFault At(int boundaryIndex);
}

/// <summary>The schedules the chaos cases drive.</summary>
internal static class FaultSchedules
{
    /// <summary>The reference run: no fault anywhere.</summary>
    internal static IFaultSchedule None { get; } = new NoFaults();

    /// <summary>
    /// A fault at EVERY boundary, the class rotating with <paramref name="pass"/> — four passes
    /// cover every boundary times every class at four runs' cost.
    /// </summary>
    /// <param name="pass">Which rotation this run is.</param>
    internal static IFaultSchedule Rotating(int pass) => new RotatingFaults(pass);

    /// <summary>One fault at one boundary and nothing anywhere else.</summary>
    /// <param name="boundaryIndex">The boundary to hit.</param>
    /// <param name="fault">The class to inject there.</param>
    internal static IFaultSchedule Only(int boundaryIndex, ChaosFault fault) =>
        new SingleFault(boundaryIndex, fault);

    private sealed class NoFaults : IFaultSchedule
    {
        public ChaosFault At(int boundaryIndex) => ChaosFault.None;
    }

    private sealed class RotatingFaults : IFaultSchedule
    {
        private const int Classes = 4;

        private readonly int _pass;

        internal RotatingFaults(int pass) => _pass = pass;

        public ChaosFault At(int boundaryIndex) =>
            (ChaosFault)(((boundaryIndex + _pass) % Classes) + 1);
    }

    private sealed class SingleFault : IFaultSchedule
    {
        private readonly int _boundary;
        private readonly ChaosFault _fault;

        internal SingleFault(int boundary, ChaosFault fault)
        {
            _boundary = boundary;
            _fault = fault;
        }

        public ChaosFault At(int boundaryIndex) =>
            boundaryIndex == _boundary ? _fault : ChaosFault.None;
    }
}

/// <summary>What happened at one command boundary of a chaos run.</summary>
/// <param name="Index">The boundary, counted from 0.</param>
/// <param name="WireName">The command's wire type.</param>
/// <param name="Sequence">The sequence it consumed in its scope.</param>
/// <param name="Fault">The class actually injected — see <see cref="ChaosRunDriver.Substitutions"/> when it differs from the schedule's.</param>
/// <param name="Attempts">How many attempts the client made before it held an answer.</param>
internal sealed record ChaosBoundary(int Index, string WireName, long Sequence, ChaosFault Fault, int Attempts);

/// <summary>A boundary where the schedule's class could not apply and another was used instead.</summary>
/// <param name="Boundary">Which boundary.</param>
/// <param name="Requested">What the schedule asked for.</param>
/// <param name="Applied">What was injected instead.</param>
/// <param name="Why">Why the requested class cannot apply there.</param>
internal sealed record FaultSubstitution(int Boundary, ChaosFault Requested, ChaosFault Applied, string Why);

/// <summary>
/// The simulated client, at the GATEWAY tier: it reads the run off the wire JSON it was answered,
/// picks the one command that is legal next, and sends it through <c>CommandGateway</c> — losing
/// the connection wherever its schedule says to.
/// </summary>
/// <remarks>
/// <para>
/// The decision tree is the domain-tier loop driver's, deliberately: the point is that a run driven
/// through envelopes, sequencing and idempotency reaches the same places a run driven straight
/// through the rules does. Everything it decides from is read off <c>outcome.run</c> — the client
/// sees the wire projection and nothing else, so a driver reaching into the aggregate would be
/// proving something no client can rely on.
/// </para>
/// <para>
/// 🔴 It owns the client's whole side of the conversation: the sequence per scope, the
/// <c>commandId</c> of every attempt, and the ordered list of bodies it ended up holding. A retry
/// reuses its boundary's <c>commandId</c> because that is what the idempotency store exists for —
/// a fresh one would be a different command carrying the same intent, which is precisely the
/// duplicate this whole task is about.
/// </para>
/// </remarks>
internal sealed class ChaosRunDriver
{
    /// <summary>Any well-formed battle log hash. The handler parses the shape and does not recompute it.</summary>
    private const string BattleLog = "1";

    /// <summary>The highest option index of a choice to try before giving the run up as stuck.</summary>
    private const int ChoiceLadder = 3;

    /// <summary>The most command boundaries one run may take, so a defect cannot hang the suite.</summary>
    /// <remarks>
    /// Sized for a board the run crosses end to end — the chapter's spine plus its boss, at up to
    /// five commands a node — and matched to the domain-tier driver's, so a run that gets further
    /// there than here is a wire-tier finding rather than a budget artefact.
    /// </remarks>
    internal const int CommandBudget = 600;

    /// <summary>The wire id of the one minigame carrying a pity counter, as the shipped tuning authors it.</summary>
    private const string ChestPick = "MG_CHEST_PICK";

    /// <summary>What the run projection carries when no tile is pending.</summary>
    private const int NoPendingTile = -1;

    /// <summary>The phase name a finished run carries on the wire.</summary>
    private const string EndedPhase = "Ended";

    /// <summary>The phase name a run with an open battle carries on the wire.</summary>
    private const string BattlePendingPhase = "BattlePending";

    private readonly ChaosWorld _world;
    private readonly IFaultSchedule _schedule;
    private readonly List<string> _log = [];
    private readonly List<string> _bodies = [];
    private readonly List<ChaosBoundary> _boundaries = [];
    private readonly List<FaultSubstitution> _substitutions = [];
    private readonly List<string> _recovered = [];
    private readonly HashSet<int> _resolved = [];
    private readonly HashSet<int> _seen = [];
    private readonly HashSet<int> _acknowledged = [];
    private readonly List<int> _visited = [];
    private readonly List<TileKind> _tiles = [];
    private readonly List<int> _stages = [];
    private readonly List<TileKind> _killed = [];

    private readonly int _commitsBefore;

    private RunView? _run;
    private RunId? _runId;
    private long _playerSequence;
    private long _runSequence = 1;
    private long _lastAnsweredRunSequence;
    private int _choice;
    private int _stage;

    private ChaosRunDriver(ChaosWorld world, IFaultSchedule schedule)
    {
        _world = world;
        _schedule = schedule;
        _playerSequence = world.NextPlayerSequence;
        _commitsBefore = world.UnitOfWork.CommitsReachingTheInner;
    }

    /// <summary>Every attempt made, with the answer it got — the trace a failure is diagnosed from.</summary>
    internal IReadOnlyList<string> Log => _log;

    /// <summary>The ordered response bodies the client ended up holding, one per boundary.</summary>
    internal IReadOnlyList<string> Bodies => _bodies;

    /// <summary>What happened at each boundary, in order.</summary>
    internal IReadOnlyList<ChaosBoundary> Boundaries => _boundaries;

    /// <summary>Every boundary where the schedule's class could not apply — never silent.</summary>
    internal IReadOnlyList<FaultSubstitution> Substitutions => _substitutions;

    /// <summary>Every outcome body the reconnect read handed back, in the order it handed them back.</summary>
    /// <remarks>
    /// ⚠️ Taken as the client receives it, escaping included. Measured on this branch, a recovered
    /// body is NOT byte-equal to the stored one: re-serialising the embedded envelope runs its
    /// string members through the JSON encoder, which escapes the plus sign of every UTC offset,
    /// where the original was written by the date writer and carries a literal one. Semantically the
    /// same envelope, different bytes. Kept unnormalised on purpose — smoothing it here would hide
    /// the difference from the very assertion that should be judging it.
    /// </remarks>
    internal IReadOnlyList<string> RecoveredOutcomes => _recovered;

    /// <summary>How many times the client took the reconnect path instead of a blind retry.</summary>
    internal int Resyncs { get; private set; }

    /// <summary>How many reconnect reads ordered a full resync — a state the schedules here never expect.</summary>
    internal int FullResyncsOrdered { get; private set; }

    /// <summary>The run this driver opened, or <c>null</c> if <c>START_RUN</c> never landed.</summary>
    internal RunId? Run => _runId;

    /// <summary>How many calls this driver made into the gateway, retries and lost attempts included.</summary>
    internal int GatewayCalls { get; private set; }

    /// <summary>
    /// How many gateway calls dispatched a command whose commit LANDED.
    /// </summary>
    /// <remarks>
    /// Read off the unit of work rather than off the replies, because that is the only honest
    /// witness: a replay is a read that returns stored bytes without touching the unit of work at
    /// all, so a commit that reached it is by definition a fresh execution.
    /// </remarks>
    internal int ExecutionsCommitted => _world.UnitOfWork.CommitsReachingTheInner - _commitsBefore;

    /// <summary>How many dispatched commands decided something and then lost their transaction.</summary>
    internal int ExecutionsLost => _world.UnitOfWork.FaultsBeforeCommit;

    /// <summary>
    /// How many gateway calls took the replay arm — every call that neither committed nor lost a
    /// transaction, which at this seam is exactly a duplicate answered from its stored record.
    /// </summary>
    internal int ReplaysServed => GatewayCalls - ExecutionsCommitted - ExecutionsLost;

    /// <summary>Whether the command budget ran out before the run ended.</summary>
    internal bool BudgetExhausted { get; private set; }

    /// <summary>How the run finished: the command that ended it, or why none could.</summary>
    internal string Ending { get; private set; } = "not started";

    /// <summary>The board positions the run stood on, in order, consecutive repeats collapsed.</summary>
    internal IReadOnlyList<int> Visited => _visited;

    /// <summary>The tile kinds the run resolved, one entry per distinct position, in order.</summary>
    internal IReadOnlyList<TileKind> Tiles => _tiles;

    /// <summary>The stage of every tile the run arrived at, in arrival order.</summary>
    internal IReadOnlyList<int> Stages => _stages;

    /// <summary>The tile kind the run could not leave, or <c>null</c> if it never met one.</summary>
    internal TileKind? StuckOn { get; private set; }

    /// <summary>The position <see cref="StuckOn"/> was concluded at, or <c>null</c>.</summary>
    internal int? StuckAt { get; private set; }

    /// <summary>Where a <c>ROLL_DICE</c> was accepted without moving the run and without opening a fork.</summary>
    internal int? StalledAt { get; private set; }

    /// <summary>How many <c>CONFIRM_BATTLE_RESULT</c>s were accepted.</summary>
    internal int BattlesFought { get; private set; }

    /// <summary>How many of those were won.</summary>
    internal int BattlesWon { get; private set; }

    /// <summary>The kind of tile behind every battle this run won, in order.</summary>
    internal IReadOnlyList<TileKind> KillsWon => _killed;

    /// <summary>How many drafts the run opened and this player skipped.</summary>
    internal int DraftsSkipped { get; private set; }

    /// <summary>How many stage boundaries this run crossed.</summary>
    internal int StageGatesCrossed { get; private set; }

    /// <summary>Plays one whole run through the gateway, losing the connection where the schedule says.</summary>
    /// <param name="world">The geared world.</param>
    /// <param name="schedule">Which fault each boundary meets.</param>
    /// <param name="budget">The most boundaries this run may take.</param>
    internal static async Task<ChaosRunDriver> PlayAsync(
        ChaosWorld world, IFaultSchedule schedule, int budget = CommandBudget)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(schedule);

        var driver = new ChaosRunDriver(world, schedule);
        await driver.DriveAsync(budget);

        return driver;
    }

    private async Task DriveAsync(int budget)
    {
        await BoundaryAsync(new StartRunCommand(ChaosWorld.Chapter, ChaosWorld.Tier), playerScope: true);

        if (_runId is null)
        {
            Ending = "START_RUN did not open a run: " + (_bodies.Count > 0 ? _bodies[^1] : "no body at all");

            return;
        }

        var dying = false;

        for (var issued = 1; issued < budget; issued++)
        {
            if (_run is not { } run || string.Equals(run.Phase, EndedPhase, StringComparison.Ordinal))
            {
                return;
            }

            if (run.Position >= 0 && (_visited.Count == 0 || _visited[^1] != run.Position))
            {
                _visited.Add(run.Position);
            }

            dying |= StalledAt is not null;

            if (Step(run, dying) is not { } next)
            {
                return;
            }

            var before = run.Position;
            var hadTile = run.HasPendingTile;
            var tileBefore = hadTile ? run.PendingTileLinearIndex : (int?)null;
            var accepted = await BoundaryAsync(next, playerScope: false);

            if (!accepted)
            {
                if (next is EventChooseCommand or CampfireChooseCommand or ChooseForkCommand
                        or ShrineChooseCommand &&
                    _choice < ChoiceLadder)
                {
                    _choice++;

                    continue;
                }

                Ending = "stuck: " + next.GetType().Name + " refused " + LastRejection();
                await AbandonAsync();

                return;
            }

            _choice = 0;

            switch (next)
            {
                case ConfirmBattleResultCommand confirmed:
                    BattlesFought++;

                    if (confirmed.Won)
                    {
                        BattlesWon++;

                        // Off the run as it stood BEFORE the command: the confirmation clears the
                        // pending tile, so the kind is gone by the time the answer comes back.
                        _killed.Add((TileKind)run.PendingTileKind);
                    }

                    break;

                case SkipDraftCommand:
                    DraftsSkipped++;
                    break;

                default:
                    break;
            }

            var after = _run;

            if (hadTile && after?.HasPendingTile == false && before >= 0)
            {
                _resolved.Add(before);
            }

            if (after?.HasPendingTile == true && after.PendingTileLinearIndex != tileBefore)
            {
                var stage = after.PendingTileStage;

                if (stage > _stage && _stage > 0)
                {
                    StageGatesCrossed++;
                }

                _stage = stage;
                _stages.Add(stage);
            }

            if (next is ResolveTileCommand && after?.HasPendingTile == true && before >= 0)
            {
                _acknowledged.Add(before);
            }

            if (next is RollDiceCommand && StalledAt is null && after?.Position == before &&
                !after.HasPendingFork && before >= 0)
            {
                StalledAt = before;
            }
        }

        BudgetExhausted = true;
        Ending = "budget of " + Text(budget) + " boundaries exhausted";
    }

    /// <summary>The one command that is legal next, or <c>null</c> when the run cannot go on.</summary>
    private GameCommand? Step(RunView run, bool dying)
    {
        if (string.Equals(run.Phase, BattlePendingPhase, StringComparison.Ordinal))
        {
            return new ConfirmBattleResultCommand(BattleLog, !dying);
        }

        if (run.DraftPending)
        {
            return new SkipDraftCommand();
        }

        if (run.HasPendingFork)
        {
            return new ChooseForkCommand(_choice);
        }

        if (run.BossDefeated)
        {
            Ending = VictoryEnding;

            return new EndRunCommand();
        }

        if (run.CurrentHp == 0)
        {
            Ending = "END_RUN after a death";

            return new EndRunCommand();
        }

        if (run.HasPendingTile)
        {
            return Resolve(run, dying);
        }

        if (dying)
        {
            Ending = "ABANDON_RUN, no battle left to lose";

            return new AbandonRunCommand();
        }

        return new RollDiceCommand();
    }

    /// <summary>The driver's own wording for a run that ended by beating the boss.</summary>
    internal const string VictoryEnding = "END_RUN after a victory";

    private GameCommand Resolve(RunView run, bool dying)
    {
        var kind = (TileKind)run.PendingTileKind;

        if (_resolved.Contains(run.Position) && !dying)
        {
            return new RollDiceCommand();
        }

        if (_seen.Add(run.Position))
        {
            _tiles.Add(kind);
        }

        var next = kind switch
        {
            TileKind.Enemy or TileKind.Elite or TileKind.MiniBoss or TileKind.Boss =>
                (GameCommand)new StartBattleCommand(),
            TileKind.Minigame when !run.HasResolvedMinigameAt(run.Position) =>
                new MinigameSubmitCommand(ChestPick, 0),
            TileKind.Campfire => new CampfireChooseCommand(_choice),
            TileKind.Event when run.PendingEventCardId.Length > 0 => new EventChooseCommand(_choice),
            TileKind.Shrine => new ShrineChooseCommand(_choice),
            TileKind.Shop when run.HasOpenShop => new ShopLeaveCommand(),
            TileKind.Minigame => new RollDiceCommand(),
            _ => new ResolveTileCommand(),
        };

        // 🔴 About to re-send the one command already tried here, which was accepted and left the
        // tile pending. The domain-tier driver takes a ROLL_DICE probe here as evidence; this one
        // does not, because a probe is a command BOUNDARY the fault schedule never indexed and
        // injecting nothing at it would be the silent truncation the schedule exists to rule out.
        // The evidence is the accepted RESOLVE_TILE itself, which the boundary log carries.
        if (next is ResolveTileCommand && _acknowledged.Contains(run.Position))
        {
            StuckOn = kind;
            StuckAt = run.Position;
            Ending = "ABANDON_RUN, stuck on " + kind + " at " + Text(run.Position) +
                " — RESOLVE_TILE was accepted and left it pending";

            return new AbandonRunCommand();
        }

        return next;
    }

    private async Task AbandonAsync()
    {
        if (!await BoundaryAsync(new AbandonRunCommand(), playerScope: false))
        {
            Ending = "unendable: " + Ending + ", and ABANDON_RUN was refused " + LastRejection();
        }
    }

    /// <summary>
    /// One command boundary: the envelope, the fault its schedule says to meet there, every attempt
    /// the client makes, and the one body it ends up holding.
    /// </summary>
    /// <returns>Whether the answer the client ended up holding is an acceptance.</returns>
    private async Task<bool> BoundaryAsync(GameCommand command, bool playerScope)
    {
        var index = _boundaries.Count;
        var sequence = playerScope ? _playerSequence : _runSequence;
        var commandId = "cid-" + Text(index);
        var envelope = ChaosWorld.EnvelopeFor(command, sequence, commandId);
        var wireName = WireCommandCodec.WireNameOf(command);
        var applied = Applicable(_schedule.At(index), index, playerScope);

        var (body, attempts) = await AttemptAsync(envelope, playerScope, applied, sequence);

        _bodies.Add(body);
        _boundaries.Add(new ChaosBoundary(index, wireName, sequence, applied, attempts));

        if (playerScope)
        {
            _playerSequence++;
        }
        else
        {
            _runSequence++;
            _lastAnsweredRunSequence = sequence;
        }

        var root = JsonDocument.Parse(body).RootElement;
        var rejected = root.TryGetProperty("rejected", out var flag) && flag.GetBoolean();

        _log.Add(
            Text(index) + " " + wireName + " @" + Text(sequence) + " " + applied +
            " x" + Text(attempts) + " -> " +
            (rejected ? "refused " + root.GetProperty("reason").GetString() : "accepted"));

        if (rejected)
        {
            return false;
        }

        var outcome = root.GetProperty("outcome");

        if (_runId is null && outcome.TryGetProperty("runId", out var minted))
        {
            _runId = new RunId(minted.GetString()
                ?? throw new InvalidOperationException("START_RUN answered a null runId."));
        }

        _run = outcome.TryGetProperty("run", out var run) ? new RunView(run.Clone()) : null;

        return true;
    }

    /// <summary>
    /// The class actually injected. The reconnect read is keyed on a run id, so a client that lost
    /// <c>START_RUN</c>'s response has no way to ask for it and its only recourse is the retry —
    /// recorded as a substitution rather than skipped, so a case can assert the cell was reached.
    /// </summary>
    private ChaosFault Applicable(ChaosFault requested, int index, bool playerScope)
    {
        if (requested != ChaosFault.ReconnectAndResync || (!playerScope && _runId is not null))
        {
            return requested;
        }

        _substitutions.Add(new FaultSubstitution(
            index,
            requested,
            ChaosFault.DropAfterCommit,
            "The reconnect read is addressed by run id, which a client that never saw START_RUN's " +
            "response does not hold. Its recourse is the retry with the same commandId, which works " +
            "because that record is PLAYER-scoped and its stored body carries the run id."));

        return ChaosFault.DropAfterCommit;
    }

    private async Task<(string Body, int Attempts)> AttemptAsync(
        string envelope, bool playerScope, ChaosFault fault, long sequence)
    {
        switch (fault)
        {
            case ChaosFault.None:
                return (await SendAsync(envelope, playerScope), 1);

            case ChaosFault.DropBeforeCommit:
                _world.UnitOfWork.ArmBeforeCommit();
                await LoseAsync(envelope, playerScope);

                return (await SendAsync(envelope, playerScope), 2);

            case ChaosFault.DropAfterCommit:
                _world.UnitOfWork.ArmAfterCommit();
                await LoseAsync(envelope, playerScope);

                return (await SendAsync(envelope, playerScope), 2);

            case ChaosFault.FaultOnTheRetryToo:
                _world.UnitOfWork.ArmAfterCommit();
                await LoseAsync(envelope, playerScope);

                // The retry's call COMPLETED — it took the replay arm and answered. The connection
                // died with the bytes in flight, so the client holds nothing and asks a third time.
                _ = await SendAsync(envelope, playerScope);

                return (await SendAsync(envelope, playerScope), 3);

            case ChaosFault.ReconnectAndResync:
                _world.UnitOfWork.ArmAfterCommit();
                await LoseAsync(envelope, playerScope);

                return (await ResyncAsync(sequence), 2);

            default:
                throw new InvalidOperationException("No fault class is named " + fault + ".");
        }
    }

    /// <summary>The reconnect path: ask what the run is now, and take the outcomes the client missed.</summary>
    private async Task<string> ResyncAsync(long sequence)
    {
        Resyncs++;

        var reply = await _world.RunStateQuery.ReadAsync(
            _world.Player, _runId!.Value, _lastAnsweredRunSequence, Worlds.Cancel);

        if (reply.StatusCode != 200)
        {
            throw new InvalidOperationException(
                "The reconnect read for sequence " + Text(sequence) + " answered HTTP " +
                reply.StatusCode + ", so the client has no way back into its own run.");
        }

        var root = JsonDocument.Parse(reply.Body).RootElement;

        if (root.TryGetProperty("resyncFull", out var full) && full.GetBoolean())
        {
            FullResyncsOrdered++;
        }

        var missed = root.GetProperty("missedOutcomes");
        string? last = null;

        foreach (var outcome in missed.EnumerateArray())
        {
            var body = outcome.GetRawText();
            _recovered.Add(body);
            last = body;
        }

        return last ?? throw new InvalidOperationException(
            "The reconnect read after losing sequence " + Text(sequence) + " handed back no missed " +
            "outcome at all, so the commit the fault fired AFTER is not in the ledger — which is the " +
            "atomicity guarantee this whole suite probes.");
    }

    private async Task LoseAsync(string envelope, bool playerScope)
    {
        GatewayReply? answered;

        try
        {
            answered = await SubmitAsync(envelope, playerScope);
        }
        catch (ConnectionLost)
        {
            return;
        }

        throw new InvalidOperationException(
            "The armed connection fault never fired: the gateway answered HTTP " +
            answered.StatusCode + " with " + answered.Body + ". Every claim below would then be " +
            "about a run that met no disconnection at all.");
    }

    private async Task<string> SendAsync(string envelope, bool playerScope)
    {
        var reply = await SubmitAsync(envelope, playerScope);

        return reply.StatusCode == 200
            ? reply.Body
            : throw new InvalidOperationException(
                "The gateway answered HTTP " + Text(reply.StatusCode) + " to " + envelope +
                ", and only 200 carries a contract body a client can act on.");
    }

    private Task<GatewayReply> SubmitAsync(string envelope, bool playerScope)
    {
        GatewayCalls++;

        return playerScope
            ? _world.Gateway.SubmitPlayerCommandAsync(_world.Player, envelope, Worlds.Cancel)
            : _world.Gateway.SubmitRunCommandAsync(_world.Player, _runId!.Value, envelope, Worlds.Cancel);
    }

    private string LastRejection()
    {
        var root = JsonDocument.Parse(_bodies[^1]).RootElement;

        return root.TryGetProperty("reason", out var reason)
            ? reason.GetString() ?? "an unnamed reason"
            : "no reason at all";
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The run as the CLIENT sees it: the wire projection and nothing else.
    /// </summary>
    /// <remarks>
    /// Every member here is read out of the JSON rather than out of a rehydrated aggregate, so a
    /// field the projection stops carrying breaks this driver instead of being quietly supplied from
    /// somewhere no client can reach.
    /// </remarks>
    private sealed class RunView
    {
        private readonly JsonElement _run;

        internal RunView(JsonElement run) => _run = run;

        internal int Position => _run.GetProperty("position").GetInt32();

        internal int CurrentHp => _run.GetProperty("currentHp").GetInt32();

        internal string Phase => _run.GetProperty("phase").GetString() ?? string.Empty;

        internal bool DraftPending => _run.GetProperty("draftPending").GetBoolean();

        internal bool BossDefeated => _run.GetProperty("bossDefeated").GetBoolean();

        internal int PendingTileKind => _run.GetProperty("pendingTileKind").GetInt32();

        internal int PendingTileLinearIndex => _run.GetProperty("pendingTileLinearIndex").GetInt32();

        internal int PendingTileStage => _run.GetProperty("pendingTileStage").GetInt32();

        internal bool HasPendingTile => PendingTileKind != NoPendingTile;

        /// <summary>A null member is OMITTED from the wire, so presence is what a fork being open looks like.</summary>
        internal bool HasPendingFork => _run.TryGetProperty("pendingForkJunctionPosition", out _);

        /// <inheritdoc cref="HasPendingFork"/>
        internal bool HasOpenShop => _run.TryGetProperty("shopOfferDraw", out _);

        internal string PendingEventCardId =>
            _run.TryGetProperty("pendingEventCardId", out var id) ? id.GetString() ?? string.Empty : string.Empty;

        internal bool HasResolvedMinigameAt(int position) =>
            _run.TryGetProperty("resolvedMinigames", out var resolved) &&
            resolved.TryGetProperty(position.ToString(CultureInfo.InvariantCulture), out _);
    }
}
