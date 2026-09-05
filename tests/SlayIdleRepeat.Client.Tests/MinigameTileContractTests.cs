using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The contract between "every arm this client can draw" and "the rules layer will resolve it and
/// clear the tile".
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A minigame the screen offers and the rules layer refuses is a tile a run cannot leave.</b>
/// Nothing else clears a Minigame tile — the board's own Continue is the refusal arm once the tile
/// draws on its own screen — so an arm whose submission comes back <c>ILLEGAL_STATE</c> leaves the
/// board redrawing the identical state after every press, with abandoning the run as the only
/// remaining control.
/// </para>
/// <para>
/// 🔒 <b>Core's half is ASKED, not transcribed.</b> A run is stood on a Minigame tile and
/// <c>MINIGAME_SUBMIT</c> is sent through <c>GameRules.Apply</c>, the one public mutation, and
/// whether the command was accepted IS the answer. Accepted is not enough either: the tile has to
/// CLEAR, because an accepted command that leaves it pending is the same dead end by another route.
/// </para>
/// <para>
/// 🔒 <b>And the tier boundary, which the timing bar's own game is scored against.</b> The hit count
/// is the tier and the game gives three strikes, so 0 through 3 are the reachable claims and 4 is the
/// one a game that kept counting past its last strike would send. Both sides are checked, because
/// "0..3 accepted" is satisfied by a handler that accepts everything.
/// </para>
/// <para>
/// ⚠️ It reads the checkout's own content set, because <c>Apply</c> does: a command runs the profile
/// catch-up before dispatch. That is <see cref="BootContent.Shipped"/>, so the answers measured are
/// the shipped game's.
/// </para>
/// </remarks>
public sealed class MinigameTileContractTests
{
    /// <summary>
    /// How many arms this client draws, so a sweep of none cannot pass.
    /// </summary>
    /// <remarks>
    /// 🔴 A loop over a list is the shape that silently measures nothing: an empty arm list would
    /// satisfy every assertion below and report a clean contract. Three is the floor because three is
    /// what ships — a fourth arm is exactly the event this case exists to be read on.
    /// </remarks>
    private const int BuiltArmCount = 3;

    /// <summary>The tier a timing bar scores by hitting every one of its three strikes.</summary>
    private const int PerfectTimingBarTier = 3;

    /// <summary>One past the last row of the timing bar's four-row table.</summary>
    private const int TierPastTheLastRow = 4;

    /// <summary>The value a run with no pending tile reports.</summary>
    private const int NoPendingTile = -1;

    /// <summary>The node the fixture run stands on.</summary>
    private const int Position = 3;

    private static readonly PlayerId Player = new("PLAYER_minigamecontract_5c04");
    private static readonly RunId Run = new("RUN_minigamecontract_a17f");

    /// <summary>Later than the fixture row's own instant, so the catch-up before dispatch is legal.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A seed for the one command this case sends, if the dispatch table asks for one.</summary>
    private const ulong CommandSeed = 0xB17Eu;

    /// <summary>
    /// 🔒 Every arm this client can open is accepted, and every one of them clears the tile.
    /// </summary>
    [Fact]
    public void Every_built_arm_is_resolved_by_the_rules_layer_and_clears_the_tile()
    {
        MinigameArms.Built.Count.ShouldBe(
            BuiltArmCount,
            "the built-arm list changed size. Every arm a tile can open has to be one the rules " +
            "layer resolves and clears the tile for, because nothing else clears a Minigame tile — " +
            "decide that for the new arm, and this is where the two are compared.");

        var refused = new List<string>();

        foreach (var arm in MinigameArms.Built)
        {
            var outcome = Submit(arm, tier: 0);

            if (!outcome.Accepted)
            {
                refused.Add(arm + " (" + outcome.Rejection + ")");

                continue;
            }

            outcome.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(
                NoPendingTile,
                arm + " was ACCEPTED and left the tile pending. An accepted command that clears " +
                "nothing is the same dead end as a refused one: the board redraws the identical " +
                "state and the player's only remaining control is the one that throws the run away.");
        }

        refused.ShouldBeEmpty(
            "🔴 the rules layer refused an arm this client offers, so a run landing on a tile that " +
            "opens it cannot leave: the board's own press is the refusal arm once the tile draws on " +
            "its own screen, and nothing else clears a Minigame tile. Either the arm is not one the " +
            $"rules layer knows, or its lowest tier is not a legal claim: [{string.Join(", ", refused)}]");
    }

