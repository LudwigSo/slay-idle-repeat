using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.BalanceHarness.Sweep;

/// <summary>
/// Where every <c>battleSeed</c> in the sweep comes from — the game's own primitives
/// (<c>Hash64</c>/<c>SeedDerivation</c>), never a hash this tool wrote, so the cross-platform
/// determinism pin that covers <c>Hash64</c> covers this too.
/// </summary>
/// <remarks>
/// The seed is a pure function of <c>(chapter, tier, archetype, fightIndex)</c> and nothing else — not
/// the thread, not scheduling order, and crucially not the variant, so an A/B experiment compares the
/// same fights on identical seeds and the measured difference is the effect and not the sample. This
/// is why <see cref="CellSeed"/> takes no variant parameter and must not grow one. Seeds are also
/// independent of <c>--fights</c>: fight <c>k</c> of a cell has the same seed at any run size, so a
/// small run is a prefix of a large one and the two are comparable.
/// </remarks>
public static class SweepSeeds
{
    /// <summary>The domain label mixed into every cell seed, so it can never collide with a real run's.</summary>
    public const string CellStream = "balance-harness:cell";

    /// <summary>
    /// The seed identifying one <c>(chapter, tier, archetype)</c> cell — the harness's stand-in for
    /// a run seed.
    /// </summary>
    public static ulong CellSeed(int chapter, Tier tier, string archetypeId)
    {
        ArgumentNullException.ThrowIfNull(archetypeId);

        return Hash64.Of(CellStream, chapter, tier, archetypeId);
    }

    /// <summary>
    /// The <c>battleSeed</c> for one fight of a cell, through the game's own
    /// <c>SeedDerivation.BattleSeed(runSeed, battleIndex)</c>.
    /// </summary>
    public static ulong FightSeed(ulong cellSeed, int fightIndex) =>
        SeedDerivation.BattleSeed(cellSeed, fightIndex);

    /// <summary>The <c>battleSeed</c> for one fight of one cell, in one call.</summary>
    public static ulong FightSeed(int chapter, Tier tier, string archetypeId, int fightIndex) =>
        FightSeed(CellSeed(chapter, tier, archetypeId), fightIndex);
}
