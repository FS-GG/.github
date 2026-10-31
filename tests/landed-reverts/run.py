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
import re
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


def commit(repo: Path, message: str, when: str | None = None) -> str:
    git(repo, "add", "-A")
    env = dict(ENV, GIT_COMMITTER_DATE=when, GIT_AUTHOR_DATE=when) if when else ENV
    done = subprocess.run(["git", "-C", str(repo), "commit", "-q", "--allow-empty", "-m", message],
                          capture_output=True, text=True, env=env)
    if done.returncode != 0:
        raise RuntimeError(done.stderr)
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
    put(repo, "r.txt", lines("old", 20))
    put(repo, "del.txt", lines("retired", 60))
    put(repo, "registry/landed-revert-allowlist.json", '{"entries": []}\n')
    c0 = commit(repo, "initial", "2026-01-01T00:00:00+0000")
    (repo / "del.txt").unlink()
    put(repo, "r.txt", lines("new", 50))
    put(repo, "f.txt", lines("base", 40) + lines("feature", 45))
    put(repo, "h.txt", lines("module", 45))
    put(repo, "bin.dat", b"\0\2" * 100)
    c1 = commit(repo, "land the feature (#12)", "2026-01-01T01:00:00+0000")
    put(repo, "k.txt", lines("unrelated", 5))
    c1b = commit(repo, "unrelated (#13)", "2026-03-01T00:00:00+0000")
    put(repo, "f.txt", "base 0 = changed_by_main()\n" + "".join(lines("base", 40).splitlines(True)[1:]) + lines("feature", 45))
    c2 = commit(repo, "later change to line one (#14)", "2026-03-02T00:00:00+0000")
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
    rc0, _, out0 = gate(repo, main, old, "--lookback", "0", "--lookback-days", "0")
    rc1, doc1, out1 = gate(repo, main, old)
    check(rc0 == 0 and rc1 == 1,
          "content landed BEFORE the branch point is invisible with no window and found by the default window", out0 + out1)

    # ---- F1: edit credit is per line, never per hunk ---------------------------------------------------
    branch(repo, "coedit", c["c2"])
    put(repo, "r.txt", lines("old", 20) + "// PR note: census v2\n")
    head = commit(repo, "stale restore of r.txt plus one co-edit in the same hunk")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and doc["undeclaredLines"] == 49,
          "F1 a stale restore of 50 landed lines plus ONE new line in the same hunk -> REFUSED (49 counted, one credited)", out)
    branch(repo, "placeholder", c["c2"])
    put(repo, "r.txt", "# removed\n")
    head = commit(repo, "delete the landed block, leave a comment")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and doc["undeclaredLines"] == 49,
          "F1 deleting 50 landed lines and leaving a `# removed` comment -> REFUSED (49 counted)", out)
    branch(repo, "rewrite50", c["c2"])
    put(repo, "r.txt", lines("brandnew", 50))
    head = commit(repo, "rewrite the landed block with new text")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "F1 control: 50 landed lines replaced by 50 new ones -> passes", out)
    branch(repo, "compress", c["c2"])
    put(repo, "r.txt", lines("brandnew", 20))
    head = commit(repo, "consolidate the landed block")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and doc["undeclaredLines"] == 30,
          "F1 consolidating 50 landed lines into 20 new ones counts the 30 that vanished (the documented price)", out)

    # ---- F2: the allowlist is the BASE's, and a glob does not cross `/` --------------------------------
    sneak = stale(repo, "sneak", c["c1b"], c["c0"], files=("f.txt", "h.txt"))
    put(repo, "registry/landed-revert-allowlist.json", json.dumps({"entries": [{"glob": "**/*.txt", "reason": "x"}, {"glob": "*.txt", "reason": "x"}]}))
    head = commit(repo, "also add an allowlist that exempts everything")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and doc["allowlisted"] == [], "F2 a PR that adds its own allowlist entries does not exempt itself", out)
    branch(repo, "main2", c["c2"])
    put(repo, "registry/landed-revert-allowlist.json", json.dumps({"entries": [{"glob": "*.txt", "reason": "regenerated"}]}))
    main2 = commit(repo, "base allowlist")
    head = stale(repo, "under-main2", c["c1b"], c["c0"], files=("f.txt", "h.txt"))
    rc, doc, out = gate(repo, main2, head)
    check(rc == 0 and doc and sorted(doc["allowlisted"]) == ["f.txt", "h.txt"], "F2 an entry on the base allowlist exempts the path it names", out)
    branch(repo, "main3", c["c2"])
    put(repo, "registry/landed-revert-allowlist.json", json.dumps({"entries": [{"glob": "*", "reason": "everything"}]}))
    main3 = commit(repo, "a base allowlist that names no file")
    rc, doc, out = gate(repo, main3, head)
    check(rc == 3, "F2 an allowlist glob made only of wildcards is refused as an override, not read as an allowlist", out)
    mod = load_gate()
    check(mod.glob_match("*.txt", "f.txt") and not mod.glob_match("*.txt", "sub/f.txt") and mod.glob_match("**/*.txt", "a/b/f.txt")
          and mod.glob_match("src/*", "src/a") and not mod.glob_match("src/*", "src/a/b") and mod.glob_match("a/**", "a/b/c"),
          "F2 `*` stays inside one path segment; only a whole `**` segment spans directories")

    # ---- F3: declarations may be scoped, and say what they covered --------------------------------------
    scoped = stale(repo, "scoped", c["c1b"], c["c0"], files=("f.txt", "h.txt"))
    body = work / "scope.md"
    body.write_text("<!-- fsgg:declared-revert/v1 reverts=#12:f.txt -->\n")
    rc, doc, out = gate(repo, main, scoped, "--body-file", str(body))
    got = {d["token"]: d["covered"] for d in doc["declarations"]} if doc else {}
    check(rc == 1 and got.get("#12:f.txt", 0) > 0 and doc["undeclaredLines"] > 0,
          "F3 `#12:f.txt` covers f.txt only: h.txt is still undeclared -> REFUSED", out)
    body.write_text("<!-- fsgg:declared-revert/v1 reverts=#12:f.txt,#12:h.txt -->\n")
    rc, doc, out = gate(repo, main, scoped, "--body-file", str(body))
    got = {d["token"]: d["covered"] for d in doc["declarations"]} if doc else {}
    check(rc == 0 and len(got) == 2 and all(v > 0 for v in got.values()) and "covers" in out,
          "F3 two scoped declarations together cover both files, and each reports its own line count", out)
    body.write_text("<!-- fsgg:declared-revert/v1 reverts=#12:g.txt -->\n")
    rc, doc, out = gate(repo, main, scoped, "--body-file", str(body))
    check(rc == 1 and "covers nothing" in out, "F3 a declaration whose scope matches nothing is reported as covering nothing", out)

    # ---- F4: caps are no-verdicts, the window reaches back in time, deleted files do not come back ------
    rc, _, out = gate(repo, main, git(repo, "rev-parse", "edit"), "--max-history-versions", "1")
    check(rc == 3 and "history-versions" in out, "F4 a file with more versions than the history cap -> NO VERDICT (3), not a silent truncation", out)
    rc, _, out = gate(repo, main, git(repo, "rev-parse", "edit"), "--max-history-versions", "100000")
    check(rc == 3, "F4 a cap override above ten times the default is itself refused", out)
    rc, doc, out = gate(repo, main, git(repo, "rev-parse", "edit"))
    check(rc == 0, "F4 the same PR passes under the default caps", out)

    branch(repo, "resurrect", c["c2"])
    put(repo, "del.txt", lines("retired", 60))
    head = commit(repo, "bring del.txt back")
    rc, doc, out = gate(repo, main, head)
    check(rc == 1 and doc and any(f["kind"] == "resurrected" for f in doc["findings"]) and doc["undeclaredLines"] == 60,
          "F4 a file the window deleted, restored byte for byte by a stale tree -> REFUSED (resurrected, 60)", out)
    rc, doc, out = gate(repo, main, head, "--body-file", str(_body(work, "<!-- fsgg:declared-revert/v1 reverts=#12 -->")))
    check(rc == 0, "F4 ... and the deleting commit, named, declares it", out)
    branch(repo, "reborn", c["c2"])
    put(repo, "del.txt", lines("fresh", 60))
    head = commit(repo, "a new del.txt with new content")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 0, "F4 control: re-creating the deleted path with new content -> passes", out)

    # ---- cross-hunk credit, scoped marker round trip ---------------------------------------------------
    branch(repo, "crosshunk", c["c2"])
    rows = lines("new", 50).splitlines(True)
    put(repo, "r.txt", lines("hdr", 5) + "".join(rows[:20]))
    head = commit(repo, "add five new lines at the top, drop thirty landed lines at the bottom")
    rc, doc, out = gate(repo, main, head)
    check(rc == 0 and doc and doc["undeclaredLines"] == 25,
          "cross-hunk: 5 new lines in one hunk offset 5 of the 30 dropped in another hunk of the same file -> 25, passes", out)

    marker_re = re.compile(r"<!-- fsgg:declared-revert/v1 reverts=(\S+) -->")
    rc, doc, out = gate(repo, main, scoped)
    printed = marker_re.search(out)
    check(rc == 1 and printed is not None and ":f.txt" in printed.group(1) and ":h.txt" in printed.group(1),
          "the refusal prints the SCOPED marker, one <sha>:<path> token per file", out)
    if printed:
        rc, doc, out = gate(repo, main, scoped, "--body-file", str(_body(work, printed.group(0))))
        check(rc == 0 and doc and doc["undeclaredLines"] == 0, "the printed marker, pasted into the PR body, clears the refusal", out)

    windowed(work)
    mirrors(work)
    pinned(work)

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


