#!/usr/bin/env python3
"""Split SCREEN_DESIGN_BRIEF.md into one self-contained packet per canvas lane.

Each packet is a standalone prompt for a Claude Design session: the full shared
constraint set (SS1-SS5) plus exactly one lane's screens. Regenerate after any
edit to the brief -- the packets are derived, never edited by hand.

    python3 docs/design-packets/generate.py
"""
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BRIEF = os.path.join(ROOT, "docs", "SCREEN_DESIGN_BRIEF.md")
OUT = os.path.join(ROOT, "docs", "design-packets")

# Lane 0 and Lane 6 are not screen tranches: lane 0 is the inventory (already in
# the preamble) and lane 6 is SS8's overlay table.
LANES = {
    0: ("System", "the component inventory, colour/type sheet, rarity frames"),
    1: ("Run loop", "S05 Board, S06 Battle, S07 Perk Draft, tile cards, Stage Gate, S10 minigames, S13, S14"),
    2: ("Entry and hub", "S01, S02 FTUE, S03 Home, S04 Chapter Select, S28, S29"),
    3: ("Character and collection", "S15 Hero, S16 Inventory, S17 Forge, S18 Talents, S19 Menagerie"),
    4: ("Social", "S20 Arena, S21 PvP Loadout, S22 Leaderboard, S33-S36 Guilds"),
    5: ("Service", "S23 Shop, S24 Codex, S38 Feats, S25, S37, S26, S27, S30-S32 Events"),
    6: ("Overlays", "the 17 unnumbered surfaces"),
}


HEADER = """# Lane %d · %s

**Packet %d of 7 — generated from `docs/SCREEN_DESIGN_BRIEF.md`, do not edit by hand.**

This packet covers **%s**. Everything below the token slot is the shared constraint set, identical in
every packet; design only this lane's surfaces. Artboard names follow §4: `S05-Board`,
`S05-Board-fork`, `X-CurseCard`.

> **Note on numbering.** These packets are the **lanes** of §4. The “Tranche” numbers in §7 below are a
> *build order that cuts across lanes* and do not line up with lane numbers — read §7 as sequencing
> advice, not as a description of this packet's contents."""


def cut(text, start, end):
    """Return text from the line beginning with `start` up to (not incl.) `end`."""
    i = text.index(start)
    j = text.index(end, i) if end else len(text)
    return text[i:j].rstrip() + "\n"


def token_slot(lane):
    if lane == 0:
        return (
            "## \U0001f512 Locked tokens — **this lane produces them**\n\n"
            "This is the first session and the only one that decides the design language. Emit the\n"
            "token block described in §5.1 as text alongside the artboards, so it can be pasted into\n"
            "every later lane. Nothing else may be designed until the inventory exists.\n"
        )
    return (
        "## \U0001f512 Locked tokens — **paste the Lane 0 block here before starting**\n\n"
        "```\n"
        "<<< PASTE THE TOKEN BLOCK EMITTED BY LANE 0 (see §5.1) >>>\n"
        "```\n\n"
        "These values are already decided. Use them exactly; do not re-derive a palette, a type scale or\n"
        "the nine perk-category hexes. If the slot above is still empty, **stop and run Lane 0 first** —\n"
        "a lane designed without it will not match the rest of the game.\n"
    )


def main():
    src = io.open(BRIEF, encoding="utf-8", newline="").read()

    title = src[: src.index("## 1. How to use")].rstrip() + "\n"
    shared = cut(src, "## 1. How to use", "## 6. The screens")
    sec6 = cut(src, "## 6. The screens", "## 7. Build order")
    sec7 = cut(src, "## 7. Build order", "## 8. Unnumbered")
    sec8 = cut(src, "## 8. Unnumbered", "## 9. Planned")
    gaps = cut(src, "## 9. Planned", "## Counts")

    # SS6's five "### Tranche N" blocks are lanes 1-5, in order.
    parts = re.split(r"(?m)^(?=### Tranche \d)", sec6)
    tranches = [p for p in parts[1:] if p.strip()]
    if len(tranches) != 5:
        sys.exit("expected 5 tranche blocks in SS6, found %d" % len(tranches))

    written = []
    for lane, (name, blurb) in LANES.items():
        if lane == 0:
            body = ""
        elif lane == 6:
            body = sec8
        else:
            body = "## 6. The screens — this lane only\n\n" + tranches[lane - 1].rstrip() + "\n"

        doc = "\n\n---\n\n".join(
            x for x in [
                HEADER % (lane, name, lane + 1, blurb),
                token_slot(lane),
                title,
                shared,
                body,
                sec7,
                gaps,
            ] if x.strip()
        )

        path = os.path.join(OUT, "lane-%d-%s.md" % (lane, name.lower().replace(" ", "-")))
        io.open(path, "w", encoding="utf-8", newline="\n").write(doc.rstrip() + "\n")
        written.append((os.path.basename(path), len(doc)))

    for n, sz in written:
        print("  %-34s %6.1f KB" % (n, sz / 1024.0))
    print("%d packets -> docs/design-packets/" % len(written))


if __name__ == "__main__":
    main()
