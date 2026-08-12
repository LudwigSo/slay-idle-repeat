using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `30` §2.3 — <b>the day's draw seed</b>: <c>BEGIN_SESSION</c>'s <c>CommandSeed</c>, the seam the
/// quest slate (`19` B) and the Daily shop block (`10` §5.1) will both be drawn from.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The two draws are deferred to M4-09 and this file does not pretend otherwise.</b>
/// <c>content/quests/</c> is empty, no quest schema exists, the Daily-shop offer model is M4-09's,
/// and — the half that is easiest to miss — `14` §8.1's stream registry is complete for the <b>run</b>
/// streams and carries <em>no row</em> for either draw, while saying a system needing randomness
/// <em>"draws from one of these streams or gets a new row here"</em>. Three inventions would be
/// needed, which steering <b>S6</b> forbids. Both are <c>GapRegister</c> entries
/// (<c>QuestSlate</c>/M4-09, <c>DailyShopStock</c>/M4-09) whose reasons M1-09 rewrote to say exactly
/// this.
/// </para>
/// <para>
/// 🔒 <b>So what is tested is the SEAM</b>: that <c>BEGIN_SESSION</c> demands its seed, that the
/// regime it reaches is deterministic in that seed and sensitive to it, and — the claim that matters
/// most for `14` §8.1 — that entering it moves <b>no run stream position</b>. The algebra itself is
/// <c>MetaDrawScopeTests</c>'.
/// </para>
/// <para>
/// ⚠️ <b>Determinism is asserted over the whole <c>Apply</c>, not over the scope.</b> A scope test
/// proves the hash; only a command test proves the <em>handler</em> reaches that scope and nothing
/// else — a handler that quietly drew from <c>Player.Id</c> or from the clock would pass every scope
/// test in the repository.
/// </para>
/// </remarks>
public sealed class BeginSessionDrawSeamTests
{
    /// <summary>
    /// 🔒 `14` §2.3 marks <c>BEGIN_SESSION</c> ⚄, so a context with <b>no</b> <c>CommandSeed</c> is a
    /// <b>defect</b> — not a rejection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same line `30` §2.1's <b>P3</b> draws for a run command whose slice carries no run: an
    /// exception out of <c>Apply</c> means the caller or the domain is wrong, never that the player
    /// asked for something they cannot have. A ⚄ row arriving unseeded is a miswired composition root.
    /// </para>
    /// <para>
    /// 🔒 <b>It is asserted on the message fragment, not only on the type</b> (steering <b>S2</b>).
    /// Several things in <c>Apply</c> throw <c>InvalidOperationException</c> — a slice that will not
    /// round-trip, a handler that stamped its own sequence, a handler that wrote an RNG position —
    /// and a test pinning the type alone would pass while a completely different rule fired.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_BEGIN_SESSION_with_no_CommandSeed_is_a_defect_and_names_the_regime()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => GameRules.Apply(
            BeginSessions.Slice(),
            BeginSessions.Command,
            Worlds.Context with { NowUtc = BeginSessions.Morning }));

        thrown.Message.ShouldMatchWildcard("*CommandSeed*META*never invents entropy*BEGIN_SESSION*");
        thrown.Message.ShouldContain(
            "MISWIRED COMPOSITION ROOT",
            Case.Sensitive,
            "the fix is the host that built the context, not a rule and not the player.");
    }

    /// <summary>
    /// 🔒 The seed is demanded on the <b>first</b> call of the game day and not on the repeats — the
    /// day's draws happen once, so the seed is needed once.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the one place the ⚄ contract and the idempotence rule interact, and the interaction
    /// is worth pinning rather than discovering at M4-09: a second <c>BEGIN_SESSION</c> that still
    /// demanded a seed would make `30` §2.3's "succeeds as a no-op" conditional on the host, and a
    /// client sending its day-boundary <c>BEGIN_SESSION</c> twice would get a 500 rather than a
    /// no-op.
    /// </remarks>
    [Fact]
    public void The_repeat_call_of_the_day_needs_no_seed_because_it_draws_nothing()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());

        // ⚠️ One minute later, which is INSIDE the regeneration interval on purpose: an accrual would
        // put an energy_regen row in the list and make the emptiness assertion below a statement
        // about the catch-up rather than about this handler.
        var second = Should.NotThrow(() => GameRules.Apply(
            first.NewState,
            BeginSessions.Command,
            Worlds.Context with { NowUtc = BeginSessions.Morning.AddMinutes(1) }));

        second.Accepted.ShouldBeTrue(
            "30 §2.3's no-op 'succeeds'. It draws nothing, so it needs no seed — and the ⚄ contract " +
            "is about the command's draws, not about its wire name.");
        second.Events.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ determinism in the seed

    /// <summary>
    /// 🔒 Two applications of one seed produce one state, byte for byte — asserted on the
    /// <c>stateHash</c>, which is `14` §16.6's canonical encoding of the whole aggregate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hash rather than a field-by-field comparison, because the claim is about <b>everything</b>
    /// the command wrote: a draw that landed in some field this test forgot to name would be exactly
    /// the non-determinism the assertion is for.
    /// </para>
    /// <para>
    /// ⚠️ <b>Today this is a guard against AMBIENT non-determinism — a clock read, a
    /// <c>Guid.NewGuid()</c>, a <c>Random.Shared</c> — and not yet a claim about the seed</b>, and
    /// the M1-09 review was right to ask. Its sibling
    /// <see cref="Two_different_CommandSeeds_address_different_draws"/> asserts the hashes are equal
    /// for two <em>different</em> seeds, because nothing is drawn yet, which makes "equal for the same
    /// seed" trivially true as well. Both invert together at M4-09: on the commit that lands the
    /// draws, this case starts distinguishing "deterministic in the seed" from "the seed is ignored",
    /// and that one starts requiring two slates. It is kept rather than folded in because the ambient
    /// guard is worth having on its own — `14` §8.1's ban on <c>System.Random</c> and
    /// <c>DateTime.Now</c> is enforced by a grep, and a grep does not see a handler that read
    /// <c>Player.Id.GetHashCode()</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_same_CommandSeed_produces_the_same_state()
    {
        const ulong seed = 0xABCD_EF01_2345_6789UL;

        var left = BeginSessions.Send(BeginSessions.Slice(), commandSeed: seed);
        var right = BeginSessions.Send(BeginSessions.Slice(), commandSeed: seed);

        CanonicalStateWriter.HashMetaCommandState(left.NewState.Player.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.HashMetaCommandState(right.NewState.Player.ToSnapshot()),
            "14 §8.1's meta regime is Hash64(CommandSeed, s, i) and nothing else — same seed, same " +
            "state. 14 §16.3 replays a resubmitted command's STORED outcome, so a meta draw that " +
            "moved between two applications of one seed could never be reproduced for a bug report.");
    }

    /// <summary>
    /// 🔒 …and the seed <b>reaches</b> the domain: two different seeds address different draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This test cannot assert differing state today, and saying so is the honest form of it</b>
    /// (steering <b>S1</b>): nothing is drawn yet, so the two states are identical and always will be
    /// until M4-09. Asserting "the states differ" would be a test that must fail, and asserting "the
    /// states are the same" would be an assertion true of a handler that ignored the seed entirely —
    /// which is the very defect this seam exists to prevent.
    /// </para>
    /// <para>
    /// So the claim is made where it is decidable: at the scope the handler actually reaches. Two
    /// seeds, one stream, one draw index — different values. When M4-09 lands the draws, the state
    /// assertion becomes available and belongs here; the <c>GapRegister</c> entries name it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_different_CommandSeeds_address_different_draws()
    {
        const ulong left = 0xABCD_EF01_2345_6789UL;
        const ulong right = 0xABCD_EF01_2345_678AUL;

        new MetaDrawScope(left).Stream(RngStreams.Drops).NextUInt().ShouldNotBe(
            new MetaDrawScope(right).Stream(RngStreams.Drops).NextUInt(),
            "if two seeds one bit apart drew the same value, 'the day's draw seed' would be " +
            "decoration and every player's quest slate would be the same slate.");

        // …and the two states really are identical today, which is what makes the paragraph above
        // an honest statement of the limit rather than an excuse.
        CanonicalStateWriter.HashMetaCommandState(
                BeginSessions.Send(BeginSessions.Slice(), commandSeed: left).NewState.Player.ToSnapshot())
            .ShouldBe(
                CanonicalStateWriter.HashMetaCommandState(
                    BeginSessions.Send(BeginSessions.Slice(), commandSeed: right).NewState.Player.ToSnapshot()),
                "NOTHING IS DRAWN YET — the quest slate and the Daily shop block are M4-09's " +
                "(GapRegister: QuestSlate, DailyShopStock). When they land, this assertion INVERTS: " +
                "two seeds must then produce two slates. It is written down rather than omitted so " +
                "that the day it starts failing is the day it is supposed to.");
    }

    // ------------------------------------------------------------------ 14 §8.1 · the other regime

    /// <summary>
    /// 🔒 <b>No run stream position moves.</b> The meta regime is a different regime, and a
    /// <c>BEGIN_SESSION</c> sent mid-run must not touch the run's `14` §8.1 counters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why this is the sharpest claim in the file.</b> `14` §8.1 makes <em>the persisted stream
    /// position the draw counter</em>. A meta command that advanced one would consume a draw the run
    /// can never see again — the board, the dice or the drops would silently skip an index — and
    /// nothing would throw: the numbers would still look random and the run would simply replay
    /// differently for the rest of its life. `14` §13's client/server parity test would then be
    /// comparing two universes and reporting on neither.
    /// </para>
    /// <para>
    /// ⚠️ The slice deliberately carries a <b>run with non-zero positions</b>. A run at all-zero
    /// counters would make "the positions did not move" true of a command that reset them, and a
    /// slice with no run at all would make the assertion unreachable — a meta command is dispatched
    /// perfectly happily with a run in the slice (a player can open the shop without leaving), which
    /// is exactly why <c>GameRules.FoldRngPositions</c> runs its check on meta commands too.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_BEGIN_SESSION_mid_run_moves_no_run_stream_position()
    {
        var positions = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [RngStreams.Board] = 8,
            [RngStreams.Dice] = 12,
            [RngStreams.Drops] = 3,
        };

        var run = Worlds.NewRun(RunSnapshots.With(rngStreamPositions: positions));

        var result = BeginSessions.Send(BeginSessions.Slice(run: run));

        result.Accepted.ShouldBeTrue("a meta command is legal with a run in the slice.");
        result.Events.ShouldNotBeEmpty("positive evidence the daily block ran: the refill was paid.");

        result.NewState.Run.ShouldNotBeNull();
        result.NewState.Run!.RngStreamPositions.ShouldBe(
            positions,
            "30 §3 puts out-of-run draws on GameContext.CommandSeed with NO persisted counter. A meta " +
            "command that moved a run stream would consume a draw the run can never see again, and " +
            "the run would replay differently for the rest of its life with nothing going red.");
    }

    /// <summary>
    /// 🔒 A <c>CommandKind.Meta</c> handler that writes the run <b>at all</b> is a defect — not only
    /// one that moves its `14` §8.1 counters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Found by M1-09's architecture review, and it is the "what can a future handler do that
    /// nothing would catch" question answered.</b> Until <c>Core/Handlers/</c> had an occupant the
    /// hole was unreachable, and <c>FoldRngPositions</c> guards <em>only</em> the stream positions —
    /// its own message says a meta command "may READ the run it was handed mid-run and must never
    /// move its counters", which left Gold, HP, Position and the per-run ad uses writable. Worse
    /// silently: <c>MarkApplied</c> deliberately does not stamp <c>Run.LastAppliedAtUtc</c> for a meta
    /// command, so the run would have come back changed while its own `14` §16.3 timestamp said
    /// nothing had happened to it.
    /// </para>
    /// <para>
    /// ⚠️ Driven through the <c>internal</c> <c>GameRules.Execute</c> door with a fixture handler,
    /// because the shape must <b>never</b> be committed to the production table — the same
    /// construction <c>GameRulesRngTests</c> uses for the hand-written-position case, and the reason
    /// <c>Execute</c> takes its table as a parameter at all.
    /// </para>
    /// <para>
    /// 🔒 The message is pinned, not just the type (steering <b>S2</b>): a meta handler that wrote a
    /// stream position trips the <em>other</em> guard, and the two send a reader to different files.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_meta_handler_that_writes_the_run_is_a_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                // Not a counter — run Gold, which nothing else was watching.
                input.Run.MoveCurrency(CurrencyId.GOLD, 500, "a_meta_command_had_no_business_here");
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldMatchWildcard("*CommandKind.Meta*WROTE THE RUN*");
        thrown.Message.ShouldContain(
            "ownership defect",
            Case.Sensitive,
            "…and it is named as an OWNERSHIP defect rather than a determinism one, because the " +
            "hand-written-position guard already owns the determinism half and the fixes differ.");
    }

    /// <summary>
    /// 🔒 …and a meta handler that only <b>reads</b> the run is fine, so the guard above is not
    /// "a meta command may not be handed a run".
    /// </summary>
    /// <remarks>
    /// The negative case, and it is load-bearing: `14` §2.3 dispatches meta commands with a run in
    /// the slice on purpose — a player can open the shop without leaving — and a guard that refused
    /// the <em>read</em> would make <c>HandlerInput.Run</c> unusable for the thing it exists for.
    /// </remarks>
    [Fact]
    public void A_meta_handler_that_only_reads_the_run_is_fine()
    {
        long seenGold = -1;

        var result = GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                seenGold = input.Run.Gold;
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        seenGold.ShouldBeGreaterThanOrEqualTo(0, "the handler really did read the run.");
    }

    /// <summary>
    /// 🔒 A <c>CommandKind.Run</c> handler that reaches for the <b>meta</b> draw regime is a defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 The mirror of <c>HandlerInput.Rng</c>'s guard, and the second hole M1-09's architecture
    /// review found. A run handler drawing from <c>GameContext.CommandSeed</c> would open streams at
    /// index 0 with <b>no persisted counter</b>, and nothing else in the repository would notice:
    /// <c>FoldRngPositions</c> looks for a <em>moved</em> position and would see none, and
    /// <c>DeterministicRng_is_constructed_only_inside_Core_Rng</c> is satisfied because the
    /// construction happens inside <c>Core/Rng/</c>. The run would replay differently for the rest of
    /// its life with every suite green — the exact failure `14` §8.1's counter model exists to make
    /// impossible.
    /// </para>
    /// <para>
    /// ⚠️ The fixture context carries a seed, so the refusal below is about the <b>kind</b> and not
    /// about a missing seed. Those are opposite defects with opposite fixes (steering <b>S2</b>) and
    /// the messages are pinned apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_handler_that_reaches_for_the_meta_regime_is_a_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => GameRules.Execute(
            // ⚠️ The command parameter is named rather than discarded: `_` would bind the discard to
            // the parameter, and `_ = input.MetaDraws` would then be an assignment to it.
            Worlds.RunTable((Worlds.RunFixtureCommand command, HandlerInput input) =>
            {
                _ = command;
                _ = input.MetaDraws;
                return HandlerResult.Accept();
            }),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Drawing(BeginSessions.Seed)));

        thrown.Message.ShouldMatchWildcard("*CommandKind.Run*EXCLUSIVE*HandlerInput.Rng*");
        thrown.Message.ShouldNotContain(
            "MISWIRED COMPOSITION ROOT",
            Case.Sensitive,
            "this is a handler in the wrong regime, not a host that forgot the seed — the context " +
            "here HAS one. Two defects, two fixes, two messages.");
    }

    /// <summary>
    /// 🔒 …and the run's `14` §16.3 sliding TTL does not move either, which is the same asymmetry
    /// M1-05 added a second timestamp for.
    /// </summary>
    /// <remarks>
    /// A meta command must not keep a run alive because its owner opened the shop — or, here, because
    /// their client sent its start-of-day <c>BEGIN_SESSION</c>. It is asserted beside the stream
    /// positions because both are "what a meta command may not touch on a run", and a reader looking
    /// for one will look for the other.
    /// </remarks>
    [Fact]
    public void A_BEGIN_SESSION_mid_run_does_not_slide_the_runs_TTL()
    {
        var run = Worlds.NewRun();
        var before = run.LastAppliedAtUtc;

        var result = BeginSessions.Send(BeginSessions.Slice(run: run), BeginSessions.Morning.AddHours(3));

        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(
            before,
            "14 §16.3's 48-hour run TTL slides on RUN commands only. Player.LastAppliedAtUtc advances " +
            "on every accepted command, which is precisely why M1-05 put a second timestamp on Run.");

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(
            BeginSessions.Morning.AddHours(3),
            "…while the player's does advance, so the assertion above is not just observing that " +
            "nothing was written anywhere.");
    }
}
