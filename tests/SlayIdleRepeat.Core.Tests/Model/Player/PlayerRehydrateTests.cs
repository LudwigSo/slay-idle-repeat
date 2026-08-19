using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// <c>Player.Rehydrate</c>, one validated entry point for every persisted state. Every failure
/// assertion pins WHICH validation fired: faults accumulate, so <c>IsFailure</c> alone would pass
/// for a row invalid in an entirely different way.
/// </summary>
public sealed class PlayerRehydrateTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    /// <summary>Every field is asserted: a constructor that dropped or crossed two would pass a bare <c>IsSuccess</c>.</summary>
    [Fact]
    public void A_valid_row_rehydrates_with_every_field_where_it_was_persisted()
    {
        var snapshot = PlayerSnapshots.With(
            displayName: "Ludwig",
            legendLevel: 7,
            legendXp: 4_187,
            wallet: PlayerSnapshots.Wallet((CurrencyId.CROWNS, 60), (CurrencyId.SOUL_SHARDS, 30)),
            energy: new EnergyBanks(80, 12),
            energyAnchorUtc: PlayerSnapshots.Midmorning,
            lastAppliedAtUtc: PlayerSnapshots.Midmorning.AddMinutes(3),
            ftueBeatId: FtueBeat.B6B,
            dailyCounters: PlayerSnapshots.Counters(("ad_energy_grants", 2)),
            weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 9)));

        var player = Core.Model.Player.Rehydrate(snapshot, Content).Value;

        player.Id.ShouldBe(PlayerSnapshots.Id);
        player.DisplayName.ShouldBe("Ludwig");
        player.LegendLevel.ShouldBe(7);
        player.LegendXp.ShouldBe(4_187);
        player.BalanceOf(CurrencyId.CROWNS).ShouldBe(60);
        player.BalanceOf(CurrencyId.SOUL_SHARDS).ShouldBe(30);
        player.BalanceOf(CurrencyId.HONOR).ShouldBe(0);
        player.Energy.ShouldBe(new EnergyBanks(80, 12));
        player.EnergyAnchorUtc.ShouldBe(PlayerSnapshots.Midmorning);
        player.LastAppliedAtUtc.ShouldBe(PlayerSnapshots.Midmorning.AddMinutes(3));
        player.FtueBeat.ShouldBe(FtueBeat.B6B);
        player.FtueCompletedAtUtc.ShouldBeNull();
        player.IsFtueComplete.ShouldBeFalse();
        player.DailyPeriodStartUtc.ShouldBe(PlayerSnapshots.Wednesday);
        player.DailyCount("ad_energy_grants").ShouldBe(2);
        player.WeeklyPeriodStartUtc.ShouldBe(PlayerSnapshots.Monday);
        player.WeeklyCount("guild_quest_contributions").ShouldBe(9);
    }

    /// <summary>
    /// Both directions: a downgraded client's future row and a past row are equally unreadable.
    /// The boundary rows are expressions, not literals — a literal would silently stop being a
    /// wrong version at the next schema bump.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(SnapshotSchema.SchemaVersion - 1)]
    [InlineData(SnapshotSchema.SchemaVersion + 1)]
    [InlineData(int.MaxValue)]
    public void An_unknown_SchemaVersion_hard_fails_and_says_no_migration_exists(int schemaVersion)
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(schemaVersion: schemaVersion), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("SchemaVersion", Case.Sensitive);
        result.Error.ShouldContain("NO MIGRATION EXISTS", Case.Sensitive);
        result.Error.ShouldContain("14 §16.6", Case.Sensitive);
    }

    /// <summary>
    /// The version check runs first and alone: reporting field faults beside it would let a reader
    /// "fix" the row instead of the version.
    /// </summary>
    [Fact]
    public void A_wrong_SchemaVersion_is_reported_alone_and_not_alongside_field_faults()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(
                schemaVersion: SnapshotSchema.SchemaVersion + 1, displayName: "  ", legendLevel: -3),
            Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("NO MIGRATION EXISTS", Case.Sensitive);
        result.Error.ShouldNotContain("DisplayName", Case.Sensitive);
        result.Error.ShouldNotContain("LegendLevel", Case.Sensitive);
    }

    /// <summary>The current <c>SchemaVersion</c> is accepted, so the check above is not "refuse everything".</summary>
    [Fact]
    public void The_current_SchemaVersion_is_accepted()
    {
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(schemaVersion: SnapshotSchema.SchemaVersion), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary><c>default(PlayerId)</c> never ran <c>PlayerId</c>'s constructor, so its <c>Value</c> is null.</summary>
    [Fact]
    public void A_default_PlayerId_is_refused_because_its_constructor_never_ran()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(id: default(PlayerId)), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Id is blank or default(PlayerId)", Case.Sensitive);
    }

    /// <summary>A blank display name is refused — and nothing else about the name is; the filter is owned elsewhere.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void A_blank_display_name_is_refused(string displayName)
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(displayName: displayName), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("DisplayName is blank", Case.Sensitive);
    }

    /// <summary>The converse that keeps the name rule honest: a name outside this aggregate's authority loads.</summary>
    [Theory]
    [InlineData("x")]
    [InlineData("Ludwig")]
    [InlineData("  Ludwig  ")]
    [InlineData("δράκων 🐉 <script>")]
    [InlineData("'; DROP TABLE players; --")]
    public void A_name_this_milestone_has_no_authority_to_judge_is_accepted(string displayName)
    {
        var player = Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(displayName: displayName), Content)
            .Value;

        player.DisplayName.ShouldBe(displayName, "the name is stored, never edited.");
    }

    /// <summary>
    /// A Legend Level outside the authored range is refused, and the message quotes the JSON
    /// pointers the range came from rather than a constant in code.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    [InlineData(int.MaxValue)]
    public void A_Legend_Level_outside_the_authored_range_is_refused(int legendLevel)
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(legendLevel: legendLevel), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("LegendLevel is", Case.Sensitive);
        result.Error.ShouldContain("tuning/progression.json#/legendLevel/min", Case.Sensitive);
        result.Error.ShouldContain("tuning/progression.json#/legendLevel/max", Case.Sensitive);
    }

    /// <summary>Both ends of the authored range load, so the check above is a range and not a wall.</summary>
    [Theory]
    [InlineData(ProgressionDocuments.ShippedLegendLevelMin)]
    [InlineData(ProgressionDocuments.ShippedLegendLevelMax)]
    public void Both_ends_of_the_authored_Legend_Level_range_load(int legendLevel)
    {
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(legendLevel: legendLevel), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>Without this, a <c>const int MaxLegendLevel = 200</c> would satisfy every other assertion in this file.</summary>
    [Fact]
    public void The_Legend_Level_range_comes_from_the_content_set_and_not_from_a_constant()
    {
        var narrowed = ProgressionDocuments.With(
            legendLevelMax: ContentValue.Number(50));

        var atFifty = PlayerSnapshots.With(legendLevel: 50);
        var atFiftyOne = PlayerSnapshots.With(legendLevel: 51);

        Core.Model.Player.Rehydrate(atFifty, narrowed).IsSuccess.ShouldBeTrue();
        Core.Model.Player.Rehydrate(atFiftyOne, narrowed).IsFailure.ShouldBeTrue();

        // …and the same two rows against the shipped data set, so the difference is the DATA.
        Core.Model.Player.Rehydrate(atFifty, Content).IsSuccess.ShouldBeTrue();
        Core.Model.Player.Rehydrate(atFiftyOne, Content).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Legend XP is lifetime banked income; it is never negative.</summary>
    [Fact]
    public void Negative_Legend_XP_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(legendXp: -1), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("LegendXp is -1", Case.Sensitive);
    }

    /// <summary>On <c>Player</c> and nowhere else: <c>runSeed</c> needs it before the <c>Run</c> exists and after it ends.</summary>
    [Fact]
    public void The_lifetime_runs_started_counter_advances_and_is_never_negative()
    {
        var negative = Core.Model.Player.Rehydrate(PlayerSnapshots.With(runsStarted: -1), Content);
        negative.IsFailure.ShouldBeTrue();
        negative.Error.ShouldContain("RunsStarted is -1", Case.Sensitive);

        var player = Core.Model.Player.Rehydrate(PlayerSnapshots.With(runsStarted: 41), Content).Value;

        player.RunsStarted.ShouldBe(41);
        player.BeginRun().ShouldBe(42, "the value returned is the counter AFTER the increment");
        player.RunsStarted.ShouldBe(42);
        player.ToSnapshot().RunsStarted.ShouldBe(42);
    }

    /// <summary>The counter refuses to wrap: it feeds into <c>runSeed</c>.</summary>
    [Fact]
    public void The_lifetime_runs_started_counter_refuses_to_wrap()
    {
        var player = Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(runsStarted: long.MaxValue), Content).Value;

        Should.Throw<InvalidOperationException>(() => player.BeginRun())
              .Message.ShouldMatchWildcard("*re-seeding runs*");

        player.RunsStarted.ShouldBe(long.MaxValue);
    }

    /// <summary>
    /// A <b>content</b> defect throws rather than failing. A corrupt row is one player's problem
    /// and belongs in a <c>Result</c>; a data set with no authored Legend Level range is every
    /// player's problem and belongs at the composition root that loaded it.
    /// </summary>
    [Fact]
    public void An_unauthorised_tunable_throws_rather_than_becoming_a_row_level_failure()
    {
        var hollow = ProgressionDocuments.With(legendLevelMax: ContentValue.Unauthorised);

        var act = () => Core.Model.Player.Rehydrate(PlayerSnapshots.Valid, hollow);

        Should.Throw<UnauthorisedTunableException>(act)
              .Message.ShouldMatchWildcard("*legendLevel/max*");
    }

    /// <summary>
    /// 🔒 An absent filter read as empty sweeps nothing — indistinguishable from a player who has
    /// not configured one, so a dropped column would silently turn the feature off.
    /// </summary>
    [Fact]
    public void A_null_auto_salvage_filter_is_refused_by_name()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(autoSalvage: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(
            nameof(PlayerSnapshot.AutoSalvageRules),
            Case.Sensitive,
            customMessage: "several fields can fail this validation; the message must say WHICH one did.");

        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(cleared: true), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>The fault names the ROW, since a filter usually has several.</summary>
    [Theory]
    [InlineData((Rarity)99, 3, "names band '99'")]
    [InlineData(Rarity.C, -1, "sweeps below level -1")]
    public void A_filter_row_the_ladder_does_not_have_is_refused_by_row(
        Rarity rule, int belowEnhanceLevel, string fragment)
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(
                autoSalvageRules: [new AutoSalvageRule(Rarity.S, 5), new AutoSalvageRule(rule, belowEnhanceLevel)]),
            Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(
            nameof(PlayerSnapshot.AutoSalvageRules) + "[1]",
            Case.Sensitive,
            customMessage: "a filter carries several rows; the fault has to name which one is wrong.");
        result.Error.ShouldContain(fragment, Case.Sensitive);
    }

    /// <summary>Negative control: a filter of legal rows loads, in persisted order.</summary>
    [Fact]
    public void A_filter_of_legal_rows_rehydrates_in_the_order_it_was_persisted()
    {
        var player = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(
                autoSalvageRules: [new AutoSalvageRule(Rarity.B, 3), new AutoSalvageRule(Rarity.C, 0)]),
            Content).Value;

        player.AutoSalvageRules.ShouldBe(
            [new AutoSalvageRule(Rarity.B, 3), new AutoSalvageRule(Rarity.C, 0)]);
    }

    /// <summary>Neither argument may be null; a null snapshot is a caller defect, not a corrupt row.</summary>
    [Fact]
    public void Null_arguments_are_refused_as_argument_errors()
    {
        Should.Throw<ArgumentNullException>(
            () => Core.Model.Player.Rehydrate(null!, Content));

        Should.Throw<ArgumentNullException>(
            () => Core.Model.Player.Rehydrate(PlayerSnapshots.Valid, null!));
    }

    /// <summary>One round trip per defect is one too many once the row is in production.</summary>
    [Fact]
    public void Every_fault_is_reported_rather_than_only_the_first()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(
                displayName: " ",
                legendLevel: 0,
                legendXp: -5,
                weeklyPeriodStartUtc: PlayerSnapshots.Wednesday),
            Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("4 problem(s)", Case.Sensitive);
        result.Error.ShouldContain("DisplayName is blank", Case.Sensitive);
        result.Error.ShouldContain("LegendLevel is 0", Case.Sensitive);
        result.Error.ShouldContain("LegendXp is -5", Case.Sensitive);
        result.Error.ShouldContain("WeeklyPeriodStartUtc", Case.Sensitive);
    }
}
