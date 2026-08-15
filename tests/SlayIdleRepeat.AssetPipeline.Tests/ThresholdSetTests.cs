using System.Globalization;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>An unauthorised threshold must stay absent and greppable (null), and never be coerced to a default at read time.</summary>
public sealed class ThresholdSetTests
{
    private const int UncalibratedKeyCount = 17;

    /// <summary>The one member of the shipped file that is not a threshold.</summary>
    private const string DocMember = "_doc";

    public static TheoryData<string> EveryUncalibratedKey()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdSet.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>The message must name the key, not merely be of the right type — "an uncalibrated threshold" alone tells a reader nothing.</summary>
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

    /// <summary>A file that silently lost a key would leave that hole invisible instead of greppable.</summary>
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

    /// <summary>"Nothing is calibrated" is also true of an implementation that reads nothing at all, so this needs the positive case too.</summary>
    [Fact]
    public void Loading_a_file_in_which_one_hole_is_filled_reports_that_one_key_and_no_other()
    {
        // 12 is this case's own stated value for a synthetic file; any number would do here.
        const double stated = 12d;
        var json = JsonWithOneValue(ThresholdKeys.BackgroundKeyTolerance, stated);

        var thresholds = ThresholdSet.LoadFrom(json);

        ThresholdSet.Keys.Where(thresholds.IsCalibrated).ToArray()
            .ShouldBe([ThresholdKeys.BackgroundKeyTolerance]);
        thresholds.RequireNumber(ThresholdKeys.BackgroundKeyTolerance).ShouldBe(stated);
        Should.Throw<UncalibratedThresholdException>(
                () => thresholds.Require(ThresholdKeys.MatteDecontaminationStrength))
            .Key.ShouldBe(ThresholdKeys.MatteDecontaminationStrength);
    }

    /// <summary>
    /// A <see cref="ThresholdSet.ThresholdsPath"/>-shaped document built from
    /// <see cref="ThresholdSet.Keys"/> — so it cannot drift from the register — with every value
    /// null except one.
    /// </summary>
    /// <param name="key">The one key to give a value.</param>
    /// <param name="value">The value to give it.</param>
    private static string JsonWithOneValue(string key, double value)
    {
        // The shipped file's shape, `_doc` block and all, so the case exercises what LoadFrom
        // actually meets rather than a stripped-down document only this case ever produces.
        var members = ThresholdSet.Keys
            .Select(name => string.Equals(name, key, StringComparison.Ordinal)
                ? $"  {JsonSerializer.Serialize(name)}: {value.ToString(CultureInfo.InvariantCulture)}"
                : $"  {JsonSerializer.Serialize(name)}: null")
            .Prepend($"  {JsonSerializer.Serialize(DocMember)}: " +
                     JsonSerializer.Serialize("A synthetic register built by ThresholdSetTests."));

        return $"{{{Environment.NewLine}{string.Join($",{Environment.NewLine}", members)}{Environment.NewLine}}}";
    }

    /// <summary>A caller stating a value must not become a default for anything else: the other sixteen keys keep throwing.</summary>
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

    /// <summary>The one list-valued hole is read as a list and throws by the same rule as the sixteen numeric ones.</summary>
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
