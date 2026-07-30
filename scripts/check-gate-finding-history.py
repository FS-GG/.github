#!/usr/bin/env python3
"""Which gates have ever produced a finding, and which have been red long enough to stop being read?

THE QUESTION, AND WHY NOBODY HAD ASKED IT (.github#1582, and #266 before it). This org runs ~79
workflows in `.github` alone and ~50 checker scripts, and *nothing measures whether the checks
themselves still measure anything*. On 2026-07-27/28 ten checks that COULD NOT FAIL were found in ten
different subsystems — #1644, #1715, FS.GG.Audio#212, FS.GG.Rendering#1120, #1710, #1768, #1772,
#1740, #1784, #1799 — and not one of them was found by the check itself. Every single one was found
because a human built the same thing twice and compared. That is not a detection strategy; it is luck,
and it does not scale.

This module is the smallest measurement that turns one of those questions from luck into arithmetic:

    A gate that has run N times and NEVER ONCE concluded `failure` is either protecting something that
    never breaks, or it is decorative. From outside those look identical — and today's evidence says
    this repo contains both.

It is a REPORT, NOT AN ENFORCER, and that distinction is deliberate rather than timid. The honest
finding "N gates have never fired" is information for a human, not a licence for a script to delete
them; #1582's own constraint says so. So this is wired into CI only through its SELFTEST — the fixture
that proves this tool itself can fail — and never as a required context that would turn its findings
into a merge block. Deleting checks is not this tool's job and it must not become a tool that makes
deleting checks easy.

WHAT IT CANNOT SEE, STATED HERE RATHER THAN DISCOVERED LATER. These are limits of the Actions API, not
of the code, and every one of them is printed in the report so a reader cannot mistake the answer for
a bigger one than it is:

  * RETENTION. GitHub retains workflow runs for a bounded window (90 days by default, and an org may
    set it lower). `totalRuns`, red-run ids, and their verdict evidence cover RETAINED history.
    "Never produced a finding" therefore always means "never within retained history" — a gate that
    red-lit once a year ago and has been green since reads as NEVER-FOUND here. Annotation/log
    evidence may expire sooner than the run; that is EVIDENCE-EXPIRED, never an invented absence.
    That is why LOW-SAMPLE and EVIDENCE-EXPIRED exist below.
  * IDENTITY. A workflow is keyed by its file path. Renaming the file starts a new history; the old
    runs are still in the API under the old id but are not joined to the new one. A recently renamed
    gate looks young.
  * `failure` IS NOT `finding`. A run concludes `failure` when the gate found something AND when the
    gate itself crashed, ran out of a token, or hit a 5xx. Acquisition therefore reads every retained
    red run's check annotations and recognises the gate harness's `FAILED — N finding(s)` marker. A
    red with an explicit crash/no-verdict marker is FALLEN-OVER, not EXERCISED. Readable prose that
    proves neither is EVIDENCE-AMBIGUOUS; an annotation read that failed is UNREAD; evidence GitHub
    retained for less time than the run is EVIDENCE-EXPIRED. None is rounded into a finding or a
    fallover, and the per-run evidence stays in the corpus and JSON report.
  * SUBJECT CHANGE. This does not know whether the thing a gate guards ever changed. A gate that has
    never fired over a subject nobody touched is unremarkable; the same gate over a subject that moved
    daily is a suspect. Joining run history to path-filtered commit history is the obvious next leg
    and it is NOT done here.

THE #266 DISCIPLINE, WHICH IS THE WHOLE POINT AND IS ENFORCED IN THE EXIT CODE. "I could not evaluate
this" is never "I evaluated it and it passed". Three separate states here are NOT green:

  * UNREAD      — the API did not answer for this workflow. Exit 2 (retryable), never 0.
  * LOW-SAMPLE  — it has run, but too few times for "never failed" to mean anything. Exit 3, never 0.
  * NO SUBJECT  — a corpus with zero workflows in it. Exit 3. A gate-auditing tool that finds no gates
                  has been pointed at the wrong thing; reporting green over that would make this the
                  eleventh entry on the list above, and the funniest.

THE TWO HALVES ARE SPLIT ON PURPOSE, AND #1772 IS WHY. That item found a fixture testing a HAND-WRITTEN
MIRROR of the probe rather than the probe. So acquisition (`--fetch`, which talks to the Actions API)
is a separate mode from classification (pure, total, over a JSON corpus). The fixture feeds planted
corpora to the REAL classifier and drives the REAL acquisition path against a recorded REST transcript
on a local server — there is no second copy of either rule set for a fixture to drift against.

    scripts/check-gate-finding-history.py --fetch --repo FS-GG/.github --out corpus.json
    scripts/check-gate-finding-history.py --corpus corpus.json [--json] [--markdown]
"""

from __future__ import annotations

import argparse
import datetime as _dt
import http.client
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Iterable

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib.gate import ExitCode, GateError, Unreachable, run  # noqa: E402

NAME = "check-gate-finding-history"

CORPUS_SCHEMA = 3
"""Bumped when the corpus SHAPE changes. `classify` refuses an unknown schema rather than guessing at
fields it does not recognise — a corpus it half-understands is a measurement of nothing."""

RED_CONCLUSIONS = frozenset({"failure", "timed_out"})
"""A run whose colour was red. Its annotations decide whether that red was a finding."""

FINDING_MARKER = " FAILED — "
"""The stable summary emitted by :func:`lib.gate.report_findings` before ``N finding(s)``."""

FALLOVER_MARKERS = (
    "the gate crashed, so it has no verdict",
    ": no verdict —",
    "the operation was canceled",
    "the runner has received a shutdown signal",
    "no space left on device",
    "the action has timed out",
)
"""Explicit crash/no-verdict/runner evidence. Unknown red prose stays ambiguous, never guessed."""


