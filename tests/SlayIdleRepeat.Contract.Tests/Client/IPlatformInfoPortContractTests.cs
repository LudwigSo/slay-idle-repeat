using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// States what <see cref="IPlatformInfoPort"/> <em>means</em> (<c>23</c> §4.1, §5 A8) — written once
/// against the interface so the host-reading adapter and the settable fake cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 This port has no methods, no arguments and no cancellation, so it has no exceptions — the
/// shape of disagreement the founding incident behind steering <b>S7</b> was about cannot arise
/// here. What can, and what these cases are aimed at, is the <em>other</em> way two implementations
/// of an information port drift: they answer the same question in two different vocabularies. One
/// says "I do not know" with <see langword="null"/> and the other with a placeholder string; one
/// hands back a culture and the other hands back the host's raw locale spelling with an underscore
/// in it. Both look correct in isolation and neither caller can tell which it is holding.
/// </para>
/// <para>
/// 🔒 Nothing here asserts a <em>value</em> — not an OS name, not a culture, not a version. Those
/// differ per machine by design, and a case that pinned one would be pinning the machine that ran
/// it. Every case below is about the <em>shape</em> of the answer, which is the part two
/// implementations have to agree on and the part a caller writes code against.
/// </para>
/// <para>
/// ⚠️ <b>What no shared suite over this port can do, stated once so it is not mistaken for an
/// oversight.</b> It cannot demand that an answer is <em>true</em>. A fake's whole job is to report
/// values a test chose, so any case asking "does this describe the machine we are on" would fail the
/// fake by design — and a suite that ran only against the real adapter would not be a shared suite.
/// <c>IClockPort</c> escapes this because time has a universal floor that a made-up instant falls
/// below; an operating system's name has no counterpart. So an implementation returning four
/// well-shaped constants passes everything here, and that is a limit of the seam rather than of
/// these cases. What the suite does close is every way two implementations could give <em>different
/// shapes</em> for the same fact, which is what a caller actually breaks on.
/// </para>
/// </remarks>
[ContractSuiteFor(typeof(IPlatformInfoPort))]
public abstract class IPlatformInfoPortContractTests
{
    /// <summary>
    /// Characters a host uses to spell a locale that <c>BCP 47</c> does not — an underscore between
    /// the subtags, and the separator before a trailing keyword list.
    /// </summary>
    /// <remarks>
    /// Not hypothetical: the engine this game ships on answers
    /// <c>language_Script_COUNTRY_VARIANT@extra</c>, and a host runtime answers
    /// <c>language-COUNTRY</c>. An implementation that passes its host's spelling straight through
    /// hands the caller a string whose punctuation says which implementation produced it.
    /// </remarks>
    private static readonly char[] SpellingsThatAreNotBcp47 = ['_', '@'];

    /// <summary>Platform info of the implementation under test, as a caller would obtain it.</summary>
    protected abstract IPlatformInfoPort Create();

    /// <summary>
    /// The same implementation on a host that cannot identify the device.
    /// </summary>
    /// <remarks>
    /// 🔒 Every implementation can be put in this state, which is why this is not optional. A
    /// general-purpose runtime has no notion of a device model and is therefore <em>always</em> in
    /// it; a game engine answers a literal of its own on every platform it cannot name; a fake is
    /// told. What the three must not do is disagree about how the absence arrives, and only a hook
    /// can reach the state on all of them.
    /// </remarks>
    protected abstract IPlatformInfoPort CreateWithUnidentifiedDevice();

    // ------------------------------------------------------------------------------ facts, not samples

