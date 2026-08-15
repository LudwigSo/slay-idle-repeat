using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `04` §§1,3,4 — the <c>ROLL_DICE</c> handler (M3-04): draws a face from the Fair-Dice bag,
/// resolves its movement (chaining `04` §1's <see cref="DieFaceKind.Chain"/> up to
/// <see cref="FaceEffectResolver.ChainMaxLinks"/> times), applies any <see cref="DieFaceKind.Surge"/>
/// heal, and moves the run.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What this handler does NOT do, and why.</b> `04` §2's die-composition sources — talents,
/// mounts, drafted perks, Dice Forge upgrades, curses — are all still <c>GapRegister</c> entries on
/// <c>Run</c> (M3-06/M3-11/M4's talents+mounts). This handler therefore always rolls against
/// <see cref="DieComposer.StartingDie"/> — six <see cref="DieFaceKind.Pip"/> faces — rather than a
/// composed one; <see cref="DieComposer"/> exists and is tested, ready for the milestone with real
/// sources to hand it. It also does not double- or halve- any tile reward for a
/// <see cref="DieFaceKind.Fortune"/>/<see cref="DieFaceKind.Void"/> face (unreachable from the
/// starting die today, and the reward itself is <c>ResolveTileCommand</c>'s, M3-03) — a future
/// <c>RESOLVE_TILE</c> handler reads the rolled <see cref="DieFace"/> off the <see cref="DiceRolled"/>
/// event to know whether to apply either multiplier.
/// </para>
/// <para>
/// ⚠️ <b><see cref="DieFaceKind.Star"/> is refused rather than guessed at</b> — see
/// <see cref="FaceEffectResolver"/>'s remarks for why <c>RollDiceCommand</c> carries no payload a
/// player's chosen movement could arrive on. Unreachable from the all-Pip starting die today.
/// </para>
/// <para>
/// 🔒 <b>The Fair-Dice bag's weight vector is reconstructed by replay, not persisted</b> — see
/// <see cref="FairDiceBag"/>'s remarks. The reset point is 0 (the whole run) because no Stage Gate
/// concept exists on <c>Run</c> yet (M3-05); the day it does, this handler's one <c>resetAtDraw</c>
/// argument is the only line that changes.
/// </para>
/// </remarks>
internal static class RollDice
{
    /// <summary>🔒 `04` §1 — applies <c>ROLL_DICE</c>.</summary>
    internal static HandlerResult Handle(RollDiceCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;
        var events = new List<DomainEvent>();
        var position = run.Position;
        var currentHp = run.CurrentHp;
        var chainLinks = 0;

        while (true)
        {
            var committedDraws = run.StreamPosition(RngStreams.Dice) + (ulong)events.Count;
            var weights = FairDiceBag.Replay(run.RunSeed, resetAtDraw: 0, uptoDraw: committedDraws);
            var (faceValue, _) = FairDiceBag.Step(input.Rng.Stream(RngStreams.Dice), weights);

            var face = DieComposer.StartingDie[faceValue - 1];
            var outcome = FaceEffectResolver.Resolve(face, chainLinks);

            if (outcome.RequiresPlayerChoice)
            {
                // 🔒 Unreachable from an all-Pip die today (see this type's remarks) — a defensive
                // domain-tier refusal rather than a thrown exception, because RollDiceCommand
                // genuinely carries no payload a Star choice could have arrived on: this is a gap in
                // the command's vocabulary, not a defect in this handler's own logic.
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            events.Add(new DiceRolled(DomainEvent.UnstampedSequence, face));
            position += outcome.Movement;

            if (outcome.HealPct is { } healPct)
            {
                var healed = currentHp + (int)Math.Round(run.MaxHp * healPct, MidpointRounding.AwayFromZero);
                currentHp = Math.Min(healed, run.MaxHp);
            }

            if (!outcome.RollAgain)
            {
                break;
            }

            chainLinks++;
        }

        run.MoveTo(position);

        if (currentHp != run.CurrentHp)
        {
            run.SetHitPoints(currentHp, run.MaxHp);
        }

        return HandlerResult.Accept(events);
    }
}
