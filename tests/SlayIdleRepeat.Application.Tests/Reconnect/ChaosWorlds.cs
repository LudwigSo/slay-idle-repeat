using System.Buffers.Binary;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Tests.Wire;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Application.Tests.Reconnect;

/// <summary>
/// The connection dying mid-exchange. Its own type so the client seam catches exactly its own
/// injected fault and never swallows a genuine defect raised from inside the pipeline.
/// </summary>
internal sealed class ConnectionLost : Exception
{
    /// <summary>Builds the fault, whose message names which side of the commit it fired on.</summary>
    /// <param name="message">Which side of the commit the connection died on.</param>
    internal ConnectionLost(string message)
        : base(message)
    {
    }
}

/// <summary>Which side of the one commit an armed fault fires on.</summary>
internal enum CommitSide
{
    /// <summary>Nothing armed.</summary>
    None = 0,

    /// <summary>Raise INSTEAD of delegating — the transaction never happened.</summary>
    Before = 1,

    /// <summary>Delegate, then raise — the transaction happened and the client cannot know it.</summary>
    After = 2,
}

/// <summary>
/// A unit of work that can be told to lose the connection on either side of the one commit, wrapped
/// around the recording one the gateway suite already runs on.
/// </summary>
/// <remarks>
/// The seam is here rather than anywhere else because the transaction commit is the moment the
/// command happened: everything the gateway does above it is a decision nobody can see, and
/// everything below it is loss-tolerant. A fault before it and a fault after it are therefore the
/// two genuinely different disconnections, and the whole reconnect contract is the asymmetry
/// between them.
/// </remarks>
internal sealed class FaultingUnitOfWork : IUnitOfWork
{
    private readonly RecordingUnitOfWork _inner;
    private CommitSide _armed;

    /// <summary>Wraps the recording unit of work this fixture writes through.</summary>
    /// <param name="inner">The unit of work that actually commits.</param>
    internal FaultingUnitOfWork(RecordingUnitOfWork inner) => _inner = inner;

    /// <summary>The unit of work underneath, so a case can read its <c>Commits</c>.</summary>
    internal RecordingUnitOfWork Inner => _inner;

    /// <summary>How many commits actually reached <see cref="Inner"/>.</summary>
    internal int CommitsReachingTheInner { get; private set; }

    /// <summary>How many faults fired before the commit — decisions that never happened.</summary>
    internal int FaultsBeforeCommit { get; private set; }

    /// <summary>How many fired after it — commits the client was never told about.</summary>
    internal int FaultsAfterCommit { get; private set; }

    /// <summary>Whether a fault is currently armed and has not yet fired.</summary>
    internal bool IsArmed => _armed != CommitSide.None;

    /// <summary>Arms one fault that raises instead of committing.</summary>
    internal void ArmBeforeCommit() => _armed = CommitSide.Before;

    /// <summary>Arms one fault that commits and then raises.</summary>
    internal void ArmAfterCommit() => _armed = CommitSide.After;

    /// <summary>Clears an armed fault that never fired.</summary>
    internal void Disarm() => _armed = CommitSide.None;

    /// <inheritdoc/>
    public async Task CommitAsync(CommandCommit commit, CancellationToken ct)
    {
        // Read and cleared before anything else, so the fault is one-shot even when the throw below
        // unwinds through a caller that never gets to disarm it.
        var armed = _armed;
        _armed = CommitSide.None;

        if (armed == CommitSide.Before)
        {
            FaultsBeforeCommit++;

            throw new ConnectionLost(
                "The connection died BEFORE the commit: no transaction was opened, so this command " +
                "never happened and a retry of it must re-execute exactly once.");
        }

        await _inner.CommitAsync(commit, ct);
        CommitsReachingTheInner++;

        if (armed == CommitSide.After)
        {
            FaultsAfterCommit++;

            throw new ConnectionLost(
                "The connection died AFTER the commit: the transaction landed and the client was " +
                "never told, so a retry of it must replay the stored outcome rather than re-execute.");
        }
    }
}

