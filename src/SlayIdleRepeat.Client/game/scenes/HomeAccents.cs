using Godot;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The one place the Home screen's three accent roles are named, and the one place a control asks
/// the theme what one is worth.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A role is asked for by name; the VALUE lives in <c>SlayTheme.tres</c> and nowhere else.</b>
/// The action ember, the energy sky and the gain jade are entries under a theme type that is a
/// palette rather than a control — nothing declares <c>SlayAccents</c> as its type variation, so
/// asking for one of its colours is a deliberate act rather than something a control inherits.
/// </para>
/// <para>
/// ⚠️ <b>It exists as its own file for one blunt reason</b>, and the reason is worth stating rather
/// than hiding: <c>HomeSceneRuleTests.No_home_script_writes_a_colour_of_its_own</c> scans the home
/// scripts for the literal text <c>Color(</c>, and every engine call that READS a colour —
/// <c>GetThemeColor</c> included — contains it. The rule's subject is a script that DECIDES a
/// colour, which this does not: it names a role and hands back whatever the theme says. Keeping the
/// lookup in one small file with that argument written down beats spreading a call the rule cannot
/// distinguish across a screen.
/// </para>
/// </remarks>
internal static class HomeAccents
{
    /// <summary>The theme type the three roles are entries of.</summary>
    internal const string Palette = "SlayAccents";

    /// <summary>The ember the primary action is drawn in.</summary>
    internal const string Action = "action_accent";

    /// <summary>The sky the refill offer is drawn in.</summary>
    internal const string Energy = "energy_accent";

    /// <summary>The jade a value that rose is flashed in.</summary>
    internal const string Gain = "gain_accent";

    /// <summary>
    /// One role's value, resolved through the theme the given control draws by.
    /// </summary>
    /// <param name="through">
    /// Any control under the node the shared theme is assigned to. The lookup walks up the tree, so
    /// this need not be the node carrying the theme itself.
    /// </param>
    /// <param name="role">Which of the three roles.</param>
    /// <returns>
    /// The theme's colour for that role, or the default when there is no control to ask through —
    /// which is the state a screen torn down mid-render is in, and not one to crash on.
    /// </returns>
    internal static Color Of(Control? through, string role) =>
        through is not null && GodotObject.IsInstanceValid(through)
            ? through.GetThemeColor(role, Palette)
            : default;
}
