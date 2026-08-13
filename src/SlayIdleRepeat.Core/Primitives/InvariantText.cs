using System.Globalization;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 <b>Round-trip, culture-invariant formatting of a number for a message or a hashed line — the
/// convention, stated once for the whole assembly.</b>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this type exists.</b> <c>value.ToString("R", CultureInfo.InvariantCulture)</c> was
/// written out at roughly thirty-five sites across <c>Core</c>, and <b>six</b> of those had grown an
/// identical <c>private static string Format(double)</c> wrapper in six different files —
/// <c>Rules/Combat/CombatLog</c>, <c>Rules/Combat/Bosses/BossEncounter</c>,
/// <c>Rules/Combat/Bosses/BossSummonSource</c>, <c>Rules/Combat/Bosses/BossTelegraphs</c>,
/// <c>Rules/Combat/Enemies/ArchetypeRow</c>, <c>Rules/Effects/Triggers/TriggerCatalogue</c> and
/// <c>Rules/Effects/Triggers/TriggerSchedule</c> — spanning four namespaces that cannot see one
/// another's privates. Each was written independently, and each is a place the <c>"R"</c> or the
/// culture could drift. That is the same shape as <see cref="DeterminismRounding"/>'s six statements
/// of `05` §1.1, and it is fixed the same way.
/// </para>
/// <para>
/// 🔒 <b>Both halves are load-bearing, and for different reasons.</b> <c>CultureInfo.InvariantCulture</c>
/// is a determinism rule: this repo is developed on a German-locale machine, whose current culture
/// writes <c>0,25</c> where an invariant one writes <c>0.25</c> — and <c>CombatLog</c>'s formatted
/// lines feed `11` §6's <c>LogHash</c>, so one culture-sensitive format would make two machines
/// disagree about a tamper check with no bug anywhere else. <c>"R"</c> is a
/// diagnosis rule: it round-trips, so a failure message that reports a value reports the value that
/// actually failed rather than a shortened one that would satisfy the guard it just tripped.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Text(int)"/> takes no <c>"R"</c>, and that is not an inconsistency.</b> An
/// <see cref="int"/> has no shortening to round-trip past; only the culture matters, and a negative
/// integer is the one place it does (some cultures write a different sign glyph).
/// </para>
/// <para>
/// ⚠️ <b>It is not the only way a number reaches a message, deliberately.</b> A call site that wants
/// a fixed number of places for a <em>human</em> — a report column, a percentage in prose — states
/// its own format string; this type is for the value's own identity, where the reader is a test
/// assertion, a hash, or someone diagnosing a determinism divergence.
/// </para>
/// </remarks>
internal static class InvariantText
{
    /// <summary>The round-trip, culture-invariant text of one <see cref="double"/>.</summary>
    internal static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The culture-invariant text of one <see cref="int"/>.</summary>
    internal static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