/// <summary>
/// A generator whose sequence is a function of how many identifiers it has minted and nothing else.
/// </summary>
/// <remarks>
/// 🔒 The suite's own <c>CountingIdGenerator</c> mixes a per-instance random prefix in, deliberately,
/// because a real generator must not repeat across processes. That makes two worlds built the same
/// way mint different run identities — and this task's central claim is that the bytes a client
/// holds after a disconnect are the bytes it would have held without one. Those bytes carry the run
/// id. So the chaos fixture trades the cross-instance uniqueness it does not need for the
/// reproducibility it cannot do without, and says so here rather than in a comparison that quietly
/// substitutes ids.
/// </remarks>
internal sealed class SequentialIdGenerator : IIdGeneratorPort
{
    private const int GuidLength = 16;

    private int _guidsDrawn;
    private int _commandIdsDrawn;

    /// <inheritdoc/>
    public Guid NewGuid()
    {
        Span<byte> bytes = stackalloc byte[GuidLength];

        // Counted from one, so no draw can come out as the empty guid.
        BinaryPrimitives.WriteInt32BigEndian(bytes[12..], Interlocked.Increment(ref _guidsDrawn));

        return new Guid(bytes);
    }

    /// <inheritdoc/>
    public string NewCommandId() =>
        "cid-server-" + Interlocked.Increment(ref _commandIdsDrawn).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One gateway wired exactly as the gateway suite's world wires it, over a unit of work that can
/// lose the connection — plus the reconnect read served from the same store and the same ledger.
/// </summary>
/// <remarks>
/// 🔒 <b>The hero is GEARED before the run opens.</b> <c>CONFIRM_BATTLE_RESULT</c> recomputes the
/// fight, and a bare-handed chapter-1 hero loses its first one — so a run driven over a starting
/// player never opens a draft, never crosses a stage boundary and never reaches the boss, and every
/// claim about disconnecting at "every command boundary of a full run" quietly becomes a claim
/// about four commands. No shipped command grants gear, so the stock is written onto the stored row
/// directly and then WORN through six real <c>EQUIP</c> commands, which is the door a player uses
/// and the only one that lands before the loadout freezes at <c>START_RUN</c>.
/// </remarks>
internal sealed class ChaosWorld
{
    /// <summary>The chapter every chaos run is driven on — the authored floor, and the board this fixture was tuned against.</summary>
    internal const int Chapter = 1;

    /// <summary>The tier every chaos run is driven on.</summary>
    internal const DifficultyTier Tier = DifficultyTier.NORMAL;

    /// <summary>
    /// The player identity every chaos run is driven under. Chosen, not arbitrary: a run's board is
    /// derived from a seed folding the player id and the start instant together, and this pair is
    /// the one whose chapter-1 board a geared hero walks end to end to the boss. Changing either
    /// changes the board, and a different board may end the run some other way.
    /// </summary>
    internal const string BoardPlayerId = "PLAYER_chaos";

    /// <summary>
    /// The instant every chaos run's clock is frozen at — the other half of the board choice made in
    /// <see cref="BoardPlayerId"/>'s remarks, and the reason it is named rather than inlined.
    /// </summary>
    internal static readonly DateTimeOffset BoardInstant = Worlds.Start;

    /// <summary>The six slots the over-par loadout fills, one family each.</summary>
    private static readonly GearFamily[] OverParFamilies =
    [
        GearFamily.BLADE,
        GearFamily.HOOD,
        GearFamily.LEATHERS,
        GearFamily.TREADS,
        GearFamily.BAND,
        GearFamily.PENDANT,
    ];

    /// <summary>The authored slot/family grid the over-par six take their ids and slots from.</summary>
    private const string GearSlotsReference = "content/gear/gear.json#/slots";

    /// <summary>The last chapter the game authors, so item power is scaled as high as content goes.</summary>
    private const int TopChapter = 8;

    /// <summary>The authored enhance ceiling.</summary>
    private const int TopEnhanceLevel = 15;

    /// <summary>A perfect quality roll.</summary>
    private const double TopQuality = 1.0;

    /// <summary>What an item that never failed an enhancement carries.</summary>
    private const int NoEnhanceFailures = 0;

    private ChaosWorld(
        CommandGateway gateway,
        RunStateQuery runStateQuery,
        WorldSliceStore store,
        VolatileCommandLedger ledger,
        FaultingUnitOfWork unitOfWork,
        AdjustableClock clock,
        PlayerId player,
        PinnedContent pins)
    {
        Gateway = gateway;
        RunStateQuery = runStateQuery;
        Store = store;
        Ledger = ledger;
        UnitOfWork = unitOfWork;
        Clock = clock;
        Player = player;
        Pins = pins;
    }

