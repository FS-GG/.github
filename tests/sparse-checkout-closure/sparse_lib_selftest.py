#!/usr/bin/env python3
"""Selftest for `scripts/lib/sparse.py` — the ONE reading of a checkout step (.github#1530, #1553).

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

THE STEP SELECTOR IS HERE FOR THE SAME REASON (#1553). #1530 hoisted the block PARSE and left the
step SELECTOR in each caller. Underneath their different filters both had to answer one identical
question — is this step an `actions/checkout` aimed at repository R — and they answered it
differently: `sparse_set.py` never read `uses:` at all, and compared `repository:` case-sensitively
where the gate casefolds and its own fixture asserts a case-variant spelling must still resolve.
Again neither divergence was reachable from either caller's fixture, because neither caller's own
subject contains those shapes. So the qualification is asserted HERE, on the module, on documents.

This asserts the module's API directly rather than through a workflow file. `tests/sparse-checkout-
closure/run.sh` covers the end-to-end path; what this covers is the reading itself, on the `with:`
mappings and job documents the readers actually receive, including the shapes no live workflow has.

Exit 0 = every case held. Exit 1 = a case did not. Exit 2 = the module could not be imported at all,
which is a tooling failure and is never reported as a pass (#266).
"""

from __future__ import annotations

from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts"))

try:
    from lib.sparse import (
        CHECKOUT_ACTION,
        SparseRefusal,
        checkout_steps,
        cone_mode_of,
        patterns_of,
        repository_matches,
    )
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

# ---- READING 4: WHICH STEPS ARE SUBJECTS AT ALL (.github#1553) -----------------------------------
# The qualification underneath both callers' filters. Two facts and no others: the step `uses:`
# `actions/checkout`, and its `with:` names a non-empty `repository:`. Both are read the way GitHub
# resolves them, and BOTH are places `sparse_set.py`'s private copy was wrong.


def _wf(*steps: dict) -> dict:
    """A parsed workflow document carrying these steps in one job, `a`.

    Deliberately not parameterised by job name: the only leg that needs more than one job is the
    document-order leg below, and it writes its document out in full because the ORDER is what it is
    asserting — a builder that hid the job mapping would be hiding the thing under test.
    """
    return {"name": "x", "on": ["push"], "jobs": {"a": {"runs-on": "ubuntu-latest", "steps": list(steps)}}}


def selects(name: str, document: object, expected: list[tuple[str, str]]) -> None:
    """`checkout_steps` qualifies exactly these (job id, repository) pairs, in document order."""
    try:
        got = checkout_steps(document)
    except Exception as error:  # noqa: BLE001 — a selector that raises on a document is a failure.
        bad(name, f"raised instead of answering: {error!r}")
        return
    if [(step.job_id, step.repository) for step in got] == expected:
        ok(name)
    else:
        bad(name, f"expected {expected!r}, got {[(s.job_id, s.repository) for s in got]!r}")


selects(
    "a plain cross-repo actions/checkout qualifies, with its job id and repository",
    _wf({"uses": "actions/checkout@v7", "with": {"repository": "FS-GG/.github", "sparse-checkout": "/scripts/"}}),
    [("a", "FS-GG/.github")],
)
# `uses:` IS READ, and casefolded with the ref stripped — every spelling below runs the real action.
for spelling in ("actions/checkout@v7", "actions/Checkout@v7", "ACTIONS/CHECKOUT", "actions/checkout@" + "d" * 40):
    selects(
        f"uses: {spelling!r} is the real action and qualifies",
        _wf({"uses": spelling, "with": {"repository": "FS-GG/.github"}}),
        [("a", "FS-GG/.github")],
    )
# THE DIVERGENCE THAT MATTERS IN THIS DIRECTION. `sparse_set.py` read no `uses:` at all, so ANY step
# whose `with:` carried the authority repository qualified — a build action, a composite action,
# anything — and the fixture would then grade a step that fetches no tree, or hard-fail on a count
# inflated by decoys. Nothing in either caller's own subject could show it.
for impostor in ("docker/build-push-action@v6", "./.github/actions/setup-policy-python", "actions/checkout-lite@v1"):
    selects(
        f"a NON-checkout step carrying repository: — {impostor!r} — does NOT qualify",
        _wf({"uses": impostor, "with": {"repository": "FS-GG/.github", "sparse-checkout": "/scripts/"}}),
        [],
    )
