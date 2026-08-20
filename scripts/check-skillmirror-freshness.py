#!/usr/bin/env python3
"""Notice when `Fsgg.SkillMirror`'s own source moves out from under this repo's conformance table.

.github#1546, filed from #1513, epic #266 (coherence gates that fail open).

WHAT #1513 PINNED, AND WHAT IT DATED
  #1513 closed the third of three divergences between `scripts/skill-union-assert.sh` and
  `Fsgg.SkillMirror.verify` by applying the #398 pattern: a shared vector table
  (`tests/skill-union/skillmirror.fixtures.json`) whose `library` column is MEASURED by running the
  library's own source (`tests/skill-union/skillmirror-oracle.sh`), plus a hermetic conformance
  harness that runs on every PR. That pins ONE direction and DATES the other:

      the shell reports what the table says   pinned by skillmirror-conformance.sh   every PR
      the table says what the library does    pinned by skillmirror-oracle.sh        ONLY at the
                                                                                     revision in
                                                                                     `derivedFrom`

  Nothing noticed when that revision stopped being current. This gate is the thing that notices.

  It is cheap on purpose — one API read of one file and a digest comparison. It deliberately does
  NOT build the library, restore its package, or sparse-checkout the sibling repository. #1513's
  criterion 3 rejected that, correctly: a conformance leg a missing dependency can SKIP is the
  fail-open (#266/#292) the leg exists to prevent, and this repo's CI holds neither the source nor
  the package.

NOTHING HERE IS A SECOND COPY OF THE SUBJECT — that would be the same disease one level up
  The repository, the path, the commit, the expected digest and the remedy command are all READ OUT
  OF `derivedFrom`. Not one of them is spelled in this file. That matters because the failure mode
  this whole family of gates exists to end is "the fact is written in two places and they
  disagree" (#485/#865): a gate that retyped `src/FS.GG.Contracts/SkillMirror.fs` beside the table
  would be free to grade a path the table was never derived from, and would report a confident
  green about a file nobody measured. Re-derive the table with the oracle and this gate follows for
  free, with no edit here.

WHY A 404 IS SPLIT IN TWO, AND WHY THAT SPLIT IS THE WHOLE OF THE FAIL-CLOSED CLAIM
  GitHub answers 404 for BOTH "that path is not there" and "your token cannot see this repository"
  — deliberately, so a private repo's existence does not leak. Those are opposite verdicts here:

      the repo is visible and the path 404s  ->  FINDING. The library MOVED. The table names a file
                                                 that no longer exists, so nothing can re-derive it
                                                 until a human says where it went. That is a real,
                                                 actionable answer about the subject.
      the repo itself is not visible         ->  NO VERDICT. We did not look. A token change, an
                                                 org permission, a rename of the repository — none
                                                 of which is evidence about the digest.

  So a 404 on the path is never graded until the repository has been confirmed readable, and the
  confirmation is a separate read that only happens on that branch. Collapsing the two would put
  this gate in exactly the state #266 catalogues: a credential problem rendered as a confident
  finding about someone else's source, or — worse in the other direction — a moved library rendered
  as "could not look", which is the shape a scheduled job's red gets muted for.

  A rate limit, a 5xx, a timeout or a hung socket is `Unreachable` (exit 2, retryable). Everything
  the gate cannot establish is exit 2 or 3. There is no path from "I could not look" to green.

WHAT A RED MEANS, AND WHY THE MESSAGE IS LOAD-BEARING (#238, #698)
  A red is NOT an accusation against the diff or the schedule that triggered it, and it is not a
  claim that `scripts/skill-union-assert.sh` is wrong. It means:

      the table's expectations were derived from a library revision that no longer exists.

  The repair is to re-run the oracle against a checkout of the new revision and reconcile — which
  is a command, and the finding prints it. A gate whose message does not say this gets read as a
  false accusation against a diff that did not cause it, and the first thing a wrongly-red gate
  teaches is that it is noise.

WHAT THIS GATE DOES NOT COVER — say it plainly, because a green here is NARROW (#1546 criterion 5)
  * IT COMPARES A DIGEST, NOTHING ELSE. A green means the bytes of the recorded file are the bytes
    the table was derived from. A red means those bytes CHANGED. Neither says anything about
    whether the change altered `verify`'s BEHAVIOUR — an added function, a comment, a reformat and
    a rewritten `verify` are all the same event to this gate. RE-RUNNING THE ORACLE is what answers
    the behavioural question; this gate only tells you when to.
  * IT WILL RED ON A PURELY ADDITIVE CHANGE, and that is correct rather than unfortunate. A digest
    cannot know that the added code is unreachable from `verify`, and a gate that tried to guess
    would be reading F# for intent — the #683 trap. The cost of the false-ALARM direction is one
    oracle run; the cost of the other direction is a table nobody notices is dated.
  * IT GRADES ONE FILE. `verify`'s behaviour also depends on `Schemas.fs` (the oracle `#load`s
    both). If `Schemas.fs` changes alone, this gate stays green. Widening it is one more entry in
    `derivedFrom` and no change to this logic — but `derivedFrom` records one file today, and the
    gate grades what the table actually recorded rather than what it wishes had been recorded.
  * IT READS THE REPOSITORY'S DEFAULT BRANCH, not the recorded commit. Asking for the file AT
    `derivedFrom.commit` would compare the recorded digest to itself and be green forever — the
    vacuous pass this gate exists to be the opposite of. The recorded commit is printed in the
    finding as provenance, never used as the read's `ref`.
  * IF THE RECORDED PATH BECOMES A DIRECTORY, the raw media type returns the directory's JSON
    listing and its digest will not match, so the verdict is a FINDING — the right verdict, reached
    for a reason the message states slightly too narrowly ("that file now hashes to…" is the hash
    of a listing, not of a file). The remedy is the same either way: re-derive.
  * IT IS NOT A SUBSTITUTE FOR THE CONFORMANCE HARNESS. `tests/skill-union/run.sh` asserts
    shell == table on every PR and is unaffected by anything here.

EXIT CODES — THE CONTRACT (scripts/lib/gate.py)
  0  OK — the live file's sha256 is the digest the table recorded.
  1  FINDING — it is not, or the recorded path no longer resolves in a repository we CAN read.
  2  NO VERDICT (retryable) — rate limit, outage, timeout, or a repository we cannot see.
  3  NO VERDICT (permanent) — no `gh`, or a `derivedFrom` block this gate cannot read.

usage: check-skillmirror-freshness.py [--root DIR] [--fixtures PATH]
"""