    /// <summary>The gateway every command goes through.</summary>
    internal CommandGateway Gateway { get; }

    /// <summary>The reconnect read, over the same store and the same ledger the gateway writes.</summary>
    internal RunStateQuery RunStateQuery { get; }

    /// <summary>The rows the gateway commits into.</summary>
    internal WorldSliceStore Store { get; }

    /// <summary>The sequencing and idempotency ledger.</summary>
    internal VolatileCommandLedger Ledger { get; }

    /// <summary>The seam the faults are injected at.</summary>
    internal FaultingUnitOfWork UnitOfWork { get; }

    /// <summary>The frozen clock every command is applied at.</summary>
    internal AdjustableClock Clock { get; }

    /// <summary>The player every command is sent as.</summary>
    internal PlayerId Player { get; }

    /// <summary>
    /// The content pins, whose store records every run pin written — the seam the orphan pin a
    /// dropped <c>START_RUN</c> leaves behind is read off.
    /// </summary>
    internal PinnedContent Pins { get; }

    /// <summary>Every economy-log row every landed commit carried, in commit order then event order.</summary>
    /// <remarks>
    /// Off the commits rather than off a sink: the rows are part of the transaction, so this is the
    /// log as the database would hold it — which is the thing "no domain event is duplicated" is a
    /// claim about.
    /// </remarks>
    internal IReadOnlyList<EconomyEventRecord> EconomyRows =>
        UnitOfWork.Inner.Commits.SelectMany(commit => commit.EconomyEvents).ToArray();

    /// <summary>
    /// Where the PLAYER scope stands: the sequence the next command on the player endpoint must
    /// carry. The six equips consumed 1 through 6, so a driver's <c>START_RUN</c> starts from here.
    /// </summary>
    internal long NextPlayerSequence { get; private set; } = 1;

    /// <summary>A geared world at the board <see cref="BoardPlayerId"/> and <see cref="BoardInstant"/> select.</summary>
    /// <param name="playerId">The player identity, for a case sweeping boards.</param>
    /// <param name="start">The frozen instant, for a case sweeping boards.</param>
    /// <param name="identitiesAlreadyIssued">
    /// How many identities the generator has handed out before this world's first command.
    /// </param>
    /// <remarks>
    /// <paramref name="identitiesAlreadyIssued"/> exists for exactly one comparison. A
    /// <c>START_RUN</c> dropped before its commit consumed a run identity that nothing kept, so the
    /// retry mints the NEXT one — and the reference run to compare that survivor against is the run
    /// the server would have produced had it minted that identity in the first place. Setting this
    /// to 1 builds that reference, which is what lets the cell be compared byte for byte instead of
    /// through a textual id substitution that would leave every <c>stateHash</c> in the bodies stale.
    /// </remarks>
    internal static async Task<ChaosWorld> GearedAsync(
        string playerId = BoardPlayerId, DateTimeOffset? start = null, int identitiesAlreadyIssued = 0)
    {
        var cache = new InMemoryLocalCache();
        var store = new WorldSliceStore(cache);
        var clock = new AdjustableClock();
        clock.Set(start ?? BoardInstant);

        var ledger = new VolatileCommandLedger();
        var throttle = new ManualThrottle();
        var flags = LocalHostAmbience.NoRemoteConfigResolved();
        var pins = PinnedContent.OverTheShippedSnapshot();

        var player = new PlayerId(playerId);
        var starting = PlayerAggregate.CreateStartingNamedAfterItsOwnId(player, clock.UtcNow, Worlds.Content);
        if (starting.IsFailure)
        {
            throw new InvalidOperationException("The starting player does not rehydrate: " + starting.Error);
        }

        var stocked = starting.Value.ToSnapshot() with
        {
            Inventory = new InventorySnapshot(0, OverParSix(Worlds.Content), []),
        };

        await store.SaveAsync(new StoredSlice(stocked, null), Worlds.Cancel);

        var unitOfWork = new FaultingUnitOfWork(new RecordingUnitOfWork(store, ledger, Worlds.Content));
        var ids = new SequentialIdGenerator();

        for (var issued = 0; issued < identitiesAlreadyIssued; issued++)
        {
            _ = ids.NewGuid();
        }

        var gateway = new CommandGateway(
            new ApplyCommandUseCase(store, new DomainEventDispatcher([])),
            clock,
            ids,
            Worlds.Content,
            LocalHostAmbience.NoSubscriptionResolved(),
            () => flags,
            ledger,
            throttle,
            pins.Pinning,
            unitOfWork);

        var world = new ChaosWorld(
            gateway,
            new RunStateQuery(new ReadOwnStateUseCase(store), ledger),
            store,
            ledger,
            unitOfWork,
            clock,
            player,
            pins);

        await world.WearTheOverParSixAsync();

        return world;
    }

