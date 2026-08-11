using System.Text;
using FluentAssertions;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// 🔒 The one suite that states what <see cref="IContentSourcePort"/> <em>means</em>, run against
/// <b>every</b> implementation including the fake (`23` §5 A8, `14` §13).
/// </summary>
/// <remarks>
/// <para>
/// This is the first port the project shipped, and it shipped without this suite. The two
/// implementations had already drifted: the port documented <em>"Throws when the path is not
/// listed"</em>, while <c>InMemoryContentSource</c> threw <c>KeyNotFoundException</c> and
/// <c>LocalFileContentSource</c> threw <c>ArgumentException</c> for an escaping path and let
/// <c>File.ReadAllBytes</c>'s <c>FileNotFoundException</c> out otherwise. Only one of them
/// validated path escape at all.
/// </para>
/// <para>
/// The point of the shape is that a case is written <b>once</b>, against the interface, and every
/// implementation inherits it. A case written against the fake alone passes and says nothing about
/// the adapter the game actually runs on — which is the whole reason `23` §5 A8 exists, and the
/// precedent M1's ~10 further ports will follow.
/// </para>
/// </remarks>
public abstract class IContentSourcePortContractTests : IDisposable
{
    private bool _disposed;

    /// <summary>A source holding exactly the given documents, keyed by snapshot-relative path.</summary>
    protected abstract IContentSourcePort Create(IReadOnlyDictionary<string, string> documents);

    /// <summary>Writes (or replaces) a document behind a source this fixture created.</summary>
    protected abstract void Write(IContentSourcePort source, string documentPath, string utf8Text);

    /// <summary>Releases whatever the fixture allocated.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            Dispose(disposing: true);
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------------------- listing

    [Fact]
    public void ListDocuments_returns_every_document_the_source_holds()
    {
        var source = Create(Three);

        source.ListDocuments().Should().BeEquivalentTo(Three.Keys);
    }

    /// <summary>
    /// 🔒 <b>Ordinal</b>, not culture-aware and not "whatever the filesystem returned". The
    /// loader's determinism is stated over this order, and the stamp is stated over the loader.
    /// </summary>
    /// <remarks>
    /// The hyphen is the discriminator: ordinal sorts <c>a-b</c> before <c>ab</c> because
    /// <c>'-'</c> &lt; <c>'b'</c>, while the invariant <em>culture</em> comparer treats the hyphen
    /// as ignorable punctuation and puts <c>ab</c> first. Case cannot be used for this — a
    /// case-insensitive filesystem cannot hold both spellings, so it would test the platform
    /// rather than the contract.
    /// </remarks>
    [Fact]
    public void ListDocuments_is_ordinal_sorted()
    {
        var source = Create(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tuning/ab.json"] = "{}",
            ["schema/a.schema.json"] = "{}",
            ["loc/en.json"] = "{}",
            ["tuning/a-b.json"] = "{}",
        });

        var listed = source.ListDocuments();

        listed.Should().BeInAscendingOrder(StringComparer.Ordinal);
        listed.Should().ContainInOrder(
            "loc/en.json", "schema/a.schema.json", "tuning/a-b.json", "tuning/ab.json");
    }

    [Fact]
    public void ListDocuments_over_an_empty_source_is_empty_rather_than_null()
    {
        Create(new Dictionary<string, string>(StringComparer.Ordinal)).ListDocuments().Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------- reading

    [Fact]
    public void ReadDocument_returns_the_exact_bytes_that_were_written()
    {
        var source = Create(Three);

        Encoding.UTF8.GetString(source.ReadDocument("tuning/a.json").Span)
                .Should().Be(Three["tuning/a.json"]);
    }

    [Fact]
    public void ReadDocument_reads_every_path_that_ListDocuments_offered()
    {
        var source = Create(Three);

        foreach (var path in source.ListDocuments())
        {
            source.ReadDocument(path).Length.Should().BeGreaterThan(0, $"'{path}' was listed");
        }
    }

    /// <summary>
    /// 🔒 The <b>same declared exception type</b> from every implementation. The port names
    /// <see cref="MissingContentException"/>; an implementation that throws its own favourite type
    /// makes every caller written against another one wrong.
    /// </summary>
    [Fact]
    public void ReadDocument_of_a_path_the_source_does_not_list_throws_the_declared_exception()
    {
        var source = Create(Three);

        var act = () => source.ReadDocument("tuning/nope.json");

        act.Should().Throw<MissingContentException>()
           .Which.Reference.Should().Be("tuning/nope.json");
    }

    /// <summary>
    /// 🔒 A document path is a key inside the content set, never a way out of it — and it fails the
    /// same way as any other unlisted path. Only one implementation checked this at all before this
    /// suite existed, so a traversal that the real adapter rejected was silently fine on the fake.
    /// </summary>
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("tuning/../../outside.json")]
    public void ReadDocument_of_a_path_that_escapes_the_content_set_throws_the_declared_exception(string path)
    {
        var source = Create(Three);

        var act = () => source.ReadDocument(path);

        act.Should().Throw<MissingContentException>();
    }

    [Fact]
    public void ReadDocument_of_an_empty_path_is_an_argument_fault_not_a_missing_document()
    {
        var source = Create(Three);

        var act = () => source.ReadDocument(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // --------------------------------------------------------------------------- revision

    [Fact]
    public void Revision_does_not_move_while_the_content_does_not()
    {
        var source = Create(Three);
        var before = source.Revision;

        source.Revision.Should().Be(before);
        source.ListDocuments();
        source.ReadDocument("tuning/a.json");

        source.Revision.Should().Be(before, "reading content is not changing it");
    }

    [Fact]
    public void Revision_moves_when_a_document_changes()
    {
        var source = Create(Three);
        var before = source.Revision;

        Write(source, "tuning/a.json", """{ "a": 2, "padding": "so the length moves too" }""");

        source.Revision.Should().NotBe(before);
    }

    [Fact]
    public void Revision_moves_when_a_document_is_added()
    {
        var source = Create(Three);
        var before = source.Revision;

        Write(source, "tuning/d.json", """{ "d": 4 }""");

        source.Revision.Should().NotBe(before);
    }

    /// <summary>Three documents spanning the three directories the layout distinguishes.</summary>
    protected static IReadOnlyDictionary<string, string> Three { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tuning/a.json"] = """{ "a": 1 }""",
            ["schema/a.schema.json"] = """{ "type": "object" }""",
            ["loc/en.json"] = """{ "_locale": "en" }""",
        };
}
