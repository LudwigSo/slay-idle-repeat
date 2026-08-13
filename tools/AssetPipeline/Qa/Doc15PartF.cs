namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// `15` Part F's eleven checklist lines, verbatim, in Part F's order.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One home for the text.</b> Each <see cref="IQaCheck"/> takes its
/// <see cref="IQaCheck.ChecklistText"/> from here rather than retyping the line, so there is
/// exactly one place a wording can drift from the doc and exactly one place a test has to watch.
/// <c>QaChecklistTests</c> reconciles these constants against
/// <c>game-design/15_ART_DIRECTION_AND_ASSET_MANIFEST.md</c> itself, character for character, so
/// an edit to the doc fails the build rather than quietly leaving the code describing an older
/// checklist.
/// </para>
/// <para>
/// 🔒 <b>"Verbatim" here means: Part F's line with its Markdown removed and nothing else.</b> The
/// doc writes each item as an unchecked task-list row, so the leading <c>- [ ] </c> is dropped, and
/// item 3 wraps its colour in a Markdown code fence, so the backticks around <c>#231A2E</c> are
/// dropped. Everything else is the doc's own bytes, including the em dash in item 6, the en dash in
/// item 9, and the section signs in items 7 and 10. Retyping any of those as an ASCII hyphen is a
/// silent divergence from the doc, which is why the reconciliation case compares ordinally.
/// </para>
/// </remarks>
public static class Doc15PartF
{
    /// <summary>The number of items `15` Part F lists. It is eleven and it does not shrink.</summary>
    public const int ItemCount = 11;

    /// <summary>Part F item 1.</summary>
    public const string Item1 = "Silhouette test passed at 64 px (characters)";

    /// <summary>Part F item 2.</summary>
    public const string Item2 = "Readable at the smallest in-game display size";

    /// <summary>Part F item 3.</summary>
    public const string Item3 = "Outline continuous, uniform width, colour #231A2E";

    /// <summary>Part F item 4.</summary>
    public const string Item4 = "Key light from upper left, consistent with the batch";

    /// <summary>Part F item 5.</summary>
    public const string Item5 = "Palette conforms to the biome's locked six colours + neutrals";

    /// <summary>Part F item 6.</summary>
    public const string Item6 = "Alpha is clean — no white/black halo, no semi-transparent fringe";

    /// <summary>Part F item 7.</summary>
    public const string Item7 = "Correct canvas size and pivot per §C";

    /// <summary>Part F item 8.</summary>
    public const string Item8 = "No text, watermark or signature anywhere in the image";

    /// <summary>Part F item 9.</summary>
    public const string Item9 = "Proportions match the Style Anchor Sheet (2.5–3 heads)";

    /// <summary>Part F item 10.</summary>
    public const string Item10 = "File named per §D1 and packed into the correct atlas";

    /// <summary>Part F item 11.</summary>
    public const string Item11 =
        "Side-by-side comparison against 3 previously-approved assets in the same category shows " +
        "no style drift";

    /// <summary>The eleven lines, in Part F's order. Index 0 is item 1.</summary>
    public static IReadOnlyList<string> Items { get; } =
    [
        Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10, Item11,
    ];

    /// <summary>
    /// `15` §A4's acceptance sentence, verbatim — the half of item 1 no measurement performs.
    /// </summary>
    public const string SilhouetteAcceptanceSentence =
        "If you cannot tell which character it is, regenerate it.";
}
