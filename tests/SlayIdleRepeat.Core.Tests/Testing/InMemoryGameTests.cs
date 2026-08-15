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

/// <summary>The harness's surface: construction, <c>CreatePlayer</c>, <c>Send</c>, <c>State</c> and
/// the event list. What the rules decide belongs in <c>InMemoryGameDayCycleTests</c> instead.</summary>
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

/// <summary>The harness never loads: it takes a pre-built <see cref="ContentSnapshot"/>, and the one
/// it was handed — by identity, not a copy — is the one every command reads.</summary>
    [Fact]
    public void The_content_set_is_the_one_it_was_handed()
    {
        var content = TuningDocuments.Shipped;
        var game = new InMemoryGame(content, Harnesses.Seed, new VirtualClock(Harnesses.Start));

        game.Content.ShouldBeSameAs(content);
        game.Seed.ShouldBe(Harnesses.Seed);
        game.Clock.NowUtc.ShouldBe(Harnesses.Start);
    }

    /// <summary>The defaults are the absence of a thing: no Plus, no kill switch thrown — and both
    /// are overridable.</summary>
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

/// <summary>A created player starts at the authored Legend Level floor holding nothing — six wallet
/// rows at zero and both Energy banks at zero, since every currency movement must carry a
/// <c>CurrencyChanged</c> and a starting balance would have none.</summary>
/// <remarks>Pins the shipped Legend Level value;
/// <see cref="A_created_player_reads_its_Legend_Level_from_the_content_set"/> is the half that
/// discriminates a tuning read from a hard-coded literal.</remarks>
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

/// <summary>A created player's Legend Level comes from the content set, not a literal — asserted
/// against an unshipped floor (5) so a hard-coded 1 could not accidentally pass. The Energy
/// assertion beside it matters because Max Energy is derived from the Legend Level, so a hard-coded
/// level would hand every simulated player the wrong tank.</summary>
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

/// <summary>A created player is already inside the game day and week the clock is in. Left at a
/// default, the first command would clear a period the player never played, and a boundary earlier
/// than the stored one makes <c>Player.RequireNotBefore</c> throw out of <c>Apply</c>.</summary>
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

