using System.Globalization;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// The Fair-Dice weighted bag: <c>w[6]</c> initialised to 1.0, a draw's face decays its own weight
/// and boosts every other, both clamped, reset at each Stage Gate.
/// </summary>
/// <remarks>
/// The weight vector is not persisted, and doesn't need to be: since RNG draws are randomly
/// accessible, the weight vector after N draws is a pure function of N alone — replaying draws
/// 0..N-1 through <see cref="Step"/> from the initial vector is deterministic and byte-identical
/// every time. <see cref="Replay"/> does exactly that, so <c>Run</c> needs no new field, only the
/// draw count it already tracks. The Stage Gate reset point is not yet wired to any stage-boundary
/// concept on <c>Run</c>, so <see cref="Replay"/> takes it as a parameter (0 today) rather than this
/// type assuming a reset always means index 0.
/// </remarks>
internal static class FairDiceBag
{
    /// <summary>Every face starts at weight 1.0.</summary>
    public const double InitialWeight = 1.0;

    /// <summary>A drawn face's own weight is multiplied by this.</summary>
    public const double DecayFactor = 0.55;

    /// <summary>Every other face's weight is increased by this.</summary>
    public const double BoostAmount = 0.12;

    /// <summary>The floor every weight is clamped to.</summary>
    public const double MinWeight = 0.25;

    /// <summary>The ceiling every weight is clamped to.</summary>
    public const double MaxWeight = 2.0;

    /// <summary>The number of faces the bag weighs.</summary>
    public const int FaceCount = 6;

    /// <summary>The bag's initial weight vector: six faces, all at <see cref="InitialWeight"/>.</summary>
    public static IReadOnlyList<double> InitialWeights { get; } =
        Array.AsReadOnly(Enumerable.Repeat(InitialWeight, FaceCount).ToArray());

    /// <summary>
    /// One draw: picks a face by <paramref name="weights"/> and returns the drawn face (1..6) plus
    /// the bag's weights after the decay/boost/clamp update. Consumes exactly one draw from
    /// <paramref name="rng"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> or <paramref name="weights"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="weights"/> does not have exactly <see cref="FaceCount"/> entries.</exception>
    public static (int Face, IReadOnlyList<double> NextWeights) Step(
        DeterministicRng rng, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(rng);
        RequireSixWeights(weights);

        var table = new (int item, double weight)[FaceCount];
        for (var i = 0; i < FaceCount; i++)
        {
            table[i] = (i + 1, weights[i]);
        }

        var drawnFace = rng.WeightedPick<int>(table);
        var next = new double[FaceCount];

        for (var i = 0; i < FaceCount; i++)
        {
            var isDrawn = i + 1 == drawnFace;
            var updated = isDrawn ? weights[i] * DecayFactor : weights[i] + BoostAmount;
            next[i] = Math.Clamp(updated, MinWeight, MaxWeight);
        }

        return (drawnFace, Array.AsReadOnly(next));
    }

    /// <summary>
    /// Reconstructs the bag's weight vector after <paramref name="uptoDraw"/> draws of the
    /// <c>dice</c> stream, counting from a reset point at <paramref name="resetAtDraw"/> — see the
    /// type remarks for why this is a replay rather than stored state.
    /// </summary>
    /// <param name="runSeed">The run's committed seed.</param>
    /// <param name="resetAtDraw">
    /// The draw index the bag last reset at (a Stage Gate). 0 until stage-gate tracking exists.
    /// </param>
    /// <param name="uptoDraw">
    /// The draw index to reconstruct weights AS OF — i.e. <c>Run.RngStreamPositions["dice"]</c>,
    /// the number of draws already taken. Never below <paramref name="resetAtDraw"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="uptoDraw"/> is below <paramref name="resetAtDraw"/>.</exception>
    public static IReadOnlyList<double> Replay(ulong runSeed, ulong resetAtDraw, ulong uptoDraw)
    {
        if (uptoDraw < resetAtDraw)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uptoDraw), uptoDraw,
                "uptoDraw cannot be before resetAtDraw: a bag cannot be asked for its state before " +
                "the point it last reset.");
        }

        var weights = InitialWeights;

        if (uptoDraw == resetAtDraw)
        {
            return weights;
        }

        var rng = DeterministicRng.OpenAt(runSeed, RngStreams.Dice, resetAtDraw);

        for (var draw = resetAtDraw; draw < uptoDraw; draw++)
        {
            (_, weights) = Step(rng, weights);
        }

        return weights;
    }

    private static void RequireSixWeights(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (weights.Count != FaceCount)
        {
            throw new ArgumentException(
                "The Fair-Dice bag weighs exactly " + Text(FaceCount) + " faces (04 §1's die); " +
                Text(weights.Count) + " were supplied.",
                nameof(weights));
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
