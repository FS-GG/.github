#!/usr/bin/env python3
"""Compare the retirement order's standing verdict against the receivers' own trees.

.github#1750, epic #266 (coherence gates that fail open). Filed after
``docs/coordination/skill-apparatus-retirement-order.md`` went stale **three separate times in one
day**, and the board row filed to repair the second of those (`#1723`) was itself two attempts out of
date before a worker reached it.

WHY THIS IS A CHECKER AND NOT A PROCESS RULE
  `#1723`'s repair consolidated the count into a single standing block so that ONE edit brings the
  document current. That is a necessary precondition and it is **not** a guarantee — the document went
  stale a fourth time *after* the consolidation, because four receivers retired in ninety minutes while
  three workers queued their records rather than contend for the file. Consolidation made the repair one
  edit; it could not make anyone able to take the lock, and it could not notice that nobody had.

  The underlying fact needs no judgement and costs two API reads per receiver: **a receiver is retired
  exactly when it no longer commits a second skill root.** This gate derives that, per receiver, from
  the receiver's own tree, and reds when the derivation disagrees with what the document says.

WHAT IT ASSERTS, IN FOUR LEGS
  1. ANCHOR — the document carries exactly one machine-readable verdict block (`ANCHOR_OPEN` below),
     and it parses. Zero blocks, two blocks, or a block this gate cannot read is a NO VERDICT, never a
     pass: a checker that silently finds nothing to compare has certified nothing while looking like it
     certified everything, which is #266's signature and the failure AC 3 was written against.
  2. HEADLINE — the heading the anchor annotates states the same count the anchor's `retired:` list
     implies. The prose a human reads and the list this gate grades cannot drift apart, because one
     check spans both.
  3. TREES — every rostered receiver's own tree agrees with the anchor about whether it is retired.
     The roster is READ from `registry/repos.yml`; the root set is READ from `scripts/skill-view`'s
     `DEFAULT_ROOTS`, overridden by a receiver's own `.agent-skill-roots` where it commits one. Neither
     is retyped here — a checker with a hardcoded receiver set is the next thing to go stale (AC 2), and
     the same is true of a checker with a hardcoded root set.
  4. NO SECOND LIVE COUNT — the count does not also appear on the document's LIVE SURFACE outside the
     anchor. See the next block, which bounds that word precisely, because it is the one leg here whose
     scope is a judgement rather than a measurement.

WHAT "A SECOND COUNT" MEANS HERE, AND WHY IT IS NARROWER THAN IT SOUNDS (#1750 AC 3)
  AC 3 asks this gate to fail when "a second count appears elsewhere in the file". Taken literally over
  prose, that is not decidable and not even desirable. Measured on the document as this gate was
  written: **fourteen** count-shaped tokens (``N of 7``), of which **twelve** are inside the dated,
  append-only records the document's own rule REQUIRES be kept unedited — *"do not edit a historical
  block to carry a new headline"*. A gate that red on those would be demanding the removal of the
  provenance that makes the record trustworthy, and it would be red on `main` from the day it landed.

  So the leg is scoped to the LIVE SURFACE — the two places a reader looks for current state, and the
  two places a restatement can therefore mislead:

      (a) the document's header block, everything above the first `---` rule. The document itself
          titles this *"Where the live state is."*
      (b) every ATX heading line in the file.

  Both are decidable, both are stable under appends, and both caught a REAL live restatement on the
  corpus this gate was written against: the header block's *"The receiver sequence is COMPLETE — 7 of
  7"* and §9's title *"What is still open after 7 of 7"*. Neither is historical; both would have gone
  stale on the next stage; the same change that adds this gate reduces both to pointers.

  WHAT THIS LEG DOES NOT COVER, SAID PLAINLY BECAUSE A GREEN HERE IS NARROW. A live count buried in
  body prose — not in a heading, not in the header block — is invisible to this gate. That is a real
  hole and it is chosen, not overlooked: the alternative is a marker discipline on every historical
  line, whose upkeep falls on exactly the append this document's workflow is built around. The hole is
  also not where the measured recurrences happened.

  AND IT IS SCOPED TO THIS FILE, WHICH IS AC 3'S SCOPE AND NOT THE WHOLE RISK. `#1750`'s own comment
  measured the same disease on the *stage* axis in `docs/adr/0067-*.md` and `docs/adr/README.md` — two
  OTHER files, which a file-scoped leg cannot see and which `#1723`'s single-edit consolidation cannot
  reach either. That is filed separately rather than smuggled in here; see the PR that lands this.

AN IN-FLIGHT STAGE IS REPRESENTABLE, AND IT IS NOT A MUTE BUTTON (#1750 AC 4)
  A receiver whose stage is running has NO settled outcome, and the correct record for it is "no
  outcome" rather than a guess — `#1734` was live while `#1723` was worked, and a checker that forced a
  binary there would pressure a worker into recording an unobserved outcome, which is the exact defect
  `#1723` existed to fix. So the anchor's `in-flight:` list names those receivers and this gate does not
  grade their trees.

  It is not silent about them. Every in-flight receiver is named in the OK line together with the ref
  that put it there, and an `in-flight:` entry without a `(#ref)` is refused — a receiver can be parked
  out of the comparison, but never anonymously and never without something a reader can open.

EXIT CODES — THE CONTRACT (scripts/lib/gate.py)
  0  OK — the anchor, the headline and every graded receiver's tree agree.
  1  FINDING — they do not. The message names the receiver, what its tree says, and what the document
     says, because a red that does not say which end is wrong gets read as noise (#238, #698).
  2  NO VERDICT (retryable) — a rate limit, an outage, a timeout, or a repository this token cannot see.
  3  NO VERDICT (permanent) — no `gh`, no anchor, two anchors, an anchor this gate cannot parse, an
     unreadable roster, or a tree the API TRUNCATED. There is no path from "I could not look" to green.

usage: check-retirement-order-coherence.py [--root DIR] [--offline]
"""

