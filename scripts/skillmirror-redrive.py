#!/usr/bin/env python3
"""Re-derive the SkillMirror conformance table from the library, and rewrite ONLY its provenance.

.github#2521, from .github#1546/#1577/#1880, epic #266 (coherence gates that fail open).

WHY THIS EXISTS — THE RECURRENCE, NOT THE INSTANCE
  `tests/skill-union/skillmirror.fixtures.json` records what `Fsgg.SkillMirror` returns, MEASURED by
  running the library's own source (`tests/skill-union/skillmirror-oracle.sh`). That measurement is a
  DATED SNAPSHOT of files in ANOTHER repository, and `derivedFrom` is the record of which revision it
  was taken at. `scripts/check-skillmirror-freshness.py` NOTICES when that revision stops being
  current (.github#1546) — and then nothing happens, because the repair is a human re-running the
  oracle by hand and re-pinning the block.

  That is why the red recurs rather than persists. .github#1880 re-ran the oracle for
  `SkillMirror.fs` on 2026-07-29; ten days later the gate was red again on `Schemas.fs`, the second
  watched file (.github#1577), and stayed red for six consecutive scheduled runs. Re-running the
  oracle a third time by hand buys a fourth. This script is the DERIVATION half of the trigger that
  ends that loop: `.github/workflows/skillmirror-redrive.yml` runs it daily against the library's
  live default branch and opens the reconcile PR a human used to have to author.

WHAT IT WILL AND WILL NOT WRITE — AND WHY THAT LINE IS THE WHOLE SAFETY ARGUMENT
  The oracle is deliberately a CHECKER AND NOT A WRITER (see its header): "a generator that rewrote
  its own expectations from the implementation would green any divergence the moment it was
  regenerated, which is the shape of the defect, not a fix for it." Automating the re-derivation must
  not quietly convert it into one.

  So this script writes exactly one class of field: PROVENANCE — the record of WHICH library revision
  the table was measured against.

      derivedFrom.commit                          derivedFrom.libraryFiles[*]
      derivedFrom.skillMirrorFsSha256             derivedFrom.derivedOn
      digestVectors.measuredAgainst.commit        digestVectors.measuredAgainst.measuredOn
      digestVectors.measuredAgainst.skillMirrorFsSha256

  It NEVER writes a MEASURED column — `fixtures[].library`, `digestVectors[].digest` — and never adds
  or removes a `libraryFiles` key. If the oracle disagrees about any of those, the library's
  BEHAVIOUR moved, and no bot may resolve that: it is refused, loudly, as a finding for a human.
  Re-pinning provenance around a moved vector is precisely the fail-open this repo files under #266.

  THE CLASSIFIER FAILS TOWARDS REFUSAL. A `DISAGREES` line this script does not RECOGNISE is treated
  as a measured disagreement, never as a provenance one, so a future change to the oracle's wording
  makes this script refuse rather than corrupt a table.

  AND THE CLASSIFIER IS NOT WHAT MAKES IT SAFE. After writing, the oracle is RE-RUN against the same
  library and must exit 0. If the classification was wrong in the dangerous direction, the re-run is
  still red, the write is REVERTED, and the run is a finding. That check is independent of every
  string this file matches on, which is why the message-matching above is allowed to be a heuristic.

WHAT A GREEN RUN ASSERTS, AND WHY IT IS STRICTLY MORE THAN THE FRESHNESS GATE'S
  `check-skillmirror-freshness.py` compares a DIGEST: it can say the library moved and can never say
  whether the move altered `verify`'s behaviour (.github#1546's stated narrowness, restated in
  .github#2521 AC4). This script RUNS THE LIBRARY over all of the table's vectors. So a green run
  here is a fresh measurement of behaviour, not a byte comparison — and a run that finds a moved
  vector says so in as many words instead of re-pinning past it.

EXIT CODES — THE CONTRACT (scripts/lib/gate.py)
  0  OK — either the table already agrees with the library (nothing to do), or provenance was
     rewritten and the oracle then agreed on a re-run. `--write` is what separates the two; without
     it the same verdict is reached and nothing is written.
  1  FINDING — the library's MEASURED answers moved, or the oracle's output could not be classified.
     A human owns this: re-run the oracle, reconcile the vectors, and write the account.
  2  NO VERDICT (retryable) — nothing here raises it; reserved by the shared contract.
  3  NO VERDICT (permanent) — the oracle could not run, the table could not be read or edited
     safely, or the two provenance blocks name different subjects.

THE TWO QUESTIONS, AND WHY ONE TOOL ANSWERS BOTH
  `--assert-derived` asks IS THIS TABLE WHAT ITS PIN CLAIMS, against a checkout of the revision
  `derivedFrom` names. It writes nothing and treats every disagreement — provenance included — as a
  finding. That is the PR-time gate on any change to the table.
  The default mode asks WHAT DOES THE LIBRARY RETURN NOW, against a checkout of whatever revision the
  caller supplies, and re-pins provenance when that is all that moved. That is the scheduled repair.
  Neither is `check-skillmirror-freshness.py`, which asks IS THE PIN STILL CURRENT and is the only one
  of the three that reads a default branch.

usage:
  skillmirror-redrive.py --checkout DIR --commit SHA [--root DIR] [--write]
                         [--date YYYY-MM-DD] [--report PATH]
  skillmirror-redrive.py --checkout DIR --commit SHA --assert-derived [--root DIR]
  skillmirror-redrive.py --subject [--root DIR]
"""

