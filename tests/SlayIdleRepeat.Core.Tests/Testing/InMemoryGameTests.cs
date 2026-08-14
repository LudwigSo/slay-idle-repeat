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
/// ⚠️ <b>What this file may and may not claim.</b> Several assertions below are about
/// <c>CreatePlayer</c>'s own contract — the starting row it builds — and are therefore assertions
/// about the fixture rather than about a rule. That is legitimate <em>here</em>, where the
/// constructor is the subject, and it is called out because it is the failure mode `30` §6's harness
/// is most exposed to: <c>game.State(player).Player.Energy</c> equalling what the harness just
/// stored proves only that the harness stored a number. Everything the <b>rules</b> decide — accrual
/// across a boundary, idempotence per game day, the event list's contents and order — is asserted in
/// <c>InMemoryGameDayCycleTests</c>, and the determinism of the whole in
/// <c>InMemoryGameDeterminismTests</c>.
/// <para>
/// 🔒 <b>The fixture-only assertions, named exactly, so nobody mistakes one for a rule.</b>
/// <see cref="A_created_player_starts_at_the_authored_floor_holding_nothing"/> and
/// <see cref="A_created_player_sits_in_the_game_day_and_week_the_clock_is_in"/> assert
/// <c>CreatePlayer</c>'s own contract, which is legitimate because that method is the subject.
/// <see cref="The_session_defaults_are_no_Plus_and_no_kill_switch_thrown"/> is a property round trip
/// and <em>no more</em>: nothing in M1 reads <c>Entitlements</c> or <c>FeatureFlags</c>, and this
/// type exposes no <c>GameContext</c>, so "they reach the domain" is not assertable until the first
/// milestone whose handler reads one. ⚠️ The <b>Legend Level</b> half of the first of those is the
/// case M1-11's review caught: comparing against the shipped floor cannot tell a tuning read from a
/// literal, so <see cref="A_created_player_reads_its_Legend_Level_from_the_content_set"/> exists to
/// discriminate.
/// </para>
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
    /// was handed is the one every command reads (M1 kickoff decision 4).
    /// </summary>
    /// <remarks>
    /// The identity comparison is the point. A harness that copied, re-stamped or rebuilt the
    /// snapshot would break `30` §3's guarantee that a replayed command reproduces its original
    /// outcome after a balance patch — the stamp is what makes that true, and a stamp the harness
    /// invented is a stamp no adapter produced.
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
    /// <para>
    /// 🔒 <b>Zero is forced rather than chosen, and this test is where that ruling is pinned.</b>
    /// `30` §7 requires every currency movement to be attributed by a <c>CurrencyChanged</c> and
    /// `21` §8.3's <c>income_attribution.csv</c> is a query over those rows, so a player who
    /// <em>started</em> with a balance would hold currency no row attributes. A starting grant needs
    /// a rule, a reason token and an event, and the milestone that creates accounts (M4-10) owns
    /// writing one — inventing an amount here is exactly what steering <b>S6</b> forbids.
    /// </para>
    /// <para>
    /// ⚠️ The Legend Level assertion below compares against
    /// <c>ProgressionDocuments.ShippedLegendLevelMin</c>, and on its own that is <b>not</b> a proof
    /// that <c>CreatePlayer</c> reads the tuning — the shipped floor is 1, so replacing
    /// <c>legend.Minimum</c> with the literal <c>1</c> leaves it green (measured on M1-11's review).
    /// <see cref="A_created_player_reads_its_Legend_Level_from_the_content_set"/> is the assertion
    /// that discriminates; this one pins the shipped value.
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
    /// <para>
    /// `07` §1.1 authors the floor at <c>progression.json#/legendLevel/min</c> and `21` §3.1 makes
    /// every such number a 📐 tunable — <em>"a 📐 tunable number that is not in this directory is a
    /// bug"</em>. The only way to assert that <c>CreatePlayer</c> honours it is to hand the harness
    /// a content set whose floor is <b>not</b> the shipped one: a comparison against the shipped
    /// value cannot tell a read from a coincidence, because the shipped value is 1 and so is the
    /// literal anyone would have written.
    /// </para>
    /// <para>
    /// 🔒 Five is arbitrary and is the point — it is a number no document authors and no default
    /// would produce, so the only way the player arrives at it is by the tuning being read. The
    /// Energy assertion beside it is the consequence that makes it matter: `10` §3's Max Energy is
    /// derived from the Legend Level, so a harness that hard-coded the level would silently hand
    /// every simulated player the wrong tank.
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
    /// 🔒 A created player is already <b>inside</b> the game day and game week the clock is in.
    /// </summary>
    /// <remarks>
    /// This is not decoration. If <c>CreatePlayer</c> left the two period boundaries at a default,
    /// the very first command would clear a period the player never played — and worse, a boundary
    /// <em>earlier</em> than the stored one makes <c>Player.RequireNotBefore</c> throw out of
    /// <c>Apply</c>, which is a `30` §2.1 <b>P3</b> violation reported as a crash. Compared against
    /// <c>GameCalendar</c>'s own answer rather than a literal, because that is the one definition
    /// both the aggregate's invariant and <c>AdvanceTime</c>'s computation read.
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
    /// <c>Core</c>, and `02` §2 hashes the player id into every <c>runSeed</c>, so a random id would
    /// make the simulation irreproducible from M3 onwards. Independence is asserted through a
    /// command rather than through construction: two players sharing one slice is the defect a
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
    /// 🔒 An id this harness never issued is a <b>defect</b>, not a rejection — and both doors say so.
    /// </summary>
    /// <remarks>
    /// The line `30` §2.1's <b>P3</b> draws: a player asking for something they cannot have is a
    /// <c>RejectionReason</c>; a caller naming a player that does not exist is a miswired caller,
    /// and answering it with <c>ILLEGAL_STATE</c> would tell the wrong person that a rule said no.
    /// <c>default(PlayerId)</c> is asserted alongside a plausible-looking id because it is the one an
    /// unassigned field produces.
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
    /// <c>ILLEGAL_STATE</c> and does not throw — forty-eight of `14` §2.3's forty-nine rows today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven over several rows rather than one, and over rows owned by different milestones, so
    /// "the deferral answers a value" is a claim about the mechanism rather than about whichever
    /// command the test happened to pick.
    /// </para>
    /// <para>
    /// 🔒 The state comparison is the half that matters: `30` §2.1's <b>P4</b> makes a rejected
    /// command provably state-free, and <c>GameRules</c>' rejection arm discards the catch-up with
    /// it. A harness that stored the working copy on a rejection would silently hand the player
    /// regeneration they were refused.
    /// </para>
    /// <para>
    /// 🔴 <b>Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not through
    /// <c>PlayerSnapshot</c> record equality — and M1-11's review found the first draft doing the
    /// latter.</b> A record compares its dictionary components by <b>reference</b>, so
    /// <c>ToSnapshot().ShouldBe(before)</c> was trivially true of the wallet and both counter maps
    /// whatever they held (<c>Player.Copy</c> even hands out one shared empty singleton), and only
    /// the Energy half was under test. The same file that documents this hazard is
    /// <c>InMemoryGameDeterminismTests</c>, and this is the assertion it was documenting it for.
    /// </para>
    /// <para>
    /// 🔒 <b>The player is driven for a day first and the gap crosses a boundary</b>, for the same
    /// reason: an empty counter map and a refusal inside one game day would be identical under any
    /// comparison, so the first draft could not have seen a catch-up that <em>was</em> stored. Now
    /// there is a counter to wipe and a boundary to cross, and both are things a stored catch-up
    /// would move.
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
    /// 🔒 <c>START_RUN</c> is <c>CommandKind.Run</c> and the harness carries no run, so it is a
    /// <b>loading defect</b> — an exception, not <c>ILLEGAL_STATE</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the one row of `14` §2.3 that cannot be sent through the harness at all today,
    /// and it is worth pinning rather than discovering.</b> <c>GameRules</c> rules a
    /// <c>CommandKind.Run</c> command with no <c>Run</c> in the slice a <b>miswired caller</b>:
    /// `30` §4.1 makes loading the right slice the Application layer's job and `14` §16.2's
    /// <c>RUN_NOT_FOUND</c> is a transport-tier value <c>Apply</c> may not return. That reaches the
    /// harness unchanged, which is the correct behaviour and also a message for <b>M3-15</b>: the
    /// commit that makes <c>START_RUN</c> <c>Handled</c> has to decide how a run enters a
    /// <c>WorldSlice</c> the harness owns, because today there is no door.
    /// </para>
    /// <para>
    /// It also proves the harness passes the slice straight through rather than pre-screening it —
    /// a harness that answered <c>ILLEGAL_STATE</c> here to be helpful would be hiding a defect the
    /// domain deliberately raises.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_command_with_no_run_in_the_slice_is_a_defect_the_harness_does_not_soften()
    {
        var (game, player) = Harnesses.WithPlayer();
        var before = game.State(player);

        var defect = Should.Throw<InvalidOperationException>(
            () => game.Send(player, new StartRunCommand(3, DifficultyTier.NORMAL)));

        defect.Message.ShouldContain("START_RUN", Case.Sensitive);
        defect.Message.ShouldContain("carries no Run", Case.Sensitive);

        // 🔒 …and the throw left the harness untouched. Send increments its counters and appends the
        // events AFTER Apply returns, so none of it runs — but "it does not run" and "nothing
        // asserts that it does not run" are different states, and the clock's own guard is pinned
        // this way one file over.
        game.CommandsIssued.ShouldBe(0L);
        game.Events.ShouldBeEmpty();
        game.State(player).ShouldBeSameAs(before);

        // 🔒 AND IT IS NOT ONLY START_RUN. All nineteen CommandKind.Run rows hit the same guard,
        // because the harness's slice never carries a Run — the type's own remarks correct the
        // "forty-eight rows answer ILLEGAL_STATE" reading this test used to imply. Driven over a
        // second row so the claim is about the KIND rather than about the row that was picked.
        Should.Throw<InvalidOperationException>(() => game.Send(player, new RollDiceCommand()))
            .Message.ShouldContain("ROLL_DICE", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The event list is the assertion surface and cannot be written through.
    /// </summary>
    /// <remarks>
    /// The same hole <c>Player.WalletCurrencies</c> and <c>GameRules.Stamp</c> each close: an
    /// <c>IReadOnlyList&lt;T&gt;</c> that <em>is</em> a <c>List&lt;T&gt;</c> or a <c>T[]</c> casts
    /// straight back. Here it would be worse than elsewhere — a test could pass by appending to its
    /// own evidence.
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
    /// `30` §6 writes <c>game.Events.OfType&lt;GearGranted&gt;().Should().HaveCount(3)</c> after a
    /// sequence of commands, which only reads as an accumulation. The <c>Sequence</c> assertion is
    /// the other half and pins what the ordinal means: it is the event's position within
    /// <b>one</b> <c>Apply</c> call's list, from 1 — not a running counter across the simulation.
    /// A harness that renumbered would break `14` §7.1's economy log and `14` §2.4's animation
    /// script at once.
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
    /// 🔒 The harness carries the session forward: command <c>n + 1</c> starts where command
    /// <c>n</c> left off.
    /// </summary>
    /// <remarks>
    /// `30` §2.1's <b>P4</b> makes <c>Apply</c> return a <em>new</em> slice, so a harness that kept
    /// re-sending against the slice it started with would be running a first command N times — the
    /// one shape that cannot tell "grants once per game day" from "grants on every command". The
    /// assertion is <c>ShouldNotBeSameAs</c> plus the advancing timestamp, because "it stored
    /// something" and "it stored the result" are different claims.
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
    /// <para>
    /// ⚠️ <b>This is not tidiness, and it is the one culture-sensitive rendering this type has.</b>
    /// `02` §2 hashes the player id into every <c>runSeed</c>, so an id that rendered its counter
    /// differently on a Swedish laptop would draw different boards from M3 onwards — a determinism
    /// failure reproducible only on the machine of whoever wrote it, which is exactly what `14` §8.2
    /// exists to rule out.
    /// </para>
    /// <para>
    /// 🔒 <c>sv-SE</c> rather than <c>de-DE</c>, for the reason M1-06 recorded and M1-02 repeated:
    /// German renders a negative integer with an ordinary hyphen, so a German test proves nothing.
    /// Swedish renders it with U+2212 MINUS SIGN. The first assertion re-establishes that the runtime
    /// actually <em>has</em> a Swedish culture — under globalization-invariant mode
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