from __future__ import annotations

import json
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
    load_yaml,
    read_text,
    report_findings,
    report_ok,
    run,
)

NAME = "check-retirement-order-coherence"

# THE THREE FILES THIS GATE READS OUT OF THE WORKING TREE.
#
# `ORDER` is the subject. `ROSTER` is where the receiver set comes from — read, never retyped, because
# AC 2 is explicit that a checker carrying its own receiver list is the next thing to go stale. `ROOTS`
# is `scripts/skill-view`, the tool that DEFINES the default root set every receiver is graded against;
# taking the set from there rather than spelling `.claude/skills .agents/skills` here keeps this gate on
# whatever ADR-0011/ADR-0065 currently say without an edit (ADR-0067 §5 already moved it once, from
# three roots to two).
ORDER = "docs/coordination/skill-apparatus-retirement-order.md"
ROSTER = "registry/repos.yml"
ROOTS = "scripts/skill-view"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266).
# `check-paths-coherence.py` rule (c) reads this BY AST and reds this gate's workflow if its `paths:`
# does not select every entry. Composed from the constants above, never retyped beside them.
PATHS_SUBJECT = (ORDER, ROSTER, ROOTS)

# The capability that identifies a receiver in the roster. A repo that does not receive the coordination
# kit was never in the retirement order's subject.
CAPABILITY = "coordination-kit"

# THE ANCHOR. One block, in an HTML comment so it renders as nothing, carrying the two lists this gate
# grades. Its opening token is deliberately long and unlikely: this gate counts occurrences, and a token
# that could appear by accident would turn "two anchors" — a no-verdict — into a routine event.
ANCHOR_OPEN = "<!-- fsgg:retirement-verdict"
ANCHOR_CLOSE = "-->"
RETIRED_KEY = "retired"
INFLIGHT_KEY = "in-flight"

# An `in-flight:` entry: a roster id and the ref that put it there. The ref is REQUIRED — see the header.
INFLIGHT_ENTRY = re.compile(r"\A(?P<id>[A-Za-z0-9._-]+)\s*\((?P<ref>[^()\s][^()]*)\)\Z")