from __future__ import annotations

import copy
import hashlib
import os
import re
import subprocess
import sys
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import (  # noqa: E402  (path shim above must run first)
    ExitCode,
    GateError,
    base_parser,
    read_text,
    report_findings,
    report_ok,
    run,
    subject_census,
)

NAME = "skillmirror-redrive"

# The table this script re-derives, and the derivation it drives. Nothing about the SUBJECT — which
# repository, which library files — is spelled here: that is read out of the table's own
# `derivedFrom` block, exactly as `check-skillmirror-freshness.py` does and for the same reason
# (#485/#865 — a fact written in two places is a fact free to disagree with itself).
TABLE = "tests/skill-union/skillmirror.fixtures.json"
ORACLE = "tests/skill-union/skillmirror-oracle.sh"
ORACLE_BODY = "tests/skill-union/skillmirror-oracle.fsx"

# WHAT THIS SCRIPT READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266).
# `check-paths-coherence.py` rule (c) reads this BY AST and reds any workflow whose `paths:` names
# this script but does not select every entry. Composed from the constants above, never retyped.
PATHS_SUBJECT = (TABLE, ORACLE, ORACLE_BODY)

SHA256 = re.compile(r"\A[0-9a-f]{64}\Z")
COMMIT = re.compile(r"\A[0-9a-f]{40}\Z")
DATE = re.compile(r"\A[0-9]{4}-[0-9]{2}-[0-9]{2}\Z")

# The oracle's disagreement lines that are PURELY about provenance — the record of which revision the
# measured columns were taken at. Matched as PREFIXES of the text after `DISAGREES  `, and this list
# is an ALLOW-LIST: anything else is a measured disagreement (see the header's fail-towards-refusal
# note). `digestVectors has no measuredAgainst block` is deliberately NOT here — a missing provenance
# BLOCK is a structural change to the table, not a stale value, and a bot inventing one would be
# manufacturing a record of a measurement nobody located.
PROVENANCE_PREFIXES = (
    "derivedFrom.skillMirrorFsSha256 is stale",
    "derivedFrom.libraryFiles[",
    "digestVectors.measuredAgainst.skillMirrorFsSha256 (",
)

DISAGREES = re.compile(r"^DISAGREES\s+(.*)$")


def sha256_file(path: str) -> str:
    """sha256 of a file's BYTES — the same quantity the table records and the oracle measures."""
    try:
        with open(path, "rb") as handle:
            return hashlib.sha256(handle.read()).hexdigest()
    except OSError as error:
        raise GateError(
            f"cannot read {path} to measure its digest ({error}). The library checkout is this "
            f"script's whole subject; a missing file is a no-verdict, never a re-pin."
        ) from error


