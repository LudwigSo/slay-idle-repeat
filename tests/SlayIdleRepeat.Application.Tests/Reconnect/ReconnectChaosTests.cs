using System.Globalization;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace SlayIdleRepeat.Application.Tests.Reconnect;

/// <summary>Everything one run leaves behind that a disconnected run has to match.</summary>
/// <param name="World">The world it was played in, for the claims that read the ledger directly.</param>
/// <param name="Driver">The client that played it.</param>
/// <param name="RunId">The identity the run ended up under.</param>
/// <param name="StateHash">The canonical hash of the final stored rows.</param>
/// <param name="PlayerRow">The final stored player snapshot.</param>
/// <param name="RunRow">The final stored run snapshot.</param>
/// <param name="Bodies">The response bodies the client ended up holding, one per boundary.</param>
/// <param name="EconomyRows">Every economy-log row committed, canonically rendered, in order.</param>
/// <param name="RunPinsWritten">Every run identity a content pin was written for, in order.</param>
internal sealed record ChaosCapture(
    ChaosWorld World,
    ChaosRunDriver Driver,
    string RunId,
    string StateHash,
    PlayerSnapshot PlayerRow,
    RunSnapshot RunRow,
    IReadOnlyList<string> Bodies,
    IReadOnlyList<string> EconomyRows,
    IReadOnlyList<string> RunPinsWritten);

/// <summary>
/// 🔒 <b>The reconnect chaos test.</b> The connection is killed at every command boundary of a whole
/// run, and the run has to finish in the state, with the bytes, and with the economy log it would
/// have had if nothing had ever gone wrong.
/// </summary>
/// <remarks>
/// <para>
/// The fault is injected around the one commit, because the transaction commit is the moment the
/// command happened: everything the pipeline decides above it is invisible to everyone, and
/// everything below it is loss-tolerant. Killing the connection on the near side and on the far
/// side of that one line are the two genuinely different disconnections, and the whole contract is
/// the asymmetry between them — a command dropped before it must be re-executed exactly once, and a
/// command dropped after it must be replayed and never re-executed.
/// </para>
/// <para>
/// ⚠️ <b>There is no socket here and there is no host.</b> This repository has no tier a socket-level
/// test could live in, by decision, so "kill the connection" is a fault schedule injected in-process
/// around the server's dispatch pipeline. What that costs is stated plainly: a genuine transport
/// kill — a half-written response, a TLS reset, a load balancer retiring a node mid-request — is not
/// exercised anywhere in this repository and is not exercised here.
/// </para>
/// <para>
/// 🔴 <b>Three oracles, not one, and each says which one broke.</b> A single end-state comparison
/// would pass a run that re-executed a command whose effects happened to cancel out, and a single
/// byte comparison would pass a run whose economy log had doubled underneath it. See
/// <see cref="AssertTheThreeOraclesAsync"/>.
/// </para>
/// </remarks>
public sealed class ReconnectChaosTests
{
    /// <summary>How many rotations it takes to give every boundary every fault class.</summary>
    /// <remarks>
    /// One per class. Pass <c>p</c> gives boundary <c>i</c> the class <c>(i + p) % 4</c>, so across
    /// the four passes each boundary meets each class exactly once — the full boundary × class
    /// product at four runs' cost, with the faults COMPOSING along each run rather than being met
    /// one at a time, which is strictly the harder claim.
    /// </remarks>
    private const int RotationPasses = 4;

    /// <summary>
    /// The fewest boundaries the isolated matrix has to walk before "every command boundary" means
    /// anything. A floor rather than the measured count, so a content retune does not fail this
    /// file — but a run that collapsed into a handful of commands cannot quietly satisfy it.
    /// </summary>
    private const int BoundaryFloor = 40;

    /// <summary>The reference run, played once and compared against by everything below.</summary>
    private static readonly Lazy<Task<ChaosCapture>> ReferenceRun =
        new(() => CaptureAsync(FaultSchedules.None));

