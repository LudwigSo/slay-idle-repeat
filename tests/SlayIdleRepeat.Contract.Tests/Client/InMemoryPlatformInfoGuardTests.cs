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
    /// of the two rules fired. <c>de_DE</c> is a real culture name to the runtime — verified: the
    /// lookup succeeds and returns it — so only the spelling rule can reject it, and a case that
    /// accepted either message would pass if the two guards were swapped.
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
    /// The negative control over both locale guards: a real, standard-spelled culture is accepted,
    /// and comes back read-only.
    /// </summary>
    [Fact]
    public void A_real_culture_is_accepted_and_handed_back_read_only()
    {
        var platform = new InMemoryPlatformInfo();
        var writable = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        platform.ReportLocale(writable);

        platform.Locale.Name.ShouldBe(writable.Name);
        platform.Locale.IsReadOnly.ShouldBeTrue(
            "a guard that stored the caller's own mutable culture would let the caller keep "
            + "changing what the fake reports after the fact.");
    }
}
