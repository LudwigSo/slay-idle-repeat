using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

public sealed class IconCatalogueStatusTests
{
    private const string ClientDirectory = "src/SlayIdleRepeat.Client";

    /// <summary>The thirteen status ordinals the log carries, in the order the rules layer numbers them.</summary>
    public static TheoryData<ushort, string> Statuses() =>
        new()
        {
            { 1, "burn" }, { 2, "poison" }, { 3, "bleed" }, { 4, "freeze" }, { 5, "stun" },
            { 6, "weaken" }, { 7, "sunder" }, { 8, "spore" }, { 9, "rage" }, { 10, "ward" },
            { 11, "haste" }, { 12, "regen" }, { 13, "chill" },
        };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Status_names_the_icon_of_each_of_the_thirteen_log_ordinals(ushort logId, string name) =>
        IconCatalogue.Status(logId).ShouldBe($"res://game/art/icons/icon_status_{name}.svg");

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)14)]
    [InlineData(ushort.MaxValue)]
    public void Status_answers_nothing_for_an_ordinal_outside_the_thirteen(ushort logId) =>
        IconCatalogue.Status(logId).ShouldBeNull();

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Status_names_an_icon_file_that_ships(ushort logId, string name)
    {
        var path = IconCatalogue.Status(logId).ShouldNotBeNull();

        File.Exists(LocalPathOf(path)).ShouldBeTrue($"{name} ({logId}) resolves to '{path}' and no such file ships.");
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Every_status_icon_is_flat_svg(ushort logId, string name)
    {
        var path = IconCatalogue.Status(logId).ShouldNotBeNull();
        var local = LocalPathOf(path);

        File.Exists(local).ShouldBeTrue($"{name} ({logId}) resolves to '{path}' and no such file ships.");

        var svg = File.ReadAllText(local);

        svg.ShouldContain("<svg", Case.Sensitive);
        svg.ShouldContain("viewBox", Case.Sensitive, "an icon with no viewBox does not scale to the plate's row.");
        svg.ShouldNotContain("Gradient", Case.Sensitive, "a status icon is solid fills at a few pixels tall.");
        svg.ShouldNotContain("<filter", Case.Sensitive);
        svg.ShouldNotContain("<image", Case.Sensitive);
        svg.ShouldNotContain("<foreignObject", Case.Sensitive);
        svg.ShouldNotContain("<script", Case.Sensitive);
    }

    private static string LocalPathOf(string resPath)
    {
        resPath.ShouldStartWith("res://");

        return Path.Combine(RepoPaths.RepositoryRoot, ClientDirectory, resPath["res://".Length..]);
    }
}