def run_oracle(root: str, lib_dir: str) -> tuple[int, str]:
    """Run the committed derivation over the library at ``lib_dir``. Returns ``(exit code, output)``.

    stdout and stderr are merged deliberately: the oracle prints agreements on stdout and
    disagreements on stderr, and the report this script writes is worth nothing without both halves.
    """
    oracle = os.path.join(root, ORACLE)
    if not os.path.isfile(oracle):
        raise GateError(f"{ORACLE} is not on disk under {root} — there is no derivation to run.")
    try:
        proc = subprocess.run(
            ["bash", oracle, "--lib", lib_dir],
            capture_output=True,
            check=False,
            text=True,
            cwd=root,
        )
    except OSError as error:
        raise GateError(f"could not execute `bash {ORACLE}` ({error}).") from error
    output = proc.stdout + proc.stderr
    if proc.returncode not in (0, 1):
        # Exit 2 from the wrapper means it could not RUN — no dotnet, no library. That is the
        # #266 split: "I could not measure" must never be spelled like "I measured, and it agrees".
        raise GateError(
            f"`bash {ORACLE} --lib {lib_dir}` exited {proc.returncode}, which is neither agreement "
            f"(0) nor disagreement (1) — it could not run the library at all, so there is no "
            f"measurement to act on:\n{output.strip()}"
        )
    return proc.returncode, output


def classify(output: str) -> tuple[list[str], list[str]]:
    """Split the oracle's disagreements into ``(provenance, measured)``.

    An unrecognised disagreement lands in ``measured``: see the header. This function decides only
    whether a WRITE is attempted; the re-run after the write is what decides whether it stands.
    """
    provenance: list[str] = []
    measured: list[str] = []
    for line in output.splitlines():
        found = DISAGREES.match(line.strip())
        if not found:
            continue
        detail = found.group(1).strip()
        if detail.startswith(PROVENANCE_PREFIXES):
            provenance.append(detail)
        else:
            measured.append(detail)
    return provenance, measured


def load_table(root: str) -> tuple[str, str, dict]:
    """``(path, raw text, parsed document)`` for the conformance table, or a no-verdict."""
    import json  # lazy, exactly as lib.gate does with yaml: only this half pays for it.

    path = os.path.join(root, TABLE)
    raw = read_text(path, f"the SkillMirror conformance table ({TABLE})")
    try:
        return path, raw, json.loads(raw)
    except json.JSONDecodeError as error:
        raise GateError(f"{TABLE}: not parsable as JSON — {error}.") from error


def block(document: dict, *keys: str) -> dict:
    """One nested object, or a no-verdict naming the whole path that was missing."""
    node: object = document
    walked: list[str] = []
    for key in keys:
        walked.append(key)
        if not isinstance(node, dict) or not isinstance(node.get(key), dict):
            raise GateError(
                f"{TABLE}: `{'.'.join(walked)}` is not an object. This script rewrites provenance in "
                f"place and refuses to invent a block that is not there."
            )
        node = node[key]
    assert isinstance(node, dict)
    return node


def find_block(lines: list[str], key: str, lo: int, hi: int) -> tuple[int, int]:
    """``(opening line, closing line)`` of ``"key": {`` within ``[lo, hi)``, by indentation.

    Textual rather than structural on purpose: this file carries hundreds of lines of hand-written
    prose in `_comment`/`residualGap` arrays, and a parse-and-redump would reflow every one of them
    into a diff nobody can review. The edit is therefore surgical, and every surgical edit is checked
    SEMANTICALLY afterwards (see :func:`repin`) — so a mis-located line cannot survive.
    """
    opener = re.compile(r'^(\s*)"' + re.escape(key) + r'"\s*:\s*\{\s*$')
    for index in range(lo, hi):
        found = opener.match(lines[index])
        if not found:
            continue
        closing = found.group(1) + "}"
        for end in range(index + 1, hi):
            if lines[end].rstrip() in (closing, closing + ","):
                return index, end
        raise GateError(f"{TABLE}: `{key}` opens at line {index + 1} and never closes at its indent.")
    raise GateError(f"{TABLE}: no `{key}` object found where one was expected.")