from __future__ import annotations

import hashlib
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import (  # noqa: E402  (path shim above must run first)
    ExitCode,
    GateError,
    Unreachable,
    base_parser,
    read_text,
    report_findings,
    report_ok,
    run,
    subject_census,
)

NAME = "check-skillmirror-freshness"

# The table this gate grades. The ONLY repo-relative path this file spells: everything about the
# SUBJECT — which repository, which file, which commit, which digest — is read out of the table's
# own `derivedFrom` block rather than retyped here.
FIXTURES = "tests/skill-union/skillmirror.fixtures.json"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266).
# `check-paths-coherence.py` rule (c) reads this BY AST and reds `skillmirror-freshness.yml` if its
# `paths:` does not select every entry. Composed from the constant the gate walks with, never
# retyped beside it.
PATHS_SUBJECT = (FIXTURES,)

# The block, and the fields it must carry. Every one of them is load-bearing at read time, so an
# absent or blank one is a no-verdict rather than a default: a gate that invented a path would be
# grading a file the table was never derived from.
BLOCK = "derivedFrom"
REPO_KEY, PATH_KEY, COMMIT_KEY, DIGEST_KEY, REDRIVE_KEY = (
    "repo",
    "path",
    "commit",
    "skillMirrorFsSha256",
    "howToRedrive",
)

# sha256, lowercase hex. Anything else in the digest field is a table this gate cannot compare
# against — an uppercase or truncated value would silently never match and read as a permanent,
# inexplicable red.
SHA256 = re.compile(r"\A[0-9a-f]{64}\Z")

# `owner/name`. Interpolated into an API path, so it is validated rather than trusted: the table is
# a checked-in file, but a gate that pastes an unvalidated string into a URL path is one typo away
# from reading a completely different repository and reporting confidently about it.
REPO_SLUG = re.compile(r"\A[A-Za-z0-9._-]+/[A-Za-z0-9._-]+\Z")

# Transport knobs. Overridable so the fixture runs at full speed against its stub; the defaults are
# what a scheduled run uses.
#
# READ AT CALL TIME, NEVER AT IMPORT — and that is not style, it is the harness's one invariant. A
# module-level `int(os.environ[...])` raises ValueError during IMPORT, which is BEFORE `run()` has
# wrapped anything, so Python exits 1 — the FINDING code. A typo in one of these variables would
# then report "the SkillMirror conformance table is DATED" to CI, confidently, having read nothing
# at all. `setting()` runs inside `main`'s call stack, so a bad value is a GateError and therefore a
# no-verdict, which is what a misconfigured gate owes its reader.
TRIES_VAR, DELAY_VAR, TIMEOUT_VAR = (
    "FSGG_SKILLMIRROR_TRIES",
    "FSGG_SKILLMIRROR_RETRY_DELAY",
    "FSGG_SKILLMIRROR_TIMEOUT",
)
DEFAULTS = {TRIES_VAR: "3", DELAY_VAR: "2", TIMEOUT_VAR: "30"}