# A count-shaped token, for leg 2 (the headline) and leg 4 (the live surface). The denominator is
# checked against the roster size by the caller rather than baked in here, so a receiver joining or
# leaving the roster does not silently make every count token unrecognisable.
COUNT = re.compile(
    r"\b(?P<n>\d+|zero|one|two|three|four|five|six|seven)\s+of\s+(?P<total>\d+|seven)\b",
    re.IGNORECASE,
)
WORDS = {
    "zero": 0,
    "one": 1,
    "two": 2,
    "three": 3,
    "four": 4,
    "five": 5,
    "six": 6,
    "seven": 7,
}

# An ATX heading. Leg 4's second live surface.
HEADING = re.compile(r"\A#{1,6}\s")

# `DEFAULT_ROOTS="..."` in `scripts/skill-view`. Anchored to the assignment so a mention in that
# script's own prose cannot be mistaken for the declaration.
DEFAULT_ROOTS_DECL = re.compile(r'^DEFAULT_ROOTS="(?P<roots>[^"]*)"', re.MULTILINE)

# gh's rendering of the HTTP status. Matched on the STATUS, not on gh's prose, because the status is the
# API's contract and the prose is not.
HTTP_404 = re.compile(r"\(HTTP 404\)")

# Transport knobs, READ AT CALL TIME rather than at import. A module-level `int(os.environ[...])` would
# raise during IMPORT — before `run()` has wrapped anything — and Python exits 1 there, which is the
# FINDING code. A typo in one of these variables would then report a confident finding about receivers
# this process never read.
TRIES_VAR, DELAY_VAR, TIMEOUT_VAR = (
    "FSGG_RETIREMENT_TRIES",
    "FSGG_RETIREMENT_RETRY_DELAY",
    "FSGG_RETIREMENT_TIMEOUT",
)
DEFAULTS = {TRIES_VAR: "3", DELAY_VAR: "2", TIMEOUT_VAR: "30"}


def setting(var: str, cast: type, *, floor: float) -> object:
    """One transport knob, parsed — or a no-verdict naming the variable and the value.

    ``floor`` differs by knob and the distinction is real: a zero BACKOFF is a legitimate configuration
    (it is what the fixture runs at), while zero TRIES or a zero TIMEOUT mean the gate makes no read at
    all and would then be reporting about a subject it never looked at.
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


class Missing(Exception):
    """A 404. A real answer from the API, not a failure to reach it.

    GitHub answers 404 both for "that path is not there" and for "your token may not know whether this
    exists", and those are opposite verdicts here. A 404 on a receiver's own repository is a no-verdict
    (we did not look); a 404 on a path INSIDE a repository we have already confirmed readable is a real
    answer about that path. The split is enforced by the order of the reads in :func:`receiver_state`.
    """


def gh_api(*args: str) -> bytes:
    """Read the GitHub API, telling "not there" from "could not tell".

    Isolated in one function so the fixture can stub `gh` on PATH and exercise this transport rather
    than a convenience shim — a fail-closed claim proved against a mock of itself is not proved.
    Retries cover the transient class only; a 404 returns to the caller immediately, because retrying an
    answer is not how you get a different one.
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
            # PERMANENT, not retryable, and not a finding. There is no `gh` to ask, so the gate has no
            # subject at all — retrying would fail identically every time.
            raise GateError(
                f"`gh` is not on PATH, so this gate cannot read the receivers' trees. That is a FAILED "
                f"READ, not a current verdict — refusing to report green (#266)."
            ) from error
        except subprocess.TimeoutExpired:
            # A hung socket is "could not tell", not "not there" — and without an explicit timeout it is
            # not even that: the call would block and the retry loop would never run.
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


def gh_json(*args: str) -> object:
    """:func:`gh_api`, parsed as JSON — or a no-verdict, because a body we cannot parse is not an answer."""
    raw = gh_api(*args)
    try:
        return json.loads(raw)
    except json.JSONDecodeError as error:
        raise Unreachable(
            f"`gh api {' '.join(args)}` returned a body this gate cannot parse as JSON ({error})."
        ) from error


# ---------------------------------------------------------------------------------------------------
# The working tree: the roster, the root set, and the anchor.
# ---------------------------------------------------------------------------------------------------


