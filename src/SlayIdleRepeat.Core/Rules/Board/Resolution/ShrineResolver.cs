using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// 🔒 `03` §7a.5 — <c>TILE_SHRINE</c>: two <b>distinct</b> options drawn with equal weight off
/// `14` §8.1's <c>shrine</c> stream, or one option plus a Cleanse when a cleansable curse is active.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Two options are OFFERED; exactly one is applied.</b> §7a.5's shrine is a choice, and a
/// player never receives both rows — see <see cref="Resolve"/> for why slot 1 is the one taken and
/// what has to exist before that stops being an assumption.
/// </para>
/// <para>
/// ⚠️ <b>Only the immediate-heal half of a drawn buff is applied.</b> §7a.5 makes the buffs
/// <em>"permanent for the run and stack additively"</em>, which needs a run-scoped stat-aggregation
/// consumer that does not exist — <c>SubjectSetFloorTests</c>' and <c>GapRegister</c>'s
/// <c>ShrineBuff</c>/M3-11 territory. Applying a stat buff to nothing would be invisible; applying a
/// guessed one would be worse. So <c>SHR_HEAL</c>'s and <c>SHR_HP</c>'s
/// <c>immediateHealPctMaxHp</c> is honoured — that component is mechanically real today — and the
/// stat component of all nine stat rows is deliberately not applied.
/// </para>
/// <para>
/// 🔒 <b>The result is almost always empty, and that is correct.</b> A heal is a <c>Run</c> mutation
/// through <see cref="Run.SetHitPoints"/>, which returns <c>void</c>: there is no HP domain event in
/// the game (`30` §7 authors <c>CurrencyChanged</c> and <c>DiceRolled</c> and no third), and
/// inventing one here would be a wire change this task is not authorised to make. A shrine moves no
/// currency, so this resolver returns an empty list every time and mutates the run in place.
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
    /// 🔒 Whether the run holds at least one cleansable curse — §7a.5's <em>"with at least one active
    /// cleansable curse, option slot 2 is always a Cleanse"</em>.
    /// <para>
    /// ⚠️ <b>An explicit parameter, and it cannot be anything else today.</b> <c>Run</c> holds no
    /// curse list: that is <c>GapRegister</c>'s <c>Curses</c> entry, M3-11's, and authoring one here
    /// to read would freeze the curse shape under four rules none of which is written. The caller
    /// passes <c>false</c> until M3-11 lands the list, and the parameter is where it will be wired in.
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

        // 🔒 The cleanse branch takes NO second draw, rather than drawing and discarding. 14 §8.1's
        // counter is the number of draws taken, and a client replaying this tile takes the same
        // branch off the same state — so a discarded draw here would leave the two streams one index
        // apart for the rest of the run. Slot 2 is decided, not drawn.
        int? second = null;

        if (!hasCleansableCurse)
        {
            second = DistinctSecond(stream, first, count);
        }

        // 🔒 ONE option is applied — slot 1 — and NOT both, which is the correctness of this line.
        // §7a.5 OFFERS two distinct options and the player takes ONE of them; there is no state in
        // which they receive both. Applying both drawn rows' heals paid up to 58% of Max HP
        // (SHR_HEAL's 40 plus SHR_HP's 18) where no single option pays more than 40, so it was not a
        // generous reading of the spec but an impossible one.
        // ⚠️ That slot 1 is the one taken IS an assumption, and it is the narrow kind: there is no
        // SHRINE_CHOOSE in 14 §2.3's registry, so the player cannot express a pick and the resolver
        // has to settle it. Slot 1 is the option present in BOTH branches — the cleanse branch has
        // no slot 2 at all — so it is the only choice that resolves identically either way. The day
        // a choose command exists, this is the line it replaces.
        ApplyImmediateHeal(input.Run, tuning.Buffs[first]);

        return new ShrineOffer(
            tuning.Buffs[first].Id,
            second is { } secondIndex ? tuning.Buffs[secondIndex].Id : null,
            IsCleanse: hasCleansableCurse);
    }

    /// <summary>
    /// 🔒 Sampling without replacement in exactly one draw: pick from the <c>count - 1</c> indices
    /// that are not <paramref name="first"/>, then map that back into the full range by stepping past
    /// <paramref name="first"/>.
    /// </summary>
    /// <remarks>
    /// The remap is <c>&gt;=</c> and not <c>&gt;</c>, and that is the whole correctness of it: the
    /// reduced draw's value <c>first</c> must become <c>first + 1</c>, because <c>first</c> itself is
    /// the index being excluded. A <c>&gt;</c> would return <paramref name="first"/> again and offer
    /// the same buff twice, which is precisely what §7a.5's "2 <b>distinct</b> options" forbids.
    /// <para>
    /// Reject-and-redraw would be the other way to do this and is rejected on purpose: it consumes a
    /// variable number of draw indices, which breaks the one-call-one-index property `14` §8.1's
    /// whole persistence model rests on — the same reasoning <c>DeterministicRng.Range</c>'s own
    /// remarks give for accepting modulo bias.
    /// </para>
    /// </remarks>
    private static int DistinctSecond(DeterministicRng stream, int first, int count)
    {
        var reduced = stream.Range(0, count - 1);

        return reduced >= first ? reduced + 1 : reduced;
    }

    /// <summary>
    /// Applies a drawn row's <c>immediateHealPctMaxHp</c>, if it has one. ⚠️ Overheal is clamped
    /// <b>here</b>, by the rule that computes it — <see cref="Run.SetHitPoints"/> refuses a current
    /// above the maximum rather than trimming it silently (`30` §11.5).
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

/// <summary>What a resolved shrine offered the player (`03` §7a.5).</summary>
/// <remarks>
/// ⚠️ <b>Returned rather than stored.</b> The offer is what a client renders, and `03` §7a.5's
/// options are not a choice the domain persists today — there is no <c>SHRINE_CHOOSE</c> in
/// `14` §2.3's registry at all, so the shrine resolves in one command. This type exists so that the
/// draw is <em>assertable</em> rather than only observable through its HP side effect.
/// </remarks>
/// <param name="FirstBuffId">The buff drawn into slot 1.</param>
/// <param name="SecondBuffId">
/// The buff drawn into slot 2, or <c>null</c> when <paramref name="IsCleanse"/> replaced it.
/// </param>
/// <param name="IsCleanse">
/// Whether slot 2 is §7a.5's Cleanse. ⚠️ A Cleanse grants nothing and removes nothing today: there
/// is no curse list to remove from (M3-11's <c>Curses</c> gap), so <b>skipping the second pool
/// draw</b> is the entirety of the cleanse behaviour this task can deliver, and it is the half that
/// determines the stream position every later draw depends on.
/// </param>
internal readonly record struct ShrineOffer(string FirstBuffId, string? SecondBuffId, bool IsCleanse);