class EvidenceExpired(GateError):
    """The run remains retained, but GitHub no longer retains its diagnostic evidence."""

PASS_CONCLUSIONS = frozenset({"success"})
"""A run that reported CLEAN."""

# Everything else — `cancelled`, `skipped`, `neutral`, `stale`, `action_required`, `startup_failure`,
# and a null conclusion (still running) — is a NON-VERDICT. It is neither counted as a finding nor
# allowed to break a red streak, and that second half is load-bearing: treating a `cancelled` run as a
# green would let one cancellation hide an otherwise unbroken week of red, which is precisely the
# "nobody could distinguish a new finding from the standing one" failure this tool exists to surface.

DEFAULT_MIN_RUNS = 10
"""Below this many retained runs, "never failed" is not evidence of anything — it is a small sample.
Ten is a judgement, not a law, which is why it is a flag. It is set where it is because the gates in
this repo that demonstrably DO fire (`repos-audit`, `coordination-coherence`) reached their first red
well inside ten runs, so a gate past ten with a perfectly clean sheet is genuinely unusual."""

DEFAULT_RED_HOURS = 24.0
"""How long a default-branch red must stand before it stops being news and becomes wallpaper. 24h is
#1611's category-D boundary made concrete: `repos-audit` was red for over a day on one unrelated cause,
two workers correctly stepped around it, and for that whole day nobody watching its colour could have
told a NEW finding from the standing one. A gate in that state is not protecting anything."""

DEFAULT_RUN_WINDOW = 30
"""How many recent default-branch runs to pull for the streak measurement. The streak only needs to
reach back past the red threshold; a full history walk would cost a page per workflow for an answer
the first few rows already contain. A streak that consumes the entire window is reported as
`streakTruncated`, so "red for at least the whole window" is never rounded to an exact age."""

VERDICTS = ("EXERCISED", "STANDING-RED", "FALLEN-OVER", "NEVER-FOUND", "NEVER-RAN",
            "REUSABLE-ELSEWHERE", "LOW-SAMPLE", "EVIDENCE-AMBIGUOUS", "EVIDENCE-EXPIRED", "UNREAD")

FINDING_VERDICTS = frozenset({"STANDING-RED", "FALLEN-OVER", "NEVER-FOUND", "NEVER-RAN"})
"""Verdicts that are a real, actionable statement about the subject → exit 1."""

UNKNOWN_VERDICTS = frozenset({
    "UNREAD", "LOW-SAMPLE", "REUSABLE-ELSEWHERE", "EVIDENCE-AMBIGUOUS", "EVIDENCE-EXPIRED"
})
"""Verdicts that are an admission, not an answer → exit 2 or 3, and NEVER 0."""

NON_SELF_STARTING_TRIGGERS = frozenset({"workflow_call"})
"""The triggers that do NOT give a workflow run history of its own in the repo that defines it.

A reusable workflow executes as a JOB INSIDE THE CALLER'S RUN — it never accrues runs of its own where
it is defined. Seven of this repo's workflows are `workflow_call`-only (`contract-coherence`,
`coordination-coherence`, `dispatch-sender`, `kit-materialize`, `lock-range-coherence`,
`lockfile-sync`, `skill-union-assert`), and the first version of this gate reported every one of them
as NEVER-RAN — a confident finding that they "have never executed" when in truth they execute
constantly, somewhere this measurement cannot see. That is #238's false accusation, and seven loud
wrong findings are how a report gets ignored — which would make this tool decorative in precisely the
way it exists to detect.

THE SET IS SPELLED AS THE EXCLUSIONS, NOT AS THE ALLOWED EVENTS, AND THAT IS THE FAIL-LOUD DIRECTION.
The first draft enumerated the ~33 self-starting event names instead, which reads as more rigorous and
is strictly worse: any event GitHub adds — or that the enumeration simply forgot — is then absent from
the list, and a genuinely dead workflow triggered by it gets quietly filed as "reusable, nothing to
see". A closed enumeration of the SAFE cases fails open, which is #1161's argument for an allow-list
inverted to the direction that suits this decision. Here the dangerous set is the small, closed,
well-known one, so it is the one that gets enumerated: everything not named here counts as
self-starting, and an unknown trigger over-reports NEVER-RAN rather than excusing it. The fixture pins
both directions.
"""


# ----------------------------------------------------------------------------------------------
# Classification — pure, total, offline. This is the half the fixture drives.
# ----------------------------------------------------------------------------------------------


def _utc(text: str, what: str) -> _dt.datetime:
    """Parse a GitHub ISO-8601 `Z` timestamp into an aware UTC datetime, or raise.

    A timestamp this cannot read is a no-verdict, not a zero: silently treating an unparsable
    `created_at` as "just now" would collapse every standing red to fresh.
    """
    try:
        return _dt.datetime.fromisoformat(text.replace("Z", "+00:00")).astimezone(_dt.timezone.utc)
    except (AttributeError, TypeError, ValueError) as e:
        raise GateError(f"{what}: cannot parse timestamp {text!r} — {e}") from e