/// <summary>Players are distinct, deterministic and independent. Independence is asserted through a
/// command rather than construction: two players sharing one slice is the defect a dictionary keyed
/// on the wrong thing produces.</summary>
    [Fact]
    public void Two_players_have_distinct_identities_and_independent_state()
    {
        var game = Harnesses.New();

        var first = game.CreatePlayer();
        var second = game.CreatePlayer("Ludwig the Unhurried");

        first.ShouldNotBe(second);

        // In creation order, not ignoreOrder: Players is an insertion-ordered list, not a
        // Dictionary.Keys view whose order is unspecified.
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

/// <summary>An id this harness never issued is a defect, not a rejection, at both doors: a player
/// asking for something they cannot have is a <c>RejectionReason</c>, but a caller naming a player
/// that does not exist is miswired and should throw rather than return <c>ILLEGAL_STATE</c>.
/// <c>default(PlayerId)</c> is asserted alongside a plausible id since it's what an unassigned field
/// produces.</summary>
    [Fact]
    public void An_id_this_harness_never_issued_is_a_defect_at_both_doors()
    {
        var game = Harnesses.New();
        var stranger = new PlayerId("PLAYER_99999999");

        // Message pinned at all three doors: Send can also throw InvalidOperationException from the
        // CommandKind.Run loading defect below, which has a different fix.
        Should.Throw<InvalidOperationException>(() => game.State(stranger))
            .Message.ShouldContain("holds no player", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => game.Send(stranger, Harnesses.BeginSession))
            .Message.ShouldContain("holds no player", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => game.State(default))
            .Message.ShouldContain("holds no player", Case.Sensitive);
    }

/// <summary>A <c>Deferred</c> command is refused with <c>ILLEGAL_STATE</c>, not thrown, and changes
/// nothing — several rows from different milestones, so the claim is about the mechanism rather than
/// whichever command was picked.</summary>
/// <remarks>Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not
/// <c>PlayerSnapshot</c> record equality: a record compares its dictionary components by reference,
/// so <c>ToSnapshot().ShouldBe(before)</c> would be trivially true regardless of their contents.</remarks>
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

        // 25h so every refused command below crosses a day boundary and several regen intervals —
        // things AdvanceTime would move if the working copy survived a rejection.
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

/// <summary>A <c>CommandKind.Run</c> command sent when the harness carries no run is a loading
/// defect — an exception, not <c>ILLEGAL_STATE</c> — since loading the right slice is the
/// Application layer's job and <c>Apply</c> may not return a transport-level "not found". Probed
/// with two rows of different payload shapes, since the claim is about the kind rather than one
/// row.</summary>
/// <remarks><c>START_RUN</c> is exempt from this guard (<c>CommandRegistration.OpensRun</c>) since
/// its whole job is to create the <c>Run</c> this guard would otherwise demand; see
/// <see cref="A_START_RUN_command_succeeds_on_the_harnesss_run_less_slice_with_no_harness_change"/>.</remarks>
    [Fact]
    public void A_run_command_with_no_run_in_the_slice_is_a_defect_the_harness_does_not_soften()
    {
        var (game, player) = Harnesses.WithPlayer();
        var before = game.State(player);

        var defect = Should.Throw<InvalidOperationException>(
            () => game.Send(player, new RollDiceCommand()));

        defect.Message.ShouldContain("ROLL_DICE", Case.Sensitive);
        defect.Message.ShouldContain("carries no Run", Case.Sensitive);

        // The throw left the harness untouched: Send increments counters and appends events only
        // after Apply returns.
        game.CommandsIssued.ShouldBe(0L);
        game.Events.ShouldBeEmpty();
        game.State(player).ShouldBeSameAs(before);

        Should.Throw<InvalidOperationException>(() => game.Send(player, new ChooseForkCommand(0)))
            .Message.ShouldContain("CHOOSE_FORK", Case.Sensitive);
    }

/// <summary><c>START_RUN</c> succeeds on the run-less slice every other <c>CommandKind.Run</c> row
/// still throws on, with no harness change required: <c>CreatePlayer</c> already builds a
/// <c>WorldSlice(player, null)</c>, so what stood in the way was <c>GameRules.Execute</c>'s own
/// pre-dispatch guard, and <c>CommandRegistration.OpensRun</c> exempts this one row from it. Asserts
/// the full shape: acceptance, a <c>Run</c> with the requested chapter and tier, the trailhead
/// position, no events, and the lifetime run counter advanced by one.</summary>
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

/// <summary>The event list is the assertion surface and cannot be written through: an
/// <c>IReadOnlyList&lt;T&gt;</c> that is actually a <c>List&lt;T&gt;</c> casts straight back, which
/// here would let a test pass by appending to its own evidence.</summary>
    [Fact]
    public void The_event_list_cannot_be_written_through()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromHours(8));
        game.Send(player, Harnesses.BeginSession);

        game.Events.ShouldNotBeEmpty();

        // ShouldBeAssignableTo rather than `as ... ?.`: a null-conditional would silently skip this
        // check if the cast ever failed, instead of failing it.
        game.Events.ShouldBeAssignableTo<ICollection<DomainEvent>>()!.IsReadOnly.ShouldBeTrue();

        (game.Events as DomainEvent[]).ShouldBeNull("a bare array casts back and is writable.");
        (game.Events as List<DomainEvent>).ShouldBeNull("a bare List casts back and is writable.");

        game.Players.ShouldBeAssignableTo<ICollection<PlayerId>>()!.IsReadOnly.ShouldBeTrue();
        (game.Players as List<PlayerId>).ShouldBeNull();
    }

/// <summary>The event list is live, and accumulates in command order across commands. The
/// <c>Sequence</c> assertion pins what the ordinal means: a position within one <c>Apply</c> call's
/// list, from 1 — not a running counter across the simulation.</summary>
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

        // Each command's list is numbered 1..n on its own, not a running counter across the sim.
        first.Events.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, first.Events.Count));
        second.Events.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, second.Events.Count));

        first.Events.ShouldNotBeEmpty("…and neither range is empty (S3).");
        second.Events.ShouldNotBeEmpty();
    }

/// <summary>The harness carries the session forward: command <c>n + 1</c> starts where <c>n</c> left
/// off, since <c>Apply</c> returns a new slice each time and a harness that kept re-sending against
/// the original slice would run the first command over and over.</summary>
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

/// <summary>The generated <see cref="PlayerId"/> is the same string under every culture — checked
/// against Swedish, whose negative-number glyph (U+2212) differs from an ordinary hyphen. The first
/// assertion re-establishes that the runtime actually has a Swedish culture: under
/// globalization-invariant mode, <c>new CultureInfo("sv-SE")</c> silently returns the invariant one
/// and everything below would hold over nothing.</summary>
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
