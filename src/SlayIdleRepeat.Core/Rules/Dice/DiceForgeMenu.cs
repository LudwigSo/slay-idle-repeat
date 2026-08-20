using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// What a Dice Forge tile actually offers today: the subset of
/// <see cref="DiceForgeUpgradeTable.Options"/> the rest of the game can resolve.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The two it withholds are withheld to stop the run wedging, not for balance.</b> The table
/// is a pure statement of what a forge COULD install; this is where "could" meets what a roll can
/// answer, and the difference is not cosmetic:
/// </para>
/// <list type="bullet">
/// <item>
/// <b><see cref="DieFaceKind.Star"/></b> — <c>ROLL_DICE</c> carries no payload a player's chosen 1-6
/// could arrive on, so a Star face makes every later roll answer <c>ILLEGAL_STATE</c>: a run that
/// can never move again, bought with the player's own tap.
/// </item>
/// <item>
/// <b><see cref="DieFaceKind.Chain"/></b> — a Chain hop is specified to resolve its landing tile and
/// then roll again (`03` §1.1), and while the run persists the link count, nothing fires the
/// follow-up roll. The player would be handed a face worth two nodes where the die's average is 3.5:
/// strictly worse, sold as an upgrade.
/// </item>
/// </list>
/// <para>
/// Built by FILTERING rather than by re-listing, so the table stays the one statement of the
/// vocabulary and a target added there arrives here automatically unless <see cref="IsResolvable"/>
/// refuses it — the direction that fails safe.
/// </para>
/// <para>
/// Public because the forge SCREEN draws this menu and the handler validates against it, and a
/// screen with a list of its own would offer an option the rules layer refuses or miss one it
/// allows. Either way the player finds out by pressing.
/// </para>
/// </remarks>
public static class DiceForgeMenu
{
    /// <summary>The options a Dice Forge offers today, in menu order.</summary>
    public static IReadOnlyList<DiceForgeUpgradeOption> Offered { get; } = Array.AsReadOnly(
        DiceForgeUpgradeTable.Options.Where(IsResolvable).ToArray());

    /// <summary>Whether the rest of the game can resolve a die carrying this option's result.</summary>
    /// <remarks>See this type's remarks for what each refusal would cost a player handed it anyway.</remarks>
    public static bool IsResolvable(DiceForgeUpgradeOption option) =>
        option.IsHigherPip || option.Kind is not (DieFaceKind.Star or DieFaceKind.Chain);
}

/// <summary>
/// The six faces of the die a run currently rolls, projected read-only so the forge screen can show
/// them before <c>DICE_FORGE_CHOOSE</c> replaces one.
/// </summary>
/// <remarks>
/// 🔒 <b>This must agree with the roll, and shares its composition rather than restating it.</b> The
/// faces come off <see cref="RunDie.Of"/>, which is what <c>ROLL_DICE</c> draws against — a screen
/// showing the starting die while the run rolled an upgraded one would make the forge look broken
/// on the one screen that exists to show it working.
/// </remarks>
public sealed class RunDieView
{
    private RunDieView(IReadOnlyList<RunDieFaceRow> faces) => Faces = faces;

    /// <summary>The six faces, in face order.</summary>
    public IReadOnlyList<RunDieFaceRow> Faces { get; }

    /// <summary>Projects the die <paramref name="run"/> currently rolls.</summary>
    /// <param name="run">The run's persisted row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    public static RunDieView Project(RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var composed = RunDie.Of(run);
        var faces = new RunDieFaceRow[composed.Count];

        for (var index = 0; index < faces.Length; index++)
        {
            var face = composed[index];

            faces[index] = new RunDieFaceRow(
                index + 1,
                face.Kind == DieFaceKind.Pip
                    ? face.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : face.Kind.ToString(),
                DiceForgeUpgradeTable.IsLegalSource(face));
        }

        return new RunDieView(Array.AsReadOnly(faces));
    }
}

/// <summary>One face of the run's die, as a screen draws it.</summary>
/// <param name="FaceIndex">1-based, 1..6 — the index <c>DICE_FORGE_CHOOSE</c> names.</param>
/// <param name="Rendered">
/// What the face shows: its pip count for a Pip face, its kind's own name otherwise. A string
/// because a screen draws it and a number would have to mean two different things.
/// </param>
/// <param name="Upgradeable">
/// Whether the forge can act on it — `04` §1 makes only a Pip face a legal source. A face an earlier
/// forge already turned into a Surge is reported as unpressable rather than omitted, because a die
/// drawn with four faces would be a worse lie than one with a disabled row.
/// </param>
public readonly record struct RunDieFaceRow(int FaceIndex, string Rendered, bool Upgradeable);
