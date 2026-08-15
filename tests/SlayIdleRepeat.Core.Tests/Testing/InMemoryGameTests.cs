using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §6 — the harness's surface: construction, <c>CreatePlayer</c>, <c>Send</c>,
/// <c>State</c> and the event list.
/// </summary>
/// <remarks>
/// ⚠️ Several assertions here are about <c>CreatePlayer</c>'s own contract rather than about a rule.
/// That is legitimate where the constructor <em>is</em> the subject, but it is called out because
/// <c>game.State(player).Player.Energy</c> equalling what the harness just stored proves only that
/// the harness stored a number. Everything the <b>rules</b> decide is in
/// <c>InMemoryGameDayCycleTests</c>, and the determinism of the whole in
/// <c>InMemoryGameDeterminismTests</c>.
/// </remarks>
public sealed class InMemoryGameTests
{
    /// <summary>The two arguments with no sensible absence are refused at construction.</summary>
    [Fact]
    public void The_harness_refuses_a_null_content_set_and_a_null_clock()
    {
        Should.Throw<ArgumentNullException>(
            () => new InMemoryGame(null!, Harnesses.Seed, new VirtualClock(Harnesses.Start)))
            .ParamName.ShouldBe("content");

        Should.Throw<ArgumentNullException>(
            () => new InMemoryGame(TuningDocuments.Shipped, Harnesses.Seed, null!))
            .ParamName.ShouldBe("clock");
    }

/// <summary>
/// 🔒 The harness never loads: it takes a pre-built <see cref="ContentSnapshot"/>, and the one it
/// was handed is the one every command reads.
/// </summary>
/// <remarks>
/// The identity comparison is the point. Copying or re-stamping the snapshot would break `30` §3's
/// guarantee that a replayed command reproduces its outcome after a balance patch — a stamp the
/// harness invented is a stamp no adapter produced.
/// </remarks>
    [Fact]
    public void The_content_set_is_the_one_it_was_handed()
    {
        var content = TuningDocuments.Shipped;
        var game = new InMemoryGame(content, Harnesses.Seed, new VirtualClock(Harnesses.Start));

        game.Content.ShouldBeSameAs(content);
        game.Seed.ShouldBe(Harnesses.Seed);
        game.Clock.NowUtc.ShouldBe(Harnesses.Start);
    }

