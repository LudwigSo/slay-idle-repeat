using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
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
/// one whose authored Legend Level they have not reached; refuse one the player's two Energy banks
/// cannot pay for; charge that price; spend the player's lifetime run counter and derive the seed
/// from it; build the new <c>Run</c> via <c>Run.Rehydrate</c> — the only way to construct one — and
/// attach it through <see cref="HandlerInput.OpenRun"/>. All five refusals sit ahead of
/// <c>Player.BeginRun()</c>.
/// </para>
/// <para>
/// 🔒 <b>A run costs Energy, and this is the one place it is charged.</b> `10` §3 prices a run at
/// <c>EnergyTuning.RunCost</c> and that price is the whole of the game's session pacing — without
/// it Energy regenerates, overflows and is granted, and nothing anywhere ever spends it.
/// <c>EnergyMath.Spend</c> owns the draw (main bar first, then the Reserve for the remainder) and
/// its verdict is what this handler refuses on, so the screen's <c>HomeEnergyView.Shortfall</c> and
/// this refusal can never disagree about who can afford a run.
/// </para>
/// <para>
/// 🔒 <b>The price is charged LAST of the five gates, and deliberately.</b> A player who has not
/// unlocked a chapter and is also out of Energy is owed the sentence about the chapter: the launch
/// block only ever offers a stage the ladder has already opened, so <c>INSUFFICIENT_ENERGY</c>
/// arriving for a locked chapter would send the screen to a refill offer for a run that would be
/// refused at full Energy anyway. <c>HandlerResult.Reject</c> carries one reason and no payload, so
/// exactly one of the five can be named.
/// </para>
/// <para>
/// ⚠️ That ordering is defence in depth and nothing more: a refused command's working copy is
/// discarded by <c>GameRules.Apply</c>, which returns the caller's own slice, so a counter spent
/// before a refusal would be spent on an object nobody reads. Mutation-testing confirmed it — moving
/// <c>BeginRun()</c> above the gate reddens no test in the repository. Do not read the order below as
/// the thing that makes a refusal free; read it as the order that stays correct if it ever is.
/// </para>
/// <para>
/// 🔒 <b>The run opens with a perk draft pending, and that is this handler's doing.</b> `06` §1's
/// trigger is a won battle; the opening draft is a second trigger and it is deliberately the SAME
/// draft — three options, the same pool, the same guarantees, the same skip and reroll economy —
/// because a second kind of perk choice would be a second set of rules for the player to learn. It
/// is opened here rather than by a command of its own for the reason the loadout snapshot is taken
/// here: this is the only moment at which the run exists and nothing has happened in it yet.
/// <para>
/// The draft is spelled with <c>TileKind.Empty</c>, which is not one of the three battle tiles, and
/// that is the honest reading — no battle caused it. <c>Rules.Perks.CurrentDraft</c> asks the kind
/// only whether it is Elite or Boss, so a kind that is neither answers both with "no" and the draft
/// draws stage 1's ordinary band. Writing <c>Enemy</c> there would key the same band off a battle
/// the run never fought.
/// </para>
/// <para>
/// ⚠️ What follows from the category gate is the point of doing this at all: a run owning nothing can
/// only be offered the nine categories' base perks, so the opening draft is the choice of which
/// element the run is going to be about. <c>GameRules.Apply</c> already refuses every command but
/// the three draft ones while a draft is pending, so the choice is made before the first roll
/// without this handler saying anything about ordering.
/// </para>
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
/// The run's identity is <c>GameContext.AllocatedRunId</c> when the host issued one — the wire
/// regime, where the server allocates the id from <c>IIdGeneratorPort</c> — and otherwise
/// <see cref="MintRunId"/>'s deterministic derivation from the player id and run counter, which is
/// the in-process regime: nothing in <c>Core</c>/<c>Application</c> may call an ambient generator,
/// and a local host's runs are worth more reproducible than opaque.
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

    /// <summary>The income-attribution reason a run's Energy price is logged under.</summary>
    /// <remarks>
    /// Its own token, kept distinct from <c>energy_regen</c> and <c>daily_free_refill</c>: those two
    /// are where Energy comes from and this is the one place it goes, so conflating them would make
    /// the Energy budget unauditable in exactly the direction the pacing depends on.
    /// </remarks>
    internal const string RunCostReason = "run_cost";

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
    /// seed derivation could not hash, the tier's authored rung demands a clear or a Legend Level
    /// this player does not have, or the two Energy banks together cannot cover
    /// <c>EnergyTuning.RunCost</c>; otherwise accepted, with the new <c>Run</c> attached and the one
    /// <c>CurrencyChanged</c> that attributes the price.
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

        // 🔒 The price of a run, charged LAST of the five gates — see this type's remarks. The banks
        // read here are already accrued to now: GameRules.AdvanceTime regenerates before dispatch,
        // so this is what the player holds at the instant they tapped, not at their last command.
        var energyTuning = EnergyTuning.Read(input.Context.Content);
        var charge = EnergyMath.Spend(player.Energy, energyTuning.RunCost);

        if (!charge.IsAffordable)
        {
            return HandlerResult.Reject(RejectionReason.INSUFFICIENT_ENERGY);
        }

        var paid = player.SetEnergy(charge.Banks, energyTuning, RunCostReason);

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
            input.Context.AllocatedRunId ?? MintRunId(player.Id, runCounter),
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

        // 🔒 The opening draft. A run begins by choosing a perk, on the same three-option draft the
        // rest of the run uses — see this type's remarks for why it is opened here and why it is
        // spelled with a tile kind that is not a battle.
        run.Value.MarkDraftPending((int)TileKind.Empty, OpeningDraftStage);

        input.OpenRun(run.Value);

        return HandlerResult.Accept(paid);
    }

    /// <summary>
    /// The stage the opening draft draws its rarity band against — the first, which is where the run
    /// is standing.
    /// </summary>
    /// <remarks>
    /// Not a free choice: the run opens at the trailhead, one step before node 0 of stage 1, so the
    /// band the draft draws under is stage 1's. Reading it as anything else would hand a run its
    /// strongest offer before it had fought anything.
    /// </remarks>
    private const int OpeningDraftStage = 1;

    /// <summary>The in-process regime's deterministic <see cref="RunId"/> — see <see cref="Handle"/>'s remarks.</summary>
    private static RunId MintRunId(PlayerId playerId, long runCounter) =>
        new("RUN_" + playerId.Value + "_" + runCounter.ToString(CultureInfo.InvariantCulture));
}