def roster(root: str) -> list[tuple[str, str]]:
    """``[(id, owner/name)]`` for every repo the registry says receives the coordination kit.

    An EMPTY roster is a broken read, not a clean one, and it is the specific shape AC 2 names: a gate
    that discovered no receivers would compare the document against nothing and report a confident green
    over a sequence it never examined.
    """
    document = load_yaml(read_text(os.path.join(root, ROSTER), f"the roster ({ROSTER})"), ROSTER)
    rows = document.get("repos")
    if not isinstance(rows, list):
        raise GateError(f"{ROSTER}: has no `repos:` list (found {type(rows).__name__}).")

    found: list[tuple[str, str]] = []
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise GateError(f"{ROSTER}: repos[{index}] is {type(row).__name__}, not a mapping.")
        receives = row.get("receives") or []
        if not isinstance(receives, list) or CAPABILITY not in receives:
            continue
        identifier, full = row.get("id"), row.get("full")
        if not isinstance(identifier, str) or not identifier.strip():
            raise GateError(f"{ROSTER}: repos[{index}] receives `{CAPABILITY}` but has no `id`.")
        if not isinstance(full, str) or full.count("/") != 1:
            raise GateError(
                f"{ROSTER}: repos[{index}] (`{identifier}`) has `full: {full!r}`, which is not an "
                f"`owner/name` slug. It is interpolated into an API path, and reading the wrong "
                f"repository confidently is worse than reading none."
            )
        found.append((identifier.strip(), full.strip()))

    if not found:
        raise GateError(
            f"{ROSTER}: no repo declares `receives: [… {CAPABILITY} …]`, so this gate found ZERO "
            f"receivers to grade. An empty roster is a broken read, not a completed sequence — "
            f"refusing to report green over a subject it never examined (#266)."
        )
    return found


def default_roots(root: str) -> list[str]:
    """The default agent-skill root set, read out of `scripts/skill-view` rather than retyped here."""
    text = read_text(os.path.join(root, ROOTS), f"the root-set declaration ({ROOTS})")
    match = DEFAULT_ROOTS_DECL.search(text)
    if not match:
        raise GateError(
            f"{ROOTS}: no `DEFAULT_ROOTS=\"…\"` assignment. That declaration is where this gate learns "
            f"which roots a receiver is graded against; without it there is nothing to count, and "
            f"'no receiver commits a second root' would be vacuously true."
        )
    roots = [entry for entry in match.group("roots").split() if entry]
    if len(roots) < 2:
        raise GateError(
            f"{ROOTS}: `DEFAULT_ROOTS` declares {len(roots)} root(s) ({roots!r}). A SECOND root is the "
            f"whole subject — with fewer than two, 'no receiver commits a second root' cannot be false, "
            f"and a green would mean nothing."
        )
    return roots