    /// <summary>
    /// 🔒 The defaults are the <b>absence</b> of a thing: no Plus, no kill switch thrown — and both
    /// are overridable, because `21` §9 sweeps 14 profiles.
    /// </summary>
    [Fact]
    public void The_session_defaults_are_no_Plus_and_no_kill_switch_thrown()
    {
        var free = Harnesses.New();

        free.Entitlements.HasPlus.ShouldBeFalse();
        free.Entitlements.ExpiresAtUtc.ShouldBeNull();
        free.Flags.PvpEnabled.ShouldBeTrue();
        free.Flags.PlusOfferEnabled.ShouldBeTrue();
        free.Flags.DisabledAdPlacements.ShouldBeEmpty();
        free.Flags.DisabledChapters.ShouldBeEmpty();

        var plus = new InMemoryGame(
            TuningDocuments.Shipped,
            Harnesses.Seed,
            new VirtualClock(Harnesses.Start),
            new Entitlements(hasPlus: true, expiresAtUtc: Harnesses.Start.AddDays(30)),
            new FeatureFlags(
                pvpEnabled: false, plusOfferEnabled: true, disabledAdPlacements: [], disabledChapters: ["CH_07"]));

        plus.Entitlements.HasPlus.ShouldBeTrue();
        plus.Flags.PvpEnabled.ShouldBeFalse();
        plus.Flags.DisabledChapters.ShouldContain("CH_07");
    }

/// <summary>
/// 🔒 A created player starts at the <b>authored</b> Legend Level floor holding <b>nothing</b> —
/// six wallet rows at zero and both Energy banks at zero.
/// </summary>
/// <remarks>
/// Zero is forced, not chosen: `30` §7 requires every currency movement to carry a
/// <c>CurrencyChanged</c>, so a player who <em>started</em> with a balance would hold currency no
/// row attributes. A starting grant needs a rule, a reason token and an event, and M4-10 owns
/// writing one.
/// <para>
/// ⚠️ The Legend Level assertion compares against the shipped floor of 1, which cannot tell a
/// tuning read from a literal —
/// <see cref="A_created_player_reads_its_Legend_Level_from_the_content_set"/> is the discriminating
/// half; this one pins the shipped value.
/// </para>
/// </remarks>
    [Fact]
    public void A_created_player_starts_at_the_authored_floor_holding_nothing()
    {
        var (game, player) = Harnesses.WithPlayer();
        var state = game.State(player).Player;

        state.LegendLevel.ShouldBe(ProgressionDocuments.ShippedLegendLevelMin);
        state.LegendXp.ShouldBe(0L);
        state.RunsStarted.ShouldBe(0L);

        foreach (var currency in Player.WalletCurrencies)
        {
            state.BalanceOf(currency).ShouldBe(
                0L,
                $"{currency} is zero because 30 §7 attributes every movement — a starting balance " +
                "would be currency no CurrencyChanged accounts for.");
        }

        state.Energy.ShouldBe(new EnergyBanks(0, 0));
        state.FtueBeat.ShouldBe(FtueBeat.B0);
        state.IsFtueComplete.ShouldBeFalse();
        state.LoginCalendarDay.ShouldBe(LoginCalendarTuning.FirstDay);
        state.LoginCalendarDayClaimed.ShouldBeFalse();

        game.State(player).Run.ShouldBeNull("nothing in M1 can start a run — START_RUN is Deferred to M3-15.");
        game.Events.ShouldBeEmpty("creating a player is not a command and produces no events.");
        game.CommandsIssued.ShouldBe(0L);
    }

/// <summary>
/// 🔒 A created player's Legend Level comes from the <b>content set</b>, not from a literal.
/// </summary>
/// <remarks>
/// The floor is authored at <c>progression.json#/legendLevel/min</c> (`07` §1.1) and is a 📐 tunable
/// (`21` §3.1). Asserting the read needs a content set whose floor is <b>not</b> the shipped one:
/// the shipped value is 1, and so is the literal anyone would have written. Five is arbitrary and
/// that is the point — no document authors it and no default produces it.
/// <para>
/// The Energy assertion beside it is why it matters: `10` §3's Max Energy is derived from the
/// Legend Level, so a hard-coded level hands every simulated player the wrong tank.
/// </para>
/// </remarks>
    [Fact]
    public void A_created_player_reads_its_Legend_Level_from_the_content_set()
    {
        const int UnshippedFloor = 5;

        UnshippedFloor.ShouldNotBe(
            ProgressionDocuments.ShippedLegendLevelMin,
            "the whole test is that this floor is NOT the shipped one.");

        var content = TuningDocuments.With(legendLevelMin: ContentValue.Number(UnshippedFloor));
        var game = Harnesses.New(content: content);
        var player = game.CreatePlayer();

        game.State(player).Player.LegendLevel.ShouldBe(UnshippedFloor);

        game.Clock.Advance(TimeSpan.FromDays(1));
        game.Send(player, Harnesses.BeginSession);

        game.State(player).Player.Energy.Energy.ShouldBe(
            EnergyTuning.Read(content).MaxEnergyAt(UnshippedFloor),
            "…and the level the harness read is the one 10 §3 derives Max Energy from, so getting it " +
            "from a literal would give every simulated player the wrong tank.");
    }

/// <summary>
/// 🔒 A created player is already <b>inside</b> the game day and week the clock is in.
/// </summary>
/// <remarks>
/// Left at a default, the first command would clear a period the player never played — and a
/// boundary <em>earlier</em> than the stored one makes <c>Player.RequireNotBefore</c> throw out of
/// <c>Apply</c>, a `30` §2.1 <b>P3</b> violation reported as a crash. Compared against
/// <c>GameCalendar</c>'s own answer, the one definition both the invariant and <c>AdvanceTime</c>
/// read.
/// </remarks>
    [Fact]
    public void A_created_player_sits_in_the_game_day_and_week_the_clock_is_in()
    {
        var midweek = new DateTimeOffset(2026, 8, 12, 9, 41, 8, TimeSpan.Zero);
        var (game, player) = Harnesses.WithPlayer(midweek);
        var state = game.State(player).Player;

        state.DailyPeriodStartUtc.ShouldBe(GameCalendar.GameDayStartAt(midweek));
        state.WeeklyPeriodStartUtc.ShouldBe(GameCalendar.GameWeekStartAt(midweek));
        state.EnergyAnchorUtc.ShouldBe(midweek);
        state.LastAppliedAtUtc.ShouldBe(midweek);

        state.WeeklyPeriodStartUtc.DayOfWeek.ShouldBe(
            DayOfWeek.Monday,
            "A2's game week starts Monday 05:00 UTC, and 2026-08-12 is a Wednesday — so the week " +
            "boundary is two days back, not the same day. A fixture that started on a Monday could " +
            "not tell the two apart.");
    }

/// <summary>Players are distinct, deterministic and independent.</summary>
/// <remarks>
/// The identities are a counter rather than a <c>Guid</c> — `14` §8.1 bans the latter in
/// <c>Core</c>, and `02` §2 hashes the player id into every <c>runSeed</c>. Independence is asserted
/// through a command rather than construction: two players sharing one slice is the defect a
/// dictionary keyed on the wrong thing produces.
/// </remarks>
    [Fact]
    public void Two_players_have_distinct_identities_and_independent_state()
    {
        var game = Harnesses.New();

        var first = game.CreatePlayer();
        var second = game.CreatePlayer("Ludwig the Unhurried");

        first.ShouldNotBe(second);

        // 🔒 In CREATION ORDER, not ignoreOrder: Players is an insertion-ordered list precisely
        // because Dictionary.Keys leaves the order unspecified, and `21` §9's sweep is the consumer
        // that would iterate it. An ignoreOrder comparison would pass over the shape this was
        // changed away from.
        game.Players.ShouldBe(new[] { first, second });

        game.State(second).Player.DisplayName.ShouldBe("Ludwig the Unhurried");
        game.State(first).Player.DisplayName.ShouldBe(first.Value);

        game.Clock.Advance(TimeSpan.FromHours(8));
        game.Send(first, Harnesses.BeginSession);

        game.State(first).Player.Energy.ShouldBe(
            new EnergyBanks(
                Harnesses.Tuning.MaxEnergyAt(ProgressionDocuments.ShippedLegendLevelMin), 0),
            "eight hours is exactly one full bar at the shipped rate — the acting player's half of " +
            "this claim deserves the same sharpness as the other player's.");

        game.State(second).Player.Energy.ShouldBe(
            new EnergyBanks(0, 0),
            "a command sent to one player must not roll another player's state forward — catch-up " +
            "is per slice, and a shared slice is what a dictionary keyed wrongly would produce.");
    }

