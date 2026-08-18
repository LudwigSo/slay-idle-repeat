using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The two buff rows a pending shrine will draw, projected read-only so the Shrine screen can show
/// them before <c>RESOLVE_TILE</c> applies one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This must agree with the resolver, and shares its draw rather than restating it.</b> The
/// rows are drawn off the run's committed <c>shrine</c> position, so a screen showing one pair while
/// the command applied another would be the shrine lying about what it gave.
/// </para>
/// <para>
/// <b>The choice is not the player's.</b> No choose command exists, so the resolver settles it:
/// <see cref="TakenRowIndex"/> names the row that is actually applied, and only its immediate-heal
/// half is applied at all — nothing consumes the stat half of a buff yet.
/// </para>
/// <para>
/// <b>The cleanse arm cannot fire in this build.</b> A run holds no curse list, so
/// <see cref="IsCleanse"/> is false and both rows are drawn buffs. The property is here because the
/// arm is real in the resolver and a screen that could not express it would have to be rewritten
/// rather than extended.
/// </para>
/// <para>🔒 Read-only: projecting mutates no <c>Run</c> and moves no stream position.</para>
/// </remarks>
public sealed class ShrineView
{
    private ShrineView(IReadOnlyList<ShrineBuffRow> rows, int takenRowIndex, bool isCleanse)
    {
        Rows = rows;
        TakenRowIndex = takenRowIndex;
        IsCleanse = isCleanse;
    }

    /// <summary>The rows the shrine offers, in slot order.</summary>
    public IReadOnlyList<ShrineBuffRow> Rows { get; }

    /// <summary>The index into <see cref="Rows"/> of the row the resolver actually applies.</summary>
    public int TakenRowIndex { get; }

    /// <summary>Whether the second slot is a Cleanse rather than a drawn buff.</summary>
    public bool IsCleanse { get; }

    /// <summary>Projects the shrine <paramref name="run"/> is standing on, or <c>null</c> when its pending tile is not one.</summary>
    /// <param name="run">The run whose shrine is being drawn.</param>
    /// <param name="content">The loaded content set, read for the shrine buff pool.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException"><paramref name="content"/> authors no shrine buff pool.</exception>
    public static ShrineView? Project(RunSnapshot run, ContentSnapshot content) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: the projection is written against the failing cases in " +
            "ShrineViewTests and filled in by the implementation phase.");
}

/// <summary>One row of a shrine's offer.</summary>
/// <param name="BuffId">The buff id, e.g. <c>SHR_ATK</c>.</param>
/// <param name="DisplayNameKey">The localisation key the row's name is drawn from.</param>
/// <param name="ImmediateHealPctMaxHp">
/// The share of Max HP this row heals the moment it is taken, or <c>null</c> where it heals nothing.
/// The one half of a buff that is mechanically real today.
/// </param>
public readonly record struct ShrineBuffRow(
    string BuffId, string DisplayNameKey, double? ImmediateHealPctMaxHp);
