using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// `12` §4.3 — the per-run ad-use counters: <c>placement id → uses</c>, for the thirteen in-run
/// placements whose <c>capWindow</c> is <c>RUN</c>.
/// </summary>
/// <remarks>
/// The mechanism is <c>Player</c>'s <c>key → long</c> counter, reused rather than reinvented.
/// <para>
/// 🔒 The one deviation is the absence of a period anchor and a reset mutator: the run <b>is</b> the
/// period, so there is nothing to reset and a reset mutator would be a way to hand a player their
/// seventeen impressions twice.
/// </para>
/// <para>
/// ⚠️ The caps themselves are not enforced here — <c>CAP_REACHED</c> belongs to the handler that reads
/// <c>ads.json</c>, and `30` §11.5 keeps that computation out of the aggregate, which holds the count
/// and refuses a count that is not a count.
/// </para>
/// </remarks>
public sealed class RunAdUseTests
{
    private static Run WithAdUses(params (string Placement, long Uses)[] uses) =>
        Run.Rehydrate(RunSnapshots.With(adUses: RunSnapshots.AdUses(uses))).Value;

    /// <summary>A placement comes into existence on its first use; nothing declares it in advance.</summary>
    [Fact]
    public void A_placement_is_registered_by_its_first_use()
    {
        var run = WithAdUses();

        run.AdUseCount("AD_REROLL_DICE").ShouldBe(0);

        run.CountAdUse("AD_REROLL_DICE", 1);

        run.AdUseCount("AD_REROLL_DICE").ShouldBe(1);
        run.AdUses["AD_REROLL_DICE"].ShouldBe(1);
    }

    /// <summary>A second use adds to the first rather than replacing it.</summary>
    [Fact]
    public void A_further_use_adds_to_the_count()
    {
        var run = WithAdUses(("AD_DOUBLE_CHEST", 1));

        run.CountAdUse("AD_DOUBLE_CHEST", 1);

        run.AdUseCount("AD_DOUBLE_CHEST").ShouldBe(2);
    }

    /// <summary>
    /// 🔒 <c>AD_REVIVE</c> is what carries `02` §6's once-per-run revive: it is one of the thirteen
    /// in-run placements, so there is no separate <c>RevivesUsed</c> field to disagree with it.
    /// </summary>
    /// <remarks>
    /// ⚠️ The cap is <b>not</b> asserted — see the class remarks. What is asserted is the absence of
    /// a second source of truth, which is the decision this milestone actually made.
    /// </remarks>
    [Fact]
    public void The_once_per_run_revive_is_counted_as_an_ad_placement_and_not_as_a_second_field()
    {
        var run = WithAdUses();

        run.CountAdUse("AD_REVIVE", 1);

        run.AdUseCount("AD_REVIVE").ShouldBe(1);

        var members = typeof(Run)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        members.ShouldContain(
            nameof(Run.CountAdUse),
            "the floor (steering S3): without it this reflection could be looking at the wrong " +
            "type — or at nothing — and the emptiness below would report success forever.");

        members.Where(name => name.Contains("Reviv", StringComparison.OrdinalIgnoreCase))
               .ShouldBeEmpty(
                   "02 §6's revive is one of 12 §4.3's thirteen in-run placements (AD_REVIVE, cap 1). A " +
                   "RevivesUsed field beside the counter would be a second source of truth for one count.");
    }

    /// <summary>A blank placement key is refused: a key names the placement it counts.</summary>
    /// <remarks>
    /// ⚠️ The <b>parameter</b> is asserted, not just the exception type.
    /// <see cref="ArgumentOutOfRangeException"/> derives from <see cref="ArgumentException"/>, so
    /// <c>Should.Throw&lt;ArgumentException&gt;</c> is also satisfied by the amount guard — and by
    /// any other guard either method grows. The parameter name says which one fired (steering S2).
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_blank_placement_key_is_refused(string placementId)
    {
        var run = WithAdUses();

        Should.Throw<ArgumentException>(() => run.CountAdUse(placementId, 1))
              .ParamName.ShouldBe("placementId");

        Should.Throw<ArgumentException>(() => run.AdUseCount(placementId))
              .ParamName.ShouldBe("placementId");
    }