def anchor(root: str) -> tuple[list[str], list[str], dict[str, str], int]:
    """``(retired ids, in-flight ids, in-flight refs, the line the anchor starts on)``.

    Every degenerate shape here is a NO VERDICT and never a green. The entire comparison arrives through
    this block: with no anchor there is nothing to compare, with two there is no way to say which one the
    document means, and either state is one in which the most complete-looking pass covers the least
    work. That is #266's signature, and it is what AC 3's first leg was written against.
    """
    text = read_text(os.path.join(root, ORDER), f"the retirement order ({ORDER})")
    starts = [index for index in range(len(text)) if text.startswith(ANCHOR_OPEN, index)]
    if not starts:
        raise GateError(
            f"{ORDER}: carries no `{ANCHOR_OPEN} … {ANCHOR_CLOSE}` block, so there is no standing "
            f"verdict for this gate to read. That is a no-verdict, not a pass: a checker with nothing "
            f"to compare certifies nothing while looking like it certified everything (#266, #1750 AC 3)."
        )
    if len(starts) > 1:
        raise GateError(
            f"{ORDER}: carries {len(starts)} `{ANCHOR_OPEN}` blocks. The count lives in ONE place by "
            f"decision; with two, this gate cannot say which one the document means, and neither can a "
            f"reader. Delete the one that is not the standing verdict."
        )

    start = starts[0]
    end = text.find(ANCHOR_CLOSE, start + len(ANCHOR_OPEN))
    if end < 0:
        raise GateError(f"{ORDER}: the `{ANCHOR_OPEN}` block is never closed with `{ANCHOR_CLOSE}`.")
    body = text[start + len(ANCHOR_OPEN) : end]
    line_number = text.count("\n", 0, start) + 1

    fields: dict[str, str] = {}
    for raw in body.splitlines():
        line = raw.strip()
        if not line:
            continue
        if ":" not in line:
            raise GateError(f"{ORDER}: the verdict block has a line with no `key: value` — {line!r}.")
        key, _, value = line.partition(":")
        key = key.strip()
        if key in fields:
            raise GateError(f"{ORDER}: the verdict block declares `{key}:` twice.")
        fields[key] = value.strip()

    for key in (RETIRED_KEY, INFLIGHT_KEY):
        if key not in fields:
            raise GateError(
                f"{ORDER}: the verdict block has no `{key}:` line. Both keys are read at grade time; a "
                f"missing one is a subject this gate cannot establish, so it refuses rather than "
                f"defaulting it to empty and grading against a list nobody wrote."
            )

    retired = [entry.strip() for entry in fields[RETIRED_KEY].split(",") if entry.strip()]
    if len(set(retired)) != len(retired):
        raise GateError(f"{ORDER}: `{RETIRED_KEY}:` lists a receiver twice — {retired!r}.")

    in_flight: list[str] = []
    refs: dict[str, str] = {}
    for entry in (item.strip() for item in fields[INFLIGHT_KEY].split(",")):
        if not entry:
            continue
        match = INFLIGHT_ENTRY.match(entry)
        if not match:
            raise GateError(
                f"{ORDER}: `{INFLIGHT_KEY}:` entry {entry!r} is not `<receiver>(<ref>)`. An in-flight "
                f"receiver is parked OUT of this gate's comparison, so it may never be parked "
                f"anonymously: name the item or PR whose landing settles it."
            )
        identifier = match.group("id")
        if identifier in refs:
            raise GateError(f"{ORDER}: `{INFLIGHT_KEY}:` lists `{identifier}` twice.")
        in_flight.append(identifier)
        refs[identifier] = match.group("ref").strip()

    overlap = sorted(set(retired) & set(in_flight))
    if overlap:
        raise GateError(
            f"{ORDER}: {', '.join(overlap)} appear(s) in both `{RETIRED_KEY}:` and `{INFLIGHT_KEY}:`. "
            f"A receiver is either settled or it is not; recording both is how an unobserved outcome "
            f"gets written down as an observed one (#1750 AC 4)."
        )
    return retired, in_flight, refs, line_number


def count_in(line: str, total: int) -> int | None:
    """The count a line states out of ``total``, or ``None`` if it states none.

    The denominator must MATCH the roster size. A `3 of 4` in a document about seven receivers is about
    something else, and reading it as this document's count would manufacture findings out of unrelated
    prose — the surest way to get a gate muted (#698).
    """
    for match in COUNT.finditer(line):
        raw_total = match.group("total").lower()
        stated_total = WORDS.get(raw_total, None)
        if stated_total is None:
            stated_total = int(raw_total) if raw_total.isdigit() else None
        if stated_total != total:
            continue
        raw = match.group("n").lower()
        value = WORDS.get(raw, None)
        if value is None:
            value = int(raw) if raw.isdigit() else None
        if value is not None:
            return value
    return None


def headline_finding(root: str, anchor_line: int, retired: int, total: int) -> str | None:
    """Leg 2: the heading the anchor annotates must state the count the anchor implies.

    The anchor is a comment — invisible in every rendered view of this document — so on its own it would
    let the machine-readable list and the sentence a human actually reads drift apart, which is the same
    two-copies defect one level down. Tying them together is one check that spans both, and it is why
    the heading is not merely allowed to carry a count but REQUIRED to.
    """
    lines = read_text(os.path.join(root, ORDER), f"the retirement order ({ORDER})").splitlines()
    for index in range(anchor_line - 2, -1, -1):
        line = lines[index]
        if not HEADING.match(line):
            continue
        stated = count_in(line, total)
        if stated is None:
            return (
                f"{ORDER}:{index + 1}: the heading the verdict block annotates states no count out of "
                f"{total} — {line.strip()!r}. The block is an HTML comment and renders as nothing, so a "
                f"heading without the count leaves every human reader with no verdict at all while this "
                f"gate reports green over one."
            )
        if stated != retired:
            return (
                f"{ORDER}:{index + 1}: the heading says {stated} of {total}; the verdict block lists "
                f"{retired} retired receiver(s). The prose a reader believes and the list this gate "
                f"grades have drifted — fix whichever is wrong, in the same edit."
            )
        return None
    return (
        f"{ORDER}: the verdict block at line {anchor_line} is preceded by no heading, so there is no "
        f"standing-verdict headline for it to annotate."
    )


