#!/usr/bin/env python3
"""Assert the retired `--engine=` flag survives only where it is HISTORY, never as a live mode.

.github#917, epic #266 (the rule nothing asserts). The fourth hand-repair of one class.

THE CLASS THIS CLOSES: *the narrative outlived the thing it described.* `--engine=fs` selected the
typed engine while bash was still authoritative (ADR-0034). ADR-0040 finished the port — the engine
BECAME the client — and the flag went with the choice it existed to express. It is not merely absent:
`Options.fs` does not parse `--engine` at all, and `OptionsTests.fs` pins it as the specimen of an
unknown flag that must be NAMED and refused. Yet doc comments went on stating a present-tense
consequence "under `--engine=fs`", as though a worker could still select it.

WHY A GATE, AND NOT A FIFTH GREP. Each retirement pass was scoped by a `Paths:` touch-set that
structurally could not see the next tier, so each tier was found BY ACCIDENT, by whoever happened to
read the file months later:

    #836        kit skills, the design doc, the `Divergence` code   — missed the next tier: its Paths
    #866/#910   docs/architecture.md                                — missed the next tier: its Paths
    #912        .github/workflows/                                  — missed the next tier: its Paths
    #917        src/ + tests/ doc comments                          — this one

#912's own title is the sentence this item would otherwise write about #912, forever. Four passes,
each correct, each blind past its own declaration. A rule enforced by whoever happens to remember it
is a rule that decays — and this one had decayed three times before anybody gated it.

WHY IT IS MECHANISABLE *HERE*, when "stale narrative" in general is a judgement call: the flag DOES
NOT EXIST. There is no live use to distinguish from a dead one, so any `--engine=` outside the
allowlist below is wrong by construction. No judgement required, which is exactly what makes it a
gate rather than a review checklist.

THE SURFACE IS THE WHOLE REPO. That is load-bearing, and it is the entire lesson of the four passes
above. A gate scoped to `src/` + `tests/` — the tier that happened to be found this time — would be
the fifth instance of the very bug it closes: it would report green while tier five grew somewhere it
was never told to look. So this walks every text file in the tree and allowlists the few places the
flag legitimately appears, rather than listing the places it must not. The default is FORBIDDEN, and
a new directory is covered the day it is created, by nobody's remembering.

For the same reason there is no extension allowlist. Deciding up front which suffixes can carry
narrative is the touch-set mistake wearing a different hat: the four tiers above spanned Markdown,
YAML, F# and shell, and tier five is by definition the one nobody predicted. Anything that decodes as
text is audited; anything that does not carries no narrative to audit.

WHAT IS DELIBERATELY NOT THE BUG — the allowlist, and why each entry is there:

  - `docs/adr/**` — ADRs are immutable historical records. ADR-0034/0038/0040 SHOULD say
    `--engine=fs`: they are the documents that introduced, gated and retired it. An ADR edited to
    remove it would be a falsified record, which is a worse defect than the one this gate closes.

  - `docs/design/coordination-engine.md` — the plan-and-retrospective record, already handled by
    #836, which added the explicit "what actually landed" sections (`--engine=fs` … "went with the
    bash they chose between"). Its plan sections are a record OF THE PLAN, correct in the past tense.

  - `tests/FS.GG.Coord.Cli.Kernel.Tests/OptionsTests.fs` (in `tests/FS.GG.Coord.Cli.Tests/` before
    .github#2725 moved it with its module; `CANARY_FILE` below is the single live spelling) — it
    asserts the REJECTION. The string is the
    test's subject: `parse [ "decide"; "--engine=fs" ] |> rejected`. Strip it and the assertion that
    the flag is refused stops existing, which is the one test standing between this gate and a flag
    that quietly comes back.

  - this gate, its workflow, and its fixture — the three files that must NAME the forbidden string in
    order to forbid it. The docstring you are reading quotes it; the finding message below quotes it;
    the fixture feeds it in on purpose to prove the gate can still say NO. All three are the rule
    being STATED, not violated. This is the same carve-out `check-worker-id-attractor.py` makes for
    the counter-example in its own docstring.

The gate is matched by BASENAME, not by `__file__`, for the reason that gate learned: the fixture
audits a COPY of the shipped tree, and a path-identity check would exempt the running gate while
red-lighting its own copy — passing on the real tree and failing on a verbatim copy of it.

FAILS CLOSED (epic #266). Auditing zero files, or finding no mention of the flag ANYWHERE, is a
failure to audit rather than a clean audit: the allowlisted sites demonstrably carry it — the ADRs
and the rejection test are built out of it — so an audit that sees none means the walker or the regex
is broken, and every "ok" it printed is worthless. If the glob breaks, the gate goes RED, not green.

THIS GATE IS PURE AND OFFLINE, so it never exits 2. It reads the working tree and nothing else: no
API, no network, no credentials. There is no condition under which "try again" is the right advice,
and a gate that can emit a retryable verdict it can never mean is a gate whose exit-code contract
lies. Exit 2 is therefore deliberately absent from the vocabulary rather than reserved.

Usage:
  check-engine-flag-narrative.py [--root <dir>]
Exit: 0 = the flag appears only where it is history; 1 = a live-mode mention outside the allowlist;
3 = no verdict, PERMANENT — an unreadable root, or an audit that examined nothing.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320).
"""
from __future__ import annotations

