#!/usr/bin/env python3
"""Selftest for `scripts/lib/sparse.py` — the ONE reading of a sparse-checkout block (.github#1530).

WHY THIS EXISTS SEPARATELY FROM THE TWO CALLERS' FIXTURES. The rules in that module are the reason it
exists: the newline split that makes a folded scalar visible, the omitted cone flag that IS `true`,
and the declared-but-empty block that is an empty tree rather than a full clone. Each is a place where
the plausible reading is the wrong one, and each was — until #1530 — asserted only INDIRECTLY, through
whichever caller happened to exercise it.

Indirect coverage is why the two copies could drift without either fixture noticing. Measured on the
pre-hoist files: `sparse_set.py` read a bare `sparse-checkout:` as a FULL CLONE where the gate refused
it, and refused a quoted `sparse-checkout-cone-mode: "false"` that the gate accepted. Neither fixture
could see either, because neither caller's own subject contains those shapes. So the rules are
asserted HERE, on the module, in both directions — the readings AND the refusals.

This asserts the module's API directly rather than through a workflow file. `tests/sparse-checkout-
closure/run.sh` covers the end-to-end path; what this covers is the reading itself, on the `with:`
mappings the readers actually receive, including the shapes no live workflow has.

Exit 0 = every case held. Exit 1 = a case did not. Exit 2 = the module could not be imported at all,
which is a tooling failure and is never reported as a pass (#266).
"""

from __future__ import annotations

from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts"))

try:
    from lib.sparse import SparseRefusal, cone_mode_of, patterns_of
except Exception as error:  # noqa: BLE001 — an import failure is a NO VERDICT, never a pass.
    print(f"FAIL  scripts/lib/sparse.py could not be imported: {error!r}", file=sys.stderr)
    sys.exit(2)

WHERE = "selftest.yml (job `a`)"

passed = 0
failed = 0


def ok(name: str) -> None:
    global passed
    passed += 1
    print(f"PASS  {name}")


def bad(name: str, detail: str = "") -> None:
    global failed
    failed += 1
    print(f"FAIL  {name}")
    if detail:
        print(f"    | {detail}")


def _both(params: dict) -> tuple[list[str] | None, bool]:
    """Both readings, composed HERE rather than in the module.

    `lib/sparse.py` exports the two readings and no combined form, on purpose — its two callers
    compose them differently and a third composition would be a second reading inside the module that
    exists to have one (see that module's `__all__` note). This helper is the selftest's own
    composition, and it is the unconditional one so that every leg below states both answers.
    """
    return patterns_of(params, WHERE), cone_mode_of(params, WHERE)


def reads(name: str, params: dict, expected: tuple[list[str] | None, bool]) -> None:
    """The module reads this `with:` mapping as exactly `expected`."""
    try:
        got = _both(params)
    except SparseRefusal as error:
        bad(name, f"refused instead of reading: {error}")
        return
    if got == expected:
        ok(name)
    else:
        bad(name, f"expected {expected!r}, got {got!r}")


def refuses(name: str, params: dict, reason: str) -> None:
    """The module refuses this mapping — and FOR THE STATED REASON.

    A bare "it raised" would pass on a typo in the module, which is epic #266's vacuous-failure
    defect in miniature, so the message is asserted too.
    """
    try:
        got = _both(params)
    except SparseRefusal as error:
        if reason in str(error):
            ok(name)
        else:
            bad(name, f"refused, but not for the claimed reason (no '{reason}'): {error}")
        return
    bad(name, f"expected a refusal, got {got!r}")


# ---- READING 1: the newline split, and the FOLDED scalar it makes visible -------------------------
# `actions/checkout` splits its input on newlines and drops blanks, so a literal block scalar and a
# plain string must reach git identically. PyYAML has already done the folding by the time the module
# sees the value, which is exactly why mirroring the ACTION's split — rather than the YAML shape —
# keeps a folded scalar honest: it arrives as ONE string with a space in it and must stay one.
reads(
    "a literal block scalar splits into one pattern per line",
    {"sparse-checkout": "/scripts/\n/docs/\n", "sparse-checkout-cone-mode": False},
    (["/scripts/", "/docs/"], False),
)
reads(
    "a plain string reaches git as the same single pattern",
    {"sparse-checkout": "/scripts/", "sparse-checkout-cone-mode": False},
    (["/scripts/"], False),
)
reads(
    "blank lines are dropped, as the action drops them",
    {"sparse-checkout": "\n/scripts/\n\n   \n/docs/\n", "sparse-checkout-cone-mode": False},
    (["/scripts/", "/docs/"], False),
)
reads(
    "a YAML list is already split and is not re-split",
    {"sparse-checkout": ["/scripts/", "/docs/"], "sparse-checkout-cone-mode": False},
    (["/scripts/", "/docs/"], False),
)
# THE ONE THAT MATTERS. `sparse-checkout: >` joins its lines with a SPACE, so the runner receives ONE
# pattern containing a space, which matches nothing and fetches an empty tree. A hand parser that
# scans by indentation reports two clean patterns and goes green over it — the exact fail-open
# sparse_set.py's first draft shipped. The module must hand back the ONE space-joined pattern, so its
# callers can see the space; flattening it into two here would put the bug back.
reads(
    "a FOLDED scalar yields ONE space-joined pattern, not two clean ones",
    {"sparse-checkout": "/scripts/ /scripts/lib/\n", "sparse-checkout-cone-mode": False},
    (["/scripts/ /scripts/lib/"], False),
)

