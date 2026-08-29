using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Hosting;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Tests.Wire;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>One step of one sequence, as both hosts answered it.</summary>
/// <param name="Step">The step's ordinal within its sequence.</param>
/// <param name="Command">The command's wire name.</param>
/// <param name="Meta">Whether it travelled the player endpoint rather than the run endpoint.</param>
/// <param name="Accepted">Whether the domain accepted it.</param>
/// <param name="Rejection">The refusal reason, or the empty string when accepted.</param>
/// <param name="StateHash">
/// The state hash both hosts produced. Recorded once because the two sides having produced two
/// different values is a failure, not a row: <see cref="ParityCorpus.Mismatches"/> carries those.
/// </param>
internal sealed record ParityStep(
    int Step, string Command, bool Meta, bool Accepted, string Rejection, string StateHash);

/// <summary>What one sequence produced, in the shape <c>CanonicalStateWriter</c> encodes.</summary>
internal sealed record ParitySequenceOutcome(int Sequence, IReadOnlyList<ParityStep> Steps);

/// <summary>One chunk of the corpus: the sequence wire hashes it covers, in ordinal order.</summary>
internal sealed record ParityChunk(int Chunk, int First, int Last, IReadOnlyList<string> Sequences);

/// <summary>The whole corpus: its chunk wire hashes, in ordinal order.</summary>
internal sealed record ParityCorpusRoot(int Sequences, int ChunkSize, IReadOnlyList<string> Chunks);

/// <summary>One step on which the two hosts disagreed — everything a failure message needs.</summary>
internal sealed record ParityMismatch(
    int Sequence, int Step, string Command, string HostHash, string GatewayHash);

/// <summary>One named sequence the committed table pins individually, and the property that named it.</summary>
/// <param name="Id">The row id in <c>ParityCorpusBaseline.json</c>.</param>
/// <param name="Index">The sequence ordinal the property first holds at.</param>
internal sealed record NamedSequence(string Id, int Index);

/// <summary>
/// `14` §13's client/server parity corpus: 1 000 command sequences driven through the in-process host
/// and through the wire gateway, with their state hashes compared step by step.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What makes this comparison non-vacuous, and what does not.</b> Both sides reference the same
/// <c>SlayIdleRepeat.Core.dll</c>, so a "parity" test that ran the same commands through one host
/// twice would be green by construction and prove nothing at all. What is genuinely compared here is
/// two DIFFERENT dispatch pipelines over that one rules library: the in-process host builds its
/// <c>GameContext</c> itself and calls <c>ExecuteAsync</c>, while the gateway decodes a JSON envelope
/// through <c>WireCommandCodec</c>, resolves a sequencing scope, consults an idempotency ledger, a
/// throttle and a content pin, builds its own <c>GameContext</c>, and splits decide / commit /
/// publish across a unit of work. Those are the two paths a command actually reaches the domain by,
/// and this asserts they land on the same state.
/// </para>
/// <para>
/// ⚠️ <b>The cross-RUNTIME half of parity is not this test's.</b> One process on one architecture
/// cannot show that a phone and a server agree about floating point; that is the determinism job's
/// matrix, and this corpus says nothing about it.
/// </para>
/// <para>
/// The two sides are handed deliberately identical ambience — one clock instance, one content
/// snapshot, the same entitlements and flags, and two <see cref="LockstepIdGenerator"/>s on the same
/// seed — because anything else would make them differ for a reason that is not parity. The host's
/// generator is advanced past the one guid the gateway spends minting the run id, and
/// <see cref="ParityCorpus.HostGuidLead"/> is asserted rather than assumed.
/// </para>
/// </remarks>
internal sealed class ParityCorpus
{
    /// <summary>The seed both sides' id generators are built on.</summary>
    internal const ulong IdSeed = 0x4D35_3132_1D5E_ED01UL;

    /// <summary>
    /// How many guids the gateway draws that the in-process host does not: exactly the one it mints
    /// the run id from, when <c>START_RUN</c> opens the corpus's baseline run.
    /// </summary>
    internal const int HostGuidLead = 1;

