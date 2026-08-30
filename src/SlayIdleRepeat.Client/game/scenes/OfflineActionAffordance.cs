using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// Draws one server-backed control as unavailable while the connection cannot carry it, and answers
/// a tap on it with the inline toast instead of a command.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Both halves, in one place, because either alone is the bug.</b> A control drawn out of use
/// that still submits is a lie; a control that silently ignores a press is "silently broken", which
/// is the exact shape the offline split forbids. So a screen adopting this writes two lines — one in
/// its render and one in its press handler — rather than reinventing a dim, a mark and a routing rule
/// of its own.
/// </para>
/// <para>
/// 🔒 <b>The control is dimmed with <c>SelfModulate</c>, never <c>Modulate</c>.</b> Modulate
/// propagates to children, and the mark is a child — so the one thing on the button that exists for a
/// player who cannot separate a 40% control from a full one would itself be drawn at 40%.
/// <c>SelfModulate</c> takes the button's own face and text down and leaves the mark alone.
/// </para>
/// <para>
/// ⚠️ <b>The mark is built from primitives rather than set as a glyph.</b> This build ships no font
/// resource and no icon set: the engine's default face has no cloud codepoint, so a cloud-with-slash
/// written as text would render as a missing-glyph box on every device. Two nodes — a rounded puff
/// and a bar rotated across it — are the same silhouette with no font dependency, and they are added
/// and removed rather than authored, because the screens that adopt this build their controls in
/// three different ways.
/// </para>
/// <para>
/// 🔴 <b>Adopted by <c>Board</c> only.</b> It is written to be adopted in one line, and the rest of
/// the screens with server-backed controls have not adopted it yet — that is a gap named in this
/// task's report, not a claim this file makes.
/// </para>
/// </remarks>
internal static class OfflineActionAffordance
{
    /// <summary>What the mark is called under the control it marks, so it can be found and removed.</summary>
    private const string MarkNodeName = "OfflineMark";

    private const string PuffNodeName = "Puff";

    private const string PanelStyleOverride = "panel";

    /// <summary>The mark's own square, in canvas units — roughly 21 dp, and inset from the corner.</summary>
    /// <remarks>
    /// ⚠️ Chosen, not authored. The primary actions it sits on are 340 units tall, so a 64-unit mark
    /// inset 24 from the top-right corner clears the caption at every type size on those screens.
    /// </remarks>
    private const int MarkSize = 64;

    private const int MarkInset = 24;

    /// <summary>How thick the slash is, and how tall the puff under it.</summary>
    private const float SlashThickness = 8f;

    private const float PuffHeight = 34f;

    /// <summary>The corner radius that turns the puff into a cloud rather than a box.</summary>
    private const int PuffCornerRadius = 17;

    /// <summary>The angle the bar crosses the puff at, in radians — a quarter turn's half, negated.</summary>
    private const float SlashRotation = -0.785398f;

    /// <summary>
    /// The quiet grey every caption on these screens is drawn in.
    /// </summary>
    /// <remarks>
    /// Deliberately the same secondary as <c>ButtonTextColours</c>'s unusable colour, and deliberately
    /// NOT a warning colour: an action waiting for a connection has not been refused anything.
    /// </remarks>
    private static readonly Color MarkColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>Full colour, for a control the connection can carry again.</summary>
    private static readonly Color Unchanged = new(1f, 1f, 1f, 1f);

    /// <summary>Draws one server-backed control at the connection's answer for it.</summary>
    /// <param name="button">The control the server would have to answer.</param>
    /// <param name="connection">
    /// The one connection presenter, or <c>null</c> when the client was composed over no remote API
    /// at all — in which case nothing is drawn and the control is left exactly as the screen made it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="button"/> is null.</exception>
    internal static void ApplyTo(Button button, ConnectionPresenter? connection)
    {
        ArgumentNullException.ThrowIfNull(button);

        if (!GodotObject.IsInstanceValid(button))
        {
            return;
        }

        if (connection is null || connection.ServerActionsAvailable)
        {
            button.SelfModulate = Unchanged;

            RemoveMark(button);

            return;
        }

        button.SelfModulate = new Color(1f, 1f, 1f, ConnectionPresenter.UnavailableActionOpacity);

        if (connection.CloudSlashGlyphVisible)
        {
            EnsureMark(button);
        }
        else
        {
            RemoveMark(button);
        }
    }

    /// <summary>
    /// Answers a press on a server-backed control, and says whether the screen may still submit it.
    /// </summary>
    /// <remarks>
    /// 🔒 An inline toast and nothing else — the presenter owns the sentence and the decision; this
    /// only asks it. Returns true while the connection can carry the command, which is also the answer
    /// when no remote API is composed at all: an in-process host has no connection to lose, so every
    /// action stays available.
    /// </remarks>
    /// <param name="connection">The one connection presenter, or null when none is composed.</param>
    /// <returns>True when the press should go on to be submitted.</returns>
    internal static bool MayBeSubmitted(ConnectionPresenter? connection)
    {
        if (connection is null || connection.ServerActionsAvailable)
        {
            return true;
        }

        connection.ReportUnavailableActionTapped();

        return false;
    }

    private static void EnsureMark(Button button)
    {
        if (button.GetNodeOrNull<Control>(MarkNodeName) is not null)
        {
            return;
        }

        var mark = new Control
        {
            Name = MarkNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,

            // Anchored to the control's own top-right corner rather than positioned, so it stays
            // there whatever the caption does to the button's width.
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -(MarkSize + MarkInset),
            OffsetRight = -MarkInset,
            OffsetTop = MarkInset,
            OffsetBottom = MarkInset + MarkSize,
        };

        var puff = new Panel
        {
            Name = PuffNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            OffsetTop = (MarkSize - PuffHeight) / 2f,
            OffsetBottom = (MarkSize + PuffHeight) / 2f,
        };

        var face = new StyleBoxFlat
        {
            BgColor = MarkColour,
            CornerRadiusTopLeft = PuffCornerRadius,
            CornerRadiusTopRight = PuffCornerRadius,
            CornerRadiusBottomLeft = PuffCornerRadius,
            CornerRadiusBottomRight = PuffCornerRadius,
        };

        puff.AddThemeStyleboxOverride(PanelStyleOverride, face);

        var slash = new ColorRect
        {
            Color = MarkColour,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            PivotOffset = new Vector2(0f, SlashThickness / 2f),
            Position = new Vector2(0f, MarkSize - (SlashThickness / 2f)),
            Size = new Vector2(MarkSize * 1.42f, SlashThickness),
            Rotation = SlashRotation,
        };

        mark.AddChild(puff);
        mark.AddChild(slash);
        button.AddChild(mark);
    }

    private static void RemoveMark(Button button)
    {
        if (button.GetNodeOrNull<Control>(MarkNodeName) is not { } mark)
        {
            return;
        }

        // Detached before it is freed, for the reason every rebuilt list on these screens detaches:
        // QueueFree alone defers removal to the end of the frame, so a mark removed and a mark added
        // in the same frame would both be in the tree and both drawn.
        button.RemoveChild(mark);
        mark.QueueFree();
    }
}