    [Fact]
    public void A_blank_display_name_is_refused_and_null_generates_one()
    {
        var game = Harnesses.New();

        Should.Throw<ArgumentException>(() => game.CreatePlayer("   "))
            .ParamName.ShouldBe("displayName");

        Should.Throw<ArgumentException>(() => game.CreatePlayer(string.Empty))
            .ParamName.ShouldBe("displayName");

        var generated = game.CreatePlayer();

        game.State(generated).Player.DisplayName.ShouldBe(
            generated.Value,
            "'not blank' is true of almost every value; what null actually means is 'name it after " +
            "the id', and that is the claim worth pinning.");

        game.Players.ShouldBe(
            new[] { generated },
            "…and the two refusals created nothing — a guard that threw after adding the row would " +
            "leave a player nobody can name.");
    }

/// <summary>
/// 🔒 An id this harness never issued is a <b>defect</b>, not a rejection — at both doors.
/// </summary>
/// <remarks>
/// The line `30` §2.1's <b>P3</b> draws: a player asking for something they cannot have is a
/// <c>RejectionReason</c>; a caller naming a player that does not exist is miswired, and
/// <c>ILLEGAL_STATE</c> would tell the wrong person that a rule said no. <c>default(PlayerId)</c> is
/// asserted alongside a plausible id because it is what an unassigned field produces.
/// </remarks>
    [Fact]
    public void An_id_this_harness_never_issued_is_a_defect_at_both_doors()
    {
        var game = Harnesses.New();
        var stranger = new PlayerId("PLAYER_99999999");

        // 🔒 The fragment is pinned at all three doors, not only the first. `Send` can raise
        // InvalidOperationException from a SECOND place on this branch — GameRules' CommandKind.Run
        // loading defect, pinned by its own test below — so the exception type alone does not say
        // which guard fired, and the two have opposite fixes (S2).
        Should.Throw<InvalidOperationException>(() => game.State(stranger))
            .Message.ShouldContain("holds no player", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => game.Send(stranger, Harnesses.BeginSession))
            .Message.ShouldContain("holds no player", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => game.State(default))
            .Message.ShouldContain("holds no player", Case.Sensitive);
    }

/// <summary>
/// 🔒 `30` §2.1's <b>P3</b>, through the harness: a <c>Deferred</c> command is <b>refused</b> with
/// <c>ILLEGAL_STATE</c> and does not throw.
/// </summary>
/// <remarks>
/// Several rows, owned by different milestones, so the claim is about the mechanism rather than
/// whichever command the test picked. The state comparison is the half that matters: <b>P4</b>
/// makes a rejected command provably state-free, and a harness that stored the working copy would
/// silently hand the player regeneration they were refused.
/// <para>
/// 🔴 Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not <c>PlayerSnapshot</c>
/// record equality: a record compares its dictionary components by <b>reference</b>, so
/// <c>ToSnapshot().ShouldBe(before)</c> is trivially true of the wallet and both counter maps
/// whatever they hold. The player is driven for a day first and the gap crosses a boundary for the
/// same reason — otherwise there is nothing a stored catch-up could have moved.
/// </para>
/// </remarks>
    [Fact]
    public void A_deferred_command_is_refused_with_ILLEGAL_STATE_and_changes_nothing()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, days: 1, commandsPerDay: 2);

        var settled = game.State(player).Player;
        settled.DailyCount("begin_session").ShouldBe(
            1L,
            "there is a daily counter set, so a catch-up that was wrongly stored would have " +
            "something visible to wipe.");

        var before = CanonicalStateWriter.HashMetaCommandState(settled.ToSnapshot());
        var rowsFromTheDrivenDay = game.Events.Count;

        rowsFromTheDrivenDay.ShouldBeGreaterThan(
            0,
            "the driven day produced rows, so 'the refusals added none' is a comparison against " +
            "something rather than two zeroes (S3).");

        // More than a day, so every refused command below crosses a 05:00 UTC boundary as well as
        // several regeneration intervals. Both are things AdvanceTime would move if the working copy
        // survived a rejection.
        game.Clock.Advance(TimeSpan.FromHours(25));

        GameCommand[] deferred =
        [
            new SkipFtueCommand(),
            new EquipCommand("ITEM_1", "WEAPON"),
            new RespecCommand(),
            new ClaimCalendarCommand(),
            new SpinWheelCommand(),
        ];

        foreach (var command in deferred)
        {
            var result = game.Send(player, command);

            result.Accepted.ShouldBeFalse($"{command.GetType().Name} is a Deferred row.");
            result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
            result.Events.ShouldBeEmpty("a refused command changed nothing, so it logs nothing.");
        }

        CanonicalStateWriter.HashMetaCommandState(game.State(player).Player.ToSnapshot()).ShouldBe(
            before,
            "twenty-five hours passed, a game day turned over and five commands were refused; a " +
            "rejected command discards the working copy AND the catch-up (30 §2.1's P4), so nothing " +
            "moved — not the banks, not the anchor, not LastAppliedAtUtc, and not the day's counter.");

        game.Events.Count.ShouldBe(
            rowsFromTheDrivenDay,
            "exactly the rows the DRIVEN day produced, and nothing from the five refusals — a " +
            "refused command has nothing to append to 14 §7.1's economy log.");

        game.CommandsIssued.ShouldBe(7L, "a refused command is still a command that was issued.");
    }

