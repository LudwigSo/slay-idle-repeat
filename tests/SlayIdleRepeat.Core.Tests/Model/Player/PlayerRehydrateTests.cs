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
/// 🔒 `30` §11.3 — <c>Player.Rehydrate</c> is <em>"one validated entry point for every persisted state
/// in the game — a corrupt row fails loudly at the seam rather than silently three rules later."</em>
/// One assertion per way a row can be wrong.
/// </summary>
/// <remarks>
/// 🔒 Every failure assertion pins <b>which</b> validation fired. <c>Rehydrate</c> reports every fault
/// it finds rather than the first, so a test that only checked <c>IsFailure</c> would pass for a row
/// invalid in some entirely different way.
/// </remarks>
public sealed class PlayerRehydrateTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    /// <summary>A valid row rehydrates, and every field arrives where it was persisted.</summary>
    /// <remarks>
    /// The positive half, and it is not a formality: it is what stops a validation being tightened
    /// into refusing states the game is legitimately in. Every field is asserted, because a
    /// constructor that dropped one — or crossed two of the four <see cref="DateTimeOffset"/>s —
    /// would pass a test that only checked <c>IsSuccess</c>.
    /// </remarks>
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
    /// 🔒 An unknown <c>SchemaVersion</c> hard-fails, and the message says there is no migration rather
    /// than reading the row anyway.
    /// </summary>
    /// <remarks>
    /// Both directions — a row from the future (a client that downgraded) and one from the past.
    /// Reading either against the current layout shifts every field after the first change by one.
    /// <para>
    /// 🔒 The "one past the current" row is an expression, not a literal: as <c>[InlineData(2)]</c> it
    /// silently stopped being a wrong version when the schema bumped to 2, and a literal 3 would have
    /// kept passing while asserting nothing about the boundary it names.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]

    // 🔒 The version this build ORPHANS. M1-09 bumped 1 -> 2 with no migration, so a row stamped 1
    // is real on-disk data this build cannot read — the exact case SnapshotSchema's history
    // paragraph and SnapshotFieldOrder.json's preamble both claim is "refused loudly". Nothing
    // asserted it until the M1-09 review asked.
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
    /// 🔒 The version check runs <b>first and alone</b>: a row from another schema is refused for
    /// being from another schema, not for whatever its fields happen to look like under this
    /// layout.
    /// </summary>
    /// <remarks>
    /// Steering <b>S2</b>. Without this, a wrong-version row that also had, say, a blank display
    /// name could report the blank name and let a reader "fix" the row instead of the version —
    /// and the test above would still pass, because its fragment would be in the joined message.
    /// </remarks>
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

    /// <summary>
    /// 🔒 <c>default(PlayerId)</c> never ran <c>PlayerId</c>'s constructor, so its <c>Value</c> is
    /// null. <c>PlayerId</c>'s own remarks name this seam as the one that has to catch it.
    /// </summary>
    [Fact]
    public void A_default_PlayerId_is_refused_because_its_constructor_never_ran()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(id: default(PlayerId)), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Id is blank or default(PlayerId)", Case.Sensitive);
    }

    /// <summary>
    /// A blank display name is refused — and nothing else about the name is. ⚠️ `16` <b>O34</b>
    /// leaves the name lifecycle open and M4-10 owns the profanity filter, so a rule here would be
    /// inventing one (S6).
    /// </summary>
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

    /// <summary>
    /// ⚠️ The converse, and it is the assertion that keeps the name rule honest: a name this
    /// milestone has no authority to judge is <b>accepted</b>. Duplicates, punctuation, mixed
    /// scripts and 400 characters all load, because `16` O34 has not ruled and M1-04 must not.
    /// </summary>
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

        player.DisplayName.ShouldBe(displayName, "16 O34 is open: the name is stored, never edited.");
    }

    /// <summary>
    /// `07` §1.1 — a Legend Level outside the authored range is refused, and the message quotes
    /// the JSON pointers the range came from rather than a constant in code.
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

    /// <summary>
    /// 🔒 The range is read from the content set, not hard-coded: a data set that authors a
    /// different maximum moves what this seam accepts.
    /// </summary>
    /// <remarks>
    /// The half that proves <c>LegendTuning</c> is actually consulted. Without it, a
    /// <c>const int MaxLegendLevel = 200</c> in the aggregate would satisfy every other assertion
    /// in this file — and `21` §3.1 calls a tunable in code a bug.
    /// </remarks>
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

    /// <summary>Legend XP is lifetime banked income (`02` §5.1a); it is never negative.</summary>
    [Fact]
    public void Negative_Legend_XP_is_refused()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.With(legendXp: -1), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("LegendXp is -1", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `02` §2's <c>runCounter</c> — the lifetime runs-started counter — is never negative, and
    /// <c>BeginRun</c> advances it and answers the value the run is seeded with.
    /// </summary>
    /// <remarks>
    /// It is on <c>Player</c> and nowhere else: <c>runSeed = Hash64(playerId, chapterId, tierId,
    /// utcUnixSeconds, runCounter)</c> needs it before the <c>Run</c> exists and after it ends, and
    /// a period-cleared counter map would reset it. M1-05 cannot write <c>START_RUN</c> without it.
    /// </remarks>
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

    /// <summary>The counter refuses to wrap: `02` §2 feeds it into <c>runSeed</c>.</summary>
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
    /// ⚠️ A <b>content</b> defect throws rather than failing. A corrupt row is one player's
    /// problem and belongs in a <c>Result</c>; a data set with no authored Legend Level range is
    /// every player's problem and belongs at the composition root that loaded it.
    /// </summary>
    [Fact]
    public void An_unauthorised_tunable_throws_rather_than_becoming_a_row_level_failure()
    {
        var hollow = ProgressionDocuments.With(legendLevelMax: ContentValue.Unauthorised);

        var act = () => Core.Model.Player.Rehydrate(PlayerSnapshots.Valid, hollow);

        Should.Throw<UnauthorisedTunableException>(act)
              .Message.ShouldMatchWildcard("*legendLevel/max*");
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

    /// <summary>
    /// 🔒 Every fault the row has is reported, not just the first. A row is usually corrupt in
    /// more than one way, and one round trip per defect is one too many once it is in production.
    /// </summary>
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