def red_streak(runs: Iterable[dict], *, what: str) -> tuple[int, str | None, bool]:
    """The unbroken leading run of finding-conclusions in a newest-first run list.

    Returns `(length, oldest_timestamp_in_the_streak, truncated)`. `truncated` is True when the streak
    consumed every run in the list, i.e. the real streak may be longer than the window shows — the
    caller must not report an exact age for a truncated streak, only a lower bound.

    NON-VERDICT RUNS ARE SKIPPED, NOT COUNTED AND NOT TREATED AS GREEN. A cancelled or in-progress run
    says nothing about the gate's colour; letting one end the streak is how a month of red gets
    reported as "went green on Tuesday".
    """
    length = 0
    oldest: str | None = None
    ended_on_a_pass = False
    for entry in runs:
        conclusion = entry.get("conclusion")
        if conclusion in PASS_CONCLUSIONS:
            ended_on_a_pass = True
            break
        if conclusion in RED_CONCLUSIONS:
            length += 1
            oldest = entry.get("createdAt")
            _utc(oldest, f"{what}: run {entry.get('headSha', '?')}")  # validated, not merely read
        # else: a non-verdict run — skipped entirely, and NOT allowed to end the streak.
    # TRUNCATED means we walked off the end of the sampled window without ever meeting a green. The
    # real streak may reach further back than the window, so the caller must treat the age as a LOWER
    # bound. Deriving it from "did we stop early on a pass?" rather than from a length comparison is
    # deliberate: a length test has to reason about how many non-verdict rows were skipped, and that
    # arithmetic is exactly the kind that inverts silently.
    return length, oldest, (length > 0 and not ended_on_a_pass)


def classify_workflow(wf: dict, *, repo: str, now: _dt.datetime, min_runs: int, red_hours: float) -> dict:
    """One workflow → one verdict row. Total: every input lands on exactly one of :data:`VERDICTS`.

    THE ORDER OF THESE TESTS IS THE CONTRACT. Unread first, because a workflow we could not read must
    never be measured on fields that are absent for exactly that reason. Standing-red before
    never-found, because a gate that is red RIGHT NOW has obviously produced a finding and the
    interesting thing about it is the duration, not the existence. Never-ran before low-sample, because
    zero runs is a definite statement ("this has never executed") while three runs is an admission.
    """
    path = wf.get("path") or wf.get("name") or "<unnamed>"
    what = f"{repo} {path}"
    row: dict[str, Any] = {
        "repo": repo,
        "name": wf.get("name"),
        "path": path,
        "state": wf.get("state"),
    }

    unread = wf.get("unread")
    if unread:
        row.update(verdict="UNREAD", detail=str(unread))
        return row

    total = wf.get("totalRuns")
    evaluated = wf.get("evaluatedRuns")
    red_count = wf.get("redRunCount")
    red_runs = wf.get("redRuns")
    if (not isinstance(total, int) or not isinstance(evaluated, int) or not isinstance(red_count, int)
            or total < 0 or evaluated < 0 or red_count < 0 or evaluated > total):
        # A corpus row that claims to be read but carries no counts is NOT a clean gate — it is a row
        # this cannot judge. Degrading it to UNREAD keeps it out of the green column (#266).
        row.update(
            verdict="UNREAD",
            detail=(f"corpus row has no usable run counts (totalRuns={total!r}, "
                    f"evaluatedRuns={evaluated!r}, redRunCount={red_count!r})"),
        )
        return row
    if red_count > evaluated:
        raise GateError(
            f"{what}: redRunCount={red_count} exceeds evaluatedRuns={evaluated}. That is an incoherent corpus, "
            f"not a gate with a lot of findings — refusing to classify it."
        )
    if not isinstance(red_runs, list):
        row.update(verdict="UNREAD", detail="corpus row carries no per-run red evidence")
        return row
    if len(red_runs) != red_count:
        raise GateError(
            f"{what}: redRunCount={red_count}, but redRuns carries {len(red_runs)} row(s). "
            "A partial evidence set cannot establish either findings or fallovers."
        )

    row["totalRuns"] = total
    row["evaluatedRuns"] = evaluated
    row["redRunCount"] = red_count
    evidence_counts = {"finding": 0, "fallover": 0, "ambiguous": 0, "unread": 0, "expired": 0}
    normalised_evidence: list[dict[str, Any]] = []
    for i, evidence in enumerate(red_runs):
        if not isinstance(evidence, dict):
            raise GateError(f"{what}: redRuns[{i}] is {type(evidence).__name__}, not an object")
        kind = evidence.get("evidence")
        if kind not in evidence_counts:
            raise GateError(f"{what}: redRuns[{i}] has unknown evidence state {kind!r}")
        evidence_counts[kind] += 1
        normalised_evidence.append(dict(evidence))
    row["findingRuns"] = evidence_counts["finding"]
    row["falloverRuns"] = evidence_counts["fallover"]
    row["ambiguousEvidenceRuns"] = evidence_counts["ambiguous"]
    row["unreadEvidenceRuns"] = evidence_counts["unread"]
    row["expiredEvidenceRuns"] = evidence_counts["expired"]
    row["redEvidence"] = normalised_evidence

    default_runs = wf.get("defaultBranchRuns") or []
    streak, oldest, truncated = red_streak(default_runs, what=what)
    if streak:
        age_h = (now - _utc(oldest, what)).total_seconds() / 3600.0
        row.update(redStreak=streak, redSince=oldest, redHours=round(age_h, 2), streakTruncated=truncated)
        if age_h >= red_hours and evidence_counts["finding"] > 0:
            row.update(
                verdict="STANDING-RED",
                detail=(
                    f"red on the default branch for {age_h:.1f}h across {streak} consecutive run(s)"
                    + (" (streak fills the sampled window, so this is a LOWER bound)" if truncated else "")
                    + f" — past the {red_hours:g}h point where a colour stops being read"
                ),
            )
            return row

    if red_count > 0 and evidence_counts["finding"] == 0:
        if evidence_counts["unread"]:
            row.update(
                verdict="UNREAD",
                detail=(
                    f"{red_count} retained red run(s), but evidence for "
                    f"{evidence_counts['unread']} could not be read. Per-run failures are preserved; "
                    "the workflow is neither EXERCISED nor classified as fallover-only."
                ),
            )
            return row
        if evidence_counts["expired"]:
            row.update(
                verdict="EVIDENCE-EXPIRED",
                detail=(
                    f"{red_count} retained red run(s), but GitHub no longer retains evidence for "
                    f"{evidence_counts['expired']}. Run retention outlived annotation/log retention, "
                    "so no finding verdict can be reconstructed."
                ),
            )
            return row
        if evidence_counts["ambiguous"]:
            row.update(
                verdict="EVIDENCE-AMBIGUOUS",
                detail=(
                    f"{red_count} retained red run(s); {evidence_counts['ambiguous']} had readable "
                    "annotations but neither the gate finding marker nor an explicit crash/no-verdict "
                    "marker. Unknown prose is not guessed into either bucket."
                ),
            )
            return row
        row.update(
            verdict="FALLEN-OVER",
            detail=(
                f"{red_count} retained red run(s), all with readable evidence and none carrying the "
                "gate finding marker — this gate has only ever fallen over, timed out, or failed "
                "infrastructure; it has not demonstrated a verdict about its subject"
            ),
        )
        return row

    if total == 0:
        # ZERO RUNS IS THREE DIFFERENT FACTS, AND THE TRIGGERS ARE WHAT SEPARATE THEM. A dead workflow
        # and a reusable one look identical from the runs endpoint; only the `on:` block tells them
        # apart, and if the corpus does not carry it then this tool DOES NOT KNOW which it is looking
        # at and must say so rather than pick the louder answer (#266).
        trig = wf.get("triggers")
        if trig is None:
            row.update(
                verdict="UNREAD",
                detail="zero runs in retained history, and the corpus records no `on:` triggers — so "
                "'this never ran' cannot be distinguished from 'this is a reusable workflow that runs "
                "inside its callers'. That is an unanswered question, not a finding.",
            )
            return row
        if not isinstance(trig, list):
            raise GateError(f"{what}: `triggers` is {type(trig).__name__}, not a list")
        row["triggers"] = sorted(str(t) for t in trig)
        self_starting = sorted(t for t in row["triggers"] if t not in NON_SELF_STARTING_TRIGGERS)
        if not self_starting:
            row.update(
                verdict="REUSABLE-ELSEWHERE",
                detail=f"no runs of its own, and no self-starting trigger ({', '.join(row['triggers']) or 'none'}) "
                f"— it executes inside its CALLERS' runs, which this measurement cannot see. UNMEASURED, "
                f"not dead: reporting it as never-run would be a false accusation (#238).",
            )
            return row
        row.update(
            verdict="NEVER-RAN",
            detail=f"no runs in retained history despite self-starting trigger(s) "
            f"({', '.join(self_starting)}) — this workflow has never executed, so whatever it asserts "
            f"has never been asserted",
        )
        return row

    if red_count == 0:
        if evaluated < min_runs:
            row.update(
                verdict="LOW-SAMPLE",
                detail=(f"{evaluated} evaluated run(s) out of {total} retained, none red — below the "
                        f"{min_runs}-run floor, so ")
                + "'never fired' is not evidence. NOT a clean verdict: this is unmeasured.",
            )
            return row
        row.update(
            verdict="NEVER-FOUND",
            detail=f"{evaluated} evaluated run(s) out of {total} retained and NOT ONE red — either it guards something that never "
            f"breaks, or it cannot fail. From outside those are indistinguishable.",
        )
        return row

    row.update(
        verdict="EXERCISED",
        detail=(
            f"{evidence_counts['finding']} of {evaluated} evaluated run(s) ({total} retained) carried a gate finding marker"
            + (
                f"; {evidence_counts['fallover']} other red run(s) were fallovers"
                if evidence_counts["fallover"]
                else ""
            )
            + " — this gate demonstrably reached a finding verdict"
        ),
    )
    return row