def setting(var: str, cast: type, *, floor: float) -> object:
    """One transport knob, parsed — or a no-verdict naming the variable and the value.

    ``floor`` differs by knob, and the distinction is real rather than defensive: a zero BACKOFF is
    a legitimate configuration (it is what the fixture runs at, and it is what you want on a stub),
    while zero TRIES or a zero TIMEOUT mean the gate makes no read at all and would then have to
    report something about a subject it never looked at.
    """
    raw = os.environ.get(var) or DEFAULTS[var]
    try:
        value = cast(raw)
    except ValueError as error:
        raise GateError(
            f"{var}={raw!r} is not a {cast.__name__}. This gate refuses to run with a transport it "
            f"cannot configure rather than guessing at one ({error})."
        ) from error
    if value < floor:
        raise GateError(
            f"{var}={raw!r} is below its floor of {floor}. A value there would mean the gate never "
            f"reads its subject, and a verdict about an unread subject is the fail-open (#266)."
        )
    return value

# gh's rendering of the HTTP status. Matched on the STATUS, not on gh's prose, because the status is
# the API's contract and the prose is not.
HTTP_404 = re.compile(r"\(HTTP 404\)")


class Missing(Exception):
    """A 404. A real answer from the API, not a failure to reach it — see the header's split."""


def gh_api(*args: str) -> bytes:
    """Read the GitHub API, returning RAW BYTES, and telling "not there" from "could not tell".

    Bytes, not text: the recorded digest is the sha256 of the FILE'S BYTES, and decoding-then-
    re-encoding to compute it would make the verdict a function of this process's locale. The
    `Accept: application/vnd.github.raw` media type is what makes the response the file itself
    rather than the JSON envelope, so no base64 round-trip stands between the wire and the hash.

    Isolated in one function so the fixture can stub `gh` on PATH and exercise this transport
    rather than a convenience shim — a fail-closed claim proved against a mock of itself is not
    proved. Retries are for the transient class only; a 404 is returned to the caller immediately
    because retrying an answer is not how you get a different one.
    """
    tries = int(setting(TRIES_VAR, int, floor=1))
    timeout = float(setting(TIMEOUT_VAR, float, floor=0.001))
    delay = float(setting(DELAY_VAR, float, floor=0))
    for attempt in range(1, tries + 1):
        try:
            proc = subprocess.run(
                ["gh", "api", *args],
                capture_output=True,
                check=False,
                timeout=timeout,
            )
        except FileNotFoundError as error:
            # PERMANENT, not retryable, and not a finding. There is no `gh` to ask, so the gate has
            # no subject at all — retrying would fail identically every time.
            raise GateError(
                f"`gh` is not on PATH, so this gate cannot read {args[-1] if args else 'the API'}. "
                f"That is a FAILED READ, not a fresh table — refusing to report green (#266)."
            ) from error
        except subprocess.TimeoutExpired:
            # A hung socket is "could not tell", not "not there" — and without an explicit timeout
            # it is not even that: the call would block and the retry loop would never run.
            if attempt >= tries:
                raise Unreachable(f"`gh api {' '.join(args)}` timed out after {timeout}s") from None
        else:
            if proc.returncode == 0:
                return proc.stdout
            err = " ".join(proc.stderr.decode("utf-8", "replace").split())
            if HTTP_404.search(err):
                raise Missing(err)
            if attempt >= tries:
                raise Unreachable(err or f"gh exited {proc.returncode}")
        time.sleep(delay)
        delay *= 2
    raise Unreachable("unreachable")  # pragma: no cover — the loop always returns or raises