    private ParityCorpus(
        IReadOnlyList<ParitySequence> sequences,
        IReadOnlyList<ParitySequenceOutcome> outcomes,
        IReadOnlyList<string> wires,
        IReadOnlyList<string> chunkWires,
        string aggregate,
        IReadOnlyList<ParityMismatch> mismatches,
        int acceptedSteps,
        int refusedSteps,
        IReadOnlyList<string> commandsDriven,
        string baselineHash,
        TimeSpan generationElapsed,
        TimeSpan comparisonElapsed)
    {
        Sequences = sequences;
        Outcomes = outcomes;
        Wires = wires;
        ChunkWires = chunkWires;
        Aggregate = aggregate;
        Mismatches = mismatches;
        AcceptedSteps = acceptedSteps;
        RefusedSteps = refusedSteps;
        CommandsDriven = commandsDriven;
        BaselineHash = baselineHash;
        GenerationElapsed = generationElapsed;
        ComparisonElapsed = comparisonElapsed;

        // Claimed as we go, so the eight rows name eight DIFFERENT sequences rather than pinning one
        // sequence eight times while reading as though it pinned eight.
        var claimed = new HashSet<int>();
        Named = Selectors
            .Select(selector => new NamedSequence(
                selector.Id, FirstUnclaimedIndexWhere(outcomes, claimed, selector.Id, selector.Holds)))
            .ToArray();
    }

    /// <summary>
    /// The named rows the committed table pins individually, in the order they appear in it.
    /// </summary>
    /// <remarks>
    /// Each row is a <em>property of the corpus</em> plus the first sequence that has it, so a row
    /// survives a re-baseline still meaning the same thing. Order matters: the resolution claims
    /// greedily down this list, so a selector satisfied by exactly one sequence comes first.
    /// </remarks>
    private static readonly IReadOnlyList<(string Id, Func<ParitySequenceOutcome, bool> Holds)> Selectors =
        new List<(string, Func<ParitySequenceOutcome, bool>)>
        {
            ("corpus-first", static outcome => outcome.Sequence == 0),
            ("corpus-last", static outcome => outcome.Sequence == ParitySequenceGenerator.SequenceCount - 1),
            ("longest-sequence", static outcome => outcome.Steps.Count == ParitySequenceGenerator.MaxLength),
            ("shortest-sequence", static outcome => outcome.Steps.Count == ParitySequenceGenerator.MinLength),
            ("every-step-accepted", static outcome => outcome.Steps.All(step => step.Accepted)),
            ("every-step-refused", static outcome => outcome.Steps.All(step => !step.Accepted)),
            // The one shape in which the run the sequence started in stops progressing MID-sequence,
            // so the run-already-ended gate and the hashing of a stopped run are both pinned by a
            // row. The last step is excluded deliberately: a departure with nothing after it leaves
            // neither of those exercised, and the row would then claim more than it holds.
            ("leaves-the-run", static outcome => outcome.Steps
                .Take(outcome.Steps.Count - 1)
                .Any(step =>
                    step.Accepted &&
                    (step.Command.Equals("END_RUN", StringComparison.Ordinal) ||
                     step.Command.Equals("ABANDON_RUN", StringComparison.Ordinal)))),
            // Both endpoints exercised inside one sequence, with an acceptance on each: the wire's
            // run/player split is the thing the in-process host has no equivalent of, so a corpus
            // whose accepted steps all landed on one endpoint would compare half of it.
            ("accepts-on-both-endpoints", static outcome =>
                outcome.Steps.Any(step => step.Accepted && !step.Meta) &&
                outcome.Steps.Any(step => step.Accepted && step.Meta)),
        };

    /// <summary>The corpus, built once for the process.</summary>
    internal static ParityCorpus Instance { get; } = BuildAsync().GetAwaiter().GetResult();

    /// <summary>The 1 000 generated sequences, in ordinal order.</summary>
    internal IReadOnlyList<ParitySequence> Sequences { get; }

    /// <summary>What driving each of them through both hosts produced.</summary>
    internal IReadOnlyList<ParitySequenceOutcome> Outcomes { get; }

