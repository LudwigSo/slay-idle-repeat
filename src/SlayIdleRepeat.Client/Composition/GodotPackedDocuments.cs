using Godot;
using SlayIdleRepeat.Adapters.Content.Packed;

// ⚠️ Aliased because `FileAccess` is ambiguous: the engine's reader and System.IO's access-mode
// enum share the name, and `System.IO` is one of this project's implicit usings. The alias is the
// honest fix — a `using static` or dropping `using Godot;` would only move the collision.
using GodotFileAccess = Godot.FileAccess;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Reads the content set out of the engine's own virtual filesystem, so a packed build can find
/// documents that <c>System.IO</c> cannot see.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This class is the answer to M7-10x finding (1).</b> A resource packed into a <c>.pck</c> or
/// into an APK's assets exists only inside the engine's <c>PackedData</c> filesystem: it has no path
/// on disk, so <c>Directory.EnumerateFiles</c> finds nothing and <c>File.ReadAllBytes</c> throws.
/// Measured in M7-10 against a real desktop export — the build succeeded and then died at
/// <c>AppRoot</c> with no content at all. <c>DirAccess</c> and <c>FileAccess</c> are the engine's own
/// readers and they see straight through the archive.
/// </para>
/// <para>
/// 🔒 <b>It lives in <c>Composition/</c>, and that is the rule rather than a convenience.</b>
/// <c>23</c> §7.2a: a class able to reach the engine API implements no port — it is a
/// <em>capability</em> the composition root names. Only composition roots may reference an adapter
/// project at all, so this is the one place in the build that can hold both an engine call and a
/// reference to <see cref="IPackedDocumentReader"/>. <c>PlaceholderAtlasCatalogue</c> beside it is
/// the same shape for the same reason.
/// </para>
/// <para>
/// 🔒 <b>Nothing is cached, on purpose.</b> <see cref="PackedContentSource"/>'s revision is required
/// to move when a document does, so it re-asks; caching here would answer for the artefact as it was
/// rather than as it is. It costs an index walk of a few dozen entries, and in a shipped build the
/// answer cannot change anyway — a mounted archive is immutable.
/// </para>
/// <para>
/// ⚠️ <b>Enumeration inside an archive was PROVED before this was written, not assumed.</b> Reading a
/// known path through <c>FileAccess</c> is uncontroversial; that <c>DirAccess</c> can <em>list</em> a
/// packed directory is the load-bearing assumption, and a probe against the client's own exported
/// <c>.pck</c> walked all 88 documents and read one back byte for byte. Had it not, the fallback would
/// have been a generated manifest shipped as a document — considerably more machinery, which is why
/// it was worth ten minutes to find out first.
/// </para>
/// </remarks>
public sealed class GodotPackedDocuments : IPackedDocumentReader
{
    /// <summary>Where the content mirror is exported to, inside the artefact.</summary>
    /// <remarks>
    /// The same root the build-time mirror writes and <c>GodotUserPaths</c> globalises, spelled as a
    /// resource path because that is the only form that resolves once the tree is packed.
    /// </remarks>
    public const string ContentResourceRoot = "res://data";

    /// <summary>The documents the content set is made of. Everything else in the tree is ignored.</summary>
    private const string DocumentExtension = ".json";

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateDocuments()
    {
        var documents = new List<string>();

        Walk(string.Empty, documents);

        return documents;
    }

    /// <inheritdoc/>
    public bool TryRead(string documentPath, out ReadOnlyMemory<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);

        bytes = ReadOnlyMemory<byte>.Empty;

        var resourcePath = Resource(documentPath);

        // Asked before opening rather than inferring absence from a failed open: FileAccess.Open
        // answers null for a missing file AND for a file it could not read, and those are different
        // facts — the second one must not be reported to the caller as "no such document".
        if (!GodotFileAccess.FileExists(resourcePath))
        {
            return false;
        }

        using var handle = GodotFileAccess.Open(resourcePath, GodotFileAccess.ModeFlags.Read);

        if (handle is null)
        {
            // Present and unreadable. Left to the caller as an absence it will turn into
            // MissingContentException, with the engine's own reason logged so the difference between
            // "not shipped" and "shipped and broken" survives in the log even though the port has
            // only one way to say it.
            GD.PushError(
                $"'{resourcePath}' is inside the artefact and could not be opened " +
                $"(FileAccess error {GodotFileAccess.GetOpenError()}). The content set will report it as " +
                "missing, because IContentSourcePort has one answer for every way a document can fail.");

            return false;
        }

        bytes = handle.GetBuffer((long)handle.GetLength());

        return true;
    }

    /// <summary>Walks one directory of the packed tree, depth first, collecting documents.</summary>
    /// <remarks>
    /// 🔒 The static <c>DirAccess</c> helpers rather than an opened handle: they answer for a path
    /// without a handle to leak, and they are the pair the probe exercised against a real
    /// <c>.pck</c>. Directories are recursed and everything that is not a document is skipped rather
    /// than reported — the tree also carries <c>schema/</c> and, in a checkout, engine import files,
    /// and the content set is defined by the loader as the JSON documents.
    /// </remarks>
    /// <param name="relativeDirectory">Content-root-relative directory, empty for the root itself.</param>
    /// <param name="into">Collects content-root-relative document paths.</param>
    private static void Walk(string relativeDirectory, List<string> into)
    {
        var resourceDirectory = Resource(relativeDirectory);

        foreach (var file in DirAccess.GetFilesAt(resourceDirectory))
        {
            if (file.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase))
            {
                into.Add(Join(relativeDirectory, file));
            }
        }

        foreach (var directory in DirAccess.GetDirectoriesAt(resourceDirectory))
        {
            Walk(Join(relativeDirectory, directory), into);
        }
    }

    /// <summary>The engine path for a content-root-relative one.</summary>
    private static string Resource(string relativePath) =>
        relativePath.Length == 0 ? ContentResourceRoot : ContentResourceRoot + "/" + relativePath;

    private static string Join(string relativeDirectory, string name) =>
        relativeDirectory.Length == 0 ? name : relativeDirectory + "/" + name;
}
