using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// Which minigames this client has a screen for — the three built arms, and the one that is only
/// missing.
/// </summary>
/// <remarks>
/// 🔴 <b>The count is stated as the catalogue's minus one, so it fails the day Rune Recall lands.</b>
/// Three literals here would leave the day a fourth screen is built looking exactly like the day
/// before it — the rules layer already accepts a submission for the unbuilt arm, so the only thing
/// keeping a tile from opening on a game nobody drew is this list.
/// </remarks>
public sealed class MinigameArmsTests
{
    /// <summary>
    /// 🔒 Exactly one of the rules layer's minigames has no screen.
    /// </summary>
    [Fact]
    public void Every_minigame_but_one_has_a_screen()
    {
        MinigameView.Ids.Count.ShouldBeGreaterThan(
            1,
            "with one known minigame or none, 'all but one' is an empty list and every sweep over " +
            "the built arms below stops measuring anything.");

        MinigameArms.Built.Count.ShouldBe(
            MinigameView.Ids.Count - 1,
            "the rules layer knows " + MinigameView.Ids.Count + " minigames and this client draws " +
            MinigameArms.Built.Count + ". Stated as the catalogue's count minus the one unbuilt arm " +
            "rather than as a number, so the day Rune Recall gets a screen — or the day a fifth " +
            "minigame is authored — this is red and says which list moved.");
    }

    /// <summary>
    /// 🔒 Every built arm is one the rules layer actually knows, and the unbuilt one is left out.
    /// </summary>
    /// <remarks>
    /// 🔴 Both halves. A list containing an id the catalogue does not know would open a screen on a
    /// game <c>MINIGAME_SUBMIT</c> refuses outright; a list containing the unbuilt arm would open a
    /// screen with nothing drawn on it.
    /// </remarks>
    [Fact]
    public void The_built_arms_are_catalogue_ids_and_the_unbuilt_one_is_not_among_them()
    {
        foreach (var arm in MinigameArms.Built)
        {
            MinigameView.Ids.ShouldContain(
                arm,
                "'" + arm + "' is offered as a built arm and the rules layer does not know it, so " +
                "a tile opening on it submits a command that is refused as an illegal state.");
        }

        MinigameArms.Built.ShouldNotContain(
            MinigameArms.Unbuilt,
            "the arm with no screen is in the list a tile picks from, so a tile can open on a game " +
            "this client cannot draw.");
        MinigameView.Ids.ShouldContain(
            MinigameArms.Unbuilt,
            "the id named as unbuilt is not one the rules layer knows at all, so the subtraction " +
            "above removes nothing and the count agrees by coincidence.");
    }

    /// <summary>No arm is listed twice, which a count alone cannot see.</summary>
    [Fact]
    public void No_arm_is_listed_twice() =>
        MinigameArms.Built.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            MinigameArms.Built.Count,
            "a duplicated arm makes the count right and the list wrong — one game would be offered " +
            "twice as often as the others by a tile picking uniformly over it.");
}
