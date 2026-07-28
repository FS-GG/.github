#!/usr/bin/env python3
"""The negative control for the mutation harness itself (.github#1810, spec from #1808).

**An audit of the audit fabric that cannot fail would be the eleventh entry on the list it exists to
prevent.** `scripts/lib/mutation.py` exists because 30 gates in this fleet have run ten or more times
without ever going red, and because four previous anchor checks — `#1784`, `#1582`, `#1790`, `#1794` —
each shipped with a defect in the anchor. So this file holds the harness to the standard the harness
applies: every verdict it can return is DEMONSTRATED here against a synthetic gate whose behaviour is
known by construction, and the demonstration includes the two the harness must never confuse.

The legs map one-to-one onto the four-point specification:

* **M1/M2 — point 1, the unmutated control.** `#1582`'s harness reported eleven mutants "caught" while
  none had executed a line: they died at import and the non-zero exit read as a catch. M1 proves a
  live gate grades JUSTIFIED; **M2 proves a DEAD one grades DECORATIVE** — that is the leg that proves
  this harness can return a negative verdict at all, and without it every JUSTIFIED here is unfalsified.
* **M3/M4/M5/M7 — point 2, `NOT_MEASURED` distinct end to end.** A red control, a mutant that dies
  before reaching the guard, a mutation that never applied, and an anchor that never matched are each
  their own answer. M4 is `#1582`'s exact shape: **non-zero exit, nothing executed** — it must NOT be
  JUSTIFIED. M9 then proves the three stay separable in the counters and the exit code.
* **M6/M8 — point 3, anchors independent of the guard.** M6 proves the loader REFUSES a spec whose
  anchor is produced by the file the mutation edits (`#1794`: the anchor vanishes with the guard, so
  the leg says `NOT MEASURED` exactly when it should say `FAIL`). M8 proves the run-time hash check
  catches the same thing when a mutation reaches the producer indirectly.
* **M10 — point 4, mutate the FIXTURE.** `#1794`'s M10. Everything above is this file asserting things
  about the harness; M10 asserts something about THIS FILE — that its own `expect` helper actually
  fails when handed a wrong answer. Without it, `expect` could be a no-op and all nine legs above
  would pass vacuously, which is the precise defect this whole line of work exists to find.

Run it with ``bash tests/gate-mutation/run.sh``.
"""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import tempfile
import textwrap
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(REPO / "scripts"))

from lib.mutation import (  # noqa: E402
    Anchor,
    Discriminator,
    Mutation,
    MutationKind,
    PathOp,
    SpecError,
    Verdict,
    adjudicate,
    exit_code,
    path_signature,
    load_specs,
    tally_counters,
)

PASSED = 0
FAILED = 0


def expect(name: str, got, want) -> bool:
    """The refutation helper. M10 proves it is not a no-op — do not simplify it into an ``assert``."""
    global PASSED, FAILED
    if got == want:
        print(f"PASS  {name}")
        PASSED += 1
        return True
    print(f"FAIL  {name}")
    print(f"    | want: {want!r}")
    print(f"    | got:  {got!r}")
    FAILED += 1
    return False


def expect_contains(name: str, haystack: str, needle: str) -> bool:
    global PASSED, FAILED
    if needle in haystack:
        print(f"PASS  {name}")
        PASSED += 1
        return True
    print(f"FAIL  {name}")
    print(f"    | {needle!r} not in {haystack!r}")
    FAILED += 1
    return False


# --------------------------------------------------------------------------------------------------
# The synthetic repo. `guard.sh` is the gate under test; `fixture.sh` is its negative control and the
# ANCHOR PRODUCER — a different file, which is the whole point of specification point 3. `subject.txt`
# is what the guard claims to protect.
# --------------------------------------------------------------------------------------------------

LIVE_GUARD = """\
#!/usr/bin/env bash
# A gate that really reads its subject.
grep -q 'INVARIANT-HOLDS' subject.txt || exit 1
exit 0
"""

FIXTURE = """\
#!/usr/bin/env bash
# Stands in for tests/*/run.sh: drives the guard, counts, and prints the tally the anchor matches.
# NOTHING here reads the guard's source, so a mutation of guard.sh cannot delete this tally.
pass=0; fail=0
run_case() {  # <want-rc> <subject-contents>
  printf '%s\\n' "$2" > subject.txt
  rc=0; bash guard.sh >/dev/null 2>&1 || rc=$?
  if [ "$rc" -eq "$1" ]; then pass=$((pass+1)); else fail=$((fail+1)); fi
}
run_case 0 'INVARIANT-HOLDS'
run_case 1 'INVARIANT-BROKEN'
echo "synthetic fixture — $((pass+fail)) assertion(s): $pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
exit 0
"""

