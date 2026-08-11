using NetArchTest.Rules;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// Turns a rule violation into a message that names the offender.
/// A failure that reads "expected true but found false" costs the next engineer
/// an hour; a failure that names the type, project or API costs them a minute.
/// </summary>
internal static class ArchRule
{
    /// <summary>Asserts a NetArchTest result, listing <c>FailingTypeNames</c> on failure.</summary>
    internal static void Assert(TestResult result, string rule)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypeNames ?? Array.Empty<string>();
        throw new ArchitectureRuleViolationException(rule, offenders);
    }

    /// <summary>Asserts that a set of offenders is empty, listing them on failure.</summary>
    internal static void Empty(IEnumerable<string> offenders, string rule)
    {
        var list = offenders.Distinct(StringComparer.Ordinal)
                            .OrderBy(o => o, StringComparer.Ordinal)
                            .ToArray();

        if (list.Length == 0)
        {
            return;
        }

        throw new ArchitectureRuleViolationException(rule, list);
    }
}

/// <summary>An architecture rule violation, rendered with the offenders named.</summary>
internal sealed class ArchitectureRuleViolationException : Exception
{
    internal ArchitectureRuleViolationException(string rule, IEnumerable<string> offenders)
        : base(Render(rule, offenders))
    {
    }

    private static string Render(string rule, IEnumerable<string> offenders)
    {
        var list = offenders.ToArray();
        var lines = string.Join(Environment.NewLine, list.Select(o => "  - " + o));
        return $"ARCHITECTURE RULE VIOLATED: {rule}{Environment.NewLine}" +
               $"{list.Length} offender(s):{Environment.NewLine}{lines}";
    }
}