/// <summary>
/// 🔒 A <c>CommandKind.Run</c> row still <c>Deferred</c> is <c>CommandKind.Run</c> and the harness
/// carries no run, so it is a <b>loading defect</b> — an exception, not <c>ILLEGAL_STATE</c>.
/// </summary>
/// <remarks>
/// `30` §4.1 makes loading the right slice the Application layer's job and `14` §16.2's
/// <c>RUN_NOT_FOUND</c> is a transport value <c>Apply</c> may not return. It proves too that the
/// harness passes the slice straight through rather than pre-screening it.
/// <para>
/// ⚠️ <b>M3-15 CORRECTED THIS TEST'S OWN EXAMPLE.</b> It used to probe with <c>START_RUN</c> — the
/// one <c>CommandKind.Run</c> row whose whole job is to create the <c>Run</c> this guard would
/// otherwise demand. M3-15 gave that row <c>CommandRegistration.OpensRun = true</c>, the one-row
/// exemption from exactly this guard, so <c>START_RUN</c> now SUCCEEDS on the harness's run-less
/// slice — see <see cref="A_START_RUN_command_succeeds_on_the_harnesss_run_less_slice_with_no_harness_change"/>
/// for the positive claim this test used to be the negative half of. This probes two rows that stay
/// <c>Deferred</c> instead, of two different shapes (no payload, and one payload field), so the
/// claim is still about the KIND rather than about a row that no longer demonstrates it.
/// </para>
/// </remarks>
    [Fact]
    public void A_run_command_with_no_run_in_the_slice_is_a_defect_the_harness_does_not_soften()
    {
        var (game, player) = Harnesses.WithPlayer();
        var before = game.State(player);

        var defect = Should.Throw<InvalidOperationException>(
            () => game.Send(player, new RollDiceCommand()));

        defect.Message.ShouldContain("ROLL_DICE", Case.Sensitive);
        defect.Message.ShouldContain("carries no Run", Case.Sensitive);

        // 🔒 …and the throw left the harness untouched. Send increments its counters and appends the
        // events AFTER Apply returns, so none of it runs — but "it does not run" and "nothing
        // asserts that it does not run" are different states, and the clock's own guard is pinned
        // this way one file over.
        game.CommandsIssued.ShouldBe(0L);
        game.Events.ShouldBeEmpty();
        game.State(player).ShouldBeSameAs(before);

        // 🔒 AND IT IS NOT ONLY ROLL_DICE. Every CommandKind.Run row but START_RUN hits the same
        // guard, because the harness's slice never carries a Run. Driven over a second row, of a
        // different shape (a payload field), so the claim is about the KIND rather than about the
        // row that was picked.
        Should.Throw<InvalidOperationException>(() => game.Send(player, new ChooseForkCommand(0)))
            .Message.ShouldContain("CHOOSE_FORK", Case.Sensitive);
    }