ANCHOR = Anchor(pattern=r"\d+ passed, \d+ failed", produced_by="fixture.sh")


def build_repo(tmp: Path, guard: str = LIVE_GUARD, fixture: str = FIXTURE) -> Path:
    root = tmp
    (root / "guard.sh").write_text(guard, encoding="utf-8")
    (root / "fixture.sh").write_text(fixture, encoding="utf-8")
    (root / "subject.txt").write_text("INVARIANT-HOLDS\n", encoding="utf-8")
    return root


def mutation(**kw) -> Mutation:
    base = dict(
        id="synthetic",
        gate="synthetic-gate.yml",
        command=["bash", "fixture.sh"],
        target="guard.sh",
        find="grep -q 'INVARIANT-HOLDS' subject.txt || exit 1",
        replace="true",
        breaks="the guard stops reading its subject",
        anchor=ANCHOR,
        discriminator=Discriminator.TALLY,
        timeout=60,
    )
    base.update(kw)
    return Mutation(**base)


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def m1() -> None:
    # ---- M1: a LIVE gate grades JUSTIFIED (specification point 1, positive half) -------------------
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td))
        before = sha(root / "guard.sh")
        r = adjudicate(mutation(), root)
        expect("M1  a live gate that fires on a broken subject grades JUSTIFIED", r.verdict, Verdict.JUSTIFIED)
        expect_contains("M1  ...and the reason cites the fixture's own tally", r.reason, "failed assertion(s)")
        expect("M1  control ran green", r.control_rc, 0)
        expect("M1  the mutant really did fail", r.mutant_rc != 0, True)
        # RESTORE, verified — a harness that can leave a mutated tree behind is a hazard.
        expect("M1  the target is byte-identical after the run", sha(root / "guard.sh"), before)

def m2() -> None:
    # ---- M2: a DEAD gate grades DECORATIVE -------------------------------------------------------
    # THIS IS THE LEG THAT PROVES THIS HARNESS CAN RETURN A NEGATIVE VERDICT. Every JUSTIFIED above is
    # meaningless without it: a harness hard-wired to say JUSTIFIED would pass M1 and fail only here.
    with tempfile.TemporaryDirectory() as td:
        dead_fixture = FIXTURE.replace("run_case 1 'INVARIANT-BROKEN'", "run_case 0 'INVARIANT-BROKEN'")
        root = build_repo(Path(td), guard="#!/usr/bin/env bash\n# reads nothing, always green\nexit 0\n",
                          fixture=dead_fixture)
        r = adjudicate(
            mutation(
                find="# reads nothing, always green",
                replace="# reads nothing, always green (mutated)",
                breaks="the subject is corrupted while the guard ignores it",
            ),
            root,
        )
        expect("M2  a gate that cannot fire grades DECORATIVE", r.verdict, Verdict.DECORATIVE)
        expect_contains("M2  ...and the reason names what was broken", r.reason, "stayed GREEN")
        expect("M2  DECORATIVE is exit code 1 (lib.gate FINDING)", exit_code([r]), 1)

def m3() -> None:
    # ---- M3: a control that is not green grades NOT_MEASURED (point 1) ---------------------------
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td), fixture=FIXTURE.replace("run_case 0 'INVARIANT-HOLDS'",
                                                            "run_case 9 'INVARIANT-HOLDS'"))
        r = adjudicate(mutation(), root)
        expect("M3  a red UNMUTATED control grades NOT_MEASURED", r.verdict, Verdict.NOT_MEASURED)
        expect_contains("M3  ...and says the control was already failing", r.reason, "UNMUTATED control")

def m4() -> None:
    # ---- M4: #1582's exact shape — non-zero exit, nothing executed -------------------------------
    # The mutant makes the fixture die before it can run or count anything. It exits non-zero, and a
    # naive harness reads that as "caught". It must not: no tally was printed, so nothing was measured.
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td))
        r = adjudicate(
            mutation(
                target="fixture.sh",
                find="pass=0; fail=0",
                replace="echo 'boom' >&2; exit 7\npass=0; fail=0",
                breaks="the fixture dies before executing a single assertion",
                anchor=Anchor(pattern=r"\d+ passed, \d+ failed", produced_by="guard.sh"),
            ),
            root,
        )
        expect("M4  a mutant that exits non-zero having executed NOTHING is NOT_MEASURED, not JUSTIFIED",
               r.verdict, Verdict.NOT_MEASURED)
        expect("M4  ...and the non-zero exit is recorded, not believed", r.mutant_rc, 7)
        expect_contains("M4  ...and the reason names the crash/finding asymmetry (#1812)",
                        r.reason, "never reached the guard")