selects(
    "a `run:` step with no `uses:` at all does not qualify",
    _wf({"run": "git clone https://github.com/FS-GG/.github", "with": {"repository": "FS-GG/.github"}}),
    [],
)

# A checkout with NO `repository:` is the caller's OWN tree: no second repository to under-fetch.
selects(
    "a checkout with no repository: is the caller's own and does not qualify",
    _wf({"uses": "actions/checkout@v7", "with": {"sparse-checkout": "/scripts/"}}),
    [],
)
selects("a checkout with no `with:` block at all does not qualify", _wf({"uses": "actions/checkout@v7"}), [])
# `or ""`, not `str(params.get("repository", ""))`: PyYAML resolves a BARE `repository:` to None and
# `str(None)` is the four-character string "None" — non-empty, and a repository name that exists
# nowhere, so a non-emptiness test written the other way QUALIFIES this step.
#
# `sparse_set.py`'s pre-#1553 copy did spell it with `get`, and it was harmless there: it compared
# `== "FS-GG/.github"`, which "None" fails. So this leg pins the gate's spelling because that is the
# correct one, NOT because the other one was a live divergence — it was not, and saying so would be
# an overstated claim in a repo whose whole subject is claims that outlived their code.
for label, value in (("an empty string", ""), ("whitespace only", "   "), ("a BARE key (PyYAML: None)", None)):
    selects(
        f"repository: {label} names no repository and does not qualify",
        _wf({"uses": "actions/checkout@v7", "with": {"repository": value, "sparse-checkout": "/scripts/"}}),
        [],
    )

# The repository is handed back in the WORKFLOW's spelling, never casefolded: callers print it back
# to an operator, and a message that rewrites the file's own text sends someone hunting for a line
# that does not exist. Comparison is `repository_matches`'s job, asserted below.
selects(
    "the repository is returned in the file's own spelling, not casefolded",
    _wf({"uses": "actions/checkout@v7", "with": {"repository": "  fs-gg/.GitHub  "}}),
    [("a", "fs-gg/.GitHub")],
)

# Document order, across jobs and within one — `sparse_set` refuses any count but one, so a selector
# that deduplicated or reordered would change which step it grades.
selects(
    "every qualifying step is returned, in document order, across jobs",
    {
        "jobs": {
            "first": {
                "steps": [
                    {"uses": "actions/checkout@v7"},  # own repo: not a subject
                    {"uses": "actions/checkout@v7", "with": {"repository": "FS-GG/FS.GG.SDD"}},
                    {"uses": "actions/Checkout@v7", "with": {"repository": "FS-GG/.github"}},
                ]
            },
            "second": {"steps": [{"uses": "actions/checkout@v7", "with": {"repository": "FS-GG/.github"}}]},
        }
    },
    [("first", "FS-GG/FS.GG.SDD"), ("first", "FS-GG/.github"), ("second", "FS-GG/.github")],
)

# SHAPES A WORKFLOW DOES NOT HAVE contribute nothing and raise nothing. This is not a fail-open: the
# question is "which steps are subjects", and a malformed region holds no readable checkout step. The
# refusals in this module are about a step that IS a subject whose BLOCK cannot be read.
selects("a job that `uses:` a reusable workflow has no steps and contributes none", {"jobs": {"a": {"uses": "o/r/.github/workflows/w.yml@main"}}}, [])
# A STRING `steps:` would be dropped anyway — iterating it yields characters, which the
# `isinstance(step, dict)` guard below rejects — so it does not distinguish the `isinstance(steps,
# list)` guard from its absence. A NON-ITERABLE one does: without the guard this raises TypeError,
# and `selects` reports a raise as a failure. Both spellings, so the leg is load-bearing rather than
# merely true. (`sparse_set`'s pre-#1553 copy wrote `job.get("steps") or []` and DID raise here.)
selects("a `steps:` that is not a list contributes nothing", {"jobs": {"a": {"steps": "not-a-list"}}}, [])
selects("...including one that is not even iterable", {"jobs": {"a": {"steps": 5}}}, [])
selects("a step that is not a mapping contributes nothing", {"jobs": {"a": {"steps": ["bare-string", None]}}}, [])
selects("a `with:` that is not a mapping contributes nothing", _wf({"uses": "actions/checkout@v7", "with": "nope"}), [])
selects("a job that is not a mapping contributes nothing", {"jobs": {"a": "nope"}}, [])
selects("a `jobs:` that is not a mapping contributes nothing", {"jobs": ["a", "b"]}, [])
selects("a document that is not a mapping contributes nothing", "name: x", [])
selects("an empty document (PyYAML: None) contributes nothing", None, [])

