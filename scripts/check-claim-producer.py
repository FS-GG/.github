#!/usr/bin/env python3
"""Assert every `claim-generation` check run on a head was produced by the one workflow policy names.

.github#4730 / #5004 (the move of `claim-generation` out of `coherence.yml`). Run it on a PR head BEFORE
merging it for as long as there may be open PRs whose heads were checked before the move:
`tools/v2-ci-ordinary-observe.py` authenticates EVERY `claim-generation` check run on the merged head
against `policy/v2-ci-ordinary-settlement.json`'s `qualification.checkProducers.claim-generation`
(workflow id + path + event) and refuses the settlement if any one came from elsewhere. A head that was
last checked by the old `coherence.yml` job, or that carries one old and one new run, is refused there
AFTER the merge; this refuses it BEFORE.

WHAT IT READS. `gh api repos/<repo>/commits/<head>/check-runs?check_name=claim-generation` (paginated),
then for each check run the Actions run its `details_url` names. It compares `workflow_id`, `path`,
`event` and `head_sha` with the policy file in the working tree, which is the single source for the
expected values (nothing is hard-coded here, so a later move edits one file).

EXIT: 0 every check run is from the policy's workflow, and there is at least one
      1 at least one is from another workflow, has no Actions run behind it (for example one
        published by the freshness sweep through the checks API), or none exists
      2 the API could not be read or answered something that is not the expected JSON (retryable; never a
        pass)
      3 no verdict, permanent (bad arguments, unreadable policy)
OBSERVER PARITY. Per check run it also requires what `tools/v2-ci-ordinary-observe.py` requires before it
accepts one (its `producer`/`select`, lines ~385-406): the check run's own `id` equals the job id in its
`details_url` (a check that merely reuses a real job's URL is refused), and its `app.id` equals the
policy's `qualification.requiredCheckAppId` (15368, GitHub Actions). It does not re-implement the
observer's remaining checks (job read-back, attempt bounds, suite id); a pass here is necessary, not
sufficient, for a clean settlement.

RUN IT FROM CURRENT `main`. The expected producer is read from the working tree's policy file, so a stale
checkout compares against a stale producer: after the cutover lands that is the OLD workflow, and it would
pass heads the settlement will refuse. `git fetch origin main && git checkout origin/main` first.

Unreadable input is never a verdict: a non-zero `gh`, a timeout, HTML or any non-JSON body, or JSON of
the wrong shape is exit 2.

It does not look at conclusions: a cancelled earlier run from the right workflow is fine (the observer
ranks by start time), a run from the wrong workflow is not, whatever it concluded.

Usage: check-claim-producer.py --repo FS-GG/.github --head <40-hex sha> [--root <dir>]
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

CONTEXT = "claim-generation"
POLICY = "policy/v2-ci-ordinary-settlement.json"
DETAILS = re.compile(r"^https://github\.com/(?P<repo>[^/]+/[^/]+)/actions/runs/(?P<run>[1-9][0-9]*)/job/(?P<job>[1-9][0-9]*)$")


class NoVerdict(Exception):
    pass


class Unreadable(Exception):
    pass


def gh(*args: str) -> str:
    try:
        proc = subprocess.run(["gh", "api", *args], capture_output=True, text=True, check=False, timeout=60)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise Unreadable(f"gh api {' '.join(args)}: {exc}") from None
    if proc.returncode != 0:
        raise Unreadable(f"gh api {' '.join(args)}: {' '.join(proc.stderr.split()) or proc.returncode}")
    return proc.stdout


def expected(root: Path) -> dict:
    try:
        policy = json.loads((root / POLICY).read_text(encoding="utf-8"))
        row = policy["qualification"]["checkProducers"][CONTEXT]
        want = {"workflowId": int(row["workflowId"]), "path": str(row["path"]), "event": str(row["event"]),
                "appId": int(policy["qualification"]["requiredCheckAppId"])}
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise NoVerdict(f"cannot read the expected producer from {POLICY}: {exc}") from None
    return want


def parse(text: str, what: str) -> dict:
    """One JSON object, or Unreadable: an HTML error page or a truncated body is not an answer."""
    try:
        value = json.loads(text)
    except ValueError:
        raise Unreadable(f"{what}: response is not JSON ({text.strip()[:60]!r})") from None
    if not isinstance(value, dict):
        raise Unreadable(f"{what}: response is JSON but not an object")
    return value


def audit(repo: str, head: str, want: dict) -> list[str]:
    out = gh("--paginate", f"repos/{repo}/commits/{head}/check-runs?check_name={CONTEXT}&per_page=100",
             "--jq", ".check_runs[] | {id,status,conclusion,details_url,name,app_id:.app.id}")
    checks = [parse(line, "check-runs") for line in out.splitlines() if line.strip()]
    if not checks:
        return [f"head {head[:8]} has no `{CONTEXT}` check run at all"]
    findings: list[str] = []
    runs: dict[int, dict] = {}
    for check in checks:
        label = f"check run {check.get('id')} ({check.get('status')}/{check.get('conclusion')})"
        match = DETAILS.match(str(check.get("details_url") or ""))
        if not match or match.group("repo") != repo:
            findings.append(f"{label}: no Actions job behind it (details_url={check.get('details_url')!r})")
            continue
        run_id = int(match.group("run"))
        job_id = int(match.group("job"))
        if run_id not in runs:
            runs[run_id] = parse(gh(f"repos/{repo}/actions/runs/{run_id}", "--jq",
                                    "{id,workflow_id,path,event,head_sha}"), f"run {run_id}")
        run = runs[run_id]
        bad = []
        if check.get("id") != job_id:
            bad.append(f"check id {check.get('id')!r} is not the job id {job_id} its details_url names")
        if check.get("app_id") != want["appId"]:
            bad.append(f"app id {check.get('app_id')!r} (want {want['appId']!r})")
        bad += [f"{k}={run.get(src)!r} (want {want[k]!r})" for k, src in
               (("workflowId", "workflow_id"), ("path", "path"), ("event", "event")) if run.get(src) != want[k]]
        if run.get("head_sha") != head:
            bad.append(f"head_sha={run.get('head_sha')!r} (want {head!r})")
        if bad:
            findings.append(f"{label} from run {run_id}: " + "; ".join(bad))
    return findings


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", required=True)
    ap.add_argument("--head", required=True)
    ap.add_argument("--root", default=".")
    args = ap.parse_args(argv)
    try:
        if not re.fullmatch(r"[0-9a-f]{40}", args.head) or not re.fullmatch(r"[\w.-]+/[\w.-]+", args.repo):
            raise NoVerdict("--head must be a full 40-hex sha and --repo owner/name")
        want = expected(Path(args.root))
        findings = audit(args.repo, args.head, want)
    except NoVerdict as exc:
        print(f"check-claim-producer: no verdict: {exc}", file=sys.stderr)
        return 3
    except Unreadable as exc:
        print(f"check-claim-producer: could not read the API (retry; not a pass): {exc}", file=sys.stderr)
        return 2
    for line in findings:
        print(f"FINDING {line}")
    if findings:
        return 1
    print(f"ok: every `{CONTEXT}` check run on {args.head[:8]} is from workflow {want['workflowId']} ({want['path']})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
