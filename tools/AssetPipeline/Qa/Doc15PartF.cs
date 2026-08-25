namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// `15` Part F's checklist lines, verbatim — the <see cref="CurrentItems"/> the doc lists today,
/// and the <see cref="SupersededItems"/> the nine built checks still implement.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One home for the text, and now two lists in it.</b> Each <see cref="IQaCheck"/> takes
/// its <see cref="IQaCheck.ChecklistText"/> from here rather than retyping the line, so a wording
/// can drift from the doc in exactly one place. <c>QaChecklistTests</c> reconciles
/// <see cref="CurrentItems"/> against
/// <c>game-design/15_ART_DIRECTION_AND_ASSET_MANIFEST.md</c> itself, character for character, so an
/// edit to Part F fails the build rather than quietly leaving this file describing an older
/// checklist. That reconciliation was deleted on 2026-08-25 and is restored.
/// </para>
/// <para>
/// 🔴 <b>The two lists do not agree, and the gap is the point.</b> D60 re-authored Part F for
/// real-time 3D: 26 items across 6 kind-tagged groups, where the pre-D60 checklist had
/// 11. Exactly 2 lines survive verbatim. The nine built checks measure PIXELS, which
/// still answers for kinds <b>R</b> (rendered from a model) and <b>F</b> (flat 2D) and answers nothing
/// for kind <b>M</b>, whose geometry, texturing and rig groups have no implementation at all.
/// <c>QaChecklistTests</c> pins that coverage as a number so it cannot drift unnoticed; raising it is
/// `16` D60 consequence 5's own task.
/// </para>
/// <para>
/// 🔒 <b>"Verbatim" means Part F's line with its Markdown removed and nothing else.</b> The doc
/// writes each item as an unchecked task-list row, so the leading <c>- [ ] </c> is dropped, and the
/// backticks around inline code are dropped. Everything else is the doc's own bytes — em dashes, en
/// dashes, section signs, the emphasis asterisks inside a kind tag. Retyping any of those as ASCII
/// is a silent divergence, which is why the reconciliation compares ordinally.
/// </para>
/// <para>
/// ⚠️ <b>Generated, not typed.</b> <c>Doc15PartF.generate.py</c>, beside this file, reads
/// Part F and emits it. Re-run it after a Part F edit rather than hand-patching the strings;
/// it is idempotent, and reads the superseded list back out of its own output.
/// </para>
/// </remarks>
public static class Doc15PartF
{
    /// <summary>The number of items `15` Part F lists today. It is 26.</summary>
    public const int CurrentItemCount = 26;

    /// <summary>The number of items Part F listed before D60, and the number of built checks.</summary>
    public const int SupersededItemCount = 11;

    /// <summary>How many superseded lines survive verbatim in the current Part F.</summary>
    /// <remarks>
    /// 🔴 A coverage number, not a target. It counts lines a built check could be re-pointed at
    /// without rewording anything — not items the pipeline decides correctly for a mesh.
    /// </remarks>
    public const int SurvivingVerbatimCount = 2;

    /// <summary>
    /// `15` §A4's acceptance sentence, verbatim — the half of the silhouette item no measurement
    /// performs.
    /// </summary>
    /// <remarks>
    /// 🔴 It read <i>"regenerate it"</i> until 2026-08-25, while §A4 already said <i>"remodel it"</i>
    /// — correct for a mesh. Nothing caught it, because unlike Part F this sentence was never
    /// reconciled against the doc (`16` D60 consequence 5b). It is reconciled now.
    /// </remarks>
    public const string SilhouetteAcceptanceSentence =
        "If you cannot tell which character it is, remodel it.";

    /// <summary>Part F group 1 — Silhouette and readability — M, R.</summary>
    private static readonly string[] Group1 =
    [
        "Silhouette test passed at 64 px from the §C6 camera, **on the block-out** (§A4)",
        "Silhouette still reads at every LOD level, including the last",
        "Readable at the smallest in-game display size",
        "Proportions match the Style Anchor Set (2.5–3 heads)",
    ];

    /// <summary>Part F group 2 — Style — M, R, F.</summary>
    private static readonly string[] Group2 =
    [
        "Outline continuous and uniform width, colour #231A2E, no interior outlines from hidden geometry",
        "Two-band cel ramp only — no PBR response, no smooth falloff, no shader AO",
        "Key light from upper left via the fixed §C6 rig; no scene light contribution",
        "Palette conforms to the biome's locked six colours + neutrals",
        "No text, watermark or signature in any mesh, texture or UV layout",
    ];

