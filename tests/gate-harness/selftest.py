#!/usr/bin/env python3
"""Selftest for ``scripts/lib/gate.py`` — the shared gate harness (.github#1159).

The harness has no gate consumers yet (they migrate onto it in #1160 and the follow-ups), so THIS is
what proves it works. Two things matter most and are asserted hardest:

* the ``ExitCode`` values ARE the contract — 0/1/2/3 exactly, and no others; and
* the "couldn't-look" path — a ``GateError``, an ``Unreachable``, and a raw CRASH each become a
  no-verdict, and in particular a crash NEVER surfaces as the FINDING code (1) the way an uncaught
  Python exception otherwise would. That last one is the whole reason :func:`gate.run` exists.

Offline and credential-free. Run via ``tests/gate-harness/run.sh``.
"""

from __future__ import annotations

import contextlib
import io
import os
import sys
import tempfile

# The import contract every gate uses: scripts/ on the path, then `from lib.gate import ...`. Asserting
# it here means #1160 can copy this exact idiom and know it resolves.
_SCRIPTS = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "scripts"))
sys.path.insert(0, _SCRIPTS)

from lib.gate import (  # noqa: E402 — deliberate: the path insert above is the point.
    ExitCode,
    GateError,
    Unreachable,
    base_parser,
    load_yaml,
    read_text,
    report_findings,
    report_ok,
    run,
    triggers,
    workflow_files,
)

_failures: list[str] = []


def check(name: str, cond: bool, detail: str = "") -> None:
    if cond:
        print(f"PASS  {name}")
    else:
        print(f"FAIL  {name}" + (f"\n    | {detail}" if detail else ""))
        _failures.append(name)


def capturing(fn):
    """Call ``fn`` with stdout+stderr captured; return ``(rc, stdout, stderr)``.

    Keeps the harness's own ``::error::`` annotations and the crash-path traceback out of the selftest
    log, and lets a check assert the MESSAGE as well as the code."""
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        rc = fn()
    return rc, out.getvalue(), err.getvalue()


def raises_gateerror(fn) -> bool:
    try:
        fn()
        return False
    except GateError:
        return True


# ── 1. The ExitCode contract IS 0/1/2/3, exactly ───────────────────────────────────────────────────
check("ExitCode.OK == 0", ExitCode.OK == 0)
check("ExitCode.FINDING == 1", ExitCode.FINDING == 1)
check("ExitCode.NO_VERDICT_RETRYABLE == 2", ExitCode.NO_VERDICT_RETRYABLE == 2)
check("ExitCode.NO_VERDICT_PERMANENT == 3", ExitCode.NO_VERDICT_PERMANENT == 3)
check(
    "ExitCode has EXACTLY these four members",
    [e.value for e in ExitCode] == [0, 1, 2, 3],
    detail=f"got {[e.value for e in ExitCode]}",
)
check("ExitCode is usable as a plain int (IntEnum)", int(ExitCode.FINDING) == 1 and isinstance(ExitCode.OK, int))

# ── 2. run(): the couldn't-look path — every failure becomes a VERDICT, never exit 1 ────────────────
check("run returns OK for a clean main", run(lambda a: 0, [], name="t") == 0)
check("run passes a FINDING return straight through", run(lambda a: 1, [], name="t") == 1)


def _raise_gateerror(_a):
    raise GateError("subject unreadable")


rc, _o, err = capturing(lambda: run(_raise_gateerror, [], name="check-x"))
check("run maps GateError -> NO_VERDICT_PERMANENT (3)", rc == 3)
check("run's GateError message is a no-verdict annotation", "::error::check-x: no verdict —" in err)


def _raise_unreachable(_a):
    raise Unreachable("rate limit")


rc, _o, err = capturing(lambda: run(_raise_unreachable, [], name="check-x"))
check("run maps Unreachable -> NO_VERDICT_RETRYABLE (2), checked before GateError", rc == 2)
check("run's Unreachable message is a no-verdict annotation", "::error::check-x: no verdict —" in err)


def _crash(_a):
    raise KeyError("a bug in the gate")


rc, _o, err = capturing(lambda: run(_crash, [], name="check-x"))
check("run turns a raw CRASH into a no-verdict (3)", rc == 3)
check(
    "a CRASH is NEVER misreported as a FINDING (1) — the whole reason run() exists",
    rc != int(ExitCode.FINDING),
)
check("run's crash message says NO VERDICT and shows the traceback", "NO VERDICT" in err and "KeyError" in err)
check(
    "Unreachable is a GateError, so a gate catching only GateError still fails closed",
    issubclass(Unreachable, GateError),
)

# ── 3. triggers(): the on:->True (YAML 1.1) normaliser, one reconciled copy ─────────────────────────
check("triggers reads the boolean True key (the YAML 1.1 trap)", triggers({True: {"push": None}}) == {"push": None})
check("triggers reads a string 'on' key", triggers({"on": {"push": None}}) == {"push": None})
check(
    "triggers normalises a list to a mapping, str-coercing items",
    triggers({True: ["push", "pull_request"]}) == {"push": None, "pull_request": None},
)
check("triggers normalises a scalar string", triggers({True: "push"}) == {"push": None})
check("triggers returns {} for an ABSENT on:", triggers({"jobs": {}}) == {})
check(
    "triggers raises GateError on an unreadable on: — a silent skip is how a coherence gate fails open",
    raises_gateerror(lambda: triggers({True: 5}, "wf.yml")),
)

# ── 4. load_yaml / read_text / workflow_files fail CLOSED ───────────────────────────────────────────
check("load_yaml parses a mapping", load_yaml("a: 1\n", "x") == {"a": 1})
check("load_yaml raises on a non-mapping top level", raises_gateerror(lambda: load_yaml("- 1\n- 2\n", "x")))
check("load_yaml raises on unparsable YAML", raises_gateerror(lambda: load_yaml("a: [\n", "x")))
check("read_text raises GateError on a missing file (a failed read is not a clean subject)",
      raises_gateerror(lambda: read_text("/no/such/path/at/all", "x")))

with tempfile.TemporaryDirectory() as d:
    check("workflow_files raises when the workflows dir is absent", raises_gateerror(lambda: workflow_files(d)))
    _wfd = os.path.join(d, ".github", "workflows")
    os.makedirs(_wfd)
    check("workflow_files raises on an EMPTY workflows dir (broken gate, not a clean one)",
          raises_gateerror(lambda: workflow_files(d)))
    open(os.path.join(_wfd, "b.yml"), "w").close()
    open(os.path.join(_wfd, "a.yaml"), "w").close()
    check(
        "workflow_files returns .yml and .yaml, sorted",
        [os.path.basename(p) for p in workflow_files(d)] == ["a.yaml", "b.yml"],
    )

# ── 5. reporting + argparse helpers ─────────────────────────────────────────────────────────────────
rc, out, _e = capturing(lambda: report_ok("check-x", "37 files clean"))
check("report_ok returns OK and prints the clean line", rc == 0 and "check-x: OK — 37 files clean" in out)
rc, _o, err = capturing(lambda: report_findings("check-x", ["thing A", "thing B"]))
check("report_findings returns FINDING and annotates each", rc == 1 and "::error::thing A" in err and "FAILED" in err)
check("base_parser gives --root defaulting to '.'", base_parser("d").parse_args([]).root == ".")

# ── verdict ─────────────────────────────────────────────────────────────────────────────────────────
print()
if _failures:
    print(f"gate-harness selftest FAILED — {len(_failures)} check(s): {', '.join(_failures)}")
    sys.exit(1)
print("gate-harness selftest OK — all checks passed")
