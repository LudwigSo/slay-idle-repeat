"""Regenerate Doc15PartF.cs from game-design/15 itself. Run from the repository root.

    python tools/AssetPipeline/Qa/Doc15PartF.generate.py

Generated rather than retyped: the whole point of the file is that its strings are the doc's own
bytes, and a human transcribing 26 lines with em dashes, en dashes and section signs in them is
exactly the drift the reconciliation exists to catch.
"""

import pathlib
import re

DOC = pathlib.Path("game-design/15_ART_DIRECTION_AND_ASSET_MANIFEST.md")
TARGET = pathlib.Path("tools/AssetPipeline/Qa/Doc15PartF.cs")

lines = DOC.read_text(encoding="utf-8").split("\n")
start = next(i for i, l in enumerate(lines) if l.startswith("# PART F — QUALITY ASSURANCE CHECKLIST"))

groups = []
current = None
for line in lines[start:]:
    if line.startswith("# PART G"):
        break
    text = line.strip()
    if text.startswith("**") and "—" in text and not text.startswith("- "):
        current = (re.sub(r"\*+", "", text), [])
        groups.append(current)
    elif text.startswith("- [ ] ") and current is not None:
        current[1].append(text[6:].replace("`", ""))

a4_line = next(l for l in lines if "cannot tell which character it is" in l)
sentence = re.search(r"(If you cannot tell which character it is, [^.]*\.)",
                     re.sub(r"\*+", "", a4_line)).group(1)

superseded = [
    "".join(re.findall(r'"([^"]*)"', m.group(1)))
    for m in re.finditer(r"public const string (?:Superseded)?Item\d+ =\s*(.*?);\s*\n",
                         TARGET.read_text(encoding="utf-8"), re.S)
]

flat = [item for _, items in groups for item in items]
survivors = [s for s in superseded if s in flat]

BS = chr(92)
QUOTE = '"'


def lit(value):
    escaped = value.replace(BS, BS + BS).replace(QUOTE, BS + QUOTE)
    if len(escaped) <= 96:
        return QUOTE + escaped + QUOTE
    cut = escaped.rfind(" ", 0, 92) + 1
    return QUOTE + escaped[:cut] + QUOTE + " +\n            " + QUOTE + escaped[cut:] + QUOTE