def set_scalar(lines: list[str], lo: int, hi: int, key: str, value: str) -> None:
    """Replace the one ``"key": "…"`` line in ``[lo, hi)``. Zero or many matches is a no-verdict.

    ONE RANGE PER BLOCK, INCLUDING WHATEVER IT NESTS, and the uniqueness requirement is what makes
    that safe rather than lucky: a `derivedFrom` scalar sought across the whole block would meet a
    colliding `libraryFiles` entry as a SECOND match, and a second match is refused, not resolved.
    An earlier draft excluded the nested block by searching around it — a guard whose inversion
    survived every leg of `tests/skillmirror-redrive/run.sh`, because the collision it defended
    against needs a library file literally named `commit`. Untested defence removed in favour of the
    refusal that is tested (the `!= 1` branch below) and the semantic check in :func:`repin`.
    """
    pattern = re.compile(r'^(\s*)"' + re.escape(key) + r'"\s*:\s*"[^"]*"(,?)\s*$')
    hits = [index for index in range(lo, hi) if pattern.match(lines[index])]
    if len(hits) != 1:
        raise GateError(
            f"{TABLE}: expected exactly one `\"{key}\": \"…\"` line in the block being re-pinned, "
            f"found {len(hits)}. Refusing to guess which one records the provenance."
        )
    found = pattern.match(lines[hits[0]])
    assert found
    lines[hits[0]] = f'{found.group(1)}"{key}": "{value}"{found.group(2)}'


def repin(raw: str, document: dict, edits: dict[tuple[str, ...], str]) -> str:
    """Apply ``edits`` (a JSON path -> new scalar) to the table's TEXT, then prove it semantically.

    The proof is the point. The textual edit is convenient; what makes it trustworthy is that the
    result is re-parsed and compared, key for key, against the document this function was ASKED to
    produce. A regex that hit the wrong line, or missed one, fails here rather than shipping.

    THAT COMPARISON IS A POSTCONDITION ON THIS FILE, NOT A GATE ON THE TABLE, and it is worth being
    exact about which: no INPUT can trip it, because it only ever fires on a bug in the writer above
    it. `tests/skillmirror-redrive/run.sh` therefore cannot manufacture a case that reds it without
    first introducing that bug — which is precisely what it is for. The real conformance table is
    four hundred lines of hand-written prose that no fixture covers, and this is what stands between
    a mis-located regex and a silent edit to it.
    """
    import json

    lines = raw.split("\n")
    derived_open, derived_close = find_block(lines, "derivedFrom", 0, len(lines))
    files_open, files_close = find_block(lines, "libraryFiles", derived_open + 1, derived_close)
    vectors_open, vectors_close = find_block(lines, "digestVectors", 0, len(lines))
    measured_open, measured_close = find_block(lines, "measuredAgainst", vectors_open + 1, vectors_close)

    spans: dict[tuple[str, ...], tuple[int, int]] = {
        ("derivedFrom",): (derived_open + 1, derived_close),
        ("derivedFrom", "libraryFiles"): (files_open + 1, files_close),
        ("digestVectors", "measuredAgainst"): (measured_open + 1, measured_close),
    }

    expected = copy.deepcopy(document)
    for path, value in edits.items():
        parent, key = path[:-1], path[-1]
        if parent not in spans:
            raise GateError(f"{TABLE}: no known text span for `{'.'.join(path)}`.")
        set_scalar(lines, *spans[parent], key, value)
        node = expected
        for step in parent:
            node = node[step]
        if key not in node:
            raise GateError(
                f"{TABLE}: `{'.'.join(path)}` is not already present. This script re-pins existing "
                f"provenance and never adds a field the table did not carry."
            )
        node[key] = value

    written = "\n".join(lines)
    try:
        round_tripped = json.loads(written)
    except json.JSONDecodeError as error:
        raise GateError(f"{TABLE}: the re-pinned text is no longer valid JSON ({error}).") from error
    if round_tripped != expected:
        raise GateError(
            f"{TABLE}: the surgical re-pin did not produce the document it was asked for. Refusing "
            f"to write. This is a bug in this script, not a finding about the library."
        )
    return written


