using System.Globalization;
using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// <c>GameContext</c> is the closed list of everything ambient a rule may know about the outside
/// world — these tests pin its five members, their order and their declared types.
/// </summary>
public sealed class GameContextTests
{
    private static readonly (string Name, Type Type)[] ThirtySectionThree =
    {
        ("NowUtc", typeof(DateTimeOffset)),
        ("CommandSeed", typeof(ulong?)),
        ("Content", typeof(ContentSnapshot)),
        ("Entitlements", typeof(Entitlements)),
        ("Flags", typeof(FeatureFlags)),
    };

    /// <summary>Order is part of the contract because the members are positional.</summary>
    [Fact]
    public void GameContext_carries_exactly_the_five_members_of_30_section_3_in_order()
    {
        var actual = PrimaryConstructor()
            .GetParameters()
            .Select(p => $"{p.Name}:{p.ParameterType.FullName}")
            .ToArray();

        var expected = ThirtySectionThree
            .Select(m => $"{m.Name}:{m.Type.FullName}")
            .ToArray();

        actual.ShouldBe(
            expected,
            Case.Sensitive,
            "GameContext is the closed list of what a rule may know about the outside world (30 §3). " +
            "Adding, removing, retyping or reordering a member changes that contract — and 30 §3's " +
            "generalising rule is that anything a rule needs from the outside world is an argument, " +
            "not a call, so a new member is a decision, never a convenience.");
    }

    /// <summary>
    /// <c>CommandSeed</c> is <c>ulong?</c> and the nullability is load-bearing: a non-nullable
    /// <c>ulong</c> would force the host to invent a seed for run commands that must not have one.
    /// </summary>
    [Fact]
    public void The_CommandSeed_is_nullable_because_run_commands_carry_none()
    {
        typeof(GameContext).GetProperty(nameof(GameContext.CommandSeed))!.PropertyType.ShouldBe(
            typeof(ulong?),
            "CommandSeed is null on run commands (14 §8.1, 30 §3). A non-nullable ulong would make " +
            "'no seed' unrepresentable and force the composition root to invent entropy for the 19 " +
            "run commands — the one thing the domain's determinism rule forbids.");
    }

    [Fact]
    public void GameContext_is_sealed_and_has_no_writable_member()
    {
        typeof(GameContext).IsSealed.ShouldBeTrue();

        var writable = typeof(GameContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(p => p.Name);

        writable.ShouldBeEmpty();

        typeof(GameContext)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name)
            .ShouldBeEmpty(
                "a hand-rolled public field is the one way a member of this record could be " +
                "writable while every property check above stayed green. Auto-properties never " +
                "produce one, which is why this reads as trivially true — it is a tripwire, not noise.");
    }

    /// <summary>Value equality is what lets a replay compare the context a command was handed against the one recorded.</summary>
    [Fact]
    public void Two_contexts_built_from_the_same_values_are_equal()
    {
        GameContexts.WithSeed(AnySeed).ShouldBe(GameContexts.WithSeed(AnySeed));
        GameContexts.WithSeed(AnySeed).ShouldNotBe(GameContexts.WithSeed(null));
    }

    /// <summary>
    /// There is no <c>GameContext.Now()</c>: a static factory here is the shortcut that would put a
    /// clock call back inside Core. <c>GetMembers</c> rather than <c>GetMethods</c>, since a property
    /// getter is invisible to a method-only filter and would let one through unnoticed.
    /// </summary>
    [Fact]
    public void GameContext_offers_no_static_factory_that_could_reach_for_a_clock()
    {
        var factories = typeof(GameContext)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is not ("op_Equality" or "op_Inequality"))
            .Select(m => $"{m.MemberType} {m.Name}");

        factories.ShouldBeEmpty(
            "a convenience factory on GameContext is where a clock call comes back into Core " +
            "(30 §3). The composition root calls IClockPort and passes the value.");
    }

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

    /// <summary>The three reference members, each nulled through a <c>with</c> expression.</summary>
    public static TheoryData<string, Func<GameContext, GameContext>> NullReferenceMembers() => new()
    {
        { nameof(GameContext.Content), context => context with { Content = null! } },
        { nameof(GameContext.Entitlements), context => context with { Entitlements = null! } },
        { nameof(GameContext.Flags), context => context with { Flags = null! } },
    };

    /// <summary>A <c>with</c> that changes one member keeps the rest and stays valid.</summary>
    [Fact]
    public void A_with_expression_that_advances_the_clock_keeps_everything_else()
    {
        var context = GameContexts.WithSeed(null);

        var tomorrow = context with { NowUtc = context.NowUtc.AddDays(1) };

        tomorrow.NowUtc.ShouldBe(GameContexts.FixedInstant.AddDays(1));
        tomorrow.Content.ShouldBeSameAs(context.Content);
        tomorrow.Entitlements.ShouldBeSameAs(context.Entitlements);
        tomorrow.Flags.ShouldBeSameAs(context.Flags);
    }

    /// <summary>
    /// <c>DateTimeOffset</c> keeps its offset, and daily-reset/event-window logic reads wall-clock
    /// components off this value — <c>06:00 +02:00</c> and <c>06:00 +00:00</c> are different game
    /// days despite naming instants two hours apart. Refused rather than normalised, so a miswired
    /// clock adapter is named instead of silently hidden.
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
            "this assertion is only meaningful if the runtime actually has a German culture. Under " +
            "globalization-invariant mode new CultureInfo(\"de-DE\") silently returns the invariant " +
            "culture, and the comparison below would then hold over nothing.");

        var context = GameContexts.WithSeed(AnySeed);

        Render(context, german).ShouldBe(Render(context, CultureInfo.InvariantCulture));

        Render(context, CultureInfo.InvariantCulture)
            .ShouldContain("NowUtc = 2026-08-12T05:00:00.0000000+00:00", Case.Sensitive);
    }

    [Fact]
    public void GameContext_carries_the_values_it_was_handed()
    {
        var context = new GameContext(
            GameContexts.FixedInstant,
            AnySeed,
            GameContexts.EmptyContent,
            GameContexts.WithoutPlus,
            GameContexts.NoKillSwitchThrown);

        context.NowUtc.ShouldBe(GameContexts.FixedInstant);
        context.CommandSeed.ShouldBe(AnySeed);
        context.Content.ShouldBeSameAs(GameContexts.EmptyContent);
        context.Entitlements.ShouldBeSameAs(GameContexts.WithoutPlus);
        context.Flags.ShouldBeSameAs(GameContexts.NoKillSwitchThrown);
    }

    private const ulong AnySeed = 0x0123456789ABCDEFUL;

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

    /// <summary>The single public instance constructor — a sealed record's copy constructor is private.</summary>
    private static ConstructorInfo PrimaryConstructor() =>
        typeof(GameContext).GetConstructors().ShouldHaveSingleItem();

    /// <summary>An <c>init</c> accessor is a construction-time setter, not a mutation surface.</summary>
    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
