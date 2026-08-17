using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.Services;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services;

/// <summary>
/// The two translations every implementation of <c>IPlatformInfoPort</c> owes its callers, tested
/// where they can actually be reached.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This file is the answer to a measured hole.</b> The shared contract suite over that port
/// asserts <em>shapes</em>, and a fixture returning four well-formed constants passes every case in
/// it — verified by running exactly such a class through the suite. That is not a defect in the
/// suite; a shared suite cannot demand that an answer is true, because the fake's whole job is to
/// report values a test chose. What it means is that the port's real content is the
/// <em>translation</em>, and the translation has to be tested somewhere the suite is not.
/// </para>
/// <para>
/// 🔒 <b>And it is the only way the engine adapter's hardest behaviour gets tested at all.</b>
/// <c>GodotPlatformInfo</c> cannot be exercised by anything — calling it outside the runtime is a
/// fatal <c>AccessViolationException</c> that kills the test host, measured. Its engine call is one
/// line; everything that happens to the string afterwards is here, and runs.
/// </para>
/// <para>
/// Every case is machine-independent: nothing asserts what culture this host is in, only what
/// happens to a spelling.
/// </para>
/// </remarks>
public sealed class HostAnswersTests
{
    /// <summary>A language every .NET runtime with cultures at all enumerates.</summary>
    private const string WidelyKnownCulture = "de-DE";

    private static bool HostHasCultures =>
        CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Any(known => known.Name.Equals(WidelyKnownCulture, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------------------------ locale

    /// <summary>
    /// 🔒 A host's own spelling becomes the standard's: underscores are separators, and the trailing
    /// keyword list is dropped.
    /// </summary>
    /// <remarks>
    /// These are the shapes the engine actually produces —
    /// <c>language_Script_COUNTRY_VARIANT@extra</c> — and the reason the port rules that a locale
    /// arrives as a culture rather than as whatever the host said. A caller handed
    /// <c>de_DE@euro</c> would have to know which host produced it to read it.
    /// </remarks>
    /// <remarks>
    /// ⚠️ The expectation is <em>derived</em> rather than skipped on a host without cultures: this
    /// repository already builds five projects with <c>InvariantGlobalization</c>, and a case that
    /// vanished there would be a case nobody notices vanishing. Where the runtime has cultures the
    /// translation must land on the language; where it has none, the invariant culture is the
    /// port's own stated answer.
    /// </remarks>
    [Theory]
    [InlineData("de_DE")]
    [InlineData("de-DE")]
    [InlineData("de_DE@euro")]
    [InlineData("  de_DE@euro  ")]
    public void A_host_spelling_becomes_the_standard_spelling(string hostLocale)
    {
        var expected = HostHasCultures ? WidelyKnownCulture : string.Empty;

        HostAnswers.ToLocale(hostLocale).Name.ShouldBe(
            expected,
            $"'{hostLocale}' is a spelling a real host produces. Passed through, the caller receives "
            + "punctuation that says which implementation answered.");
    }

    /// <summary>The result carries none of the punctuation the standard does not use.</summary>
    /// <remarks>
    /// Holds on every host, with cultures or without: the invariant culture's name is empty and
    /// contains neither separator either.
    /// </remarks>
    [Fact]
    public void The_result_never_carries_a_host_separator()
    {
        HostAnswers.ToLocale("zh_Hans_CN@collation=pinyin").Name
            .IndexOfAny(['_', '@'])
            .ShouldBe(-1, "a four-subtag locale with a keyword list is the worst case an engine emits.");
    }

    /// <summary>
    /// 🔒 A language the runtime does not know resolves to the invariant culture rather than
    /// throwing.
    /// </summary>
    /// <remarks>
    /// <c>xx-Zzzz-QQ</c> is well formed, so the runtime will happily manufacture a culture for it —
    /// which is why the check is enumeration and not parsing. A host reporting a language nothing
    /// has resources for is not a fault a caller can handle: the game still has to start, in some
    /// language.
    /// </remarks>
    [Theory]
    [InlineData("xx-Zzzz-QQ")]
    [InlineData("xx_Zzzz_QQ@nonsense")]
    public void A_language_the_runtime_does_not_know_resolves_to_the_invariant_culture(string unknown)
    {
        HostAnswers.ToLocale(unknown).Name.ShouldBe(
            string.Empty,
            "the runtime manufactures a culture for any well-formed tag, so accepting it would put a "
            + "language nobody speaks in front of a player.");
    }

    /// <summary>A host with no preference at all is the invariant culture, which is an answer.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_preference_at_all_is_the_invariant_culture(string? nothing)
    {
        HostAnswers.ToLocale(nothing).Name.ShouldBe(string.Empty);
    }

    /// <summary>The culture handed back cannot be reconfigured by whoever receives it.</summary>
    /// <remarks>
    /// A culture is mutable, and on a host-backed implementation the instance is the one the runtime
    /// itself is using — so a caller adjusting a format would change it for the whole process.
    /// </remarks>
    [Fact]
    public void The_result_is_read_only()
    {
        HostAnswers.ToLocale(WidelyKnownCulture).IsReadOnly.ShouldBeTrue();
        HostAnswers.ToLocale(null).IsReadOnly.ShouldBeTrue("the invariant fallback is handed out too.");
    }

    // ------------------------------------------------------------------------------ device model

    /// <summary>
    /// 🔒 A host's placeholder for "I do not know" becomes the absence the port rules, whatever its
    /// case.
    /// </summary>
    /// <remarks>
    /// This is the one that loses a triage week. Let the placeholder through and crash grouping
    /// collects every unidentified handset on earth under one invented model name, which reads as a
    /// single real device with a single catastrophic defect.
    /// </remarks>
    [Theory]
    [InlineData("GenericDevice")]
    [InlineData("genericdevice")]
    [InlineData("  GenericDevice  ")]
    public void A_host_placeholder_becomes_an_absence(string placeholder)
    {
        HostAnswers.ToDeviceModel(placeholder, "GenericDevice").ShouldBeNull();
    }

    /// <summary>Nothing at all is also an absence — there is no third state.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_at_all_is_an_absence(string? nothing)
    {
        HostAnswers.ToDeviceModel(nothing, "GenericDevice").ShouldBeNull();
    }

    /// <summary>
    /// The negative control: a device that names itself comes back, trimmed and unchanged.
    /// </summary>
    /// <remarks>
    /// Without this, every case above is satisfied by a translation that answers
    /// <see langword="null"/> to everything — which is the implementation that makes the port
    /// useless while passing its whole contract suite.
    /// </remarks>
    [Fact]
    public void A_device_that_names_itself_comes_back()
    {
        HostAnswers.ToDeviceModel("  Pixel 7a  ", "GenericDevice").ShouldBe("Pixel 7a");
    }

    /// <summary>A host that offers no placeholders keeps whatever it said.</summary>
    /// <remarks>
    /// The placeholder list is the caller's, because an engine's own word for an absence is that
    /// adapter's knowledge and not <c>Application</c>'s.
    /// </remarks>
    [Fact]
    public void With_no_placeholders_stated_every_non_blank_answer_survives()
    {
        HostAnswers.ToDeviceModel("GenericDevice").ShouldBe("GenericDevice");
    }
}
