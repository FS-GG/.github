#!/usr/bin/env python3
"""Focused semantic checks for current skill guidance and host policy."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOTS = (".agents/skills", ".claude/skills")
SKILLS = ("lane-steward", "publishing-and-deployment", "spectre-console")
ALL_SKILLS = (
    "check-board",
    "cross-repo-coordination",
    "cut-nuget-release",
    "drive-board",
    "intra-repo-parallel-work",
    "lane-steward",
    "p-add",
    "padd-item",
    "pnext-item",
    "publishing-and-deployment",
    "spectre-console",
    "work-board",
    "work-roadmap",
)
EXPLICIT_ONLY = {"cut-nuget-release", "drive-board", "p-add", "padd-item", "work-roadmap"}


def fail(message: str) -> None:
    print(f"skill-current-truth: {message}", file=sys.stderr)
    raise SystemExit(1)


def body(root: Path, skill: str) -> str:
    paths = [root / runtime / skill / "SKILL.md" for runtime in ROOTS]
    missing = [str(path.relative_to(root)) for path in paths if not path.is_file()]
    if missing:
        fail(f"{skill}: missing mirror(s): {', '.join(missing)}")

    payloads = [path.read_bytes() for path in paths]
    if len(set(payloads)) != 1:
        fail(f"{skill}: SKILL.md bytes differ across the three runtime roots")
    return payloads[0].decode("utf-8")


def frontmatter(text: str) -> str:
    parts = text.split("---", 2)
    if len(parts) != 3:
        fail("malformed skill frontmatter")
    return parts[1]


def check_links(root: Path, skill: str, text: str) -> None:
    source = root / ROOTS[0] / skill / "SKILL.md"
    for target in re.findall(r"\[[^\]]+\]\(([^)]+)\)", text):
        if "://" in target or target.startswith("#"):
            continue
        path = target.split("#", 1)[0]
        if path and not (source.parent / path).resolve().exists():
            fail(f"{skill}: broken relative link {target!r}")


def check_host_policy(root: Path) -> None:
    for skill in ALL_SKILLS:
        paths = [root / runtime / skill / "agents/openai.yaml" for runtime in ROOTS]
        missing = [str(path.relative_to(root)) for path in paths if not path.is_file()]
        if missing:
            fail(f"{skill}: missing OpenAI metadata mirror(s): {', '.join(missing)}")
        payloads = [path.read_bytes() for path in paths]
        if len(set(payloads)) != 1:
            fail(f"{skill}: agents/openai.yaml bytes differ across runtime roots")
        text = payloads[0].decode("utf-8")
        prompt = re.search(r'^\s+default_prompt:\s+"([^"]+)"$', text, re.MULTILINE)
        short = re.search(r'^\s+short_description:\s+"([^"]+)"$', text, re.MULTILINE)
        if not prompt or f"${skill}" not in prompt.group(1):
            fail(f"{skill}: default_prompt must explicitly select ${skill}")
        if not short or not 25 <= len(short.group(1)) <= 64:
            fail(f"{skill}: short_description must be 25-64 characters")
        explicit = "allow_implicit_invocation: false" in text
        if explicit != (skill in EXPLICIT_ONLY):
            expected = "false" if skill in EXPLICIT_ONLY else "implicit/default"
            fail(f"{skill}: invocation policy must be {expected}")

    # work-roadmap still carries the portable host-loop rules. drive-board and work-board no longer do:
    # #4680 (75ab98dd) rewrote work-board's host-loop around the canonical coordinator and #4900 rewrote
    # it again, and drive-board's host-loop is a redirect to it. Their obligations are different now.
    roadmap = (root / ROOTS[0] / "work-roadmap/references/host-loop.md").read_text()
    for required in ("current host exposes", "isolated worktree", "FSGG_WORKER", "selector supported by the current host"):
        if required not in roadmap:
            fail(f"work-roadmap: host loop missing portability rule {required!r}")
    board_loop = (root / ROOTS[0] / "work-board/references/host-loop.md").read_text()
    if "provides no alternate scheduler" not in board_loop:
        fail("work-board: host loop must remain the canonical reference without an alternate scheduler")
    redirect = (root / ROOTS[0] / "drive-board/references/host-loop.md").read_text()
    if "(../../work-board/references/host-loop.md)" not in redirect or "adds no" not in redirect:
        fail("drive-board: host loop must stay a compatibility redirect to the canonical work-board host loop")
    # Host-specific orchestration syntax must not return anywhere in the three coordinator skills.
    for skill in ("drive-board", "work-board", "work-roadmap"):
        for path in sorted((root / ROOTS[0] / skill).rglob("*.md")):
            text = path.read_text()
            if 'isolation: "worktree"' in text or "Agent tool" in text:
                fail(f"{skill}: host-specific orchestration syntax returned in {path.relative_to(root / ROOTS[0])}")


def check(root: Path) -> None:
    check_host_policy(root)
    docs = {skill: body(root, skill) for skill in SKILLS}
    for skill, text in docs.items():
        check_links(root, skill, text)

    # lane-steward was rewritten by #4711 (0d85830e): it no longer prescribes applying a narrowing through
    # `set-paths` or an additive `widen`. It proposes the narrower declaration to the accountable owner and
    # states that no V1 scan/lanes/set-paths/widen/take/claim route is authorized for organization V2.
    lane = docs["lane-steward"]
    if re.search(r"fsgg-coord\s+(?:scan|lanes|set-paths|widen|take|claim)\b", lane):
        fail("lane-steward: obsolete V1 scan/lanes/set-paths/widen/take/claim recipe returned")
    # The statements wrap across lines in the source; compare on normalised whitespace so a reflow is not a finding.
    flat = re.sub(r"\s+", " ", lane)
    if "No V1 scan/lanes/set-paths, widen, take or claim route is authorized for organization V2" not in flat:
        fail("lane-steward: must state that no V1 narrowing/claim route is authorized for organization V2")
    if "to its accountable owner" not in flat or "does not select, claim, enroll" not in flat:
        fail("lane-steward: diagnosis must propose to the accountable owner and not select, claim or enroll")

    publishing = docs["publishing-and-deployment"]
    stale_publishing = (
        "`-preview` always",
        "Public nuget.org (decided, wiring pending",
        "`coherent: false` until wired",
        "Blocked on an admin gate",
    )
    for claim in stale_publishing:
        if claim in publishing:
            fail(f"publishing-and-deployment: stale live claim returned: {claim}")
    publishing_meta = frontmatter(publishing).lower()
    if "preview channel" in publishing_meta or "local-feed fallback" in publishing_meta:
        fail("publishing-and-deployment: routing metadata advertises superseded preview/local behavior")
    for required in (
        "stable channel",
        "There is no local-feed fallback",
        "byte-identical `.nupkg` to nuget.org",
        "Historical rollout record",
    ):
        if required not in publishing:
            fail(f"publishing-and-deployment: missing current-truth statement: {required}")

    spectre = docs["spectre-console"]
    for stale in ("invisible-byte", "String.Length (bytes", "measures **bytes**", "cells vs bytes"):
        if stale in spectre:
            fail(f"spectre-console: String.Length/display terminology regressed: {stale}")
    for required in ("UTF-16 code units", "ANSI sequences", "display **cells**", "encoding bytes are a separate measure"):
        if required not in spectre:
            fail(f"spectre-console: missing measurement distinction: {required}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    if not root.is_dir():
        print(f"skill-current-truth: no such root: {root}", file=sys.stderr)
        return 3
    check(root)
    print("skill-current-truth: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
