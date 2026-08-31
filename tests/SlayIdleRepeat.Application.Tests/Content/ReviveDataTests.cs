using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The shipped <c>tuning/ads.json</c> values `16` D45 rules: the revive heals 66% of Max HP, once
/// per run.
/// </summary>
/// <remarks>
/// The Core handler suites read <c>InRunIncomeDocuments</c>' transcription of this document, so a
/// drifted shipped value would leave every handler test green while the game heals a different
/// number — proven by mutating the shipped 0.66 to 0.5 and watching nothing go red before this
/// suite existed. This is the pin on the file itself.
/// </remarks>
public sealed class ReviveDataTests
{
    private const string Document = "tuning/ads.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_revive_heal_share_is_the_D45_66_percent()
    {
        Data().ReadDouble($"{Document}#/placementRewardValues/AD_REVIVE/healPctMaxHp").ShouldBe(
            0.66, "16 D45 overrides 02 §6's authored 50%");
    }

    [Fact]
    public void The_revive_is_capped_at_once_per_run()
    {
        var placements = Data().Read($"{Document}#/inRunPlacements").Items;
        var revive = placements.Single(p =>
        {
            p.TryGetMember("id", out var id).ShouldBeTrue();
            return id!.AsText() == "AD_REVIVE";
        });

        revive.TryGetMember("cap", out var cap).ShouldBeTrue();
        revive.TryGetMember("capWindow", out var window).ShouldBeTrue();

        cap!.AsInt32().ShouldBe(1, "D45's 'first death only' is this cap");
        window!.AsText().ShouldBe("RUN");
    }
}
