using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `14` §6 — the version stamp. It has to be a hash of the content, it has to be the same hash
/// everywhere, and it has to move when anything that could change an outcome changes.
/// </summary>
public sealed class ContentHashingTests
{
    private static ContentDocument Doc(string path, params (string Name, ContentValue Value)[] members) =>
        new(path, ContentValue.Object(members.Select(m => new KeyValuePair<string, ContentValue>(m.Name, m.Value))));

    [Fact]
    public void Compute_produces_a_sixty_four_character_lowercase_hex_stamp()
    {
        var version = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1m)))]);

        version.Value.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_is_stable_across_calls()
    {
        var documents = new[] { Doc("tuning/a.json", ("x", ContentValue.Number(1m))) };

        ContentHashing.Compute(documents).ShouldBe(ContentHashing.Compute(documents));
    }

    [Fact]
    public void Compute_ignores_the_order_documents_are_supplied_in()
    {
        var a = Doc("tuning/a.json", ("x", ContentValue.Number(1m)));
        var b = Doc("tuning/b.json", ("y", ContentValue.Number(2m)));

        ContentHashing.Compute([a, b]).ShouldBe(ContentHashing.Compute([b, a]));
    }

    [Fact]
    public void Compute_moves_when_a_value_changes()
    {
        var before = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1m)))]);
        var after = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(2m)))]);

        after.ShouldNotBe(before);
    }

    [Fact]
    public void Compute_moves_when_a_key_is_renamed()
    {
        var before = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1m)))]);
        var after = ContentHashing.Compute([Doc("tuning/a.json", ("y", ContentValue.Number(1m)))]);

        after.ShouldNotBe(before);
    }

    [Fact]
    public void Compute_moves_when_a_document_is_renamed()
    {
        var before = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1m)))]);
        var after = ContentHashing.Compute([Doc("tuning/b.json", ("x", ContentValue.Number(1m)))]);

        after.ShouldNotBe(before);
    }

    [Fact]
    public void Compute_moves_when_a_document_is_added()
    {
        var a = Doc("tuning/a.json", ("x", ContentValue.Number(1m)));
        var b = Doc("tuning/b.json", ("x", ContentValue.Number(1m)));

        ContentHashing.Compute([a, b]).ShouldNotBe(ContentHashing.Compute([a]));
    }

    [Fact]
    public void Compute_distinguishes_an_unauthorised_leaf_from_a_zero()
    {
        var unauthorised = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Unauthorised))]);
        var zero = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(0m)))]);

        unauthorised.ShouldNotBe(zero);
    }

    [Fact]
    public void Compute_distinguishes_a_number_from_the_same_digits_as_text()
    {
        var number = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1m)))]);
        var text = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Text("1")))]);

        number.ShouldNotBe(text);
    }

    [Fact]
    public void Compute_treats_trailing_zeroes_on_a_number_as_the_same_number()
    {
        var plain = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1.5m)))]);
        var padded = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1.500m)))]);

        padded.ShouldBe(plain);
    }

    [Fact]
    public void Compute_moves_when_two_array_items_swap_places()
    {
        var ascending = new ContentDocument("tuning/a.json",
            ContentValue.Array([ContentValue.Number(1m), ContentValue.Number(2m)]));
        var descending = new ContentDocument("tuning/a.json",
            ContentValue.Array([ContentValue.Number(2m), ContentValue.Number(1m)]));

        ContentHashing.Compute([descending]).ShouldNotBe(ContentHashing.Compute([ascending]));
    }

    [Fact]
    public void Compute_cannot_be_fooled_by_a_key_whose_name_looks_like_a_neighbouring_value()
    {
        var left = ContentHashing.Compute([Doc("tuning/a.json", ("ab", ContentValue.Text("c")))]);
        var right = ContentHashing.Compute([Doc("tuning/a.json", ("a", ContentValue.Text("bc")))]);

        right.ShouldNotBe(left,
            "the canonical encoding length-prefixes every string, so concatenation cannot collide");
    }

    [Fact]
    public void CanonicalBytes_begins_with_the_format_version_so_an_encoding_change_is_never_silent()
    {
        var bytes = ContentHashing.CanonicalBytes([Doc("tuning/a.json", ("x", ContentValue.Number(1m)))]);

        bytes[0].ShouldBe(ContentHashing.CanonicalFormatVersion);
    }

    [Fact]
    public void The_stamp_of_the_real_data_set_is_the_same_on_every_load()
    {
        var first = ContentLoader.Load(RepoData.Source()).Require().Version;
        var second = ContentLoader.Load(RepoData.Source()).Require().Version;

        second.ShouldBe(first);
    }

    // -------------------------------------- 🔒 the two decimal decisions, which had no test

    /// <summary>
    /// 🔒 <c>ContentValue</c> holds numbers as <see cref="decimal"/> so the load path never rounds.
    /// Nothing asserted it: refactoring <c>_number</c> to <c>double</c> keeps every other test in
    /// the repository green while <c>AsNumber()</c> starts answering
    /// <c>0.30000000000000004</c> — and a tuning number that shifts in the last place shifts every
    /// stamp with it.
    /// </summary>
    [Fact]
    public void A_sum_of_two_shipped_style_rates_is_exact_because_the_load_path_never_rounds()
    {
        var sum = ContentValue.Number(0.1m).AsNumber() + ContentValue.Number(0.2m).AsNumber();

        sum.ShouldBe(0.3m);
        ContentValue.Number(sum).ShouldBe(ContentValue.Number(0.3m));
    }

    /// <summary>
    /// 🔒 <em>"1.5 and 1.500 are one number."</em> Value equality, not representation equality —
    /// the decision <c>ContentValue.Equals</c> and <c>GetHashCode</c> both state in comments and
    /// neither had a case. The duplicate-id gate now groups on this, so it is load-bearing twice.
    /// </summary>
    [Fact]
    public void The_same_number_written_at_two_scales_is_one_value_and_one_hash_code()
    {
        ContentValue.Number(1.5m).ShouldBe(ContentValue.Number(1.500m));

        ContentValue.Number(1.500m).GetHashCode()
                    .ShouldBe(ContentValue.Number(1.5m).GetHashCode());
    }

    /// <summary>And the stamp agrees with equality: the canonical encoding normalises the scale.</summary>
    [Fact]
    public void The_same_number_written_at_two_scales_stamps_identically()
    {
        var left = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1.5m)))]);
        var right = ContentHashing.Compute([Doc("tuning/a.json", ("x", ContentValue.Number(1.500m)))]);

        right.ShouldBe(left);
    }
}