/// <summary>
/// 🔒 M3-15's positive claim, over the real harness: <c>START_RUN</c> now succeeds on the run-less
/// slice every other <c>CommandKind.Run</c> row still throws on — and <c>InMemoryGame</c> required
/// <b>zero</b> changes to reach it, exactly as the M1 finding this task settles said it would not.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why zero changes was even plausible.</b> <c>InMemoryGame.CreatePlayer</c> already builds a
/// <c>WorldSlice(player, null)</c> for a fresh player — the M1 finding's own words, "(player, null)
/// is already right" — so the harness was never the thing standing between <c>START_RUN</c> and a
/// caller. What stood in the way was <c>GameRules.Execute</c>'s own guard, throwing before dispatch
/// on every <c>CommandKind.Run</c> row including this one; M3-15 exempted this one row
/// (<c>CommandRegistration.OpensRun</c>) rather than opening an injection door on the harness — the
/// wrong answer this finding explicitly ruled out.
/// </para>
/// <para>
/// This asserts the full shape: acceptance, a <c>Run</c> now present with the requested chapter and
/// tier, the trailhead position, no events (`30` §7 names none for a run's own creation), and the
/// player's lifetime run counter advanced by exactly one.
/// </para>
/// </remarks>
    [Fact]
    public void A_START_RUN_command_succeeds_on_the_harnesss_run_less_slice_with_no_harness_change()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.State(player).Run.ShouldBeNull("CreatePlayer's own slice — the shape START_RUN acts on.");

        var runsStartedBefore = game.State(player).Player.RunsStarted;

        var result = game.Send(player, new StartRunCommand(3, DifficultyTier.NORMAL));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run.ShouldNotBeNull();
        result.NewState.Run!.ChapterId.ShouldBe(3);
        result.NewState.Run.Tier.ShouldBe(DifficultyTier.NORMAL);
        result.NewState.Run.Position.ShouldBe(-1, "03 §1.1's virtual trailhead — one step before node 0.");
        result.NewState.Run.RngStreamPositions.ShouldBeEmpty();
        result.NewState.Player.RunsStarted.ShouldBe(runsStartedBefore + 1);

        game.State(player).Run.ShouldNotBeNull("Send persists the accepted result back onto the harness.");
    }

