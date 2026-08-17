using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The fake's refusals — the states <see cref="InMemoryPlatformInfo"/> will not be put into, and
/// which of the two argument faults each one is.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Not in the shared suite, and the distinction is the point.</b>
/// <see cref="IPlatformInfoPortContractTests"/> states what every implementation of the port means;
/// these knobs exist on this one type and no port member reaches them. What they <em>enforce</em> is
/// the port's clauses, which is why they are worth a test at all: a fake that quietly accepted a
/// blank device model or a language nobody speaks would let a scenario be written against an answer
/// no real adapter can produce, and the scenario would pass forever.
/// </para>
/// <para>
/// 🔒 <b>Each case pins the exception's IDENTITY, not merely that something was thrown</b>
/// (steering S2). A missing argument and a malformed one are different mistakes and get different
/// types here, exactly as they do on <c>ILocalCachePort</c> — and the founding incident behind the
/// shared-suite rule was two implementations disagreeing on precisely that. The parameter name is
/// pinned with it, because an exception naming the wrong argument sends the next reader to the wrong
/// call site.
/// </para>
/// </remarks>
public sealed class InMemoryPlatformInfoGuardTests
{
    /// <summary>
    /// A culture that answers whatever name a case needs it to, without asking the runtime whether
    /// such a language exists.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Necessary, not convenient.</b> Both locale guards have to be driven with names the
    /// runtime refuses, and <c>new CultureInfo(name)</c> cannot produce one on a host in
    /// globalization-invariant mode — it throws <c>CultureNotFoundException</c> before the guard is
    /// reached, so the two cases below would fail for the wrong reason on exactly the hosts this
    /// repository already builds for. Deriving and overriding <see cref="CultureInfo.Name"/> is the
    /// only construction that reaches the guard on every host, and it is also the more honest
    /// subject: the state under test is "a culture object carrying this name", which is what an
    /// implementation translating a host string would hand over.
    /// </remarks>
    private sealed class CultureNamed(string name) : CultureInfo(string.Empty)
    {
        /// <inheritdoc/>
        public override string Name { get; } = name;
    }

    /// <summary>A device model that is neither an absence nor an answer.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_device_model_is_refused(string blank)
    {
        var platform = new InMemoryPlatformInfo();

        Should.Throw<ArgumentException>(() => platform.ReportDeviceModel(blank))
            .ParamName.ShouldBe("model");
    }

    /// <summary>An absent device is accepted, because it is one of the port's two states.</summary>
    /// <remarks>
    /// The negative control on the case above. A guard that refused everything would satisfy the
    /// loud half on its own, and this fake's entire purpose is to be able to reach the absent state.
    /// </remarks>
    [Fact]
    public void An_absent_device_model_is_accepted()
    {
        var platform = new InMemoryPlatformInfo();

        platform.ReportDeviceModel(null);

        platform.DeviceModel.ShouldBeNull();
    }

    /// <summary>A missing operating system is a null argument fault, not a malformed one.</summary>
    [Fact]
    public void A_null_os_version_is_a_null_argument_fault()
    {
        var platform = new InMemoryPlatformInfo();

        Should.Throw<ArgumentNullException>(() => platform.ReportOsVersion(null!))
            .ParamName.ShouldBe("osVersion");
    }

    /// <summary>A blank operating system is a malformed argument, which is the other fault.</summary>
    /// <remarks>
    /// Stated separately from the case above because <see cref="ArgumentNullException"/> derives
    /// from <see cref="ArgumentException"/>: a single case asserting the base type would pass
    /// whichever of the two was thrown, and the two are different mistakes.
    /// </remarks>
    [Fact]
    public void A_blank_os_version_is_a_malformed_argument_fault()
    {
        var platform = new InMemoryPlatformInfo();

        var failure = Should.Throw<ArgumentException>(() => platform.ReportOsVersion("   "));

        failure.ShouldNotBeOfType<ArgumentNullException>(
            "a blank string was passed, not a missing one. Answering the missing-argument fault "
            + "here collapses two different mistakes into one, and a caller reading the type to "
            + "decide what went wrong is told the wrong thing.");
        failure.ParamName.ShouldBe("osVersion");
    }

    /// <summary>A missing build is a null argument fault.</summary>
    [Fact]
    public void A_null_app_version_is_a_null_argument_fault()
    {
        var platform = new InMemoryPlatformInfo();

        Should.Throw<ArgumentNullException>(() => platform.ReportAppVersion(null!))
            .ParamName.ShouldBe("appVersion");
    }

    /// <summary>A blank build is a malformed argument.</summary>
    [Fact]
    public void A_blank_app_version_is_a_malformed_argument_fault()
    {
        var platform = new InMemoryPlatformInfo();

        var failure = Should.Throw<ArgumentException>(() => platform.ReportAppVersion(string.Empty));

        failure.ShouldNotBeOfType<ArgumentNullException>();
        failure.ParamName.ShouldBe("appVersion");
    }

    /// <summary>A missing locale is a null argument fault.</summary>
    [Fact]
    public void A_null_locale_is_a_null_argument_fault()
    {
        var platform = new InMemoryPlatformInfo();

        Should.Throw<ArgumentNullException>(() => platform.ReportLocale(null!))
            .ParamName.ShouldBe("locale");
    }