def classify(corpus: dict, *, min_runs: int, red_hours: float, now: _dt.datetime | None = None) -> list[dict]:
    """Every workflow in the corpus → a verdict row. Raises :class:`GateError` on an unusable corpus.

    AN EMPTY SUBJECT IS A NO-VERDICT, NOT A PASS. #1784 shipped a check that printed
    *"ok: all 0 stable version(s)… has not moved"* at exit 0. A corpus with no repos, or repos with no
    workflows, is the same shape and gets the same refusal here.
    """
    schema = corpus.get("schema")
    if schema != CORPUS_SCHEMA:
        raise GateError(
            f"corpus schema is {schema!r}, this build understands {CORPUS_SCHEMA}. A corpus this tool "
            f"only half-understands measures nothing; refusing rather than guessing."
        )
    repos = corpus.get("repos")
    if not isinstance(repos, list) or not repos:
        raise GateError("corpus contains no repos — there is nothing to audit, which is not a pass")

    now = now or _dt.datetime.now(_dt.timezone.utc)
    rows: list[dict] = []
    for entry in repos:
        if not isinstance(entry, dict):
            raise GateError(f"corpus repo entry is {type(entry).__name__}, not an object")
        repo = entry.get("repo") or "<unnamed>"
        if entry.get("unread"):
            # A whole repo we could not read is ONE unread row, not zero rows. Zero rows would make an
            # unreadable repo indistinguishable from a repo with no workflows, which is the exact
            # collapse this file exists to prevent.
            rows.append({"repo": repo, "name": None, "path": "<whole repo>", "verdict": "UNREAD",
                         "detail": str(entry["unread"])})
            continue
        workflows = entry.get("workflows")
        if not isinstance(workflows, list):
            raise GateError(f"{repo}: `workflows` is not a list")
        if not workflows:
            raise GateError(
                f"{repo}: the corpus records zero workflows and no read error. A repo with no "
                f"workflows and a repo whose workflow list failed to load are not the same fact, and "
                f"this tool will not report the second as the first (#266)."
            )
        for wf in workflows:
            if not isinstance(wf, dict):
                raise GateError(f"{repo}: workflow entry is {type(wf).__name__}, not an object")
            rows.append(classify_workflow(wf, repo=repo, now=now, min_runs=min_runs, red_hours=red_hours))

    if not rows:
        raise GateError("no workflows were classified — an empty measurement is not a clean one")
    return rows


