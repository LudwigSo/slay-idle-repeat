using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `14` §6 — the build-time mirror of <c>game-data</c> into the Godot project holds exactly what
/// <c>game-data</c> holds: no more, and no less.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Since M7-10y the mirror is what the export SHIPS</b>, read out of the packed artefact by
/// <c>PackedContentSource</c>. Before that it was an editor convenience whose contents nothing
/// depended on, which is why its build target copied and never deleted, and said so: <em>"if a
/// removal ever matters, delete the directory rather than teaching this target to."</em> It matters
/// now, the target prunes, and this is the check that says whether it worked.
/// </para>
/// <para>
/// 🔴 <b>The gap this closes is one nothing else in the build can see.</b> Content validation runs
/// over <c>game-data</c>; the schema/data pairing rules, the 📐 audit and the loc-key invariants all
/// read <c>game-data</c>. NOTHING reads the mirror. So a document that exists only in the mirror is
/// invisible to every gate in CI and present in the player's build — and a document missing from the
/// mirror is content the game ships without while every content check stays green.
/// </para>
/// <para>
/// Found for real: a probe of the exported <c>.pck</c> walked 88 documents where <c>game-data</c>
/// holds 87, and the extra was <c>schema/inventory_screen.schema.json</c>, left behind when M7-11
/// renamed that schema. ⚠️ <b>That instance was harmless</b> — schemas are excluded from the content
/// snapshot, so it changed nothing at runtime — <b>and that is exactly why it survived</b>. The
/// mechanism is the hazard: the same drift in <c>content/</c> or <c>loc/</c> lands in the snapshot and
/// changes the game.
/// </para>
/// <para>
/// ⚠️ Scoped to <c>*.json</c>, which is what the mirror owns. The engine writes its own files into
/// the project tree and they are not this check's business.
/// </para>
/// </remarks>
public sealed class ContentMirrorTests
{
    /// <summary>The generated mirror, relative to the checkout root.</summary>
    private static readonly string[] MirrorSegments = ["src", "SlayIdleRepeat.Client", "data"];

    private const string DocumentPattern = "*.json";

    /// <summary>
    /// 🔒 <b>Every document in <c>game-data</c> reaches the mirror, and nothing else does.</b>
    /// </summary>
    /// <remarks>
    /// Both directions in one case, because they are one property — "the mirror is a copy" — and the
    /// two failures are told apart in the message rather than by two cases that could disagree about
    /// which set is the authority. <c>game-data</c> is always the authority.
    /// </remarks>
    [Fact]
    public void The_mirror_holds_exactly_what_game_data_holds()
    {
        var authored = Documents(RepoPaths.ContentDataRoot);
        var mirrored = Documents(MirrorRoot());

        var stale = mirrored.Except(authored, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal);
        var missing = authored.Except(mirrored, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal);

        stale.ShouldBeEmpty(
            "the mirror holds documents game-data does not author, and since M7-10y the mirror is what " +
            "the export ships — so these are documents in the player's build that the repository has no " +
            "record of, and no content gate can see them because every one of them reads game-data. The " +
            "build target prunes stale documents; if these survived it, the prune is broken. Delete " +
            "src/SlayIdleRepeat.Client/data and rebuild to recover.");

        missing.ShouldBeEmpty(
            "game-data authors documents the mirror does not hold, so an export would ship without " +
            "them while every content check stayed green — the loader would fail at boot in a build " +
            "nobody could reproduce from a checkout. Rebuild SlayIdleRepeat.Client to refresh it.");
    }

    /// <summary>
    /// …and the mirror is not empty, which is the one failure the two set comparisons cannot see.
    /// </summary>
    /// <remarks>
    /// 🔒 An empty mirror against an empty <c>game-data</c> satisfies "holds exactly what game-data
    /// holds" perfectly. <c>RepoPaths.ContentDataRoot</c> already refuses a checkout with no content
    /// tree, so this is the other half: a real count, so the case above cannot pass vacuously on a
    /// tree that was never copied.
    /// </remarks>
    [Fact]
    public void The_mirror_is_not_empty()
    {
        Documents(MirrorRoot()).Count.ShouldBeGreaterThan(
            0,
            "the mirror holds no documents at all. The client's build target creates it, so this means " +
            "the client was never built in this checkout — and an exported build would start and then " +
            "find no content, three steps away from the thing that actually went wrong.");
    }

    private static string MirrorRoot()
    {
        var path = Path.Combine([RepoPaths.RepositoryRoot, .. MirrorSegments]);

        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException(
                $"No mirror at '{path}'. It is generated by the client's own build, so a run without " +
                "one has not built the client — and the mirror is what an exported build reads its " +
                "content out of.");
    }

    /// <summary>Every document under a root, as root-relative forward-slashed paths.</summary>
    private static IReadOnlyCollection<string> Documents(string root) =>
        Directory.EnumerateFiles(root, DocumentPattern, SearchOption.AllDirectories)
                 .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                 .ToHashSet(StringComparer.Ordinal);
}
