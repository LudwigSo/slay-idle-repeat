using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C9 — steering rule S6: <em>"If the design docs do not authorise a number, leave it absent and
/// greppable (null) … Never coerce such a hole to a default at read time; fail loudly."</em>
/// </summary>
public sealed class ThresholdSetTests
{
    /// <summary>The seventeen holes `15` leaves in the `15` §B4 pipeline and Part F checklist.</summary>
    private const int UncalibratedKeyCount = 17;

    /// <summary>The one member of the shipped file that is not a threshold.</summary>
    private const string DocMember = "_doc";

    /// <summary>Every key, one theory case each.</summary>
    public static TheoryData<string> EveryUncalibratedKey()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdSet.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>
    /// 🔒 The message must name the key, not merely be of the right type. Seventeen holes can throw
    /// the same exception, and "an uncalibrated threshold stopped the batch" tells a reader nothing
    /// about which of the seventeen to go and measure (steering rule S2).
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryUncalibratedKey))]
    public void Require_throws_naming_the_key_when_15_authorises_no_value_for_it(string key)
    {
        var thresholds = ThresholdSet.Uncalibrated();

        var exception = Should.Throw<UncalibratedThresholdException>(() => thresholds.Require(key));

        exception.Key.ShouldBe(key);
        exception.Message.ShouldContain(key, Case.Sensitive);
    }

    [Fact]
    public void The_register_of_uncalibrated_keys_holds_exactly_seventeen()
    {
        ThresholdSet.Keys.Count.ShouldBe(UncalibratedKeyCount);
        ThresholdSet.Keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(UncalibratedKeyCount);
    }

    /// <summary>
    /// 🔒 Floored at seventeen in both directions. A file that silently lost a key would leave that
    /// hole invisible instead of greppable, which is the exact failure S6 exists to prevent.
    /// </summary>
    [Fact]
    public void The_shipped_file_holds_every_key_and_every_value_is_null()
    {
        using var document = JsonDocument.Parse(PipelineFiles.ThresholdsJson());
        var thresholds = document.RootElement.EnumerateObject()
            .Where(member => !string.Equals(member.Name, DocMember, StringComparison.Ordinal))
            .ToArray();

        thresholds.Length.ShouldBe(UncalibratedKeyCount);
        thresholds.Select(member => member.Name).ToArray()
            .ShouldBe(ThresholdSet.Keys.ToArray(), ignoreOrder: true);
        thresholds.Select(member => member.Value.ValueKind).ToArray()
            .ShouldAllBe(kind => kind == JsonValueKind.Null);
    }

    [Fact]
    public void The_shipped_file_says_why_every_value_is_null_and_who_could_change_that()
    {
        using var document = JsonDocument.Parse(PipelineFiles.ThresholdsJson());

        var block = document.RootElement.GetProperty(DocMember).GetRawText();

        block.ShouldContain("Never fill a hole with a plausible value", Case.Sensitive);
        block.ShouldContain("M8-10", Case.Sensitive);
    }

    [Fact]
    public void Loading_the_shipped_file_yields_a_set_in_which_nothing_is_calibrated()
    {
        var thresholds = ThresholdSet.LoadFrom(PipelineFiles.ThresholdsJson());

        var calibrated = ThresholdSet.Keys.Where(thresholds.IsCalibrated).ToArray();

        calibrated.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A caller stating a value is a decision recorded at the call site; it must not become a
    /// default for anything else. This case pins the second half of that: stating one key leaves
    /// the other sixteen throwing, and the exception still names the right one.
    /// </summary>
    [Fact]
    public void A_value_a_caller_states_is_readable_and_calibrates_nothing_else()
    {
        var thresholds = ThresholdSet.Uncalibrated()
            .With(ThresholdKeys.BackgroundKeyTolerance, 12d);

        thresholds.RequireNumber(ThresholdKeys.BackgroundKeyTolerance).ShouldBe(12d);
        Should.Throw<UncalibratedThresholdException>(
                () => thresholds.Require(ThresholdKeys.MatteDecontaminationStrength))
            .Key.ShouldBe(ThresholdKeys.MatteDecontaminationStrength);
    }

    /// <summary>
    /// `15` §A5 says "+ neutrals" and never enumerates them, so the one list-valued hole is read as
    /// a list and throws by the same rule as the sixteen numeric ones.
    /// </summary>
    [Fact]
    public void The_unenumerated_15_A5_neutrals_are_a_colour_list_and_throw_like_the_rest()
    {
        var stated = ThresholdSet.Uncalibrated()
            .WithColours(ThresholdKeys.PaletteNeutrals, ["#FFFFFF", "#000000"]);

        stated.RequireColours(ThresholdKeys.PaletteNeutrals)
            .ShouldBe(new[] { "#FFFFFF", "#000000" });
        Should.Throw<UncalibratedThresholdException>(
                () => ThresholdSet.Uncalibrated().RequireColours(ThresholdKeys.PaletteNeutrals))
            .Key.ShouldBe(ThresholdKeys.PaletteNeutrals);
    }
}
