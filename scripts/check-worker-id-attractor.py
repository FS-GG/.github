#!/usr/bin/env python3
"""Assert no protocol doc or skill hands a worker an id to copy, or a second way to mint one.

.github#570, epic #266 (the rule nothing asserts). Found while working .github#551.

THE DEFECT THIS CLOSES. #419 found the collision attractor: agents asked to pick a worker id
converge on the same corner of the name space, and an id two workers share is an id the claim lock
cannot separate — `release` drops the other's claim mid-flight, `heartbeat` renews a marker that is
not yours, `say`/`inbox` cross-deliver. This board carried FOUR `finch-*` workers at once, every one
of them lifted from the example that used to sit in the recipe. The attractor is the WORD, not the
suffix: randomising `-a3f` does not help if the next reader still reaches for the bird they just
read.

It has now been removed BY HAND TWICE. #532 took it out of the four SKILL.md files; it grew straight
back in `docs/coordination/parallel-work.md` — the document those skills are a projection OF — and
#551 took it out a second time. Twice removed, twice by hand, because nothing asserts it. #551's own
acceptance criteria were literally two greps in an issue body, and a rule enforced by whoever happens
to remember it is a rule that decays. It had already decayed once, in the window between #532 and
#551.

So: gate it.

WHAT IT ASSERTS, over every Markdown file in the agent-skill roots and in docs/:

  1. NO LITERAL WORKER ID in a copyable position. Both spellings, because the commit-trailer example
     is how one of the two survived #532:

         FSGG_WORKER=w-4f2a91c7            <- a shell assignment
         FSGG-Worker: w-4f2a91c7           <- the git trailer

     A PLACEHOLDER is fine and is the whole point — `<id>`, `<the id claim printed>`, `$FSGG_WORKER`.
     Nobody pastes a placeholder and collides with anybody. A CONCRETE id is a loaded gun on the
     page: the next worker copies the line, and now two workers share a lock.

  2. EXACTLY ONE MINT IDIOM, and it is `eval "$(scripts/fsgg-coord whoami --mint)"`. Any other way
     to conjure an id is a finding:

         - a rival substitution on the right of `FSGG_WORKER=` (a hand-rolled mint);
         - a randomness primitive anywhere in the surface (`od -An`, `/dev/urandom`, `uuidgen`,
           `openssl rand`, `$RANDOM`, `shuf`). There is exactly one sanctioned source of worker-id
           randomness and it lives INSIDE fsgg-coord, so a doc that shows one is, definitionally, a
           second idiom. That is the check #551 wrote as `grep -rn 'od -An -tx1'`.

  3. THE SANCTIONED IDIOM IS STILL TAUGHT. "Exactly one" has a floor as well as a ceiling: a surface
     that no longer shows anyone how to mint an id is a surface whose readers will invent one, which
     is #419 again from the other end.

FAILS CLOSED (epic #266). Auditing zero files is a failure to audit, not a clean audit — and so is
auditing files in which the extractor finds no worker-id mention at all, because these documents
demonstrably carry them. If the glob breaks, the gate goes RED, not green.

THIS GATE IS PURE AND OFFLINE, so it never exits 2. It reads the working tree and nothing else: no
API, no network, no credentials. There is no condition under which "try again" is the right advice,
and a gate that can emit a retryable verdict it can never mean is a gate whose exit-code contract
lies. Exit 2 is therefore deliberately absent from the vocabulary rather than reserved.

Usage:
  check-worker-id-attractor.py [--root <dir>] [--surface <dir> ...]
Exit: 0 = no attractor and one mint idiom; 1 = a literal id, a rival mint, or no mint taught at all;
3 = no verdict, PERMANENT — a missing surface directory, or an audit that examined nothing.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320).
"""
from __future__ import annotations

import argparse
import re
import sys
import traceback
from pathlib import Path

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

# The roots are READ FROM `.agent-skill-roots`, not hardcoded (#517, as skill-union-assert.sh and
# check-recipe-pagination.py do). A root added there is audited without touching this file; a private
# copy of the list would go stale silently, and a root nobody audits is a root the attractor can grow
# back in while this gate reports green.
ROOTS_DECL = ".agent-skill-roots"
FALLBACK_ROOTS = (".claude/skills", ".agents/skills")

