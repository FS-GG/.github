#!/usr/bin/env python3
"""Assert coord-board-reconcile.yml's concurrency group stays fan-out-safe (.github#2361 round 2).

THE INVARIANT THIS PROTECTS. `coord-board-reconcile.yml`'s single `reconcile` job scans and can
WRITE the entire ~2000-item board on every run that reaches it — not just the item that triggered
it — and posts `Writes.lifecycleWatermark` comments with no dedup/CAS guard
(`src/FS.GG.Coord.GitHub/Writes.fs:933-934` is a blind `postComment`). Before .github#2361, the
workflow's `concurrency.group` was `coord-board-reconcile-${{ github.repository }}` — one shared
group, so `cancel-in-progress: false` genuinely serialised every run that could ever write, which is
the reasoning `--worker coord-board-reconcile` (a FIXED id) rests on twice in that file: "there is
no fan-out for a shared id to double-claim across."

.github#2361 needed to narrow that group, because the same one-group-fits-all design is what let a
burst of unrelated `issue_comment` marker traffic evict a PR's own pending run (measured: 22/37
issue_comment-triggered runs cancelled; PR #2383's required `reconcile` context went missing, not
red). But narrowing the group PER TRIGGERING ISSUE/PR — the first cut of that fix — would have let
genuinely concurrent board-writing runs happen (a PR's push, an unrelated `issues` edit, and the
hourly `schedule` all in different groups at once), trading the measured defect for an unmeasured
one: exactly the "no fan-out" property this file's comments assert being silently removed.

THE SHAPE THAT IS SAFE: the workflow's `reconcile` job already carries an `if:` that skips it
entirely — no checkout, no board read, no write, a near-instant `skipped` job conclusion — whenever
`github.event_name == 'issue_comment'` AND the comment's body is a machine marker
(`startsWith(github.event.comment.body, '<!-- fsgg:')`). Every run that does NOT match that
predicate is a run that CAN reach the write step, and every one of those must keep sharing exactly
one concurrency group (this script calls that group `main`, the fixed literal the real workflow
resolves to — see below) so `cancel-in-progress: false` still serialises all of them. Only a run
GUARANTEED to be a no-op — the predicate's negation — is safe to isolate into its own,
per-issue-numbered group, because it will never reach a write regardless of what else is running.

WHAT THIS SCRIPT CHECKS, entirely offline (no GitHub API, no live scheduler — this can't be
triggered from a worker session or a CI runner either, so the check is a pure string exercise
mirroring exactly what GitHub's own `${{ }}` template substitution does):

  1. The `reconcile` job's `if:` predicate is read from the live file, not hard-coded, so a
     legitimate future change to it (e.g. a different marker prefix) is honoured automatically —
     but its two defining fragments (the event-name comparison and the `startsWith` marker check)
     must appear VERBATIM inside `concurrency.group`. A group that merely LOOKS similar but drifted
     from the job's real predicate is exactly the failure mode this guards: two independently
     editable YAML fields asserting the same fact, with nothing forcing them to agree.

  2. Resolving the group template against a representative event set (mirroring the six real
     trigger types, PLUS the marker/non-marker split within `issue_comment` that this fix's whole
     mechanism depends on) must produce:
       - the SAME group string for every event outside the skip predicate — `pull_request`,
         `pull_request_review`, `issues`, `schedule`, `workflow_dispatch`, and a GENUINE (non-marker)
         `issue_comment`, regardless of which issue/PR it targets. This is the regression check for
         .github#2361 round 2: a group keyed per issue/PR for every event, or an equivalent
         reintroduction of write-time fan-out, fails here.
       - a DISTINCT group, from `main` and from each other, for marker-matching `issue_comment`
         events on different issues. This is the regression check for the ORIGINAL .github#2361: a
         group reverted to the flat `coord-board-reconcile-${{ github.repository }}` (no isolation
         at all) fails here, because every marker event would then share `main` too.

Usage: check-reconcile-concurrency-scope.py [--root <dir>]
Exit: 0 = the group is fan-out-safe; 1 = a finding (predicate drift, missing isolation, or
unsafe fan-out); 3 = no verdict, PERMANENT — the workflow/job/expressions could not be read.

THIS GATE IS STATIC, so exit 2 is deliberately absent, matching check-workflow-timeouts.py's own
reasoning: it reads one committed file and nothing else, so there is no transport failure it could
ever report, and a retryable verdict it can never mean would be a lie about the exit contract.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    print("check-reconcile-concurrency-scope: PyYAML is required (pip install pyyaml)", file=sys.stderr)
    sys.exit(3)

WORKFLOW_REL = ".github/workflows/coord-board-reconcile.yml"
JOB_ID = "reconcile"

EXPR_RE = re.compile(r"\$\{\{\s*(.*?)\s*\}\}", re.DOTALL)


def resolve_template(template: str, ctx: dict) -> str:
    """Mirror GitHub's own `${{ }}` substitution: evaluate each block, concatenate with the literal
    text around it. Every unresolved context reference is a hard error (exit 3, not a silent guess) —
    an expression this evaluator does not understand is a file this gate cannot vouch for."""

    def repl(m: re.Match) -> str:
        return str(eval_expr(m.group(1), ctx))

    return EXPR_RE.sub(repl, template)


def eval_expr(expr: str, ctx: dict) -> object:
    """Translate the small subset of GitHub Actions expression syntax this file actually uses into
    Python and evaluate it against a restricted namespace. Deliberately narrow: this is not a general
    GH-expressions interpreter, only enough to resolve the exact shapes `coord-board-reconcile.yml`
    contains today. An expression outside that subset raises, which surfaces as exit 3 — no verdict,
    not a silent wrong answer."""
    # Mask every single-quoted literal FIRST — a marker literal like '<!-- fsgg:' contains a bare
    # `!` that the operator-translation pass below must never touch. Restoring it only after every
    # `!`/`&&`/`||` substitution is what makes this safe, unlike a blind global replace.
    literals: list[str] = []

    def mask(m: re.Match) -> str:
        literals.append(m.group(0))
        return f"\x01{len(literals) - 1}\x01"

    py = re.sub(r"'[^']*'", mask, expr)

    # Protect `!=` before turning every other `!` into `not `.
    py = py.replace("!=", "\x00NE\x00")
    py = py.replace("!", " not ")
    py = py.replace("\x00NE\x00", "!=")
    py = py.replace("&&", " and ")
    py = py.replace("||", " or ")
    # startsWith(a, b) -> (a).startswith(b) — GitHub Actions' own function, one call depth is all
    # this file ever nests.
    py = re.sub(
        r"startsWith\(\s*([^,()]+?)\s*,\s*(\x01\d+\x01)\s*\)",
        r"(\1).startswith(\2)",
        py,
    )

    # Restore the masked literals now that operator translation is done.
    for i, lit in enumerate(literals):
        py = py.replace(f"\x01{i}\x01", lit)

    py = py.replace("github.event_name", "event_name")
    py = py.replace("github.event.comment.body", "comment_body")
    py = py.replace("github.event.issue.number", "issue_number")
    py = py.replace("github.event.pull_request.number", "pr_number")
    py = py.replace("github.repository", "repository")

    names = {
        "event_name": ctx.get("event_name"),
        "comment_body": ctx.get("comment_body"),
        "issue_number": ctx.get("issue_number"),
        "pr_number": ctx.get("pr_number"),
        "repository": ctx.get("repository"),
    }
    return eval(py, {"__builtins__": {}}, names)  # noqa: S307 — fixed, narrow, offline expression subset


# Representative events. `skip` mirrors what the job's `if:` will do — True means the job is
# guaranteed to conclude `skipped` without checking out or writing anything.
EVENTS = [
    ("pull_request_push",       dict(event_name="pull_request",       comment_body=None, issue_number=None, pr_number=2372), False),
    ("pull_request_review",     dict(event_name="pull_request_review", comment_body=None, issue_number=None, pr_number=2372), False),
    ("issues_edited",           dict(event_name="issues",             comment_body=None, issue_number=500,  pr_number=None), False),
    ("hourly_schedule",         dict(event_name="schedule",           comment_body=None, issue_number=None, pr_number=None), False),
    ("workflow_dispatch",       dict(event_name="workflow_dispatch",  comment_body=None, issue_number=None, pr_number=None), False),
    ("genuine_comment_on_pr",   dict(event_name="issue_comment",      comment_body="LGTM, ship it", issue_number=2372, pr_number=None), False),
    ("genuine_comment_on_issue", dict(event_name="issue_comment",     comment_body="please retriage this", issue_number=999, pr_number=None), False),
    ("marker_comment_2361",     dict(event_name="issue_comment",      comment_body="<!-- fsgg:claim worker=osprey-fb25 -->", issue_number=2361, pr_number=None), True),
    ("marker_comment_2375",     dict(event_name="issue_comment",      comment_body="<!-- fsgg:delivery-route/v1 -->\n{}", issue_number=2375, pr_number=None), True),
]

REPOSITORY = "FS-GG/.github"


def fail(msg: str, code: int) -> "int":
    print(f"check-reconcile-concurrency-scope: {msg}", file=sys.stderr)
    return code


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    args = parser.parse_args()

    path = Path(args.root) / WORKFLOW_REL
    if not path.is_file():
        return fail(f"NO VERDICT (permanent) — {path} does not exist", 3)

    try:
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as e:
        return fail(f"NO VERDICT (permanent) — {path} would not parse: {e}", 3)

    if not isinstance(doc, dict):
        return fail(f"NO VERDICT (permanent) — {path} did not parse to a mapping", 3)

    jobs = doc.get("jobs") or {}
    job = jobs.get(JOB_ID)
    if not isinstance(job, dict):
        return fail(f"NO VERDICT (permanent) — no `{JOB_ID}` job in {path}", 3)

    job_if = job.get("if")
    if not isinstance(job_if, str) or not job_if.strip():
        return fail(f"NO VERDICT (permanent) — `{JOB_ID}` job declares no `if:` to mirror", 3)

    concurrency = doc.get("concurrency")
    if not isinstance(concurrency, dict) or not isinstance(concurrency.get("group"), str):
        return fail("NO VERDICT (permanent) — no `concurrency.group` string in the workflow", 3)

    group_template = concurrency["group"]

    # --- Check 1: the group's skip predicate must VERBATIM contain the job's own two defining
    # fragments, extracted from the live `if:` rather than hard-coded, so a legitimate future change
    # to the marker prefix or the compared event name is honoured on both sides together.
    m = re.search(
        r"github\.event_name\s*!=\s*('[^']*')",
        job_if,
    )
    n = re.search(
        r"startsWith\(\s*github\.event\.comment\.body\s*,\s*('[^']*')\s*\)",
        job_if,
    )
    if not m or not n:
        return fail(
            f"NO VERDICT (permanent) — `{JOB_ID}`'s `if:` no longer has the shape "
            "(`github.event_name != '<name>'`, `startsWith(github.event.comment.body, '<prefix>')`) "
            "this gate knows how to mirror-check; update this script alongside that change",
            3,
        )
    event_literal, marker_literal = m.group(1), n.group(1)

    findings = []
    if f"github.event_name != {event_literal}" not in group_template:
        findings.append(
            f"the group template does not contain `github.event_name != {event_literal}` verbatim — "
            "it no longer mirrors the job's own skip predicate"
        )
    if f"startsWith(github.event.comment.body, {marker_literal})" not in group_template:
        findings.append(
            f"the group template does not contain `startsWith(github.event.comment.body, {marker_literal})` "
            "verbatim — it no longer mirrors the job's own skip predicate"
        )

    # --- Check 2: resolve the group template against the representative event set and assert the
    # two-sided invariant.
    resolved = {}
    for name, event_ctx, skip in EVENTS:
        ctx = dict(event_ctx)
        ctx["repository"] = REPOSITORY
        try:
            resolved[name] = (resolve_template(group_template, ctx), skip)
        except Exception as e:  # noqa: BLE001 — an expression this evaluator can't resolve is a no-verdict, not a crash
            return fail(
                f"NO VERDICT (permanent) — could not resolve `concurrency.group` for the "
                f"'{name}' scenario: {e!r}. The template uses an expression shape newer than this "
                "gate's narrow evaluator; update it alongside the workflow change.",
                3,
            )

    non_skip_groups = {g for g, skip in resolved.values() if not skip}
    if len(non_skip_groups) != 1:
        findings.append(
            "events outside the skip predicate resolve to more than one group — real board-writing "
            f"runs could now run concurrently: {[(n, g) for n, (g, s) in resolved.items() if not s]}"
        )

    skip_groups = [g for g, skip in resolved.values() if skip]
    main_group = next(iter(non_skip_groups), None)
    if main_group is not None:
        colliding_with_main = [g for g in skip_groups if g == main_group]
        if colliding_with_main:
            findings.append(
                "a skip-guaranteed (marker) issue_comment event resolves to the SAME group as "
                f"non-skip events ({main_group!r}) — it can still evict a real board-writing run's "
                "pending slot, reproducing the original .github#2361 defect"
            )
    if len(set(skip_groups)) != len(skip_groups):
        dupes = sorted({g for g in skip_groups if skip_groups.count(g) > 1})
        findings.append(
            f"two DIFFERENT skip-guaranteed issue_comment events collapse onto the same group {dupes!r} — "
            "unrelated marker traffic can still contend for one pending slot"
        )

    if findings:
        for f in findings:
            print(f"check-reconcile-concurrency-scope: FINDING — {f}", file=sys.stderr)
        return 1

    print("check-reconcile-concurrency-scope: OK — non-skip events share one group; every "
          "skip-guaranteed marker event is isolated from it and from each other.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