    /// <summary>Each outcome's <c>"fnv1a:"</c> wire hash.</summary>
    internal IReadOnlyList<string> Wires { get; }

    /// <summary>The 10 chunk hashes, each over 100 sequence wire hashes.</summary>
    internal IReadOnlyList<string> ChunkWires { get; }

    /// <summary>The one hash over all 10 chunks — the headline of the committed table.</summary>
    internal string Aggregate { get; }

    /// <summary>Every step on which the two hosts disagreed. Empty is the claim.</summary>
    internal IReadOnlyList<ParityMismatch> Mismatches { get; }

    /// <summary>How many steps the domain accepted across the whole corpus.</summary>
    internal int AcceptedSteps { get; }

    /// <summary>How many it refused.</summary>
    internal int RefusedSteps { get; }

    /// <summary>Every distinct command wire name the corpus drove, ordinally sorted.</summary>
    internal IReadOnlyList<string> CommandsDriven { get; }

    /// <summary>The hash of the state every sequence starts from.</summary>
    internal string BaselineHash { get; }

    /// <summary>The individually pinned rows, resolved to the sequences that carry their property.</summary>
    internal IReadOnlyList<NamedSequence> Named { get; }

    /// <summary>How long generating the 1 000 sequences took.</summary>
    internal TimeSpan GenerationElapsed { get; }

    /// <summary>How long driving them through both hosts took.</summary>
    internal TimeSpan ComparisonElapsed { get; }

    /// <summary>Generation and comparison together.</summary>
    internal TimeSpan TotalElapsed => GenerationElapsed + ComparisonElapsed;

    private static async Task<ParityCorpus> BuildAsync()
    {
        var clock = Stopwatch.StartNew();

        var (baselineWorld, _) = await GatewayWorld
            .InAStartedRunAsync(ids: new LockstepIdGenerator(IdSeed)).ConfigureAwait(false);
        var baselineRows = await baselineWorld.RowsAsync().ConfigureAwait(false);
        var baseline = Rehydrate(baselineRows);

        var sequences = ParitySequenceGenerator.Corpus(
            baseline,
            Worlds.Content,
            baselineWorld.Clock.UtcNow,
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved());

        var generation = clock.Elapsed;
        clock.Restart();

        var outcomes = new List<ParitySequenceOutcome>(sequences.Count);
        var mismatches = new List<ParityMismatch>();
        var driven = new SortedSet<string>(StringComparer.Ordinal);
        var accepted = 0;
        var refused = 0;

        foreach (var sequence in sequences)
        {
            var outcome = await DriveAsync(sequence, mismatches, driven).ConfigureAwait(false);
            outcomes.Add(outcome);
            accepted += outcome.Steps.Count(step => step.Accepted);
            refused += outcome.Steps.Count(step => !step.Accepted);
        }

        var wires = outcomes.Select(ParityResolution.Hash).ToList();

        var chunkWires = new List<string>(
            (wires.Count + ParitySequenceGenerator.ChunkSize - 1) / ParitySequenceGenerator.ChunkSize);
        for (var first = 0; first < wires.Count; first += ParitySequenceGenerator.ChunkSize)
        {
            var span = wires.GetRange(
                first, Math.Min(ParitySequenceGenerator.ChunkSize, wires.Count - first));
            chunkWires.Add(ParityResolution.HashChunk(first, span));
        }

        var comparison = clock.Elapsed;

        return new ParityCorpus(
            sequences,
            outcomes,
            wires,
            chunkWires,
            ParityResolution.HashCorpus(chunkWires),
            mismatches,
            accepted,
            refused,
            driven.ToArray(),
            WireProjections.HashPlayerAndRun(baselineRows.Player, baselineRows.Run!),
            generation,
            comparison);
    }

