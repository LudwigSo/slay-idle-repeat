using Shouldly;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The account-creation display-name policy over the shipped rules: one filter for one field, and
/// the default an account that chose no name carries.
/// </summary>
public sealed class HeroNameDisplayNamePolicyTests
{
    private static readonly Lazy<ContentSnapshot> Content = new(
        () => ContentLoader.Load(new LocalFileContentSource(FindGameData())).Require());

    private static HeroNameDisplayNamePolicy Policy() => new(Content.Value);

    [Fact]
    public void Decide_names_an_account_that_supplied_no_name_Wanderer()
    {
        var decision = Policy().Decide(null);

        decision.Refusal.ShouldBeNull(
            "the shipped default must pass the same gate a player's own choice does; a default the " +
            "filter refuses would name every unnamed account something the game itself rejects.");
        decision.Name.ShouldBe("Wanderer");
    }

    [Fact]
    public void Decide_accepts_a_plain_name()
    {
        var decision = Policy().Decide("Rogue");

        decision.Refusal.ShouldBeNull();
        decision.Name.ShouldBe("Rogue", "an accepted name is stored exactly as it was typed.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Decide_refuses_a_blank_name_the_caller_actually_sent(string candidate)
    {
        Policy().Decide(candidate).Refusal.ShouldBe(
            HeroNameRefusal.BLANK,
            "a supplied empty string is a name the caller chose, not an absent one — the absent " +
            "case is null and takes the default.");
    }

    [Fact]
    public void Decide_refuses_a_name_past_the_authored_limit()
    {
        Policy().Decide(new string('n', 13)).Refusal.ShouldBe(HeroNameRefusal.TOO_LONG);
    }

    [Fact]
    public void Decide_accepts_a_name_exactly_at_the_authored_limit()
    {
        // The negative control for the length arm: twelve is allowed, thirteen is not.
        Policy().Decide(new string('n', 12)).Refusal.ShouldBeNull();
    }

    [Theory]
    [InlineData("Ro\u0007gue")]
    [InlineData("Ro\u200Bgue")]
    [InlineData("Ro\u202Egue")]
    public void Decide_refuses_a_name_carrying_an_invisible_character(string candidate)
    {
        Policy().Decide(candidate).Refusal.ShouldBe(
            HeroNameRefusal.DISALLOWED_CHARACTER,
            "a control or format character renders as nothing and can reorder or truncate the text " +
            "around it on every screen a name appears on.");
    }

    /// <summary>The repo's <c>game-data</c>, found upward from the output directory.</summary>
    private static string FindGameData()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "game-data");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No game-data directory above " + AppContext.BaseDirectory + " — this suite reads the shipped content set.");
    }
}
