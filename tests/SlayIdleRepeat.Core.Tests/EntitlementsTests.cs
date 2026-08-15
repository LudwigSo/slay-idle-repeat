using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// <c>Entitlements</c> is a read-only value — <c>{ HasPlus, ExpiresAtUtc }</c> — and the domain may
/// read <c>HasPlus</c> only to resolve ad-reward auto-grant caps, never to alter a stat, rate or drop.
/// That "never alters a rate" half is enforced structurally by the architecture suite; what is pinned
/// here is the shape those rules key on.
/// </summary>
public sealed class EntitlementsTests
{
    /// <summary>Expiry rides on the value rather than being recomputed, since a recompute would need a clock.</summary>
    [Fact]
    public void Entitlements_carries_exactly_HasPlus_and_a_nullable_ExpiresAtUtc()
    {
        var actual = typeof(Entitlements)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => $"{p.Name}:{p.ParameterType.FullName}");

        actual.ShouldBe(
            new[]
            {
                $"hasPlus:{typeof(bool).FullName}",
                $"expiresAtUtc:{typeof(DateTimeOffset?).FullName}",
            },
            Case.Sensitive);

        typeof(Entitlements).GetProperty(nameof(Entitlements.HasPlus))!.PropertyType.ShouldBe(typeof(bool));
        typeof(Entitlements).GetProperty(nameof(Entitlements.ExpiresAtUtc))!.PropertyType.ShouldBe(typeof(DateTimeOffset?));
    }

    [Fact]
    public void Entitlements_is_sealed_and_read_only()
    {
        typeof(Entitlements).IsSealed.ShouldBeTrue();

        typeof(Entitlements)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ShouldBeEmpty();

        typeof(Entitlements)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name)
            .ShouldBeEmpty(
                "a hand-rolled public field is the only way a member here could be writable while " +
                "the property check above stayed green. A tripwire, not noise.");
    }

    /// <summary>
    /// <c>Entitlements</c> is deliberately not a <c>record</c>: a record's synthesized <c>Equals</c>
    /// loads <c>HasPlus</c> and branches on it, which the architecture suite forbids inside Core.
    /// </summary>
    [Fact]
    public void Entitlements_declares_no_equality_because_an_equality_would_branch_on_HasPlus()
    {
        var declared = typeof(Entitlements)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        declared.ShouldBe(
            new[] { ".ctor", "ExpiresAtUtc", "HasPlus", "get_ExpiresAtUtc", "get_HasPlus" },
            Case.Sensitive,
            "Entitlements declares two getters and a constructor and nothing else. An Equals, a " +
            "Deconstruct or a record's synthesized equality all read HasPlus and branch, which " +
            "No_entitlement_branch_outside_a_composition_root forbids inside Core (12 §3.2, 23 §7.2).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entitlements_carries_the_values_the_composition_root_resolved(bool hasPlus)
    {
        var expiry = GameContexts.FixedInstant;

        var entitlements = new Entitlements(hasPlus, expiry);

        entitlements.HasPlus.ShouldBe(hasPlus);
        entitlements.ExpiresAtUtc.ShouldBe(expiry);
    }

    /// <summary>
    /// An absent expiry is not coerced to anything; whether a lapsed subscription still reports
    /// <c>HasPlus</c> is the composition root's call, not this type's.
    /// </summary>
    [Fact]
    public void An_absent_expiry_stays_absent()
    {
        new Entitlements(hasPlus: true, expiresAtUtc: null).ExpiresAtUtc.ShouldBeNull();
    }
}