import argparse
import re
import sys
import traceback
from pathlib import Path, PurePosixPath

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

# The retired flag, in every spelling. `--engine=fs` is the one #917 found, but the WHOLE flag went
# with ADR-0040 — `--engine=bash` and `--engine=auto` name modes that are equally gone, and gating
# only the spelling that happened to be found this time is the touch-set mistake again. A bare
# `--engine` is matched too: it is the flag itself, and it parses no better than its values.
#
# The trailing guard keeps this off longer flags that merely start with the same letters: a future
# `--engine-log=x` is a different flag and not this gate's business.
ENGINE_FLAG = re.compile(r"--engine(?:=(?P<value>[A-Za-z][\w.-]*))?(?![\w-])")

# Directories that are not source: version control, and build output. `bin/`/`obj/` carry a COPY of
# every doc comment (the generated `*.xml` docs, and .fs the compiler emits), so a finding there is
# one nobody can act on — it is a reflection of a finding we already report at its real site.
EXCLUDE_DIRS = frozenset((".git", "bin", "obj", "node_modules"))

# WHERE THE FLAG IS HISTORY, AND MUST STAY. See the module docstring for why each entry is here.
# Paths are repo-relative POSIX; a trailing "/" means "this directory and everything under it".
ALLOWED_PREFIXES = (
    # ADRs are immutable records: they introduced, gated and retired the flag.
    "docs/adr/",
    # The gate's own fixture must feed the forbidden string in to prove the gate can say NO.
    "tests/engine-flag-narrative/",
)
ALLOWED_FILES = frozenset(
    (
        # The plan-and-retrospective record (#836 added its "what actually landed" sections).
        "docs/design/coordination-engine.md",
        # Asserts the REJECTION — the string is the test's subject, not its narrative.
        # It moved to the kernel suite with `Options` itself at .github#2725; a stale entry here does
        # not fail open, it fails LOUD (the file stops being exempt and its two deliberate mentions
        # become findings), which is the right direction for an allow-list to fail in.
        "tests/FS.GG.Coord.Cli.Kernel.Tests/OptionsTests.fs",
        # States the rule in its own summary prose.
        ".github/workflows/engine-flag-narrative.yml",
        # TIER FIVE — found by this gate, on the day it was written, in the tree #917 declared clean.
        # Two dated records of the port that retired the flag. Both are `Status: COMPLETE` manifests
        # whose mentions NARRATE the removal ("`--engine=bash` is gone *because there is no bash left
        # to be*") and quote the deleted `51-fs-flip.sh` assertions VERBATIM, which is what a
        # disposition manifest is for — ADR-0040 D.4 required the retirement be "recorded, not
        # silent", and these are that record. Rewording them would falsify it, exactly as it would an
        # ADR. They are the vindication of the whole-repo surface: four hand-passes never looked here.
        "docs/2026-07-15-phase-d-corpus-through-shim-plan.md",
        "docs/2026-07-16-d4-differential-disposition.md",
    )
)
# LISTED, rather than matched as `docs/<date>-*.md`. A dated record IS historical by convention, so a
# pattern is tempting and it fails in the wrong direction: it would green a future record that
# described the flag as LIVE, silently. The explicit list goes stale the safe way — a new record that
# needs the flag reds this gate once, and a human confirms it is history before adding a line. That
# friction is the check. Note `docs/` at large is deliberately NOT allowlisted: `docs/architecture.md`
# was tier two (#866/#910), so a blanket `docs/` rule would re-open the tier it closed.

# This gate NAMES the forbidden string in order to forbid it. Matched by basename so the fixture's
# copy of the shipped tree is exempt on the same terms as the original — a path-identity check would
# pass on the real tree and red-light a verbatim copy of it.
SELF_BASENAME = "check-engine-flag-narrative.py"