/// <summary>
/// 🔒 The event list is the assertion surface and cannot be written through.
/// </summary>
/// <remarks>
/// The same hole <c>Player.WalletCurrencies</c> and <c>GameRules.Stamp</c> each close: an
/// <c>IReadOnlyList&lt;T&gt;</c> that <em>is</em> a <c>List&lt;T&gt;</c> casts straight back. Worse
/// here than elsewhere — a test could pass by appending to its own evidence.
/// </remarks>
    [Fact]
    public void The_event_list_cannot_be_written_through()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromHours(8));
        game.Send(player, Harnesses.BeginSession);

        game.Events.ShouldNotBeEmpty();

        // 🔒 ShouldBeAssignableTo rather than `as … ?.`: a null-conditional swallows the assertion
        // entirely when the cast fails, so an Events that stopped being an ICollection<T> would skip
        // this check rather than fail it (S1).
        game.Events.ShouldBeAssignableTo<ICollection<DomainEvent>>()!.IsReadOnly.ShouldBeTrue();

        (game.Events as DomainEvent[]).ShouldBeNull("a bare array casts back and is writable.");
        (game.Events as List<DomainEvent>).ShouldBeNull("a bare List casts back and is writable.");

        // …and the same for Players, which M1-11's review turned from Dictionary.Keys — whose order
        // the BCL leaves unspecified — into an insertion-ordered list behind a read-only wrapper.
        game.Players.ShouldBeAssignableTo<ICollection<PlayerId>>()!.IsReadOnly.ShouldBeTrue();
        (game.Players as List<PlayerId>).ShouldBeNull();
    }

