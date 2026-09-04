using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// <c>MinigameView</c> — what a pending Minigame tile is offering, projected so its screen can show
/// the reward ladder and the chest guarantee before <c>MINIGAME_SUBMIT</c> resolves one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The preview and the payout are one number, and this suite asks Core rather than
/// transcribing.</b> Every reward assertion compares the projected row against what
/// <c>GameRules.Apply</c> really pays for that tier — a table figure written down here would agree
/// with a view that forgot the run's Gold modifiers, because the table figure is what such a view
/// would show.
/// </para>
/// <para>
/// 🔒 <b>The guarantee numbers are asked of <c>ChestPickGuarantee</c>.</b> The ordinal arithmetic is
/// <c>misses &gt;= everyNth - 1</c>, both readings of that key have been wrong in this repository
/// before, and a second statement of it in a test agrees with a second statement of it in the view.
/// So the expected countdown is computed by asking the rule which pick fires, over a fixture that
/// authors the ordinal at 3 and at 4 — the pair the two readings disagree on.
/// </para>
/// <para>
/// 🔴 <b>The pity counter is read through a key the shipped document does NOT author.</b> The
/// fixture's <c>MINIGAME</c> row carries <see cref="FixtureMinigameCounterKey"/>, so a view that
/// spelled <c>minigame.chestpick:GOLD</c> by hand finds nothing and reports a player who has stood
/// several misses as one who has stood none.
/// </para>
/// </remarks>
public sealed class MinigameViewTests
{
    /// <summary>
    /// The counter key the fixture authors for the minigame class — deliberately unlike the shipped
    /// one, so a hand-spelled key cannot read the fixture's counter by coincidence.
    /// </summary>
    private const string FixtureMinigameCounterKey = "fixture.minigame.chestpick";

    /// <summary>How many minigames the rules layer knows, so an empty <c>Ids</c> cannot pass.</summary>
    private const int KnownMinigameCount = 4;

    /// <summary>The node this suite's runs stand on. Nonzero, so a view keyed on 0 cannot agree.</summary>
    private const int Position = 5;

    // ------------------------------------------------------------------------------------------
    // The four arguments.
    // ------------------------------------------------------------------------------------------

    /// <summary>Each reference argument is named as the argument it is, not met as a dereference.</summary>
    /// <remarks>
    /// 🔒 Pinned by <c>ParamName</c> rather than by the exception type alone: a guard that fired for
    /// the wrong argument sends a caller to the content set when the run was what was missing, and
    /// every one of these passes a bare <see cref="ArgumentNullException"/> assertion.
    /// </remarks>
    [Fact]
    public void A_null_run_is_refused_by_name() =>
        Should.Throw<ArgumentNullException>(() =>
                MinigameView.Project(null!, AnyPlayer(), ShippedContent, MinigameCatalogue.TimingBar))
            .ParamName.ShouldBe("run");

    /// <inheritdoc cref="A_null_run_is_refused_by_name"/>
    [Fact]
    public void A_null_player_is_refused_by_name() =>
        Should.Throw<ArgumentNullException>(() =>
                MinigameView.Project(OnAMinigame(), null!, ShippedContent, MinigameCatalogue.TimingBar))
            .ParamName.ShouldBe("player");

    /// <inheritdoc cref="A_null_run_is_refused_by_name"/>
    [Fact]
    public void A_null_content_set_is_refused_by_name() =>
        Should.Throw<ArgumentNullException>(() =>
                MinigameView.Project(OnAMinigame(), AnyPlayer(), null!, MinigameCatalogue.TimingBar))
            .ParamName.ShouldBe("content");

    /// <summary>An id the catalogue does not carry is refused rather than projected as an empty table.</summary>
    [Fact]
    public void An_id_that_names_no_minigame_is_refused()
    {
        Should.Throw<ArgumentException>(() => MinigameView.Project(
            OnAMinigame(), AnyPlayer(), ShippedContent, "MG_NOT_A_REAL_MINIGAME"));
    }

