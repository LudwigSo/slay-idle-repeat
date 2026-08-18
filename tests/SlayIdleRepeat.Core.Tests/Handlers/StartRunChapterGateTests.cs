using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// START_RUN enforces `10` §7's chapter/tier ladder server-side: the clear a tier demands, then the
/// Legend Level it demands, each with its own named refusal.
/// </summary>
/// <remarks>
/// <para>
/// `14` §9 makes command validation the server's job — "is the action legal now" — so the ladder is
/// answered here and not only on the screen that draws it. The chapter select presenter still lists
/// <em>every</em> unmet requirement; this handler answers with one, because
/// <c>HandlerResult.Reject</c> carries a single enum value and no detail payload. Which of the two it
/// answers with is pinned below rather than left to whichever check happens to be written first.
/// </para>
/// <para>
/// Driven through <c>GameRules.Apply</c> over the production dispatch table, so these cases prove the
/// real START_RUN row rather than a stand-in shaped like it.
/// </para>
/// <para>
/// 🔴 <b>Every <c>RunsStarted</c> assertion below is a statement about <c>Apply</c>, not about this
/// handler, and was proved so by mutation.</b> Moving <c>Player.BeginRun()</c> <em>above</em> the
/// ladder gate — spending the lifetime counter on every refusal — leaves all thirty-eight cases in
/// the two gate suites green, and the whole Core suite green with it. The reason is structural:
/// <c>Apply</c> runs the handler against a <em>clone</em> and, on a rejection, returns the caller's
/// own slice, so the only object whose counter moved is discarded unread. "A refused START_RUN costs
/// the player nothing" is therefore bought by that discard and not by the handler's ordering, and
/// nothing in this repository can tell the two apart. The ordering inside <c>Handle</c> is kept
/// because it is right, not because it is pinned — the lines below restate
/// <c>NewState.ShouldBeSameAs(state)</c> in another spelling, and would only ever redden if
/// <c>Apply</c> stopped discarding.
/// </para>
/// <para>
/// 🔒 The Legend Level the Mythic rung demands is read out of <see cref="Worlds.Context"/>'s own
/// content, once, and reused for the "one below" and "exactly at" cases. Writing it as a literal
/// would put the number in C# — invisible to every architecture rule that watches tuning (S18) — and
/// would make both cases pass against a gate that had stopped reading the document.
/// </para>
/// </remarks>
public sealed class StartRunChapterGateTests
{
    /// <summary>The Legend Level the shipped Mythic rung demands, read from the content the gate reads.</summary>
    private static readonly int MythicLegendLevel = Worlds.Context.Content.ReadInt32(
        $"{ChapterGatingTuning.GatingReference}/{DifficultyTier.MYTHIC}/requiresLegendLevel");