    /// <summary>Part F group 3 — Geometry and topology — M.</summary>
    private static readonly string[] Group3 =
    [
        "Manifold, watertight, consistent normals, **no interior faces**",
        "Within the §C2 triangle budget at LOD0, and LOD chain present per §C2",
        "Y-up, metres, origin at the feet, facing +Z, scale per §C1",
        "Retopologised onto the shared topology where the subject allowed it",
        "No plinth, base or ground plane geometry",
    ];

    /// <summary>Part F group 4 — Texturing — M, R.</summary>
    private static readonly string[] Group4 =
    [
        "Single non-overlapping UV set; texel density uniform within the asset (§C3)",
        "Within the §C3 texture budget; shares the group sheet where §D2 says it should",
        "Albedo quantised to the biome palette; AO baked where wanted and nowhere else",
    ];

    /// <summary>Part F group 5 — Rig and animation — M, actors only.</summary>
    private static readonly string[] Group5 =
    [
        "Bound to the correct shared skeleton (§C4); bone count within cap",
        "Deformation checked at the extremes of every clip — no pinching, no collapse",
        "Full shared clip set present and correctly named (§C5)",
        "Mounts: carry the hero mesh with no intersection at any clip frame",
    ];

    /// <summary>Part F group 6 — Delivery — all.</summary>
    private static readonly string[] Group6 =
    [
        ".glb passes a glTF validator with zero errors (*M, and R's source model*)",
        "Correct canvas size and pivot per the delivery table (*R, F*)",
        "Named per §D1 and grouped per §D2",
        "Production record written per §B5",
        "Side-by-side against 3 previously-approved assets in the same category through the same §C6 " +
            "rig shows no style drift",
    ];

    /// <summary>Part F's lines as the doc lists them today, in Part F's order. Index 0 is item 1.</summary>
    public static IReadOnlyList<string> CurrentItems { get; } =
    [
        .. Group1, .. Group2, .. Group3, .. Group4, .. Group5, .. Group6,
    ];

    /// <summary>Superseded Part F item 1 — what built check 1 implements.</summary>
    public const string SupersededItem1 =
        "Silhouette test passed at 64 px (characters)";

    /// <summary>Superseded Part F item 2 — what built check 2 implements.</summary>
    public const string SupersededItem2 =
        "Readable at the smallest in-game display size";

    /// <summary>Superseded Part F item 3 — what built check 3 implements.</summary>
    public const string SupersededItem3 =
        "Outline continuous, uniform width, colour #231A2E";

    /// <summary>Superseded Part F item 4 — what built check 4 implements.</summary>
    public const string SupersededItem4 =
        "Key light from upper left, consistent with the batch";

    /// <summary>Superseded Part F item 5 — what built check 5 implements.</summary>
    public const string SupersededItem5 =
        "Palette conforms to the biome's locked six colours + neutrals";

    /// <summary>Superseded Part F item 6 — what built check 6 implements.</summary>
    public const string SupersededItem6 =
        "Alpha is clean — no white/black halo, no semi-transparent fringe";

    /// <summary>Superseded Part F item 7 — what built check 7 implements.</summary>
    public const string SupersededItem7 =
        "Correct canvas size and pivot per §C";

    /// <summary>Superseded Part F item 8 — what built check 8 implements.</summary>
    public const string SupersededItem8 =
        "No text, watermark or signature anywhere in the image";

    /// <summary>Superseded Part F item 9 — what built check 9 implements.</summary>
    public const string SupersededItem9 =
        "Proportions match the Style Anchor Sheet (2.5–3 heads)";

    /// <summary>Superseded Part F item 10 — what built check 10 implements.</summary>
    public const string SupersededItem10 =
        "File named per §D1 and packed into the correct atlas";

    /// <summary>Superseded Part F item 11 — what built check 11 implements.</summary>
    public const string SupersededItem11 =
        "Side-by-side comparison against 3 previously-approved assets in the same category shows no " +
            "style drift";

    /// <summary>The superseded lines, in their own order. Index 0 is item 1.</summary>
    public static IReadOnlyList<string> SupersededItems { get; } =
    [
        SupersededItem1, SupersededItem2, SupersededItem3, SupersededItem4, SupersededItem5, SupersededItem6, SupersededItem7, SupersededItem8, SupersededItem9, SupersededItem10, SupersededItem11,
    ];
}