    /// <summary>
    /// 🔒 Every member answers identically on a second read: these are facts about the host, not
    /// samples of anything.
    /// </summary>
    /// <remarks>
    /// This is the case that makes the port cacheable, and the whole reason its members are
    /// properties rather than an <c>...Async</c> call. It fails an implementation that recomputes,
    /// re-queries or re-formats on every read — the defect that turns a boot-time read stored in a
    /// field into a value that no longer matches what a crash report is grouped by.
    /// <para>
    /// All four members are checked in one case, and the failure names the member that moved:
    /// asserted one at a time, the interesting distinction — one member drifting versus the whole
    /// port being re-sampled — is the one a stop-at-the-first assertion cannot draw.
    /// </para>
    /// <para>
    /// ⚠️ <b>What this case cannot catch, measured rather than assumed.</b> It sees a member whose
    /// <em>value</em> differs between two reads, which is not the same as a member that recomputes.
    /// An implementation returning <c>version + Environment.TickCount64</c> was run against it and
    /// <b>passed</b>: two property reads are microseconds apart and the tick had not moved. It also
    /// compares <see cref="CultureInfo.Name"/> rather than the reference, so a member handing back a
    /// fresh but equal object each read is not caught either — nor should it be, since
    /// <see cref="Locale_is_handed_back_read_only"/> is what makes that harmless. So this case
    /// catches a member wired to a counter or a fresh identifier, and it does not catch one wired to
    /// a slow-moving source. Widening it — reading again after a delay —
    /// would buy that at the price of a timing-dependent contract case, which is the kind that gets
    /// deleted rather than debugged.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_member_answers_the_same_on_a_second_read()
    {
        var platform = Create();
        var offenders = new List<string>();

        Stable(offenders, "DeviceModel", platform.DeviceModel, platform.DeviceModel);
        Stable(offenders, "OsVersion", platform.OsVersion, platform.OsVersion);
        Stable(offenders, "AppVersion", platform.AppVersion, platform.AppVersion);
        Stable(offenders, "Locale", platform.Locale.Name, platform.Locale.Name);

        offenders.ShouldBeEmpty(
            "IPlatformInfoPort reports facts about the host, and a caller is entitled to read one "
            + "once at boot and keep it. A member that answers differently on a second read makes "
            + $"the value in that caller's field silently wrong: {string.Join("; ", offenders)}");
    }

    // ------------------------------------------------------------------- how the host says 'I do not know'

    /// <summary>
    /// 🔒 A host that cannot identify the device answers <see langword="null"/> — never an empty
    /// string, never a placeholder that reads as a device.
    /// </summary>
    /// <remarks>
    /// The load-bearing case of this suite, and the one no implementation can be trusted to get
    /// right on its own, because every host offers its own tempting placeholder. Let one through and
    /// crash grouping collects every unidentified handset in the world under a single invented model
    /// name, which is indistinguishable from one real device with one catastrophic defect — and the
    /// triage that follows is spent on a device that does not exist.
    /// </remarks>
    [Fact]
    public void An_unidentified_device_is_null_rather_than_a_placeholder()
    {
        var platform = CreateWithUnidentifiedDevice();

        platform.DeviceModel.ShouldBeNull(
            $"the host cannot identify the device, and this port spells that absence null. "
            + $"'{platform.DeviceModel}' is a placeholder: a caller cannot tell it from a device "
            + "model that was genuinely reported, so it is worse than no answer at all.");
    }

    /// <summary>
    /// A device model that <em>is</em> reported is a real answer: never empty, never whitespace.
    /// </summary>
    /// <remarks>
    /// The other half of the case above, and it survives a fix aimed only at that one. An
    /// implementation that translated its host's placeholder into <c>""</c> rather than into
    /// <see langword="null"/> satisfies nothing here, and a caller asked to test for both an absence
    /// and a blank will eventually test for only one of them.
    /// <para>
    /// Stated as the whole invariant — null <em>or</em> a real answer — rather than as a
    /// conditional assertion, so the case asserts something on every fixture instead of returning
    /// early and reporting success on the ones whose host never identifies a device.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_device_model_is_either_absent_or_a_real_answer()
    {
        var platform = Create();
        var model = platform.DeviceModel;

        (model is null || !string.IsNullOrWhiteSpace(model)).ShouldBeTrue(
            $"DeviceModel answered '{model}'. A blank string is a third way of saying nothing, "
            + "beside null and a real answer. This port has two states, and a caller that has to "
            + "handle three will handle two.");
    }

    // --------------------------------------------------------------------------- the members that are always there

    /// <summary>The operating system is always reported.</summary>
    [Fact]
    public void OsVersion_is_never_blank()
    {
        var platform = Create();

        platform.OsVersion.ShouldNotBeNullOrWhiteSpace(
            "OsVersion has no absent state — every host knows what it is running on. An empty "
            + "string here is an implementation that was never wired, and it reaches a crash report "
            + "as a blank column nobody can act on.");
    }

