#!/usr/bin/env python3
"""Fail closed when agent-facing prose reaches for a RECENCY-based comment edit (.github#2666).

`gh pr comment --edit-last` edits the last comment made by the **authenticating account**, not the
last one made by the caller. Every agent in an FS-GG fan-out — host, implementers, and independent
critics — authenticates as the same account, so "my last comment" is fleet-wide state. The minted
`FSGG_WORKER` id that separates claims is an FS-GG construct GitHub knows nothing about, and it
separates nothing here.

Measured live on `FS-GG/.github` PR #2663 on 2026-08-15: a worker rebinding its own
`fsgg:delivery-obligation` declaration to a new head with `--edit-last` overwrote an independent
critic's 18879-code-point findings comment with its own 2451-code-point declaration. The
`fsgg:review-decision/v2` record the whole review contract treats as sole authority survived only
because it happened not to be that account's most recent comment at that instant — ordering luck, not
a property of the design. Nothing gated it; it was caught because one worker volunteered it.

Four legs, all fail-closed:

1. PROHIBITION. No recency-based comment-edit token appears in authored agent-facing prose, ANYWHERE
   except inside the one canonical hazard statement below. The check is strip-then-scan: every
   byte-identical occurrence of `CANONICAL` is removed and the residue must hold no token. So the
   statement may name the flag in order to forbid it, and any OTHER mention — an instruction, a
   reworded near-copy, a helpful aside — reds. There is no allowlist to grow and no proximity
   heuristic to fool.

2. STATED ONCE, WHERE AN EDITING AGENT READS IT. Every `.claude/agents/*.md` definition carries
   `CANONICAL` exactly once and byte-identically, and no other authored file carries it at all. The
   definitions are the prose every dispatched worker and critic loads before it can post or edit
   anything, so the hazard is unavoidable rather than one reference-file load away; pinning the exact
   bytes here is what stops the statement being silently deleted or diluted, which is the failure
   `review-round-contract.py` documents for the review contract itself.

3. NON-VACUITY. This gate's subject is source text, so it can pass because it examined nothing
   (.github#2551). Two guards. Every root it scans must yield at least one markdown file — an
   existing-but-empty root is the shape a migration produces, and it clears every mention by default.
   And `self_test` runs the real `scan_text` pipeline over a synthetic document that carries the
   canonical statement AND one fresh violation, asserting it finds exactly the violation: that proves
   the detector is live in this process and that stripping the canonical block has not blinded it,
   which is the one way the strip design could fail open. It CALLS the scanner rather than grepping
   for its name.

4. TRIGGER. `.github/workflows/skill-quality.yml` must list `<root>/**` for every root scanned here,
   and `tests/skill-quality/**` for this file itself, under BOTH `pull_request` and `push`. The
   required list is DERIVED from the roots actually scanned, so renaming a root without teaching the
   workflow about it reds rather than silently narrowing what CI sees. A gate that does not run when
   its own subject changes is epic #266's fail-open class.

Scope, stated honestly: "authored agent-facing prose" here means the agent definitions, both declared
skill roots, and `docs/coordination` — what an agent loads as instructions. CI workflow YAML is out of
scope deliberately: `.github/workflows/touch-set-drift.yml` names `--edit-last` to explain why it does
NOT use it, and that is machinery a bot executes, not an instruction an agent reads. The token set
covers the `--edit-last` family; it does not attempt to recognize every conceivable recency-based edit
expressed in some other tool's spelling, and this gate does not claim to.

Usage: recency-comment-edit.py [--root DIR]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

# The one canonical statement of the hazard. Byte-identical in every `.claude/agents/*.md`, and
# nowhere else. Changing a byte of it here means changing it in all four definitions in the same
# commit — that coupling is the point.
CANONICAL = """- **Never edit a comment by recency — always by explicit comment id.** `gh pr comment --edit-last`
  edits the last comment made by the **authenticating account**, not the last one made by you, and
  every agent in an FS-GG fan-out — host, implementers, and critics alike — authenticates as the
  *same* account. Your minted `FSGG_WORKER` id separates claims; GitHub knows nothing about it and it
  separates nothing here. Measured on PR #2663: a worker rebinding its own `fsgg:delivery-obligation`
  declaration to a new head with `--edit-last` overwrote an independent critic's 18879-code-point
  findings comment with its own 2451-code-point declaration, and the `fsgg:review-decision/v2` record
  the whole review contract treats as sole authority survived only because it happened not to be that
  account's most recent comment at that instant (`.github#2666`). To rebind or amend **your own**
  comment, find it by its marker and PATCH that exact id —
  `gh api -X PATCH repos/<owner>/<repo>/issues/comments/<id> -f body=@<file>` — or delete it and post
  a replacement. Editing by recency is never safe here."""

# `--edit-last`, `--edit_last`, `edit-last`, `editlast`. The leading `\b` keeps `credit-last` and
# friends out; the trailing one keeps `edit-lastly` out.
TOKEN = re.compile(r"\bedit[-_]?last\b", re.IGNORECASE)

SKILL_ROOT_DECLARATION = ".agent-skill-roots"
AGENT_DEFINITIONS = ".claude/agents"
# Roots that are agent-facing prose but are not skill roots, so the declaration above never names them.
FIXED_ROOTS = (AGENT_DEFINITIONS, "docs/coordination")

WORKFLOW = ".github/workflows/skill-quality.yml"
OWN_TRIGGER = "tests/skill-quality/**"


class Finding(Exception):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Finding(message)


def scan_text(text: str) -> list[str]:
    """Every recency-token match left AFTER removing byte-identical copies of the canonical statement.

    This is the whole prohibition, in one function, so `self_test` can exercise the real thing.
    """
    return TOKEN.findall(text.replace(CANONICAL, ""))


def self_test() -> None:
    """Prove the pipeline fires, in this process, before trusting a clean sweep (.github#2551).

    The strip-then-scan design has exactly one fail-open mode: a `CANONICAL` that swallows more than
    itself (empty, whitespace, or over-broad) would delete the violation along with the statement and
    report a confident clean tree. The probe below carries the canonical statement AND one fresh
    mention, so a clean result here means the strip removed the statement and nothing else.
    """
    require(
        len(CANONICAL) > 400 and bool(TOKEN.search(CANONICAL)),
        "the canonical hazard statement is too short or does not name the flag it forbids, so "
        "stripping it could not license anything and this gate would be checking a different string",
    )
    require(
        scan_text(CANONICAL) == [],
        "the canonical hazard statement does not survive its own strip, so every file carrying it "
        "would red and the gate could never be satisfied",
    )
    probe = CANONICAL + "\n\nIf the head moved, just `gh pr comment --edit-last` to rebind it.\n"
    hits = scan_text(probe)
    require(
        len(hits) == 1,
        f"the detector found {len(hits)} violation(s) in a probe carrying the canonical statement "
        "plus exactly one fresh mention; stripping the statement has blinded the scan, which is the "
        "one way this gate fails open",
    )


def scan_roots(root: Path) -> list[Path]:
    """The authored agent-facing roots: the declared skill roots plus the fixed ones.

    Every declared root must resolve. This mirrors `agent-definition-coverage.py` and
    `scripts/skill-union-assert.sh`: declaring roots narrows WHAT IS ASKED FOR and never weakens the
    answer, so a declaration that has drifted past its directories is a broken tree, not a smaller
    sweep.
    """
    declaration = root / SKILL_ROOT_DECLARATION
    require(declaration.is_file(), f"no {SKILL_ROOT_DECLARATION} declaration to read skill roots from")
    declared = [
        root / line.strip()
        for line in declaration.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]
    require(bool(declared), f"{SKILL_ROOT_DECLARATION} declares no skill roots")
    roots = declared + [root / fixed for fixed in FIXED_ROOTS]
    missing = [str(path.relative_to(root)) for path in roots if not path.is_dir()]
    require(
        not missing,
        f"{', '.join(missing)} does not exist, so that surface of authored agent-facing prose is "
        "not swept at all and any recency-based comment edit in it would clear by default",
    )
    return roots


def check_trigger(root: Path, roots: list[Path]) -> list[str]:
    """The workflow must run this gate when any scanned root — or this file — changes.

    The required list is derived from `roots`, not hardcoded, so widening the sweep without widening
    the trigger reds here instead of shipping a gate CI never reaches on the paths it now claims.
    """
    workflow = root / WORKFLOW
    require(workflow.is_file(), f"{WORKFLOW} is missing, so nothing runs this gate")
    triggers = yaml.safe_load(workflow.read_text(encoding="utf-8"))
    # PyYAML resolves the bare key `on:` to the boolean True (YAML 1.1); accept either spelling.
    on = triggers.get("on", triggers.get(True))
    require(isinstance(on, dict), f"{WORKFLOW} has no parseable trigger block")
    required = [f"{path.relative_to(root)}/**" for path in roots] + [OWN_TRIGGER]
    for event in ("pull_request", "push"):
        paths = (on.get(event) or {}).get("paths") or []
        for wanted in required:
            require(
                wanted in paths,
                f"{WORKFLOW} does not list '{wanted}' under {event}.paths, so a change to prose this "
                "gate sweeps would never reach it",
            )
    return required


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=str(Path(__file__).resolve().parents[2]))
    args = parser.parse_args(argv)
    root = Path(args.root).resolve()

    self_test()

    roots = scan_roots(root)
    definitions = root / AGENT_DEFINITIONS

    scanned = 0
    carriers: list[str] = []
    for scan_root in roots:
        here = 0
        for path in sorted(scan_root.rglob("*.md")):
            here += 1
            scanned += 1
            relative = str(path.relative_to(root))
            text = path.read_text(encoding="utf-8")

            # (1) prohibition. `require`'s message is built eagerly, so it is composed only on the
            # branch where `hits` is non-empty.
            hits = scan_text(text)
            if hits:
                raise Finding(
                    f"{relative} names a recency-based comment edit ({hits[0]!r}) outside the "
                    "canonical hazard statement. `--edit-last` edits by ACCOUNT, and this whole "
                    "fleet shares one account, so it overwrites whichever agent commented most "
                    "recently — a critic's durable record, routinely. Address your own comment by "
                    "its explicit id instead (.github#2666)"
                )

            # (2) stated once.
            occurrences = text.count(CANONICAL)
            is_definition = path.parent == definitions
            if is_definition:
                require(
                    occurrences == 1,
                    f"{relative} carries the canonical recency-edit hazard statement {occurrences} "
                    "time(s), not exactly once; every dispatched agent must read it verbatim before "
                    "it can edit a comment (.github#2666)",
                )
                # Counted from the MEASURED occurrence, not from "we visited a definition". The two
                # are equivalent only while the assertion above holds, and a success line whose
                # number is true by a neighbouring check rather than by construction is exactly the
                # kind that keeps reading "4 definitions carry it" after one stopped.
                if occurrences == 1:
                    carriers.append(relative)
            else:
                require(
                    occurrences == 0,
                    f"{relative} repeats the canonical recency-edit hazard statement; it is stated "
                    "once, in the agent definitions, and a second home is a copy that drifts",
                )
        # (3) non-vacuity, per root: an existing-but-empty root sweeps nothing and reports success.
        require(
            here > 0,
            f"scanned 0 file(s) under {scan_root.relative_to(root)}: that root exists but holds no "
            "markdown, so every recency-based comment edit in it would clear by default",
        )

    require(scanned > 0, "scanned 0 authored file(s) in total, so this gate proved nothing")
    require(
        bool(carriers),
        f"no definition under {AGENT_DEFINITIONS} carries the canonical hazard statement, so no "
        "dispatched agent reads it (.github#2666)",
    )

    required = check_trigger(root, roots)

    # The success line states the BASIS of its answer — how many files, how many carriers, how many
    # trigger paths — so a green resting on a blind sweep is legible in a log.
    print(
        f"recency-comment-edit: {scanned} authored file(s) across {len(roots)} root(s) name no "
        f"recency-based comment edit, {len(carriers)} agent definition(s) carry the canonical hazard "
        f"statement verbatim, and {len(required)} trigger path(s) keep CI reaching all of it"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except Finding as finding:
        print(f"recency-comment-edit: {finding}", file=sys.stderr)
        sys.exit(1)
