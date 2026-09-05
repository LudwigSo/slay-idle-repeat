using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// <c>RunModifierTotals.ScaleGoldIncome</c> — what a run actually receives once its own Gold-gain
/// modifiers have had their say, and the floor that stops a heavily cursed run paying out negative.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why the figures are named here rather than cross-checked against another caller.</b> The
/// minigame screen's preview and the minigame handler's payout both call this one function, so a
/// test comparing the two agrees with itself whatever the arithmetic does: scale by the wrong sign,
/// divide instead of multiply, or return zero for every row, and both sides move together and the
/// cross-check stays green. That guard proves preview and payout cannot DRIFT APART; it says
/// nothing about whether either is right. So each row below states the arrived figure outright.
/// </para>
/// <para>
/// The modifier used is <c>CUR_TITHE</c>, the one curse that takes a fifth of all Gold gained, because
/// its magnitude is a row of <c>CurseEffects</c>, code cross-checked against the curse document by
/// its own suite, rather than a tuning number a balance pass could move underneath these rows.
/// </para>
/// </remarks>
public sealed class RunModifierTotalsTests
{
    /// <summary>The one curse that moves <c>GOLD_PCT</c>, authored at −20% per copy.</summary>
    private const string GoldCurse = "CUR_TITHE";

    /// <summary>The whole percent one copy of <see cref="GoldCurse"/> takes off Gold income.</summary>
    /// <remarks>Held as an integer so a failure message never renders through the ambient culture.</remarks>
    private const int GoldCursePercent = -20;

    private static readonly IReadOnlyList<string> NoShrineBuffs = Array.Empty<string>();

    /// <summary>
    /// 🔒 Income arrives multiplied by <c>1 + pct</c>, and the arrived figure is stated.
    /// </summary>
    /// <remarks>
    /// 🔴 Four rows spanning a zero modifier and three sizes of negative one. A single row could be
    /// satisfied by a function that scaled the wrong way and happened to agree; the ladder cannot.
    /// A run scaled by <c>1 − pct</c> receives MORE for being cursed, and one that divided by
    /// <c>1 + pct</c> receives more still — both are the same tuning read backwards, and both put a
    /// number on the minigame screen that the payout will not honour.
    /// </remarks>
    [Theory]
    [InlineData(0, 1000L, 1000L)]
    [InlineData(1, 1000L, 800L)]
    [InlineData(2, 1000L, 600L)]
    [InlineData(4, 1000L, 200L)]
    [InlineData(1, 375L, 300L)]
    public void Gold_income_arrives_scaled_by_the_runs_own_Gold_modifiers(
        int curseCount, long amount, long expected) =>
        Scaled(curseCount, amount).ShouldBe(
            expected,
            "a run carrying " + curseCount + " × " + GoldCurse + " is at " +
            (curseCount * GoldCursePercent) + "% Gold, so " + amount + " arriving should land as " +
            expected + ". Anything else is the modifier applied with the wrong sign or the wrong " +
            "operator — and the screen previewing this reward reads the same function, so it would " +
            "show the same wrong number rather than disagree with it.");

    /// <summary>
    /// 🔒 A run cursed to −100% or past it receives nothing, never negative Gold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 The floor is the only branch in this function, and without a row that reaches it the
    /// comparison could be inverted, dropped, or forced either way with every other case still
    /// green: every reward this repository authors is positive, so a clamp that never fires and a
    /// clamp that is wrong look identical from the paying side.
    /// </para>
    /// <para>
    /// Five copies is exactly −100% and six is past it. Both are hypothetical stacks — nothing
    /// authored today puts five Tithes on one run — which is precisely why the behaviour has to be
    /// pinned somewhere: it is stated as an intention in the function's own remarks and is
    /// otherwise unreachable from any shipped table.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(5, 1000L)]
    [InlineData(6, 1000L)]
    [InlineData(10, 250L)]
    public void A_run_cursed_to_minus_one_hundred_percent_or_past_it_receives_nothing(
        int curseCount, long amount) =>
        Scaled(curseCount, amount).ShouldBe(
            0L,
            "a run at " + (curseCount * GoldCursePercent) + "% Gold was paid something other " +
            "than nothing for " + amount + " arriving. Below zero the source would be TAKING Gold " +
            "it never gave, which no authored modifier is meant to do.");

    /// <summary>What the run receives for <paramref name="amount"/> under so many copies of the curse.</summary>
    private static long Scaled(int curseCount, long amount) =>
        RunModifierTotals.ScaleGoldIncome(
            NoShrineBuffs, Curses(curseCount), CurrenciesDocuments.Shipped, amount);

    /// <summary>
    /// A curse list holding <paramref name="count"/> copies of the Gold curse.
    /// </summary>
    /// <remarks>
    /// Repeated rather than assembled out of different ids because <c>CUR_TITHE</c> is the only
    /// curse in the game that moves <c>GOLD_PCT</c> at all, so it is the only way to reach a total
    /// past one copy's −20%.
    /// </remarks>
    private static IReadOnlyList<string> Curses(int count) =>
        Enumerable.Repeat(GoldCurse, count).ToArray();
}