# ---- THE TWO FAIL-CLOSED GUARDS, AND WHY ONE OF THEM IS NOT ENOUGH -------------------------------
#
# The obvious non-vacuity guard is "did we see the flag at all?" — and on THIS gate it fails open,
# which was found by driving it rather than by reading it. The ADRs carry ~70 mentions and are
# allowlisted, so a walk that silently stopped covering `src/` and `tests/` still counts those and
# still prints a confident green over a tree it never read. Measured: pruning `src`/`tests` reports
# `ok … 77 mention(s) audited`, rc=0. That is #266's signature exactly, and .github#930's sub-class
# (b) — the subject is not covered, the check is satisfied, the gate stays green — which is the very
# family this gate belongs to. A gate that fails the way its own class fails is not a gate.
#
# The deeper trap: once the repair lands, `src/` legitimately carries ZERO mentions. So "was `src/`
# audited?" CANNOT be answered by looking for a mention in it — absence is the correct state, and
# absence is also what a broken walker produces. The two are indistinguishable by counting mentions,
# which is why a second guard has to assert COVERAGE rather than content.
#
# 1. COVERAGE. A directory that exists must have been WALKED. This is a floor, never a ceiling: it
#    does not limit what is scanned (the surface is still the whole tree), so a new directory needs
#    no entry here and is audited the day it is created. It only asserts that the big, known trees
#    were actually reached — the one thing a silent prune or a broken walk takes away.
REQUIRED_COVERAGE = ("src", "tests", "docs", "scripts", ".github")

# 2. CONTENT. A file that demonstrably CARRIES the flag must be seen carrying it — proof the reader
#    and the pattern still work, not just that the walk reached the file. The rejection test is the
#    honest canary: its whole subject is the string, so it can never legitimately stop carrying it
#    (a version of it that does not mention the flag has stopped asserting the flag is refused, which
#    is itself the finding). This mirrors `check-worker-id-attractor.py`'s `engine_sites` guard.
#
#    THIS CONSTANT IS A HARD-CODED SUBJECT PATH, AND UNTIL .github#2725 ITS GUARD WAS DISARMED BY THE
#    ONE EVENT IT EXISTS TO SURVIVE. The guard read `if CANARY_FILE.is_file() and canary_mentions == 0`,
#    so a canary path that had gone STALE — the file moved, which is the commonest way a hard-coded path
#    stops being right — made the first conjunct false and skipped the assertion entirely. Measured:
#    with `CANARY_FILE` still naming the pre-.github#2725 location, this gate printed
#    `ok: … 85 mention(s) audited` and exited 0 while its content guard checked nothing at all. The same
#    inverted shape was found and repaired in `check-worker-id-attractor.py` in the same change; see the
#    guard below for how the condition is now written.
CANARY_FILE = "tests/FS.GG.Coord.Cli.Kernel.Tests/OptionsTests.fs"


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def is_allowed(rel: str) -> bool:
    """Is this file one of the few places the flag is legitimately history?"""
    if Path(rel).name == SELF_BASENAME:
        return True
    if rel in ALLOWED_FILES:
        return True
    return any(rel.startswith(p) for p in ALLOWED_PREFIXES)