    // ------------------------------------------------------------------------------------------
    // The ids.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The ids are the rules layer's own, not four literals a client would have to keep in step.
    /// </summary>
    /// <remarks>
    /// 🔴 The count is pinned rather than merely non-empty: an empty list would satisfy a
    /// "every id is known" sweep, and the client's built-arm list is derived from this one by
    /// subtraction, so an empty <c>Ids</c> would leave the screen offering nothing and say nothing.
    /// </remarks>
    [Fact]
    public void The_ids_are_the_four_the_rules_layer_knows()
    {
        MinigameView.Ids.Count.ShouldBe(
            KnownMinigameCount,
            "the catalogue knows four minigames and the view offered " + MinigameView.Ids.Count +
            ". The client derives its built arms from this list by removing the one it cannot draw, " +
            "so a list that grew or shrank silently changes which games a tile can open.");

        MinigameView.Ids.ShouldBe(
            [
                MinigameCatalogue.ChestPick,
                MinigameCatalogue.TimingBar,
                MinigameCatalogue.DiceDuel,
                MinigameCatalogue.MemoryRune,
            ],
            ignoreOrder: true,
            "the ids are copied out of the internal catalogue rather than re-spelled, so a rename " +
            "there moves them here rather than leaving two spellings of one id in the codebase.");
    }

    // ------------------------------------------------------------------------------------------
    // The gate.
    // ------------------------------------------------------------------------------------------

    /// <summary>A run standing on no tile at all is offering no minigame.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_projects_nothing() =>
        MinigameView.Project(
            NoPendingTile(), AnyPlayer(), ShippedContent, MinigameCatalogue.TimingBar).ShouldBeNull();

    /// <summary>…and a pending tile of another kind is not a minigame either.</summary>
    [Theory]
    [InlineData((int)TileKind.Campfire)]
    [InlineData((int)TileKind.Event)]
    [InlineData((int)TileKind.Shop)]
    public void A_pending_tile_of_another_kind_projects_nothing(int kind) =>
        MinigameView.Project(
                Run(pendingTileKind: kind), AnyPlayer(), ShippedContent, MinigameCatalogue.TimingBar)
            .ShouldBeNull();

    // ------------------------------------------------------------------------------------------
    // The authority split.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Which arms the server rolls for itself, and which two take the client at its word.
    /// </summary>
    /// <remarks>
    /// 🔴 Both answers in one sweep, because the claim is the split: a view answering <c>true</c> for
    /// everything satisfies half these rows and a view answering <c>false</c> satisfies the other
    /// half. The screen decides from this whether to submit a tier it played for or a placeholder the
    /// handler ignores, so getting it backwards submits a claimed tier on a game the server rolls.
    /// </remarks>
    [Theory]
    [InlineData("MG_CHEST_PICK", true)]
    [InlineData("MG_DICE_DUEL", true)]
    [InlineData("MG_TIMING_BAR", false)]
    [InlineData("MG_MEMORY_RUNE", false)]
    public void The_server_rolled_arms_are_named_and_the_client_asserted_ones_are_not(
        string minigameId, bool expectedServerRolled) =>
        Projected(minigameId).IsServerRolled.ShouldBe(
            expectedServerRolled,
            minigameId + " is reported as " + (expectedServerRolled ? "client-asserted" : "server-rolled") +
            ". The screen decides from this whether the tier it submits is one the player earned or a " +
            "placeholder the handler throws away, and the two are not interchangeable.");

    // ------------------------------------------------------------------------------------------
    // The per-tile legality gate.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 A run that has already resolved a minigame at the node it stands on says so.
    /// </summary>
    /// <remarks>
    /// 🔴 Both arms, and the second is keyed at a DIFFERENT node: the gate is per tile instance, so a
    /// view that answered "already resolved" from the map being non-empty would close every later
    /// tile of a run that played one minigame.
    /// </remarks>
    [Fact]
    public void A_minigame_already_resolved_at_this_node_is_reported_and_one_elsewhere_is_not()
    {
        var here = Run(resolvedMinigames: new Dictionary<int, string>
        {
            [Position] = MinigameCatalogue.DiceDuel,
        });

        var elsewhere = Run(resolvedMinigames: new Dictionary<int, string>
        {
            [Position + 1] = MinigameCatalogue.DiceDuel,
        });

        Projected(MinigameCatalogue.TimingBar, here).AlreadyResolvedHere.ShouldBeTrue(
            "MINIGAME_SUBMIT refuses a second submission at this run's position, so a screen that " +
            "still offered a press would offer one the rules layer turns away.");
        Projected(MinigameCatalogue.TimingBar, elsewhere).AlreadyResolvedHere.ShouldBeFalse(
            "the gate is per TILE INSTANCE. A view reading it off the map being non-empty would " +
            "close every minigame tile a run met after its first.");
        Projected(MinigameCatalogue.TimingBar).AlreadyResolvedHere.ShouldBeFalse(
            "…and a run that has resolved nothing at all is plainly not resolved here.");
    }