    /// <summary>The stored rows, read back the way the gateway's own dispatch reads them.</summary>
    internal async Task<(PlayerSnapshot Player, RunSnapshot? Run)> RowsAsync()
    {
        var rows = await Store.ReadSnapshotsAsync(Player, Worlds.Cancel)
            ?? throw new InvalidOperationException("No rows stored for the chaos player.");

        return (rows.Player, rows.Run);
    }

    /// <summary>One envelope body carrying a real command, encoded the way the wire encodes it.</summary>
    /// <param name="command">The command.</param>
    /// <param name="sequence">Its scope's next sequence.</param>
    /// <param name="commandId">The client-generated idempotency key.</param>
    internal static string EnvelopeFor(GameCommand command, long sequence, string commandId) =>
        Envelopes.Body(
            WireCommandCodec.WireNameOf(command),
            sequence,
            commandId,
            WireCommandCodec.EncodePayload(command));

    private async Task WearTheOverParSixAsync()
    {
        foreach (var item in OverParSix(Worlds.Content))
        {
            var command = new EquipCommand(item.InstanceId, item.Slot);
            var reply = await Gateway.SubmitPlayerCommandAsync(
                Player,
                EnvelopeFor(command, NextPlayerSequence, "cid-equip-" + item.Slot),
                Worlds.Cancel);

            var body = Replies.Parse(reply, expectedStatus: 200);

            if (body.TryGetProperty("rejected", out var rejected) && rejected.GetBoolean())
            {
                throw new InvalidOperationException(
                    "EQUIP of the fixture's " + item.Slot + " was refused (" +
                    body.GetProperty("reason").GetString() + "), so this run's hero fights bare-handed " +
                    "and loses its first battle. Nothing further down would name a bare hero as the " +
                    "cause — the failures land on stage gates, drafts and a run that never reaches " +
                    "the boss instead.");
            }

            NextPlayerSequence++;
        }
    }

    /// <summary>
    /// One item per slot, far above chapter-1 par: top band, top chapter of origin, perfect quality,
    /// top enhance level. Ids and slots come off the authored grid rather than being spelled here,
    /// so a renamed base item fails loudly instead of producing an item no catalogue knows.
    /// </summary>
    private static IReadOnlyList<GearInstanceSnapshot> OverParSix(ContentSnapshot content)
    {
        var slots = content.Read(GearSlotsReference);
        var items = new List<GearInstanceSnapshot>(OverParFamilies.Length);

        foreach (var family in OverParFamilies)
        {
            var (defId, slot) = AuthoredRow(slots, family);

            items.Add(new GearInstanceSnapshot(
                new GearInstanceId("above_par_" + family),
                defId,
                slot,
                family,
                Rarity.SS,
                TopChapter,
                TopQuality,
                TopEnhanceLevel,
                NoEnhanceFailures,
                [],
                Locked: false));
        }

        return items;
    }

    private static (string DefId, GearSlot Slot) AuthoredRow(ContentValue slots, GearFamily family)
    {
        foreach (var block in slots.Items)
        {
            if (!block.TryGetMember("slot", out var slotName) ||
                !block.TryGetMember("families", out var families))
            {
                continue;
            }

            foreach (var row in families!.Items)
            {
                if (row.TryGetMember("family", out var authored) &&
                    string.Equals(authored!.AsText(), family.ToString(), StringComparison.Ordinal) &&
                    row.TryGetMember("id", out var id))
                {
                    return (id!.AsText(), Enum.Parse<GearSlot>(slotName!.AsText()));
                }
            }
        }

        throw new InvalidOperationException(
            "The gear grid authors no row for " + family + ", so the chaos fixture cannot build the " +
            "over-par loadout its hero needs to survive a whole run.");
    }
}