    /// <summary>
    /// 🔴 The reference for the ONE cell where the run's identity legitimately moves: the same
    /// undisturbed run, played by a server that had already issued one run identity.
    /// </summary>
    /// <remarks>
    /// A <c>START_RUN</c> dropped before its commit consumed an identity nothing kept, so the retry
    /// mints the NEXT one — and the run to compare that survivor against is the run a server whose
    /// generator had already issued one would have produced. Built rather than substituted for,
    /// because substituting the id textually CANNOT work and the attempt hides real divergence:
    /// MEASURED, the identity is folded into the instance id of every item the run drops
    /// (<c>GI_&lt;runId&gt;_D7</c>) and into every <c>stateHash</c> the response bodies carry, so a
    /// text replacement leaves the dropped gear named after the wrong run and every embedded hash
    /// describing the other one. Playing the reference under the shifted identity keeps all three
    /// oracles exact and normalises nothing.
    /// </remarks>
    private static readonly Lazy<Task<ChaosCapture>> ReferenceRunOnTheSecondIdentity =
        new(() => CaptureAsync(FaultSchedules.None, identitiesAlreadyIssued: 1));

    private readonly ITestOutputHelper _output;

    /// <summary>Builds the case with the sink the measured figures are reported through.</summary>
    /// <param name="output">xunit's output sink.</param>
    public ReconnectChaosTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 🔒 <b>The headline.</b> A whole run played with the connection killed at EVERY one of its
    /// command boundaries finishes in the same state, holding the same bytes, having written the
    /// same economy log, as the same run played with no disconnection at all.
    /// </summary>
    /// <param name="pass">Which rotation of the fault classes this run meets.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Killing_the_connection_at_every_command_boundary_of_a_whole_run_loses_and_duplicates_nothing(
        int pass)
    {
        var reference = await ReferenceRun.Value;
        var chaos = await CaptureAsync(FaultSchedules.Rotating(pass));
        var what = "rotation pass " + Text(pass);

        // The floors come FIRST and on purpose: every oracle below is a comparison, and a schedule
        // that quietly stopped injecting faults would satisfy all three of them perfectly.
        chaos.Driver.Boundaries.Count.ShouldBe(
            reference.Driver.Boundaries.Count,
            what + ": the disconnected run took a different number of command boundaries than the " +
            "undisturbed one, so the rotation no longer lines up with the boundary it is named " +
            "against and every claim below is comparing two different runs." + Trace(chaos));

        Undisturbed(chaos).ShouldBeEmpty(
            what + ": these boundaries met no disconnection at all, so this run does not kill the " +
            "connection at EVERY command boundary and its name overstates it." + Trace(chaos));

        chaos.Driver.ExecutionsLost.ShouldBeGreaterThan(
            0,
            what + ": no transaction was ever lost, so the before-the-commit class never fired and " +
            "the re-execution half of this claim was never exercised." + Trace(chaos));

        chaos.Driver.ReplaysServed.ShouldBeGreaterThan(
            0,
            what + ": nothing ever replayed, so the after-the-commit class never fired and the " +
            "no-duplicate half of this claim was never exercised." + Trace(chaos));

        chaos.Driver.Resyncs.ShouldBeGreaterThan(
            0,
            what + ": the client never reconnected, so the reconnect read was never exercised." +
            Trace(chaos));

        chaos.Driver.Ending.ShouldBe(
            ChaosRunDriver.VictoryEnding,
            what + ": the run did not complete correctly — it ended some other way than by beating " +
            "the boss and being closed by an accepted END_RUN." + Trace(chaos));

        await AssertTheThreeOraclesAsync(chaos, what);

        _output.WriteLine(
            what + ": " + Text(chaos.Driver.Boundaries.Count) + " boundaries, " +
            Text(chaos.Driver.GatewayCalls) + " gateway calls (" +
            Text(chaos.Driver.ExecutionsCommitted) + " committed, " +
            Text(chaos.Driver.ExecutionsLost) + " lost, " +
            Text(chaos.Driver.ReplaysServed) + " replayed, " +
            Text(chaos.Driver.Resyncs) + " resynced).");
    }

    /// <summary>
    /// The same claim with ONE disconnection instead of fifty: every boundary of the run, taken one
    /// at a time, for one fault class.
    /// </summary>
    /// <remarks>
    /// The saturated passes above already cover the whole boundary × class product; this walks the
    /// product again with the faults ISOLATED, which is what makes a failure attributable to a
    /// single boundary rather than to a run that met fifty disconnections. Nothing is sampled — the
    /// loop runs every boundary — and the measured cost is reported so the next person deciding
    /// whether that is still affordable has the figure rather than a guess.
    /// </remarks>
    /// <param name="fault">The class injected, alone, at each boundary in turn.</param>
    [Theory]
    [InlineData(ChaosFault.DropBeforeCommit)]
    [InlineData(ChaosFault.DropAfterCommit)]
    [InlineData(ChaosFault.FaultOnTheRetryToo)]
    [InlineData(ChaosFault.ReconnectAndResync)]
    public async Task One_disconnection_at_one_boundary_leaves_the_run_exactly_where_no_disconnection_would_have(
        ChaosFault fault)
    {
        var reference = await ReferenceRun.Value;
        var boundaries = reference.Driver.Boundaries.Count;

        boundaries.ShouldBeGreaterThanOrEqualTo(
            BoundaryFloor,
            "the reference run took only " + Text(boundaries) + " command boundaries, which is not " +
            "a whole run — every cell of the matrix below would be a claim about a run that barely " +
            "left the trailhead.");

        var walked = 0;

        for (var index = 0; index < boundaries; index++)
        {
            var chaos = await CaptureAsync(FaultSchedules.Only(index, fault));
            var at = "a single " + fault + " at boundary " + Text(index) + " (" +
                reference.Driver.Boundaries[index].WireName + ")";

            chaos.Driver.Boundaries.Count.ShouldBe(
                boundaries,
                at + ": the run took a different number of boundaries than the undisturbed one." +
                Trace(chaos));

            AssertTheOneFaultFired(reference, chaos, index, at);
            await AssertTheThreeOraclesAsync(chaos, at);

            walked++;
        }

        walked.ShouldBe(
            boundaries,
            "the matrix walked " + Text(walked) + " of the run's " + Text(boundaries) +
            " boundaries, so it did not cover every command boundary after all.");

        _output.WriteLine(fault + ": " + Text(walked) + " isolated boundaries walked.");
    }

    /// <summary>
    /// 🔴 <b>The one cell where the run's identity legitimately moves.</b> A connection killed before
    /// <c>START_RUN</c>'s commit drops the identity that command minted, so the retry mints another
    /// — and the dropped one was never committed and was never told to any client. One run exists,
    /// not two, which is exactly the no-duplicate outcome.
    /// </summary>
    [Fact]
    public async Task A_connection_killed_before_START_RUNs_commit_opens_exactly_one_run_and_orphans_the_other_pin()
    {
        var reference = await ReferenceRun.Value;
        var chaos = await CaptureAsync(FaultSchedules.Only(0, ChaosFault.DropBeforeCommit));

        // The identity the dropped attempt minted is the very one the undisturbed run kept: both
        // draw it first from a generator that counts, so the reference names the orphan.
        var dropped = new RunId(reference.RunId);
        var surviving = new RunId(chaos.RunId);

        chaos.RunId.ShouldNotBe(
            reference.RunId,
            "the retry re-used the identity whose transaction was dropped. That is only safe if the " +
            "identity was committed, and it was not." + Trace(chaos));

        chaos.Driver.ExecutionsLost.ShouldBe(
            1, "exactly one transaction should have been lost." + Trace(chaos));

        var orphan = await chaos.World.Ledger.ReadLastSequenceAsync(
            CommandScopes.ForRun(chaos.World.Player, dropped), Worlds.Cancel);

        orphan.ShouldBeNull(
            "the dropped run " + dropped.Value + " has an OPEN sequencing scope, so START_RUN's " +
            "transaction did not roll back as one unit: the scope it opens committed while the " +
            "snapshot and the outcome record did not." + Trace(chaos));

        var open = await chaos.World.Ledger.ReadLastSequenceAsync(
            CommandScopes.ForRun(chaos.World.Player, surviving), Worlds.Cancel);

        open.ShouldNotBeNull(
            "the surviving run " + surviving.Value + " has no sequencing scope, so the retry's " +
            "START_RUN did not open one and the run it answered cannot be played." + Trace(chaos));

        // The pins go down before the commit that makes a run addressable, so a dropped commit
        // leaves the pin it already wrote behind. Asserted rather than tolerated: it is the one
        // observable that outlives a rolled-back command, and a reader meeting it later deserves to
        // find it named here rather than to discover it.
        chaos.RunPinsWritten.ShouldBe(
            [dropped.Value, surviving.Value],
            "a content pin should have been written for the dropped identity and then for the one " +
            "that survived — the orphan is expected, its absence would mean the pin moved to the " +
            "far side of the commit, and a third would mean a third attempt." + Trace(chaos));

        reference.RunPinsWritten.ShouldBe(
            [dropped.Value],
            "the undisturbed run should have written exactly one pin, for the one identity it " +
            "minted." + Trace(reference));

        await AssertTheThreeOraclesAsync(chaos, "a dropped START_RUN transaction");
    }

    /// <summary>
    /// 🔴 <b>The reconnect read cannot answer a client that lost <c>START_RUN</c>'s reply.</b> It is
    /// addressed by run id, and that reply is where the run id comes from. The client's only
    /// recourse is the retry with the same command id, which works because the record is on the
    /// PLAYER scope and its stored body carries the identity.
    /// </summary>
    [Fact]
    public async Task The_reconnect_read_cannot_answer_a_client_that_never_saw_the_run_id_so_only_the_retry_can()
    {
        var reference = await ReferenceRun.Value;
        var chaos = await CaptureAsync(FaultSchedules.Only(0, ChaosFault.ReconnectAndResync));

        chaos.Driver.Substitutions.Count.ShouldBe(
            1,
            "the reconnect read is addressed by a run id the client does not yet hold at boundary " +
            "0, so exactly one substitution should have been recorded there. A schedule that " +
            "silently skipped the cell would record none." + Trace(chaos));

        var substitution = chaos.Driver.Substitutions[0];

        substitution.Boundary.ShouldBe(0, "the substitution was recorded at the wrong boundary.");
        substitution.Requested.ShouldBe(ChaosFault.ReconnectAndResync);
        substitution.Applied.ShouldBe(ChaosFault.DropAfterCommit);
        substitution.Why.ShouldContain(
            "run id",
            Case.Sensitive,
            "the recorded reason does not name the run id as what the client is missing, so it does " +
            "not explain why the class could not apply.");

        chaos.Driver.Resyncs.ShouldBe(
            0, "the client reconnected without a run id to reconnect to." + Trace(chaos));

        var opening = new CommandId("cid-0");

        var onThePlayer = await chaos.World.Ledger.ReadRecordAsync(
            CommandScopes.ForPlayer(chaos.World.Player), opening, Worlds.Cancel);

        onThePlayer.ShouldNotBeNull(
            "START_RUN's outcome is not recorded on the PLAYER scope, so the retry that is this " +
            "client's only recourse has nothing to replay from." + Trace(chaos));

        onThePlayer.ResponseBody.ShouldContain(
            chaos.RunId,
            Case.Sensitive,
            "the record the retry replays does not carry the run id, so a client that lost the " +
            "first reply can never learn which run it opened." + Trace(chaos));

        var onTheRun = await chaos.World.Ledger.ReadRecordAsync(
            CommandScopes.ForRun(chaos.World.Player, new RunId(chaos.RunId)), opening, Worlds.Cancel);

        onTheRun.ShouldBeNull(
            "START_RUN's outcome is recorded on the RUN scope as well, which would make the run's " +
            "own sequence start at 1 already consumed." + Trace(chaos));

        await AssertTheThreeOraclesAsync(chaos, "a dropped START_RUN reply recovered by retry");
    }

    /// <summary>
    /// The three oracles, together, each naming itself. Any one of them alone would pass a run the
    /// other two would catch.
    /// </summary>
    private static async Task AssertTheThreeOraclesAsync(ChaosCapture chaos, string what)
    {
        var undisturbed = await ReferenceRun.Value;

        // A run's identity reaches into the id of every item it drops and into every stateHash it
        // was answered with, so a moved identity cannot be substituted back out textually. The
        // comparison is made against an undisturbed run that opened under the SAME identity instead
        // — but only after the one disconnection allowed to move it has been named.
        var identityMoved = !string.Equals(chaos.RunId, undisturbed.RunId, StringComparison.Ordinal);

        if (identityMoved)
        {
            chaos.Driver.Boundaries[0].Fault.ShouldBe(
                ChaosFault.DropBeforeCommit,
                what + ": the run identity moved from " + undisturbed.RunId + " to " + chaos.RunId +
                ", and the only disconnection that may do that is one that dropped the opening " +
                "command's own transaction, leaving an identity that was never committed and never " +
                "told to anyone. This run met " + chaos.Driver.Boundaries[0].Fault + " there " +
                "instead, so the identity moved for a reason nothing here accounts for." +
                Trace(chaos));
        }

        var reference = identityMoved ? await ReferenceRunOnTheSecondIdentity.Value : undisturbed;

        chaos.RunId.ShouldBe(
            reference.RunId,
            what + ": the surviving run opened under " + chaos.RunId + " where the reference it is " +
            "compared against opened under " + reference.RunId + ". A dropped opening transaction " +
            "must consume exactly one identity, so the survivor is the SECOND one and nothing else." +
            Trace(chaos));

        chaos.StateHash.ShouldBe(
            reference.StateHash,
            what + " — ORACLE 1 (final state) BROKE. The run finished in a different state than the " +
            "same run played with no disconnection at all. The hash is over the canonical bytes of " +
            "the client-visible projection, so this is a real divergence in stored state and not a " +
            "difference in how it was compared. Differing projection members: " +
            DifferingMembers(reference, chaos) + "." +
            Trace(chaos));

        // ⚠️ Oracle 3 is asserted BEFORE oracle 2, and the order is the point. Oracles 1 and 3 ask
        // whether the WORLD diverged; oracle 2 asks whether the client was told the right bytes.
        // Asserting the byte comparison last means a failure there is proof the other two held,
        // which is the difference between "the answer was serialised differently" and "a command
        // ran twice" — and with three oracles a failure has to name which one broke.
        var rows = chaos.EconomyRows;

        rows.Count.ShouldBe(
            reference.EconomyRows.Count,
            what + " — ORACLE 3 (economy log) BROKE: " + Text(rows.Count) + " rows against the " +
            "undisturbed run's " + Text(reference.EconomyRows.Count) + ". MORE rows means a command " +
            "whose transaction had already committed was RE-EXECUTED by its retry instead of " +
            "replayed — the duplicate this whole suite exists to rule out. FEWER means an outcome " +
            "was LOST: a command the client was answered for never wrote its events." + Trace(chaos));

        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(rows[index], reference.EconomyRows[index], StringComparison.Ordinal))
            {
                continue;
            }

            rows[index].ShouldBe(
                reference.EconomyRows[index],
                what + " — ORACLE 3 (economy log) BROKE at row " + Text(index) + ". The log has the " +
                "right number of rows and the wrong content, so nothing was duplicated or lost but " +
                "a command wrote a different event than it would have undisturbed." + Trace(chaos));
        }

        var bodies = chaos.Bodies;

        bodies.Count.ShouldBe(
            reference.Bodies.Count,
            what + " — ORACLE 2 (outcome bytes) BROKE: the client ended up holding " +
            Text(bodies.Count) + " answers where the undisturbed run held " +
            Text(reference.Bodies.Count) + ". An outcome was lost or an extra one was produced." +
            Trace(chaos));

        for (var index = 0; index < bodies.Count; index++)
        {
            if (string.Equals(bodies[index], reference.Bodies[index], StringComparison.Ordinal))
            {
                continue;
            }

            bodies[index].ShouldBe(
                reference.Bodies[index],
                what + " — ORACLE 2 (outcome bytes) BROKE at boundary " + Text(index) + " (" +
                chaos.Driver.Boundaries[index].WireName + ", met " +
                chaos.Driver.Boundaries[index].Fault + "). Oracles 1 and 3 passed, so the stored " +
                "state and the economy log are both exactly the undisturbed run's — what differs is " +
                "only what the client was TOLD. The bytes it ended up holding after the " +
                "disconnection are not the bytes it would have been given without one. " +
                FirstDifference(reference.Bodies[index], bodies[index]) +
                " A duplicate must replay its stored outcome rather than re-decide it, and the " +
                "reconnect read must hand back the stored envelopes untouched rather than a second " +
                "serialisation of them." + Trace(chaos));
        }
    }

    /// <summary>The per-class attribution for one isolated fault: which rule fired, counted exactly.</summary>
    private static void AssertTheOneFaultFired(
        ChaosCapture reference, ChaosCapture chaos, int index, string at)
    {
        var applied = chaos.Driver.Boundaries[index].Fault;
        var substituted = chaos.Driver.Substitutions.Count > 0;

        chaos.Driver.ExecutionsCommitted.ShouldBe(
            reference.Driver.ExecutionsCommitted,
            at + ": the run committed " + Text(chaos.Driver.ExecutionsCommitted) + " commands where " +
            "the undisturbed one committed " + Text(reference.Driver.ExecutionsCommitted) +
            ". One disconnection may not change how many commands actually happened." + Trace(chaos));

        switch (applied)
        {
            case ChaosFault.DropBeforeCommit:
                chaos.Driver.ExecutionsLost.ShouldBe(
                    1,
                    at + ": the transaction was supposed to be lost exactly once." + Trace(chaos));
                chaos.Driver.ReplaysServed.ShouldBe(
                    0,
                    at + ": something replayed. A command whose transaction never committed has no " +
                    "stored outcome to replay, so its retry must RE-EXECUTE." + Trace(chaos));
                break;

            case ChaosFault.DropAfterCommit:
                chaos.Driver.ExecutionsLost.ShouldBe(
                    0, at + ": a transaction was lost, but this class commits before it drops." +
                    Trace(chaos));
                chaos.Driver.ReplaysServed.ShouldBe(
                    1,
                    at + ": the retry did not take the replay arm exactly once. Its transaction had " +
                    "already committed, so re-executing it would be the duplicate outcome the " +
                    "idempotency record exists to prevent." + Trace(chaos));
                break;

            case ChaosFault.FaultOnTheRetryToo:
                chaos.Driver.ReplaysServed.ShouldBe(
                    2,
                    at + ": the retry rule was applied twice, so the stored outcome should have been " +
                    "served twice — the replay of a replay is still a read." + Trace(chaos));
                break;

            case ChaosFault.ReconnectAndResync:
                chaos.Driver.Resyncs.ShouldBe(
                    1, at + ": the client should have reconnected exactly once." + Trace(chaos));
                chaos.Driver.FullResyncsOrdered.ShouldBe(
                    0,
                    at + ": the reconnect read ordered a FULL resync, so it could not cover the gap " +
                    "from its stored outcomes and the client had to start over instead of resuming " +
                    "exactly where it was." + Trace(chaos));
                chaos.Driver.RecoveredOutcomes.Count.ShouldBe(
                    1,
                    at + ": the reconnect read handed back " +
                    Text(chaos.Driver.RecoveredOutcomes.Count) + " missed outcomes where exactly one " +
                    "command had been committed without the client being told." + Trace(chaos));
                chaos.Driver.RecoveredOutcomes[0].ShouldBe(
                    reference.Bodies[index],
                    at + ": the outcome the client recovered by reconnecting is not the outcome that " +
                    "was stored for it. The reconnect read is documented to hand the stored envelopes " +
                    "back untouched, so this is a second serialisation of them rather than the bytes " +
                    "the first processing committed. " +
                    FirstDifference(reference.Bodies[index], chaos.Driver.RecoveredOutcomes[0]) +
                    Trace(chaos));
                break;

            default:
                throw new InvalidOperationException(
                    at + ": no attribution is written for the class " + applied + ".");
        }

        if (applied == ChaosFault.DropAfterCommit && substituted)
        {
            chaos.Driver.Substitutions[0].Boundary.ShouldBe(
                index,
                at + ": a substitution was recorded at a boundary this run never scheduled one for.");
        }
    }

    private static async Task<ChaosCapture> CaptureAsync(
        IFaultSchedule schedule, int identitiesAlreadyIssued = 0)
    {
        var world = await ChaosWorld.GearedAsync(identitiesAlreadyIssued: identitiesAlreadyIssued);
        var driver = await ChaosRunDriver.PlayAsync(world, schedule);
        var (player, run) = await world.RowsAsync();

        if (run is null)
        {
            throw new InvalidOperationException(
                "The run is gone from the stored rows, so there is no final state to compare. " +
                "Ending: " + driver.Ending);
        }

        return new ChaosCapture(
            world,
            driver,
            driver.Run?.Value ?? throw new InvalidOperationException("The driver opened no run."),
            WireProjections.HashPlayerAndRun(player, run),
            player,
            run,
            driver.Bodies,
            world.EconomyRows.Select(Canonical).ToArray(),
            world.Pins.Store.RunPinWrites.Select(write => write.Run.Value).ToArray());
    }

    /// <summary>
    /// One economy row as a comparable line. Written out field by field rather than compared as a
    /// record, because a record's generated equality compares any collection member by reference.
    /// </summary>
    private static string Canonical(EconomyEventRecord row) =>
        string.Join(
            '|',
            row.Player.Value,
            row.Run?.Value ?? "-",
            row.CommandId.Value,
            Text(row.Sequence),
            Text(row.OccurredAtUtc.ToUnixTimeMilliseconds()),
            row.EventType,
            row.PayloadJson);

    /// <summary>The boundaries a schedule left undisturbed — empty is what a saturating pass owes.</summary>
    private static IReadOnlyList<int> Undisturbed(ChaosCapture chaos) =>
        chaos.Driver.Boundaries
            .Where(boundary => boundary.Fault == ChaosFault.None)
            .Select(boundary => boundary.Index)
            .ToArray();

    /// <summary>
    /// Which members of the client-visible projections disagree, so a broken end-state names the
    /// field rather than only the hash it changed.
    /// </summary>
    private static string DifferingMembers(ChaosCapture reference, ChaosCapture chaos)
    {
        var differing = Members(WireProjections.Of(reference.PlayerRow), WireProjections.Of(chaos.PlayerRow))
            .Select(name => "player." + name)
            .Concat(
                Members(WireProjections.Of(reference.RunRow), WireProjections.Of(chaos.RunRow))
                    .Select(name => "run." + name))
            .ToArray();

        return differing.Length == 0
            ? "none — the projections agree member by member, so the difference is in how they were " +
              "written rather than in what they hold"
            : string.Join(", ", differing);
    }

    private static IEnumerable<string> Members(object expected, object actual)
    {
        foreach (var property in expected.GetType().GetProperties())
        {
            var left = Rendered(property.GetValue(expected));
            var right = Rendered(property.GetValue(actual));

            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                yield return property.Name + " (" + left + " vs " + right + ")";
            }
        }
    }

    private static string Rendered(object? value)
    {
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (NotSupportedException)
        {
            return value?.ToString() ?? "null";
        }
    }

    /// <summary>Where two bodies first differ, with enough either side to read the difference.</summary>
    private static string FirstDifference(string expected, string actual)
    {
        const int Context = 60;

        var shared = Math.Min(expected.Length, actual.Length);
        var at = 0;

        while (at < shared && expected[at] == actual[at])
        {
            at++;
        }

        var from = Math.Max(0, at - Context);

        return "First difference at index " + Text(at) + " of " + Text(expected.Length) +
            ". Expected …" + Excerpt(expected, from, Context) + "… but got …" +
            Excerpt(actual, from, Context) + "….";
    }

    private static string Excerpt(string text, int from, int context) =>
        text.Substring(from, Math.Min(text.Length - from, (context * 2) + 1));

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Trace(ChaosCapture capture) =>
        Environment.NewLine + "run " + capture.RunId + ", ending '" + capture.Driver.Ending + "', " +
        Text(capture.Driver.Boundaries.Count) + " boundaries, " +
        Text(capture.Driver.GatewayCalls) + " gateway calls (" +
        Text(capture.Driver.ExecutionsCommitted) + " committed, " +
        Text(capture.Driver.ExecutionsLost) + " lost, " +
        Text(capture.Driver.ReplaysServed) + " replayed, " +
        Text(capture.Driver.Resyncs) + " resynced), " +
        Text(capture.EconomyRows.Count) + " economy rows. Log:" + Environment.NewLine +
        string.Join(Environment.NewLine, capture.Driver.Log);
}