def m4b() -> None:
    # ---- M4b: the DISCRIMINATOR itself — a non-zero exit with a CLEAN tally is not a catch --------
    # M4 above is caught by the anchor check, one step earlier, so on its own it leaves `_fired`
    # unexercised — which was measured, not assumed: a harness mutant that replaced `_fired` with
    # `x_rc != 0` still passed M4. This leg reaches the guard, prints a tally reporting ZERO failed
    # assertions, and exits non-zero anyway. That combination is a crash dressed as a finding
    # (`#1812`), and grading it JUSTIFIED is exactly how an EXERCISED count becomes an upper bound.
    with tempfile.TemporaryDirectory() as td:
        noisy = FIXTURE.replace(
            '[ "$fail" -eq 0 ] || exit 1\nexit 0',
            'if grep -q "TRIPWIRE" guard.sh; then exit 4; fi\n[ "$fail" -eq 0 ] || exit 1\nexit 0',
        )
        root = build_repo(Path(td), fixture=noisy)
        r = adjudicate(
            mutation(
                find="#!/usr/bin/env bash",
                replace="#!/usr/bin/env bash\n# TRIPWIRE",
                breaks="the guard is edited, and the run dies AFTER reporting a clean tally",
            ),
            root,
        )
        expect("M4b a non-zero exit whose tally reports 0 failed is NOT_MEASURED, never JUSTIFIED",
               r.verdict, Verdict.NOT_MEASURED)
        expect_contains("M4b ...and the reason names the contradiction", r.reason, "contradictory")
        expect("M4b ...and the anchor DID match, so this is not the M4 path", r.mutant_anchor is not None, True)

def m4c() -> None:
    # ---- M4c: a fixture that prints SEVERAL tallies is graded on the one the anchor names ---------
    # FOUND BY RUNNING THIS HARNESS, not by reading it. `tests/skill-union/run.sh` prints four tally
    # lines; the discriminator was reading the FIRST match in the raw output while the anchor had
    # matched a different one, so a genuinely-caught mutant came back "contradictory: exited 1 but 0
    # failed". The refusal was correct and the question was wrong. The tally is now read out of the
    # ANCHOR'S match, so a spec with several tallies must name the authoritative one — and this leg
    # holds that fix in place.
    with tempfile.TemporaryDirectory() as td:
        multi = FIXTURE.replace(
            'echo "synthetic fixture — $((pass+fail)) assertion(s): $pass passed, $fail failed"',
            'echo "section A: 9 passed, 0 failed"\n'
            'echo "section B: 4 passed, 0 failed"\n'
            'echo "synthetic fixture: $pass passed, $fail failed"',
        )
        root = build_repo(Path(td), fixture=multi)
        r = adjudicate(
            mutation(anchor=Anchor(pattern=r"synthetic fixture: \d+ passed, \d+ failed",
                                   produced_by="fixture.sh")),
            root,
        )
        expect("M4c the graded tally is the one the ANCHOR matched, not the first in the output",
               r.verdict, Verdict.JUSTIFIED)
        expect("M4c ...and it is the authoritative final tally", r.mutant_anchor,
               "synthetic fixture: 1 passed, 1 failed")
        # ...and an anchor that matches something carrying NO tally cannot grade a tally leg.
        r2 = adjudicate(
            mutation(anchor=Anchor(pattern=r"section A: ", produced_by="fixture.sh")), root
        )
        expect("M4c an anchor with no tally in it grades NOT_MEASURED, never a guess",
               r2.verdict, Verdict.NOT_MEASURED)
        expect_contains("M4c ...and says the anchor must match the tally it grades",
                        r2.reason, "needs the anchor to match the tally")


