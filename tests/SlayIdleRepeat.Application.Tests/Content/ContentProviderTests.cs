using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `14` §6 — <em>"In editor/dev builds, content hot-reloads without restarting."</em>
/// The property that matters is that reloading <b>replaces</b> the snapshot rather than editing
/// it: immutability has to survive hot-reload, or `14` §6's replay guarantee only holds until the
/// first reload.
/// </summary>
public sealed class ContentProviderTests
{
    private static ContentProvider DevProvider(Adapters.InMemory.InMemoryContentSource source) =>
        new(source, ContentLoadOptions.Canonical, ContentReloadPolicy.Enabled);

    [Fact]
    public void Current_is_the_snapshot_the_provider_loaded_at_construction()
    {
        var provider = DevProvider(ContentTestData.Valid());

        provider.Current.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(3);
    }

    [Fact]
    public void Constructing_a_provider_over_invalid_content_throws_rather_than_booting_on_it()
    {
        var source = ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": 99");

        var act = () => new ContentProvider(source, ContentLoadOptions.Canonical, ContentReloadPolicy.Disabled);

        act.Should().Throw<ContentLoadException>();
    }

    [Fact]
    public void Reload_returns_a_snapshot_reflecting_the_edited_source()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        source.Set(ContentTestData.TuningPath,
            ContentTestData.WidgetTuning.Replace("\"inputCount\": 3", "\"inputCount\": 4", StringComparison.Ordinal));

        var reloaded = provider.Reload();

        reloaded.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(4);
    }

    [Fact]
    public void Reload_swaps_in_a_new_snapshot_object_rather_than_mutating_the_old_one()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        var before = provider.Current;
        source.Set(ContentTestData.TuningPath,
            ContentTestData.WidgetTuning.Replace("\"inputCount\": 3", "\"inputCount\": 4", StringComparison.Ordinal));

        provider.Reload();

        provider.Current.Should().NotBeSameAs(before);
    }

    [Fact]
    public void Reload_leaves_a_snapshot_that_was_already_handed_out_completely_unchanged()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        var handedOut = provider.Current;
        var stampWhenHandedOut = handedOut.Version;
        source.Set(ContentTestData.TuningPath,
            ContentTestData.WidgetTuning.Replace("\"inputCount\": 3", "\"inputCount\": 4", StringComparison.Ordinal));

        provider.Reload();

        handedOut.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(3);
        handedOut.Version.Should().Be(stampWhenHandedOut);
    }

    [Fact]
    public void Reload_moves_the_version_stamp_when_the_content_changed()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        var before = provider.Current.Version;
        source.Set(ContentTestData.TuningPath,
            ContentTestData.WidgetTuning.Replace("\"inputCount\": 3", "\"inputCount\": 4", StringComparison.Ordinal));

        provider.Reload();

        provider.Current.Version.Should().NotBe(before);
    }

    [Fact]
    public void Reload_keeps_the_version_stamp_when_the_content_did_not_change()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        var before = provider.Current.Version;

        provider.Reload();

        provider.Current.Version.Should().Be(before);
    }

    [Fact]
    public void Reload_over_content_that_became_invalid_throws_and_keeps_the_last_good_snapshot()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        var lastGood = provider.Current;
        source.Set(ContentTestData.TuningPath,
            ContentTestData.WidgetTuning.Replace("\"inputCount\": 3", "\"inputCount\": 99", StringComparison.Ordinal));

        var act = () => provider.Reload();

        act.Should().Throw<ContentLoadException>();
        provider.Current.Should().BeSameAs(lastGood);
    }

    [Fact]
    public void Reload_is_refused_when_the_host_is_not_an_editor_or_dev_build()
    {
        var provider = new ContentProvider(
            ContentTestData.Valid(), ContentLoadOptions.Canonical, ContentReloadPolicy.Disabled);

        var act = () => provider.Reload();

        act.Should().Throw<ContentReloadNotPermittedException>();
    }

    [Fact]
    public void TryReloadIfChanged_does_nothing_when_the_source_revision_has_not_moved()
    {
        var provider = DevProvider(ContentTestData.Valid());
        var before = provider.Current;

        var reloaded = provider.TryReloadIfChanged(out var snapshot);

        reloaded.Should().BeFalse();
        snapshot.Should().BeSameAs(before);
    }

    [Fact]
    public void TryReloadIfChanged_rebuilds_when_the_source_revision_moved()
    {
        var source = ContentTestData.Valid();
        var provider = DevProvider(source);
        source.Set(ContentTestData.TuningPath,
            ContentTestData.WidgetTuning.Replace("\"inputCount\": 3", "\"inputCount\": 4", StringComparison.Ordinal));

        var reloaded = provider.TryReloadIfChanged(out var snapshot);

        reloaded.Should().BeTrue();
        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(4);
    }
}