def walk(root: Path) -> list[tuple[Path, str]]:
    """Every file in the tree, minus version control and build output.

    PRUNES at the directory rather than walking everything and discarding: on a built checkout
    `bin/`/`obj/` hold thousands of artifacts, and walking them only to throw them away is work that
    grows with the build output instead of with the source.
    """
    files: list[tuple[Path, str]] = []
    stack = [root]
    while stack:
        try:
            entries = sorted(stack.pop().iterdir())
        except OSError as e:
            raise GateError(f"cannot list a directory under {root}: {e}") from e
        for p in entries:
            if p.is_symlink():
                # A symlink's target is audited at its own real path, if it is in the tree at all.
                continue
            if p.is_dir():
                if p.name not in EXCLUDE_DIRS:
                    stack.append(p)
            elif p.is_file():
                files.append((p, p.relative_to(root).as_posix()))
    if not files:
        raise GateError(
            f"found NO files under {root}. Examining nothing is a failure to audit, not a clean "
            f"audit (#266)."
        )
    return sorted(files, key=lambda t: t[1])


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="repo root (default: .)")
    args = ap.parse_args(argv)
    root = Path(args.root).resolve()
    if not root.is_dir():
        print(
            f"::error::check-engine-flag-narrative: no verdict — root '{root}' is not a directory",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT

    findings: list[str] = []
    mentions = 0  # every mention, allowlisted or not — the non-vacuity subject.
    canary_mentions = 0  # ...in the one file whose subject IS the string.
    walked: set[str] = set()  # top-level dirs actually reached, for the coverage guard.

    for path, rel in walk(root):
        top = rel.split("/", 1)[0]
        walked.add(top)
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            # Not text, or unreadable: it carries no narrative to audit.
            continue

        allowed = is_allowed(rel)
        for lineno, line in enumerate(text.splitlines(), 1):
            for m in ENGINE_FLAG.finditer(line):
                mentions += 1
                if rel == CANARY_FILE:
                    canary_mentions += 1
                if allowed:
                    continue
                spelling = m.group(0)
                findings.append(
                    f"{rel}:{lineno}: `{spelling}` names a flag that DOES NOT EXIST. ADR-0040 "
                    f"finished the port — the engine became the client — and `--engine` went with "
                    f"the choice it expressed; `Options.fs` does not parse it, and `OptionsTests.fs` "
                    f"pins it as the specimen of an unknown flag that must be refused. A doc comment "
                    f"that states a present-tense consequence 'under {spelling}' describes a mode no "
                    f"worker can select. Reword it in the PAST tense, naming the flag as removed — or "
                    f"drop the clause: since the engine IS the client, the consequence is no longer "
                    f"conditional on anything. This narrative has been retired BY HAND four times "
                    f"(#836, #866/#910, #912, #917), each pass blind past its own touch-set."
                )

    # FAIL CLOSED on the real subject. The allowlisted sites are BUILT out of this flag — three ADRs
    # introduce/gate/retire it, and a test asserts its rejection. If the walk found no mention of it
    # anywhere at all, the walker or ENGINE_FLAG is broken, and the clean report above is worthless
    # rather than good news. This is the guard that makes a broken glob RED instead of green.
    if mentions == 0:
        raise GateError(
            "audited the tree and found NO mention of `--engine` at all, in any file. The ADRs and "
            "the rejection test demonstrably carry it, so the walker or the pattern is broken — "
            "examining nothing is a failure to audit, not a clean audit (#266)."
        )

    # COVERAGE (guard 1). The global count above is satisfied by the ADRs alone, so on its own it
    # would green a walk that silently stopped covering the source tree. Assert instead that every
    # known tree that EXISTS was actually reached — the one claim a silent prune falsifies, and the
    # one that a post-repair `src/` (legitimately zero mentions) cannot make by counting content.
    for d in REQUIRED_COVERAGE:
        if (root / d).is_dir() and d not in walked:
            raise GateError(
                f"'{d}/' exists in the tree but the walk never reached a file in it, so this audit "
                f"has no opinion about it — and `{d}/` is exactly where a live-flag narrative would "
                f"hide. The ADRs alone satisfy the mention count, so a green here would be a "
                f"confident report over a tree that was never read (#266). The prune list or the "
                f"walk is broken, not the tree."
            )

    # CONTENT (guard 2). Coverage proves the walk reached the file; it does not prove the reader or
    # the pattern still work. The rejection test's entire subject is this string, so if we did not see
    # the flag in it, ENGINE_FLAG or the decoder is broken — and every "ok" above is worthless rather
    # than good news.
    #
    # THE SKIP CONDITION IS THE PROJECT, NOT THE FILE (.github#2725), AND THAT IS THE WHOLE REPAIR.
    # It used to be `CANARY_FILE.is_file() and canary_mentions == 0`, which disarms the guard in exactly
    # the case it exists for: a canary path that has gone stale because the file MOVED. The intent it
    # was written with is still honoured — a synthetic or foreign tree with no such suite in it has
    # genuinely nothing to be canary of, and must not red — but that intent is served by asking whether
    # the canary's PROJECT is here, which a fixture tree answers "no" to and this repository answers
    # "yes" to whether or not the constant is still correct.
    canary_project = str(PurePosixPath(CANARY_FILE).parent)
    if (root / canary_project).is_dir() and not (root / CANARY_FILE).is_file():
        raise GateError(
            f"{canary_project}/ is here but {CANARY_FILE} is not, so the content guard below examined "
            f"NOTHING while every leg above reported green. A hard-coded canary path whose absence "
            f"skips its own assertion is a check that cannot fail (#266). Repoint CANARY_FILE."
        )
    if (root / CANARY_FILE).is_file() and canary_mentions == 0:
        raise GateError(
            f"found NO mention of `--engine` in {CANARY_FILE}, whose whole subject is that string "
            f"(it asserts the flag is REFUSED). The pattern or the reader is broken — examining "
            f"nothing is a failure to audit, not a clean audit (#266)."
        )

    if findings:
        for f in findings:
            print(f"::error::check-engine-flag-narrative: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} finding(s). The retired-flag narrative has been removed by hand four "
            f"times already (#836, #866/#910, #912, #917) — every pass scoped by a touch-set that "
            f"could not see the next tier. This gate exists so there is no fifth.",
            file=sys.stderr,
        )
        return FINDING

    print(
        f"ok: `--engine` appears only where it is history — {mentions} mention(s) audited across the "
        f"tree, all inside the allowlist (docs/adr/, the design record, the rejection test, and this "
        f"gate's own three files)."
    )
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a dead flag is being described
    as live". A crash would therefore be dressed up as a specific, confident, WRONG claim about
    somebody's doc comment. "I could not check" must never share a code with "I checked, and it's
    broken" (#266, #320).
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-engine-flag-narrative: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-engine-flag-narrative: the gate crashed, so it has NO VERDICT. This is "
            "not a finding about any file — it is a bug in the gate. See the traceback above.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
