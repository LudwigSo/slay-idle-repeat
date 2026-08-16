using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Testing;

/// <summary>
/// 🔒 <b>A player gets more than one run, and it is driven rather than asserted about.</b>
/// <c>BEGIN_SESSION</c> → <c>START_RUN</c> → <c>ABANDON_RUN</c> → <c>START_RUN</c>, on one
/// <see cref="InMemoryGame"/>, every step of it a command. There is no call to an aggregate mutator
/// anywhere in this file and no seam other than <c>game.Send(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why the first run is ABANDONED rather than won.</b> A command-driven run cannot reach a boss
/// today — the stage-boundary stall parks every run in stage 1, which is somebody else's repair and is
/// not merged. <c>AbandonRun.Handle</c> requires <c>RunPhase.InProgress</c>, which is exactly what a
/// freshly started run is, so <c>START_RUN</c> immediately followed by <c>ABANDON_RUN</c> reaches
/// <c>RunPhase.Ended</c> in two commands with no board traversal at all. Nothing here depends on how
/// far a run can travel.
/// </para>
/// <para>
/// ⚠️ This is a unit test in the domain suite, driving the in-memory harness in-process. There is no
/// integration tier in this repository and this is not one.
/// </para>
/// </remarks>
public sealed class InMemoryGameRunLifecycleTests
{
    /// <summary>The chapter both runs name. Chapter 1 is the authored floor.</summary>
    private const int Chapter = 1;

    /// <summary>
    /// 🔒 <b>The acceptance case.</b> Two runs, back to back, through commands only — and the second
    /// one is a genuinely different run rather than the first one re-phased.
    /// </summary>
    [Fact]
    public void A_second_run_starts_once_the_first_one_has_ended()
    {
        var (game, player) = Session();

        Accepted(game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN (first)");

        var first = game.State(player).Run;

        first.ShouldNotBeNull("START_RUN was accepted and attached no run.");
        first!.Phase.ShouldBe(RunPhase.InProgress, "a just-started run is playable.");

        var firstId = first.Id;
        var firstSeed = first.RunSeed;

        Accepted(game.Send(player, new AbandonRunCommand()), "ABANDON_RUN");

        game.State(player).Run!.Phase.ShouldBe(
            RunPhase.Ended, "ABANDON_RUN was accepted and the run is still open.");

        // Read between the two runs, which is the only moment the carry can be compared against.
        var heldBetweenRuns = Canonical(game.State(player).Player.Loadout.ToSnapshot());

        var second = game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL));

        second.Accepted.ShouldBeTrue(
            "the second START_RUN was refused " + second.Rejection + ", so a player still gets exactly " +
            "one run for the life of a slice. RUN_ALREADY_ENDED means the phase gate refuses every run " +
            "command on an ended run, START_RUN included — the gate never opened. ILLEGAL_STATE means " +
            "the gate DID let it through but the ended run was still sitting in the working slice, so " +
            "StartRun.Handle's already-active-run guard fired instead. The two are different bugs.");

        var opened = game.State(player).Run;

        opened.ShouldNotBeNull("the second START_RUN was accepted and attached no run.");
        opened!.Phase.ShouldBe(RunPhase.InProgress, "the second run came back unplayable.");

        opened.Id.ShouldNotBe(
            firstId, "the second run carries the first run's identity, so nothing new was opened.");

        opened.RunSeed.ShouldNotBe(
            firstSeed,
            "the second run committed the first run's seed, so it would replay the board the player " +
            "has already walked.");

        opened.RngStreamPositions.ShouldBeEmpty(
            "the second run started with draw counters already advanced, which a run that has drawn " +
            "nothing cannot have.");

        game.State(player).Player.RunsStarted.ShouldBe(
            2L, "two runs were opened, so the lifetime counter reads two.");

        // ⚠️ VACUOUS TODAY, and that is recorded rather than dressed up: the loadout is empty on both
        // sides because EQUIP is still a deferred row, and an empty loadout has exactly one canonical
        // encoding. It is written in the shape that will discriminate the day a slot can be filled —
        // canonical bytes rather than record equality, since LoadoutSnapshot holds an
        // IReadOnlyDictionary that compares by reference.
        Canonical(opened.StartingLoadout.ToSnapshot()).ShouldBe(
            heldBetweenRuns,
            "the loadout the second run froze at START_RUN is not the one the player was holding " +
            "between the two runs.");
    }

    /// <summary>
    /// 🔴 <b>The door is no wider at the harness tier either.</b> With the second run in progress, a
    /// third <c>START_RUN</c> is refused and the counter does not move.
    /// </summary>
    /// <remarks>
    /// The reason is pinned, not merely the refusal: <c>ILLEGAL_STATE</c> is
    /// <c>StartRun.Handle</c>'s already-active-run guard, and <c>RUN_ALREADY_ENDED</c> here would mean
    /// the phase gate had started treating a live run as a finished one.
    /// </remarks>
    [Fact]
    public void A_third_START_RUN_is_refused_while_the_second_run_is_still_being_played()
    {
        var (game, player) = Session();

        Accepted(game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN (first)");
        Accepted(game.Send(player, new AbandonRunCommand()), "ABANDON_RUN");
        Accepted(game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN (second)");

        var second = game.State(player).Run!;

        var third = game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL));

        third.Accepted.ShouldBeFalse(
            "a third run was opened on top of the run the player is still playing. The ended-run " +
            "exemption is for RunPhase.Ended and nothing else.");

        third.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the refusal came from somewhere other than StartRun.Handle's already-active-run guard.");

        game.State(player).Run!.Id.ShouldBe(
            second.Id, "the live run was replaced by a refused command's run.");

        game.State(player).Player.RunsStarted.ShouldBe(
            2L, "a refused START_RUN must not spend the lifetime run counter.");
    }

    // ═════════════════════════════════════════════════════════ fixtures

    /// <summary>A harness with one player, already through <c>BEGIN_SESSION</c>.</summary>
    private static (InMemoryGame Game, PlayerId Player) Session()
    {
        var game = Harnesses.New();
        var player = game.CreatePlayer();

        Accepted(game.Send(player, Harnesses.BeginSession), "BEGIN_SESSION");

        return (game, player);
    }

    private static byte[] Canonical(object snapshot) => CanonicalStateWriter.CanonicalBytes(snapshot);

    private static void Accepted(CommandResult result, string name) =>
        result.Accepted.ShouldBeTrue(
            name + " was refused " + result.Rejection + ". The lifecycle cannot go on without it.");
}