# ...plus the protocol docs. THIS IS NOT OPTIONAL: docs/coordination/parallel-work.md is the document
# the skills are a projection of, and it is exactly where the attractor grew back after #532 removed
# it from the skills alone. A gate over the skill roots only would have reported green through the
# entire window that #551 was needed to close.
DOC_SURFACE = ("docs",)

# The ONE sanctioned mint. `fsgg-coord whoami --mint` prints one shell line and nothing else on
# stdout, so `eval "$(...)"` is the whole ritual.
SANCTIONED_MINT = re.compile(r"fsgg-coord\s+whoami\s+--mint")

# What an actual worker id looks like: a word, a hyphen, and a hex-ish tail (`w-4f2a91c7`,
# `finch-a3f`). Anything with `<`, `$`, or a backtick in it is a placeholder or a substitution, and
# is the correct thing for a recipe to show.
WORKER_ID = r"[A-Za-z][A-Za-z0-9]*-[0-9a-fA-F]{3,}"

# `FSGG_WORKER=<value>` (optionally `export`ed, optionally quoted) and the `FSGG-Worker: <value>`
# git trailer. Captured separately so a finding can say WHICH spelling leaked.
ASSIGNMENT = re.compile(r"FSGG_WORKER\s*=\s*(?P<q>[\"']?)(?P<value>[^\"'\s]*)")
TRAILER = re.compile(r"FSGG-Worker:[ \t]*(?P<q>[\"']?)(?P<value>[^\"'\s]*)")

# Any mention at all — used only to prove the extractor is not silently seeing nothing.
ANY_MENTION = re.compile(r"FSGG_WORKER|FSGG-Worker")

# What actually MINTS: a command substitution. `$(…)` or a backtick. A bare `$VAR` / `${VAR}`
# expansion does not mint — `FSGG_WORKER=$FSGG_WORKER cmd` forwards an id the worker already holds.
MINTS_SOMETHING = re.compile(r"\$\(|`")

# A second source of randomness is a second mint idiom, whatever it is dressed as.
RIVAL_RANDOMNESS = re.compile(
    r"od\s+-An|/dev/urandom|uuidgen|openssl\s+rand|\$RANDOM\b|\bshuf\b", re.IGNORECASE
)


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def is_literal(value: str) -> bool:
    """Is this right-hand side a concrete id somebody could paste?

    A placeholder (`<id>`), a substitution (`$(…)`, `$FSGG_WORKER`), a backticked reference, or an
    empty value is exactly what a recipe SHOULD show, and is not a finding. Anything that reads as a
    bare, concrete id is.
    """
    if not value:
        return False
    if value[0] in "<$`({[" or "$" in value:
        return False
    return re.fullmatch(WORKER_ID, value) is not None


