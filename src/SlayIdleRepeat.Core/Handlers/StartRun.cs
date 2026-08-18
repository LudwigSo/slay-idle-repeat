using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>START_RUN</c> handler: commits a run seed and creates the <c>Run</c> every other run
/// command assumes already exists.
/// </summary>
/// <remarks>
/// <para>
/// In order: refuse a request that already has an active run; refuse a chapter/tier the seed
/// derivation could not hash; refuse a tier whose authored clear the player has not earned; refuse
/// one whose authored Legend Level they have not reached; spend the player's lifetime run counter
/// and derive the seed from it; build the new <c>Run</c> via <c>Run.Rehydrate</c> — the only way to
/// construct one — and attach it through <see cref="HandlerInput.OpenRun"/>. All four refusals sit
/// ahead of <c>Player.BeginRun()</c>.
/// </para>
/// <para>
/// ⚠️ That ordering is defence in depth and nothing more: a refused command's working copy is
/// discarded by <c>GameRules.Apply</c>, which returns the caller's own slice, so a counter spent
/// before a refusal would be spent on an object nobody reads. Mutation-testing confirmed it — moving
/// <c>BeginRun()</c> above the gate reddens no test in the repository. Do not read the order below as
/// the thing that makes a refusal free; read it as the order that stays correct if it ever is.
/// </para>
/// <para>
/// `14` §9 makes command validation the server's job — "is the action legal now" — so `10` §7's
/// chapter/tier ladder is answered here and not only on the screen that draws it. The ladder itself
/// is authored data, read by <see cref="ChapterGatingTuning"/>; this handler asks it a question and
/// picks the refusal.
/// </para>
/// <para>
/// <c>Position</c>, HP and <c>Gold</c> are not equally authored. <c>Position</c> is the design's own
/// starting value (the virtual trailhead, one step before node 0); <c>Gold</c> is 0 because nothing
/// can have paid a fresh run any.
/// </para>
/// <para>
/// 🔒 <b>Max HP is scored off the hero's build, which is the revisit this comment used to ask for.</b>
/// It read <em>"no build (gear/perks/pets/talents) exists yet to score"</em> and used
/// <c>Run.SetHitPoints</c>' structural floor of 1/1 instead. M4-16 and M7-06b built that hero, so the
/// condition is met: <see cref="Rules.Stats.HeroBuild"/> composes <c>05</c> §2's
/// <c>MaxHP = 250 + 45·L</c> through the equipped items, their affixes and their set bonuses, and the
/// run is opened at that value, full.
/// </para>
/// <para>
/// 🔴 <b>Leaving it at 1 was not a harmless placeholder — it made the whole HP economy inert</b>, which
/// is what M7-06c's code review found and M7-06d fixes. Every <c>SetHitPoints</c> call in the game
/// passes <c>run.MaxHp</c> unchanged, so the 1 never moved: <c>Revive</c> healed
/// <c>MaxHp × HealPctMaxHp</c> clamped into <c>[1, MaxHp]</c> = <b>1</b>, the campfire's 40% rest and
/// the Stage Gate's 15% heal were fractions of <b>1</b>, and a loss set 0 out of 1.
/// </para>
/// <para>
/// 🔒 <b>The build is read with <c>run: null</c> on purpose.</b> <c>HeroBuild.Of</c> reads the run's
/// FROZEN loadout when given a run and the player's CURRENT one otherwise — and at this line the run
/// does not exist yet. The player's loadout is the right answer twice over: it is what <c>07</c> §4
/// freezes on the very next argument, so the Max HP a run opens with and the gear it will fight in
/// come from one reading rather than two.
/// </para>
/// <para>
/// <see cref="MintRunId"/> derives a deterministic id from the player id and run counter rather than
/// calling an ambient id generator, which nothing in <c>Core</c>/<c>Application</c> is allowed to
/// call. It is a stand-in for the wire-issued, collision-checked id the server will allocate later.
/// </para>
/// </remarks>
internal static class StartRun
{
    /// <summary>The virtual trailhead, one step before node 0 — the run's authored starting position.</summary>
    private const int TrailheadPosition = -1;

    /// <summary>
    /// The floor a composed Max HP is clamped up to, so a content set that scored zero still opens a
    /// run the domain accepts.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not a starting value any more — see <see cref="Handle"/>'s remarks. It survives as a clamp
    /// because <c>Run.SetHitPoints</c> refuses a Max HP below 1, and a hero whose build somehow scored
    /// zero should fail on the fight it cannot win rather than on a rehydrate nobody can read.
    /// </remarks>
    private const int MinimumHitPoints = 1;

    /// <summary>Gold is scoped to the run; a run that has picked up nothing holds none.</summary>
    private const long StartingGold = 0L;

    private static readonly ReadOnlyDictionary<string, ulong> NoStreamPositions =
        new(new Dictionary<string, ulong>(0, StringComparer.Ordinal));

    private static readonly ReadOnlyDictionary<string, long> NoAdUses =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>A just-started run has resolved no minigames.</summary>
    private static readonly ReadOnlyDictionary<int, string> NoResolvedMinigames = new(new Dictionary<int, string>(0));