def _body(work: Path, content: str) -> Path:
    path = work / "decl.md"
    path.write_text(content + "\n")
    return path


def load_gate():
    import importlib.util
    spec = importlib.util.spec_from_file_location("check_landed_reverts", GATE)
    module = importlib.util.module_from_spec(spec)
    sys.modules["check_landed_reverts"] = module
    spec.loader.exec_module(module)
    return module


def windowed(work: Path) -> None:
    """A stale tree more than 100 commits old but inside the day window: found by the window, missed without it."""
    repo = work / "windowed"
    repo.mkdir()
    git(repo, "init", "-q", "-b", "main")
    put(repo, "f.txt", lines("base", 40))
    commit(repo, "initial", "2026-01-01T00:00:00+0000")
    put(repo, "f.txt", lines("base", 40) + lines("feature", 45))
    commit(repo, "land the feature (#12)", "2026-01-01T01:00:00+0000")
    for i in range(120):
        put(repo, f"fill/{i}.txt", f"{i}\n")
        commit(repo, f"filler {i} (#{100 + i})", f"2026-01-01T02:{i % 60:02d}:00+0000")
    tip = git(repo, "rev-parse", "HEAD")
    git(repo, "checkout", "-q", "-B", "old-stale", tip)
    put(repo, "f.txt", lines("base", 40))
    head = commit(repo, "a stale tree 120 commits behind its parent", "2026-01-02T00:00:00+0000")
    rc, doc, out = gate(repo, tip, head)
    check(rc == 1 and doc and doc["undeclaredLines"] == 45,
          "F4 content landed 121 commits before the branch point but inside the day window -> REFUSED", out)
    rc, doc, out = gate(repo, tip, head, "--lookback-days", "0")
    check(rc == 0, "F4 ... and the same PR is not seen by the commit-count window alone", out)


