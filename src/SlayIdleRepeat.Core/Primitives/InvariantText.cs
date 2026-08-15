using System.Globalization;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>Round-trip, culture-invariant formatting of a number for a message or a hashed line — the convention, stated once for the whole assembly.</summary>
/// <remarks>
/// Both halves matter independently: <see cref="CultureInfo.InvariantCulture"/> is a determinism
/// rule — a culture-sensitive format (e.g. <c>0,25</c> on a German-locale machine vs <c>0.25</c>
/// elsewhere) would make two machines disagree about a hash with no real bug behind it. The
/// round-trip <c>"R"</c> format is a diagnosis rule: a failure message that reports a value reports
/// the value that actually failed, not a shortened one that would pass the guard it just tripped.
/// <para>
/// <see cref="Text(int)"/> takes no <c>"R"</c> because an <see cref="int"/> has nothing to shorten;
/// only the culture matters there.
/// </para>
/// <para>
/// Not the only way a number reaches a message: a call site formatting for a human reader (a report
/// column, a percentage in prose) states its own format string. This type is for a value's own
/// identity — a test assertion, a hash, or diagnosing a determinism divergence.
/// </para>
/// </remarks>
internal static class InvariantText
{
    /// <summary>The round-trip, culture-invariant text of one <see cref="double"/>.</summary>
    internal static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The culture-invariant text of one <see cref="int"/>.</summary>
    internal static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