    /// <summary>Drives one sequence through both hosts, comparing the hash after every command.</summary>
    private static async Task<ParitySequenceOutcome> DriveAsync(
        ParitySequence sequence, List<ParityMismatch> mismatches, SortedSet<string> driven)
    {
        var (world, run) = await GatewayWorld
            .InAStartedRunAsync(ids: new LockstepIdGenerator(IdSeed)).ConfigureAwait(false);

        var rows = await world.RowsAsync().ConfigureAwait(false);

        var hostIds = new LockstepIdGenerator(IdSeed);
        hostIds.Advance(HostGuidLead);

        var host = Hosts.Over(
            Worlds.CacheHolding(Rehydrate(rows)), clock: world.Clock, ids: hostIds, content: Worlds.Content);

        // START_RUN consumed player sequence 1 in this world; the run scope opened with it and starts
        // its own counter at 1.
        var playerSequence = 1L;
        var runSequence = 0L;

        var steps = new List<ParityStep>(sequence.Commands.Count);

        for (var step = 0; step < sequence.Commands.Count; step++)
        {
            var command = sequence.Commands[step];
            var meta = GameRules.RequiresCommandSeed(command);
            var wireName = WireCommandCodec.WireNameOf(command);
            driven.Add(wireName);

            var hostOutcome = await host
                .SubmitAsync(world.Player, meta ? null : run, command, Worlds.Cancel)
                .ConfigureAwait(false);

            var hostHash = HashOf(hostOutcome.State, meta);

            var envelope = Envelopes.Body(
                wireName,
                meta ? ++playerSequence : ++runSequence,
                $"c-{sequence.Index.ToString(CultureInfo.InvariantCulture)}-{step.ToString(CultureInfo.InvariantCulture)}",
                WireCommandCodec.EncodePayload(command));

            var reply = meta
                ? await world.Gateway
                    .SubmitPlayerCommandAsync(world.Player, envelope, Worlds.Cancel).ConfigureAwait(false)
                : await world.Gateway
                    .SubmitRunCommandAsync(world.Player, run, envelope, Worlds.Cancel).ConfigureAwait(false);

            var body = JsonDocument.Parse(reply.Body).RootElement;
            var gatewayHash = body.TryGetProperty("stateHash", out var hash) ? hash.GetString() : null;
            var rejected = body.TryGetProperty("rejected", out var flag) && flag.GetBoolean();
            var reason = rejected ? body.GetProperty("reason").GetString() ?? string.Empty : string.Empty;

            // 🔒 A transport-tier refusal never reached the domain, so it answers no state hash and
            // consumes no sequence number — every command after it in the sequence would then be a
            // SEQUENCE_GAP, and the comparison would degrade into a thousand cascades that all
            // "agree" about nothing. It is a fault in the driver, not a finding about parity, so it
            // stops the corpus here and says which command caused it.
            if (rejected && !RejectionReasons.IsDomainTier(Enum.Parse<RejectionReason>(reason)))
            {
                throw new InvalidOperationException(
                    $"Sequence {sequence.Index.ToString(CultureInfo.InvariantCulture)} step " +
                    $"{step.ToString(CultureInfo.InvariantCulture)} ({wireName}) earned the " +
                    $"transport-tier refusal {reason} from the gateway. The parity driver is " +
                    "responsible for sending well-sequenced, uniquely identified, correctly versioned " +
                    "envelopes; a transport refusal means it stopped doing that, and everything after " +
                    "it in this sequence compares a desynchronised conversation.");
            }

            if (!string.Equals(hostHash, gatewayHash, StringComparison.Ordinal))
            {
                mismatches.Add(new ParityMismatch(
                    sequence.Index, step, wireName, hostHash, gatewayHash ?? "<none>"));
            }

            steps.Add(new ParityStep(step, wireName, meta, !rejected, reason, hostHash));
        }

        if (hostIds.GuidsDrawn != ((LockstepIdGenerator)world.Ids).GuidsDrawn)
        {
            throw new InvalidOperationException(
                $"Sequence {sequence.Index.ToString(CultureInfo.InvariantCulture)} left the two id " +
                $"generators out of step: the host drew " +
                $"{hostIds.GuidsDrawn.ToString(CultureInfo.InvariantCulture)} guids and the gateway " +
                $"{((LockstepIdGenerator)world.Ids).GuidsDrawn.ToString(CultureInfo.InvariantCulture)}. " +
                "One side consumed entropy the other did not, so every matching hash above it was " +
                "luck rather than parity.");
        }

        return new ParitySequenceOutcome(sequence.Index, steps);
    }

