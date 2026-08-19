using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SHRINE_CHOOSE</c> handler: takes one of the two options a Shrine tile offers, applies it,
/// and clears the tile.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The draw happens HERE, not at <c>RESOLVE_TILE</c>, and that is what makes the offer stable
/// across the two commands.</b> A shrine's two options are a function of the run's committed
/// <c>shrine</c> stream position; leaving the position untouched until the choice lands means the
/// screen (<c>Rules.Board.ShrineView</c>) and this handler read the same two rows off the same
/// number, with no third field to hold an offer that the seed already determines. <c>RESOLVE_TILE</c>
/// only acknowledges the tile, exactly as it does for a Campfire.
/// </para>
/// <para>
/// 🔒 <b>The whole buff is applied now, both halves of it.</b> A drawn row contributes its immediate
/// heal (the half that was already real) AND joins the run's shrine-buff list, which
/// <c>Rules.Effects.ShrineBuffEffectSource</c> turns into a permanent stat move for the rest of the
/// run. Taking only the heal — which is what happened before that source existed — is what made
/// eight of the ten pool rows indistinguishable from taking nothing.
/// </para>
/// <para>
/// The Cleanse takes slot 2 whenever the run carries a cleansable curse (`03` §7a.5), and the run's
/// own curse list is what answers that now. It removes ONE curse, and which one is the first the run
/// applied rather than the player's pick: `03` §7a.5 says "of the player's choice", and
/// <c>ShrineChooseCommand</c> carries a slot index rather than a curse id — a gap named here rather
/// than papered over, and closable by widening that payload the day the screen offers the list.
/// </para>
/// </remarks>
internal static class ShrineChoose
{
    /// <summary>The slot a Cleanse occupies when one is offered — always the second (`03` §7a.5).</summary>
    internal const int CleanseSlot = 1;

    /// <summary>Applies <c>SHRINE_CHOOSE</c>.</summary>
    /// <param name="command">Which of the two options.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no shrine is pending or the index names no
    /// offered option; otherwise accepted, with the option applied and the tile cleared.
    /// </returns>
    internal static HandlerResult Handle(ShrineChooseCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile || (TileKind)run.PendingTileKindValue != TileKind.Shrine)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var cleansable = ShrineCleanse.FirstCleansable(run);
        var offersCleanse = cleansable is not null;

        // Checked BEFORE the draw, so an out-of-range index costs the run no stream index. A shrine
        // offering a Cleanse has one drawn row and one Cleanse; one with none has two drawn rows —
        // either way, two slots.
        if (command.OptionIndex is < 0 or > CleanseSlot)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var tuning = ShrineTuning.Read(input.Context.Content);
        var drawn = ShrineResolver.Draw(tuning, input.Rng.Stream(RngStreams.Shrine), offersCleanse);

        if (offersCleanse && command.OptionIndex == CleanseSlot)
        {
            run.CleanseCurse(cleansable!);
            run.ClearPendingTile();

            return HandlerResult.Accept();
        }

        // Slot 1 is always a drawn row; slot 2 is one too whenever no Cleanse took it. SecondIndex is
        // non-null in exactly that case, which is the same condition, so the null-forgiving read is
        // the branch's own precondition rather than an assumption about the draw.
        var poolIndex = command.OptionIndex == CleanseSlot ? drawn.SecondIndex!.Value : drawn.FirstIndex;
        var row = tuning.Buffs[poolIndex];

        ShrineResolver.ApplyBuff(run, row);
        run.ClearPendingTile();

        return HandlerResult.Accept();
    }
}