def provenance_edits(
    document: dict, checkout: str, commit: str, date: str
) -> tuple[dict[tuple[str, ...], str], list[str]]:
    """The provenance this run would write, and a human-readable list of what moves."""
    derived = block(document, "derivedFrom")
    measured = block(document, "digestVectors", "measuredAgainst")

    for name, node in (("derivedFrom", derived), ("digestVectors.measuredAgainst", measured)):
        for key in ("repo", "path"):
            if not isinstance(node.get(key), str) or not node[key].strip():
                raise GateError(f"{TABLE}: `{name}.{key}` is not a non-empty string.")
    if (derived["repo"], derived["path"]) != (measured["repo"], measured["path"]):
        raise GateError(
            f"{TABLE}: `derivedFrom` names {derived['repo']}:{derived['path']} while "
            f"`digestVectors.measuredAgainst` names {measured['repo']}:{measured['path']}. One "
            f"oracle run measures ONE library, so it cannot honestly re-pin two different subjects. "
            f"A human owns reconciling them."
        )

    files = derived.get("libraryFiles")
    if not isinstance(files, dict) or not files:
        raise GateError(f"{TABLE}: `derivedFrom.libraryFiles` is not a non-empty object.")

    edits: dict[tuple[str, ...], str] = {}
    changes: list[str] = []

    def stage(path: tuple[str, ...], old: object, new: str) -> None:
        edits[path] = new
        if old != new:
            changes.append(f"  {'.'.join(path)}\n      {old!r}\n   -> {new!r}")

    subject = sha256_file(os.path.join(checkout, derived["path"]))
    stage(("derivedFrom", "commit"), derived.get("commit"), commit)
    stage(("derivedFrom", "skillMirrorFsSha256"), derived.get("skillMirrorFsSha256"), subject)
    stage(("derivedFrom", "derivedOn"), derived.get("derivedOn"), date)
    for relative in sorted(files):
        if relative.startswith("/") or ".." in relative.split("/"):
            raise GateError(f"{TABLE}: `derivedFrom.libraryFiles` names non-relative path {relative!r}.")
        stage(("derivedFrom", "libraryFiles", relative), files[relative], sha256_file(os.path.join(checkout, relative)))
    stage(("digestVectors", "measuredAgainst", "commit"), measured.get("commit"), commit)
    stage(("digestVectors", "measuredAgainst", "skillMirrorFsSha256"), measured.get("skillMirrorFsSha256"), subject)
    stage(("digestVectors", "measuredAgainst", "measuredOn"), measured.get("measuredOn"), date)
    return edits, changes


REPO_SLUG = re.compile(r"\A[A-Za-z0-9._-]+/[A-Za-z0-9._-]+\Z")


def print_subject(root: str) -> int:
    """Print the library subject the table declares, as ``key=value`` lines for ``$GITHUB_OUTPUT``.

    ONE COPY, IN THE TOOL THAT ALREADY READS THE TABLE. Two workflow jobs need the same three facts —
    which repository, which revision, and which directory the oracle loads — and a `python3 -c` in
    each `run:` block would be the second and third copies of a read this file already does, free to
    disagree with it and with each other (#485/#865). It measures nothing and writes nothing.

    The slug and commit are VALIDATED here rather than trusted, because a workflow interpolates them
    into `actions/checkout`'s `repository:`/`ref:` inputs: a checked-in file is still an input, and a
    job that checks out whatever a table says would be one edit away from cloning something else.
    """
    _, _, document = load_table(root)
    derived = block(document, "derivedFrom")
    for key in ("repo", "path", "commit"):
        if not isinstance(derived.get(key), str) or not derived[key].strip():
            raise GateError(f"{TABLE}: `derivedFrom.{key}` is not a non-empty string.")
    if not REPO_SLUG.fullmatch(derived["repo"]):
        raise GateError(f"{TABLE}: `derivedFrom.repo` is {derived['repo']!r}, not an `owner/name` slug.")
    if not COMMIT.fullmatch(derived["commit"]):
        raise GateError(
            f"{TABLE}: `derivedFrom.commit` is {derived['commit']!r}, not a 40-character lowercase "
            f"hex sha — it cannot be resolved as a checkout ref."
        )
    path = derived["path"]
    if path.startswith("/") or ".." in path.split("/") or not os.path.dirname(path):
        raise GateError(
            f"{TABLE}: `derivedFrom.path` is {path!r}, which is not a plain repository-relative path "
            f"inside a directory. Refusing to build a checkout path out of it."
        )
    print(f"repo={derived['repo']}")
    print(f"commit={derived['commit']}")
    print(f"libdir={os.path.dirname(path)}")
    return int(ExitCode.OK)


def write_report(path: str, body: str) -> None:
    try:
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(body)
    except OSError as error:
        raise GateError(f"cannot write the report to {path} ({error}).") from error


