using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary><c>GameContext</c>'s construction guards: everything ambient a rule may know arrives whole.</summary>
public sealed class GameContextTests
{
    private const ulong AnySeed = 0x0123456789ABCDEFUL;

    [Fact]
    public void GameContext_refuses_a_null_ContentSnapshot()
    {
        Should.Throw<ArgumentNullException>(() => new GameContext(
                GameContexts.FixedInstant, null, null!, GameContexts.WithoutPlus, GameContexts.NoKillSwitchThrown))
            .ParamName.ShouldBe("Content");
    }

    [Fact]
    public void GameContext_refuses_a_null_Entitlements()
    {
        Should.Throw<ArgumentNullException>(() => new GameContext(
                GameContexts.FixedInstant, null, GameContexts.EmptyContent, null!, GameContexts.NoKillSwitchThrown))
            .ParamName.ShouldBe("Entitlements");
    }

    /// <summary>A null here would read identically to "no kill switches thrown" — the difference between a live PvP season and a dead one.</summary>
    [Fact]
    public void GameContext_refuses_null_Flags()
    {
        Should.Throw<ArgumentNullException>(() => new GameContext(
                GameContexts.FixedInstant, null, GameContexts.EmptyContent, GameContexts.WithoutPlus, null!))
            .ParamName.ShouldBe("Flags");
    }

    /// <summary>
    /// Guards must survive <c>with</c> too: property initializers run only in the primary
    /// constructor, so a guard written as one would be silently absent on the copy path.
    /// </summary>
    [Theory]
    [MemberData(nameof(NullReferenceMembers))]
    public void A_with_expression_cannot_punch_a_hole_in_a_context(string member, Func<GameContext, GameContext> mutate)
    {
        Should.Throw<ArgumentNullException>(() => mutate(GameContexts.WithSeed(null)))
            .ParamName.ShouldBe(member);
    }

    public static TheoryData<string, Func<GameContext, GameContext>> NullReferenceMembers() => new()
    {
        { nameof(GameContext.Content), context => context with { Content = null! } },
        { nameof(GameContext.Entitlements), context => context with { Entitlements = null! } },
        { nameof(GameContext.Flags), context => context with { Flags = null! } },
    };

    /// <summary>
    /// Daily-reset/event-window logic reads wall-clock components off this value, and
    /// <c>06:00 +02:00</c> and <c>06:00 +00:00</c> are different game days. Refused rather than
    /// normalised, so a miswired clock adapter is named instead of silently hidden.
    /// </summary>
    [Fact]
    public void GameContext_refuses_an_instant_that_is_not_stated_in_UTC()
    {
        var localMorning = new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.FromHours(2));

        Should.Throw<ArgumentException>(() => new GameContext(
                localMorning, null, GameContexts.EmptyContent, GameContexts.WithoutPlus, GameContexts.NoKillSwitchThrown))
            .ParamName.ShouldBe(nameof(GameContext.NowUtc));

        Should.Throw<ArgumentException>(() => GameContexts.WithSeed(null) with { NowUtc = localMorning })
            .ParamName.ShouldBe(nameof(GameContext.NowUtc));
    }

    /// <summary>
    /// A record's synthesized <c>PrintMembers</c> appends <c>NowUtc</c> through
    /// <c>StringBuilder.Append(object)</c>, which formats with the ambient culture — boxing hides
    /// that from the architecture suite's culture rule, so it is pinned here instead.
    /// </summary>
    [Fact]
    public void ToString_renders_identically_under_any_culture()
    {
        var german = new CultureInfo("de-DE");

        german.DateTimeFormat.ShortDatePattern.ShouldNotBe(
            CultureInfo.InvariantCulture.DateTimeFormat.ShortDatePattern,
            "under globalization-invariant mode new CultureInfo(\"de-DE\") silently returns the " +
            "invariant culture, and the comparison below would then hold over nothing.");

        var context = GameContexts.WithSeed(AnySeed);

        Render(context, german).ShouldBe(Render(context, CultureInfo.InvariantCulture));

        Render(context, CultureInfo.InvariantCulture)
            .ShouldContain("NowUtc = 2026-08-12T05:00:00.0000000+00:00", Case.Sensitive);
    }

    /// <summary>Renders a context under a given ambient culture, restoring the caller's afterwards.</summary>
    private static string Render(GameContext context, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return context.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
