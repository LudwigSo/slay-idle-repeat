using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §3 — the entitlement is <em>"a read-only value … <c>{ HasPlus, ExpiresAtUtc }</c>"</em>,
/// and <em>"the domain may read <c>HasPlus</c> only to resolve ad-reward auto-grant caps — never to
/// alter a stat, a rate or a drop."</em>
/// </summary>
/// <remarks>
/// The "never alters a rate" half is enforced structurally by the architecture suite
/// (<c>IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation</c> and
/// <c>No_entitlement_branch_outside_a_composition_root</c>). What is pinned here is the shape those
/// rules key on, and — see
/// <see cref="Entitlements_declares_no_equality_because_an_equality_would_branch_on_HasPlus"/> —
/// the one non-obvious consequence of them.
/// </remarks>
public sealed class EntitlementsTests
{
    /// <summary>
    /// 🔒 `30` §3 — exactly <c>{ bool HasPlus, DateTimeOffset? ExpiresAtUtc }</c>. The expiry rides
    /// on the value rather than being recomputed because `12` §2.2 makes Plus expiry a
    /// time-dependent rule, and a rule that recomputed it would need a clock.
    /// </summary>
    [Fact]
    public void Entitlements_carries_exactly_HasPlus_and_a_nullable_ExpiresAtUtc()
    {
        var actual = typeof(Entitlements)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => $"{p.Name}:{p.ParameterType.FullName}");

        actual.ShouldBe(new[]
        {
            $"hasPlus:{typeof(bool).FullName}",
            $"expiresAtUtc:{typeof(DateTimeOffset?).FullName}",
        });

        typeof(Entitlements).GetProperty(nameof(Entitlements.HasPlus))!.PropertyType.ShouldBe(typeof(bool));
        typeof(Entitlements).GetProperty(nameof(Entitlements.ExpiresAtUtc))!.PropertyType.ShouldBe(typeof(DateTimeOffset?));
    }

    /// <summary>`30` §3 — read-only to the domain: sealed, no setter, no public field.</summary>
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
    /// 🔒 The whole public surface, pinned. <c>Entitlements</c> is a plain sealed class and
    /// deliberately <b>not</b> a <c>record</c>: a record's synthesized
    /// <c>Equals(Entitlements?)</c> loads <c>&lt;HasPlus&gt;k__BackingField</c> and then branches,
    /// which is precisely the shape
    /// <c>IsolationTests.No_entitlement_branch_outside_a_composition_root</c>'s IL backstop is
    /// written to catch ("reads the flag AND contains a conditional branch"). Making this a record
    /// turns the architecture suite red, and the only ways to make it green again are to widen that
    /// rule's single licensed exemption — `30` §3 licenses exactly one, the ad-grant cap rule — or
    /// to stop the domain reading the entitlement at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ Nothing in the domain compares two <c>Entitlements</c>, so reference equality is
    /// sufficient. If something ever needs to, the comparison belongs at the composition root,
    /// where the branch is allowed — not on this type. This test is what stops "let's make it a
    /// record for consistency" from reopening the question silently.
    /// </remarks>
    [Fact]
    public void Entitlements_declares_no_equality_because_an_equality_would_branch_on_HasPlus()
    {
        // Static as well as instance: a `public static Entitlements None` convenience would be a
        // fifth member this rule is meant to see, and an instance-only filter would miss it.
        var declared = typeof(Entitlements)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        declared.ShouldBe(
            new[] { ".ctor", "ExpiresAtUtc", "HasPlus", "get_ExpiresAtUtc", "get_HasPlus" },
            "Entitlements declares two getters and a constructor and nothing else. An Equals, a " +
            "Deconstruct or a record's synthesized equality all read HasPlus and branch, which " +
            "No_entitlement_branch_outside_a_composition_root forbids inside Core (12 §3.2, 23 §7.2).");
    }

    /// <summary>`30` §3 — the value keeps what the composition root resolved.</summary>
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
    /// `30` §3 / `12` §2.2 — <c>ExpiresAtUtc</c> may be absent, and an absent expiry is not
    /// coerced to anything. Whether a lapsed subscription still reports <c>HasPlus</c> is the
    /// composition root's answer to give against its own <c>NowUtc</c>; this type stores what it
    /// was told and re-derives nothing.
    /// </summary>
    [Fact]
    public void An_absent_expiry_stays_absent()
    {
        new Entitlements(hasPlus: true, expiresAtUtc: null).ExpiresAtUtc.ShouldBeNull();
    }
}
