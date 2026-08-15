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
/// Two options are offered; exactly one is applied. A player never receives both rows — see
/// <see cref="Resolve"/> for why slot 1 is the one taken and what has to exist before that stops
/// being an assumption.
/// </para>
/// <para>
/// Only the immediate-heal half of a drawn buff is applied. The buffs are meant to be permanent
/// for the run and stack additively, which needs a run-scoped stat-aggregation consumer that does
/// not exist yet. Applying a stat buff to nothing would be invisible; applying a guessed one would
/// be worse. So <c>SHR_HEAL</c>'s and <c>SHR_HP</c>'s <c>immediateHealPctMaxHp</c> is honoured —
/// that component is mechanically real today — and the stat component of the other rows is
/// deliberately not applied.
/// </para>
/// <para>
/// The result is almost always empty, and that is correct. A heal is a <c>Run</c> mutation through
/// <see cref="Run.SetHitPoints"/>, which returns <c>void</c> — there is no HP domain event in the
/// game, and inventing one here would be a wire change this task is not authorised to make. A
/// shrine moves no currency, so this resolver returns an empty list every time and mutates the run
/// in place.
/// </para>
/// </remarks>
internal static class ShrineResolver
{
    /// <summary>
    /// Resolves a shrine tile: draws its options and applies the immediate heal of any drawn row that
    /// carries one.
    /// </summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <param name="hasCleansableCurse">
    /// Whether the run holds at least one cleansable curse — with one active, option slot 2 is
    /// always a Cleanse.
    /// <para>
    /// An explicit parameter, and it cannot be anything else today: <c>Run</c> holds no curse list
    /// yet, and authoring one here to read would freeze the curse shape under rules none of which
    /// is written. The caller passes <c>false</c> until that lands, and the parameter is where it
    /// will be wired in.
    /// </para>
    /// </param>
    /// <returns>
    /// The options drawn, and an empty event list — see this type's remarks for why a heal produces
    /// no event.
    /// </returns>
    internal static ShrineOffer Resolve(HandlerInput input, bool hasCleansableCurse)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tuning = ShrineTuning.Read(input.Context.Content);
        var stream = input.Rng.Stream(RngStreams.Shrine);
        var count = tuning.Buffs.Count;

        var first = stream.Range(0, count);

        // The cleanse branch takes NO second draw, rather than drawing and discarding. A client
        // replaying this tile takes the same branch off the same state, so a discarded draw here
        // would leave the two streams one index apart for the rest of the run. Slot 2 is decided,
        // not drawn.
        int? second = null;

        if (!hasCleansableCurse)
        {
            second = DistinctSecond(stream, first, count);
        }

        // ONE option is applied — slot 1 — and NOT both. The shrine offers two distinct options and
        // the player takes one of them; applying both drawn rows' heals paid up to 58% of Max HP
        // (SHR_HEAL's 40 plus SHR_HP's 18) where no single option pays more than 40, so it was not a
        // generous reading of the spec but an impossible one.
        // That slot 1 is the one taken IS an assumption: there is no choose command, so the player
        // cannot express a pick and the resolver has to settle it. Slot 1 is the option present in
        // BOTH branches — the cleanse branch has no slot 2 at all — so it is the only choice that
        // resolves identically either way. The day a choose command exists, this is the line it
        // replaces.
        ApplyImmediateHeal(input.Run, tuning.Buffs[first]);

        return new ShrineOffer(
            tuning.Buffs[first].Id,
            second is { } secondIndex ? tuning.Buffs[secondIndex].Id : null,
            IsCleanse: hasCleansableCurse);
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