def m4d() -> None:
    # ---- M4d: the TERMINAL discriminator, for fixtures that count nothing ------------------------
    # `tests/ignored-author-coherence/run.sh` and `tests/gate-harness/run.sh` print no tally at all.
    # Inventing one for them would be worse than saying so, so TERMINAL reads the exit code — but ONLY
    # behind an anchor that matches the fixture's LAST line, which proves the run finished. The two
    # halves both matter, so both are demonstrated: a completed run that exits non-zero FIRED, and a
    # run that dies BEFORE its terminal line is NOT_MEASURED however loudly it exits.
    counting_free = (
        "#!/usr/bin/env bash\n"
        "bad=0\n"
        "bash guard.sh >/dev/null 2>&1 || true\n"
        "printf 'INVARIANT-BROKEN\\n' > subject.txt\n"
        "bash guard.sh >/dev/null 2>&1 || bad=1\n"
        "if [ \"$bad\" -eq 1 ]; then echo 'synthetic fixture: OK'; exit 0; fi\n"
        "echo 'synthetic fixture: 1 FAILURE(S)'; exit 1\n"
    )
    term_anchor = Anchor(pattern=r"synthetic fixture: (OK|\d+ FAILURE\(S\))", produced_by="fixture.sh")
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td), fixture=counting_free)
        r = adjudicate(mutation(discriminator=Discriminator.TERMINAL, anchor=term_anchor), root)
        expect("M4d a tally-free fixture that reaches its terminal line and exits non-zero is JUSTIFIED",
               r.verdict, Verdict.JUSTIFIED)
        expect("M4d ...and the anchor is the terminal report", r.mutant_anchor,
               "synthetic fixture: 1 FAILURE(S)")

    # The unsafe half: a mutant that kills the fixture BEFORE its terminal line must not be read as a
    # catch just because TERMINAL trusts the exit code. The anchor is what stops that.
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td), fixture=counting_free)
        r = adjudicate(
            Mutation(
                id="terminal-crash", gate="synthetic", command=["bash", "fixture.sh"],
                target="fixture.sh", find="bad=0", replace="exit 9\nbad=0",
                breaks="the fixture dies before its terminal report",
                anchor=Anchor(pattern=r"synthetic fixture: (OK|\d+ FAILURE\(S\))", produced_by="guard.sh"),
                discriminator=Discriminator.TERMINAL, timeout=60,
            ),
            root,
        )
        expect("M4d a TERMINAL leg that never reached its terminal line is NOT_MEASURED",
               r.verdict, Verdict.NOT_MEASURED)
        expect("M4d ...and the loud exit code is recorded, not believed", r.mutant_rc, 9)


def m5() -> None:
    # ---- M5: a mutation that did not apply is NOT_MEASURED ---------------------------------------
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td))
        r = adjudicate(mutation(find="THIS TEXT IS NOT IN THE GUARD", replace="x"), root)
        expect("M5  a `find` that does not occur grades NOT_MEASURED", r.verdict, Verdict.NOT_MEASURED)
        expect_contains("M5  ...and says the gate was never challenged", r.reason, "did not apply")
        r2 = adjudicate(mutation(find="exit", replace="exit ", occurrences=1), root)
        expect("M5b a `find` occurring more often than declared grades NOT_MEASURED",
               r2.verdict, Verdict.NOT_MEASURED)

def m6() -> None:
    # ---- M6: #1794 — the loader REFUSES an anchor produced by the mutation target -----------------
    # This is the defect that made a real harness report NOT MEASURED exactly where the truth was FAIL.
    # It is refused at LOAD time, because it does not yield a hedged answer, it yields a wrong one.
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        spec = root / "specs.yml"
        spec.write_text(textwrap.dedent("""\
            mutations:
              - id: self-anchored
                gate: synthetic
                command: ["bash", "fixture.sh"]
                target: guard.sh
                find: "exit 0"
                replace: "exit 1"
                breaks: "anything"
                anchor:
                  pattern: "\\\\d+ passed"
                  produced_by: guard.sh
            """), encoding="utf-8")
        try:
            load_specs(spec, root)
            got = "accepted"
        except SpecError as e:
            got = "refused" if "cannot be its own anchor" in str(e) else f"refused, wrong reason: {e}"
        expect("M6  a spec whose anchor is produced by the mutation target is REFUSED (#1794)",
               got, "refused")

def m7() -> None:
    # ---- M7: #1784 — an anchor that never matched is NOT_MEASURED, not a silent pass --------------
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td))
        r = adjudicate(mutation(anchor=Anchor(pattern=r"NO SUCH LINE EVER", produced_by="fixture.sh")), root)
        expect("M7  an anchor that fails to match the GREEN control grades NOT_MEASURED",
               r.verdict, Verdict.NOT_MEASURED)
        expect_contains("M7  ...and blames the anchor, not the gate", r.reason, "did not match the GREEN control")

