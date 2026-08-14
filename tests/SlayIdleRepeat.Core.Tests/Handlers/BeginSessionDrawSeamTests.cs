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
/// 🔒 Both draws are deferred to M4-09: no quest schema exists, the Daily-shop offer model is M4-09's,
/// and `14` §8.1's stream registry carries <em>no row</em> for either. Three inventions would be
/// needed, which steering <b>S6</b> forbids. What is tested is the <b>seam</b> — that
/// <c>BEGIN_SESSION</c> demands its seed, that the regime it reaches is deterministic in and
/// sensitive to that seed, and that entering it moves <b>no run stream position</b>.
/// <para>
/// ⚠️ Determinism is asserted over the whole <c>Apply</c>, not over the scope: a scope test proves the
/// hash, but only a command test proves the <em>handler</em> reaches that scope and nothing else.
/// </para>
/// </remarks>
public sealed class BeginSessionDrawSeamTests
{
/// <summary>
/// 🔒 `14` §2.3 marks <c>BEGIN_SESSION</c> ⚄, so a context with <b>no</b> <c>CommandSeed</c> is a
/// <b>defect</b> — a miswired composition root, not a rejection.
/// </summary>
/// <remarks>
/// 🔒 Asserted on the message fragment, not only the type (steering <b>S2</b>): several things in
/// <c>Apply</c> throw <c>InvalidOperationException</c>, and a type-only test would pass while a
/// completely different rule fired.
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
/// 🔒 The seed is demanded on the <b>first</b> call of the game day and not on repeats — the day's
/// draws happen once, so the seed is needed once.
/// </summary>
/// <remarks>
/// ⚠️ The one place the ⚄ contract and the idempotence rule interact: a second <c>BEGIN_SESSION</c>
/// that still demanded a seed would make `30` §2.3's "succeeds as a no-op" conditional on the host,
/// and a client sending its day-boundary call twice would get a 500 rather than a no-op.
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
/// <c>stateHash</c>, `14` §16.6's canonical encoding of the whole aggregate.
/// </summary>
/// <remarks>
/// The hash rather than field-by-field, because the claim is about <b>everything</b> the command
/// wrote: a draw landing in a field this test forgot to name is exactly the non-determinism it is
/// for.
/// <para>
/// ⚠️ Today this guards <b>ambient</b> non-determinism — a clock read, a <c>Guid.NewGuid()</c>, a
/// <c>Random.Shared</c> — and not yet the seed, because nothing is drawn until M4-09. Kept separate
/// from <see cref="Two_different_CommandSeeds_address_different_draws"/> because the ambient guard is
/// worth having alone: `14` §8.1's ban is enforced by a grep, and a grep does not see a handler that
/// read <c>Player.Id.GetHashCode()</c>.
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
/// ⚠️ It cannot assert differing <em>state</em> today (steering <b>S1</b>): nothing is drawn until
/// M4-09, so "the states differ" would be a test that must fail and "the states are the same" would
/// be true of a handler that ignored the seed entirely. So the claim is made where it is decidable —
/// two seeds, one stream, one draw index, different values.
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
/// 🔒 <b>No run stream position moves.</b> A <c>BEGIN_SESSION</c> sent mid-run must not touch the
/// run's `14` §8.1 counters.
/// </summary>
/// <remarks>
/// 🔒 The persisted stream position <em>is</em> the draw counter, so a meta command that advanced one
/// would consume a draw the run can never see again — and nothing would throw. The run would simply
/// replay differently for the rest of its life, and `14` §13's parity test would be comparing two
/// universes.
/// <para>
/// ⚠️ The slice carries a run with <b>non-zero</b> positions: at all-zero, "the positions did not
/// move" is also true of a command that reset them.
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
/// 🔒 A <c>CommandKind.Meta</c> handler that writes the run <b>at all</b> is a defect — not only one
/// that moves its `14` §8.1 counters.
/// </summary>
/// <remarks>
/// 🔴 <c>FoldRngPositions</c> guards only the stream positions, leaving Gold, HP, Position and the
/// per-run ad uses writable. Worse silently: <c>MarkApplied</c> deliberately does not stamp
/// <c>Run.LastAppliedAtUtc</c> for a meta command, so the run would come back changed while its own
/// `14` §16.3 timestamp said nothing had happened to it.
/// <para>
/// ⚠️ Driven through the <c>internal</c> <c>GameRules.Execute</c> door with a fixture handler, because
/// the shape must never be committed to the production table. 🔒 The message is pinned, not just the
/// type: a meta handler that wrote a stream position trips the <em>other</em> guard.
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
/// 🔒 A meta handler that touches nothing is accepted over a run that <b>has watched an ad</b> — the
/// case the ownership guard used to reject, accusing a handler that had written nothing.
/// </summary>
/// <remarks>
/// ⚠️ The non-empty <c>AdUses</c> is the whole test. The guard compared two <c>RunSnapshot</c>
/// records, and a synthesized record compares its <c>IReadOnlyDictionary</c> components <b>by
/// reference</b>, while <c>Run.CopyAdUses</c> allocates a fresh map whenever it is non-empty — so two
/// <c>ToSnapshot()</c> calls on an untouched run were unequal the moment it held one ad use.
/// <para>
/// A player mid-run who had watched a single rewarded ad could send no meta command at all: a `30`
/// §2.1 <b>P3</b> violation for a state a correct composition root produces routinely. Every suite
/// stayed green because <c>CopyAdUses</c> short-circuits an <em>empty</em> map to a shared singleton,
/// which is the only case every other fixture builds.
/// </para>
/// <para>
/// 🔒 The fix compares `14` §16.6's canonical bytes, the one encoding whose contract is "two states
/// differing in anything encode differently".
/// </para>
/// </remarks>
    [Fact]
    public void A_meta_command_is_accepted_over_a_run_that_has_watched_an_ad()
    {
        var withAnAdWatched = Worlds.InARun(RunSnapshots.With(
            adUses: RunSnapshots.AdUses((Placement: "AD_REVIVE", Uses: 1L))));

        var result = GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            withAnAdWatched,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue(
            "the handler wrote nothing. An untouched run that happens to hold an ad use is not an " +
            "ownership defect, and refusing it strands every mid-run player who has watched one.");

        result.NewState.Run!.AdUseCount("AD_REVIVE").ShouldBe(
            1L,
            "…and the ad use survives the command untouched, so the guard is passing the run " +
            "through rather than being satisfied by something having reset it.");
    }

/// <summary>
/// 🔒 …and a meta handler that only <b>reads</b> the run is fine, so the guard above is not "a meta
/// command may not be handed a run".
/// </summary>
/// <remarks>
/// Load-bearing: `14` §2.3 dispatches meta commands with a run in the slice on purpose — a player can
/// open the shop without leaving — and a guard refusing the <em>read</em> would make
/// <c>HandlerInput.Run</c> unusable for the thing it exists for.
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
/// 🔴 A run handler drawing from <c>GameContext.CommandSeed</c> would open streams at index 0 with
/// <b>no persisted counter</b>, and nothing would notice: <c>FoldRngPositions</c> looks for a
/// <em>moved</em> position and sees none, and the construction-site rule is satisfied because it
/// happens inside <c>Core/Rng/</c>. The run would replay differently for life with every suite green.
/// <para>
/// ⚠️ The fixture context carries a seed, so the refusal is about the <b>kind</b> and not a missing
/// seed — opposite defects with opposite fixes, pinned apart by message.
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
/// 🔒 …and the run's `14` §16.3 sliding TTL does not move either.
/// </summary>
/// <remarks>
/// A meta command must not keep a run alive because its owner opened the shop — or here, because
/// their client sent its start-of-day <c>BEGIN_SESSION</c>. Asserted beside the stream positions
/// because both are "what a meta command may not touch on a run".
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