def verdict_exit(rows: list[dict]) -> ExitCode:
    """Findings outrank admissions; admissions outrank green; green is only ever reached honestly.

    The precedence is the #266 rule written as arithmetic: an UNREAD row can never be summed away by a
    hundred green ones, because the branch that returns OK is guarded on BOTH unknown sets being empty.
    """
    if any(r["verdict"] in FINDING_VERDICTS for r in rows):
        return ExitCode.FINDING
    if any(r["verdict"] == "UNREAD" for r in rows):
        return ExitCode.NO_VERDICT_RETRYABLE
    if any(r["verdict"] in UNKNOWN_VERDICTS for r in rows):
        return ExitCode.NO_VERDICT_PERMANENT
    return ExitCode.OK


# ----------------------------------------------------------------------------------------------
# Acquisition — the network half. Never runs in a fixture; every failure becomes an UNREAD row.
# ----------------------------------------------------------------------------------------------

API = os.environ.get("GITHUB_API_URL", "https://api.github.com").rstrip("/")


def _get(path: str, *, what: str, expired_is_distinct: bool = False) -> Any:
    """One REST GET against the Actions API.

    REST, not GraphQL, and deliberately: `check-graphql-monopoly.py` reserves the shared 5,000-point
    GraphQL budget for the coordination client, and draining it is how the board starts lying (#587,
    #418). The Actions run history lives on REST's own budget, so this measurement cannot cost the
    fleet its ability to coordinate.
    """
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if not token:
        raise Unreachable(
            "no GITHUB_TOKEN/GH_TOKEN in the environment — the Actions run history is not readable "
            "anonymously, and an unauthenticated read is not an empty history"
        )
    req = urllib.request.Request(
        f"{API}/{path.lstrip('/')}",
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "fsgg-check-gate-finding-history",
        },
    )
    # A FLEET SWEEP IS A BURST, AND GITHUB ANSWERS BURSTS WITH 403. This measurement makes ~4 requests
    # per workflow across ~250 workflows, and GitHub's SECONDARY rate limit — which is about request
    # RATE, not the 5,000/hour primary quota — starts refusing with a bare 403 well before the primary
    # budget is touched. Measured while writing this: the first sweep read `.github` cleanly and then
    # 403'd all seven remaining repos, and a sweep started minutes later 403'd everything including
    # `.github`, while the primary budget still showed 4,338 of 5,000 remaining and a single `gh api`
    # call to the same URL succeeded.
    #
    # WITHOUT THIS RETRY THE TOOL WOULD REPORT SEVEN REPOS AS UNREAD AND BE RIGHT TO — and that is
    # exactly the trap. An honest UNREAD is correct behaviour but a useless report; a reader who sees
    # "7 of 8 repos unread" every single run learns to skip the line, which is #1611's category-D
    # finding arriving from the other direction. So the sweep backs off and tries again rather than
    # normalising its own blindness.
    delay = 2.0
    last: Exception | None = None
    for attempt in range(5):
        if attempt:
            time.sleep(delay)
            delay *= 2
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            last = e
            if expired_is_distinct and e.code in (404, 410):
                raise EvidenceExpired(
                    f"{what}: HTTP {e.code} — the run is retained but its annotations/jobs are not"
                ) from e
            retry_after = e.headers.get("Retry-After") if e.headers else None
            if retry_after:
                try:
                    delay = max(delay, float(retry_after))
                except ValueError:
                    pass
            # 403/429 are the rate-limit shapes and are worth another go. A 404 is a REAL ANSWER — the
            # repo or workflow is not there — and retrying it four more times only wastes budget.
            if e.code not in (403, 429):
                raise Unreachable(f"{what}: HTTP {e.code} {e.reason}") from e
        except (urllib.error.URLError, TimeoutError, OSError, http.client.HTTPException) as e:
            last = e
        except json.JSONDecodeError as e:
            raise Unreachable(f"{what}: response was not JSON — {e}") from e
    if isinstance(last, urllib.error.HTTPError):
        raise Unreachable(
            f"{what}: HTTP {last.code} {last.reason} after 5 attempts with backoff — this is most "
            f"likely GitHub's secondary (rate-of-request) limit rather than a permission problem; the "
            f"primary quota can be untouched while this fires. UNREAD, not clean."
        ) from last
    raise Unreachable(f"{what}: {last!r} after 5 attempts with backoff")


def _count(repo: str, wf_id: Any, query: dict, *, what: str) -> int:
    """`total_count` for a filtered run query — the exact count over RETAINED history in one request.

    `per_page=1` because only the count is wanted: paging the runs to count them would cost a request
    per hundred for a number the first response already carries.
    """
    q = dict(query)
    q["per_page"] = 1
    doc = _get(f"repos/{repo}/actions/workflows/{wf_id}/runs?{urllib.parse.urlencode(q)}", what=what)
    n = doc.get("total_count")
    if not isinstance(n, int):
        raise Unreachable(f"{what}: response carried no integer total_count")
    return n