def surface_files(root: Path, surfaces: list[str]) -> list[tuple[Path, str]]:
    files: list[tuple[Path, str]] = []
    for d in surfaces:
        base = root / d
        if not base.is_dir():
            raise GateError(
                f"surface directory '{d}' does not exist under {root} — the glob is broken, and a "
                f"gate that audits a directory it cannot find would report green over anything"
            )
        for p in sorted(base.rglob("*.md")):
            files.append((p, str(p.relative_to(root))))
    if not files:
        raise GateError(
            f"found NO Markdown files under {', '.join(surfaces)}. Examining nothing is a failure "
            f"to audit, not a clean audit (#266)."
        )
    return files


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="repo root (default: .)")
    ap.add_argument(
        "--surface", action="append", default=None,
        help=f"directory to audit, repeatable (default: the roots in {ROOTS_DECL}, plus docs/)",
    )
    args = ap.parse_args(argv)
    root = Path(args.root).resolve()

    if args.surface:
        surfaces = args.surface
    else:
        decl = root / ROOTS_DECL
        if decl.is_file():
            roots = [
                ln.strip() for ln in decl.read_text(encoding="utf-8").splitlines()
                if ln.strip() and not ln.lstrip().startswith("#")
            ]
            if not roots:
                raise GateError(f"{ROOTS_DECL} declares no roots")
        else:
            roots = list(FALLBACK_ROOTS)
        surfaces = roots + list(DOC_SURFACE)

    findings: list[str] = []
    mentions = 0
    sanctioned = 0

    for path, rel in surface_files(root, surfaces):
        try:
            text = path.read_text(encoding="utf-8")
        except OSError as e:
            raise GateError(f"cannot read {rel}: {e}") from e

        for lineno, line in enumerate(text.splitlines(), 1):
            if ANY_MENTION.search(line):
                mentions += 1
            if SANCTIONED_MINT.search(line):
                sanctioned += 1

            # 1. A concrete id, in either spelling.
            for kind, rx in (("FSGG_WORKER=", ASSIGNMENT), ("FSGG-Worker:", TRAILER)):
                for m in rx.finditer(line):
                    value = m.group("value")
                    if is_literal(value):
                        findings.append(
                            f"{rel}:{lineno}: `{kind}{value}` is a LITERAL worker id, on a line a "
                            f"worker can paste. Ids picked by reading converge (#419: four `finch-*` "
                            f"workers at once, all from the example on this page), and two workers "
                            f"sharing an id is an id the claim lock cannot separate. Show a "
                            f"placeholder (`<id>`) or the mint, never a usable id."
                        )

            # 2a. A rival mint on the right of the assignment.
            #
            # A COMMAND SUBSTITUTION is what mints something. A bare variable expansion does not —
            # `FSGG_WORKER=$FSGG_WORKER cmd` forwards the id a worker already has, which is the
            # opposite of conjuring a new one, and flagging it would be the gate crying wolf at the
            # very idiom the protocol depends on.
            for m in ASSIGNMENT.finditer(line):
                value = m.group("value")
                if MINTS_SOMETHING.search(value) and not SANCTIONED_MINT.search(line):
                    findings.append(
                        f"{rel}:{lineno}: `FSGG_WORKER=` is assigned from a command substitution "
                        f"that is not the sanctioned mint. There is exactly ONE way to mint a worker "
                        f"id — `eval \"$(scripts/fsgg-coord whoami --mint)\"` — and a second idiom is "
                        f"a second thing to keep correct, in a place nobody looks."
                    )

            # 2b. A randomness primitive anywhere in the surface IS a second mint idiom: the only
            #     sanctioned source of worker-id randomness lives inside fsgg-coord.
            if (m := RIVAL_RANDOMNESS.search(line)):
                findings.append(
                    f"{rel}:{lineno}: `{m.group(0)}` is a hand-rolled source of randomness. The mint "
                    f"is a solved problem with exactly one idiom, "
                    f"`eval \"$(scripts/fsgg-coord whoami --mint)\"`, and it lives inside fsgg-coord "
                    f"so that it cannot drift. A doc that rolls its own is the second idiom #570 "
                    f"exists to keep out."
                )

    # Fail closed: an extractor that sees nothing must not be mistaken for a clean surface. These
    # documents demonstrably discuss the worker id — if we found no mention of it at all, the glob or
    # the regex is broken, and every "ok" above is worthless.
    if mentions == 0:
        raise GateError(
            f"audited the surface and found NO mention of a worker id at all, in any file. These "
            f"documents carry them, so the extractor is broken — examining nothing is a failure to "
            f"audit, not a clean audit (#266)."
        )

    # 3. "Exactly one" has a floor as well as a ceiling.
    if sanctioned == 0:
        findings.append(
            "the surface no longer shows the sanctioned mint "
            "(`eval \"$(scripts/fsgg-coord whoami --mint)\"`) ANYWHERE. A recipe that does not tell a "
            "worker how to mint an id is a recipe whose readers will invent one — which is #419 "
            "again, from the other end."
        )

    if findings:
        for f in findings:
            print(f"::error::check-worker-id-attractor: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} finding(s). The collision attractor has been removed by hand twice "
            f"already (#532, #551); this gate exists so there is no third time.",
            file=sys.stderr,
        )
        return FINDING

    print(
        f"ok: no literal worker id, and exactly one mint idiom — {mentions} worker-id mention(s) "
        f"audited, {sanctioned} showing the sanctioned mint."
    )
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a literal worker id is shipping
    in a recipe". A crash would therefore be dressed up as a specific, confident, WRONG claim about
    somebody's doc. "I could not check" must never share a code with "I checked, and it's broken"
    (#266, #320).
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-worker-id-attractor: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-worker-id-attractor: the gate crashed, so it has NO VERDICT. This is not "
            "a finding about any document — it is a bug in the gate. See the traceback above.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