# ---- READING 2: an omitted cone flag IS `true` ----------------------------------------------------
# It decides whether the patterns are gitignore expressions or rooted directory prefixes — what they
# MEAN — so the default is the action's documented one, and an unreadable one is a no-verdict.
reads(
    "an OMITTED sparse-checkout-cone-mode is `true`, the action's documented default",
    {"sparse-checkout": "scripts"},
    (["scripts"], True),
)
reads(
    "a real YAML boolean is taken as-is (false)",
    {"sparse-checkout": "/scripts/", "sparse-checkout-cone-mode": False},
    (["/scripts/"], False),
)
reads(
    "a real YAML boolean is taken as-is (true)",
    {"sparse-checkout": "scripts", "sparse-checkout-cone-mode": True},
    (["scripts"], True),
)
# Every `with:` value reaches an action as a string and the action reads this one with
# `core.getBooleanInput`, so the quoted spelling is a workflow that WORKS. sparse_set.py refused it
# before the hoist — a no-verdict over a healthy workflow, which is how a guard gets deleted.
for spelling, expected_cone in (("false", False), ("FALSE", False), ("true", True), ("True", True)):
    reads(
        f"a QUOTED boolean {spelling!r} is not unreadable — it is what every with: value already is",
        {"sparse-checkout": "/scripts/", "sparse-checkout-cone-mode": spelling},
        (["/scripts/"], expected_cone),
    )
refuses(
    "an unevaluated ${{ }} cone-mode is refused; its value is decided at run time",
    {"sparse-checkout": "/scripts/", "sparse-checkout-cone-mode": "${{ inputs.cone }}"},
    "unreadable",
)
refuses(
    "a cone-mode that is neither boolean nor a boolean spelling is refused, not defaulted",
    {"sparse-checkout": "/scripts/", "sparse-checkout-cone-mode": "yes-please"},
    "unreadable",
)

# ---- READING 3: declared-but-empty is an EMPTY TREE, never a full clone ---------------------------
# All four spellings are refused ALIKE, because the runner cannot tell them apart either. Giving them
# different verdicts would be inventing a distinction the subject does not make.
#
# THE FOURTH IS THE DRIFT #1530 FOUND. PyYAML resolves a bare `sparse-checkout:` to `None`, exactly as
# it resolves an ABSENT key, so `params.get(...)` conflates a fetch of NOTHING with a fetch of
# EVERYTHING. sparse_set.py used `get` and reported a full clone. Only `in` can tell them apart, and
# this leg is what holds the module to it.
for label, value in (
    ("an empty string", ""),
    ("a whitespace-only string", "   "),
    ("an empty list", []),
    ("a BARE key with no value (PyYAML: None) — the pre-#1530 fail-open", None),
):
    refuses(
        f"[{label}] a DECLARED sparse-checkout supplying no pattern is refused",
        {"sparse-checkout": value, "sparse-checkout-cone-mode": False},
        "supplies no pattern",
    )

# ...and the fact it must NOT be confused with. An ABSENT key is a full clone, which under-fetches
# nothing and is not a subject. `None`, not `[]` — the two are different facts and the callers branch
# on the difference.
if patterns_of({"repository": "FS-GG/.github"}, WHERE) is None:
    ok("an ABSENT sparse-checkout is None (a full clone), not an empty pattern list")
else:
    bad("an ABSENT sparse-checkout is None (a full clone), not an empty pattern list")

reads(
    "a step with NO sparse-checkout at all reads as (None, cone-default)",
    {"repository": "FS-GG/.github"},
    (None, True),
)

# THE ORDER IS THE CALLER'S, AND THIS LEG IS WHY THE MODULE EXPORTS NO COMBINED FORM. A full clone
# beside an unreadable cone flag is a full clone: the gate reads the patterns, sees `None`, and never
# asks about a cone mode that decides the meaning of patterns nobody wrote. `sparse_set.py` reads both
# unconditionally, and is right to for its own purpose. Both readings stay available separately so
# neither caller has to accept the other's ordering — and a step like this one is proof the ordering
# is a real choice rather than a stylistic one.
if patterns_of({"sparse-checkout-cone-mode": "${{ inputs.cone }}"}, WHERE) is None:
    ok("a full clone is readable on its own, even beside a cone flag that would be refused")
else:
    bad("a full clone is readable on its own, even beside a cone flag that would be refused")
try:
    cone_mode_of({"sparse-checkout-cone-mode": "${{ inputs.cone }}"}, WHERE)
    bad("...and asking about that cone flag SEPARATELY still refuses it")
except SparseRefusal:
    ok("...and asking about that cone flag SEPARATELY still refuses it")

# ---- THE REFUSAL IS THE MODULE'S OWN, so a caller can map it to its own no-verdict ----------------
# `lib/sparse.py` deliberately does not import `lib/gate.py`: the gate collects SparseRefusal as an
# exit-3 refusal, sparse_set.py re-raises it as its own exit-2 SparseError. If SparseRefusal ever
# became a GateError, the test helper would silently acquire the gate harness as a dependency.
if SparseRefusal.__mro__[1] is Exception:
    ok("SparseRefusal derives straight from Exception — lib/sparse.py stays harness-free")
else:
    bad(
        "SparseRefusal has grown a base class",
        f"mro: {[c.__name__ for c in SparseRefusal.__mro__]}",
    )

# The module must not be importable only by luck of the caller's sys.path: `scripts/` on the path is
# the whole contract, and `lib` is a namespace package with no __init__.py (as lib/gate.py documents).
if not (REPO_ROOT / "scripts" / "lib" / "__init__.py").exists():
    ok("lib stays a namespace package — `scripts/` on sys.path is the whole import contract")
else:
    bad("scripts/lib/__init__.py appeared; the documented import contract has changed")

print()
print(f"sparse lib selftest: {passed} passed, {failed} failed.")
sys.exit(1 if failed else 0)