def _red_runs(repo: str, wf_id: Any, conclusion: str) -> list[dict]:
    """Every retained run with one red conclusion, paging until the API's declared count is met."""
    rows: list[dict] = []
    page = 1
    declared: int | None = None
    while declared is None or len(rows) < declared:
        query = urllib.parse.urlencode({"status": conclusion, "per_page": 100, "page": page})
        doc = _get(
            f"repos/{repo}/actions/workflows/{wf_id}/runs?{query}",
            what=f"{repo} workflow {wf_id}: {conclusion} run evidence listing",
        )
        if declared is None:
            declared = doc.get("total_count")
            if not isinstance(declared, int):
                raise Unreachable(
                    f"{repo} workflow {wf_id}: {conclusion} listing carried no integer total_count"
                )
        batch = doc.get("workflow_runs")
        if not isinstance(batch, list):
            raise Unreachable(f"{repo} workflow {wf_id}: {conclusion} listing carried no workflow_runs")
        if any(not isinstance(run_row, dict) for run_row in batch):
            raise Unreachable(f"{repo} workflow {wf_id}: {conclusion} listing carried a non-object run")
        rows.extend(batch)
        if not batch:
            break
        page += 1
    if declared != len(rows):
        raise Unreachable(
            f"{repo} workflow {wf_id}: API declares {declared} {conclusion} run(s), but paging "
            f"returned {len(rows)} — evidence would be partial"
        )
    return rows


def _annotations(repo: str, check_run_url: str, *, what: str) -> list[dict]:
    """Every annotation for one job/check-run, with expiry distinct from transient unreadability."""
    path = urllib.parse.urlparse(check_run_url).path
    if not path:
        raise Unreachable(f"{what}: job carried no usable check_run_url")
    rows: list[dict] = []
    page = 1
    while True:
        sep = "&" if "?" in path else "?"
        batch = _get(
            f"{path}/annotations{sep}per_page=100&page={page}",
            what=f"{what}: annotations page {page}",
            expired_is_distinct=True,
        )
        if not isinstance(batch, list):
            raise Unreachable(f"{what}: annotations response was not an array")
        if any(not isinstance(annotation, dict) for annotation in batch):
            raise Unreachable(f"{what}: annotations response carried a non-object row")
        rows.extend(batch)
        if len(batch) < 100:
            return rows
        page += 1


def fetch_red_evidence(repo: str, run_row: dict) -> dict:
    """One retained red run → finding/fallover/unread/expired, using its check annotations."""
    run_id = run_row.get("id")
    base = {
        "runId": run_id,
        "conclusion": run_row.get("conclusion"),
        "createdAt": run_row.get("created_at"),
        "headSha": (run_row.get("head_sha") or "")[:7],
    }
    if not isinstance(run_id, int):
        return {**base, "evidence": "unread", "detail": "red run listing carried no integer id"}

    what = f"{repo} run {run_id}"
    try:
        jobs_doc = _get(
            f"repos/{repo}/actions/runs/{run_id}/jobs?filter=all&per_page=100",
            what=f"{what}: jobs",
            expired_is_distinct=True,
        )
        jobs = jobs_doc.get("jobs")
        declared = jobs_doc.get("total_count")
        if not isinstance(jobs, list) or not isinstance(declared, int):
            raise Unreachable(f"{what}: jobs response carried no complete jobs array/count")
        page = 2
        while len(jobs) < declared:
            more = _get(
                f"repos/{repo}/actions/runs/{run_id}/jobs?filter=all&per_page=100&page={page}",
                what=f"{what}: jobs page {page}",
                expired_is_distinct=True,
            )
            batch = more.get("jobs")
            if not isinstance(batch, list) or not batch:
                break
            jobs.extend(batch)
            page += 1
        if len(jobs) != declared:
            raise Unreachable(f"{what}: API declares {declared} jobs but returned {len(jobs)}")

        annotations: list[dict] = []
        for job in jobs:
            url = job.get("check_run_url") if isinstance(job, dict) else None
            if not isinstance(url, str):
                raise Unreachable(f"{what}: a job carried no check_run_url")
            annotations.extend(_annotations(repo, url, what=f"{what} job {job.get('id', '?')}"))

        text = "\n".join(
            str(a.get(k) or "")
            for a in annotations
            for k in ("title", "message", "raw_details")
        )
        if FINDING_MARKER in text and " finding(s)" in text:
            return {
                **base,
                "evidence": "finding",
                "detail": f"finding marker present in {len(annotations)} annotation(s)",
            }
        lower = text.lower()
        if run_row.get("conclusion") == "timed_out" or any(marker in lower for marker in FALLOVER_MARKERS):
            return {
                **base,
                "evidence": "fallover",
                "detail": (
                    f"{len(annotations)} annotation(s) read; explicit crash/no-verdict/timeout/"
                    "infrastructure evidence and no gate finding marker"
                ),
            }
        return {
            **base,
            "evidence": "ambiguous",
            "detail": (
                f"{len(annotations)} annotation(s) read; neither a gate finding marker nor an "
                "explicit crash/no-verdict marker was present"
            ),
        }
    except EvidenceExpired as e:
        return {**base, "evidence": "expired", "detail": str(e)}
    except GateError as e:
        return {**base, "evidence": "unread", "detail": str(e)}


def fetch_triggers(repo: str, path: str) -> list[str]:
    """The workflow's declared `on:` keys, read from the default branch.

    Uses :func:`lib.gate.triggers` rather than a local re-reading of the `on:`/``True`` YAML 1.1 trap.
    That normaliser exists precisely because five gates re-solved it and did not agree (#1158 D2); a
    sixth spelling here would be this repo's own D-class finding — one fact, N implementations.

    A read or parse failure RAISES, so the caller records the workflow as unread. Guessing the triggers
    would decide the never-ran/reusable question by coin toss.
    """
    from lib.gate import load_yaml, triggers as _triggers

    doc = _get(
        f"repos/{repo}/contents/{urllib.parse.quote(path)}",
        what=f"{repo} {path}: workflow source",
    )
    content = doc.get("content")
    if not isinstance(content, str):
        raise Unreachable(f"{repo} {path}: the contents API returned no inline content")
    import base64

    try:
        text = base64.b64decode(content).decode("utf-8")
    except (ValueError, UnicodeDecodeError) as e:
        raise Unreachable(f"{repo} {path}: workflow source is not decodable UTF-8 — {e}") from e
    return [str(k) for k in _triggers(load_yaml(text, f"{repo} {path}"))]