def derived_from(root: str, fixtures: str | None) -> tuple[str, dict[str, str], dict[str, str]]:
    """``(path read, the validated derivedFrom block)`` — or a no-verdict naming what is wrong.

    EVERY DEGENERATE SHAPE IS A NO-VERDICT, NEVER A GREEN. This gate's entire subject arrives
    through this block: with a missing `path` it would have nothing to read, and with a missing
    digest "the live file matches what we recorded" is vacuously satisfiable. Those are the states
    in which the loudest, most complete-looking pass covers the least work, which is #266's
    signature. A blank field is treated exactly as an absent one.
    """
    import json  # lazy, exactly as lib.gate does with yaml: only this half pays for it.

    path = fixtures or os.path.join(root, FIXTURES)
    where = os.path.relpath(path, root).replace(os.sep, "/") if not fixtures else path
    text = read_text(path, f"the SkillMirror conformance table ({where})")
    try:
        document = json.loads(text)
    except json.JSONDecodeError as error:
        raise GateError(
            f"{where}: not parsable as JSON — {error}. This is the table whose freshness is the "
            f"whole subject; an unreadable one is a no-verdict, never a pass."
        ) from error
    if not isinstance(document, dict):
        raise GateError(f"{where}: top level is {type(document).__name__}, not a JSON object.")

    block = document.get(BLOCK)
    if not isinstance(block, dict):
        raise GateError(
            f"{where}: has no `{BLOCK}` object (found {type(block).__name__}). That block is the "
            f"ONLY record of which library revision the `library` column was measured against — "
            f"without it there is nothing to compare the live source to, and 'the table is fresh' "
            f"is vacuously true. Re-derive with the oracle rather than deleting the provenance."
        )

    fields: dict[str, str] = {}
    for key in (REPO_KEY, PATH_KEY, COMMIT_KEY, DIGEST_KEY, REDRIVE_KEY):
        value = block.get(key)
        if not isinstance(value, str) or not value.strip():
            raise GateError(
                f"{where}: `{BLOCK}.{key}` is {value!r} — not a non-empty string. Every field of "
                f"this block is read at grade time; a missing one is a subject this gate cannot "
                f"establish, so it refuses rather than guessing (#266)."
            )
        fields[key] = value.strip()

    if not SHA256.fullmatch(fields[DIGEST_KEY]):
        raise GateError(
            f"{where}: `{BLOCK}.{DIGEST_KEY}` is {fields[DIGEST_KEY]!r}, which is not 64 lowercase "
            f"hex characters. A truncated or re-cased digest can never match a computed one, so "
            f"the gate would red permanently and inexplicably instead of saying what is wrong."
        )
    if not REPO_SLUG.fullmatch(fields[REPO_KEY]):
        raise GateError(
            f"{where}: `{BLOCK}.{REPO_KEY}` is {fields[REPO_KEY]!r}, which is not an `owner/name` "
            f"slug. It is interpolated into an API path, and reading the wrong repository "
            f"confidently is worse than reading none."
        )
    if fields[PATH_KEY].startswith("/") or ".." in fields[PATH_KEY].split("/"):
        raise GateError(
            f"{where}: `{BLOCK}.{PATH_KEY}` is {fields[PATH_KEY]!r}, which is not a plain "
            f"repository-relative path. Refusing to build an API path out of it."
        )
    files = block.get("libraryFiles")
    if not isinstance(files, dict) or not files:
        raise GateError(
            f"{where}: `{BLOCK}.libraryFiles` is not a non-empty object. The oracle loads a set of "
            "library files; partial or absent coverage is a no-verdict, never a green (#1577)."
        )
    checked: dict[str, str] = {}
    for file_path, digest in files.items():
        if not isinstance(file_path, str) or not isinstance(digest, str) or not SHA256.fullmatch(digest):
            raise GateError(f"{where}: `{BLOCK}.libraryFiles` contains an invalid path/digest entry.")
        if file_path.startswith("/") or ".." in file_path.split("/"):
            raise GateError(f"{where}: `{BLOCK}.libraryFiles` contains non-relative path {file_path!r}.")
        checked[file_path] = digest
    # Keep the legacy named digest as a checked member while fixtures and downstream provenance migrate.
    # The mapping is authoritative for coverage; this duplicate preserves the established field's evidence.
    checked[fields[PATH_KEY]] = fields[DIGEST_KEY]
    return where, fields, checked


def repository_is_readable(repo: str) -> bool:
    """Can this token see ``repo`` AT ALL? The disambiguator for a 404 on the path.

    GitHub answers 404 both for "no such path" and for "you may not know whether this exists", so
    this is the only way to tell a library that MOVED (a finding) from a repository we cannot READ
    (no verdict). A transient failure of this probe is itself Unreachable — propagated, not caught,
    because a probe that cannot answer must not be read as a `False` that turns into a finding.
    """
    gh_api(f"repos/{repo}", "--jq", ".full_name")
    return True