    // ------------------------------------------------------------------------------------------
    // The reward rows: preview equals payout.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>Every projected row is what an accepted submission at that tier actually pays.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Asked of the handler, not read off the table.</b> Gold is paid through the run's own
    /// Gold modifiers at the income site, so the reward table's figure and the payout are different
    /// numbers on any run carrying a shrine buff or a curse — and a view that showed the table figure
    /// would agree with every assertion written against the table. This run carries the Gold-gain
    /// shrine buff for exactly that reason.
    /// </para>
    /// <para>
    /// Two chapters, because the rows are chapter-scaled and one chapter cannot tell a scaled view
    /// from an unscaled one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 3)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public void A_projected_row_is_what_an_accepted_submission_at_that_tier_pays(int chapterId, int tier)
    {
        var row = RowFor(MinigameCatalogue.TimingBar, tier, chapterId, GoldBuffed);
        var paid = Paid(MinigameCatalogue.TimingBar, tier, chapterId, GoldBuffed);

        row.Gold.ShouldBe(
            paid.Gold,
            "the screen promised " + row.Gold + " Gold at tier " + tier + " of chapter " + chapterId +
            " and the accepted submission paid " + paid.Gold + ". Gold income is scaled by the run's " +
            "own modifiers at the income site, so a preview taken straight off the reward table " +
            "overstates a cursed run's winnings and understates a buffed one's.");
        row.Crowns.ShouldBe(paid.Crowns);
        row.BeastFeed.ShouldBe(paid.BeastFeed);
        row.EnhanceStones.ShouldBe(paid.EnhanceStones);
    }

    /// <summary>
    /// 🔒 …and the preview really is the SCALED number, not the table's own.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control for the case above. Both halves of a comparison could be wrong
    /// together if the handler stopped scaling too, so this holds the projection against the raw
    /// authored figure and requires them to differ on a run whose modifier is non-zero.
    /// </remarks>
    [Fact]
    public void The_previewed_Gold_is_the_scaled_figure_and_not_the_tables_own()
    {
        const int PayingTier = 3;

        var authored = MinigameRewardTuning.Read(ShippedContent)
                                           .RewardFor(MinigameCatalogue.TimingBar, PayingTier, 1);
        var row = RowFor(MinigameCatalogue.TimingBar, PayingTier, chapterId: 1, GoldBuffed);

        authored.Gold.ShouldBeGreaterThan(
            0L, "a row paying no Gold cannot show whether a Gold modifier was applied to it.");
        row.Gold.ShouldNotBe(
            authored.Gold,
            "the run carries a Gold-gain shrine buff and the preview came back at the table's " +
            "unmodified figure, so the screen is showing a number the payout will not match.");
    }