def m8() -> None:
    # ---- M8: point 3 at RUN time — the producer's bytes must not move ----------------------------
    # A spec can pass the load-time check and still edit the producer, if the mutation target is a file
    # the producer is generated from or a path that resolves to it. The hash check catches that.
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td))
        # `guard.sh` here rewrites the anchor producer when it runs — a mutation whose blast radius
        # reaches the anchor. The harness must refuse to grade rather than trust the anchor.
        (root / "guard.sh").write_text(
            "#!/usr/bin/env bash\n# MARKER\ngrep -q 'INVARIANT-HOLDS' subject.txt || exit 1\nexit 0\n",
            encoding="utf-8")
        r = adjudicate(
            mutation(
                find="# MARKER",
                replace="# MARKER mutated",
                anchor=Anchor(pattern=r"\d+ passed, \d+ failed", produced_by="subject.txt"),
            ),
            root,
        )
        # subject.txt is rewritten by the fixture on every case, so it cannot serve as an anchor; the
        # anchor never matches the control and the leg is NOT_MEASURED rather than falsely graded.
        expect("M8  an anchor pointing at a file the run rewrites cannot grade a gate",
               r.verdict, Verdict.NOT_MEASURED)

def m9() -> None:
    # ---- M9: the three verdicts stay separable, in the counters AND in the exit code (point 2) ----
    with tempfile.TemporaryDirectory() as td:
        root = build_repo(Path(td))
        live = adjudicate(mutation(), root)
        unmeasured = adjudicate(mutation(find="NOT PRESENT", replace="x"), root)
        counts = tally_counters([live, unmeasured])
        expect("M9  every bucket is reported, including the empty one",
               sorted(counts), sorted([v.value for v in Verdict]))
        expect("M9  NOT_MEASURED is counted as itself", counts[Verdict.NOT_MEASURED.value], 1)
        expect("M9  NOT_MEASURED alone is exit 3, never 0 and never 1 (#266)",
               exit_code([live, unmeasured]), 3)
        expect("M9  all-JUSTIFIED is the only exit 0", exit_code([live]), 0)
        blob = json.dumps([live.to_json(), unmeasured.to_json()])
        expect_contains("M9  NOT_MEASURED survives serialisation end to end", blob, '"NOT_MEASURED"')
        expect_contains("M9  ...and so does its reason", blob, unmeasured.reason[:40])

def m10() -> None:
    # ---- M10: MUTATE THE FIXTURE — does `expect` above actually fail? (#1794's M10, point 4) ------
    # Everything above is this file judging the harness. This leg judges THIS FILE. `expect` is the
    # refutation helper all nine legs lean on; if it were a no-op — an `if got == want` with no else,
    # a swallowed assert — every line above would print PASS forever and this selftest would be the
    # eleventh dead check. So: hand it a deliberately WRONG answer and require it to say so.
    global PASSED, FAILED
    saved_pass, saved_fail = PASSED, FAILED
    PASSED = FAILED = 0
    sink = Path(os.devnull).open("w")
    real_stdout, sys.stdout = sys.stdout, sink
    try:
        returned_wrong = expect("(probe) a WRONG expectation", Verdict.JUSTIFIED, Verdict.DECORATIVE)
        returned_right = expect("(probe) a RIGHT expectation", Verdict.JUSTIFIED, Verdict.JUSTIFIED)
        probe_pass, probe_fail = PASSED, FAILED
    finally:
        sys.stdout = real_stdout
        sink.close()
        PASSED, FAILED = saved_pass, saved_fail
    expect("M10 the refutation helper RETURNS False on a wrong answer", returned_wrong, False)
    expect("M10 ...and True on a right one", returned_right, True)
    expect("M10 ...and increments the FAILED counter exactly once", probe_fail, 1)
    expect("M10 ...and the PASSED counter exactly once", probe_pass, 1)

    # ...and the same for the whole pipeline: a fixture-level mutation must be visible as a real
    # failure, not merely as a returned flag. Drive the harness's own selftest command through a
    # mutation of THIS file's expectation and require the run to go red.
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        (root / "probe.py").write_text(textwrap.dedent("""\
            import sys
            want = "JUSTIFIED"
            got = "JUSTIFIED"
            ok = got == want
            print("probe fixture — 1 assertion(s): %d passed, %d failed" % (int(ok), int(not ok)))
            sys.exit(0 if ok else 1)
            """), encoding="utf-8")
        (root / "driver.sh").write_text("#!/usr/bin/env bash\nexec python3 probe.py\n", encoding="utf-8")
        r = adjudicate(
            Mutation(
                id="fixture-mutation",
                gate="the harness's own probe",
                command=["bash", "driver.sh"],
                target="probe.py",
                find='want = "JUSTIFIED"',
                replace='want = "DECORATIVE"',
                breaks="the probe's expected verdict is falsified",
                anchor=Anchor(pattern=r"\d+ passed, \d+ failed", produced_by="driver.sh"),
                timeout=60,
            ),
            root,
        )
        expect("M10 a mutation of the FIXTURE's own expectation is caught as JUSTIFIED",
               r.verdict, Verdict.JUSTIFIED)

