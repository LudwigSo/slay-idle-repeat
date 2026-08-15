using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `02` §2 — the <c>START_RUN</c> handler (M3-15): commits <c>runSeed</c> and creates the
/// <c>Run</c> the other 18 <c>CommandKind.Run</c> rows all assume already exists.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What this handler owns, in order.</b> (1) refuse a request that already has an active run,
/// as a domain-tier rejection, before anything is spent; (2) refuse a chapter or tier the command
/// carries that <c>SeedDerivation.RunSeed</c> could not hash, the same way — a client's illegal
/// request is `30` §2.1's <b>P3</b> data, never an exception out of <c>Apply</c>; (3) spend
/// <c>Player.BeginRun()</c>'s lifetime counter and derive <c>runSeed</c> from it (`02` §2); (4) build
/// the new <c>Run</c> the only way `30` §11.3 allows — <c>Run.Rehydrate</c> over a snapshot, never a
/// constructor `Run` does not have — and hand it to <see cref="HandlerInput.OpenRun"/>, the one seam
/// that can attach it to this command's run-less slice.
/// </para>
/// <para>
/// ⚠️ <b><c>Position</c>, <c>CurrentHp</c>/<c>MaxHp</c> and <c>Gold</c> are not equally authored, and
/// that is stated here rather than left for a reader to discover by diffing three numbers against
/// nothing.</b> <c>Position</c> is `03` §1.1's own value — <em>"the hero begins every run at a virtual
/// trailhead one step before node 0 (position −1)"</em> — so <see cref="TrailheadPosition"/> is a
/// transcription, not an invention. <c>Gold</c> is 0 because a run that has picked up nothing has
/// moved nothing (`10` §1 scopes it to the run, and nothing between <c>START_RUN</c> and a player's
/// first pickup can have paid it in).
/// </para>
/// <para>
/// 🔴 <b><c>CurrentHp</c>/<c>MaxHp</c> are the one pair no document authors, and this handler does not
/// invent a balance number to fill the hole (steering S6).</b> `30` §11.5 makes <c>Run.MaxHp</c> "not
/// a function of the player's gear" — it is read off the run's own <c>PowerCalculator</c>-scored build
/// at creation — but <c>Inventory</c>, <c>DraftedPerks</c>, pets, mounts and talents are all still
/// <c>GapRegister</c> entries: there is no build to score yet, so there is no formula this handler
/// could read even if one existed in the design documents, and none does — `02` and `03` describe only
/// <em>percentage</em> effects on Max HP (heals, shrines, the Heartroot Tonic), never a starting value.
/// <see cref="StartingHitPoints"/> is <c>Run.SetHitPoints</c>'s own structural floor — 1 current, 1
/// maximum, the least a <c>Run.Rehydrate</c> row can legally hold — used because it is the one value
/// this handler can name without a design document backing it, and it is 📐 <b>NOT a design number</b>
/// (no document authorises it, and it is deliberately not a plausible-looking one like 100/100 would
/// be). It is a <b>new, unlettered assumption</b>, exactly as <c>GameRules.EnergyRegenReason</c>'s and
/// <c>Handlers.BeginSession.DailyRefillReason</c>'s are: reported here as one, to get its letter at
/// integration, and it should be revisited the day a hero-build task (M4's gear/pet/talent lane) can
/// compute a real starting Max HP.
/// </para>
/// <para>
/// 🔴 <b><c>Id</c> is also unauthored plumbing, stated for the same reason.</b> `14` §2.3's own remarks
/// on <c>StartRunCommand</c> say <em>"the server allocates the <see cref="RunId"/>"</em>, but no port
/// for that exists in <c>Core</c> today — there is no <c>NewId</c> seam, and <c>Guid.NewGuid()</c> is a
/// banned ambient API here (`14` §8.1's own rule: nothing in <c>Core</c>/<c>Application</c> may call
/// it). <see cref="MintRunId"/> derives one deterministically from <see cref="PlayerId"/> and the same
/// <c>runCounter</c> that seeds the run — reproducible, ambient-free, and never colliding for one
/// player, because <c>Player.BeginRun()</c> never repeats a counter. It is not `14` §2.3's real
/// allocator (a wire-issued, collision-checked id is the Application layer's, at M5-03), and this
/// paragraph flags the gap rather than let a plausible id read as though it settled it.
/// </para>
/// </remarks>
internal static class StartRun
{
    /// <summary>
    /// 🔒 `03` §1.1 (ruled in `16` A7) — the virtual trailhead, one step before node 0. The one
    /// position value `02`/`03` actually author for a just-started run; see <see cref="Handle"/>'s
    /// remarks for the two that are not.
    /// </summary>
    private const int TrailheadPosition = -1;

    /// <summary>
    /// 📐 <b>Not a design value</b> — see <see cref="Handle"/>'s remarks. <c>Run.SetHitPoints</c>'s
    /// own floor (never below 1 current, never below 1 maximum), used because no document authors a
    /// starting Max HP before a hero build exists to score.
    /// </summary>
    private const int StartingHitPoints = 1;