    /// <summary>
    /// One host outcome's state hash, under the gateway's own endpoint rule.
    /// </summary>
    /// <remarks>
    /// The gateway decides run-scoped by ENDPOINT — <c>routedRun is not null || opensRun</c> — and
    /// the in-process host has no endpoint, so the rule is restated here from the same predicate the
    /// host itself routes by. Both sides go through <c>WireProjections</c>, which is the one
    /// serialiser `14` §16.6 allows: hashing the raw snapshots instead would compare bytes no client
    /// ever holds.
    /// </remarks>
    private static string HashOf(WorldSlice state, bool meta)
    {
        var player = state.Player.ToSnapshot();
        var run = meta ? null : state.Run?.ToSnapshot();

        return run is null
            ? WireProjections.HashPlayerAlone(player)
            : WireProjections.HashPlayerAndRun(player, run);
    }

    /// <summary>
    /// The lowest-numbered sequence carrying the named property that no earlier row has claimed — or
    /// a refusal naming the property.
    /// </summary>
    /// <remarks>
    /// It throws rather than returning a sentinel: a named row whose property no longer holds
    /// anywhere is a walker that stopped emitting a shape, which is exactly the silent-coverage-loss
    /// failure the named rows exist to catch.
    /// </remarks>
    private static int FirstUnclaimedIndexWhere(
        IReadOnlyList<ParitySequenceOutcome> outcomes,
        HashSet<int> claimed,
        string id,
        Func<ParitySequenceOutcome, bool> holds)
    {
        for (var index = 0; index < outcomes.Count; index++)
        {
            if (!claimed.Contains(index) && holds(outcomes[index]))
            {
                claimed.Add(index);

                return index;
            }
        }

        throw new InvalidOperationException(
            $"No unclaimed sequence in the corpus carries the property '{id}', which the committed " +
            "baseline pins a row for. Either the walker has stopped emitting a shape it used to emit " +
            "— a loss of coverage, not a row to delete — or the property has become so rare that the " +
            "other rows consumed every sequence carrying it.");
    }

    private static WorldSlice Rehydrate((PlayerSnapshot Player, RunSnapshot? Run) rows)
    {
        var player = PlayerAggregate.Rehydrate(rows.Player, Worlds.Content);
        if (player.IsFailure)
        {
            throw new InvalidOperationException("The parity baseline player does not rehydrate: " + player.Error);
        }

        if (rows.Run is null)
        {
            return new WorldSlice(player.Value, null);
        }

        var run = RunAggregate.Rehydrate(rows.Run);
        if (run.IsFailure)
        {
            throw new InvalidOperationException("The parity baseline run does not rehydrate: " + run.Error);
        }

        return new WorldSlice(player.Value, run.Value);
    }
}

/// <summary>Hashing one parity outcome, its chunk and the corpus — through the one canonical writer.</summary>
internal static class ParityResolution
{
    /// <summary>The <c>"fnv1a:"</c> wire hash of one sequence's steps.</summary>
    internal static string Hash(ParitySequenceOutcome outcome) =>
        CanonicalStateWriter.HashMetaCommandState(outcome);

    /// <summary>The wire hash of one chunk of sequence wire hashes.</summary>
    internal static string HashChunk(int first, IReadOnlyList<string> sequenceWires)
    {
        ArgumentNullException.ThrowIfNull(sequenceWires);

        return CanonicalStateWriter.HashMetaCommandState(new ParityChunk(
            first / ParitySequenceGenerator.ChunkSize,
            first,
            first + sequenceWires.Count - 1,
            sequenceWires));
    }

    /// <summary>The wire hash of the whole corpus — the headline row of the committed table.</summary>
    internal static string HashCorpus(IReadOnlyList<string> chunkWires)
    {
        ArgumentNullException.ThrowIfNull(chunkWires);

        return CanonicalStateWriter.HashMetaCommandState(new ParityCorpusRoot(
            ParitySequenceGenerator.SequenceCount, ParitySequenceGenerator.ChunkSize, chunkWires));
    }
}