def fetch_repo(repo: str, *, window: int) -> dict:
    """Acquire one repo's workflow-run corpus. Never raises: a failure becomes an `unread` field.

    NOT RAISING IS THE POINT. One unreadable repo must not abort the sweep and must not vanish from
    it either — both would turn "I could not look at FS.GG.Audio" into a report that silently covers
    seven repos while claiming eight.
    """
    entry: dict[str, Any] = {"repo": repo}
    try:
        meta = _get(f"repos/{repo}", what=f"{repo}: repo metadata")
        default_branch = meta.get("default_branch") or "main"
        entry["defaultBranch"] = default_branch
        listing = _get(f"repos/{repo}/actions/workflows?per_page=100", what=f"{repo}: workflow list")
        workflows = listing.get("workflows")
        if not isinstance(workflows, list):
            entry["unread"] = "workflow listing carried no `workflows` array"
            return entry
        declared = listing.get("total_count")
        if isinstance(declared, int) and declared > len(workflows):
            # A truncated listing would silently shrink the subject. Say so rather than measure a part
            # and report it as the whole (#1506's shape: `byte-identical=4` over 30 uncompared).
            entry["unread"] = (
                f"the API declares {declared} workflows but returned {len(workflows)} on one page — "
                f"this sweep does not page the listing, so the subject would be truncated"
            )
            return entry
        if not workflows:
            entry["unread"] = "the repo reports zero workflows — nothing to measure here"
            return entry
    except GateError as e:
        entry["unread"] = str(e)
        return entry

    rows: list[dict] = []
    for wf in workflows:
        row: dict[str, Any] = {
            "name": wf.get("name"),
            "path": wf.get("path"),
            "state": wf.get("state"),
        }
        wf_id = wf.get("id")
        try:
            if wf_id is None:
                raise Unreachable("workflow listing row carried no id")
            total = _count(repo, wf_id, {}, what=f"{repo} {row['path']}: total runs")
            row["totalRuns"] = total
            red_rows = _red_runs(repo, wf_id, "failure") + _red_runs(repo, wf_id, "timed_out")
            successful = _count(repo, wf_id, {"status": "success"}, what=f"{repo} {row['path']}: successful runs")
            row["evaluatedRuns"] = successful + len(red_rows)
            # The two conclusion queries are disjoint by contract. Refuse duplicate ids rather than
            # counting one run twice if the API ever contradicts that contract.
            ids = [r.get("id") for r in red_rows]
            if len(ids) != len(set(ids)):
                raise Unreachable(f"{repo} {row['path']}: red-run listings contained duplicate ids")
            row["redRunCount"] = len(red_rows)
            row["redRuns"] = [fetch_red_evidence(repo, r) for r in red_rows]
            if total == 0:
                # FETCHED ONLY WHERE IT CHANGES AN ANSWER. The `on:` block is what separates a dead
                # workflow from a reusable one, and that question only arises at zero runs — so this
                # costs one extra request for the handful of workflows that have none, rather than one
                # per workflow for a field the other ~95% would never consult.
                row["triggers"] = sorted(fetch_triggers(repo, row["path"]))
            recent = _get(
                f"repos/{repo}/actions/workflows/{wf_id}/runs?"
                + urllib.parse.urlencode({"branch": default_branch, "per_page": window}),
                what=f"{repo} {row['path']}: recent {default_branch} runs",
            )
            row["defaultBranchRuns"] = [
                {
                    "conclusion": r.get("conclusion"),
                    "createdAt": r.get("created_at"),
                    "headSha": (r.get("head_sha") or "")[:7],
                }
                for r in (recent.get("workflow_runs") or [])
            ]
        except GateError as e:
            row["unread"] = str(e)
        rows.append(row)

    entry["workflows"] = rows
    return entry


def fetch(repos: list[str], *, window: int) -> dict:
    return {
        "schema": CORPUS_SCHEMA,
        "fetchedAt": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "runWindow": window,
        "repos": [fetch_repo(r, window=window) for r in repos],
    }


# ----------------------------------------------------------------------------------------------
# Reporting
# ----------------------------------------------------------------------------------------------


def tally(rows: list[dict]) -> dict[str, int]:
    return {v: sum(1 for r in rows if r["verdict"] == v) for v in VERDICTS}


def evidence_notes(row: dict) -> list[str]:
    """Non-finding red evidence, kept per-run even when another run established EXERCISED."""
    notes: list[str] = []
    for evidence in row.get("redEvidence") or []:
        if evidence.get("evidence") == "finding":
            continue
        notes.append(
            f"run {evidence.get('runId', '?')} [{evidence.get('evidence', '?')}] "
            f"{evidence.get('detail', '')}"
        )
    return notes


def render_text(rows: list[dict], counts: dict[str, int], code: ExitCode) -> None:
    for verdict in VERDICTS:
        selected = [r for r in rows if r["verdict"] == verdict]
        if not selected:
            # NAMED EVEN WHEN EMPTY. #1582 §4 constraint 4: "a rule that silently produces nothing is
            # indistinguishable from a rule that is broken". A verdict class that produced no rows says
            # so out loud rather than simply not appearing.
            print(f"{verdict}: 0")
            continue
        print(f"{verdict}: {len(selected)}")
        for r in sorted(selected, key=lambda r: (r["repo"], r["path"])):
            print(f"  {r['repo']}  {r['path']}  — {r.get('detail', '')}")
            for note in evidence_notes(r):
                print(f"    {note}")
    print(
        f"{NAME}: {sum(counts.values())} workflow(s) over retained history; "
        f"{counts['STANDING-RED'] + counts['FALLEN-OVER'] + counts['NEVER-FOUND'] + counts['NEVER-RAN']} finding(s), "
        f"{counts['UNREAD'] + counts['LOW-SAMPLE'] + counts['EVIDENCE-AMBIGUOUS'] + counts['EVIDENCE-EXPIRED']} "
        f"unmeasured; exit {int(code)}"
    )