def mirrors(work: Path) -> None:
    """The skill roots hold byte-identical copies: one edit drops the same lines in each, counted once."""
    roots = (".claude/skills/a/SKILL.md", ".agents/skills/a/SKILL.md", ".codex/skills/a/SKILL.md")
    for label, distinct in (("identical", False), ("different", True)):
        repo = work / f"mirrors-{label}"
        repo.mkdir()
        git(repo, "init", "-q", "-b", "main")
        for r in roots:
            put(repo, r, lines("intro", 5))
        commit(repo, "initial")
        for i, r in enumerate(roots):
            put(repo, r, lines("intro", 5) + lines(f"rule{i}" if distinct else "rule", 25))
        landed = commit(repo, "land a skill (#7)")
        put(repo, "x.txt", "x\n")
        main = commit(repo, "later (#8)")
        branch(repo, "drop", main)
        for r in roots:
            put(repo, r, lines("intro", 5))
        head = commit(repo, "remove the skill text from every root")
        rc, doc, out = gate(repo, main, head)
        if not distinct:
            check(rc == 0 and doc and doc["undeclaredLines"] == 25 and len(doc["mirrors"]) == 1 and len(doc["mirrors"][0]) == 3,
                  "mirrors: the same 25 lines dropped from three identical skill roots count once (25, not 75) and are named", out)
        else:
            check(rc == 1 and doc and doc["undeclaredLines"] == 75 and doc["mirrors"] == [],
                  "mirrors: three copies with DIFFERENT content are counted separately (75) -> REFUSED", out)


