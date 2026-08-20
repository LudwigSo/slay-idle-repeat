using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

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
/// 🔒 <b>The choice IS the player's now.</b> <c>SHRINE_CHOOSE</c> names the slot, and both halves of
/// the chosen row are applied — the immediate heal and the permanent stat move. There is no longer a
/// row this view can call "the taken one" before the player has spoken, which is why the property
/// that used to say so is gone.
/// </para>
/// <para>
/// <b>The cleanse arm fires off the run's own curse list.</b> With at least one curse carried,
/// <see cref="IsCleanse"/> is true, slot 2 is the Cleanse and no second buff is drawn — which is
/// also one fewer draw off the shrine stream, so a view that guessed this wrong would show a
/// different pair than the command applies.
/// </para>
/// <para>🔒 Read-only: projecting mutates no <c>Run</c> and moves no stream position.</para>
/// </remarks>
public sealed class ShrineView
{
    private ShrineView(IReadOnlyList<ShrineBuffRow> rows, bool isCleanse, string? cleansableCurseId)
    {
        Rows = rows;
        IsCleanse = isCleanse;
        CleansableCurseId = cleansableCurseId;
    }

    /// <summary>The rows the shrine offers, in slot order.</summary>
    /// <remarks>
    /// One row when <see cref="IsCleanse"/> is true — slot 2 is the Cleanse, which is not a pool row
    /// and has nothing to name here — and two otherwise.
    /// </remarks>
    public IReadOnlyList<ShrineBuffRow> Rows { get; }

    /// <summary>Whether the second slot is a Cleanse rather than a drawn buff.</summary>
    public bool IsCleanse { get; }

    /// <summary>
    /// The curse a Cleanse would remove, or <c>null</c> when the shrine offers none.
    /// </summary>
    /// <remarks>
    /// Named so the screen can say WHICH curse the option lifts. It is the run's first-applied
    /// curse rather than the player's pick, for the reason <c>Handlers.ShrineChoose</c> gives — and
    /// a screen that could not name it would have to describe the option as "remove a curse", which
    /// is a worse offer than the one the player is actually being made.
    /// </remarks>
    public string? CleansableCurseId { get; }

    /// <summary>Projects the shrine <paramref name="run"/> is standing on, or <c>null</c> when its pending tile is not one.</summary>
    /// <param name="run">The run whose shrine is being drawn.</param>
    /// <param name="content">The loaded content set, read for the shrine buff pool.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException"><paramref name="content"/> authors no shrine buff pool.</exception>
    public static ShrineView? Project(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        if (run.PendingTileKind != (int)TileKind.Shrine)
        {
            return null;
        }

        var tuning = ShrineTuning.Read(content);

        // Off the run's own curse list, exactly as SHRINE_CHOOSE reads it: the branch decides how
        // many draws the shrine spends, so a view that guessed it would show a different pair than
        // the command applies.
        var cleansable = run.Curses is { Count: > 0 } curses ? curses[0] : null;

        // Reopened at the position the run COMMITTED the stream at, and never folded back: these are
        // the draws SHRINE_CHOOSE will spend, re-derived rather than consumed.
        var drawn = ShrineResolver.Draw(
            tuning,
            DeterministicRng.OpenAt(run.RunSeed, RngStreams.Shrine, CommittedShrinePosition(run)),
            cleansable is not null);

        var rows = new List<ShrineBuffRow>(2) { RowOf(tuning, drawn.FirstIndex) };

        if (drawn.SecondIndex is { } second)
        {
            rows.Add(RowOf(tuning, second));
        }

        return new ShrineView(Array.AsReadOnly(rows.ToArray()), cleansable is not null, cleansable);
    }

    /// <summary>The <c>shrine</c> stream index the run stands at. An unrecorded stream stands at zero.</summary>
    private static ulong CommittedShrinePosition(RunSnapshot run) =>
        run.RngStreamPositions is { } committed &&
        committed.TryGetValue(RngStreams.Shrine, out var position)
            ? position
            : 0UL;

    /// <summary>One drawn pool index, as the row a screen draws.</summary>
    private static ShrineBuffRow RowOf(ShrineTuning tuning, int index)
    {
        var buff = tuning.Buffs[index];

        return new ShrineBuffRow(buff.Id, buff.DisplayName, buff.ImmediateHealPctMaxHp);
    }
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