    /// <summary>The rows come back in ascending tier order, each carrying its own authored token.</summary>
    /// <remarks>
    /// 🔒 The tier IS the wire value a client-asserted submission carries, so a row whose
    /// <c>Tier</c> is not its position submits a different outcome than the one the player is
    /// looking at — and every tier of the table is a legal claim, so nothing refuses it.
    /// </remarks>
    [Theory]
    [InlineData("MG_CHEST_PICK", 3)]
    [InlineData("MG_TIMING_BAR", 4)]
    [InlineData("MG_DICE_DUEL", 3)]
    public void The_rows_are_the_authored_tiers_in_order_each_naming_its_own_outcome(
        string minigameId, int expectedTiers)
    {
        var view = Projected(minigameId);
        var tuning = MinigameRewardTuning.Read(ShippedContent);

        view.Rows.Count.ShouldBe(
            expectedTiers,
            minigameId + " authors " + expectedTiers + " outcome tiers and the view drew " +
            view.Rows.Count + ". A screen showing fewer hides an outcome the rules layer pays.");
        view.Rows.Count.ShouldBe(
            tuning.TierCount(minigameId), "…and the count is the document's own, not a transcription.");

        for (var tier = 0; tier < view.Rows.Count; tier++)
        {
            view.Rows[tier].Tier.ShouldBe(
                tier,
                "row " + tier + " of " + minigameId + " carries tier " + view.Rows[tier].Tier +
                ". The tier is what MINIGAME_SUBMIT travels as on a client-asserted arm, so a row " +
                "whose tier is not its position claims an outcome the player did not play for.");
            view.Rows[tier].Outcome.ShouldBe(
                tuning.OutcomeName(minigameId, tier),
                "row " + tier + " of " + minigameId + " is named with another row's token, so the " +
                "screen captions one outcome with another's words.");
        }
    }

    /// <summary>
    /// 🔒 Currency columns scale with the chapter; the fixed-dice column does not.
    /// </summary>
    /// <remarks>
    /// 🔴 The pair is the claim. A view that scaled everything and one that scaled nothing each
    /// satisfy exactly one half — and scaling the die column would hand a late-chapter dice duel a
    /// fistful of dice for winning one game.
    /// </remarks>
    [Fact]
    public void The_currency_columns_scale_with_the_chapter_and_the_fixed_die_column_does_not()
    {
        const int DiceDuelTopTier = 2;

        var early = RowFor(MinigameCatalogue.DiceDuel, DiceDuelTopTier, chapterId: 1);
        var late = RowFor(MinigameCatalogue.DiceDuel, DiceDuelTopTier, chapterId: 3);

        early.FixedDice.ShouldBeGreaterThan(
            0L,
            "this is the one shipped row that grants a fixed die, and with none granted the " +
            "unscaled half of this case is comparing zero with zero.");

        late.Gold.ShouldBeGreaterThan(
            early.Gold,
            "the reward table is Chapter-1 base values scaled by chapter, so a later chapter's row " +
            "pays more Gold. A view ignoring the run's chapter shows a chapter-6 player the " +
            "chapter-1 winnings.");
        late.Crowns.ShouldBeGreaterThan(early.Crowns);
        late.FixedDice.ShouldBe(
            early.FixedDice,
            "a fixed die is one die whatever chapter it was won in, and it is the only column that " +
            "is not scaled. Scaling it turns one authored die into a handful.");
    }

    // ------------------------------------------------------------------------------------------
    // The chest guarantee.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The guarantee is the chest pick's alone — the other three carry no counter at all.
    /// </summary>
    [Theory]
    [InlineData("MG_TIMING_BAR")]
    [InlineData("MG_DICE_DUEL")]
    [InlineData("MG_MEMORY_RUNE")]
    public void A_skill_scaled_minigame_projects_no_guarantee_at_all(string minigameId) =>
        Projected(minigameId).Guarantee.ShouldBeNull(
            minigameId + " came back carrying a guarantee. Only the chest pick has a pity counter; " +
            "a countdown drawn on a skill-scaled game is a promise nothing in the rules layer keeps.");

    /// <summary>…and the chest pick projects one.</summary>
    [Fact]
    public void The_chest_pick_projects_its_guarantee() =>
        Projected(MinigameCatalogue.ChestPick).Guarantee.ShouldNotBeNull(
            "the chest pick is the one minigame carrying a pity counter, and the screen's guarantee " +
            "line has nothing to say without it.");