def workflow_step() -> str:
    text = (ROOT / ".github" / "workflows" / "landed-revert-guard.yml").read_text()
    m = re.search(r"^        run: \|\n((?:          .*\n|\n)+)", text, re.M)
    if not m:
        return ""
    return "".join(line[10:] if line.strip() else line for line in m.group(1).splitlines(True))


def pinned(work: Path) -> None:
    """Run the workflow's own step against a head whose copy of the script always passes."""
    wf = (ROOT / ".github" / "workflows" / "landed-revert-guard.yml").read_text()
    types = re.search(r"pull_request:\s*\n\s*types: \[([^\]]*)\]", wf)
    check(types is not None and "edited" in types.group(1),
          "the trigger includes `edited`, so a body declaration re-runs the job")
    check("github.event.pull_request.body" in wf and '--body-file "$work/body.md"' in wf,
          "the workflow passes the live event body to the script as --body-file")
    step = workflow_step()
    check(bool(step), "the workflow's run step can be extracted")
    if not step:
        return
    sub = work / "pin"
    sub.mkdir()
    repo, c = build(sub)
    (repo / "scripts" / "lib").mkdir(parents=True)
    put(repo, "scripts/check-landed-reverts.py", GATE.read_text())
    put(repo, "scripts/lib/gate.py", (ROOT / "scripts" / "lib" / "gate.py").read_text())
    c3 = commit(repo, "the check (#15)", "2026-03-03T00:00:00+0000")
    git(repo, "update-ref", "refs/remotes/origin/main", c3)
    stale(repo, "pin-head", c3, c["c0"], files=("f.txt", "h.txt"))
    put(repo, "scripts/check-landed-reverts.py", "import sys\nsys.exit(0)\n")
    head = commit(repo, "and a script that always passes")

    def run_step(body: str) -> tuple[int, str]:
        summary = sub / "summary.md"
        summary.write_text("")
        env = dict(ENV, BASE_REF="main", HEAD_SHA=head, PR_BODY=body, GITHUB_STEP_SUMMARY=str(summary))
        done = subprocess.run(["bash", "-c", step], cwd=repo, capture_output=True, text=True, env=env)
        return done.returncode, done.stdout + done.stderr + summary.read_text()

    rc, out = run_step("")
    check(rc == 1 and "no-silent-revert (exit 1)" in out,
          "BASE-READ PIN: a head whose script always passes is still judged by the base's script -> REFUSED", out)
    rc, out = run_step("<!-- fsgg:declared-revert/v1 reverts=#12,#14,#15 -->")
    check(rc == 0, "the body the workflow was given is read: a declaration in it clears the refusal", out)


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
