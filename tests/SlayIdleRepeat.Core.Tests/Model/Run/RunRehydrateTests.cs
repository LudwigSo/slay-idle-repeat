using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `30` §11.3 — <c>Run.Rehydrate</c> is <em>"one validated entry point for every persisted state in
/// the game — a corrupt row fails loudly at the seam rather than silently three rules later."</em>
/// One assertion per way a row can be wrong.
/// </summary>
/// <remarks>
/// 🔒 Every failure assertion pins <b>which</b> validation fired. <c>Rehydrate</c> reports every fault
/// it finds rather than the first, so a test that only checked <c>IsFailure</c> would pass for a row
/// invalid in some entirely different way — which is how a validation gets deleted without anything
/// going red.
/// <para>
/// 🔒 No <c>ContentSnapshot</c> parameter, unlike <c>Player.Rehydrate</c>: nothing <c>RunSnapshot</c>
/// carries has a content-derived bound today.
/// </para>
/// </remarks>
public sealed class RunRehydrateTests
{
    /// <summary>A valid row rehydrates, and every field arrives where it was persisted.</summary>
    /// <remarks>
    /// The positive half, and it is not a formality: it is what stops a validation being tightened
    /// into refusing states the game is legitimately in. Every field is asserted, because a
    /// constructor that dropped one — or crossed <c>CurrentHp</c> and <c>MaxHp</c> — would pass a
    /// test that only checked <c>IsSuccess</c>.
    /// </remarks>
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
            adUses: RunSnapshots.AdUses(("AD_REVIVE", 1), ("AD_REROLL_DICE", 2)));

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
        run.AdUseCount("AD_REROLL_DICE").ShouldBe(2);
        run.AdUseCount("AD_SHOP_FREEBIE").ShouldBe(0);
    }

    /// <summary>A null row is a caller bug, not a corrupt row, so it throws rather than failing.</summary>
    [Fact]
    public void A_null_snapshot_is_refused_by_the_argument_guard()
    {
        Should.Throw<ArgumentNullException>(() => Run.Rehydrate(null!));
    }

    /// <summary>
    /// 🔒 `14` §16.6 + the M1 kickoff ruling — an unknown <c>SchemaVersion</c> hard-fails, and the
    /// message says there is no migration rather than reading the row anyway.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The "one past the current" row is an expression, not a literal</b> — see
    /// <c>PlayerRehydrateTests</c>'s case of the same name for why M1-09 changed it. <c>RunSnapshot</c>
    /// did not move at the SchemaVersion 2 bump and this case went red all the same, which is the
    /// point: the pin is per <em>version</em>, not per record.
    /// </remarks>
    [Theory]
    [InlineData(0)]

    // 🔒 The version M1-09 orphans — see PlayerRehydrateTests' case of the same name. RunSnapshot's
    // own layout did not move at that bump, which is the point: the pin is per VERSION, so a v1
    // RunSnapshot is unreadable too.
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
    /// 🔒 The version check runs <b>first and alone</b>: a row from another schema is refused for
    /// being from another schema, not for whatever its fields happen to look like under this layout.
    /// </summary>
    /// <remarks>
    /// Steering <b>S2</b>. Without this, a wrong-version row that also had a negative position could
    /// report the position and let a reader "fix" the row instead of the version — and the case above
    /// would still pass, because its fragment would be in the joined message.
    /// </remarks>
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

    /// <summary>
    /// 🔒 <c>default(RunId)</c> never ran <c>RunId</c>'s constructor, so its <c>Value</c> is null.
    /// <c>RunId</c>'s own remarks name this seam as the one that has to catch it.
    /// </summary>
    [Fact]
    public void A_default_RunId_is_refused_because_its_constructor_never_ran()
    {
        var result = Run.Rehydrate(RunSnapshots.With(id: default(RunId)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Id is blank or default(RunId)", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 …and the same for the owning player. `30` §4 makes <c>Run</c> a child of <c>Player</c>, so
    /// a run that names no player is an orphan rather than a run.
    /// </summary>
    [Fact]
    public void A_default_PlayerId_is_refused_because_a_run_is_a_child_of_a_player()
    {
        var result = Run.Rehydrate(RunSnapshots.With(playerId: default(PlayerId)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("PlayerId is blank or default(PlayerId)", Case.Sensitive);
    }

    /// <summary>
    /// A chapter below 1 is refused — <c>chapter.schema.json</c> sets <c>"minimum": 1</c> and `02`
    /// §1 runs chapters from 1.
    /// </summary>
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
    /// ⚠️ A chapter <b>above</b> the eight `02` §1 names is <b>accepted</b>, deliberately — the
    /// assertion that keeps the chapter rule honest.
    /// </summary>
    /// <remarks>
    /// <c>content/chapters/</c> holds only chapters 1-2 (M3-14); chapters 3-8 are M11-02's
    /// unauthored rows, so the content set still does not span the full range and no ceiling is
    /// derivable from it. Hard-coding <c>8</c> would put a content bound in code and be a
    /// <em>partial</em> invariant masquerading as the real one; the deferral already has a
    /// self-expiring mechanism in <c>RealDataSetTests</c>, and this pins that <c>Core</c> did not
    /// grow a second one.
    /// </remarks>
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
    /// 🔒 An undefined <see cref="DifficultyTier"/> is refused, including the zero
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
    /// 🔒 A non-UTC <c>LastAppliedAtUtc</c> is refused. <c>CanonicalStateWriter</c> encodes a
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

    /// <summary>
    /// A position below `03` §1.1's trailhead is refused — and that is the whole position check
    /// (see M3-01).
    /// </summary>
    /// <remarks>
    /// ⚠️ The fault <b>count</b> is asserted beside the fragment, because <c>Position</c> is a
    /// substring of <c>RngStreamPositions</c>: on its own, that fragment would be satisfied by a
    /// message about the stream map and this case would pass while the position check was gone
    /// (steering S2).
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
    /// 🔒 …but the <b>trailhead itself</b> rehydrates, because that is where every run starts.
    /// </summary>
    /// <remarks>
    /// ⚠️ The case a floor of 0 would have got wrong. `03` §1.1 (ruled in `16` A7) begins every run
    /// at <em>"a virtual trailhead one step before node 0 (position −1)"</em>, and the movement
    /// arithmetic closes from there — <em>"a first roll of <c>1</c> therefore lands on node 0"</em>.
    /// A run that <c>START_RUN</c> (M3-15) created and the player left before their first
    /// <c>ROLL_DICE</c> is persisted at −1, and `14` §16.3's sliding 48-hour TTL exists precisely to
    /// let that row come back — so <c>Rehydrate</c> has to read it.
    /// </remarks>
    [Fact]
    public void The_trailhead_position_rehydrates_because_that_is_where_every_run_starts()
    {
        Run.Rehydrate(RunSnapshots.With(position: -1)).Value.Position.ShouldBe(-1);
    }

    /// <summary>
    /// ⚠️ …and a position no board could contain is <b>accepted</b>, because there is no board.
    /// </summary>
    /// <remarks>
    /// The converse half, and it is the one that keeps the deferral honest: `30` §11.5's <em>"a run's
    /// position is a valid node"</em> is registered against M3-01 in <c>GapRegister</c> rather than
    /// approximated here. If someone later invents a range check, this case turns red and points at
    /// the register entry instead of the invariant quietly becoming a guess.
    /// </remarks>
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
    /// 🔒 One defect is one fault. A row whose <c>MaxHp</c> is itself invalid must not <em>also</em>
    /// be reported for a current that exceeds it — the comparison is meaningless against a maximum
    /// the row does not have.
    /// </summary>
    /// <remarks>
    /// Steering <b>S2</b>, stated as an exact count rather than as a fragment: a reader handed two
    /// faults for one defect fixes the wrong one. <c>Player.RequireTimestamps</c> makes the same move
    /// for the game-day boundary checks and says so in a comment.
    /// </remarks>
    [Fact]
    public void An_invalid_MaxHp_is_reported_once_and_not_also_as_a_CurrentHp_overflow()
    {
        var result = Run.Rehydrate(RunSnapshots.With(currentHp: 5, maxHp: 0));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("MaxHp", Case.Sensitive);
        result.Error.ShouldContain("(1 problem(s))", Case.Sensitive);
    }

    /// <summary>`30` §11.5 — a currency never goes negative, checked at the seam as well.</summary>
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
    /// 🔒 A persisted stream name the `14` §8.1 registry does not recognise is refused, by the same
    /// predicate <c>DeterministicRng</c>'s constructor uses.
    /// </summary>
    /// <remarks>
    /// ⚠️ The cases are the ones <c>RngStreams.IsRegistered</c> is specified to separate:
    /// <c>minigame:03</c> is a <em>different string</em> from <c>minigame:3</c> and therefore a
    /// different sequence for what a human reads as the same minigame, and <c>DICE</c> pins that the
    /// comparison is ordinal. A row carrying one could never be drawn from.
    /// </remarks>
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

    /// <summary>
    /// …and every row the registry <b>does</b> recognise is accepted, including the parameterised
    /// ninth. Without this half, a validation that refused everything would pass the case above.
    /// </summary>
    [Fact]
    public void Every_row_of_the_registry_is_accepted_including_the_parameterised_minigame_row()
    {
        // ⚠️ index + 1, not index: a stream persisted at 0 is indistinguishable from one the row
        // never carried, because absent means 0. With a zero in the fixture, one of the ten
        // assertions below would hold for a validation that dropped that key entirely.
        var everyStream = RngStreams.FixedNames
            .Select((name, index) => (Stream: name, Position: (ulong)(index + 1)))
            .Append((Stream: RngStreams.Minigame(0), Position: 9UL))
            .Append((Stream: RngStreams.Minigame(7), Position: 10UL))
            .ToArray();

        everyStream.Length.ShouldBe(
            10,
            "eight fixed rows plus two minigame indices. A shrunken fixture would make the assertion " +
            "below hold over fewer streams than 14 §8.1 has.");

        var run = Run.Rehydrate(
            RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(everyStream))).Value;

        foreach (var (stream, position) in everyStream)
        {
            run.StreamPosition(stream).ShouldBe(position);
        }
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
    /// ⚠️ An ad placement id the game does not author is <b>accepted</b>, because <c>Core</c> holds
    /// no placement catalogue: the thirteen in-run ids live in <c>tuning/ads.json</c> and
    /// <c>AdPlacementId</c> is an <c>Application</c>-layer type (`12` §7).
    /// </summary>
    [Fact]
    public void An_unauthored_placement_id_is_accepted_because_the_catalogue_is_content()
    {
        var run = Run.Rehydrate(
            RunSnapshots.With(adUses: RunSnapshots.AdUses(("AD_NOT_YET_AUTHORED", 3)))).Value;

        run.AdUseCount("AD_NOT_YET_AUTHORED").ShouldBe(3);
    }

    /// <summary>
    /// 🔒 `30` §11.3 — a corrupt row reports <b>every</b> fault, not the first. One round trip per
    /// defect is one round trip too many when the row is already in production.
    /// </summary>
    /// <remarks>
    /// Stated as an exact count <em>and</em> as the identity of each fault (steering S2): a count
    /// alone would hold for five faults about the wrong five fields.
    /// </remarks>
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
    /// A map the caller still holds cannot reach inside the aggregate — both maps are copied on the
    /// way in, into ordinal dictionaries.
    /// </summary>
    /// <remarks>
    /// Ordinal because <c>CanonicalStateWriter</c> orders string keys ordinally, so a map that
    /// compared its keys any other way would round-trip to a different hash than the one it was
    /// stored under. <c>Player.ReadCounters</c> says the same thing about the counter maps.
    /// </remarks>
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

    // ---------------------------------------------------------------- M3-03c, ResolvedMinigames

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

    // ------------------------------------------------------------------ RequireBankedRewards faults (M3-13)

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

    /// <summary>🔒 Faults accumulate: both pools negative reports both problems, not just the first.</summary>
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