    /// <summary>A run-less slice whose player has cleared exactly the given chapter/tier pairs.</summary>
    /// <param name="cleared">The clear history. Empty is a brand-new account.</param>
    /// <param name="legendLevel">The Legend Level, or <c>null</c> for the starting one.</param>
    /// <remarks>
    /// The clear keys are built with <c>Player.ChapterTierKey</c> rather than spelled out here. A
    /// second transcription of the key format would go stale silently in exactly the direction that
    /// hides a defect: every refusal case below would keep passing over a history the aggregate could
    /// no longer read as a clear.
    /// </remarks>
    private static WorldSlice Outside(
        (int Chapter, DifficultyTier Tier)[] cleared, int? legendLevel = null) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                legendLevel: legendLevel,
                clearedChapterTiers: PlayerSnapshots.Counters(
                    cleared.Select(row => (PlayerAggregate.ChapterTierKey(row.Chapter, row.Tier), 1L)).ToArray()))),
            null);

    /// <summary>A brand-new account: nothing cleared, at the starting Legend Level.</summary>
    private static WorldSlice Fresh() => Outside([]);

    private static CommandResult Start(WorldSlice state, int chapter, DifficultyTier tier) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new StartRunCommand(chapter, tier), Worlds.Context);

    // ------------------------------------------------------------------ the ladder lets the right runs through

    /// <summary>
    /// Negative control: chapter 1 Normal is open to a brand-new account, so the gate refuses
    /// specific states rather than everything.
    /// </summary>
    /// <remarks>
    /// The Normal rung names the chapter <em>before</em> this one and there is none before chapter 1,
    /// so it resolves to nothing. Without this case a gate that refused every START_RUN outright
    /// would satisfy every other refusal case in this file.
    /// </remarks>
    [Fact]
    public void Chapter_1_Normal_is_accepted_for_a_brand_new_account()
    {
        var result = Start(Fresh(), 1, DifficultyTier.NORMAL);

        result.Accepted.ShouldBeTrue(
            "10 §7 puts no prerequisite on chapter 1 Normal — it is where every account starts. A " +
            "refusal here is the game locked against every new player.");
        result.NewState.Run.ShouldNotBeNull();
    }

    /// <summary>Chapter 2 Normal opens once chapter 1 has been cleared on Normal.</summary>
    [Fact]
    public void Chapter_2_Normal_is_accepted_once_chapter_1_Normal_is_cleared()
    {
        var result = Start(Outside([(1, DifficultyTier.NORMAL)]), 2, DifficultyTier.NORMAL);

        result.Accepted.ShouldBeTrue(
            "10 §7: chapter 2 Normal is unlocked by clearing chapter 1 Normal, and this player has. " +
            "A refusal here would mean the gate is not reading the clear history at all.");
        result.NewState.Run!.ChapterId.ShouldBe(2);
    }

    /// <summary>Chapter 1 Heroic opens once chapter 1 has been cleared on Normal — the <c>SAME_CHAPTER_NORMAL</c> arm.</summary>
    [Fact]
    public void Chapter_1_Heroic_is_accepted_once_chapter_1_Normal_is_cleared()
    {
        var result = Start(Outside([(1, DifficultyTier.NORMAL)]), 1, DifficultyTier.HEROIC);

        result.Accepted.ShouldBeTrue(
            "10 §7: chapter c Heroic is unlocked by clearing chapter c Normal — this chapter, not the " +
            "one before it. A refusal here means the Heroic rung is being resolved against the " +
            "Normal rung's token.");
        result.NewState.Run!.Tier.ShouldBe(DifficultyTier.HEROIC);
    }

    /// <summary>
    /// Chapter 1 Mythic opens at exactly the authored Legend Level, with the Heroic clear in hand.
    /// </summary>
    /// <remarks>
    /// Paired with the "one below" case, this pins the comparison as <c>&lt;</c> and not
    /// <c>&lt;=</c>: a gate written one level tight locks Mythic for every player who reaches the
    /// authored level and stops there, and nothing but the boundary pair notices.
    /// </remarks>
    [Fact]
    public void Chapter_1_Mythic_is_accepted_at_exactly_the_authored_Legend_Level()
    {
        var result = Start(
            Outside([(1, DifficultyTier.HEROIC)], legendLevel: MythicLegendLevel), 1, DifficultyTier.MYTHIC);

        result.Accepted.ShouldBeTrue(
            $"the ladder demands Legend Level {MythicLegendLevel} and this player is exactly there. " +
            "The level gate opens AT the rung — a refusal here is every Mythic run in the game " +
            "locked one level late, and the level gate is not a permanent block.");
        result.NewState.Run!.Tier.ShouldBe(DifficultyTier.MYTHIC);
    }

    // ------------------------------------------------------------------------------ the clear refusal

    /// <summary>Chapter 2 Normal is refused for an account that has cleared nothing.</summary>
    [Fact]
    public void Chapter_2_Normal_is_refused_for_an_account_that_has_cleared_nothing()
    {
        var state = Fresh();
        var runsStartedBefore = state.Player.RunsStarted;

        var result = Start(state, 2, DifficultyTier.NORMAL);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.PREREQUISITE_NOT_CLEARED,
            "10 §7 gates chapter 2 Normal on clearing chapter 1 Normal, and 14 §9 makes that the " +
            "server's answer rather than the screen's. A client that skipped the chapter select " +
            "screen must not be able to start a chapter it has not opened.");

        result.NewState.ShouldBeSameAs(
            state, "a rejected command's NewState is the caller's own slice, untouched.");
        result.NewState.Player.RunsStarted.ShouldBe(
            runsStartedBefore,
            "a refused START_RUN does not spend the lifetime run counter. 🔴 Read the class remarks " +
            "before trusting this line: it restates the assertion above rather than probing the " +
            "handler, because the counter GameRules.Apply discards on a rejection is the one the " +
            "handler could have spent.");
    }

    /// <summary>
    /// A different shape of the same refusal: the missing clear is not the first rung.
    /// </summary>
    /// <remarks>
    /// Chapter 3 with only chapter 1 cleared. A gate implemented as "has this account cleared
    /// anything at all" passes the case above and fails this one, which is why both are here.
    /// </remarks>
    [Fact]
    public void Chapter_3_Normal_is_refused_for_an_account_that_has_only_cleared_chapter_1()
    {
        var state = Outside([(1, DifficultyTier.NORMAL)]);
        var runsStartedBefore = state.Player.RunsStarted;

        var result = Start(state, 3, DifficultyTier.NORMAL);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.PREREQUISITE_NOT_CLEARED,
            "the Normal rung names the chapter immediately before this one — chapter 2, which this " +
            "account has not cleared. A gate satisfied by any clear at all would let a player who " +
            "finished chapter 1 jump straight to chapter 8.");
        result.NewState.Player.RunsStarted.ShouldBe(runsStartedBefore);
    }

    /// <summary>Chapter 1 Heroic is refused before chapter 1 Normal has been cleared.</summary>
    /// <remarks>
    /// The tier axis rather than the chapter axis: chapter 1 is open on Normal for this very same
    /// account, so a gate keyed on the chapter alone would let this through.
    /// </remarks>
    [Fact]
    public void Chapter_1_Heroic_is_refused_before_chapter_1_Normal_is_cleared()
    {
        var state = Fresh();
        var runsStartedBefore = state.Player.RunsStarted;

        var result = Start(state, 1, DifficultyTier.HEROIC);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.PREREQUISITE_NOT_CLEARED,
            "10 §7 gates chapter 1 Heroic on clearing chapter 1 Normal. This same account may start " +
            "chapter 1 on NORMAL, so a gate that looked only at the chapter would open this.");
        result.NewState.Run.ShouldBeNull(
            "a refused START_RUN attaches no run. The slice went in run-less and must come back that " +
            "way — a run attached beside a rejection is a run nothing will ever end.");
        result.NewState.Player.RunsStarted.ShouldBe(runsStartedBefore);
    }

    // ------------------------------------------------------------------------------ the level refusal

    /// <summary>Chapter 1 Mythic is refused one Legend Level below the authored rung.</summary>
    [Fact]
    public void Chapter_1_Mythic_is_refused_one_Legend_Level_below_the_authored_rung()
    {
        var state = Outside([(1, DifficultyTier.HEROIC)], legendLevel: MythicLegendLevel - 1);
        var runsStartedBefore = state.Player.RunsStarted;

        var result = Start(state, 1, DifficultyTier.MYTHIC);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.LEGEND_LEVEL_TOO_LOW,
            $"the clear this player needs is in hand, so the only unmet requirement is Legend Level " +
            $"{MythicLegendLevel}. Answering PREREQUISITE_NOT_CLEARED here would tell the player to " +
            "go and clear something they already cleared.");
        result.NewState.Player.RunsStarted.ShouldBe(runsStartedBefore);
    }

    /// <summary>
    /// 🔒 The double block: neither requirement met, and the handler answers with the clear.
    /// </summary>
    /// <remarks>
    /// A rejection carries one enum value and no detail payload, so exactly one of the two
    /// requirements can be named. The clear is checked first and therefore wins — deliberately, and
    /// pinned here so a later reordering of the two guards is a failing test rather than a silent
    /// change to what every doubly-blocked player is told. The screen remains the surface that lists
    /// both.
    /// </remarks>
    [Fact]
    public void A_Mythic_request_blocked_by_both_requirements_is_answered_with_the_clear()
    {
        var state = Outside([], legendLevel: MythicLegendLevel - 1);

        var result = Start(state, 1, DifficultyTier.MYTHIC);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.PREREQUISITE_NOT_CLEARED,
            "this player has neither cleared chapter 1 Heroic nor reached the Mythic rung's Legend " +
            "Level, and the handler answers with the CLEAR. The order of the two guards is the whole " +
            "of this decision: HandlerResult.Reject carries one value, so whichever check is written " +
            "first is what every doubly-blocked player in the game is told.");
    }

    // ---------------------------------------------------- three refusals, three distinct wire values

    /// <summary>
    /// 🔒 The three ways START_RUN says no are three different values on the wire, and none of them is
    /// a reused one.
    /// </summary>
    /// <remarks>
    /// M7-04 shipped <c>NOT_ENTITLED</c> doing double duty for a Plus paywall and a malformed slot
    /// index, and the client could not tell the two apart. A locked chapter is not an illegal state,
    /// an under-levelled account is not a locked chapter, and a Plus paywall is none of the three —
    /// so this case asserts the values against each other rather than only against their expected
    /// selves.
    /// </remarks>
    [Fact]
    public void The_three_refusals_of_START_RUN_are_three_distinct_wire_values()
    {
        var locked = Start(Fresh(), 2, DifficultyTier.NORMAL).Rejection;
        var underLevelled = Start(
            Outside([(1, DifficultyTier.HEROIC)], legendLevel: MythicLegendLevel - 1),
            1,
            DifficultyTier.MYTHIC).Rejection;
        var alreadyRunning = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context).Rejection;

        locked.ShouldBe(RejectionReason.PREREQUISITE_NOT_CLEARED);
        locked.ShouldNotBe(
            RejectionReason.ILLEGAL_STATE,
            "a chapter the player has not opened is a legal request against a legal state — the " +
            "answer is simply no. Folding it into ILLEGAL_STATE would tell the client to resync over " +
            "a decision the server made on purpose.");
        locked.ShouldNotBe(
            RejectionReason.NOT_ENTITLED,
            "NOT_ENTITLED is the Plus paywall. A client that saw it for a locked chapter would offer " +
            "a purchase to a player whose only problem is that they have not cleared chapter 1.");

        underLevelled.ShouldBe(RejectionReason.LEGEND_LEVEL_TOO_LOW);
        underLevelled.ShouldNotBe(
            locked,
            "the two unmet requirements of the Mythic rung are answered by different actions — play " +
            "the tier below, or level up — so they cannot share one value.");

        alreadyRunning.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the pre-existing guard keeps its own value: a second START_RUN while a run is open IS a " +
            "state-machine violation, and appending the ladder's two values must not renumber or " +
            "relabel it.");
        alreadyRunning.ShouldNotBe(locked);
        alreadyRunning.ShouldNotBe(underLevelled);
    }

    /// <summary>
    /// The shape checks stay ahead of the ladder: an undefined tier is still <c>ILLEGAL_STATE</c>.
    /// </summary>
    /// <remarks>
    /// Load-bearing ordering, not a nicety — the gate looks the tier up as a rung key, and a tier the
    /// enum does not declare has no rung to look up. Refusing it as a shape violation before the
    /// ladder is consulted is what keeps the ladder's own "every declared tier has a rung" floor from
    /// being asked an impossible question.
    /// </remarks>
    [Fact]
    public void An_undefined_tier_is_still_refused_ahead_of_the_ladder()
    {
        var result = Start(Fresh(), 1, (DifficultyTier)0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "default(DifficultyTier) is not a tier the ladder authors a rung for. If the gate ran " +
            "first it would either throw out of Apply or refuse with a ladder reason for a request " +
            "that is malformed rather than locked.");
    }
}