out = [
    "namespace SlayIdleRepeat.AssetPipeline.Qa;",
    "",
    "/// <summary>",
    "/// `15` Part F's checklist lines, verbatim — the <see cref=\"CurrentItems\"/> the doc lists today,",
    "/// and the <see cref=\"SupersededItems\"/> the nine built checks still implement.",
    "/// </summary>",
    "/// <remarks>",
    "/// <para>",
    "/// \U0001F512 <b>One home for the text, and now two lists in it.</b> Each <see cref=\"IQaCheck\"/> takes",
    "/// its <see cref=\"IQaCheck.ChecklistText\"/> from here rather than retyping the line, so a wording",
    "/// can drift from the doc in exactly one place. <c>QaChecklistTests</c> reconciles",
    "/// <see cref=\"CurrentItems\"/> against",
    "/// <c>game-design/15_ART_DIRECTION_AND_ASSET_MANIFEST.md</c> itself, character for character, so an",
    "/// edit to Part F fails the build rather than quietly leaving this file describing an older",
    "/// checklist. That reconciliation was deleted on 2026-08-25 and is restored.",
    "/// </para>",
    "/// <para>",
    "/// \U0001F534 <b>The two lists do not agree, and the gap is the point.</b> D60 re-authored Part F for",
    f"/// real-time 3D: {len(flat)} items across {len(groups)} kind-tagged groups, where the pre-D60 checklist had",
    f"/// {len(superseded)}. Exactly {len(survivors)} lines survive verbatim. The nine built checks measure PIXELS, which",
    "/// still answers for kinds <b>R</b> (rendered from a model) and <b>F</b> (flat 2D) and answers nothing",
    "/// for kind <b>M</b>, whose geometry, texturing and rig groups have no implementation at all.",
    "/// <c>QaChecklistTests</c> pins that coverage as a number so it cannot drift unnoticed; raising it is",
    "/// `16` D60 consequence 5's own task.",
    "/// </para>",
    "/// <para>",
    "/// \U0001F512 <b>\"Verbatim\" means Part F's line with its Markdown removed and nothing else.</b> The doc",
    "/// writes each item as an unchecked task-list row, so the leading <c>- [ ] </c> is dropped, and the",
    "/// backticks around inline code are dropped. Everything else is the doc's own bytes — em dashes, en",
    "/// dashes, section signs, the emphasis asterisks inside a kind tag. Retyping any of those as ASCII",
    "/// is a silent divergence, which is why the reconciliation compares ordinally.",
    "/// </para>",
    "/// <para>",
    "/// ⚠️ <b>Generated, not typed.</b> <c>Doc15PartF.generate.py</c>, beside this file, reads",
    "/// Part F and emits it. Re-run it after a Part F edit rather than hand-patching the strings;",
    "/// it is idempotent, and reads the superseded list back out of its own output.",
    "/// </para>",
    "/// </remarks>",
    "public static class Doc15PartF",
    "{",
    f"    /// <summary>The number of items `15` Part F lists today. It is {len(flat)}.</summary>",
    f"    public const int CurrentItemCount = {len(flat)};",
    "",
    "    /// <summary>The number of items Part F listed before D60, and the number of built checks.</summary>",
    f"    public const int SupersededItemCount = {len(superseded)};",
    "",
    "    /// <summary>How many superseded lines survive verbatim in the current Part F.</summary>",
    "    /// <remarks>",
    "    /// \U0001F534 A coverage number, not a target. It counts lines a built check could be re-pointed at",
    "    /// without rewording anything — not items the pipeline decides correctly for a mesh.",
    "    /// </remarks>",
    f"    public const int SurvivingVerbatimCount = {len(survivors)};",
    "",
    "    /// <summary>",
    "    /// `15` §A4's acceptance sentence, verbatim — the half of the silhouette item no measurement",
    "    /// performs.",
    "    /// </summary>",
    "    /// <remarks>",
    "    /// \U0001F534 It read <i>\"regenerate it\"</i> until 2026-08-25, while §A4 already said <i>\"remodel it\"</i>",
    "    /// — correct for a mesh. Nothing caught it, because unlike Part F this sentence was never",
    "    /// reconciled against the doc (`16` D60 consequence 5b). It is reconciled now.",
    "    /// </remarks>",
    "    public const string SilhouetteAcceptanceSentence =",
    "        " + lit(sentence) + ";",
    "",
]

for index, (name, items) in enumerate(groups, 1):
    out.append(f"    /// <summary>Part F group {index} — {name}.</summary>")
    out.append(f"    private static readonly string[] Group{index} =")
    out.append("    [")
    out.extend("        " + lit(item) + "," for item in items)
    out.append("    ];")
    out.append("")

out.extend([
    "    /// <summary>Part F's lines as the doc lists them today, in Part F's order. Index 0 is item 1.</summary>",
    "    public static IReadOnlyList<string> CurrentItems { get; } =",
    "    [",
    "        " + ", ".join(f".. Group{i}" for i in range(1, len(groups) + 1)) + ",",
    "    ];",
    "",
])

for number, text in enumerate(superseded, 1):
    out.append(f"    /// <summary>Superseded Part F item {number} — what built check {number} implements.</summary>")
    out.append(f"    public const string SupersededItem{number} =")
    out.append("        " + lit(text) + ";")
    out.append("")

out.extend([
    "    /// <summary>The superseded lines, in their own order. Index 0 is item 1.</summary>",
    "    public static IReadOnlyList<string> SupersededItems { get; } =",
    "    [",
    "        " + ", ".join(f"SupersededItem{i}" for i in range(1, len(superseded) + 1)) + ",",
    "    ];",
    "}",
])

TARGET.write_bytes(("\n".join(out) + "\n").replace("\n", "\r\n").encode("utf-8"))
print(f"generated: {len(flat)} current in {len(groups)} groups, "
      f"{len(superseded)} superseded, {len(survivors)} surviving verbatim")
print("A4 sentence:", sentence)