# ==================================================================================================
# P-LEGS — the PATH mutation kind (.github#1830, spec on #1842)
#
# Everything above holds the TEXT kind to the four-point specification. These legs do the same for the
# kind that mutates the TREE, and they exist because #1830 found that not one of the never-red gates
# outside `.github` has a subject a text substitution can reach: their subjects are an absent
# directory, a dangling symlink, a symlink checked out as a regular file, and — the one that matters —
# a view that RESOLVES but is INCOMPLETE.
#
# The synthetic gate below is a miniature `skill-view check`, and `DEAD_VIEW_GUARD` is deliberately
# built in #1715's shape: it tests the root's presence THROUGH the symlink and never looks at what is
# inside. P2 requires that guard to grade DECORATIVE. **That is the leg that proves this kind can
# return a negative verdict at all**, exactly as M2 does for the text kind, and without it every
# JUSTIFIED below would be unfalsified.
# ==================================================================================================

VIEW_GUARD = """\
#!/usr/bin/env bash
# A miniature skill-view: every id the SOURCE declares must be visible THROUGH the view root.
# It prints its count BEFORE looking, so the anchor witnesses that the guard was reached — and it
# lives in a file no path mutation touches, which is specification point 3 with real content.
ids=(); for d in source/*/; do [ -f "$d/SKILL.md" ] && ids+=("$(basename "$d")"); done
echo "synthetic view-guard: ${#ids[@]} declared id(s)"
[ -d view ] || { echo "[unresolvable-root]"; exit 1; }
bad=0
for id in "${ids[@]}"; do
  [ -e "view/$id/SKILL.md" ] || { echo "[missing-skill] $id"; bad=$((bad+1)); }
done
[ "$bad" -eq 0 ] || exit 1
exit 0
"""

DEAD_VIEW_GUARD = """\
#!/usr/bin/env bash
# #1715's shape, on purpose: it enumerates the SOURCE, tests the root with `[ -d ]` THROUGH the
# symlink, and never opens it. A partial view is invisible to this, and P2 requires the harness to
# SAY SO rather than to report a clean sweep.
ids=(); for d in source/*/; do [ -f "$d/SKILL.md" ] && ids+=("$(basename "$d")"); done
echo "synthetic view-guard: ${#ids[@]} declared id(s)"
[ -d view ] || exit 1
exit 0
"""

VIEW_ANCHOR = Anchor(pattern=r"synthetic view-guard: \d+ declared id\(s\)", produced_by="guard.sh")


def build_view_repo(tmp: Path, guard: str = VIEW_GUARD) -> Path:
    """`source/` holds two skills; `view` is a whole-directory symlink to it — the receivers' layout."""
    root = tmp
    for skill in ("alpha", "beta"):
        (root / "source" / skill).mkdir(parents=True)
        (root / "source" / skill / "SKILL.md").write_text(f"# {skill}\n", encoding="utf-8")
    (root / "view").symlink_to("source")
    (root / "guard.sh").write_text(guard, encoding="utf-8")
    return root


def path_mutation(**kw) -> Mutation:
    base = dict(
        id="synthetic-path",
        gate="synthetic-view-gate.yml",
        command=["bash", "guard.sh"],
        target="view",
        find="",
        replace="",
        breaks="the generated view root stops resolving to the declared skills",
        anchor=VIEW_ANCHOR,
        discriminator=Discriminator.EXIT_CONTRACT,
        kind=MutationKind.PATH,
        timeout=60,
    )
    base.update(kw)
    return Mutation(**base)


def p1() -> None:
    # ---- P1: all four ops APPLY, the gate fires, and the tree comes back byte-for-byte ------------
    cases = [
        (PathOp.DELETE, {}, "the view was never generated"),
        (PathOp.RETARGET, {"replace": "nowhere-at-all"}, "the view dangles"),
        (PathOp.REPLACE_WITH_FILE, {"replace": "source"}, "core.symlinks=false left a text file"),
        (PathOp.PARTIAL_VIEW, {"find": "alpha"}, "the view resolves but is INCOMPLETE (#1715)"),
    ]
    for op, extra, what in cases:
        with tempfile.TemporaryDirectory() as td:
            root = build_view_repo(Path(td))
            before = path_signature(root / "view")
            r = adjudicate(path_mutation(op=op, **extra), root)
            expect(f"P1  `{op.value}` — {what} — grades JUSTIFIED", r.verdict, Verdict.JUSTIFIED)
            expect(f"P1  `{op.value}` control ran green", r.control_rc, 0)
            expect(f"P1  `{op.value}` the mutant exited 1 (lib.gate FINDING)", r.mutant_rc, 1)
            # RESTORE, verified by SIGNATURE — the path kind's answer to M1's byte comparison. A
            # symlink must come back AS a symlink, with the same target text, or a later leg in a
            # sweep measures a tree this one quietly rewrote.
            expect(f"P1  `{op.value}` the tree signature is restored exactly",
                   path_signature(root / "view"), before)
            expect(f"P1  `{op.value}` ...and it is still a symlink, not a copy",
                   (root / "view").is_symlink(), True)
            expect(f"P1  `{op.value}` no stash directory is left behind",
                   [p.name for p in root.iterdir() if p.name.startswith(".gate-mutate-stash-")], [])


