using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// <c>TILE_SHRINE</c>: two distinct options drawn with equal weight off the <c>shrine</c> stream,
/// or one option plus a Cleanse when a cleansable curse is active.
/// </summary>
/// <remarks>
/// <para>
/// Two options are offered; exactly one is applied, and the player picks which —
/// <c>Handlers.ShrineChoose</c> owns the choice and calls <see cref="ApplyBuff"/> with the row it
/// names. This type owns the DRAW, which is the half that must be identical for the screen showing
/// the offer and the command applying it.
/// </para>
/// <para>
/// 🔒 <b>Both halves of a drawn row are applied.</b> The immediate heal is written here; the
/// permanent stat move is recorded on the run and turned into a build effect every aggregation pass
/// by <c>Rules.Effects.ShrineBuffEffectSource</c>. Applying only the heal — which is what this
/// resolver did before that source existed — left eight of the ten pool rows indistinguishable from
/// taking nothing at all.
/// </para>
/// <para>
/// A shrine produces no domain events. A heal is a <c>Run</c> mutation through
/// <see cref="Run.SetHitPoints"/>, which returns <c>void</c> — there is no HP domain event in the
/// game — and a shrine moves no currency.
/// </para>
/// </remarks>
internal static class ShrineResolver
{
    /// <summary>
    /// Applies one drawn row in full: its immediate heal, if it carries one, and its permanent stat
    /// move, recorded on the run for the effect source to read.
    /// </summary>
    /// <param name="run">The run taking the buff.</param>
    /// <param name="row">The pool row the player chose.</param>
    /// <remarks>
    /// 🔒 <b>Every taken row joins the list, including <c>SHR_HEAL</c>, which has no stat.</b> The
    /// list is the record of what the shrine gave — a screen naming this run's buffs reads it, and a
    /// row omitted because it happened to have no stat would be a shrine visit that left no trace.
    /// The effect source skips a stat-less row on its own, so nothing is contributed twice.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    internal static void ApplyBuff(Run run, ShrineBuffPoolEntry row)
    {
        ArgumentNullException.ThrowIfNull(run);

        ApplyImmediateHeal(run, row);
        run.AddShrineBuff(row.Id);
    }

    /// <summary>
    /// The two pool indices a shrine draws off <paramref name="stream"/> at its current position —
    /// the whole of the shrine's randomness, in one place.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="ShrineView"/> rather than restated there. The offer is never persisted,
    /// so the screen showing it and the command applying it both re-derive it from the same committed
    /// position; a second copy of this draw would be a shrine naming two buffs and handing over a
    /// third. Everything that consumes a draw index lives here, so the two cannot fall out of step.
    /// </remarks>
    /// <param name="tuning">The authored pool, read for how many rows there are to draw from.</param>
    /// <param name="stream">The <c>shrine</c> stream. Advanced by one draw, or two.</param>
    /// <param name="hasCleansableCurse">Whether slot 2 is the Cleanse rather than a drawn row.</param>
    /// <exception cref="ArgumentNullException">Either reference argument is null.</exception>
    internal static ShrineDraw Draw(ShrineTuning tuning, DeterministicRng stream, bool hasCleansableCurse)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(stream);

        var count = tuning.Buffs.Count;
        var first = stream.Range(0, count);

