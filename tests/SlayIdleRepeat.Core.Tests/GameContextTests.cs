using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §3 — <em>"everything ambient, as data … This is the part that most often gets missed,
/// and it is where 'playable in memory' is actually won or lost."</em>
/// </summary>
/// <remarks>
/// The rules here are structural: they pin the five members, their order and their declared types,
/// because <c>GameContext</c>'s whole job is to be the closed list of what a rule may know about
/// the outside world. A sixth member, a reordering, or a <c>ulong</c> where `30` §3 wrote
/// <c>ulong?</c> are each a change to that contract, and none of them would fail anything else.
/// </remarks>
public sealed class GameContextTests
{
    /// <summary>
    /// The five members of `30` §3's declaration, in the order it declares them. Written out as
    /// (name, type) pairs rather than read off the type under test, which would prove nothing.
    /// </summary>
    private static readonly (string Name, Type Type)[] ThirtySectionThree =
    {
        ("NowUtc", typeof(DateTimeOffset)),
        ("CommandSeed", typeof(ulong?)),
        ("Content", typeof(ContentSnapshot)),
        ("Entitlements", typeof(Entitlements)),
        ("Flags", typeof(FeatureFlags)),
    };

    /// <summary>
    /// 🔒 `30` §3 — <c>GameContext</c> carries exactly those five members, in that order, with
    /// those declared types. Order is part of the contract because the members are positional.
    /// </summary>
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
            "GameContext is the closed list of what a rule may know about the outside world (30 §3). " +
            "Adding, removing, retyping or reordering a member changes that contract — and 30 §3's " +
            "generalising rule is that anything a rule needs from the outside world is an argument, " +
            "not a call, so a new member is a decision, never a convenience.");
    }

    /// <summary>
    /// 🔒 `14` §8.1 / `30` §3 — <c>CommandSeed</c> is <c>ulong?</c>, and the nullability is
    /// load-bearing: it is <b>null on every run command</b>. In-run draws come from the
    /// <c>Run</c> aggregate's committed <c>runSeed</c> and its persisted per-stream counters —
    /// state, not ambience — so a non-nullable <c>ulong</c> here would force the host to invent a
    /// seed for the 19 run commands that must not have one.
    /// </summary>
    /// <remarks>
    /// The pairing rule itself — which commands carry a seed — is pinned by
    /// <see cref="CommandSeedPinTests"/>; this is the half of it that can be asserted before
    /// M1-02 declares a single command type.
    /// </remarks>
    [Fact]
    public void The_CommandSeed_is_nullable_because_run_commands_carry_none()
    {
        typeof(GameContext).GetProperty(nameof(GameContext.CommandSeed))!.PropertyType.ShouldBe(
            typeof(ulong?),
            "CommandSeed is null on run commands (14 §8.1, 30 §3). A non-nullable ulong would make " +
            "'no seed' unrepresentable and force the composition root to invent entropy for the 19 " +
            "run commands — the one thing the domain's determinism rule forbids.");
    }

    /// <summary>
    /// `30` §3 / P4 — the context is a sealed, immutable record. Anything ambient is data the
    /// composition root handed in; nothing in the domain may write to it mid-rule.
    /// </summary>
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
            .ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `30` §3 — there is no <c>GameContext.Now()</c>. Time enters the domain because the
    /// composition root called <c>IClockPort</c> and put the answer here; a static factory on this
    /// type is the exact shape of the shortcut that would put the clock call back inside `Core`.
    /// </summary>
    /// <remarks>
    /// The architecture suite bans <c>DateTimeOffset.UtcNow</c> and <c>IClockPort</c> in `Core`
    /// independently. This rule bans the <em>place</em> such a call would live, which is what makes
    /// the temptation a compile-time-visible design decision rather than a diff nobody reads.
    /// </remarks>
    [Fact]
    public void GameContext_offers_no_static_factory_that_could_reach_for_a_clock()
    {
        var factories = typeof(GameContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name);

        factories.ShouldBeEmpty(
            "a convenience factory on GameContext is where a clock call comes back into Core " +
            "(30 §3). The composition root calls IClockPort and passes the value.");
    }

    /// <summary>
    /// `30` §3 — a context with no content is not a context. Failing at construction names the
    /// argument; failing later names a rule that did nothing wrong.
    /// </summary>
    [Fact]
    public void GameContext_refuses_a_null_ContentSnapshot()
    {
        Should.Throw<ArgumentNullException>(() => new GameContext(
                GameContexts.FixedInstant, null, null!, GameContexts.WithoutPlus, GameContexts.NoKillSwitchThrown))
            .ParamName.ShouldBe("Content");
    }

    /// <summary>`30` §3 — the entitlement is a value the host resolved, never an absence.</summary>
    [Fact]
    public void GameContext_refuses_a_null_Entitlements()
    {
        Should.Throw<ArgumentNullException>(() => new GameContext(
                GameContexts.FixedInstant, null, GameContexts.EmptyContent, null!, GameContexts.NoKillSwitchThrown))
            .ParamName.ShouldBe("Entitlements");
    }

    /// <summary>
    /// `30` §3 / `14` §14 — the flags are resolved at the composition root into a plain record.
    /// A null here would mean "no kill switches resolved", which reads identically to "no kill
    /// switches thrown" and is the difference between a live PvP season and a dead one.
    /// </summary>
    [Fact]
    public void GameContext_refuses_null_Flags()
    {
        Should.Throw<ArgumentNullException>(() => new GameContext(
                GameContexts.FixedInstant, null, GameContexts.EmptyContent, GameContexts.WithoutPlus, null!))
            .ParamName.ShouldBe("Flags");
    }

    /// <summary>`30` §3 — a well-formed context keeps every value it was handed.</summary>
    [Fact]
    public void GameContext_carries_the_values_it_was_handed()
    {
        var context = new GameContext(
            GameContexts.FixedInstant,
            0x0123456789ABCDEFUL,
            GameContexts.EmptyContent,
            GameContexts.WithoutPlus,
            GameContexts.NoKillSwitchThrown);

        context.NowUtc.ShouldBe(GameContexts.FixedInstant);
        context.CommandSeed.ShouldBe(0x0123456789ABCDEFUL);
        context.Content.ShouldBeSameAs(GameContexts.EmptyContent);
        context.Entitlements.ShouldBeSameAs(GameContexts.WithoutPlus);
        context.Flags.ShouldBeSameAs(GameContexts.NoKillSwitchThrown);
    }

    /// <summary>The single public instance constructor — a sealed record's copy constructor is private.</summary>
    private static ConstructorInfo PrimaryConstructor() =>
        typeof(GameContext).GetConstructors().ShouldHaveSingleItem();

    /// <summary>An <c>init</c> accessor is a construction-time setter, not a mutation surface.</summary>
    internal static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
