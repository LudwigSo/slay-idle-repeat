namespace SlayIdleRepeat.BalanceHarness.Guardrails;

/// <summary>How a guardrail came out.</summary>
public enum GuardrailVerdict
{
    /// <summary>Every subject satisfied the assertion, and there was at least one subject.</summary>
    Pass,

    /// <summary>At least one subject breached it.</summary>
    Fail,

    /// <summary>
    /// The assertion could not be evaluated — there was nothing to evaluate it over. Not a pass: an
    /// empty subject set satisfies "every X is Y" vacuously, so <c>assert</c> exits non-zero on this
    /// verdict for the same reason it does on <see cref="Fail"/>.
    /// </summary>
    Inconclusive,
}

/// <summary>One guardrail's outcome, with the numbers behind it.</summary>
/// <param name="Number">The authored guardrail number. Guardrail 2 belongs elsewhere and never appears here.</param>
/// <param name="Name">The guardrail's statement.</param>
/// <param name="Verdict">Pass, fail, or not measurable.</param>
/// <param name="Summary">One line: what was measured and what the extreme was.</param>
/// <param name="Details">Per-subject lines — the breaches first, then whatever the report needs.</param>
/// <param name="SubjectCount">
/// How many subjects the assertion actually ran over. A guardrail that swept nothing must not pass.
/// </param>
public sealed record GuardrailResult(
    int Number,
    string Name,
    GuardrailVerdict Verdict,
    string Summary,
    IReadOnlyList<string> Details,
    int SubjectCount)
{
    /// <summary>True only for <see cref="GuardrailVerdict.Pass"/>. Inconclusive is not a pass.</summary>
    public bool Passed => Verdict == GuardrailVerdict.Pass;
}
