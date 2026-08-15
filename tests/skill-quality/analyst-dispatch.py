#!/usr/bin/env python3
"""Fail closed when the board host loops stop dispatching the analyst (.github#2675).

`.github#2584` landed the `board-analyst` role and codified it on two of the three sides it needs.
`pnext-item/references/findings-and-filing.md` tells a finder that where an analyst is available it
**does not file** — it posts an `fsgg:finding-packet` comment and moves on. `board-analyst/SKILL.md`
specifies what the role does once invoked, from the callee's view, opening "Whichever route dispatches
you". Nothing on the host side dispatched it: over `origin/main` at `cbc7bfd3`, every file under
`skills/{drive-board,work-board,p-add,padd-item,check-board,lane-steward}` searched for
`board-analyst|fsgg-analyst|analyst` returned ZERO matches.

That is worse than the state before the role existed, not merely incomplete. Before, a finding that
cleared the finder's bar became a row a scheduler could see; after, the same finding becomes a comment
with no reader and no owner. It went unnoticed because a human host adjudicated by hand throughout the
session that built it, so the loop appeared to work end to end.

Four legs, all fail-closed:

1. DISPATCH STEP PRESENT. Every canonical board host-loop reference — `drive-board` and `work-board`,
   under every declared skill root — carries `CANONICAL` exactly once and byte-identically. That is
   where the existing critic-dispatch contract lives, so the two dispatch rules sit together and a
   reader of either reaches both. Pinning the exact bytes is what stops the step being diluted into a
   suggestion, which is the failure `review-round-contract.py` documents for the review contract.

2. NOT RESTATED INTO THE ROUTED VARIANTS. The `-best`/`-normal` variants change MODEL ROUTING ONLY and
   inherit their canonical parent's protocol wholesale; restating protocol into them is the drift
   `.github#485` names. So no file under a variant directory may name the analyst vocabulary at all —
   not the canonical block, and not a reworded near-copy of it. The variant set is DERIVED by globbing
   `<board>-*`, so a future `-turbo` route is covered the day it is added rather than the day someone
   remembers this gate.

3. THE NO-ANALYST BRANCH SURVIVES. `findings-and-filing.md` ships in the coordination kit to seven
   receivers and NO receiver carries a `board-analyst`: it is `scope: operator` with the never-true
   predicate, so it materializes nowhere and resolves only in an operator checkout. That makes the
   finder-files branch the whole design for every repository except this one. This leg asserts both
   halves — the branch text is still in the shipped file, and the registry still says the analyst
   reaches no receiver — because either one silently flipping strands every kit consumer. The file is
   read, never written: it is kit source under an operator-gated release, and this row does not
   republish it.

4. NON-VACUITY AND TRIGGER. This gate's subject is source text, so it can pass because it examined
   nothing (`.github#2551`, and the zero-file gate `.github#2510` filed). `self_test` runs the real
   detectors over synthetic documents, in this process, before any tree is trusted; every declared
   root must resolve, hold markdown, and actually yield the boards; and every scanned root plus this
   file's own directory must appear under BOTH `pull_request` and `push` in the workflow, DERIVED from
   what is scanned rather than hardcoded. A gate that does not run when its own subject changes is
   epic #266's fail-open class.

Usage: analyst-dispatch.py [--root DIR]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

# The one canonical statement of the dispatch step. Byte-identical in every canonical board host-loop,
# and in no other authored file. Changing a byte of it here means changing it in all four host-loops in
# the same commit — that coupling is the point, and it is what keeps `drive-board`'s loop and
# `work-board`'s from drifting into two different answers to the same question.
CANONICAL = """**Collect the finding packets, then dispatch the analyst — at the re-plan boundary, not inside a wave.**
`pnext-item`'s findings-and-filing contract routes a finder that has established a distinct cause to post
an `fsgg:finding-packet` comment INSTEAD of filing, wherever a `board-analyst` resolves. Nothing waits on
that packet, by design — which is exactly why a loop with no collection step is worse than the filing it
replaced: the finding becomes a comment with no reader and no owner, where before it became a row a
scheduler could see (`.github#2675`). So the step is owned here, beside critic dispatch, and it runs on
the same boundary as the post-wave reconcile and re-triage: after this wave's merges are verified, before
the next wave is sized. Hand the analyst the packets you collected and nothing else — it adjudicates what
it is handed, it never re-derives a packet, and it never dispatches, claims, or merges.