def second_count_findings(root: str, anchor_line: int, total: int) -> list[str]:
    """Leg 4: no second count on the document's LIVE SURFACE.

    The surface, and why it is exactly this, is argued in the module header. In short: the header block
    above the first `---` rule (which the document itself titles *"Where the live state is"*) and every
    ATX heading. The heading the anchor annotates is the one place the count belongs and is exempt; leg
    2 above is what grades it.
    """
    lines = read_text(os.path.join(root, ORDER), f"the retirement order ({ORDER})").splitlines()

    governing = None
    for index in range(anchor_line - 2, -1, -1):
        if HEADING.match(lines[index]):
            governing = index
            break

    header_end = len(lines)
    for index, line in enumerate(lines):
        if line.rstrip() == "---":
            header_end = index
            break

    findings: list[str] = []
    for index, line in enumerate(lines):
        if index == governing:
            continue
        live = index < header_end or HEADING.match(line)
        if not live:
            continue
        stated = count_in(line, total)
        if stated is None:
            continue
        where = "the header block" if index < header_end else "a heading"
        findings.append(
            f"{ORDER}:{index + 1}: {where} states a second count ({stated} of {total}) — "
            f"{line.strip()!r}. The count lives in the standing verdict and nowhere else; every copy "
            f"is a thing that goes stale on its own and that no single edit can reach. Replace it with "
            f"a pointer at the standing verdict — a pointer cannot go stale."
        )
    return findings


# ---------------------------------------------------------------------------------------------------
# The receivers' trees.
# ---------------------------------------------------------------------------------------------------


def receiver_state(slug: str, fallback_roots: list[str]) -> tuple[bool, dict[str, int], list[str]]:
    """``(retired?, blobs per root, the roots used)`` for one receiver, from its own tree.

    THE READS ARE ORDERED, AND THE ORDER IS THE FAIL-CLOSED CLAIM. The repository read comes FIRST, so a
    404 anywhere after it is known to be a real answer about a path rather than a token that cannot see
    the repository at all. Collapsing the two would render a permissions problem as a confident finding
    about somebody else's retirement — or, in the other direction, a receiver that genuinely re-committed
    a root as "could not look", which is the shape a red gets muted for.
    """
    try:
        repository = gh_json(f"repos/{slug}")
    except Missing as error:
        raise Unreachable(
            f"{slug}: the repository itself answers 404. GitHub says that both for 'no such repository' "
            f"and for 'this token may not know whether it exists', so this is 'we did not look', never "
            f"a verdict about {slug}'s roots ({error})."
        ) from error
    if not isinstance(repository, dict) or not isinstance(repository.get("default_branch"), str):
        raise Unreachable(f"{slug}: the repository read carries no `default_branch`.")
    branch = repository["default_branch"]

    tree = gh_json(f"repos/{slug}/git/trees/{branch}?recursive=1")
    if not isinstance(tree, dict) or not isinstance(tree.get("tree"), list):
        raise Unreachable(f"{slug}: the tree read at `{branch}` carries no `tree` array.")
    if tree.get("truncated"):
        # A TRUNCATED TREE IS NOT A TREE. The API drops entries silently past its cap, and the entries it
        # drops are exactly the ones whose absence this gate reads as "retired". A green computed over a
        # partial tree is the fail-open, arrived at by believing an answer that told us it was partial.
        raise GateError(
            f"{slug}: the recursive tree read at `{branch}` came back TRUNCATED, so the absence of a "
            f"root in it is not evidence the root is absent. Refusing to grade a partial tree (#266)."
        )
    paths = [entry["path"] for entry in tree["tree"] if entry.get("type") == "blob" and entry.get("path")]

    roots = fallback_roots
    if ".agent-skill-roots" in paths:
        declared = gh_api(
            f"repos/{slug}/contents/.agent-skill-roots",
            "-H",
            "Accept: application/vnd.github.raw",
        ).decode("utf-8", "replace")
        # Same grammar `scripts/lib/roots.sh:read_roots_decl` parses: whitespace-separated, `#` comments.
        found = [
            token
            for line in declared.splitlines()
            if not line.lstrip().startswith("#")
            for token in line.split()
        ]
        if found:
            roots = found

    blobs = {root: sum(1 for path in paths if path.startswith(root.rstrip("/") + "/")) for root in roots}
    occupied = [root for root, count in blobs.items() if count]
    return len(occupied) <= 1, blobs, roots


