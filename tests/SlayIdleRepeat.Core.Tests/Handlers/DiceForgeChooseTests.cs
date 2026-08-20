using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Dice;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// DICE_FORGE_CHOOSE: the run-scoped die a Dice Forge tile builds, and the menu that is deliberately
/// narrower than the upgrade table.
/// </summary>
public sealed class DiceForgeChooseTests
{
    private static CommandResult Forge(
        WorldSlice state, int faceIndex, int optionIndex, int? higherPip = null) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state,
            new DiceForgeChooseCommand(faceIndex, optionIndex, higherPip),
            TileWorlds.Context);

    private static WorldSlice AtAForge(IReadOnlyDictionary<int, int>? upgrades = null) =>
        TileWorlds.OnTile(TileKind.DiceForge, dieFaceUpgrades: upgrades);

    /// <summary>The index of the "raise to a higher Pip value" option in the offered menu.</summary>
    private const int HigherPipOption = 0;

    // ------------------------------------------------------------------------------ the upgrade

    [Fact]
    public void Raising_a_pip_installs_the_new_face_and_clears_the_tile()
    {
        var result = Forge(AtAForge(), faceIndex: 1, HigherPipOption, higherPip: 6);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.HasPendingTile.ShouldBeFalse();
        RunDie.Of(result.NewState.Run!)[0].ShouldBe(DieFace.Pip(6));
    }

    /// <summary>…and the other five faces are untouched, so an upgrade replaces one face rather than the die.</summary>
    [Fact]
    public void Raising_one_face_leaves_the_rest_of_the_die_alone()
    {
        var result = Forge(AtAForge(), faceIndex: 1, HigherPipOption, higherPip: 6);
        var die = RunDie.Of(result.NewState.Run!);

        for (var face = 2; face <= 6; face++)
        {
            die[face - 1].ShouldBe(DieFace.Pip(face));
        }
    }

    /// <summary>A special-kind option installs that kind.</summary>
    [Fact]
    public void A_special_option_installs_its_kind()
    {
        var surge = SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu
            .Select((option, index) => (option, index))
            .First(row => row.option.Kind == DieFaceKind.Surge)
            .index;

        var result = Forge(AtAForge(), faceIndex: 2, surge);

        result.Accepted.ShouldBeTrue();
        RunDie.Of(result.NewState.Run!)[1].Kind.ShouldBe(DieFaceKind.Surge);
    }

    /// <summary>
    /// 🔒 A second forge upgrades what the FIRST one left, so "raise to a higher pip" is measured
    /// against the run's current die rather than the starting one.
    /// </summary>
    /// <remarks>
    /// Against the starting die, raising face 1 to a 3 after it had already been raised to a 6 would
    /// be a legal "increase" that LOWERS the face — an upgrade tile that makes the die worse.
    /// </remarks>
    [Fact]
    public void A_second_forge_cannot_lower_a_face_it_already_raised()
    {
        var raised = Forge(AtAForge(), faceIndex: 1, HigherPipOption, higherPip: 6).NewState;

        raised.Run!.ArriveAtTile((int)TileKind.DiceForge, linearIndex: 12, stage: 2);

        var lowered = Forge(raised, faceIndex: 1, HigherPipOption, higherPip: 3);

        lowered.Accepted.ShouldBeFalse(
            "face 1 already shows 6, so 3 is not higher — measured against the starting die it " +
            "would have been.");
        lowered.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------------------- the menu

    /// <summary>
    /// 🔒 The menu withholds Star and Chain, and both are withheld to stop the run wedging — see
    /// <c>Handlers.DiceForgeChoose</c>'s remarks.
    /// </summary>
    [Fact]
    public void The_menu_withholds_the_two_faces_nothing_can_resolve()
    {
        SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu.ShouldNotContain(
            option => option.Kind == DieFaceKind.Star,
            "a Star face makes every later ROLL_DICE answer ILLEGAL_STATE — a run the player " +
            "cannot move again, bought with their own tap.");
        SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu.ShouldNotContain(
            option => option.Kind == DieFaceKind.Chain,
            "nothing fires a chain's follow-up roll, so a Chain face is a two-node move sold as an " +
            "upgrade.");
    }

    /// <summary>…and it still offers something, so the withholding above is not the whole table.</summary>
    [Fact]
    public void The_menu_still_offers_the_faces_that_do_work()
    {
        SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu.Count.ShouldBe(
            DiceForgeUpgradeTable.Options.Count - 2,
            "exactly two of the table's options are withheld; a menu that lost more than that is a " +
            "tile with nothing to sell.");
        SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu.ShouldContain(option => option.IsHigherPip);
    }

    // ------------------------------------------------------------------------------- refusals

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void A_face_index_outside_the_die_is_rejected(int faceIndex)
    {
        Forge(AtAForge(), faceIndex, HigherPipOption, higherPip: 6)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void An_option_index_outside_the_menu_is_rejected(int optionIndex)
    {
        Forge(AtAForge(), faceIndex: 1, optionIndex, higherPip: 6)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>"Raise to a higher pip" with no pip count, or one that is not higher, is refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(7)]
    public void An_illegal_pip_raise_is_rejected(int? higherPip)
    {
        Forge(AtAForge(), faceIndex: 1, HigherPipOption, higherPip)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// A face already upgraded to a special kind is not a legal SOURCE — only a Pip face is
    /// (`04` §1 rules Void out by name, and every other kind is already an upgrade's result).
    /// </summary>
    [Fact]
    public void A_face_already_upgraded_to_a_special_kind_cannot_be_upgraded_again()
    {
        var surge = SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu
            .Select((option, index) => (option, index))
            .First(row => row.option.Kind == DieFaceKind.Surge)
            .index;

        var forged = Forge(AtAForge(), faceIndex: 3, surge).NewState;

        forged.Run!.ArriveAtTile((int)TileKind.DiceForge, linearIndex: 12, stage: 2);

        Forge(forged, faceIndex: 3, HigherPipOption, higherPip: 6)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_choice_away_from_a_forge_is_rejected()
    {
        Forge(TileWorlds.OnTile(TileKind.Shrine), faceIndex: 1, HigherPipOption, higherPip: 6)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------------------ the codec

    /// <summary>
    /// Every face the menu can install round-trips through the code a <c>Run</c> persists.
    /// </summary>
    /// <remarks>
    /// The encoding is a WIRE value — a code written into a run's row must still decode to the same
    /// face after any later edit — so it is asserted over the whole reachable set rather than one
    /// sample.
    /// </remarks>
    [Fact]
    public void Every_installable_face_round_trips_through_its_code()
    {
        var faces = new List<DieFace>();

        for (var pips = 1; pips <= 6; pips++)
        {
            faces.Add(DieFace.Pip(pips));
        }

        foreach (var option in SlayIdleRepeat.Core.Handlers.DiceForgeChoose.Menu.Where(o => !o.IsHigherPip))
        {
            for (var tier = DieFace.MinTier; tier <= DieFace.MaxTier; tier++)
            {
                faces.Add(DieFace.Special(option.Kind!.Value, tier));
            }
        }

        foreach (var face in faces)
        {
            DieFaceCodec.TryDecode(DieFaceCodec.Encode(face)).ShouldBe(face);
        }
    }

    /// <summary>
    /// 🔒 A code this build cannot read leaves the STARTING face standing rather than throwing — the
    /// run keeps playing, the player loses the upgrade.
    /// </summary>
    /// <remarks>
    /// The negative control for the round trip above: without it, a codec that decoded everything to
    /// something would satisfy it. A run row written by a later build is the only way this arm is
    /// reached, and bricking such a run at its next ROLL_DICE is the wound the whole change is
    /// against.
    /// </remarks>
    [Fact]
    public void An_unreadable_face_code_falls_back_to_the_starting_die()
    {
        var alien = new Dictionary<int, int> { [1] = 999_999 };

        DieFaceCodec.TryDecode(999_999).ShouldBeNull();
        RunDie.Of(AtAForge(alien).Run!)[0].ShouldBe(DieFace.Pip(1));
    }

    /// <summary>A run-less slice still throws, the same guard every other run command hits.</summary>
    [Fact]
    public void A_run_less_slice_throws_rather_than_rejects()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(
                    Worlds.OutsideARun(),
                    new DiceForgeChooseCommand(1, HigherPipOption, 6),
                    Worlds.Context))
            .Message.ShouldContain("DICE_FORGE_CHOOSE", Case.Sensitive);
    }
}