def main(argv: list[str]) -> int:
    parser = base_parser(__doc__.splitlines()[0])
    parser.add_argument("--checkout", default=None, help="a checkout of the library repository")
    parser.add_argument("--commit", default=None, help="the library revision that checkout is at")
    parser.add_argument("--write", action="store_true", help="write the re-pinned table (default: dry run)")
    parser.add_argument("--date", default=None, help="the derivation date to record (default: today, UTC)")
    parser.add_argument("--report", default=None, help="write a markdown account of this run here")
    parser.add_argument(
        "--subject",
        action="store_true",
        help="print the library subject as `key=value` lines and exit; measures nothing",
    )
    parser.add_argument(
        "--assert-derived",
        action="store_true",
        help="assert the table IS derived from --commit; write nothing. Any disagreement is a finding",
    )
    args = parser.parse_args(argv)

    if args.subject:
        return print_subject(args.root)
    if not args.checkout or not args.commit:
        raise GateError("--checkout and --commit are both required unless --subject is given.")
    if args.assert_derived and args.write:
        raise GateError("--assert-derived asserts; it never writes. Drop one of --assert-derived/--write.")

    if not COMMIT.fullmatch(args.commit):
        raise GateError(
            f"--commit {args.commit!r} is not a 40-character lowercase hex sha. The provenance this "
            f"writes is a revision pin; an abbreviated or symbolic ref would record a subject that "
            f"cannot be resolved later."
        )
    date = args.date or datetime.now(timezone.utc).date().isoformat()
    if not DATE.fullmatch(date):
        raise GateError(f"--date {date!r} is not YYYY-MM-DD.")
    checkout = os.path.abspath(args.checkout)
    if not os.path.isdir(checkout):
        raise GateError(f"--checkout {args.checkout!r} is not a directory.")

    table_path, raw, document = load_table(args.root)
    derived = block(document, "derivedFrom")
    library_files = derived.get("libraryFiles")
    if not isinstance(library_files, dict) or not library_files:
        raise GateError(f"{TABLE}: `derivedFrom.libraryFiles` is not a non-empty object.")
    examined = tuple(sorted(library_files))
    census = subject_census(
        declared=PATHS_SUBJECT,
        resolved=tuple(path for path in PATHS_SUBJECT if os.path.exists(os.path.join(args.root, path))),
        examined=examined,
        producers=examined,
        authority_revision="PATHS_SUBJECT/v1",
    )
    lib_dir = os.path.join(checkout, os.path.dirname(derived["path"]))
    if not os.path.isdir(lib_dir):
        raise GateError(
            f"{lib_dir} is not a directory. `derivedFrom.path` is {derived['path']!r}, so the "
            f"library the oracle loads should live beside it in the checkout; measuring a subject "
            f"that is not there is a no-verdict, never a re-pin."
        )

    if args.assert_derived:
        # THE PR-TIME QUESTION, WHICH IS NOT THE SCHEDULED ONE. `check-skillmirror-freshness.py` asks
        # "is this pin still CURRENT?" and reads the default branch. This asks "is this table what its
        # pin CLAIMS?" and reads the pinned revision — the exact read that gate forbids itself, because
        # there it would compare a digest to the bytes it was computed from and be green forever. Here
        # the compared quantities are independent: the table's MEASURED columns against the library's
        # actual answers. Every disagreement is a finding, provenance included — a table whose
        # provenance names a revision it was not derived from is exactly the defect.
        if args.commit != derived["commit"]:
            return report_findings(
                NAME,
                [
                    f"{TABLE}: asked to verify derivation from {args.commit}, but `derivedFrom.commit` "
                    f"claims {derived['commit']}. The checkout under test is not the revision this "
                    f"table names, so agreement would prove nothing about the claim it makes."
                ],
            )
        code, output = run_oracle(args.root, lib_dir)
        if code != int(ExitCode.OK):
            return report_findings(
                NAME,
                [
                    f"{TABLE}: the table is NOT derived from {derived['repo']} {args.commit}, which "
                    f"is the revision its own `derivedFrom` names. Running the library at that "
                    f"revision over this table disagrees:\n{output.strip()}"
                ],
            )
        return report_ok(
            NAME,
            f"{derived['repo']} @ {args.commit}: every measured column, and both provenance records, "
            f"are what the library at the RECORDED revision returns.",
            census,
        )

    code, before = run_oracle(args.root, lib_dir)
    edits, changes = provenance_edits(document, checkout, args.commit, date)

    header = (
        f"library: {derived['repo']} @ {args.commit}\n"
        f"oracle:  bash {ORACLE} --lib {lib_dir}\n"
    )

    if code == int(ExitCode.OK):
        if changes:
            # The oracle agreed on every field it GRADES, and something this script pins still moved
            # — `commit` and the dates, which the oracle does not read. Recording them is honest
            # housekeeping, not a measurement claim, so it is a write and not a finding.
            note = "the oracle agrees on every graded field; only ungraded provenance moves"
        else:
            note = "nothing to do"
        summary = (
            f"{header}verdict: FRESH — the table's measured columns are what the library returns, "
            f"and {note}.\n"
        )
    else:
        provenance, measured = classify(before)
        if measured or not provenance:
            findings = measured or [
                "the oracle exited 1 but printed no disagreement this script could read — its "
                "output format may have changed. Refusing to re-pin a table it cannot understand."
            ]
            if args.report:
                write_report(
                    args.report,
                    f"{header}\nverdict: **the library's measured answers moved — a human owns this**\n\n"
                    + "".join(f"- `{f}`\n" for f in findings)
                    + f"\n```\n{before.strip()}\n```\n",
                )
            return report_findings(
                NAME,
                [
                    f"{TABLE}: the oracle's MEASURED columns disagree with the library at "
                    f"{derived['repo']} {args.commit}, so this is a BEHAVIOURAL change and not a "
                    f"stale pin. No re-pin was written: rewriting provenance around a moved vector "
                    f"would green the divergence, which is the defect and not a fix for it. Re-run "
                    f"`bash {ORACLE} --lib <checkout>{os.sep}{os.path.dirname(derived['path'])}`, "
                    f"reconcile each vector, and record what moved. Disagreements: " + "; ".join(findings)
                ],
            )
        summary = (
            f"{header}verdict: STALE PROVENANCE ONLY — every measured vector reproduced; "
            f"{len(provenance)} provenance field(s) record a superseded revision.\n"
        )

    print(summary + ("\n".join(changes) if changes else "  (no field moves)"))

    reconciled = False
    if changes and args.write:
        written = repin(raw, document, edits)
        try:
            with open(table_path, "w", encoding="utf-8") as handle:
                handle.write(written)
        except OSError as error:
            raise GateError(f"cannot write {table_path} ({error}).") from error
        # THE INDEPENDENT CHECK. Everything above this line is a judgement about the oracle's
        # WORDING; this is a fresh measurement against the library. A write that does not make the
        # derivation agree is reverted, not explained away.
        after_code, after = run_oracle(args.root, lib_dir)
        if after_code != int(ExitCode.OK):
            with open(table_path, "w", encoding="utf-8") as handle:
                handle.write(raw)
            return report_findings(
                NAME,
                [
                    f"{TABLE}: the re-pin was written and the oracle STILL disagrees, so the write "
                    f"has been REVERTED and the table is exactly as it was. This is a finding for a "
                    f"human, not a retry: either the disagreement was never purely about provenance "
                    f"or this script wrote the wrong fields. Oracle output after the write:\n{after.strip()}"
                ],
            )
        reconciled = True

    if args.report:
        verdict = (
            "**re-pinned, and the derivation re-run agrees**"
            if reconciled
            else ("**nothing to write**" if not changes else "**dry run — nothing was written**")
        )
        write_report(
            args.report,
            f"{header}\nverdict: {verdict}\n\n"
            f"Every vector reproduced against the library at "
            f"`{args.commit}`: this is a fresh MEASUREMENT of `verify` and `sha256`, not a digest "
            f"comparison, so it answers the question `check-skillmirror-freshness.py` structurally "
            f"cannot — whether the library's move altered behaviour. It did not.\n\n"
            + ("### Provenance re-pinned\n\n```\n" + "\n".join(changes) + "\n```\n" if changes else "")
            + f"\n### Oracle output\n\n```\n{before.strip()}\n```\n",
        )

    return report_ok(
        NAME,
        f"{derived['repo']} @ {args.commit}: every measured vector reproduced"
        + (
            "; provenance re-pinned and re-verified"
            if reconciled
            else ("; provenance is current" if not changes else "; provenance is stale (dry run)")
        ),
        census,
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