        // The cleanse branch takes NO second draw, rather than drawing and discarding. A client
        // replaying this tile takes the same branch off the same state, so a discarded draw here
        // would leave the two streams one index apart for the rest of the run. Slot 2 is decided,
        // not drawn.
        return new ShrineDraw(
            first, hasCleansableCurse ? null : DistinctSecond(stream, first, count));
    }

    /// <summary>
    /// Sampling without replacement in exactly one draw: pick from the <c>count - 1</c> indices
    /// that are not <paramref name="first"/>, then map that back into the full range by stepping past
    /// <paramref name="first"/>.
    /// </summary>
    /// <remarks>
    /// The remap is <c>&gt;=</c> and not <c>&gt;</c>, and that is the whole correctness of it: the
    /// reduced draw's value <c>first</c> must become <c>first + 1</c>, because <c>first</c> itself is
    /// the index being excluded. A <c>&gt;</c> would return <paramref name="first"/> again and offer
    /// the same buff twice.
    /// <para>
    /// Reject-and-redraw would be the other way to do this and is rejected on purpose: it consumes a
    /// variable number of draw indices, which breaks the one-call-one-index property the stream's
    /// persistence model rests on.
    /// </para>
    /// </remarks>
    private static int DistinctSecond(DeterministicRng stream, int first, int count)
    {
        var reduced = stream.Range(0, count - 1);

        return reduced >= first ? reduced + 1 : reduced;
    }

    /// <summary>
    /// Applies a drawn row's <c>immediateHealPctMaxHp</c>, if it has one. Overheal is clamped here,
    /// by the rule that computes it — <see cref="Run.SetHitPoints"/> refuses a current above the
    /// maximum rather than trimming it silently.
    /// </summary>
    private static void ApplyImmediateHeal(Run run, ShrineBuffPoolEntry buff)
    {
        if (buff.ImmediateHealPctMaxHp is not { } share)
        {
            return;
        }

        var healed = (int)Math.Round(run.MaxHp * share, MidpointRounding.ToEven);
        var current = Math.Min(run.MaxHp, run.CurrentHp + healed);

        run.SetHitPoints(current, run.MaxHp);
    }
}


/// <summary>Which of the run's curses a Shrine's Cleanse would remove, and whether it offers one.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every curse this build applies is cleansable, and that is a decision rather than an
/// omission.</b> `19` Part E says "SOME curses can be cleansed at a Shrine or by an ad", and never
/// marks which — there is no <c>cleansable</c> column in <c>curses.json</c> and no list in the
/// document. Treating them all as cleansable is the reading that cannot silently strand a player:
/// the alternative is inventing the column (steering S6), and inventing it the wrong way makes a
/// curse permanent that the design meant to be removable, on a tile whose whole purpose is removing
/// it. The day the column is authored, this type is the one place that reads it.
/// </para>
/// <para>
/// In <c>Rules/</c> rather than beside the handler that calls it, because `30` §11.4 makes
/// <c>Core/Handlers/</c> one type per command: a shared helper there widens the set of types the
/// architecture suite treats as dispatch surface.
/// </para>
/// </remarks>
internal static class ShrineCleanse
{
    /// <summary>The curse a Cleanse would remove, or <c>null</c> when the run carries none.</summary>
    /// <remarks>
    /// The FIRST applied, not the player's pick: `03` §7a.5 says "of the player's choice" and
    /// <c>ShrineChooseCommand</c> carries a slot index with no room for a curse id — a gap named
    /// rather than papered over, closable by widening that payload the day the screen offers the
    /// list. First rather than last so the choice is stable: a run that picks up another curse
    /// between the screen being drawn and the command landing still cleanses the one the screen
    /// named.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    internal static string? FirstCleansable(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Curses.Count == 0 ? null : run.Curses[0];
    }
}

/// <summary>What a resolved shrine offered the player.</summary>
/// <remarks>
/// Returned rather than stored. The offer is what a client renders; the shrine resolves in one
/// command with no persisted choice. This type exists so that the draw is assertable rather than
/// only observable through its HP side effect.
/// </remarks>
/// <param name="FirstBuffId">The buff drawn into slot 1.</param>
/// <param name="SecondBuffId">
/// The buff drawn into slot 2, or <c>null</c> when <paramref name="IsCleanse"/> replaced it.
/// </param>
/// <param name="IsCleanse">
/// Whether slot 2 is the Cleanse. A Cleanse grants nothing and removes nothing today: there is no
/// curse list to remove from yet, so skipping the second pool draw is the entirety of the cleanse
/// behaviour this task can deliver, and it is the half that determines the stream position every
/// later draw depends on.
/// </param>
internal readonly record struct ShrineOffer(string FirstBuffId, string? SecondBuffId, bool IsCleanse);

/// <summary>Which rows of the authored pool one shrine drew, by index.</summary>
/// <remarks>
/// Indices rather than rows, because the draw is an index into the pool as the document lists it and
/// that is the fact both the resolver and the view need: one looks the row up to apply its heal, the
/// other to name it on screen.
/// </remarks>
/// <param name="FirstIndex">The row drawn into slot 1 — the one the resolver applies.</param>
/// <param name="SecondIndex">
/// The row drawn into slot 2, or <c>null</c> when a Cleanse took the slot and no second draw was
/// spent at all.
/// </param>
internal readonly record struct ShrineDraw(int FirstIndex, int? SecondIndex);
