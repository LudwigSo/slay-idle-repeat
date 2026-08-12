using System.Globalization;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6.2's Elite Modifier draw — one modifier per Elite, never the previous one.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6.2: <em>"plus <b>one Elite Modifier</b> drawn from"</em> the eight, and 🔒 <em>"No Elite
/// may draw the same modifier as the immediately preceding Elite in the same run — redraw on
/// collision."</em>
/// </para>
/// <para>
/// 🔒 <b>Redraw, not exclude-then-draw.</b> The two produce the same distribution over eight equally
/// weighted rows and a <em>different number of draw indices</em>, and the draw index is persisted
/// (`14` §8.1) and auditable. `05` §6.2 authors "redraw on collision", so that is what happens: the
/// stream advances once per attempt, and a replay of the same battle seed reproduces the same
/// attempts. Collapsing it to a single draw over seven rows would be a quiet determinism change
/// dressed as a tidy-up.
/// </para>
/// <para>
/// 🔒 <b>The weights are equal because `05` §6.2 states none.</b> It writes the eight names as a
/// flat list. Inventing a weighting would be exactly the fabricated number `16` R6 forbids — see
/// <see cref="UniformWeight"/>.
/// </para>
/// <para>
/// ⚠️ <b>The seed is handed in.</b> `14` §8.1 roots the combat stream at a <c>battleSeed</c>;
/// nothing here derives or holds a <c>runSeed</c>, and the caller opens the stream.
/// </para>
/// </remarks>
internal static class EliteModifierDraw
{
    /// <summary>
    /// 🔒 The weight every modifier carries, because `05` §6.2 states no weighting at all.
    /// </summary>
    internal const double UniformWeight = 1.0;

    /// <summary>
    /// The number of collisions after which the redraw is treated as a defect rather than as bad
    /// luck.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Not a rule of `05` §6.2 and not a change to it.</b> With eight equally weighted rows the
    /// chance of this many consecutive collisions is 8⁻⁶⁴, so no reachable seed hits it — but a pool
    /// that has been edited down to a single row would loop forever inside the game's hottest path,
    /// and a hang is the one failure that cannot be diagnosed from a log. This converts it into a
    /// throw that names the cause. If a future rule really does make a redraw likely, this constant
    /// is the thing to argue with.
    /// </remarks>
    internal const int MaxAttempts = 64;

    /// <summary>
    /// `05` §6.2 — draws one modifier, redrawing while it collides with the run's previous one.
    /// </summary>
    /// <param name="rng">
    /// A combat stream opened at the encounter's <c>battleSeed</c> — <c>new DeterministicRng(battleSeed,
    /// RngStreams.Combat)</c>. Each attempt consumes exactly one draw index.
    /// </param>
    /// <param name="modifiers">`05` §6.2's rows, as authored in <c>content/enemies/enemies.json</c>.</param>
    /// <param name="history">The run's memory of the previous Elite's modifier.</param>
    /// <returns>The modifier drawn. The caller records it through <paramref name="history"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="modifiers"/> is empty, or holds only the previous modifier so no draw can
    /// satisfy the rule.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The redraw collided <see cref="MaxAttempts"/> times — see that constant's remarks.
    /// </exception>
    internal static EliteModifier Draw(
        DeterministicRng rng, IReadOnlyList<EliteModifierRow> modifiers, IEliteModifierHistory history)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(history);

        if (modifiers.Count == 0)
        {
            throw new ArgumentException(
                "05 §6.2 declares eight Elite Modifiers and this pool is empty. An Elite with no " +
                "modifier is not an Elite the section describes.",
                nameof(modifiers));
        }

        var previous = history.PreviousEliteModifier;

        if (previous is { } excluded && modifiers.All(m => m.Id == excluded))
        {
            throw new ArgumentException(
                $"05 §6.2 forbids drawing {excluded} again immediately, and every row of this pool is " +
                $"{excluded} — so 'redraw on collision' can never terminate. A pool of one is the " +
                "editing mistake this check exists to name.",
                nameof(modifiers));
        }

        // 🔒 Built once, outside the loop: WeightedPick reads the table and consumes one draw index
        // per call, and rebuilding an identical table per attempt would only allocate.
        var table = new List<(EliteModifier item, double weight)>(modifiers.Count);
        foreach (var row in modifiers)
        {
            table.Add((row.Id, UniformWeight));
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var drawn = rng.WeightedPick(table);

            if (drawn != previous)
            {
                return drawn;
            }
        }

        throw new InvalidOperationException(
            $"05 §6.2's redraw collided with {previous} " +
            $"{MaxAttempts.ToString(CultureInfo.InvariantCulture)} times running. With " +
            $"{modifiers.Count.ToString(CultureInfo.InvariantCulture)} equally weighted rows that is " +
            "not luck — the pool or the weights have been edited into a shape where the rule cannot " +
            "be satisfied. See EliteModifierDraw.MaxAttempts.");
    }
}
