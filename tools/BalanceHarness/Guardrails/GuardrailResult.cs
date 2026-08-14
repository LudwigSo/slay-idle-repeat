namespace SlayIdleRepeat.BalanceHarness.Guardrails;

/// <summary>How a guardrail came out.</summary>
public enum GuardrailVerdict
{
    /// <summary>Every subject satisfied the assertion, and there was at least one subject.</summary>
    Pass,

    /// <summary>At least one subject breached it.</summary>
    Fail,

    /// <summary>
    /// 🔴 The assertion could not be evaluated — there was nothing to evaluate it over.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Not a pass, and it must never be reported as one.</b> Guardrails 3 and 4 are stated over
    /// <em>cleared</em> fights, and on the shipped data no cell clears anything at par: an empty set
    /// satisfies "every clear is above 12 s" vacuously, which is the exact shape of assertion that
    /// stays green while the thing it guards is broken. <c>assert</c> exits non-zero on this verdict
    /// for the same reason it does on <see cref="Fail"/>.
    /// </remarks>
    Inconclusive,
}

/// <summary>One guardrail's outcome, with the numbers behind it.</summary>
/// <param name="Number">🔒 `05` §9's own numbering. Guardrail 2 is M2-16b/M3-07's and never appears here.</param>
/// <param name="Name">The guardrail as `05` §9 states it.</param>
/// <param name="Verdict">Pass, fail, or not measurable.</param>
/// <param name="Summary">One line: what was measured and what the extreme was.</param>
/// <param name="Details">Per-subject lines — the breaches first, then whatever the report needs.</param>
/// <param name="SubjectCount">
/// 🔒 How many subjects the assertion actually ran over. A guardrail that swept nothing must not
/// pass, so this is asserted against the expected count rather than trusted.
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