    /// <summary>
    /// 🔒 <b>The timing bar's four reachable tiers are all accepted.</b>
    /// </summary>
    /// <remarks>
    /// The hit count IS the tier, so a game of three strikes can end on any of these four — and one
    /// the handler refused would be a play the player made and the game would not pay.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(PerfectTimingBarTier)]
    public void Every_tier_a_three_strike_game_can_reach_is_accepted(int tier)
    {
        var outcome = Submit(MinigameContent.TimingBar, tier);

        outcome.Accepted.ShouldBeTrue(
            "a timing bar played to " + tier + " hits submits tier " + tier + " and the rules layer " +
            "refused it as " + outcome.Rejection + ". That is a play the player made and the game " +
            "will not pay — and the refusal reaches them as a wire value that names nothing.");
        outcome.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(
            NoPendingTile, "and an accepted submission clears the tile as its last step.");
    }

    /// <summary>
    /// 🔒 …and the tier one past the last row is refused, so the sweep above is a measurement.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control. If <c>Apply</c> accepted every tier it was handed, the theory above
    /// would report a clean contract whatever the table says — and tier 4 is precisely what a game
    /// that kept counting past its last strike would claim, which is the defect
    /// <c>TimingBarGameTests</c> pins on the other side of the boundary.
    /// </remarks>
    [Fact]
    public void The_tier_one_past_the_timing_bars_last_row_is_refused()
    {
        var outcome = Submit(MinigameContent.TimingBar, TierPastTheLastRow);

        outcome.Accepted.ShouldBeFalse(
            "tier " + TierPastTheLastRow + " is one past the timing bar's last authored row and was " +
            "accepted, so this dispatch is not reaching the legality check at all — and every " +
            "'accepted' above means something other than what it says.");
        outcome.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "a tier the table has no row for is an illegal state. Any other reason means the " +
            "arrangement was turned away before the tier was ever read.");
        outcome.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(
            MinigamePresenter.MinigameTileKind,
            "a refused submission changes nothing, so the tile it could not resolve is still " +
            "pending. A refusal that had cleared it anyway would leave the run past a tile it never " +
            "played and paid nothing for.");
    }

    /// <summary>Submits one arm at one tier, over a run standing on a Minigame tile.</summary>
    private static CommandResult Submit(string minigameId, int tier)
    {
        var command = new MinigameSubmitCommand(minigameId, tier);

        var context = new GameContext(
            Noon,
            GameRules.RequiresCommandSeed(command) ? CommandSeed : null,
            BootContent.Shipped,
            new Entitlements(hasPlus: false, expiresAtUtc: null),
            new FeatureFlags(
                pvpEnabled: true,
                plusOfferEnabled: true,
                mailEnabled: true,
                disabledAdPlacements: [],
                disabledChapters: []));

        return GameRules.Apply(OnAMinigameTile(), command, context);
    }

    /// <summary>A run standing on an unresolved Minigame tile, with a profile that rehydrates.</summary>
    private static WorldSlice OnAMinigameTile() =>
        PlayerState.SliceWith(
            PlayerState.EmptySlice(Player),
            PlayerState.Run(
                Run,
                Player,
                RunPhase.InProgress,
                position: Position,
                pendingTileKind: MinigamePresenter.MinigameTileKind,
                pendingTileLinearIndex: Position,
                pendingTileStage: 1));
}
