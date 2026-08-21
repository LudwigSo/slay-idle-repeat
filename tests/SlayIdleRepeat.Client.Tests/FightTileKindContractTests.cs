using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The one contract between "which tiles the rules layer will open a fight on" and "which tiles this
/// client sends <c>START_BATTLE</c> for".
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The guard for the worst defect this screen has had, twice.</b> A tile kind the rules layer
/// accepts <c>START_BATTLE</c> for, and that the client's own list does not name, is a node the run
/// can never leave: the board falls back to <c>RESOLVE_TILE</c>, the rules layer ACCEPTS it for a
/// fight tile and clears nothing, the board redraws the identical state, and the player's only
/// remaining control is the one that throws the run away. It happened first for Enemy, Elite and Boss
/// — every fight in the game — and again for the mini-boss, on a node no run can walk around, roll
/// past or skip. Both times the whole suite stayed green, because nothing compared the two lists.
/// </para>
/// <para>
/// 🔒 <b>Core's half is ASKED, not transcribed, and that is the entire design of this case.</b> A
/// second copy of the fight kinds here would be a third list to keep true and would agree with
/// whatever it was copied from on the day it was copied — which is exactly how the client's list
/// drifted. So the answer is taken from the rules layer's own behaviour: a run is stood on each kind
/// in turn and <c>START_BATTLE</c> is submitted through <c>GameRules.Apply</c>, the one public
/// mutation, and whether the command was accepted IS the answer. Nothing here names a handler, and
/// the gate itself is internal to another assembly — this case never needs to see it.
/// </para>
/// <para>
/// 🔒 <b>Both directions, because they are two different defects.</b> A kind Core accepts and the
/// client does not send is the dead end above. A kind the client sends and Core refuses is its
/// mirror: the board would submit a command certain to be rejected and show the player the one
/// sentence that covers every refusal, on a tile that actually needed resolving.
/// </para>
/// <para>
/// ⚠️ It reads the checkout's own content set, because <c>Apply</c> does — a command runs the profile
/// catch-up before dispatch. That is <see cref="BootContent.Shipped"/>, which this suite already
/// loads; no fixture content set is invented for it, so the kinds measured are the kinds the shipped
/// game accepts.
/// </para>
/// </remarks>
public sealed class FightTileKindContractTests
{
    /// <summary>How many kinds the tile vocabulary holds, so a sweep of none cannot pass.</summary>
    /// <remarks>
    /// 🔴 A loop over an enum is the shape that silently measures nothing: a reflection call that
    /// answered with an empty set would satisfy every set comparison below and report a clean
    /// contract. The count is therefore pinned rather than merely non-zero — a sixteenth kind is
    /// exactly the event this case exists to be read on, so it must arrive as a failure here that
    /// says to check both lists, not as a sweep that quietly grew by one.
    /// </remarks>
    private const int TileKindCount = 15;

    private static readonly PlayerId Player = new("PLAYER_fightkind_5c11");
    private static readonly RunId Run = new("RUN_fightkind_9d42");

    /// <summary>Later than the fixture rows' own instant, so the catch-up before dispatch is legal.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A seed for the one command this case sends, if the dispatch table asks for one.</summary>
    /// <remarks>
    /// Asked of <c>GameRules.RequiresCommandSeed</c> rather than always supplied or always omitted:
    /// which commands need a seed is the dispatch table's answer, and a case that guessed would be
    /// refused for the wrong reason and report every kind as "not a fight".
    /// </remarks>
    private const ulong CommandSeed = 0x5EEDu;

    [Fact]
    public void The_client_sends_START_BATTLE_for_exactly_the_kinds_the_rules_layer_accepts_it_for()
    {
        var kinds = Enum.GetValues<TileKind>();

        kinds.ShouldNotBeEmpty(
            "the tile vocabulary came back empty, so this case swept nothing and would have reported " +
            "a clean contract whatever either side actually says.");

        kinds.Length.ShouldBe(
            TileKindCount,
            "the tile vocabulary changed size. If a kind was ADDED, decide whether the rules layer " +
            "opens a fight on it and whether this client sends START_BATTLE for it — those two " +
            "answers have to agree, and this case is where they are compared.");

        var coreAccepts = kinds.Where(RulesLayerOpensAFightOn).ToArray();
        var clientSends = kinds.Where(kind => BattleReplayPresenter.OpensAFight((int)kind)).ToArray();

        coreAccepts.ShouldNotBeEmpty(
            "the rules layer refused START_BATTLE on every kind in the game, so this case is not " +
            "measuring the fight gate at all — the run row it stands on is being refused for some " +
            "other reason, and every comparison below would be made against an empty set.");

        coreAccepts.Length.ShouldBeLessThan(
            kinds.Length,
            "the rules layer accepted START_BATTLE on EVERY kind, which no gate does — so the " +
            "command is being accepted before the gate is reached and this case cannot tell a fight " +
            "tile from a shop.");

        clientSends.ShouldNotBeEmpty(
            "this client sends START_BATTLE for no tile at all, so no run can ever fight anything.");

        coreAccepts.Except(clientSends).ShouldBeEmpty(
            "🔴 the rules layer opens a fight on a kind this client does not send START_BATTLE for, " +
            "so a run landing on one is sent RESOLVE_TILE, which the rules ACCEPT for a fight tile " +
            "and which clears nothing: the board redraws the identical state after every press and " +
            "abandoning the run is the player's only way off that node. Add the kind to the client's " +
            $"fight list. Kinds: [{string.Join(", ", coreAccepts.Except(clientSends))}]");

        clientSends.Except(coreAccepts).ShouldBeEmpty(
            "this client sends START_BATTLE for a kind the rules layer refuses, so pressing the " +
            "tile's control spends a round trip to be refused and the player is shown the sentence " +
            "that covers every refusal — on a tile that needed resolving instead. Remove the kind " +
            $"from the client's fight list. Kinds: [{string.Join(", ", clientSends.Except(coreAccepts))}]");
    }

    /// <summary>
    /// Whether the rules layer accepts <c>START_BATTLE</c> on a run standing on this kind.
    /// </summary>
    /// <remarks>
    /// Through <c>GameRules.Apply</c>, which is the rules layer's only public mutation, over a run row
    /// rehydrated by the domain's own validated path — so a kind this returns true for is a kind the
    /// shipped game really does open a fight on, and not a kind some list here says it does.
    /// </remarks>
    private static bool RulesLayerOpensAFightOn(TileKind kind)
    {
        var slice = PlayerState.SliceWith(
            PlayerState.EmptySlice(Player),
            PlayerState.Run(
                Run,
                Player,
                RunPhase.InProgress,
                position: 1,
                pendingTileKind: (int)kind,
                pendingTileLinearIndex: 1,
                pendingTileStage: 1));

        var command = new StartBattleCommand();

        var context = new GameContext(
            Noon,
            GameRules.RequiresCommandSeed(command) ? CommandSeed : null,
            BootContent.Shipped,
            new Entitlements(hasPlus: false, expiresAtUtc: null),
            new FeatureFlags(
                pvpEnabled: true,
                plusOfferEnabled: true,
                disabledAdPlacements: [],
                disabledChapters: []));

        return GameRules.Apply(slice, command, context).Accepted;
    }
}