    /// <summary>A negative amount is refused: a use counter counts, it does not settle back down.</summary>
    [Fact]
    public void A_negative_amount_is_refused()
    {
        var run = WithAdUses(("AD_SHOP_REFRESH", 2));

        var act = () => run.CountAdUse("AD_SHOP_REFRESH", -1);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .ParamName.ShouldBe(
                  "amount",
                  "the other guard on this method throws an ArgumentException over the key, and " +
                  "ArgumentOutOfRangeException is one of those — so the type alone does not say " +
                  "which refusal this is (steering S2).");

        run.AdUseCount("AD_SHOP_REFRESH").ShouldBe(2, "a refused advance changes nothing");
    }

    /// <summary>An overflowing advance is refused rather than wrapping to a negative count.</summary>
    [Fact]
    public void An_overflowing_advance_is_refused()
    {
        var run = WithAdUses(("AD_DOUBLE_CHEST", long.MaxValue));

        var act = () => run.CountAdUse("AD_DOUBLE_CHEST", 1);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*overflows a 64-bit count*");

        run.AdUseCount("AD_DOUBLE_CHEST").ShouldBe(long.MaxValue);
    }

    /// <summary>
    /// ⚠️ The exposed map is a <b>live view</b>, unlike <c>RngStreamPositions</c>: the counts are
    /// mutated in place, so a caller holding the reference across a <c>CountAdUse</c> sees the new
    /// value. Read it, do not hold it.
    /// </summary>
    [Fact]
    public void The_exposed_map_is_a_live_view_and_cannot_be_mutated_through_its_reference()
    {
        var run = WithAdUses(("AD_CAMPFIRE_HEAL", 1));
        var exposed = run.AdUses;

        Should.Throw<NotSupportedException>(
            () => ((IDictionary<string, long>)exposed)["AD_CAMPFIRE_HEAL"] = 999);

        run.CountAdUse("AD_CAMPFIRE_HEAL", 1);

        exposed["AD_CAMPFIRE_HEAL"].ShouldBe(
            2,
            "the ad counters are mutated in place, so the view a caller took earlier reflects the " +
            "increment — the opposite of RngStreamPositions, which is replaced wholesale.");
    }

    /// <summary>
    /// 🔒 There is <b>no reset mutator</b>, and there is not going to be one.
    /// </summary>
    /// <remarks>
    /// `12` §4.3 makes the in-run caps per run, so the run <em>is</em> the period: there is no
    /// boundary to cross and nothing to clear. A <c>ResetAdUses</c> would be a way to hand a player
    /// their seventeen in-run impressions twice inside one run. Stated by reflection over the type
    /// rather than by reading the source, and floored on a member the type does have so the
    /// assertion cannot pass by looking at nothing (steering S3).
    /// </remarks>
    [Fact]
    public void There_is_no_reset_mutator_because_the_run_is_the_period()
    {
        var members = typeof(Run)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        members.ShouldContain(
            nameof(Run.CountAdUse),
            "if this is absent the reflection is looking at the wrong type and the assertion below " +
            "is quantifying over nothing.");

        members.Where(name => name.StartsWith("Reset", StringComparison.Ordinal))
               .ShouldBeEmpty(
                   "Player has ResetDailyCounters/ResetWeeklyCounters because 30 §2.3 gives its " +
                   "counters a period boundary. 12 §4.3's in-run caps have none — the run is the " +
                   "period — so a reset here would only ever be a second helping of the cap.");

        members.Where(name => name.Contains("PeriodStart", StringComparison.Ordinal))
               .ShouldBeEmpty("and there is no period anchor either, for the same reason.");
    }
}
