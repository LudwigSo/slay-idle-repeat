using System.Globalization;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// The Elite Modifier draw — one modifier per Elite, never the previous one.
/// </summary>
/// <remarks>
/// <para>
/// Redraw, not exclude-then-draw: the two produce the same distribution over eight equally weighted
/// rows but a different number of draw indices, and the draw index is persisted and auditable — a
/// replay of the same battle seed must reproduce the same attempts, so collapsing this to a single
/// draw over seven rows would be a quiet determinism change dressed as a tidy-up.
/// </para>
/// <para>
/// The weights are equal because none are authored — see <see cref="UniformWeight"/>.
/// </para>
/// </remarks>
internal static class EliteModifierDraw
{
    /// <summary>The weight every modifier carries, since no weighting is authored.</summary>
    internal const double UniformWeight = 1.0;

    /// <summary>
    /// The number of collisions after which the redraw is treated as a defect rather than as bad
    /// luck.
    /// </summary>
    /// <remarks>
    /// With eight equally weighted rows the chance of this many consecutive collisions is 8^-64, so
    /// no reachable seed hits it — but a pool edited down to a single row would loop forever inside
    /// the game's hottest path, and a hang cannot be diagnosed from a log. This converts it into a
    /// throw that names the cause.
    /// </remarks>
    internal const int MaxAttempts = 64;

    /// <summary>
    /// Draws one modifier, redrawing while it collides with the run's previous one.
    /// </summary>
    /// <param name="rng">
    /// A combat stream opened at the encounter's battle seed. Each attempt consumes exactly one draw
    /// index.
    /// </param>
    /// <param name="modifiers">The rows, as authored in <c>content/enemies/enemies.json</c>.</param>
    /// <param name="history">The run's memory of the previous Elite's modifier.</param>
    /// <param name="noRepeat">
    /// <c>elites.noRepeatWithPreviousEliteInRun</c>, as authored. Handed in rather than assumed.
    /// </param>
    /// <returns>The modifier drawn. The caller records it through <paramref name="history"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="modifiers"/> is empty, or holds only the previous modifier so no draw can
    /// satisfy the rule.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The redraw collided <see cref="MaxAttempts"/> times — see that constant's remarks.
    /// </exception>
    internal static EliteModifier Draw(
        DeterministicRng rng,
        IReadOnlyList<EliteModifierRow> modifiers,
        IEliteModifierHistory history,
        bool noRepeat)
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

        var previous = noRepeat ? history.PreviousEliteModifier : null;

        if (previous is { } excluded && modifiers.All(m => m.Id == excluded))
        {
            throw new ArgumentException(
                $"05 §6.2 forbids drawing {excluded} again immediately, and every row of this pool is " +
                $"{excluded} — so 'redraw on collision' can never terminate. A pool of one is the " +
                "editing mistake this check exists to name.",
                nameof(modifiers));
        }

        // Built once, outside the loop: rebuilding an identical table per attempt would only allocate.
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