    /// <summary>
    /// 🔒 A locale spelled the way a host spells one is refused, and refused for <em>that</em>
    /// reason.
    /// </summary>
    /// <remarks>
    /// The message is pinned because this guard and the one below both throw
    /// <see cref="ArgumentException"/> over the same argument, and the type alone cannot say which
    /// of the two rules fired.
    /// <para>
    /// ⚠️ <b>An earlier draft of this remark claimed <c>de_DE</c> is a name the runtime knows, and
    /// that was measured FALSE</b>: <c>GetCultureInfo("de_DE").Name</c> is <c>de_de</c>, so it does
    /// not round-trip, and <c>de_DE</c> is not among the 813 cultures this host enumerates — the
    /// second guard would reject it too. The case still discriminates, because the punctuation guard
    /// runs first and the message is pinned; what was wrong was the stated reason, left over from
    /// the round-trip design this guard replaced.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_host_spelled_locale_is_refused_for_its_punctuation()
    {
        var platform = new InMemoryPlatformInfo();

        Should.Throw<ArgumentException>(() => platform.ReportLocale(new CultureNamed("de_DE")))
            .Message.ShouldContain("punctuation BCP 47 does not use");
    }

    /// <summary>
    /// 🔒 A language nothing speaks is refused, and refused for <em>that</em> reason — even though
    /// the runtime will happily manufacture a culture for it.
    /// </summary>
    /// <remarks>
    /// <c>xx-Zzzz-QQ</c> is well formed, so the runtime builds a custom culture whose name round-
    /// trips exactly. This case is the reason the guard enumerates rather than round-trips, and
    /// pinning the message is what keeps it distinguishable from the punctuation rule above.
    /// </remarks>
    [Fact]
    public void A_language_the_runtime_does_not_know_is_refused_as_unknown()
    {
        var platform = new InMemoryPlatformInfo();

        Should.Throw<ArgumentException>(() => platform.ReportLocale(new CultureNamed("xx-Zzzz-QQ")))
            .Message.ShouldContain("not among the cultures this runtime enumerates");
    }

    /// <summary>
    /// The negative control over both locale guards: a real, <em>named</em>, standard-spelled
    /// culture is accepted, and comes back read-only.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Named, and that word is the whole case.</b> This was written against
    /// <see cref="CultureInfo.InvariantCulture"/>, whose <see cref="CultureInfo.Name"/> is the empty
    /// string — so it proved only that the guards admit the empty name, and a guard that rejected
    /// every real language would have passed it. Combined with the fake's invariant fallback that
    /// was a live hole: tighten the enumeration comparison to ordinal and <c>en-GB</c> starts
    /// throwing while the fallback quietly reports invariant, with every case still green.
    /// <para>
    /// The subject is taken from the host's own enumeration rather than hard-coded, so the case
    /// says the same thing on a host with cultures and skips no assertion on one without.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_named_culture_is_accepted_and_handed_back_read_only()
    {
        var platform = new InMemoryPlatformInfo();
        var named = CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Select(known => known.Name)
            .FirstOrDefault(name => !string.IsNullOrEmpty(name));

        var subject = named is null
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(named);

        platform.ReportLocale((CultureInfo)subject.Clone());

        platform.Locale.Name.ShouldBe(
            subject.Name,
            "the guards must admit a language this runtime actually has. A control driven with the "
            + "invariant culture asserts only that the EMPTY name is admitted, which every guard "
            + "that rejects real languages also satisfies.");
        platform.Locale.IsReadOnly.ShouldBeTrue(
            "a guard that stored the caller's own mutable culture would let the caller keep "
            + "changing what the fake reports after the fact.");
    }

    /// <summary>
    /// 🔒 Both branches of the fake's start locale, which nothing else in the tree can see.
    /// </summary>
    /// <remarks>
    /// The shared suite asserts shapes, and the invariant culture satisfies every shape
    /// <c>en-GB</c> does — so inverting that ternary left all 143 cases green while the fake
    /// silently stopped reporting a language on every host. The expectation is derived from the
    /// same host fact the production code branches on, but independently, so the case still fails
    /// if the branch is inverted.
    /// </remarks>
    [Fact]
    public void The_start_locale_is_the_preferred_language_where_the_host_has_it()
    {
        var hostEnumeratesIt = CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Any(known => known.Name.Equals(
                InMemoryPlatformInfo.PreferredStartLocaleName, StringComparison.OrdinalIgnoreCase));

        InMemoryPlatformInfo.StartLocale.Name.ShouldBe(
            hostEnumeratesIt ? InMemoryPlatformInfo.PreferredStartLocaleName : string.Empty,
            $"this host {(hostEnumeratesIt ? "does" : "does not")} enumerate "
            + $"'{InMemoryPlatformInfo.PreferredStartLocaleName}', so the fake must start on "
            + (hostEnumeratesIt ? "it" : "the invariant culture")
            + ". The fallback exists so the fake can be CONSTRUCTED in globalization-invariant mode; "
            + "it is not a licence to report no language on a host that has them.");

        InMemoryPlatformInfo.StartLocale.IsReadOnly.ShouldBeTrue();
    }
}
