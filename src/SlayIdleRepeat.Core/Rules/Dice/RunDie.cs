using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// The one place a <see cref="DieFace"/> is encoded into the opaque <c>int</c> a <c>Run</c> persists
/// and decoded back out of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Run.DieFaceUpgrades</c> stores <c>int</c> codes because `30` §11.4 forbids <c>Model</c> from
/// naming the die vocabulary — the same reason <c>Run.PendingTileKindValue</c> is an <c>int</c>. This
/// is the seam that crosses it, exactly once, the way <see cref="DieFaceKindCodec"/> is the seam for
/// the wire token.
/// </para>
/// <para>
/// 🔒 <b>The encoding is a wire value, because it is persisted.</b> A code written into a run's row
/// today must decode to the same face after any later edit here: the multipliers below are frozen,
/// and a new component would have to take a place above them rather than renumber the existing ones.
/// The layout is <c>kind × 1000 + pips × 10 + tier</c>, which leaves both lower components room to
/// grow past their current 6 and 3 before they could ever collide.
/// </para>
/// </remarks>
internal static class DieFaceCodec
{
    /// <summary>The multiplier the face kind occupies.</summary>
    private const int KindScale = 1000;

    /// <summary>The multiplier the pip count occupies.</summary>
    private const int PipScale = 10;

    /// <summary>Encodes a face into the code a <c>Run</c> persists.</summary>
    /// <param name="face">The face. Never <see cref="DieFace.IsUnset"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The face is unset, or its kind is not a defined member.</exception>
    internal static int Encode(DieFace face)
    {
        if (face.IsUnset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(face), face,
                "A default DieFace names no face, so there is nothing to encode. Build one with " +
                "DieFace.Pip or DieFace.Special.");
        }

        if (!Enum.IsDefined(face.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(face), face.Kind, "04 §1 fixes DieFaceKind at six named members.");
        }

        return ((int)face.Kind * KindScale) + (face.Value * PipScale) + face.Tier;
    }

    /// <summary>Decodes a persisted code back into a face.</summary>
    /// <param name="code">A code <see cref="Encode"/> produced.</param>
    /// <returns>The face, or <c>null</c> when the code decodes to nothing this build recognises.</returns>
    /// <remarks>
    /// 🔒 Answers <c>null</c> rather than throwing, and the difference matters at the one call site:
    /// a code this build cannot read comes from a row written by a version that could, and the run
    /// carrying it must still be playable. The caller falls back to the starting die's face for that
    /// index — the player loses the upgrade, not the run. Refusing would brick the run at its next
    /// <c>ROLL_DICE</c>, which is the wound this whole change exists to prevent.
    /// </remarks>
    internal static DieFace? TryDecode(int code)
    {
        if (code < 0)
        {
            return null;
        }

        var kind = (DieFaceKind)(code / KindScale);
        var pips = code / PipScale % (KindScale / PipScale);
        var tier = code % PipScale;

        if (!Enum.IsDefined(kind) || tier is < DieFace.MinTier or > DieFace.MaxTier)
        {
            return null;
        }

        if (kind == DieFaceKind.Pip)
        {
            return pips is >= Content.Effects.DieFaceIndex.MinFace and <= Content.Effects.DieFaceIndex.MaxFace
                ? DieFace.Pip(pips)
                : null;
        }

        return DieFace.Special(kind, tier);
    }
}

/// <summary>The six-face die a run actually rolls: the starting die with its Dice Forge upgrades on top.</summary>
/// <remarks>
/// <para>
/// The composition itself is <see cref="DieComposer"/>'s; this type is the seam that turns a run's
/// persisted upgrade map into the layer list that composer takes, and it is where the decision about
/// an unreadable code lives.
/// </para>
/// <para>
/// ⚠️ Talents, mounts, perks and curse-inflicted faces are still absent from the layer list, and
/// each is absent for its own reason rather than being forgotten: talents and mounts are unbuilt
/// systems, no authored perk carries a <c>MODIFY_DIE_FACE</c>, and the one curse that rewrites faces
/// (<c>CUR_LEADFOOT</c>) acts on face KINDS wherever they sit, which a by-index map cannot express —
/// see <c>CurseEffects.UnappliedReason</c>. <see cref="DieComposer.Compose"/> takes the layers in
/// precedence order, so each of them becomes another entry here rather than a change to this shape.
/// </para>
/// </remarks>
internal static class RunDie
{
    /// <summary>The die <paramref name="run"/> rolls right now.</summary>
    /// <param name="run">The run.</param>
    /// <returns>Six faces, always — the starting die where the run has installed nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    internal static IReadOnlyList<DieFace> Of(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var upgrades = run.DieFaceUpgrades;

        if (upgrades.Count == 0)
        {
            return DieComposer.StartingDie;
        }

        var layers = new List<DieFaceOverride>(upgrades.Count);

        // Ascending face index, not the map's enumeration order: DieComposer is last-write-wins per
        // index, so with one entry per index the order cannot change the result — but a dictionary's
        // order is a device-dependent fact, and letting one reach a composition is how a client and
        // a server end up rolling different dice from the same row.
        for (var faceIndex = Content.Effects.DieFaceIndex.MinFace;
             faceIndex <= Content.Effects.DieFaceIndex.MaxFace;
             faceIndex++)
        {
            if (!upgrades.TryGetValue(faceIndex, out var code))
            {
                continue;
            }

            // A code this build cannot read leaves the starting face standing — see
            // DieFaceCodec.TryDecode's remarks for why that is a fallback rather than a refusal.
            if (DieFaceCodec.TryDecode(code) is { } face)
            {
                layers.Add(new DieFaceOverride(faceIndex, face));
            }
        }

        return DieComposer.Compose(DieComposer.StartingDie, layers);
    }

    /// <summary>
    /// The movement a rolled face produces once the run's curses have had their say.
    /// </summary>
    /// <param name="rolled">The face the bag drew, already composed.</param>
    /// <param name="run">The run, for its active curses.</param>
    /// <returns>The movement, and the pips the penalty removed (0 when nothing applied).</returns>
    /// <remarks>
    /// 🔒 Only a <see cref="DieFaceKind.Pip"/> face is penalised, and only down to
    /// <see cref="CurseEffects.MinimumPipAfterPenalty"/> — `19` Part E's <c>CUR_SLIPPERY</c> reads
    /// <em>"-1 to all Pip rolls (minimum 1)"</em>, and the floor is the whole reason the curse cannot
    /// stop a run dead: a hero who rolled a 1 still moves one node.
    /// </remarks>
    internal static (int Movement, int PipsLost) PenalisedMovement(DieFace rolled, Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (rolled.IsUnset || rolled.Kind != DieFaceKind.Pip)
        {
            return (0, 0);
        }

        var penalty = 0;

        foreach (var curseId in run.Curses)
        {
            penalty += CurseEffects.PipPenalty(curseId);
        }

        if (penalty == 0)
        {
            return (rolled.Value, 0);
        }

        var moved = Math.Max(CurseEffects.MinimumPipAfterPenalty, rolled.Value - penalty);

        return (moved, rolled.Value - moved);
    }

    /// <summary>Renders a face index for a failure message.</summary>
    internal static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