    /// <summary>
    /// <c>RunSnapshot.PendingTileKind</c>'s "no tile pending" sentinel — a run at the trailhead is
    /// standing on no node, so it is on no tile either.
    /// </summary>
    /// <remarks>
    /// Restated here rather than read off <c>Run</c>, whose own constant is private: this is part of
    /// the snapshot's own contract, and this handler is writing a snapshot.
    /// </remarks>
    private const int NoPendingTile = -1;

    /// <inheritdoc cref="NoPendingTile"/>
    private const int NoPendingTileLinearIndex = 0;

    /// <inheritdoc cref="NoPendingTile"/>
    private const int NoPendingTileStage = 0;

    /// <summary><c>RunSnapshot.PendingEventCardId</c>'s "no card drawn" value: empty, never null.</summary>
    private const string NoPendingEventCard = "";

    /// <summary>Applies <c>START_RUN</c>.</summary>
    /// <param name="command">The chapter and tier to start on.</param>
    /// <param name="input">The cloned, already-caught-up, run-less slice.</param>
    /// <returns>
    /// A rejection if the player already has an active run, the command names a chapter/tier the
    /// seed derivation could not hash, or the tier's authored rung demands a clear or a Legend Level
    /// this player does not have; otherwise accepted, with the new <c>Run</c> attached and no events.
    /// </returns>
    internal static HandlerResult Handle(StartRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        // Checked first, before anything is spent: a player who already has a run legitimately
        // double-taps "start run" or retries a call whose response they never saw.
        if (input.State.Run is not null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Checked before Player.BeginRun() spends the lifetime counter — belt to Apply's braces,
        // not the thing that makes a refusal free. See this type's remarks.
        if (command.ChapterId < 1 || !Enum.IsDefined(command.Tier))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var player = input.Player;

        // Only now: the ladder looks the tier up as a rung key, and a tier the enum does not declare
        // has no rung to look up. The shape check above is what keeps that question askable.
        var rung = ChapterGatingTuning.Read(input.Context.Content).Rung(command.Tier);
        var requiredClear = rung.RequiredClear(command.ChapterId);

        // The clear is checked FIRST, so a request blocked by both requirements is answered with the
        // clear. HandlerResult.Reject carries one enum value and no detail payload, so exactly one of
        // the two can ever be named; the chapter select screen stays the surface that lists both.
        if (requiredClear is { } clear && !player.HasClearedChapterTier(clear.ChapterId, clear.Tier))
        {
            return HandlerResult.Reject(RejectionReason.PREREQUISITE_NOT_CLEARED);
        }

        if (rung.RequiresLegendLevel is { } requiredLevel && player.LegendLevel < requiredLevel)
        {
            return HandlerResult.Reject(RejectionReason.LEGEND_LEVEL_TOO_LOW);
        }

        var runCounter = player.BeginRun();

        var runSeed = SeedDerivation.RunSeed(
            player.Id, command.ChapterId, command.Tier, input.Context.NowUtc, runCounter);

        // Scored from the build, then clamped to the domain's own floor. Rounded away from zero so a
        // hero whose composed Max HP lands on a fraction opens at the higher whole point rather than
        // silently losing one — the same rounding Revive's heal uses.
        var startingHitPoints = Math.Max(
            MinimumHitPoints,
            (int)Math.Round(
                HeroBuild.Of(player, null, input.Context.Content).BaseStats[StatId.MAX_HP],
                MidpointRounding.AwayFromZero));

        var snapshot = new RunSnapshot(
            SnapshotSchema.SchemaVersion,
            MintRunId(player.Id, runCounter),
            player.Id,
            runSeed,
            command.ChapterId,
            command.Tier,
            input.Context.NowUtc,
            TrailheadPosition,
            startingHitPoints,
            startingHitPoints,
            StartingGold,
            NoStreamPositions,
            NoAdUses,
            NoResolvedMinigames,
            PendingForkJunctionPosition: null,
            PendingForkRemainingSteps: null,
            NoPendingTile,
            NoPendingTileLinearIndex,
            NoPendingTileStage,
            NoPendingEventCard,

            // 07 §4's snapshot-at-run-start, taken here because here is the only moment it can be:
            // the run does not exist before this line and the loadout may not change after it.
            // Named rather than positional, since every parameter past this point is optional and a
            // positional argument would bind to whichever one a later append happened to displace.
            StartingLoadout: player.Loadout.ToSnapshot());

        var run = Run.Rehydrate(snapshot);

        if (run.IsFailure)
        {
            // A defect, not a rejection: every field above is this handler's own construction, never
            // the player's.
            throw new InvalidOperationException(
                "StartRun built a RunSnapshot that does not rehydrate: " + run.Error + " Every field " +
                "on that snapshot is this handler's own construction, never the player's, so this is " +
                "a defect in StartRun.Handle, not an illegal move.");
        }

        input.OpenRun(run.Value);

        return HandlerResult.Accept();
    }

    /// <summary>A deterministic <see cref="RunId"/> stand-in — see <see cref="Handle"/>'s remarks.</summary>
    private static RunId MintRunId(PlayerId playerId, long runCounter) =>
        new("RUN_" + playerId.Value + "_" + runCounter.ToString(CultureInfo.InvariantCulture));
}