def live_digest(repo: str, path: str) -> str:
    """sha256 of the live bytes of ``path`` on ``repo``'s DEFAULT BRANCH.

    No `?ref=` — see the header. Pinning the read to `derivedFrom.commit` would compare the
    recorded digest to the bytes it was computed from and be green forever, which is precisely the
    vacuous pass this gate is the opposite of.
    """
    return hashlib.sha256(
        gh_api("-H", "Accept: application/vnd.github.raw", f"repos/{repo}/contents/{path}")
    ).hexdigest()


def moved(where: str, fields: dict[str, str], detail: str) -> str:
    """The finding for a recorded path that no longer resolves in a repository we CAN read."""
    return (
        f"{where}: `{BLOCK}` records {fields[REPO_KEY]}:{fields[PATH_KEY]}, and that path DOES NOT "
        f"RESOLVE on the default branch — while the repository itself reads fine, so this is an "
        f"answer and not a failed read ({detail}). THE LIBRARY MOVED. The `library` column of this "
        f"table was measured by running that file; nothing can re-derive it until someone says "
        f"where it went. This is not a finding about whatever triggered this run, and it does not "
        f"mean scripts/skill-union-assert.sh is wrong. Repair: find the file's new home, update "
        f"`{BLOCK}.{PATH_KEY}`/`{COMMIT_KEY}`/`{DIGEST_KEY}`, and re-derive with "
        f"`{fields[REDRIVE_KEY]}`."
    )


def stale(where: str, fields: dict[str, str], live: str) -> str:
    """The finding for a live digest that differs from the recorded one."""
    return (
        f"{where}: the SkillMirror conformance table is DATED. "
        f"`{BLOCK}` records {fields[REPO_KEY]}:{fields[PATH_KEY]} at commit "
        f"{fields[COMMIT_KEY]} with sha256 {fields[DIGEST_KEY]}, but that file on the default "
        f"branch now hashes to {live}. "
        f"WHAT THIS MEANS: the `library` column of this table — the MEASURED record of what "
        f"`Fsgg.SkillMirror.verify` returns — was derived from a library revision that no longer "
        f"exists. WHAT IT DOES NOT MEAN: it is not an accusation against the diff or the schedule "
        f"that triggered this run, and it is not a claim that scripts/skill-union-assert.sh has "
        f"drifted; the PR-time conformance harness still asserts shell == table and is unaffected. "
        f"This gate compares a DIGEST, so it can say the library changed and can never say whether "
        f"the change altered `verify`'s behaviour — an added function and a rewritten `verify` look "
        f"identical from here. Re-running the oracle against a checkout of the new revision is what "
        f"answers that: `{fields[REDRIVE_KEY]}`. Then update `{BLOCK}.{COMMIT_KEY}` and "
        f"`{BLOCK}.{DIGEST_KEY}` in the same change, and reconcile any vector the oracle disagrees "
        f"with."
    )


def main(argv: list[str]) -> int:
    parser = base_parser(__doc__.splitlines()[0])
    parser.add_argument(
        "--fixtures",
        default=None,
        help=f"the conformance table to grade (default: <root>/{FIXTURES})",
    )
    args = parser.parse_args(argv)

    where, fields, files = derived_from(args.root, args.fixtures)
    repo = fields[REPO_KEY]
    findings: list[str] = []
    for path, expected in files.items():
        file_fields = {**fields, PATH_KEY: path, DIGEST_KEY: expected}
        try:
            live = live_digest(repo, path)
        except Missing as error:
            try:
                repository_is_readable(repo)
            except Missing as invisible:
                raise Unreachable(
                    f"{repo} answers 404 for the repository ITSELF ({invisible}), so this gate cannot "
                    f"see the subject: the recorded path's 404 may mean the file moved, or may mean "
                    f"only that this token has no access. Those are opposite verdicts and nothing here "
                    f"can tell them apart, so this is a NO VERDICT rather than a finding. Check the "
                    f"token's scope, and whether {repo} was renamed or made private."
                ) from invisible
            findings.append(moved(where, file_fields, str(error)))
            continue
        if live != expected:
            findings.append(stale(where, file_fields, live))
    if findings:
        return report_findings(NAME, findings)
    examined = tuple(sorted(files))
    fixtures_path = args.fixtures or os.path.join(args.root, FIXTURES)
    return report_ok(
        NAME,
        f"{repo}: every recorded SkillMirror library file still hashes to its measured digest (BYTES, not behaviour).",
        subject_census(
            declared=(fixtures_path,),
            resolved=(fixtures_path,) if os.path.isfile(fixtures_path) else (),
            examined=examined,
            producers=examined,
            authority_revision="PATHS_SUBJECT/v1",
        ),
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
