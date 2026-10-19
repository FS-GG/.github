#!/usr/bin/env python3
"""Keep findings owned while analysis stays optional and wrappers inherit canonical guidance.

The historical filename remains a maintained caller. Canonical work-board owns finding disposition;
drive-board redirects to it, receivers can file through their current owner, and routed wrappers add
no protocol. This source guard neither dispatches nor establishes native effect authority.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

# Pin the retained owned-finding safeguard once in each runtime's canonical host. Compatibility
# hosts must link to it instead of duplicating it; wrappers must inherit rather than restate it.
CANONICAL = '**Keep an owner for reported finding packets.** Canonical work-board owns coordination.\nWhen an existing owner reports an evidence-backed distinct finding or a known `fsgg:finding-packet`\nneeds disposition, retain that finding and its source identity in the existing owning issue/native\nthread. Resolve its accountable owner before treating it as new work. If a separately authorized\n`board-analyst` is available and useful, send only the bounded relevant finding for adjudication;\nits verdict never dispatches, claims, merges or grants operation authority. Otherwise the current\nowner applies the existing evidence/materiality and deduplication boundary. Do not strand a known\nfinding with no owner, scan all comments on every cycle, create duplicate issues or launch a worker\nmerely to fill capacity. Missing analyst availability is not a source-delivery gate. The current owner may dispose and file directly through [canonical intake](intake.md);\nanalyst availability never requires a packet or second filing actor. Old wave/slot assumptions do not\napply to this coordinator.'

# The vocabulary that makes prose ABOUT analyst dispatch. Leg 2 forbids all of it inside a routed
# variant: the canonical block names every one of these, so a variant that pasted the block reds, and so
# does a variant that paraphrased it into one sentence of its own.
VOCABULARY = re.compile(r"fsgg:finding-packet|finding packet|board-analyst|fsgg-analyst", re.IGNORECASE)

SKILL_ROOT_DECLARATION = ".agent-skill-roots"
# The canonical board skills. Their routed variants are DERIVED from these by glob, never listed.
BOARDS = ("drive-board", "work-board")
HOST_LOOP = "references/host-loop.md"

# Every receiver retains direct accountable-owner disposition, whether an optional analyst exists
# or not. Keep the deliberately operator-only distribution boundary independently visible.
FINDER_CONTRACT = "pnext-item/references/findings-and-filing.md"
NO_ANALYST_BRANCH = "**The current owner may dispose and file the finding directly.**"

REGISTRY = "registry/skills.yml"
# `materializes-when` is ADR-0017's never-true literal. Broadening the operator payload requires
# separate receiver review; it never removes the current owner's direct filing route.
ANALYST_NEVER_MATERIALIZES = re.compile(
    r"id:\s*board-analyst\s*,.*?materializes-when:\s*\"false\"", re.DOTALL
)

WORKFLOW = ".github/workflows/skill-quality.yml"
OWN_TRIGGER = "tests/skill-quality/**"


class Finding(Exception):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Finding(message)


def carries(text: str) -> int:
    """How many byte-identical copies of the dispatch step this document holds."""
    return text.count(CANONICAL)


def restatements(text: str) -> list[str]:
    """Every analyst-dispatch token in this document. Leg 2's whole detector, in one function."""
    return VOCABULARY.findall(text)


def self_test() -> None:
    """Prove both detectors fire, in this process, before trusting any sweep (.github#2551).

    A presence gate has one fail-open mode a clean tree cannot distinguish from success: a `CANONICAL`
    that matches trivially — empty, or whitespace — is carried by every document, so leg 1 would pass
    over a tree that had deleted the step entirely. The probes below pin the real strings.
    """
    require(
        len(CANONICAL) > 400,
        "the canonical dispatch step is too short to be the contract it stands for, so leg 1 would be "
        "asserting the presence of a fragment every document happens to contain",
    )
    require(
        carries(CANONICAL) == 1 and carries("") == 0,
        "the canonical dispatch step does not count itself exactly once, so leg 1 is measuring "
        "something other than the step it names",
    )
    require(
        bool(restatements(CANONICAL)),
        "the canonical dispatch step names none of the analyst vocabulary, so a routed variant could "
        "paste it verbatim and leg 2 would clear it",
    )
    absent = "A wave ends when its merges are verified.\n"
    require(
        carries(absent) == 0 and restatements(absent) == [],
        "both detectors fire on a document that mentions neither the dispatch step nor the analyst, "
        "so every leg below would report findings it did not measure",
    )
    probe = absent + "\nDispatch `fsgg-analyst-best` after collecting each finding packet.\n"
    require(
        carries(probe) == 0 and len(restatements(probe)) == 2,
        "the vocabulary detector missed a reworded analyst-dispatch instruction carrying no copy of "
        "the canonical block, which is exactly the variant drift leg 2 exists to catch",
    )


