using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>MG_CHEST_PICK</c>'s pity counter as <c>MINIGAME_SUBMIT</c> moves it: player-scoped, lifetime,
/// and untouched by the other three minigames.
/// </summary>
/// <remarks>
/// Separate from <c>MinigameSubmitTests</c>, which is about the legality gate and the reward table.
/// The counter is a different concern with a different scope, and the per-tile resolution map those
/// cases are written against is exactly the thing it must NOT be stored in.
/// </remarks>
public sealed class MinigameChestPickPityTests
{
    /// <summary>The counter id, formed the way production forms it. Never spelled here.</summary>
    private static string CounterKey =>
        LuckTuning.Read(LuckDocuments.LuckOnly())
            .CounterKey(SourceClass.MINIGAME, LuckDocuments.ShippedChestPickGuaranteeToken);

    private static WorldSlice Submit(WorldSlice state, string minigameId, int claimed = 0) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(minigameId, claimed), Worlds.Context).NewState;

    /// <summary>
    /// 🔒 A run seed whose chest pick draws a <b>miss</b> — a Bronze or Silver chest, not Gold.
    /// </summary>
    /// <remarks>
    /// Fixed rather than inherited from the fixture's default, and the choice is load-bearing twice
    /// over. A natural Gold resets the counter, so on a Gold-drawing seed every "the counter
    /// advanced" case below is unsatisfiable by a <em>correct</em> handler — and the forced-pick
    /// case would observe a Gold the guarantee never had to produce, proving nothing at all. The
    /// fixture's own default draws Gold, which is exactly that seed.
    /// <para>
    /// Both runs a case builds carry this seed and both draw at minigame stream index zero (a fresh
    /// run has resolved nothing), so every pick in this file is the same single deterministic draw.
    /// If a change to how the pick consumes its draw index ever turns that draw into a Gold, the
    /// answer is a new seed picked the same way — never a relaxed assertion.
    /// </para>
    /// </remarks>
    private const ulong MissingSeed = 1;

    /// <summary>
    /// A run standing on a fresh, unresolved tile at <paramref name="position"/>, whose player's
    /// chest-pick counter already stands at <paramref name="misses"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 The starting value is a parameter rather than always zero, and the cases below start
    /// mid-ladder on purpose: from zero, "unchanged" and "reset" are the same observation, so a
    /// handler that cleared this counter on every submission would be invisible to the negative
    /// control.
    /// </remarks>
    private static WorldSlice AtTile(int position, int misses = 0) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                pityCounters: PlayerSnapshots.Pity((CounterKey, misses)))),
            Worlds.NewRun(RunSnapshots.With(position: position, runSeed: MissingSeed)));

    private static int Counter(WorldSlice state) => state.Player.PityCounters.Get(CounterKey);

    // ------------------------------------------------------------------ the counter moves

    /// <summary>A chest pick moves the chest-pick counter, whichever tier it lands on.</summary>
    /// <remarks>
    /// Asserted as "no longer where it started" rather than "not zero": both legal outcomes of a
    /// pick move the counter off a mid-ladder value — a miss advances it, a natural gold resets it —
    /// while a handler that never touches it leaves the value standing. Asserting "not zero" from a
    /// zero start would instead be unsatisfiable for the one draw in three that rolls gold.
    /// </remarks>
    [Fact]
    public void A_chest_pick_moves_the_chest_pick_counter()
    {
        const int Standing = 1;

        Counter(Submit(AtTile(5, Standing), MinigameCatalogue.ChestPick)).ShouldNotBe(
            Standing,
            "24 §4.9's guarantee is unreachable if nothing ever moves its counter, and a counter " +
            "that never moves is invisible to every other case in this file.");
    }

    /// <summary>
    /// The other three minigames never move it. They are skill-scaled and carry no counter at all.
    /// </summary>
    /// <remarks>
    /// 🔒 The negative control. A handler that advanced the counter on every <c>MINIGAME_SUBMIT</c>
    /// would satisfy the case above and would make the gold chest farmable through the cheapest
    /// minigame on the board — the anti-farming question `24` §1.2 asks of every counter.
    /// <para>
    /// Started mid-ladder so that "untouched" is a different observation from "reset". Asserting a
    /// zero from a zero start is satisfied by a handler that <em>clears</em> the chest-pick counter
    /// on every submission — which is the same farming hole read from the other side, and worse,
    /// because it would also silently undo the one counter this file exists to protect.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(MinigameCatalogue.TimingBar)]
    [InlineData(MinigameCatalogue.MemoryRune)]
    [InlineData(MinigameCatalogue.DiceDuel)]
    public void The_other_three_minigames_never_move_the_chest_pick_counter(string minigameId)
    {
        const int Standing = 1;

        Counter(Submit(AtTile(5, Standing), minigameId)).ShouldBe(Standing);
    }

    // ------------------------------------------------------------------ the scope

    /// <summary>
    /// The counter lives on the player and survives the run it was advanced in.
    /// </summary>
    /// <remarks>
    /// 🔒 The scope claim, asserted through a <c>PlayerSnapshot</c> round-trip rather than through
    /// the live aggregate: the run-scoped alternative — a row in <c>Run.ResolvedMinigames</c> — would
    /// pass every in-run assertion above and lose the counter at the run boundary, which makes a
    /// four-miss guarantee unreachable for a player who meets one chest pick per run.
    /// </remarks>
    [Fact]
    public void The_counter_is_player_scoped_and_survives_the_run_that_advanced_it()
    {
        const int Standing = 1;

        var after = Submit(AtTile(5, Standing), MinigameCatalogue.ChestPick);
        var advanced = Counter(after);

        advanced.ShouldBe(
            Standing + 1,
            "the round trip below has to carry a value an EMPTY map could not answer with: Get " +
            "reads an absent counter as zero, so a snapshot that dropped the map entirely would " +
            "satisfy this case if the pick had reset the counter instead of advancing it. That is " +
            "what MissingSeed is for.");

        var rehydrated = Core.Model.Player.Rehydrate(after.Player.ToSnapshot(), Worlds.Context.Content);

        // 🔴 The failure message used to be `rehydrated.Error`, which Shouldly evaluates EAGERLY —
        // and Result.Error throws on a successful result by design, so this assertion could not pass
        // however correct the handler was. Corrected rather than worked around: the claim is that the
        // snapshot round-trips, and the diagnostic that made it unsatisfiable was never part of it.
        rehydrated.IsSuccess.ShouldBeTrue();
        rehydrated.Value.PityCounters.Get(CounterKey).ShouldBe(advanced);
    }

    /// <summary>Two chest picks in different runs accumulate on the same counter.</summary>
    /// <remarks>
    /// <para>
    /// The run is rebuilt between them — a new <c>RunSnapshot</c> at a different position, which is
    /// what a second run is to this handler — while the player carries over.
    /// </para>
    /// <para>
    /// ⚠️ <b>The premise both picks rest on — see <see cref="MissingSeed"/> — is that neither draws
    /// the gold tier.</b> A gold pick resets the counter to zero, and a lifetime counter sitting at
    /// zero is indistinguishable from a run-scoped one wiped at the run boundary: there is no
    /// formulation of this claim that survives a gold roll. The intermediate assertion below is
    /// where that premise is checked rather than assumed, so a seed that stopped missing reads as
    /// the premise failing instead of as the scope claim failing.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_chest_picks_across_two_runs_accumulate_on_one_counter()
    {
        const int Standing = 1;

        var first = Submit(AtTile(5, Standing), MinigameCatalogue.ChestPick);

        Counter(first).ShouldBe(
            Standing + 1,
            "the premise above: this pick misses the gold tier, so it advances rather than resets.");

        var nextRun = new WorldSlice(
            first.Player, Worlds.NewRun(RunSnapshots.With(position: 9, runSeed: MissingSeed)));

        var second = Submit(nextRun, MinigameCatalogue.ChestPick);

        Counter(second).ShouldBeGreaterThan(
            Counter(first),
            "24 §1.1: counters never reset on anything but their own guarantee. A counter that " +
            "started over every run would make the four-pick guarantee unreachable.");
    }

    // ------------------------------------------------------------------ the guarantee

    /// <summary>
    /// A pick standing on the last miss of the streak is <b>forced</b> onto the gold tier, where the
    /// identical pick with a cold counter is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven end to end rather than against the resolver, because the claim is about what the
    /// handler stores between picks: a resolver that decides correctly against a counter nothing
    /// persists guarantees nothing at all.
    /// </para>
    /// <para>
    /// 🔴 <b>The "this seed does not roll gold naturally" premise is now established IN the case,
    /// and the name no longer overstates the body.</b> This asserted only the gold outcome and the
    /// counter reset — both of which a <em>natural</em> gold satisfies — so nothing said the
    /// guarantee had fired rather than the draw (steering S2), and it was called "four consecutive
    /// picks" while making one. The cold-counter submission below is the same seed at the same
    /// position drawing the same index, so the counter is the <em>only</em> difference between the
    /// two halves and the gold in the second is attributable to nothing else.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_pick_that_completes_the_streak_is_forced_onto_the_gold_tier()
    {
        var natural = Submit(AtTile(5, 0), MinigameCatalogue.ChestPick);

        natural.Player.BalanceOf(CurrencyId.BEAST_FEED).ShouldBe(
            0L,
            "the premise, and the whole discriminating power of this case: on MissingSeed the pick " +
            "MISSES the gold tier, and the gold row is the only chest-pick outcome that pays Beast " +
            "Feed. If this seed ever starts rolling gold, the answer is a new seed picked the same " +
            "way — never a relaxed assertion below, which would leave the guarantee unpinned again.");

        natural.Player.PityCounters.Get(CounterKey).ShouldBe(
            1, "…and a miss advances the counter rather than satisfying it.");

        var forced = Submit(
            AtTile(5, LuckDocuments.ShippedMinigameChestPickN - 1), MinigameCatalogue.ChestPick);

        forced.Player.BalanceOf(CurrencyId.BEAST_FEED).ShouldBeGreaterThan(
            0L,
            "🔒 the TIER, not the counter movement that follows it. Same seed, same tile, same draw " +
            "index as the miss above — only the counter differs, so this gold is 24 §4.9's guarantee " +
            "and cannot be the draw. A handler that reset the counter without forcing the tier " +
            "satisfies the assertion below and guarantees the player nothing at all.");

        forced.Player.PityCounters.Get(CounterKey).ShouldBe(
            0,
            "the forced pick satisfies the guarantee, and satisfying a guarantee resets its counter.");
    }

    // ------------------------------------------------------------------ S24 regression guard

    /// <summary>
    /// The pending tile is still cleared as the last step of a submission.
    /// </summary>
    /// <remarks>
    /// A regression guard rather than a new claim: recording the resolution touches only the
    /// per-tile legality proxy, so a submission that stopped clearing the pending tile would leave
    /// <c>ROLL_DICE</c> unable to fire again for the rest of the run — and wiring a counter through
    /// this handler is exactly the kind of edit that drops a trailing statement.
    /// </remarks>
    [Theory]
    [InlineData(MinigameCatalogue.ChestPick)]
    [InlineData(MinigameCatalogue.TimingBar)]
    public void A_submission_still_clears_the_pending_tile(string minigameId)
    {
        var state = Worlds.InARun(
            RunSnapshots.OnPendingTile((int)Core.Rules.Board.TileKind.Minigame, linearIndex: 5, stage: 1)
                with { Position = 5 });

        var after = Submit(state, minigameId);

        after.Run!.ToSnapshot().PendingTileKind.ShouldBe(
            RunSnapshots.NoPendingTile,
            "the pending tile is cleared last, and nothing added to this handler may land after it.");
    }
}
