#!/usr/bin/env python3
"""Fixture for scripts/check-landed-reverts.py (.github#4730): the guard against a squash that silently
reverts work which landed on the base moments earlier.

Every leg asserts the EXIT CODE (the gate's contract) and, where it matters, a count it reports. The
failure legs are the point: a guard that cannot say NO is the #266 defect it exists to close.

Two halves:
  * synthetic repositories built here, offline, so every class of change has a known answer -- the
    stale-tree revert, a normal PR, an EDIT of recently landed lines, a pure deletion, a move, a rename,
    a binary, a declared revert, the fail-closed cases;
  * the REAL incident, replayed from objects that are on main: #4977's squash commit 99c83d57 against its
    parent 3c44f893, and a few real PRs that must pass. These need full history and FAIL (never skip)
    without it, because a skipped replay would read as a passed one.

Set LANDED_REVERTS_NO_REAL=1 only for the mutation sweep's inner loop; the shipped workflow never does.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
GATE = ROOT / "scripts" / "check-landed-reverts.py"

passed = failed = 0


def check(ok: bool, label: str, detail: str = "") -> None:
    global passed, failed
    if ok:
        passed += 1
        print(f"PASS  {label}")
    else:
        failed += 1
        print(f"FAIL  {label}")
        for row in detail.strip().splitlines()[:12]:
            print(f"    | {row}")


ENV = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@example.invalid", GIT_COMMITTER_NAME="t",
           GIT_COMMITTER_EMAIL="t@example.invalid", GIT_CONFIG_GLOBAL="/dev/null", GIT_CONFIG_SYSTEM="/dev/null")


def git(repo: Path, *args: str, check_rc: bool = True) -> str:
    done = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, env=ENV)
    if check_rc and done.returncode != 0:
        raise RuntimeError(f"git {args}: {done.stderr}")
    return done.stdout.strip()


def put(repo: Path, name: str, body: str | bytes) -> None:
    path = repo / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(body if isinstance(body, bytes) else body.encode())


def commit(repo: Path, message: str) -> str:
    git(repo, "add", "-A")
    git(repo, "commit", "-q", "--allow-empty", "-m", message)
    return git(repo, "rev-parse", "HEAD")


def lines(prefix: str, count: int) -> str:
    return "".join(f"{prefix} {i} = compute_{prefix}({i})\n" for i in range(count))


def gate(repo: Path, base: str, head: str, *extra: str) -> tuple[int, dict | None, str]:
    done = subprocess.run([sys.executable, str(GATE), "--repo", str(repo), "--base", base, "--head", head,
                           "--json", *extra], capture_output=True, text=True, env=ENV)
    try:
        doc = json.loads(done.stdout)
    except ValueError:
        doc = None
    return done.returncode, doc, done.stdout + done.stderr


def build(work: Path) -> tuple[Path, dict[str, str]]:
    """main: c0 -> c1 (the landed feature, squash-style subject) -> c1b -> c2 (later, touches line 1)."""
    repo = work / "repo"
    repo.mkdir()
    git(repo, "init", "-q", "-b", "main")
    put(repo, "f.txt", lines("base", 40))
    put(repo, "g.txt", lines("other", 20))
    put(repo, "bin.dat", b"\0\1" * 100)
    c0 = commit(repo, "initial")
    put(repo, "f.txt", lines("base", 40) + lines("feature", 45))
    put(repo, "h.txt", lines("module", 45))
    put(repo, "bin.dat", b"\0\2" * 100)
    c1 = commit(repo, "land the feature (#12)")
    put(repo, "k.txt", lines("unrelated", 5))
    c1b = commit(repo, "unrelated (#13)")
    put(repo, "f.txt", "base 0 = changed_by_main()\n" + "".join(lines("base", 40).splitlines(True)[1:]) + lines("feature", 45))
    c2 = commit(repo, "later change to line one (#14)")
    return repo, {"c0": c0, "c1": c1, "c1b": c1b, "c2": c2}


def branch(repo: Path, name: str, at: str) -> None:
    git(repo, "checkout", "-q", "-B", name, at)


def stale(repo: Path, name: str, at: str, older: str, message: str = "census change", files=("f.txt", "h.txt", "bin.dat")) -> str:
    """The incident: a branch built on `at` whose commit carries the OLDER copy of files."""
    branch(repo, name, at)
    for f in files:
        blob = subprocess.run(["git", "-C", str(repo), "cat-file", "-p", f"{older}:{f}"], capture_output=True, env=ENV)
        if blob.returncode == 0:
            put(repo, f, blob.stdout)
        else:
            (repo / f).unlink(missing_ok=True)
    put(repo, "z.txt", "census 1\ncensus 2\n")
    return commit(repo, message)


def synthetic(work: Path) -> None:
    repo, c = build(work)
    main = c["c2"]

    # ---- the incident class -------------------------------------------------------------------------
    head = stale(repo, "stale", c["c1b"], c["c0"])
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and doc["refused"] and doc["undeclaredLines"] >= 90,
          "stale tree on a newer parent: the landed feature is reverted -> REFUSED", out)
    check(doc is not None and any(f["kind"] == "whole-file" and f["path"] == "h.txt" for f in doc["findings"])
          and any(f["kind"] == "binary" for f in doc["findings"]),
          "  ... the new file is a whole-file revert and the restored binary is reported", out)
    check(doc is not None and c["c1"] in {x for f in doc["findings"] for x in f["commits"]},
          "  ... each dropped line is attributed to the commit that introduced it", out)

    # ---- controls that must pass --------------------------------------------------------------------
    branch(repo, "normal", c["c0"])
    put(repo, "g.txt", lines("other", 20) + lines("added", 3))
    put(repo, "z.txt", "z\n")
    head = commit(repo, "ordinary change from an old base")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0 and doc["findings"] == [],
          "a normal PR branched before the feature landed -> passes with no finding", out)

    branch(repo, "edit", c["c1b"])
    text = lines("base", 40) + lines("feature", 45)
    rows = text.splitlines(True)
    for i in range(40, 85):
        rows[i] = f"feature {i - 40} = rewritten_by_the_pr_{i}()\n"
    put(repo, "f.txt", "base 0 = changed_by_main()\n" + "".join(rows[1:]))
    put(repo, "h.txt", lines("module_v2", 45))
    head = commit(repo, "rewrite the recently landed feature")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0,
          "EDIT: replacing 90 recently landed lines with new text -> passes", out)

    branch(repo, "delete", c["c1b"])
    rows = (lines("base", 40)).splitlines(True)
    put(repo, "f.txt", "base 0 = changed_by_main()\n" + "".join(rows[1:]))
    head = commit(repo, "drop the feature block")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and doc["undeclaredLines"] == 45,
          "REVERT: deleting 45 recently landed lines and adding nothing -> REFUSED (45 counted)", out)

    branch(repo, "move", c["c2"])
    rows = ("base 0 = changed_by_main()\n" + "".join(lines("base", 40).splitlines(True)[1:]) + lines("feature", 45)).splitlines(True)
    put(repo, "f.txt", "".join(rows[40:] + rows[:40]))
    head = commit(repo, "reorder: feature block first")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "MOVE: the same lines reordered in the file -> passes", out)

    branch(repo, "rename", c["c1b"])
    git(repo, "mv", "h.txt", "h2.txt")
    rows = lines("module", 45).splitlines(True)
    rows[3] = "module 3 = renamed()\n"
    put(repo, "h2.txt", "".join(rows))
    head = commit(repo, "rename h.txt with a small edit")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "RENAME: a landed file renamed and lightly edited -> passes", out)

    branch(repo, "five", c["c1b"])
    rows = (lines("base", 40) + lines("feature", 45)).splitlines(True)
    del rows[50:55]
    put(repo, "f.txt", "base 0 = changed_by_main()\n" + "".join(rows[1:]))
    head = commit(repo, "trim five lines")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 5 and "::warning" in out,
          "below the refusal bound: 5 dropped landed lines -> exit 0 with a warning, not silence", out)

    branch(repo, "binnew", c["c1b"])
    put(repo, "bin.dat", b"\0\3" * 100)
    head = commit(repo, "a different binary")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "BINARY: a new binary where the landed one was -> passes", out)

    branch(repo, "binrestore", c["c1b"])
    put(repo, "bin.dat", b"\0\1" * 100)
    head = commit(repo, "the old binary again")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1, "BINARY: the pre-landing binary restored -> REFUSED", out)

    # ---- declarations --------------------------------------------------------------------------------
    sha = stale(repo, "declared-sha", c["c1b"], c["c0"], f"census change\n\n<!-- fsgg:declared-revert/v1 reverts={c['c1'][:12]} -->")
    rc, doc, out = gate(repo, main, sha)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0 and doc["findings"],
          "a revert declared by commit sha in a commit message -> passes, and the findings are still listed", out)

    plain = stale(repo, "declared-pr", c["c1b"], c["c0"])
    body = work / "body.md"
    body.write_text("Reverts the feature.\n<!-- fsgg:declared-revert/v1 reverts=#12 -->\n")
    rc, doc, out = gate(repo, main, plain, "--body-file", str(body))
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "a revert declared as #PR in the PR body -> passes", out)

    text_only = stale(repo, "declared-wrong", c["c1b"], c["c0"], files=("f.txt", "h.txt"))
    body.write_text(f"<!-- fsgg:declared-revert/v1 reverts={c['c0'][:12]} -->\n")
    rc, doc, out = gate(repo, main, text_only, "--body-file", str(body))
    check(rc == 1, "a declaration naming a commit that did not introduce the lines declares nothing -> REFUSED", out)

    body.write_text("<!-- fsgg:declared-revert/v1 reverts=#999 -->\n")
    rc, doc, out = gate(repo, main, plain, "--body-file", str(body))
    check(rc == 1 and "unresolvable" in out, "an unresolvable PR number -> REFUSED and reported as unresolvable", out)

    body.write_text("reverts the feature on purpose\n")
    rc, doc, out = gate(repo, main, plain, "--body-file", str(body))
    check(rc == 1, "prose that merely says 'revert' is not a declaration -> REFUSED", out)

    # ---- allowlist -----------------------------------------------------------------------------------
    allow = work / "allow.json"
    allow.write_text(json.dumps({"entries": [{"glob": "*.txt", "reason": "regenerated"}, {"glob": "bin.dat", "reason": "regenerated"}]}))
    rc, doc, out = gate(repo, main, plain, "--allowlist", str(allow))
    check(rc == 0 and doc and sorted(doc["allowlisted"]) == ["bin.dat", "f.txt", "h.txt"],
          "an allowlisted path (with a reason) is skipped and named in the verdict", out)
    allow.write_text(json.dumps({"entries": [{"glob": "*.txt", "reason": " "}]}))
    rc, doc, out = gate(repo, main, plain, "--allowlist", str(allow))
    check(rc == 3, "an allowlist entry without a reason -> NO VERDICT (3)", out)

    # ---- the lookback parameter: why the check is about recency, not the merge base ------------------
    old = stale(repo, "old-stale", c["c1b"], c["c0"], files=("f.txt", "h.txt"))
    rc0, _, out0 = gate(repo, main, old, "--lookback", "0")
    rc1, doc1, out1 = gate(repo, main, old)
    check(rc0 == 0 and rc1 == 1,
          "content landed BEFORE the branch point is invisible at lookback 0 and found by the default window", out0 + out1)

    # ---- fail closed ---------------------------------------------------------------------------------
    rc, _, out = gate(repo, main, "0" * 40)
    check(rc == 3, "an unreadable head -> NO VERDICT (3)", out)
    branch(repo, "conflict", c["c0"])
    put(repo, "f.txt", "base 0 = changed_by_the_branch()\n" + "".join(lines("base", 40).splitlines(True)[1:]))
    head = commit(repo, "conflicts with main's change to line one")
    rc, _, out = gate(repo, main, head)
    check(rc == 3 and "conflict" in out, "a head that does not merge cleanly -> NO VERDICT (3)", out)
    shallow = work / "shallow"
    # Depth 2 keeps base (c2) and the branch point (c1b) but cuts the history where the feature landed (c1), so
    # WITHOUT the shallow refusal the stale tree reads as a clean PR: the exact fail-open this leg guards.
    git(work, "clone", "-q", "--depth", "2", "--branch", "main", f"file://{repo}", str(shallow), check_rc=True)
    git(shallow, "fetch", "-q", "--depth", "1", "origin", plain)
    rc, _, out = gate(shallow, main, plain)
    check(rc == 3 and "the clone is shallow" in out, "a shallow clone -> NO VERDICT (3), never a green over history it cannot see", out)
    rc, doc, out = gate(repo, main, c["c1"])
    check(rc == 0 and doc and doc["branchPoint"] is None, "a head already contained in the base -> nothing to judge, exit 0", out)


def real_incident() -> None:
    squash = "99c83d579fcc8826d76d1d736dcfcde030da713d"
    base = "3c44f893d5db8587ce4891fa98371f041d473fe4"
    have = subprocess.run(["git", "-C", str(ROOT), "cat-file", "-e", f"{squash}^{{commit}}"], capture_output=True).returncode == 0
    check(have, "full history is present (the real-incident legs cannot be skipped)")
    if not have:
        return
    tree = git(ROOT, "rev-parse", f"{squash}^{{tree}}")
    rc, doc, out = gate(ROOT, base, squash)
    check(rc == 1 and doc and doc["refused"], "REAL #4977: the squash that deleted #4968's work against its live base -> REFUSED", out)
    check(doc is not None and doc["resultTree"] == tree, "  ... the computed squash result is byte-identical to the merged tree", out)
    check(doc is not None and doc["undeclaredLines"] >= 2000 and len([f for f in doc["findings"] if f["undeclared"]]) >= 13,
          "  ... it reports >= 2000 dropped lines across >= 13 files", out)
    check(doc is not None and "dd7665f664ab1db862ab40f388209c19fa83b640" in {x for f in doc["findings"] for x in f["commits"]},
          "  ... and names #4968 (dd7665f6) as the work that was dropped", out)
    body = Path(tempfile.mkdtemp(prefix="lr-body-")) / "b.md"
    body.write_text("<!-- fsgg:declared-revert/v1 reverts=#4968,#4976 -->\n")
    rc, doc, out = gate(ROOT, base, squash, "--body-file", str(body))
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "REAL #4977 with the revert declared (#4968,#4976) -> passes", out)
    for name, sha in (("#4968 (a large new feature)", "dd7665f664ab1db862ab40f388209c19fa83b640"),
                      ("#4981 (edits recently landed census code)", "5217ea9e540253a2498a7d9252b586cce792ee09"),
                      ("#4975 (census reconcile)", "1fda4b46f7f18bb6dc68cb542abb9e03d6f1c113")):
        parent = git(ROOT, "rev-parse", f"{sha}^1")
        rc, doc, out = gate(ROOT, parent, sha)
        check(rc == 0, f"REAL {name} against its live base -> passes", out)


def main() -> int:
    print("landed-reverts fixture")
    root = os.environ.get("TMPDIR") or tempfile.gettempdir()
    with tempfile.TemporaryDirectory(prefix="landed-reverts-", dir=root) as work:
        synthetic(Path(work))
    if os.environ.get("LANDED_REVERTS_NO_REAL") != "1":
        real_incident()
    print(f"landed-reverts fixture — {passed + failed} assertion(s): {passed} passed, {failed} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