def p2() -> None:
    # ---- P2: THE NEGATIVE VERDICT FOR THE PATH KIND, and #1715 is the model ----------------------
    # A guard that tests the root through the symlink and never opens it CANNOT see a partial view.
    # If this leg ever grades JUSTIFIED, every P1 result above is worthless.
    with tempfile.TemporaryDirectory() as td:
        root = build_view_repo(Path(td), guard=DEAD_VIEW_GUARD)
        r = adjudicate(path_mutation(op=PathOp.PARTIAL_VIEW, find="alpha"), root)
        expect("P2  a guard blind to a PARTIAL view grades DECORATIVE (#1715's shape)",
               r.verdict, Verdict.DECORATIVE)
        expect_contains("P2  ...and the reason names what was broken", r.reason, "stayed GREEN")
        expect("P2  DECORATIVE is exit code 1 (lib.gate FINDING)", exit_code([r]), 1)
        expect("P2  ...and the tree is restored even after a negative verdict",
               (root / "view").is_symlink(), True)


def p3() -> None:
    # ---- P3: an op that CANNOT apply is NOT_MEASURED, and never a finding ------------------------
    # "The mutation did not apply" and "the gate held" are opposite facts. #1810's S5 measured that
    # dropping this check reds the selftest for the text kind; this is the same check one kind over.
    with tempfile.TemporaryDirectory() as td:
        root = build_view_repo(Path(td))
        r = adjudicate(path_mutation(op=PathOp.PARTIAL_VIEW, find="gamma-does-not-exist"), root)
        expect("P3  `partial-view` naming a non-entry is NOT_MEASURED", r.verdict, Verdict.NOT_MEASURED)
        expect_contains("P3  ...and says the mutation did not apply", r.reason, "did not apply")
        expect("P3  ...and the tree is restored", (root / "view").is_symlink(), True)

    with tempfile.TemporaryDirectory() as td:
        root = build_view_repo(Path(td))
        r = adjudicate(path_mutation(op=PathOp.RETARGET, target="source", replace="nowhere"), root)
        expect("P3  `retarget` on a path that is not a symlink is NOT_MEASURED",
               r.verdict, Verdict.NOT_MEASURED)
        expect("P3  ...and the real directory it refused to retarget is intact",
               (root / "source" / "alpha" / "SKILL.md").is_file(), True)

    with tempfile.TemporaryDirectory() as td:
        root = build_view_repo(Path(td))
        r = adjudicate(path_mutation(op=PathOp.DELETE, target="view-that-is-not-there"), root)
        expect("P3  an absent target is NOT_MEASURED, not a dead gate", r.verdict, Verdict.NOT_MEASURED)


def p3b() -> None:
    # ---- P3b: an op that APPLIES CLEANLY and changes NOTHING is NOT_MEASURED -----------------------
    # THIS LEG EXISTS BECAUSE IT WAS MISSING, and its absence was found the way everything else here
    # is found: by removing the safeguard and watching. The probe that neuters the path kind's no-op
    # check (`if path_signature(target) == sig_before`) left the selftest at 86/0 — a safeguard with
    # no negative control, which is the exact shape #1810's S1 exposed in leg M4b and the eleventh
    # entry on the list this module exists to prevent.
    #
    # `retarget` at a symlink's EXISTING target is the reachable case: the loader cannot catch it,
    # because catching it means reading the tree, and `load_specs` is pure. So the run-time check is
    # the only thing standing between "the gate held" and "nothing was ever broken" — and those are
    # opposite facts.
    with tempfile.TemporaryDirectory() as td:
        root = build_view_repo(Path(td))
        r = adjudicate(path_mutation(op=PathOp.RETARGET, replace="source"), root)
        expect("P3b `retarget` at the link's EXISTING target is NOT_MEASURED, never a held gate",
               r.verdict, Verdict.NOT_MEASURED)
        expect_contains("P3b ...and says it was a NO-OP", r.reason, "NO-OP")
        expect("P3b ...and the tree is unchanged", path_signature(root / "view"), "link:source")