def main(argv: list[str]) -> int:
    parser = base_parser(
        "Compare the retirement order's standing verdict against the receivers' own trees."
    )
    parser.add_argument(
        "--offline",
        action="store_true",
        help="grade only the document (anchor, headline, second-count) and read no receiver trees",
    )
    args = parser.parse_args(argv)
    root = args.root

    receivers = roster(root)
    identifiers = {identifier for identifier, _ in receivers}
    total = len(receivers)
    retired, in_flight, refs, anchor_line = anchor(root)

    unknown = sorted((set(retired) | set(in_flight)) - identifiers)
    if unknown:
        raise GateError(
            f"{ORDER}: the verdict block names {', '.join(unknown)}, which the roster ({ROSTER}) does "
            f"not list as a `{CAPABILITY}` receiver. The roster is the receiver set; a verdict about a "
            f"repo outside it is a verdict about nothing."
        )

    findings: list[str] = []
    headline = headline_finding(root, anchor_line, len(retired), total)
    if headline:
        findings.append(headline)
    findings.extend(second_count_findings(root, anchor_line, total))

    if args.offline:
        if findings:
            return report_findings(NAME, findings)
        return report_ok(
            NAME,
            f"document only (--offline): the verdict block says {len(retired)} of {total} retired, the "
            f"headline agrees, and no second count is on the live surface. NO receiver tree was read, "
            f"so this says NOTHING about whether the count is true.",
        )

    fallback = default_roots(root)
    graded = 0
    for identifier, slug in receivers:
        if identifier in in_flight:
            continue
        graded += 1
        is_retired, blobs, roots = receiver_state(slug, fallback)
        claimed = identifier in retired
        if is_retired == claimed:
            continue
        occupied = ", ".join(f"{root}={count}" for root, count in sorted(blobs.items()))
        if is_retired:
            findings.append(
                f"{slug} ({identifier}) IS retired — its tree commits at most one skill root "
                f"({occupied}, roots {' '.join(roots)}) — and {ORDER}'s standing verdict does not list "
                f"it as retired. The document is behind its own subject; that is the exact staleness "
                f"#1750 was filed for. Add it to `{RETIRED_KEY}:`, correct the headline count in the "
                f"same edit, and APPEND a dated record — never edit a historical block to carry it."
            )
        else:
            findings.append(
                f"{slug} ({identifier}) is NOT retired — its tree still commits more than one skill "
                f"root ({occupied}, roots {' '.join(roots)}) — and {ORDER}'s standing verdict lists it "
                f"as retired. Either the retirement was reverted in that repo, or the verdict recorded "
                f"an outcome nobody observed. Do not 'fix' this by re-running anything here: go read "
                f"{slug}'s tree."
            )

    if findings:
        return report_findings(NAME, findings)

    parked = (
        ""
        if not in_flight
        else " NOT GRADED (in flight): "
        + ", ".join(f"{identifier} {refs[identifier]}" for identifier in sorted(in_flight))
        + "."
    )
    return report_ok(
        NAME,
        f"{len(retired)} of {total} retired, and {graded} receiver tree(s) agree. The headline agrees "
        f"with the verdict block and no second count is on the live surface.{parked}",
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