/// <summary>
/// 🔒 The event list is <b>live</b>, and accumulates in command order across commands.
/// </summary>
/// <remarks>
/// The <c>Sequence</c> assertion pins what the ordinal means: a position within <b>one</b>
/// <c>Apply</c> call's list, from 1 — not a running counter across the simulation. Renumbering would
/// break `14` §7.1's economy log and `14` §2.4's animation script at once.
/// </remarks>
    [Fact]
    public void The_event_list_accumulates_in_command_order_and_keeps_each_commands_own_sequence()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromHours(8));
        var first = game.Send(player, Harnesses.BeginSession);

        game.Clock.Advance(TimeSpan.FromDays(1));
        var second = game.Send(player, Harnesses.BeginSession);

        game.Events.Count.ShouldBe(first.Events.Count + second.Events.Count);
        game.Events.Take(first.Events.Count).ShouldBe(first.Events);
        game.Events.Skip(first.Events.Count).ShouldBe(second.Events);

        // 🔒 Both lists numbered exactly, not a `>= 1` predicate over the accumulation: each
        // command's list is numbered 1..n on its own — Apply stamps within one result, not across
        // the simulation (30 §7) — and a running counter would satisfy any weaker check.
        first.Events.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, first.Events.Count));
        second.Events.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, second.Events.Count));

        first.Events.ShouldNotBeEmpty("…and neither range is empty (S3).");
        second.Events.ShouldNotBeEmpty();
    }

/// <summary>
/// 🔒 The harness carries the session forward: command <c>n + 1</c> starts where <c>n</c> left off.
/// </summary>
/// <remarks>
/// <b>P4</b> makes <c>Apply</c> return a <em>new</em> slice, so a harness re-sending against the
/// slice it started with would run a first command N times — the one shape that cannot tell "grants
/// once per game day" from "grants on every command". <c>ShouldNotBeSameAs</c> plus the advancing
/// timestamp, because "it stored something" and "it stored the result" are different claims.
/// </remarks>
    [Fact]
    public void The_slice_the_harness_holds_is_the_one_Apply_returned()
    {
        var (game, player) = Harnesses.WithPlayer();
        var initial = game.State(player);

        game.Clock.Advance(TimeSpan.FromHours(1));
        var result = game.Send(player, Harnesses.BeginSession);

        result.Accepted.ShouldBeTrue();
        game.State(player).ShouldBeSameAs(result.NewState);
        game.State(player).ShouldNotBeSameAs(initial);
        game.State(player).Player.LastAppliedAtUtc.ShouldBe(game.Clock.NowUtc);
        game.CommandsIssued.ShouldBe(1L);
    }

    /// <summary>A null command is a caller defect.</summary>
    [Fact]
    public void A_null_command_is_refused()
    {
        var (game, player) = Harnesses.WithPlayer();

        Should.Throw<ArgumentNullException>(() => game.Send(player, null!))
            .ParamName.ShouldBe("command");
    }

/// <summary>
/// 🔒 `14` §8.2 — the generated <see cref="PlayerId"/> is the same string under every culture.
/// </summary>
/// <remarks>
/// `02` §2 hashes the player id into every <c>runSeed</c>, so an id rendering its counter
/// differently on a Swedish laptop would draw different boards from M3 onwards.
/// <para>
/// 🔒 <c>sv-SE</c> rather than <c>de-DE</c>: German renders a negative integer with an ordinary
/// hyphen, Swedish with U+2212. The first assertion re-establishes that the runtime actually
/// <em>has</em> a Swedish culture — under globalization-invariant mode
/// <c>new CultureInfo("sv-SE")</c> silently returns the invariant one and everything below would
/// hold over nothing.
/// </para>
/// </remarks>
    [Fact]
    public void A_generated_player_id_reads_identically_under_any_culture()
    {
        var swedish = new System.Globalization.CultureInfo("sv-SE");

        (-1).ToString(swedish).ShouldNotBe(
            (-1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a Swedish culture.");

        var invariantId = Ids(System.Globalization.CultureInfo.InvariantCulture);
        var swedishId = Ids(swedish);

        swedishId.ShouldBe(invariantId);

        invariantId.ShouldBe(
            new[] { "PLAYER_00000001", "PLAYER_00000002", "PLAYER_00000003" },
            "…and the ids are what they look like: a zero-padded counter, so the sequence is stable " +
            "and readable rather than merely equal to itself.");

        static string[] Ids(System.Globalization.CultureInfo culture)
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;

            try
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;

                var game = Harnesses.New();

                return [game.CreatePlayer().Value, game.CreatePlayer().Value, game.CreatePlayer().Value];
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
