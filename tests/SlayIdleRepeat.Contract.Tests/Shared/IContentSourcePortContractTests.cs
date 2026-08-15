using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// States what <see cref="IContentSourcePort"/> <em>means</em>, run against every implementation
/// including the fake — written once against the interface so every implementation inherits it,
/// instead of each adapter drifting on its own (as the two already had: differing exception types
/// for an unlisted/escaping path before this suite existed).
/// </summary>
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

        source.ListDocuments().ShouldBe(Three.Keys, ignoreOrder: true);
    }

    /// <summary>
    /// Ordinal order, not culture-aware: the hyphen in <c>a-b</c> sorts before <c>ab</c> under
    /// ordinal comparison but after it under the invariant-culture comparer, which treats hyphens
    /// as ignorable punctuation.
    /// </summary>
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

        listed.ShouldBeInOrder(SortDirection.Ascending, StringComparer.Ordinal);

        // Ordered subsequence, not equality: every expected path appears, each after the previous.
        var expectedInOrder = new[]
        {
            "loc/en.json", "schema/a.schema.json", "tuning/a-b.json", "tuning/ab.json",
        };
        var remaining = new Queue<string>(expectedInOrder);
        foreach (var path in listed)
        {
            if (remaining.Count > 0 && string.Equals(path, remaining.Peek(), StringComparison.Ordinal))
            {
                remaining.Dequeue();
            }
        }

        remaining.ShouldBeEmpty(
            $"[{string.Join(", ", listed)}] does not contain "
            + $"[{string.Join(", ", expectedInOrder)}] in that order");
    }

    [Fact]
    public void ListDocuments_over_an_empty_source_is_empty_rather_than_null()
    {
        Create(new Dictionary<string, string>(StringComparer.Ordinal)).ListDocuments().ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------- reading

    [Fact]
    public void ReadDocument_returns_the_exact_bytes_that_were_written()
    {
        var source = Create(Three);

        Encoding.UTF8.GetString(source.ReadDocument("tuning/a.json").Span)
                .ShouldBe(Three["tuning/a.json"]);
    }

    [Fact]
    public void ReadDocument_reads_every_path_that_ListDocuments_offered()
    {
        var source = Create(Three);

        foreach (var path in source.ListDocuments())
        {
            source.ReadDocument(path).Length.ShouldBeGreaterThan(0, $"'{path}' was listed");
        }
    }

    /// <summary>The same declared exception type from every implementation, not each adapter's own favourite.</summary>
    [Fact]
    public void ReadDocument_of_a_path_the_source_does_not_list_throws_the_declared_exception()
    {
        var source = Create(Three);

        Action act = () => _ = source.ReadDocument("tuning/nope.json");

        Should.Throw<MissingContentException>(act)
            .Reference.ShouldBe("tuning/nope.json");
    }

    /// <summary>A path that escapes the content set fails the same way as any other unlisted path.</summary>
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("tuning/../../outside.json")]
    public void ReadDocument_of_a_path_that_escapes_the_content_set_throws_the_declared_exception(string path)
    {
        var source = Create(Three);

        Action act = () => _ = source.ReadDocument(path);

        Should.Throw<MissingContentException>(act);
    }

    [Fact]
    public void ReadDocument_of_an_empty_path_is_an_argument_fault_not_a_missing_document()
    {
        var source = Create(Three);

        Action act = () => _ = source.ReadDocument(string.Empty);

        Should.Throw<ArgumentException>(act);
    }

    // --------------------------------------------------------------------------- revision

    [Fact]
    public void Revision_does_not_move_while_the_content_does_not()
    {
        var source = Create(Three);
        var before = source.Revision;

        source.Revision.ShouldBe(before);
        source.ListDocuments();
        source.ReadDocument("tuning/a.json");

        source.Revision.ShouldBe(before, "reading content is not changing it");
    }

    [Fact]
    public void Revision_moves_when_a_document_changes()
    {
        var source = Create(Three);
        var before = source.Revision;

        Write(source, "tuning/a.json", """{ "a": 2, "padding": "so the length moves too" }""");

        source.Revision.ShouldNotBe(before);
    }

    [Fact]
    public void Revision_moves_when_a_document_is_added()
    {
        var source = Create(Three);
        var before = source.Revision;

        Write(source, "tuning/d.json", """{ "d": 4 }""");

        source.Revision.ShouldNotBe(before);
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