    /// <summary>The build is always reported.</summary>
    [Fact]
    public void AppVersion_is_never_blank()
    {
        var platform = Create();

        platform.AppVersion.ShouldNotBeNullOrWhiteSpace(
            "AppVersion has no absent state either. A support conversation that cannot establish "
            + "which build the player is running cannot begin.");
    }

    // --------------------------------------------------------------------------------------- the locale

    /// <summary>
    /// 🔒 The locale is a culture the runtime <em>knows</em> — one of the cultures it enumerates,
    /// not merely one whose name it can parse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="CultureInfo"/> can be constructed around a name no language has, so the type
    /// alone guarantees nothing. This case is what makes the member a <em>language the player
    /// reads</em> rather than a string that happens to be wrapped in a culture object.
    /// </para>
    /// <para>
    /// 🔒 <b>Enumeration rather than a round-trip, and the difference was measured.</b> This case
    /// was first written as "<c>GetCultureInfo(name).Name</c> equals <c>name</c>", and that check
    /// passes for <c>xx-Zzzz-QQ</c>: the runtime manufactures a custom culture for any
    /// syntactically well-formed tag and hands it straight back, so the round-trip only ever
    /// asserted that the string parses. A language that does not exist would have reached every
    /// caller of this port with the case green.
    /// </para>
    /// <para>
    /// ⚠️ The invariant culture is in the enumerated set, so a host with no language preference
    /// still passes — which is the port's own ruling, not a gap here.
    /// </para>
    /// </remarks>
    [Fact]
    public void Locale_is_a_culture_the_runtime_knows()
    {
        var platform = Create();
        var locale = platform.Locale;

        locale.ShouldNotBeNull("absence is not one of this member's states — a host with no "
            + "preference at all resolves to the invariant culture, which is an answer.");

        CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Any(known => string.Equals(known.Name, locale.Name, StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue(
                $"'{locale.Name}' is not among the cultures this runtime enumerates. It may still "
                + "parse — the runtime invents a culture for any well-formed tag — and that is "
                + "exactly why parsing is not the test. A player cannot read a language nothing has "
                + "resources for.");
    }

    /// <summary>
    /// 🔒 The culture handed back cannot be mutated by whoever receives it.
    /// </summary>
    /// <remarks>
    /// <see cref="CultureInfo"/> is mutable — its calendar, its number and date formats — and the
    /// real host reader's answer is the very object backing the thread's current UI culture. One
    /// caller adjusting a format on the value it was handed changes what every later reader of this
    /// port sees, and on that implementation changes formatting for the whole process. This is the
    /// same ruling <c>ILocalCachePort</c> makes when it says a read returns a copy, and it has to be
    /// stated here for the same reason: the implementation that cannot have the defect is not the
    /// one the rule is for.
    /// </remarks>
    [Fact]
    public void Locale_is_handed_back_read_only()
    {
        var platform = Create();

        platform.Locale.IsReadOnly.ShouldBeTrue(
            "the culture this port returns is writable, so any caller can reconfigure it for every "
            + "other caller. CultureInfo.ReadOnly is what closes it, and closing it is the "
            + "implementation's job — this case is only where the two implementations are told that "
            + "they both have it.");
    }

    /// <summary>
    /// 🔒 The locale name is spelled the way <c>BCP 47</c> spells it, not the way the host does.
    /// </summary>
    /// <remarks>
    /// The case above is satisfied by a name the runtime happens to accept in the host's own
    /// punctuation, and this is the one that closes it. It is the exact drift this port exists to
    /// prevent: two implementations, two host spellings of the same language, and a caller that can
    /// tell which implementation it is holding by looking at the punctuation. The translation is
    /// each implementation's job; that there is only one answer is this suite's.
    /// </remarks>
    [Fact]
    public void Locale_is_spelled_the_way_the_standard_spells_it()
    {
        var platform = Create();
        var name = platform.Locale.Name;

        name.IndexOfAny(SpellingsThatAreNotBcp47).ShouldBe(
            -1,
            $"'{name}' carries punctuation BCP 47 does not use, which means it is a host's own "
            + "locale spelling passed through rather than translated. An underscore separates the "
            + "subtags on one host and a hyphen on another; an '@' introduces a trailing keyword "
            + "list that has no counterpart to translate into and is dropped, not kept.");
    }

    private static void Stable(ICollection<string> offenders, string member, string? first, string? second)
    {
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            offenders.Add($"{member} answered '{first}' and then '{second}'");
        }
    }
}
