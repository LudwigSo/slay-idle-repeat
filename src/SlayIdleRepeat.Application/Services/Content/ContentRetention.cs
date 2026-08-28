using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>One retained content version and the last moment anything was known to still want it.</summary>
/// <param name="Version">The stamp the stored bundle carries.</param>
/// <param name="LastReferencedAtUtc">
/// When a pin, a live run or a session last named this version. Never the moment it was published:
/// a version nobody has played against since it shipped is exactly the one a sweep is for.
/// </param>
public readonly record struct RetainedVersion(ContentVersion Version, DateTimeOffset LastReferencedAtUtc);

/// <summary>
/// Decides which stored content versions a sweep may drop. Pure: no I/O, no clock, no store —
/// the time and the inventory both arrive as values.
/// </summary>
/// <remarks>
/// <para>
/// Deleting a bundle a live run is pinned to would strand that run mid-play, so the rule is
/// stated once here and the file-backed store obeys it rather than re-deriving it. The current
/// version is never a candidate, whatever its age.
/// </para>
/// <para>
/// Pure also means testable at the boundary: "expired by one tick" and "expiring on the tick" are
/// two different calls with two different <c>nowUtc</c> values, not a test that waits.
/// </para>
/// </remarks>
public static class ContentRetention
{
    /// <summary>
    /// The default retention window, 48 hours.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>An inference, not a specified number.</b> No retention interval is authored anywhere
    /// in this project — no design document states one, and no configuration carries one. 48 h is
    /// taken from the run TTL, on the reasoning that a bundle stops being needed once the last run
    /// that could still be pinned to it has itself expired. Aligning to that TTL is the narrowest
    /// window that cannot strand a live run; it is deliberately not presented as a decided value,
    /// and the day somebody authors a real one this constant should give way to it rather than be
    /// quietly re-justified.
    /// </remarks>
    public static readonly TimeSpan WindowAlignedToRunTtl = TimeSpan.FromHours(48);

    /// <summary>The stored versions a sweep may delete.</summary>
    /// <param name="nowUtc">The moment the sweep runs, supplied by the caller.</param>
    /// <param name="current">The version being served now. Never a candidate for deletion.</param>
    /// <param name="stored">Every version the store currently holds, with its last reference.</param>
    /// <param name="window">How long an unreferenced version is kept — usually <see cref="WindowAlignedToRunTtl"/>.</param>
    /// <returns>The deletable versions, ordinal-sorted by stamp so a sweep is reproducible.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> or <paramref name="stored"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is not positive — a zero window would delete a version the moment it stopped being current.</exception>
    public static IReadOnlyList<ContentVersion> Sweep(
        DateTimeOffset nowUtc,
        ContentVersion current,
        IReadOnlyCollection<RetainedVersion> stored,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var cutoff = nowUtc - window;

        // Grouped by stamp and reduced to the LATEST reference: one version can be named by a run
        // pin and a session pin at once, and a sweep that took whichever row it met first would
        // delete a bundle something newer still wants.
        return stored
            .Where(entry => !entry.Version.Equals(current))
            .GroupBy(entry => entry.Version.Value, StringComparer.Ordinal)
            .Where(group => group.Max(entry => entry.LastReferencedAtUtc) < cutoff)
            .Select(group => group.First().Version)
            .OrderBy(version => version.Value, StringComparer.Ordinal)
            .ToArray();
    }
}
