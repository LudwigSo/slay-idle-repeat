using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Tests.Model;

// 🔒 Namespace SlayIdleRepeat.Core.Tests.Model, not ...Tests.Model.Run, and the files still sit
// under Model/Run/. Same reason the production aggregate does it: a child namespace named `Run`
// shadows the type `Run` for everything inside SlayIdleRepeat.Core.Tests.Model, so a test written
// here could not name the very class it is testing (CS0118).

/// <summary>
/// Hermetic <see cref="RunSnapshot"/> fixtures — one valid row, and a <c>With(...)</c> that replaces
/// exactly one field so a test names the single thing it is about.
/// </summary>
/// <remarks>
/// A test that built a whole snapshot inline would restate thirteen fields to change one, and the
/// reader could not tell which of the thirteen it was asserting about.
/// <para>
/// ⚠️ The baseline numbers are test values and carry <b>no design claim</b>: legal and unremarkable, not
/// a starting state — no document authors one until <c>START_RUN</c>, and a fixture that looked like a
/// starting run would be the S6 hole wearing a plausible value.
/// </para>
/// </remarks>
internal static class RunSnapshots
{
    /// <summary>2026-08-12 09:41:07 UTC — an ordinary instant, on no boundary at all.</summary>
    internal static readonly DateTimeOffset Midmorning = new(2026, 8, 12, 9, 41, 7, TimeSpan.Zero);

    /// <summary>The run identity every fixture uses unless a test is about identity.</summary>
    internal static readonly RunId Id = new("RUN_TEST");

    /// <summary>The owning player every fixture uses unless a test is about parentage.</summary>
    internal static readonly PlayerId Owner = new("PLAYER_TEST");

    /// <summary>An arbitrary but fixed committed run seed, so a test that hashes is reproducible.</summary>
    internal const ulong Seed = 0x0123456789ABCDEFUL;

    /// <summary>An ordinal <c>stream → next draw index</c> map of the shape the snapshot carries.</summary>
    internal static IReadOnlyDictionary<string, ulong> Streams(
        params (string Stream, ulong Position)[] entries) =>
        new ReadOnlyDictionary<string, ulong>(
            entries.ToDictionary(e => e.Stream, e => e.Position, StringComparer.Ordinal));

    /// <summary>An ordinal <c>placement → uses</c> map of the shape the snapshot carries.</summary>
    internal static IReadOnlyDictionary<string, long> AdUses(
        params (string Placement, long Uses)[] entries) =>
        new ReadOnlyDictionary<string, long>(
            entries.ToDictionary(e => e.Placement, e => e.Uses, StringComparer.Ordinal));

    /// <summary>A <c>position → MG_* id</c> map of the shape M3-03c's field carries.</summary>
    internal static IReadOnlyDictionary<int, string> ResolvedMinigames(
        params (int Position, string MinigameId)[] entries) =>
        new ReadOnlyDictionary<int, string>(entries.ToDictionary(e => e.Position, e => e.MinigameId));

    /// <summary>
    /// A valid row: chapter 1 on <see cref="DifficultyTier.NORMAL"/>, at position 0, unhurt, with no
    /// Gold, no stream ever drawn from and no ad watched.
    /// </summary>
    internal static RunSnapshot Valid { get; } = With();

    /// <summary>
    /// The valid row with one of its two reference-typed maps replaced by <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="With"/> cannot express this: its optional parameters read <c>null</c> as "keep the
    /// shipped value", which is what makes it readable — so the null cases get their own door rather
    /// than a sentinel every other call site would have to understand.
    /// </remarks>
    internal static RunSnapshot WithNull(bool streams = false, bool adUses = false, bool resolvedMinigames = false) =>
        new(
            SnapshotSchema.SchemaVersion,
            Id,
            Owner,
            Seed,
            1,
            DifficultyTier.NORMAL,
            Midmorning,
            0,
            100,
            100,
            0L,
            streams ? null! : Streams(),
            adUses ? null! : AdUses(),
            resolvedMinigames ? null! : ResolvedMinigames(),
            PendingForkJunctionPosition: null,
            PendingForkRemainingSteps: null);

    /// <summary>The valid row with individual fields replaced. Omit a parameter to keep it.</summary>
    internal static RunSnapshot With(
        int? schemaVersion = null,
        RunId? id = null,
        PlayerId? playerId = null,
        ulong? runSeed = null,
        int? chapterId = null,
        DifficultyTier? tier = null,
        DateTimeOffset? lastAppliedAtUtc = null,
        int? position = null,
        int? currentHp = null,
        int? maxHp = null,
        long? gold = null,
        IReadOnlyDictionary<string, ulong>? rngStreamPositions = null,
        IReadOnlyDictionary<string, long>? adUses = null,
        IReadOnlyDictionary<int, string>? resolvedMinigames = null,
        int? pendingForkJunctionPosition = null,
        int? pendingForkRemainingSteps = null) =>
        new(
            schemaVersion ?? SnapshotSchema.SchemaVersion,
            id ?? Id,
            playerId ?? Owner,
            runSeed ?? Seed,
            chapterId ?? 1,
            tier ?? DifficultyTier.NORMAL,
            lastAppliedAtUtc ?? Midmorning,
            position ?? 0,
            currentHp ?? 100,
            maxHp ?? 100,
            gold ?? 0L,
            rngStreamPositions ?? Streams(),
            adUses ?? AdUses(),
            resolvedMinigames ?? ResolvedMinigames(),
            pendingForkJunctionPosition,
            pendingForkRemainingSteps);
}