def p4() -> None:
    # ---- P4: the LOADER refuses a path spec that could not yield a measurement -------------------
    # Refused at load time rather than graded cautiously, for the same reason M6's self-anchored spec
    # is: these defects do not produce a hedged answer, they produce a leg that looks like it ran.
    def spec(body: str) -> str:
        return "mutations:\n" + textwrap.indent(textwrap.dedent(body), "  ")

    common = """\
        - id: p
          gate: g.yml
          kind: path
          command: [bash, guard.sh]
          target: view
          breaks: the view root
          anchor: {pattern: 'x', produced_by: guard.sh}
        """
    cases = [
        ("op: shell-out\n", "unknown path op", "an op outside the CLOSED vocabulary"),
        ("op: retarget\n", "writes `replace:`", "`retarget` with nothing to point at"),
        ("op: partial-view\n", "omitting nothing", "`partial-view` with no entry to omit"),
        ("", "missing required key(s): op", "a path spec with no `op:` at all"),
    ]
    for extra, needle, what in cases:
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "s.yml"
            p.write_text(spec(common + textwrap.indent(extra, "  ")) if extra else spec(common),
                         encoding="utf-8")
            try:
                load_specs(p, Path(td))
                expect(f"P4  {what} is REFUSED", "loaded", "SpecError")
            except SpecError as e:
                expect_contains(f"P4  {what} is REFUSED", str(e), needle)

    # And the closed vocabulary is stated in the refusal, so a spec author does not go looking for a
    # member that deliberately does not exist.
    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "s.yml"
        p.write_text(spec(common + "  op: shell-out\n"), encoding="utf-8")
        try:
            load_specs(p, Path(td))
            expect("P4  the refusal names the whole vocabulary", "loaded", "SpecError")
        except SpecError as e:
            expect_contains("P4  the refusal names the whole vocabulary", str(e), "partial-view")
            expect_contains("P4  ...and says there is no command escape hatch", str(e), "run this command")


def p5() -> None:
    # ---- P5: path_signature is the check the restore leans on, so hold IT to a standard ----------
    # S9 on #1810's table removed the restore and every leg refused. That check is only as good as the
    # comparison behind it: a signature that cannot tell a symlink from the directory it resolves to
    # would report a successful restore over a tree that had been flattened.
    with tempfile.TemporaryDirectory() as td:
        root = build_view_repo(Path(td))
        link = path_signature(root / "view")
        real = path_signature(root / "source")
        expect("P5  a symlink signs as its TARGET TEXT, never as what it resolves to", link, "link:source")
        expect("P5  ...so a link and the directory it points at do not compare equal", link == real, False)
        (root / "source" / "alpha" / "SKILL.md").write_text("# changed\n", encoding="utf-8")
        expect("P5  a directory's signature moves when an entry's CONTENT moves",
               path_signature(root / "source") != real, True)
        after_content = path_signature(root / "source")
        (root / "source" / "alpha" / "SKILL.md").unlink()
        expect("P5  ...and when an entry is REMOVED, which is what partial-view does",
               path_signature(root / "source") != after_content, True)


LEGS = [m1, m2, m3, m4, m4b, m4c, m4d, m5, m6, m7, m8, m9, m10, p1, p2, p3, p3b, p4, p5]


def main() -> int:
    """Run every leg in isolation, and REPORT a crash instead of dying of one.

    The isolation is not tidiness, it is the standard this file exists to enforce, applied to itself.
    Measured: removing the harness's control-anchor check (`#1784`'s defect) or its mutant-anchor check
    (`#1582`'s) made ``adjudicate`` raise ``AttributeError`` — and the selftest exited 1 with a
    traceback and NO tally. That is a red build, but it is a CRASH, not a finding, and `#1812` is
    precisely the record that the two must not be conflated. A crashed leg now prints ``FAIL`` and the
    run still reaches its tally, so the anchor this file offers the world is the same one it demands
    of everything else.
    """
    global FAILED
    print("gate-mutation selftest — the harness's own negative control")
    for leg in LEGS:
        try:
            leg()
        except Exception as e:  # noqa: BLE001 — a crashed leg is a REPORTED failure, never a silent one.
            print(f"FAIL  {leg.__name__} CRASHED — {type(e).__name__}: {e}")
            for line in __import__("traceback").format_exc().splitlines():
                print(f"    | {line}")
            FAILED += 1

    total = PASSED + FAILED
    print(f"gate-mutation selftest — {total} assertion(s): {PASSED} passed, {FAILED} failed")
    if FAILED:
        print("::error::gate-mutation selftest FAILED — the mutation harness is not trustworthy")
        return 1
    print("gate-mutation selftest — OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