- **Collect without a board scan.** One REST read reaches every packet: the repository-wide issue-comments
  listing, bounded by the previous boundary's timestamp —
  `gh api -X GET repos/<owner>/<repo>/issues/comments -f since=<previous-boundary> -f per_page=100`.
  It returns comments on issues AND on pull requests in one paginated call, which is both of the surfaces
  a packet is allowed to live on, and it never fans out per issue. That spelling is load-bearing rather
  than a preference: a `scan` costs more than the pass it would be deciding about, and
  `scripts/fsgg-coord issues` cannot stand in for it — that command reads the issue LIST endpoint, which
  carries a comment COUNT and no comment bodies, and it drops pull requests outright
  (`src/FS.GG.Coord.GitHub/Reads.fs`).
- **The analyst occupies no slot, and that is a stated exemption with its cost, not silence.** It holds no
  claim, takes no lane, and blocks no chain — `board-analyst` may never `claim`, `take`, or `release` — so
  it can consume neither an implementer slot nor one of the reserved critic slots, and the generated
  policy above is unchanged by it. What it does spend is the one shared REST budget that also holds the
  claim lock, at one `scan` per pass. Bound it exactly there: at most one analyst pass per boundary, never
  two at once, and none at all while an `EX_RATE` backoff is in effect.
- **Where no analyst resolves, the step is a no-op and there are no packets to collect.** `board-analyst`
  is `scope: operator` and materializes nowhere, so it resolves only in an operator checkout; everywhere
  else findings-and-filing's other branch governs and the finder files its own row. An empty collection is
  that branch reporting itself, not a broken loop."""

# The vocabulary that makes prose ABOUT analyst dispatch. Leg 2 forbids all of it inside a routed
# variant: the canonical block names every one of these, so a variant that pasted the block reds, and so
# does a variant that paraphrased it into one sentence of its own.
VOCABULARY = re.compile(r"fsgg:finding-packet|finding packet|board-analyst|fsgg-analyst", re.IGNORECASE)

SKILL_ROOT_DECLARATION = ".agent-skill-roots"
# The canonical board skills. Their routed variants are DERIVED from these by glob, never listed.
BOARDS = ("drive-board", "work-board")
HOST_LOOP = "references/host-loop.md"

# Leg 3. The finder-side contract every kit receiver reads, and the sentence that carries its
# no-analyst branch. Read-only here: this is kit source under an operator-gated release (.github#2648).
FINDER_CONTRACT = "pnext-item/references/findings-and-filing.md"
NO_ANALYST_BRANCH = (
    "**Where no analyst is available**, the finder files — and applies the same three tests to itself,"
)
REGISTRY = "registry/skills.yml"
# `materializes-when` is ADR-0017's never-true literal, quoted in YAML. If this ever became `always`,
# `board-analyst` would start reaching receivers and the branch above would become dead prose there.
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
            f"{relative} no longer carries its no-analyst branch verbatim. That branch is the whole "
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
        "no-analyst branch asserted above is correct only while the analyst materializes nowhere; if "
        "it now reaches receivers, the finder-side contract needs re-deciding, not this gate's green",
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
            occurrences = carries(host_loop.read_text(encoding="utf-8"))
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
        f"canonical dispatch step verbatim, {len(variants)} routed variant(s) restate none of it, "
        f"{contracts} finder contract(s) keep the no-analyst branch an analyst reaches no receiver to "
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