    /// <summary>
    /// 🔒 <b>The countdown is the guarantee rule's own answer, at both readings of the authored key.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 The expected numbers are computed by asking <c>ChestPickGuarantee.GuaranteeFires</c> which
    /// pick fires, never by re-deriving <c>misses &gt;= everyNth - 1</c> here. A test that restated
    /// the arithmetic would agree with a view that restated it the other way round, which is the
    /// exact failure this key has already had in this repository.
    /// </para>
    /// <para>
    /// 🔴 Two authored ordinals, 3 and 4, and two standing counts. Under the off-by-one reading the
    /// countdown at <c>N = 4</c> equals the countdown at <c>N = 3</c> under the right one, so a
    /// single ordinal cannot separate them.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    [InlineData(4, 2)]
    public void The_guarantee_counts_down_the_way_the_guarantee_rule_says_it_does(
        int guaranteeOnNthPick, int picksStood)
    {
        var content = GuaranteeContent(guaranteeOnNthPick);
        var counterKey = ChestPickCounterKeyOf(content);
        var rule = LuckTuning.Read(content).ChestPick;

        rule.GuaranteeOnNthPick.ShouldBe(
            guaranteeOnNthPick,
            "the fixture did not take the ordinal this row is about, so both rows below are asking " +
            "the same question and the pair stops separating the two readings.");

        var expectedUntilForced = PicksUntilForcedAsked(rule, picksStood);

        var view = Projected(
            MinigameCatalogue.ChestPick,
            OnAMinigame(),
            PlayerStanding(counterKey, picksStood),
            content);

        var guarantee = view.Guarantee.ShouldNotBeNull();

        guarantee.PicksStood.ShouldBe(
            picksStood,
            "the profile's counter reads " + picksStood + " at '" + counterKey + "' and the view " +
            "reported " + guarantee.PicksStood + ". This key is NOT the shipped one, so a view " +
            "spelling the counter id by hand reads an absent counter and reports a clean slate.");
        guarantee.PicksUntilForced.ShouldBe(
            expectedUntilForced,
            "the guarantee rule says the forced pick is " + expectedUntilForced + " away and the " +
            "view said " + guarantee.PicksUntilForced + ". The ordinal is misses >= everyNth - 1, " +
            "and a view restating it the other way shifts every countdown in the game by one pick.");
        guarantee.ForcedOnPick.ShouldBe(
            picksStood + expectedUntilForced + 1,
            "the forced pick's own ordinal within this streak has to agree with the countdown " +
            "beside it — the line names both numbers, and two numbers that disagree are worse than " +
            "one.");
    }

    /// <summary>
    /// 🔒 The fixture's counter key really is not the shipped one, so the case above discriminates.
    /// </summary>
    /// <remarks>
    /// 🔴 Without this the whole guarantee sweep could be passing because the fixture quietly took
    /// the shipped key back, and a hand-spelled counter id would read correctly by coincidence.
    /// </remarks>
    [Fact]
    public void The_fixture_counter_key_is_deliberately_not_the_shipped_one()
    {
        var fixtureKey = ChestPickCounterKeyOf(GuaranteeContent(LuckDocuments.ShippedMinigameChestPickN));
        var shippedKey = ChestPickCounterKeyOf(ShippedContent);

        fixtureKey.ShouldStartWith(FixtureMinigameCounterKey, Case.Sensitive);
        fixtureKey.ShouldNotBe(
            shippedKey,
            "the fixture counter key has drifted back onto the shipped one, so a view that spelled " +
            "'" + shippedKey + "' by hand instead of composing it through LuckTuning would satisfy " +
            "every guarantee case above.");
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 Projecting moves nothing.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 A minigame that was LOOKED at resolves byte-for-byte like one that was not.
    /// </summary>
    /// <remarks>
    /// The server-rolled draw belongs to the command. A projection that opened the minigame stream
    /// and let it advance would leave the pick resolving against a different roll than the screen was
    /// showing — invisible in the projection itself, so the run's canonical bytes are taken on both
    /// sides and the resolution is compared against a run that was never projected.
    /// </remarks>
    [Fact]
    public void A_minigame_that_was_projected_resolves_exactly_like_one_that_was_not()
    {
        var looked = Worlds.InARun(RunRow(chapterId: 1));
        var before = CanonicalStateWriter.CanonicalBytes(looked.Run!.ToSnapshot());

        MinigameView.Project(
            looked.Run.ToSnapshot(), AnyPlayer(), ShippedContent, MinigameCatalogue.ChestPick);

        CanonicalStateWriter.CanonicalBytes(looked.Run.ToSnapshot()).ShouldBe(
            before,
            "projecting the offer moved the run's own row. The view is handed a snapshot rather " +
            "than the aggregate, so the only way it can do that is by writing through one of the " +
            "collections the row lent it — which is what advancing the minigame stream inside a " +
            "projection would look like.");

        var blind = Worlds.InARun(RunRow(chapterId: 1));

        var afterLooking = SlayIdleRepeat.Core.GameRules.Apply(
            looked, new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 0), Worlds.Context);
        var afterBlind = SlayIdleRepeat.Core.GameRules.Apply(
            blind, new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 0), Worlds.Context);

