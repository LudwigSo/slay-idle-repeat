using System.Globalization;

namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>
/// What the host the game is running on will say about itself: which build this is, which operating
/// system it sits on, which language the player reads, and — when the host knows — which device it
/// is.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every member here is a fact, not a sample.</b> Two reads of the same member on the same
/// instance answer identically, so a caller may read one once at boot and keep it. That is the
/// difference between this port and <c>IClockPort</c>, and it is why nothing here is asynchronous or
/// cancellable: an implementation that had to go and ask somebody would be answering a question this
/// port does not pose.
/// </para>
/// <para>
/// ⚠️ Per <em>instance</em>, deliberately, and not "per process". <see cref="Locale"/> is the
/// clearest case: a host's language preference is thread-scoped on some runtimes, so two instances
/// built on two threads may legitimately disagree, and an instance is the only scope an
/// implementation can actually promise.
/// </para>
/// <para>
/// 🔒 <b>Nothing here decides anything.</b> These are inputs to crash grouping, support, and the
/// initial language choice. A rule that branches on <see cref="OsVersion"/> or
/// <see cref="DeviceModel"/> puts a per-device difference inside deterministic computation, which is
/// the one thing `14` §8 forbids outright.
/// </para>
/// <para>
/// ⚠️ <b>This port declares four of the five members `23` §4.1 writes, and the omission is
/// deliberate.</b> The section's fifth member is <c>bool IsLowEndDevice</c>, and no document in this
/// repository rules what makes a device low-end: `01` §8's success criteria name a 2021 mid-range
/// Android under 400 MB of RAM as the bar the game must <i>clear</i>, which is not the same claim as
/// a threshold below which a device is degraded. Declaring the member would mean each implementation
/// inventing its own cut-off, and a shared contract suite cannot state what two implementations mean
/// by it — so the two would disagree silently, which is the failure a port exists to prevent. It
/// belongs to whichever task first has a quality setting to drive with it. The omission is carried
/// in <c>PortCatalogue.OmittedMembers</c>, which expires it: the entry fails the build on the commit
/// that declares the member. ⚠️ That entry names <c>M9-04</c> — the Settings screen, the nearest row
/// and <b>not</b> a row that claims this — because an entry with no owner has no expiry at all; the
/// caveat above it says so, and a reader checking M9-04 at kickoff may well find it disowns this.
/// <c>ILocalCachePort</c> departs from `23` §4.1 too, differently shaped rather than narrower, and
/// argues its own departure in its own remarks — on serializer placement, not by citing the section.
/// </para>
/// </remarks>
public interface IPlatformInfoPort
{
    /// <summary>
    /// The device the game is running on, or <see langword="null"/> when the host cannot identify
    /// it.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>An unidentified device is <see langword="null"/>, never a placeholder string.</b> Hosts
    /// each have their own way of saying "I do not know" — a general-purpose runtime has no notion
    /// of a device model at all, and a game engine answers a literal of its own on every platform it
    /// cannot name — and every one of those has to arrive here as the same absence. Let the
    /// placeholders through and a crash report groups thousands of unrelated devices under one
    /// invented model name, which reads as a real device with a real defect.
    /// <para>
    /// When it is not <see langword="null"/> it is a real answer: never empty and never whitespace.
    /// A blank string is a third way of saying nothing, and a caller cannot be asked to test for
    /// three.
    /// </para>
    /// </remarks>
    string? DeviceModel { get; }

    /// <summary>The host operating system, as the host names it. Never empty.</summary>
    /// <remarks>
    /// Passed through in the host's own spelling rather than mapped onto a vocabulary this
    /// repository would then have to maintain: the string's only readers are a human triaging a
    /// crash and a support agent reading a bug report, and both are better served by the spelling
    /// the platform's own documentation uses.
    /// </remarks>
    string OsVersion { get; }

    /// <summary>The version of the build the player is running. Never empty.</summary>
    /// <remarks>
    /// The first of the three independent numbers <c>Directory.Build.props</c> enumerates — the
    /// assembly's own SemVer, not the wire protocol version and not the snapshot schema version.
    /// A support conversation that cannot establish which build a player is on cannot begin.
    /// <para>
    /// ⚠️ The <em>version</em>, not the version plus whatever the build stamped after it. The SDK
    /// appends the source revision to the informational version by default, so an implementation
    /// reading that attribute verbatim answers a forty-nine character string with a commit hash in
    /// it — which is a fine thing to log and is not what a player reads off a settings screen or a
    /// support agent asks for. Build metadata is the implementation's to strip.
    /// </para>
    /// </remarks>
    string AppVersion { get; }

    /// <summary>The language the player reads, as a culture the runtime knows.</summary>
    /// <remarks>
    /// 🔒 <b>A culture, not the host's raw locale string.</b> Hosts spell a locale their own way —
    /// underscores instead of hyphens, a trailing keyword list after an <c>@</c> — and a caller that
    /// received one of those verbatim would have to know which host it came from to read it. The
    /// translation is each implementation's, and this port's ruling is that whatever arrives here
    /// is a culture the runtime <b>enumerates</b> — one it has resources for, not merely one whose
    /// name parses. The runtime manufactures a culture for any well-formed tag, so a name that
    /// round-trips is not evidence the language exists.
    /// <para>
    /// 🔒 <b>Read-only</b>, via <see cref="CultureInfo.ReadOnly(CultureInfo)"/>. A culture is a
    /// mutable object carrying the host's number, date and calendar formats, and on a host-backed
    /// implementation the instance handed out is the one the runtime itself is using — so a caller
    /// that adjusts a format on the value it received changes it for every later reader and,
    /// there, for the whole process. Same ruling as <c>ILocalCachePort</c>'s "a read returns a
    /// copy", for the same reason.
    /// </para>
    /// <para>
    /// ⚠️ Never <see langword="null"/>. A host with no language preference at all resolves to the
    /// invariant culture, which is an answer; absence is not one of this member's states.
    /// </para>
    /// </remarks>
    CultureInfo Locale { get; }
}