def render_markdown(rows: list[dict], counts: dict[str, int], corpus: dict, code: ExitCode) -> None:
    print(f"# Gate finding history — {corpus.get('fetchedAt', 'unknown time')}")
    print()
    print(f"Verdict: exit **{int(code)}** ({ExitCode(code).name}).")
    print()
    print("| verdict | count | meaning |")
    print("| --- | --- | --- |")
    meaning = {
        "EXERCISED": "has emitted at least one confirmed gate finding",
        "STANDING-RED": "red on the default branch past the threshold — its colour carries no news",
        "FALLEN-OVER": "red runs exist, but every readable one was crash/no-verdict/infrastructure",
        "NEVER-FOUND": "ran enough times and was never red — decorative, or guarding the unbreakable",
        "NEVER-RAN": "no runs in retained history despite a trigger that could start one",
        "REUSABLE-ELSEWHERE": "`workflow_call`-only — runs inside its callers, invisible here. UNMEASURED",
        "LOW-SAMPLE": "too few runs for 'never red' to mean anything — UNMEASURED, not clean",
        "EVIDENCE-AMBIGUOUS": "red annotations were readable but proved neither finding nor fallover",
        "EVIDENCE-EXPIRED": "runs remain, but their annotations/log evidence expired — UNMEASURED",
        "UNREAD": "the API did not answer — UNMEASURED, not clean (#266)",
    }
    for v in VERDICTS:
        print(f"| {v} | {counts[v]} | {meaning[v]} |")
    print()
    for v in VERDICTS:
        selected = sorted((r for r in rows if r["verdict"] == v), key=lambda r: (r["repo"], r["path"]))
        print(f"## {v} — {len(selected)}")
        print()
        if not selected:
            print("_none_")
            print()
            continue
        print("| repo | workflow | runs | red runs | detail |")
        print("| --- | --- | --- | --- | --- |")
        for r in selected:
            detail = str(r.get("detail", ""))
            notes = evidence_notes(r)
            if notes:
                detail += "<br>" + "<br>".join(notes)
            detail = detail.replace("|", r"\|")
            print(
                f"| {r['repo']} | `{r['path']}` | {r.get('totalRuns', '—')} | "
                f"{r.get('findingRuns', '—')} | {detail} |"
            )
        print()


def main(argv) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--corpus", help="classify this JSON corpus (default: stdin)")
    ap.add_argument("--fetch", action="store_true", help="acquire a corpus from the GitHub API")
    ap.add_argument("--repo", action="append", default=[], metavar="OWNER/NAME", help="repo to fetch (repeatable)")
    ap.add_argument("--out", help="with --fetch: write the corpus here instead of stdout")
    ap.add_argument("--min-runs", type=int, default=DEFAULT_MIN_RUNS)
    ap.add_argument("--red-hours", type=float, default=DEFAULT_RED_HOURS)
    ap.add_argument("--window", type=int, default=DEFAULT_RUN_WINDOW)
    ap.add_argument("--json", action="store_true", help="emit the verdict rows as JSON")
    ap.add_argument("--markdown", action="store_true", help="emit the ledger as a Markdown report")
    ap.add_argument("--now", help="fix 'now' to this ISO-8601 UTC instant (fixtures; makes ages deterministic)")
    args = ap.parse_args(argv)

    if args.fetch:
        if not args.repo:
            raise GateError("--fetch needs at least one --repo OWNER/NAME")
        corpus = fetch(args.repo, window=args.window)
        text = json.dumps(corpus, indent=2, sort_keys=True)
        if args.out:
            with open(args.out, "w", encoding="utf-8") as fh:
                fh.write(text + "\n")
            print(f"{NAME}: wrote a corpus for {len(args.repo)} repo(s) to {args.out}", file=sys.stderr)
        else:
            print(text)
        return int(ExitCode.OK)

    if args.min_runs < 1:
        raise GateError(f"--min-runs must be >= 1, got {args.min_runs}. A floor of 0 would make every "
                        f"never-red gate an EXERCISED-adjacent pass over a sample of nothing.")

    try:
        raw = open(args.corpus, encoding="utf-8").read() if args.corpus else sys.stdin.read()
    except OSError as e:
        raise GateError(f"cannot read the corpus at {args.corpus}: {e}") from e
    if not raw.strip():
        raise GateError("the corpus is empty — an empty measurement is not a clean one")
    try:
        corpus = json.loads(raw)
    except json.JSONDecodeError as e:
        raise GateError(f"the corpus is not JSON: {e}") from e
    if not isinstance(corpus, dict):
        raise GateError("the corpus root must be an object")

    now = _utc(args.now, "--now") if args.now else _dt.datetime.now(_dt.timezone.utc)
    rows = classify(corpus, min_runs=args.min_runs, red_hours=args.red_hours, now=now)
    counts = tally(rows)
    code = verdict_exit(rows)

    if args.json:
        print(json.dumps({"schema": CORPUS_SCHEMA, "fetchedAt": corpus.get("fetchedAt"),
                          "minRuns": args.min_runs, "redHours": args.red_hours,
                          "counts": counts, "exit": int(code), "rows": rows},
                         indent=2, sort_keys=True))
    elif args.markdown:
        render_markdown(rows, counts, corpus, code)
    else:
        render_text(rows, counts, code)
    return int(code)


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