        afterLooking.Accepted.ShouldBeTrue(
            "with the submission refused there is no resolution to compare and this case measures " +
            "nothing.");
        CanonicalStateWriter.CanonicalBytes(afterLooking.NewState.Run!.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.CanonicalBytes(afterBlind.NewState.Run!.ToSnapshot()),
            "looking at the chest pick changed what picking did. The tier is drawn off the run's " +
            "minigame stream, so a projection that opened and advanced it moves the outcome the " +
            "player actually gets.");
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    /// <summary>The Gold-gain shrine buff, so the preview and the payout have a modifier to disagree over.</summary>
    private static readonly IReadOnlyList<string> GoldBuffed = ["SHR_GOLD"];

    private static ContentSnapshot ShippedContent => Worlds.Context.Content;

    /// <summary>
    /// The shipped set with <c>tuning/luck.json</c> replaced: the minigame class's counter key is the
    /// fixture's, and the chest guarantee fires on the ordinal a case names.
    /// </summary>
    private static ContentSnapshot GuaranteeContent(int guaranteeOnNthPick) =>
        ShippedHarness.WithShippedGaps(LuckDocuments.LuckOnly(
            sourceClasses: FixtureSourceClasses,
            minigameGuaranteeOnNthPick: ContentValue.Number(guaranteeOnNthPick)));

    /// <summary>
    /// The ten source-class rows, shipped but for the minigame's counter key.
    /// </summary>
    /// <remarks>
    /// Authored here rather than borrowed, because the ONE row this suite needs changed is the one
    /// the shared fixture has no parameter for — and the change is the point: a counter key nothing
    /// in <c>game-data</c> authors is what stops a hand-spelled key reading the right counter.
    /// </remarks>
    private static ContentValue FixtureSourceClasses { get; } = ContentValue.Array(
    [
        SourceClassRow("CHEST_STANDARD", LuckDocuments.ShippedChestStandardCounterKey, "PLAYER"),
        SourceClassRow("CHEST_PREMIUM", LuckDocuments.ShippedChestPremiumCounterKey, "PLAYER"),
        SourceClassRow("CHEST_APEX", LuckDocuments.ShippedChestApexCounterKey, "PLAYER"),
        SourceClassRow("DROP_RUN", LuckDocuments.ShippedDropRunCounterKey, "PLAYER"),
        SourceClassRow("EGG_PET", LuckDocuments.ShippedEggPetCounterKey, "PLAYER"),
        SourceClassRow("CRATE_MOUNT", LuckDocuments.ShippedCrateMountCounterKey, "PLAYER"),
        SourceClassRow("ENHANCE", counterKey: null, "GEAR_INSTANCE"),
        SourceClassRow("DRAFT", counterKey: null, "RUN"),
        SourceClassRow("WHEEL", LuckDocuments.ShippedWheelCounterKey, "PLAYER"),
        SourceClassRow("MINIGAME", FixtureMinigameCounterKey, "PLAYER"),
    ]);