    /// <summary>`10` §1 scopes <c>GOLD</c> to the run; a run that has picked up nothing holds none.</summary>
    private const long StartingGold = 0L;

    private static readonly ReadOnlyDictionary<string, ulong> NoStreamPositions =
        new(new Dictionary<string, ulong>(0, StringComparer.Ordinal));

    private static readonly ReadOnlyDictionary<string, long> NoAdUses =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>
    /// M3-03c — a just-started run has resolved no minigames. See <c>Run</c>'s
    /// <c>_resolvedMinigames</c> field remarks.
    /// </summary>
    private static readonly ReadOnlyDictionary<int, string> NoResolvedMinigames = new(new Dictionary<int, string>(0));

    /// <summary>🔒 `02` §2 — applies <c>START_RUN</c>.</summary>
    /// <param name="command">The chapter and tier to start on.</param>
    /// <param name="input">
    /// The cloned, already-caught-up, run-less slice — run-less because `30` §4.1 makes that the
    /// natural slice for the one command whose job is to create one.
    /// </param>
    /// <returns>
    /// A rejection if the player already has an active run or the command names a chapter/tier
    /// <c>SeedDerivation.RunSeed</c> could not hash; otherwise accepted, with the new <c>Run</c>
    /// attached through <see cref="HandlerInput.OpenRun"/> and no events (`30` §7 names no event for
    /// a run's own creation).
    /// </returns>
    internal static HandlerResult Handle(StartRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        // 🔒 1 · Already-active-run check, FIRST and before anything is spent. This is the mirror
        // image of GameRules.Execute's run-less guard: that guard is a defect because the Application
        // layer loaded the wrong slice for 18 rows that REQUIRE a Run; this is a REJECTION because a
        // player who already has one legitimately double-taps "start run", or retries a call whose
        // response they never saw, and 30 §2.1's P3 makes that the player's illegal move, data, not
        // an exception. No RejectionReason in 14 §16.2 names "a run is already active" specifically,
        // so this is ILLEGAL_STATE, the domain-tier catch-all — inventing a new wire value here would
        // be exactly the hole steering S6 forbids.
        if (input.State.Run is not null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // 🔒 2 · The chapter/tier a client could send that SeedDerivation.RunSeed cannot hash.
        // Checked HERE, before Player.BeginRun() spends the lifetime counter, so a rejected START_RUN
        // costs the player nothing — the same reasoning GameRules.Execute's whole reject-before-write
        // shape rests on. Letting SeedDerivation.RunSeed's own guards throw instead would turn a
        // player's malformed request into a P3-violating exception out of Apply.
        if (command.ChapterId < 1 || !Enum.IsDefined(command.Tier))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var player = input.Player;

        // 🔒 3 · 02 §2's runCounter — Player.BeginRun()'s return, AFTER the increment — and the
        // seed itself, computed inside Apply as 02 §2 requires (via this handler, which Apply calls).
        var runCounter = player.BeginRun();

        var runSeed = SeedDerivation.RunSeed(
            player.Id, command.ChapterId, command.Tier, input.Context.NowUtc, runCounter);

        // 🔒 4 · Run.Rehydrate is the ONLY way to obtain a Run (30 §11.3; Run.cs's own remarks are
        // explicit there is no "new run" constructor). LastAppliedAtUtc is this command's own
        // NowUtc — the instant the run is created is the instant its TTL starts sliding from
        // (14 §16.3) — and the two maps are shared empty singletons: a run that has drawn nothing
        // and watched no ad genuinely holds none (14 §8.1, 12 §4.3).
        var snapshot = new RunSnapshot(
            SnapshotSchema.SchemaVersion,
            MintRunId(player.Id, runCounter),
            player.Id,
            runSeed,
            command.ChapterId,
            command.Tier,
            input.Context.NowUtc,
            TrailheadPosition,
            StartingHitPoints,
            StartingHitPoints,
            StartingGold,
            NoStreamPositions,
            NoAdUses,
            NoResolvedMinigames,
            PendingForkJunctionPosition: null,
            PendingForkRemainingSteps: null);

        var run = Run.Rehydrate(snapshot);

        if (run.IsFailure)
        {
            // 🔒 A defect, not a rejection: every field above is this handler's own construction,
            // never the player's, so a row that fails Run.Rehydrate's validation is this handler
            // wrong — not a player who asked for something they cannot have (30 §2.1's P3).
            throw new InvalidOperationException(
                "StartRun built a RunSnapshot that does not rehydrate: " + run.Error + " Every field " +
                "on that snapshot is this handler's own construction, never the player's, so this is " +
                "a defect in StartRun.Handle, not an illegal move.");
        }

        input.OpenRun(run.Value);

        return HandlerResult.Accept();
    }

    /// <summary>
    /// 🔒 A deterministic <see cref="RunId"/> stand-in — see <see cref="Handle"/>'s remarks for why
    /// <c>Core</c> mints one at all rather than receiving `14` §2.3's server-allocated id.
    /// </summary>
    private static RunId MintRunId(PlayerId playerId, long runCounter) =>
        new("RUN_" + playerId.Value + "_" + runCounter.ToString(CultureInfo.InvariantCulture));
}