# The `with:` mapping handed back is the DOCUMENT'S OWN OBJECT, asserted by identity rather than by
# `==`: two steps in one workflow can carry byte-identical `with:` blocks, so an equality check could
# not tell a selector that returned the wrong one of them from a selector that worked. Both callers go
# on to read `patterns_of`/`cone_mode_of` out of whatever object they are handed.
_clean = {"repository": "FS-GG/.github", "sparse-checkout": "/scripts/"}
_sneaky = {"repository": "FS-GG/.github", "sparse-checkout": "/scripts/"}
_doc = _wf({"uses": "actions/checkout@v7", "with": _clean}, {"uses": "actions/checkout@v7", "with": _sneaky})
_got = [step.params for step in checkout_steps(_doc)]
if len(_got) == 2 and _got[0] is _clean and _got[1] is _sneaky:
    ok("the `with:` mapping is the document's own object, so identical blocks stay distinguishable")
else:
    bad("the `with:` mapping is the document's own object, so identical blocks stay distinguishable")

# ---- READING 4b: `repository_matches` — GitHub resolves `repository:` WITHOUT REGARD TO CASE -------
# THE SECOND DIVERGENCE. `sparse_set.py` compared with `==`, so the case-variant spelling the closure
# gate's `repo-casing` fixture leg asserts must still resolve (`repository: fs-gg/.GitHub`) yielded
# ZERO authority steps and a hard failure on the count — a no-verdict over a workflow that works.
AUTHORITY = "FS-GG/.github"
for declared in ("FS-GG/.github", "fs-gg/.github", "fs-gg/.GitHub", "FS-GG/.GITHUB", "  FS-GG/.github  "):
    if repository_matches(declared, AUTHORITY):
        ok(f"repository: {declared!r} resolves to {AUTHORITY} — GitHub ignores case, so this must too")
    else:
        bad(f"repository: {declared!r} resolves to {AUTHORITY} — GitHub ignores case, so this must too")
for declared in ("FS-GG/FS.GG.SDD", "FS-GG/.github-actions", "other/.github"):
    if repository_matches(declared, AUTHORITY):
        bad(f"repository: {declared!r} is a DIFFERENT repository and must not match {AUTHORITY}")
    else:
        ok(f"repository: {declared!r} is a DIFFERENT repository and must not match {AUTHORITY}")
# An EMPTY side never matches, including empty against empty. `origin_repository()` returns None for a
# tree whose remote cannot be read, and the gate asks this question with that None: "I cannot tell
# which repository is ours" must not resolve rule (4) as if it had said yes (#266).
for left, right, why in (
    (None, AUTHORITY, "an unreadable `repository:` matches nothing"),
    (AUTHORITY, None, "an unreadable `ours` (no origin remote) answers NO, never yes"),
    ("", "", "empty against empty is still not a repository"),
    ("   ", "   ", "whitespace against whitespace is still not a repository"),
):
    if repository_matches(left, right):
        bad(f"{why} ({left!r} vs {right!r})")
    else:
        ok(f"{why} ({left!r} vs {right!r})")

# The action literal is the shared module's, so the gate and sparse_set cannot disagree about what
# they are looking for. Asserted as a value, because a constant nobody reads is one that can rot.
if CHECKOUT_ACTION == "actions/checkout":
    ok("CHECKOUT_ACTION is the action's published owner/name, shared by both callers")
else:
    bad("CHECKOUT_ACTION is the action's published owner/name, shared by both callers", repr(CHECKOUT_ACTION))

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