    private static ContentValue SourceClassRow(string id, string? counterKey, string scope) =>
        ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("id", ContentValue.Text(id)),
            new KeyValuePair<string, ContentValue>(
                "counterKey",
                counterKey is null ? ContentValue.Unauthorised : ContentValue.Text(counterKey)),
            new KeyValuePair<string, ContentValue>("counterScope", ContentValue.Text(scope)),
        ]);

    /// <summary>
    /// The counter id the chest pick's guarantee is stored under, composed the one way it may be.
    /// </summary>
    private static string ChestPickCounterKeyOf(ContentSnapshot content)
    {
        var tuning = MinigameRewardTuning.Read(content);
        var topTier = ChestPickGuarantee.TopTier(tuning.TierCount(MinigameCatalogue.ChestPick));

        return LuckTuning.Read(content).CounterKey(
            SourceClass.MINIGAME, tuning.OutcomeName(MinigameCatalogue.ChestPick, topTier));
    }

    /// <summary>
    /// How many further picks the guarantee rule itself says must be stood — asked, never derived.
    /// </summary>
    private static int PicksUntilForcedAsked(ChestPickRule rule, int picksStood)
    {
        for (var further = 0; further <= rule.GuaranteeOnNthPick; further++)
        {
            if (ChestPickGuarantee.GuaranteeFires(rule, picksStood + further))
            {
                return further;
            }
        }

        throw new InvalidOperationException(
            "The guarantee never fires within its own authored ordinal, so this case has no " +
            "expected countdown to compare against and would otherwise pass on whatever the view said.");
    }

    /// <summary>What one accepted submission at a tier really paid, read off the outcome.</summary>
    private static (long Gold, long Crowns, long BeastFeed, long EnhanceStones) Paid(
        string minigameId, int tier, int chapterId, IReadOnlyList<string>? shrineBuffs = null)
    {
        var before = Worlds.InARun(RunRow(chapterId, shrineBuffs));

        var outcome = SlayIdleRepeat.Core.GameRules.Apply(
            before, new MinigameSubmitCommand(minigameId, tier), Worlds.Context);

        outcome.Accepted.ShouldBeTrue(
            "the submission this case measures the payout from was refused, so there is no payout " +
            "and every column below would compare zero with zero.");

        return (
            outcome.NewState.Run!.Gold - before.Run!.Gold,
            Moved(outcome, CurrencyId.CROWNS),
            Moved(outcome, CurrencyId.BEAST_FEED),
            Moved(outcome, CurrencyId.ENHANCE_STONES));
    }

    private static long Moved(CommandResult outcome, CurrencyId currency)
    {
        long total = 0;

        foreach (var change in outcome.Events.OfType<SlayIdleRepeat.Core.Events.CurrencyChanged>())
        {
            if (change.Id == currency)
            {
                total += change.Delta;
            }
        }

        return total;
    }

    private static MinigameTierRow RowFor(
        string minigameId, int tier, int chapterId, IReadOnlyList<string>? shrineBuffs = null)
    {
        var view = Projected(
            minigameId, RunRow(chapterId, shrineBuffs), AnyPlayer(), ShippedContent);

        tier.ShouldBeLessThan(
            view.Rows.Count, "tier " + tier + " is not a row of " + minigameId + "'s table.");

        return view.Rows[tier];
    }

    private static MinigameView Projected(
        string minigameId,
        RunSnapshot? run = null,
        PlayerSnapshot? player = null,
        ContentSnapshot? content = null) =>
        MinigameView.Project(
            run ?? OnAMinigame(), player ?? AnyPlayer(), content ?? ShippedContent, minigameId)
        ?? throw new InvalidOperationException(
            "the fixture run is standing on a Minigame tile, so '" + minigameId +
            "' has an offer to project");

    private static PlayerSnapshot AnyPlayer() => PlayerSnapshots.Valid;

    /// <summary>A profile standing the given number of consecutive misses on one counter.</summary>
    private static PlayerSnapshot PlayerStanding(string counterKey, int picks) =>
        PlayerSnapshots.With(pityCounters: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [counterKey] = picks,
        });

    private static RunSnapshot OnAMinigame() => Run();

    private static RunSnapshot NoPendingTile() =>
        RunSnapshots.With(position: Position, pendingTileKind: -1);

    private static RunSnapshot Run(
        int pendingTileKind = (int)TileKind.Minigame,
        int chapterId = 1,
        IReadOnlyDictionary<int, string>? resolvedMinigames = null,
        IReadOnlyList<string>? shrineBuffs = null) =>
        RunSnapshots.With(
            chapterId: chapterId,
            position: Position,
            pendingTileKind: pendingTileKind,
            pendingTileLinearIndex: Position,
            pendingTileStage: 1,
            resolvedMinigames: resolvedMinigames,
            shrineBuffs: shrineBuffs);

    private static RunSnapshot RunRow(int chapterId, IReadOnlyList<string>? shrineBuffs = null) =>
        Run(chapterId: chapterId, shrineBuffs: shrineBuffs);
}