def scan_roots(root: Path) -> list[Path]:
    """The declared skill roots, every one of which must resolve.

    This mirrors `agent-definition-coverage.py`, `recency-comment-edit.py`, and
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
    missing = [str(path.relative_to(root)) for path in declared if not path.is_dir()]
    require(
        not missing,
        f"{', '.join(missing)} does not exist, so that runtime's board host loops are not checked at "
        "all and a missing analyst dispatch step there would clear by default",
    )
    return declared


def check_trigger(root: Path, roots: list[Path]) -> list[str]:
    """The workflow must run this gate when any scanned root — or this gate — changes.

    The required list is DERIVED from `roots`, not hardcoded, so widening the sweep without widening
    the trigger reds here instead of shipping a gate CI never reaches on the paths it now claims.
    """
    workflow = root / WORKFLOW
    require(workflow.is_file(), f"{WORKFLOW} is missing, so nothing runs this gate")
    triggers = yaml.safe_load(workflow.read_text(encoding="utf-8"))
    # PyYAML resolves the bare key `on:` to the boolean True (YAML 1.1); accept either spelling.
    on = triggers.get("on", triggers.get(True))
    require(isinstance(on, dict), f"{WORKFLOW} has no parseable trigger block")
    required = [f"{path.relative_to(root)}/**" for path in roots] + [OWN_TRIGGER, REGISTRY]
    for event in ("pull_request", "push"):
        paths = (on.get(event) or {}).get("paths") or []
        for wanted in required:
            require(
                wanted in paths,
                f"{WORKFLOW} does not list '{wanted}' under {event}.paths, so a change to what this "
                "gate reads would never reach it",
            )
    return required


def check_finder_contract(root: Path, roots: list[Path]) -> int:
    """Leg 3: the branch every kit receiver actually reads still exists, and still applies to them."""
    carriers = 0
    for scan_root in roots:
        path = scan_root / FINDER_CONTRACT
        relative = str(path.relative_to(root))
        require(
            path.is_file(),
            f"{relative} is missing, so the finder-side half of this contract — the branch that tells "
            "a receiver with no analyst to file its own row — is not there to be read",
        )
        text = path.read_text(encoding="utf-8")
        require(
            NO_ANALYST_BRANCH in text,
            f"{relative} no longer carries its current-owner branch verbatim. That branch is the whole "
            "design for every repository except this one: `board-analyst` is `scope: operator` and "
            "reaches no kit receiver, so without it a receiver-side finder is told to post a packet "
            "that nothing in its tree will ever read (.github#2675)",
        )
        carriers += 1

    registry = root / REGISTRY
    require(registry.is_file(), f"{REGISTRY} is missing, so nothing establishes where the analyst ships")
    require(
        bool(ANALYST_NEVER_MATERIALIZES.search(registry.read_text(encoding="utf-8"))),
        f"{REGISTRY} no longer declares `board-analyst` with `materializes-when: \"false\"`. The "
        "operator-only distribution is part of this selected payload; changing receiver materialization "
        "needs its own review, while direct accountable-owner disposition remains available",
    )
    return carriers


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=str(Path(__file__).resolve().parents[2]))
    args = parser.parse_args(argv)
    root = Path(args.root).resolve()

    self_test()

    roots = scan_roots(root)

    scanned = 0
    dispatchers: list[str] = []
    variants: list[str] = []

    for scan_root in roots:
        here = 0
        for board in BOARDS:
            # (1) the dispatch step, in the canonical board's host loop.
            host_loop = scan_root / board / HOST_LOOP
            relative = str(host_loop.relative_to(root))
            require(
                host_loop.is_file(),
                f"{relative} is missing, so the loop that owns critic dispatch has nowhere to own "
                "analyst dispatch beside it (.github#2675)",
            )
            here += 1
            scanned += 1
            host_text = host_loop.read_text(encoding="utf-8")
            if board == "drive-board":
                require("../../work-board/references/host-loop.md" in host_text and carries(host_text) == 0,
                        f"{relative} must redirect to the canonical host without duplicating its procedure")
                host_text = (scan_root / "work-board" / HOST_LOOP).read_text(encoding="utf-8")
            occurrences = carries(host_text)
            require(
                occurrences == 1,
                f"{relative} carries the canonical analyst-dispatch step {occurrences} time(s), not "
                "exactly once. Finders are routed to post `fsgg:finding-packet` comments instead of "
                "filing, so a host loop that never collects them turns every finding into a comment "
                "with no reader and no owner — strictly worse than the filing it replaced "
                "(.github#2675)",
            )
            # Counted from the MEASURED occurrence, not from "we visited a host loop". A success line
            # whose number is true by a neighbouring assertion rather than by construction is the kind
            # that keeps reading "4 loops dispatch it" after one stopped.
            if occurrences == 1:
                dispatchers.append(relative)

            # (2) and NOT in that board's routed variants, which change model routing only.
            #
            # This is the leg that can sweep NOTHING and report success (.github#2510): leg 1 fails
            # closed on a board whose host loop is missing, but a glob that matches no variant
            # directory — a renamed route, a migration half-done — silently clears every restatement
            # in the tree. So the variant count is MEASURED per board and asserted below, and the
            # markdown count is measured with it: a variant directory holding no markdown is the same
            # vacuous pass wearing a different shape.
            found = 0
            variant_files = 0
            for variant in sorted(scan_root.glob(f"{board}-*")):
                if not variant.is_dir():
                    continue
                found += 1
                variants.append(str(variant.relative_to(root)))
                for path in sorted(variant.rglob("*.md")):
                    variant_files += 1
                    here += 1
                    scanned += 1
                    hits = restatements(path.read_text(encoding="utf-8"))
                    if hits:
                        raise Finding(
                            f"{path.relative_to(root)} states analyst-dispatch protocol ({hits[0]!r}). "
                            f"The `{variant.name}` variant changes MODEL ROUTING ONLY and inherits "
                            f"`{board}`'s loop wholesale; restating protocol into a variant is the "
                            "drift .github#485 names, and it is how the two copies stop agreeing "
                            "(.github#2675)"
                        )
            require(
                found > 0,
                f"found 0 routed variant(s) of {board} under {scan_root.relative_to(root)}: the "
                "no-restatement sweep matched no directory at all, so a variant that had grown its "
                "own copy of the dispatch protocol would clear by default (.github#2510)",
            )
            require(
                variant_files > 0,
                f"read 0 file(s) across {found} routed variant(s) of {board} under "
                f"{scan_root.relative_to(root)}: the directories exist but hold no markdown, which "
                "clears every restatement exactly as an unmatched glob does (.github#2510)",
            )

        # (4) non-vacuity, per root: a root that exists but holds no boards checked nothing.
        require(
            here > 0,
            f"read 0 file(s) under {scan_root.relative_to(root)}: that root exists but holds no board "
            "host loops, so a missing analyst dispatch step in it would clear by default",
        )

    require(scanned > 0, "read 0 authored file(s) in total, so this gate proved nothing")
    require(
        len(dispatchers) == len(roots) * len(BOARDS),
        f"only {len(dispatchers)} of {len(roots) * len(BOARDS)} board host loop(s) carry the dispatch "
        "step; every declared runtime must carry it, or one runtime's host reads a loop that never "
        "collects a packet (.github#2675)",
    )

    contracts = check_finder_contract(root, roots)
    required = check_trigger(root, roots)

    # The success line states the BASIS of its answer — how many loops, variants, files, and trigger
    # paths — so a green resting on a blind sweep is legible in a log.
    print(
        f"analyst-dispatch: {len(dispatchers)} board host loop(s) across {len(roots)} root(s) carry the "
        f"canonical owned-finding guidance or redirect, {len(variants)} routed variant(s) restate none of it, "
        f"{contracts} finder contract(s) keep the direct-owner branch with no analyst required to "
        f"override, {scanned} file(s) were read, and {len(required)} trigger path(s) keep CI reaching "
        "all of it"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except Finding as finding:
        print(f"analyst-dispatch: {finding}", file=sys.stderr)
        sys.exit(1)
