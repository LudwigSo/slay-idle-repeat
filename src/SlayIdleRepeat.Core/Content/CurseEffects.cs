using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// What each of `19` Part E's ten curses actually DOES, as a narrow, named table keyed on curse
/// id — and, just as importantly, which of them this build cannot yet do and why.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a parser, on <see cref="CurseRewards"/>'s precedent and for its reason: the
/// <c>effect</c> column of <c>content/curses/curses.json</c> is free-form prose with no specified
/// grammar ("-8% DEF", "Enemies +10% ATK for the rest of the stage", "Chain and Surge faces behave
/// as plain Pip 3"). A general parser would be inventing that grammar, and the rows it guessed
/// wrong would be curses that load, appear on screen and change nothing.
/// </para>
/// <para>
/// 🔒 <b>Three classes, and the third is named rather than hidden.</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>Stat-expressible</b> (<see cref="Stat"/>) — six rows whose whole effect is a percentage move
/// on one of `18` §2.1's stats. They enter the fight through <c>Rules.Effects.CurseEffectSource</c>
/// like any other build effect, and two of them (<c>SHOP_PRICE_PCT</c>, <c>GOLD_PCT</c>) are read
/// off the aggregated block by the shop and the gold payout rather than by combat.
/// </item>
/// <item>
/// <b>Board-expressible</b> (<see cref="IsBoardRule"/>) — one row, <c>CUR_SLIPPERY</c>, whose effect
/// is arithmetic on a rolled Pip face. There is no stat for "your die is worse", so it is honoured
/// by <c>Handlers.RollDice</c> reading <see cref="PipPenalty"/> directly.
/// </item>
/// <item>
/// <b>Not yet applied</b> (<see cref="UnappliedReason"/>) — five rows, each with the specific
/// missing mechanism written down. They are still APPLIED to the run: the player carries them, a
/// Shrine can Cleanse them, and the client can name them. What does not happen is the debuff. That
/// is a worse deal for the player than the design intends and a better one than pretending the
/// curse is not there, and it is the only honest option while the mechanism is missing (steering
/// S6).
/// </item>
/// </list>
/// <para>
/// The magnitudes are transcribed from `19` Part E, not derived, and are cross-checked against
/// <c>content/curses/curses.json</c>'s own prose by <c>CurseEffectsTests</c>.
/// </para>
/// </remarks>
internal static class CurseEffects
{
    /// <summary>One curse's stat move: the stat, and the signed percentage applied to it.</summary>
    /// <param name="Stat">The stat the curse moves.</param>
    /// <param name="PctAdd">
    /// The signed share, as a fraction — <c>-0.08</c> for "-8% DEF". A <c>STAT_ADD_PCT</c> value,
    /// so it composes additively with every other percentage bucket rather than multiplying.
    /// </param>
    internal readonly record struct CurseStatMove(StatId Stat, double PctAdd);

    /// <summary>The stat move a curse makes, or <c>null</c> when it is not a stat curse.</summary>
    /// <param name="curseId">The curse id.</param>
    internal static CurseStatMove? Stat(string? curseId) => curseId switch
    {
        // "-8% DEF"
        "CUR_FRACTURED" => new CurseStatMove(StatId.DEF, -0.08),

        // "-15% Attack Speed"
        "CUR_UNTIMELY" => new CurseStatMove(StatId.ASPD, -0.15),

        // "-40% Healing Received"
        "CUR_FAMISHED" => new CurseStatMove(StatId.HEAL_PCT, -0.40),

        // "-12% Max HP"
        "CUR_BRITTLE_BONES" => new CurseStatMove(StatId.MAX_HP, -0.12),

        // "Shop prices +50%" — a POSITIVE move on a stat where higher is worse for the player, which
        // is why the sign is stated per row rather than derived from the word "curse".
        "CUR_MISERLY" => new CurseStatMove(StatId.SHOP_PRICE_PCT, 0.50),

        // "20% of all Gold gained is lost"
        "CUR_TITHE" => new CurseStatMove(StatId.GOLD_PCT, -0.20),

        _ => null,
    };

    /// <summary>
    /// The Pip penalty a curse applies to every rolled Pip face, or <c>0</c> for a curse that
    /// applies none. `19` Part E's <c>CUR_SLIPPERY</c> is the only row that does.
    /// </summary>
    /// <remarks>
    /// A separate seam from <see cref="Stat"/> because the die is not a stat: `18` §2.1 has no
    /// "movement" stat, and inventing one would put a rule that only the board reads into the block
    /// every combat pass aggregates.
    /// </remarks>
    internal static int PipPenalty(string? curseId) => curseId switch
    {
        "CUR_SLIPPERY" => 1,
        _ => 0,
    };

    /// <summary>The floor a Pip face can be reduced to — `19` Part E's "(minimum 1)".</summary>
    internal const int MinimumPipAfterPenalty = 1;

    /// <summary>Whether this curse is honoured as a board rule rather than as a build effect.</summary>
    internal static bool IsBoardRule(string? curseId) => PipPenalty(curseId) != 0;

    /// <summary>
    /// Why a curse's debuff is not applied, or <c>null</c> when it is (as a stat or as a board rule).
    /// </summary>
    /// <remarks>
    /// Each reason names the specific missing mechanism, not "not implemented": a reason a later
    /// reader can check against the repo is the difference between a declared gap and an
    /// undiscovered one.
    /// </remarks>
    internal static string? UnappliedReason(string curseId)
    {
        ArgumentNullException.ThrowIfNull(curseId);

        if (Stat(curseId) is not null || IsBoardRule(curseId))
        {
            return null;
        }

        return curseId switch
        {
            "CUR_MARKED" =>
                "'Enemies +10% ATK for the rest of the stage' moves the ENEMY's block, and nothing " +
                "run-scoped reaches an enemy: RunBattle composes each enemy from the chapter ladder " +
                "at fight time, with no seam for a run-scoped modifier. It also expires at the stage " +
                "boundary, and no curse carries a duration today.",

            "CUR_HUNTED" =>
                "'Every Elite gains an extra modifier' needs the Elite modifier system, which does " +
                "not exist: enemies.json authors elites as a power multiplier, with no modifier pool " +
                "to draw an extra from. Its paired reward (+1 gear drop per Elite) is equally " +
                "unpayable, which is why CurseRewards does not name it either.",

            "CUR_BLIND" =>
                "'Tile preview reduced to 2' is a CLIENT rule — how many tiles the board screen " +
                "shows ahead. TILE_PREVIEW exists as a stat, but the curse SETS it rather than " +
                "moving it, and nothing in Core reads it; the board screen reads its own constant.",

            _ => "This curse is outside 19 Part E's ten. Nothing here knows what it does.",
        };
    }
}
