using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// <c>Run.Rehydrate</c>, one validated entry point for every persisted state. Every failure
/// assertion pins WHICH validation fired: faults accumulate, so <c>IsFailure</c> alone would pass
/// for a row invalid in an entirely different way.
/// </summary>
public sealed class RunRehydrateTests
{
    /// <summary>Every field is asserted: a constructor that dropped or crossed two would pass a bare <c>IsSuccess</c>.</summary>
    [Fact]
    public void A_valid_row_rehydrates_with_every_field_where_it_was_persisted()
    {
        var snapshot = RunSnapshots.With(
            runSeed: 0xFEEDFACECAFEBEEFUL,
            chapterId: 3,
            tier: DifficultyTier.HEROIC,
            lastAppliedAtUtc: RunSnapshots.Midmorning.AddMinutes(4),
            position: 19,
            currentHp: 61,
            maxHp: 140,
            gold: 1_450,
            rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 12UL), (RngStreams.Board, 8UL)),
            adUses: RunSnapshots.AdUses(("AD_REVIVE", 1), ("AD_REROLL_PERK", 2)));

        var run = Run.Rehydrate(snapshot).Value;

        run.Id.ShouldBe(RunSnapshots.Id);
        run.PlayerId.ShouldBe(RunSnapshots.Owner);
        run.RunSeed.ShouldBe(0xFEEDFACECAFEBEEFUL);
        run.ChapterId.ShouldBe(3);
        run.Tier.ShouldBe(DifficultyTier.HEROIC);
        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning.AddMinutes(4));
        run.Position.ShouldBe(19);
        run.CurrentHp.ShouldBe(61);
        run.MaxHp.ShouldBe(140);
        run.Gold.ShouldBe(1_450);
        run.BalanceOf(CurrencyId.GOLD).ShouldBe(1_450);
        run.StreamPosition(RngStreams.Dice).ShouldBe(12UL);
        run.StreamPosition(RngStreams.Board).ShouldBe(8UL);
        run.StreamPosition(RngStreams.Drops).ShouldBe(0UL, "a stream nobody drew from stands at draw 0");
        run.AdUseCount("AD_REVIVE").ShouldBe(1);
        run.AdUseCount("AD_REROLL_PERK").ShouldBe(2);
        run.AdUseCount("AD_SHOP_FREEBIE").ShouldBe(0);
    }

    /// <summary>A null row is a caller bug, not a corrupt row, so it throws rather than failing.</summary>
    [Fact]
    public void A_null_snapshot_is_refused_by_the_argument_guard()
    {
        Should.Throw<ArgumentNullException>(() => Run.Rehydrate(null!));
    }

    /// <summary>
    /// The pin is per VERSION, not per record: an old <c>RunSnapshot</c> is unreadable even at a
    /// bump where its own layout did not move. Boundary rows are expressions, not literals.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(SnapshotSchema.SchemaVersion - 1)]
    [InlineData(SnapshotSchema.SchemaVersion + 1)]
    [InlineData(int.MaxValue)]
    public void An_unknown_SchemaVersion_hard_fails_and_says_no_migration_exists(int schemaVersion)
    {
        var result = Run.Rehydrate(RunSnapshots.With(schemaVersion: schemaVersion));

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
        var result = Run.Rehydrate(
            RunSnapshots.With(
                schemaVersion: SnapshotSchema.SchemaVersion + 1, position: -3, chapterId: 0, gold: -5));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("NO MIGRATION EXISTS", Case.Sensitive);
        result.Error.ShouldNotContain("Position", Case.Sensitive);
        result.Error.ShouldNotContain("ChapterId", Case.Sensitive);
        result.Error.ShouldNotContain("Gold", Case.Sensitive);
    }

    /// <summary>The current <c>SchemaVersion</c> is accepted, so the check above is not "refuse everything".</summary>
    [Fact]
    public void The_current_SchemaVersion_is_accepted()
    {
        Run.Rehydrate(RunSnapshots.With(schemaVersion: SnapshotSchema.SchemaVersion))
           .IsSuccess.ShouldBeTrue();
    }

    /// <summary><c>default(RunId)</c> never ran <c>RunId</c>'s constructor, so its <c>Value</c> is null.</summary>
    [Fact]
    public void A_default_RunId_is_refused_because_its_constructor_never_ran()
    {
        var result = Run.Rehydrate(RunSnapshots.With(id: default(RunId)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Id is blank or default(RunId)", Case.Sensitive);
    }

    /// <summary>
    /// And the same for the owning player: a <c>Run</c> is a child of <c>Player</c>, so a run that
    /// names no player is an orphan rather than a run.
    /// </summary>
    [Fact]
    public void A_default_PlayerId_is_refused_because_a_run_is_a_child_of_a_player()
    {
        var result = Run.Rehydrate(RunSnapshots.With(playerId: default(PlayerId)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("PlayerId is blank or default(PlayerId)", Case.Sensitive);
    }

    /// <summary>A chapter below 1 is refused — <c>chapter.schema.json</c> sets <c>"minimum": 1</c>.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_chapter_below_one_is_refused(int chapterId)
    {
        var result = Run.Rehydrate(RunSnapshots.With(chapterId: chapterId));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("ChapterId", Case.Sensitive);
        result.Error.ShouldContain("minimum", Case.Sensitive);
    }

    /// <summary>
    /// A chapter above the authored set is accepted, deliberately: hard-coding a maximum would put
    /// a content bound in code — a partial invariant masquerading as the real one.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(4_096)]
    public void A_chapter_no_content_authors_yet_is_accepted_because_the_bound_is_content_not_code(int chapterId)
    {
        var run = Run.Rehydrate(RunSnapshots.With(chapterId: chapterId)).Value;

        run.ChapterId.ShouldBe(chapterId);
    }

    /// <summary>
    /// An undefined <see cref="DifficultyTier"/> is refused, including the zero
    /// <c>default(DifficultyTier)</c> reads as — which is why the enum has no <c>0</c> member.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void An_undefined_DifficultyTier_is_refused(int tier)
    {
        var result = Run.Rehydrate(RunSnapshots.With(tier: (DifficultyTier)tier));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Tier", Case.Sensitive);
        result.Error.ShouldContain("NORMAL", Case.Sensitive);
    }

    /// <summary>
    /// A non-UTC <c>LastAppliedAtUtc</c> is refused. <c>CanonicalStateWriter</c> encodes a
    /// <see cref="DateTimeOffset"/> as Unix milliseconds, so two offsets naming one instant share a
    /// <c>stateHash</c> while record equality calls the two snapshots different.
    /// </summary>
    [Fact]
    public void A_LastAppliedAtUtc_carrying_a_non_zero_offset_is_refused()
    {
        var offset = new DateTimeOffset(2026, 8, 12, 11, 41, 7, TimeSpan.FromHours(2));

        var result = Run.Rehydrate(RunSnapshots.With(lastAppliedAtUtc: offset));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("LastAppliedAtUtc", Case.Sensitive);
        result.Error.ShouldContain("offset", Case.Sensitive);
    }

    /// <summary>A position below the trailhead is refused — and that is the whole position check.</summary>
    /// <remarks>
    /// The fault <b>count</b> is asserted beside the fragment, because <c>Position</c> is a
    /// substring of <c>RngStreamPositions</c>: on its own, that fragment would be satisfied by a
    /// message about the stream map and this case would pass while the position check was gone.
    /// </remarks>
    [Fact]
    public void A_position_below_the_trailhead_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.With(position: -2));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("(1 problem(s))", Case.Sensitive);
        result.Error.ShouldContain("Position", Case.Sensitive);
    }

    /// <summary>
    /// The case a floor of 0 would get wrong: every run starts at the virtual trailhead (−1), and
    /// a run abandoned before its first roll is persisted there.
    /// </summary>
    [Fact]
    public void The_trailhead_position_rehydrates_because_that_is_where_every_run_starts()
    {
        Run.Rehydrate(RunSnapshots.With(position: -1)).Value.Position.ShouldBe(-1);
    }

    /// <summary>"A run's position is a valid node" is registered as a gap (M3-01), not approximated here.</summary>
    [Fact]
    public void A_position_no_board_could_contain_is_accepted_because_node_identity_is_M3_01s()
    {
        Run.Rehydrate(RunSnapshots.With(position: 10_000)).Value.Position.ShouldBe(10_000);
    }

    /// <summary>A maximum below 1 is refused: a hero with no hit points at all is not a run state.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_MaxHp_below_one_is_refused(int maxHp)
    {
        var result = Run.Rehydrate(RunSnapshots.With(currentHp: 0, maxHp: maxHp));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("(1 problem(s))", Case.Sensitive);
        result.Error.ShouldContain("MaxHp", Case.Sensitive);
    }

    /// <summary>A negative current is refused; zero is not — a downed hero awaiting a revive is at 0.</summary>
    [Fact]
    public void A_negative_CurrentHp_is_refused_while_zero_is_a_legal_state()
    {
        Run.Rehydrate(RunSnapshots.With(currentHp: -1)).Error
           .ShouldContain("CurrentHp", Case.Sensitive);

        Run.Rehydrate(RunSnapshots.With(currentHp: 0)).Value.CurrentHp.ShouldBe(0);
    }

    /// <summary>Current above maximum is refused, and the message says <b>which</b> of the two rules fired.</summary>
    [Fact]
    public void A_CurrentHp_above_MaxHp_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.With(currentHp: 101, maxHp: 100));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(
            "(1 problem(s))",
            Case.Sensitive,
            "an overheal is ONE defect: the maximum is legal and the current is non-negative, so " +
            "neither of the other two hit-point rules has anything to say about this row.");
        result.Error.ShouldContain("CurrentHp", Case.Sensitive);
        result.Error.ShouldContain("MaxHp", Case.Sensitive);
    }

    /// <summary>Current equal to maximum is legal — the boundary is not off by one.</summary>
    [Fact]
    public void A_CurrentHp_equal_to_MaxHp_is_allowed()
    {
        Run.Rehydrate(RunSnapshots.With(currentHp: 100, maxHp: 100)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// One defect is one fault. A row whose <c>MaxHp</c> is itself invalid must not <em>also</em>
    /// be reported for a current that exceeds it — the comparison is meaningless against a maximum
    /// the row does not have.
    /// </summary>
    /// <remarks>
    /// Stated as an exact count rather than as a fragment: a reader handed two faults for one
    /// defect fixes the wrong one.
    /// </remarks>
    [Fact]
    public void An_invalid_MaxHp_is_reported_once_and_not_also_as_a_CurrentHp_overflow()
    {
        var result = Run.Rehydrate(RunSnapshots.With(currentHp: 5, maxHp: 0));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("MaxHp", Case.Sensitive);
        result.Error.ShouldContain("(1 problem(s))", Case.Sensitive);
    }

    /// <summary>A currency never goes negative, checked at the seam as well.</summary>
    [Fact]
    public void A_negative_Gold_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.With(gold: -1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Gold", Case.Sensitive);
        result.Error.ShouldContain("30 §11.5", Case.Sensitive);
    }

    /// <summary>A null stream map is refused; an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_RngStreamPositions_map_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.WithNull(streams: true));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("RngStreamPositions is null", Case.Sensitive);
    }

    /// <summary>
    /// Same predicate as <c>DeterministicRng</c>'s constructor. <c>minigame:03</c> is a different
    /// string — and so a different sequence — from <c>minigame:3</c>; <c>DICE</c> pins the
    /// comparison as ordinal.
    /// </summary>
    [Theory]
    [InlineData("loot")]
    [InlineData("DICE")]
    [InlineData("dice ")]
    [InlineData("minigame:03")]
    [InlineData("minigame:")]
    [InlineData("minigame:-1")]
    [InlineData("")]
    public void A_stream_name_outside_the_14_section_8_1_registry_is_refused(string streamName)
    {
        var result = Run.Rehydrate(
            RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((streamName, 1UL))));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("RngStreamPositions", Case.Sensitive);
        result.Error.ShouldContain("14 §8.1", Case.Sensitive);
    }

    public static TheoryData<string> EveryRegisteredStream
    {
        get
        {
            var streams = new TheoryData<string>();
            foreach (var name in RngStreams.FixedNames)
            {
                streams.Add(name);
            }

            streams.Add(RngStreams.Minigame(0));
            streams.Add(RngStreams.Minigame(7));
            return streams;
        }
    }

    /// <summary>
    /// …and every row the registry DOES recognise is accepted, including the parameterised ninth.
    /// Position 1, not 0: absent means 0, so a zero would also hold for a validation that dropped
    /// the key entirely.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRegisteredStream))]
    public void Every_row_of_the_registry_is_accepted_including_the_parameterised_minigame_row(string streamName)
    {
        var run = Run.Rehydrate(
            RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((streamName, 1UL)))).Value;

        run.StreamPosition(streamName).ShouldBe(1UL);
    }

    /// <summary>A null ad-use map is refused; an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_AdUses_map_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.WithNull(adUses: true));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("AdUses is null", Case.Sensitive);
    }

    /// <summary>A blank placement key is refused: a key names the placement it counts.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_ad_placement_key_is_refused(string placementId)
    {
        var result = Run.Rehydrate(
            RunSnapshots.With(adUses: RunSnapshots.AdUses((placementId, 1))));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("AdUses", Case.Sensitive);
        result.Error.ShouldContain("blank", Case.Sensitive);
    }

    /// <summary>A negative ad-use count is refused: a use counter counts upwards.</summary>
    [Fact]
    public void A_negative_ad_use_count_is_refused()
    {
        var result = Run.Rehydrate(
            RunSnapshots.With(adUses: RunSnapshots.AdUses(("AD_REVIVE", -1))));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("AdUses['AD_REVIVE'] is -1", Case.Sensitive);
    }

    /// <summary>
    /// An ad placement id the game does not author is <b>accepted</b>, because <c>Core</c> holds
    /// no placement catalogue: the in-run ids live in content tuning, and <c>AdPlacementId</c> is
    /// an <c>Application</c>-layer type.
    /// </summary>
    [Fact]
    public void An_unauthored_placement_id_is_accepted_because_the_catalogue_is_content()
    {
        var run = Run.Rehydrate(
            RunSnapshots.With(adUses: RunSnapshots.AdUses(("AD_NOT_YET_AUTHORED", 3)))).Value;

        run.AdUseCount("AD_NOT_YET_AUTHORED").ShouldBe(3);
    }

    /// <summary>Exact count AND identity of each fault: a count alone would hold for faults about the wrong fields.</summary>
    [Fact]
    public void Every_fault_in_a_row_is_reported_not_just_the_first()
    {
        var result = Run.Rehydrate(RunSnapshots.With(
            id: default(RunId),
            chapterId: 0,
            position: -2,
            gold: -7,
            adUses: RunSnapshots.AdUses(("AD_REVIVE", -1))));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("(5 problem(s))", Case.Sensitive);
        result.Error.ShouldContain("Id is blank or default(RunId)", Case.Sensitive);
        result.Error.ShouldContain("ChapterId", Case.Sensitive);
        result.Error.ShouldContain("Position", Case.Sensitive);
        result.Error.ShouldContain("Gold", Case.Sensitive);
        result.Error.ShouldContain("AdUses['AD_REVIVE'] is -1", Case.Sensitive);
    }

    /// <summary>
    /// Both maps are copied on the way in, into ordinal dictionaries — <c>CanonicalStateWriter</c>
    /// orders string keys ordinally, so any other comparer round-trips to a different hash.
    /// </summary>
    [Fact]
    public void A_map_the_caller_still_holds_cannot_reach_inside_the_aggregate()
    {
        var streams = new Dictionary<string, ulong>(StringComparer.Ordinal) { [RngStreams.Dice] = 4 };
        var adUses = new Dictionary<string, long>(StringComparer.Ordinal) { ["AD_REVIVE"] = 1 };

        var run = Run.Rehydrate(
            RunSnapshots.With(rngStreamPositions: streams, adUses: adUses)).Value;

        streams[RngStreams.Dice] = 99;
        streams[RngStreams.Board] = 1;
        adUses["AD_REVIVE"] = 99;
        adUses["AD_DOUBLE_CHEST"] = 1;

        run.StreamPosition(RngStreams.Dice).ShouldBe(4UL);
        run.StreamPosition(RngStreams.Board).ShouldBe(0UL);
        run.AdUseCount("AD_REVIVE").ShouldBe(1);
        run.AdUseCount("AD_DOUBLE_CHEST").ShouldBe(0);
    }

    // ---------------------------------------------------------------- ResolvedMinigames

    /// <summary>A null resolved-minigames map is refused; an absent map is not an empty one.</summary>
    [Fact]
    public void A_null_ResolvedMinigames_map_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.WithNull(resolvedMinigames: true));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("ResolvedMinigames is null", Case.Sensitive);
    }

    /// <summary>A blank minigame id at a position is refused: an entry names which MG_* id resolved.</summary>
    [Fact]
    public void A_blank_minigame_id_is_refused()
    {
        var result = Run.Rehydrate(
            RunSnapshots.With(resolvedMinigames: RunSnapshots.ResolvedMinigames((3, ""))));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("ResolvedMinigames", Case.Sensitive);
        result.Error.ShouldContain("blank", Case.Sensitive);
    }

    /// <summary>A position below the trailhead is refused — nothing could have resolved there.</summary>
    [Fact]
    public void A_resolved_minigame_below_the_trailhead_position_is_refused()
    {
        var result = Run.Rehydrate(
            RunSnapshots.With(resolvedMinigames: RunSnapshots.ResolvedMinigames((-2, "MG_CHEST_PICK"))));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("ResolvedMinigames", Case.Sensitive);
    }

    /// <summary>A well-formed row round-trips: the position and the id both arrive as persisted.</summary>
    [Fact]
    public void A_well_formed_ResolvedMinigames_row_rehydrates()
    {
        var run = Run.Rehydrate(
            RunSnapshots.With(resolvedMinigames: RunSnapshots.ResolvedMinigames(
                (3, "MG_CHEST_PICK"), (7, "MG_TIMING_BAR")))).Value;

        run.ResolvedMinigames[3].ShouldBe("MG_CHEST_PICK");
        run.ResolvedMinigames[7].ShouldBe("MG_TIMING_BAR");
        run.HasResolvedMinigameAt(3).ShouldBeTrue();
        run.HasResolvedMinigameAt(4).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ RequireBankedRewards faults

    /// <summary>Banked Legend XP is a pending grant — it never goes negative.</summary>
    [Fact]
    public void A_negative_BankedLegendXp_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.With(bankedLegendXp: -1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.BankedLegendXp), Case.Sensitive);
    }

    /// <summary>Banked Soul Shards are a pending grant — they never go negative.</summary>
    [Fact]
    public void A_negative_BankedSoulShards_is_refused()
    {
        var result = Run.Rehydrate(RunSnapshots.With(bankedSoulShards: -1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.BankedSoulShards), Case.Sensitive);
    }

    /// <summary>Faults accumulate: both pools negative reports both problems, not just the first.</summary>
    [Fact]
    public void Negative_banked_rewards_in_both_pools_accumulate()
    {
        var result = Run.Rehydrate(RunSnapshots.With(bankedLegendXp: -5, bankedSoulShards: -3));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.BankedLegendXp), Case.Sensitive);
        result.Error.ShouldContain(nameof(RunSnapshot.BankedSoulShards), Case.Sensitive);
    }

    /// <summary>…and the negative control: non-negative banked rewards rehydrate and read back.</summary>
    [Fact]
    public void A_well_formed_banked_rewards_row_rehydrates()
    {
        var run = Run.Rehydrate(RunSnapshots.With(bankedLegendXp: 40, bankedSoulShards: 15)).Value;

        run.BankedLegendXp.ShouldBe(40);
    }
}
