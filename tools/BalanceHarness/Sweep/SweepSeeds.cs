using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.BalanceHarness.Sweep;

/// <summary>
/// 🔒 Where every <c>battleSeed</c> in the sweep comes from — the game's own primitives, never a hash
/// this tool wrote.
/// </summary>
/// <remarks>
/// <para>
/// <c>Hash64</c> and <c>SeedDerivation</c> are public in <c>SlayIdleRepeat.Core</c> and are the same
/// two functions a real run derives its battle seeds with (`14` §8.1, `02` §2). Rolling a private
/// hash here would give the harness a determinism story the game does not have: `14` §8.2's
/// cross-platform pin covers <c>Hash64</c>, and a bespoke mixer would be unpinned on ARM64 while
/// looking identical on the developer's machine.
/// </para>
/// <para>
/// 🔒 <b>The seed is a pure function of <c>(chapter, tier, archetype, fightIndex)</c> and of nothing
/// else.</b> Not of the thread that ran it, not of the order cells were scheduled in, and — crucially
/// — not of the <em>variant</em>. The A/B experiments therefore compare the same fights: Thornmaw with
/// and without its phase-3 <c>RAGE</c> runs on identical seeds, so the measured difference is the
/// effect and not the sample. That is also why <see cref="CellSeed"/> takes no variant parameter and
/// must not grow one.
/// </para>
/// <para>
/// ⚠️ Seeds are independent of <c>--fights</c>: fight <c>k</c> of a cell has the same seed in a
/// 200-fight run as in a 10 000-fight run, so a small run is a prefix of a large one rather than a
/// different sample. That is what lets the PR-tier subset and the nightly sweep be compared.
/// </para>
/// </remarks>
public static class SweepSeeds
{
    /// <summary>
    /// 🔒 The domain label mixed into every cell seed, so a harness seed can never collide with a
    /// real run's <c>runSeed</c>-derived one.
    /// </summary>
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
    /// 🔒 `14` §8.1 — the <c>battleSeed</c> for one fight of a cell, through the game's own
    /// <c>SeedDerivation.BattleSeed(runSeed, battleIndex)</c>.
    /// </summary>
    public static ulong FightSeed(ulong cellSeed, int fightIndex) =>
        SeedDerivation.BattleSeed(cellSeed, fightIndex);

    /// <summary>The <c>battleSeed</c> for one fight of one cell, in one call.</summary>
    public static ulong FightSeed(int chapter, Tier tier, string archetypeId, int fightIndex) =>
        FightSeed(CellSeed(chapter, tier, archetypeId), fightIndex);
}
