"""The anchor-checked mutation harness (.github#1808, applied by #1810).

WHY THIS EXISTS. "This gate has never once been red" is equally consistent with *nothing was ever
wrong* and *it cannot fire*, and `.github#1582` measured 30 gates in that state across eight repos.
No amount of reading separates the two — every one of the ten dead checks found on 2026-07-27/28
(`#1644`, `#1715`, `FS.GG.Audio#212`, `FS.GG.Rendering#1120`, `#1710`, `#1768`, `#1772`, `#1740`,
`#1784`, `#1799`) was found by BREAKING the subject and watching, and several were in code written by
people who were specifically looking for that defect. This module is that method, generalised: break
the thing a gate claims to protect, run the gate, and record whether it fired.

THE FOUR-POINT SPECIFICATION, AND WHAT EACH POINT COST. Four harnesses shipped an anchor check with a
defect in it before this one, and every clause below is one of their bills:

1. **An unmutated CONTROL that must pass.** `#1582`'s harness reported all eleven of its mutants
   "caught" while NONE had executed a line — they died at import, and a non-zero exit read as a catch.
   :func:`adjudicate` runs the pristine command first and refuses to grade anything whose control is
   not green (:attr:`Verdict.NOT_MEASURED`).
2. **``NOT MEASURED`` distinct from both ``FAIL`` and ``PASS``, end to end.** `#1784` reported "1 leg
   fired" when its anchor had silently failed to match. :class:`Verdict` has three members, the JSON
   carries three counters, and :func:`exit_code` maps the third to its own code — there is no path
   that collapses it into either neighbour.
3. **Anchors independent of the guard under test.** `#1794` used the guard's own output as its anchor,
   so mutating the guard removed the anchor and the leg reported ``NOT MEASURED`` exactly when it
   should have reported ``FAIL``. Here the anchor names the file that PRINTS it
   (:attr:`Anchor.produced_by`), the loader REFUSES a spec whose producer is the mutation target, and
   :func:`adjudicate` hashes that producer either side of the mutation and refuses to grade if its
   bytes moved. Independence is checked mechanically, not asserted in a comment.
4. **A leg that mutates the FIXTURE.** `#1794`'s M10. Without it the refutation helper is an untested
   guard guarding against untested guards. That leg lives in ``tests/gate-mutation/selftest.py``,
   which is this module's own negative control — see :func:`adjudicate`'s contract below.

THE CRASH/FINDING ASYMMETRY, WHICH IS POINT 1 ONE LEVEL DOWN. `.github#1812` records that a run
concluding ``failure`` may be a CRASH rather than a finding, so `#1582`'s EXERCISED count is an upper
bound. The same asymmetry applies to a mutant: **a gate that errored is not a gate that fired.** A
non-zero exit is therefore never sufficient here. The anchor must match, and a
:class:`Discriminator` must positively identify the non-zero exit as a verdict about the subject —
a failed assertion in the fixture's own tally, or the typed ``FINDING`` code from
:class:`lib.gate.ExitCode`. Anything else is :attr:`Verdict.NOT_MEASURED`, and says why.

WHAT THIS MODULE WILL NOT DO. It does not delete, disable or weaken anything. It returns a verdict;
removal is a separate, human decision with its own justification (`#1810` AC3). "Mutating it was
hard" is reported as :attr:`Verdict.NOT_MEASURED` with a reason — never as a licence to remove.
"""

from __future__ import annotations

import hashlib
import os
import re
import subprocess
import time
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Iterable, Sequence

__all__ = [
    "Verdict",
    "Discriminator",
    "Anchor",
    "Mutation",
    "LegResult",
    "SpecError",
    "adjudicate",
    "load_specs",
    "exit_code",
    "tally_counters",
]


class SpecError(Exception):
    """A mutation spec this harness REFUSES to run, because running it could not produce a measurement.

    Raised at load time, never at grading time. The distinction matters: a spec that violates point 3
    of the specification (an anchor the mutation could delete) does not yield a cautious
    ``NOT MEASURED`` — it yields a CONFIDENT WRONG ANSWER, because the anchor goes missing precisely
    on the mutants that prove the gate dead. `#1794` shipped exactly that and the leg read
    ``NOT MEASURED`` where the truth was ``FAIL``. So it is refused up front rather than graded
    cautiously.
    """


class Verdict(str, Enum):
    """The three answers, and there are exactly three (specification point 2).

    ``str``-valued so a verdict serialises to its own name in JSON with no encoder, and so a report
    template cannot accidentally print ``Verdict.DECORATIVE`` where it meant ``DECORATIVE``.
    """

    JUSTIFIED = "JUSTIFIED"
    """The gate FIRED on the mutant — it demonstrably still protects what it claims to. Keep it, and
    record the verdict so nobody re-asks."""

    DECORATIVE = "DECORATIVE"
    """The gate stayed GREEN on a mutant that broke the very thing it claims to protect. It cannot
    fire. Repair it or remove it — with this verdict as the justification."""

    NOT_MEASURED = "NOT_MEASURED"
    """No measurement was obtained. **This is not a pass and it is not a failure** (`#266`: "I could
    not evaluate this" is NEVER "I evaluated it and it passed"). A control that would not go green, a
    mutation that did not apply, a mutant that crashed before reaching the guard, an anchor that never
    matched — all land here, each with its own reason, and NONE of them is grounds for removing a
    gate."""


class Discriminator(str, Enum):
    """How a non-zero exit is positively identified as a FINDING rather than a crash (`#1812`).

    The harness never treats "exited non-zero" as "fired". It asks one of these.
    """

    TALLY = "tally"
    """The command prints ``<n> passed, <m> failed``. FIRED iff ``m > 0``. This is the shape all ~17
    of this repo's ``tests/*/run.sh`` fixtures already emit, and the count comes from the fixture's own
    bookkeeping — which the mutation does not touch."""

    EXIT_CONTRACT = "exit-contract"
    """The command honours :class:`lib.gate.ExitCode`. FIRED iff the code is exactly ``1``
    (``FINDING``). ``2``/``3`` are no-verdict and grade as :attr:`Verdict.NOT_MEASURED`, which is the
    whole reason that contract exists."""

    TERMINAL = "terminal"
    """The command prints a TERMINAL REPORT LINE but no tally, so the exit code is its verdict.

    Two of this repo's fixtures are like this — ``tests/ignored-author-coherence/run.sh`` ends
    ``ignored-author-coherence fixture: OK`` or ``… : <n> FAILURE(S)``, and
    ``tests/gate-harness/run.sh`` ends ``gate-harness selftest OK …`` or ``… FAILED — <n> check(s)``.
    Neither counts assertions, so :attr:`TALLY` cannot grade them, and neither honours
    :class:`lib.gate.ExitCode`, so :attr:`EXIT_CONTRACT` cannot either. Inventing a tally for them
    would be worse than saying so.

    THE ANCHOR IS WHAT MAKES THIS SAFE, and it is doing more work here than anywhere else. The exit
    code alone is exactly what `#1812` warns against — a crash and a finding share it. So the anchor
    must match the fixture's TERMINAL line, which is printed only after every leg has run: matching it
    proves the run reached the end, and only then is the non-zero exit read as that fixture's own
    verdict rather than as a death. A leg using this discriminator whose anchor matches something
    printed EARLY is measuring nothing, and the spec loader cannot detect that for you — choose the
    last line the fixture prints."""


@dataclass(frozen=True)
class Anchor:
    """A witness that the run REACHED and COMPLETED the guard, emitted by something the mutation cannot edit.

    :attr:`pattern` is searched against the run's combined stdout+stderr. :attr:`produced_by` is the
    repo-relative path of the file that prints it, and it is the load-bearing field: the loader refuses
    a spec where it equals the mutation target, and :func:`adjudicate` hashes it either side of the
    mutation and refuses to grade if the bytes moved. That is specification point 3 enforced
    mechanically — an anchor that the guard's own code emits is not an anchor, it is the guard
    vouching for itself.
    """

    pattern: str
    produced_by: str

    def search(self, text: str) -> re.Match[str] | None:
        return re.search(self.pattern, text, re.MULTILINE)


@dataclass(frozen=True)
class Mutation:
    """One adjudication: break ``target``, run ``command``, and see whether the gate says so.

    ``find``/``replace`` is an exact textual substitution, not a patch — ``occurrences`` states how
    many times ``find`` must appear, and a count that does not match is a
    :attr:`Verdict.NOT_MEASURED` rather than a silent partial edit. ``breaks`` is prose naming what
    protection the mutation removes; it is required, because a mutation nobody can explain is not
    evidence about anything.
    """

    id: str
    gate: str
    command: Sequence[str]
    target: str
    find: str
    replace: str
    breaks: str
    anchor: Anchor
    discriminator: Discriminator = Discriminator.TALLY
    occurrences: int = 1
    timeout: int = 600
    note: str = ""


@dataclass
class LegResult:
    """One mutation's outcome, carrying every fact a reader needs to disbelieve it."""

    mutation: Mutation
    verdict: Verdict
    reason: str
    control_rc: int | None = None
    mutant_rc: int | None = None
    control_anchor: str | None = None
    mutant_anchor: str | None = None
    seconds: float = 0.0

    def to_json(self) -> dict:
        return {
            "id": self.mutation.id,
            "gate": self.mutation.gate,
            "verdict": self.verdict.value,
            "reason": self.reason,
            "target": self.mutation.target,
            "breaks": self.mutation.breaks,
            "command": list(self.mutation.command),
            "discriminator": self.mutation.discriminator.value,
            "control_rc": self.control_rc,
            "mutant_rc": self.mutant_rc,
            "control_anchor": self.control_anchor,
            "mutant_anchor": self.mutant_anchor,
            "seconds": round(self.seconds, 1),
            "note": self.mutation.note,
        }


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _run(cmd: Sequence[str], root: Path, timeout: int) -> tuple[int, str]:
    """Run ``cmd`` from ``root``, returning ``(rc, combined output)``.

    A timeout is not a verdict, so it surfaces as a distinctive negative code the grader routes to
    :attr:`Verdict.NOT_MEASURED` rather than as "the gate failed".
    """
    try:
        p = subprocess.run(
            list(cmd),
            cwd=str(root),
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired as e:
        got = (e.stdout or "") + (e.stderr or "")
        if isinstance(got, bytes):  # pragma: no cover - text=True keeps this str
            got = got.decode("utf-8", "replace")
        return -1, got + f"\n[harness] TIMED OUT after {timeout}s"
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def _fired(m: Mutation, rc: int, anchor_text: str) -> tuple[bool | None, str]:
    """Did this non-zero exit represent a FINDING about the subject, or a crash (`#1812`)?

    Returns ``(True, why)`` for a finding, ``(False, why)`` for a clean pass, and ``(None, why)`` when
    the evidence does not let us tell — which is the answer that must never be rounded to either
    neighbour.

    THE TALLY IS READ OUT OF THE ANCHOR'S OWN MATCH, not out of the raw output, and that is a bug fix
    rather than a nicety. Reading the raw output meant ``re.search`` took the FIRST tally it found,
    while the anchor had matched some other one — measured on ``tests/skill-union/run.sh``, which
    prints four (``materializes-when conformance``, ``SkillMirror conformance``, ``skill
    current-truth``, and the authoritative ``skill-union fixture``). The leg reported a mutant that
    exited 1 alongside a tally reading ``0 failed`` and graded NOT_MEASURED — a correct refusal, but
    of a question the harness had asked itself wrong. One source of truth removes the disagreement:
    a spec's anchor must MATCH the tally it wants believed, so a fixture with several must name the
    final one.
    """
    if m.discriminator is Discriminator.TALLY:
        hit = re.search(r"(\d+)\s+passed,\s+(\d+)\s+failed", anchor_text)
        if hit is None:
            return None, (
                f"the anchor matched {anchor_text!r}, which carries no `<n> passed, <m> failed` tally "
                f"— a `tally` discriminator needs the anchor to match the tally it grades"
            )
        passed, failed = int(hit.group(1)), int(hit.group(2))
        if failed > 0:
            return True, f"the fixture's own tally reports {failed} failed assertion(s) ({passed} passed)"
        return False, f"the fixture's own tally reports 0 failed assertions ({passed} passed)"
    if m.discriminator is Discriminator.TERMINAL:
        # The anchor already proved the run reached its terminal report; the exit code is that
        # report's verdict, not a death mid-run.
        if rc == 0:
            return False, f"the run reached its terminal report ({anchor_text!r}) and exited 0"
        return True, (
            f"the run reached its terminal report ({anchor_text!r}) and then exited {rc} — a "
            f"completed fixture reporting a failure, not a crash"
        )
    # EXIT_CONTRACT
    if rc == 1:
        return True, "exited 1 — lib.gate.ExitCode.FINDING, a verdict about the subject"
    if rc == 0:
        return False, "exited 0 — lib.gate.ExitCode.OK"
    if rc in (2, 3):
        return None, f"exited {rc} — lib.gate.ExitCode.NO_VERDICT_*, which is 'could not look', not 'fired'"
    return None, f"exited {rc}, which is outside the lib.gate.ExitCode contract — a crash, not a verdict"


def adjudicate(m: Mutation, root: Path) -> LegResult:
    """Run one mutation to a verdict, and leave the tree exactly as it was found.

    THE ORDER IS THE SPECIFICATION, and each step can only ever downgrade to
    :attr:`Verdict.NOT_MEASURED` — there is no step that can manufacture a
    :attr:`Verdict.JUSTIFIED` out of an absence:

    1. **CONTROL** — run the pristine command. Not green? Nothing measured; every "catch" after this
       would be an artefact of a tree that was already broken (`#1582`).
    2. **CONTROL ANCHOR** — the anchor must match on that green run. An anchor that never matches at
       all is `#1784`'s silent failure, and it would make every later mismatch unreadable.
    3. **APPLY** — ``find`` must occur exactly ``occurrences`` times and the bytes must actually move.
       A mutation that did not apply is not a gate that held.
    4. **PRODUCER INTEGRITY** — the anchor's producing file must be byte-identical after the edit.
       This is point 3, mechanised: if the mutation could have touched the anchor, the anchor cannot
       testify about the mutation (`#1794`).
    5. **MUTANT** — run again. The anchor must match, or the run never reached the guard and the
       result is unmeasured regardless of exit code (`#1582`'s eleven import deaths).
    6. **DISCRIMINATE** — a non-zero exit becomes :attr:`Verdict.JUSTIFIED` only if
       :func:`_fired` positively identifies it as a finding (`#1812`).
    7. **RESTORE** — always, via ``finally``, and the restore is verified by hash before the result is
       returned. A harness that can leave a mutated tree behind is a hazard, not a measurement.
    """
    target = root / m.target
    producer = root / m.anchor.produced_by
    started = time.monotonic()

    def done(v: Verdict, reason: str, **kw) -> LegResult:
        return LegResult(mutation=m, verdict=v, reason=reason, seconds=time.monotonic() - started, **kw)

    if not target.is_file():
        return done(Verdict.NOT_MEASURED, f"mutation target {m.target} does not exist")
    if not producer.is_file():
        return done(Verdict.NOT_MEASURED, f"anchor producer {m.anchor.produced_by} does not exist")

    original = target.read_bytes()
    producer_before = _sha(producer)

    try:
        # 1. CONTROL — the unmutated tree must be green, or nothing below means anything.
        c_rc, c_out = _run(m.command, root, m.timeout)
        if c_rc != 0:
            return done(
                Verdict.NOT_MEASURED,
                f"the UNMUTATED control exited {c_rc}, not 0 — the tree was already failing, so a "
                f"mutant that also fails proves nothing (#1582)",
                control_rc=c_rc,
            )

        # 2. CONTROL ANCHOR — an anchor that never matched cannot report a later absence (#1784).
        c_hit = m.anchor.search(c_out)
        if c_hit is None:
            return done(
                Verdict.NOT_MEASURED,
                f"the anchor /{m.anchor.pattern}/ did not match the GREEN control run — the anchor is "
                f"wrong, so no run can be checked against it (#1784)",
                control_rc=c_rc,
            )
        c_anchor = c_hit.group(0).strip()

        # 3. APPLY — an exact, counted substitution or nothing at all.
        text = original.decode("utf-8")
        seen = text.count(m.find)
        if seen != m.occurrences:
            return done(
                Verdict.NOT_MEASURED,
                f"the mutation did not apply: expected {m.occurrences} occurrence(s) of the `find` "
                f"text in {m.target}, found {seen} — the gate was never actually challenged",
                control_rc=c_rc,
                control_anchor=c_anchor,
            )
        mutated = text.replace(m.find, m.replace)
        if mutated == text:
            return done(
                Verdict.NOT_MEASURED,
                f"the mutation was a NO-OP — {m.target} is byte-identical after the substitution",
                control_rc=c_rc,
                control_anchor=c_anchor,
            )
        target.write_text(mutated, encoding="utf-8")

        # 4. PRODUCER INTEGRITY — specification point 3, checked rather than promised (#1794).
        if _sha(producer) != producer_before:
            return done(
                Verdict.NOT_MEASURED,
                f"the mutation changed {m.anchor.produced_by}, which is the anchor's PRODUCER — an "
                f"anchor the mutation can edit cannot testify about that mutation (#1794)",
                control_rc=c_rc,
                control_anchor=c_anchor,
            )

        # 5. MUTANT.
        x_rc, x_out = _run(m.command, root, m.timeout)
        x_hit = m.anchor.search(x_out)
        if x_hit is None:
            return done(
                Verdict.NOT_MEASURED,
                f"the mutant run exited {x_rc} but the anchor /{m.anchor.pattern}/ did not match, so "
                f"the run never reached the guard — a gate that ERRORED is not a gate that FIRED "
                f"(#1812). This is not evidence the gate is dead, and not evidence it is alive.",
                control_rc=c_rc,
                mutant_rc=x_rc,
                control_anchor=c_anchor,
            )
        x_anchor = x_hit.group(0).strip()

        # 6. DISCRIMINATE.
        fired, why = _fired(m, x_rc, x_anchor)
        common = dict(control_rc=c_rc, mutant_rc=x_rc, control_anchor=c_anchor, mutant_anchor=x_anchor)
        if fired is None:
            return done(Verdict.NOT_MEASURED, f"could not classify the mutant run: {why}", **common)
        if fired:
            if x_rc == 0:
                return done(
                    Verdict.NOT_MEASURED,
                    f"contradictory: {why}, but the command still exited 0 — the gate reported a "
                    f"finding and passed anyway, which is a defect in the gate's own reporting",
                    **common,
                )
            return done(Verdict.JUSTIFIED, f"the gate fired on the mutant — {why}", **common)
        if x_rc == 0:
            return done(
                Verdict.DECORATIVE,
                f"the gate stayed GREEN after `{m.breaks}` — {why}. It cannot fire on the defect it "
                f"claims to protect against.",
                **common,
            )
        return done(
            Verdict.NOT_MEASURED,
            f"contradictory: the command exited {x_rc} but {why} — the exit code and the run's own "
            f"report disagree, so neither can be believed",
            **common,
        )
    finally:
        # 7. RESTORE, unconditionally, and prove it took.
        target.write_bytes(original)
        if target.read_bytes() != original:  # pragma: no cover - filesystem failure
            raise SpecError(f"FAILED TO RESTORE {m.target} — the working tree is left mutated")


def load_specs(path: Path, root: Path) -> list[Mutation]:
    """Parse and VALIDATE a spec file, refusing anything that could not yield a measurement.

    The refusals are the specification, enforced before a single command runs:

    * an anchor whose :attr:`Anchor.produced_by` IS the mutation target (point 3 — `#1794`);
    * a ``find`` string that is empty, or identical to its ``replace`` (a no-op dressed as a mutation);
    * a missing ``breaks`` sentence — a mutation nobody can explain is not evidence;
    * a duplicate ``id`` — two legs sharing a name make a report unreadable.

    Refusing at load time rather than grading cautiously at run time is deliberate: these defects do
    not produce a hedged answer, they produce a confidently wrong one.
    """
    import yaml

    doc = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(doc, dict) or not isinstance(doc.get("mutations"), list):
        raise SpecError(f"{path}: expected a mapping with a `mutations:` list")

    out: list[Mutation] = []
    seen_ids: set[str] = set()
    for i, raw in enumerate(doc["mutations"]):
        where = f"{path}: mutations[{i}]"
        if not isinstance(raw, dict):
            raise SpecError(f"{where}: not a mapping")
        missing = [k for k in ("id", "gate", "command", "target", "find", "replace", "breaks", "anchor") if k not in raw]
        if missing:
            raise SpecError(f"{where}: missing required key(s): {', '.join(missing)}")
        mid = str(raw["id"])
        if mid in seen_ids:
            raise SpecError(f"{where}: duplicate mutation id {mid!r}")
        seen_ids.add(mid)
        if not str(raw["breaks"]).strip():
            raise SpecError(f"{where} ({mid}): `breaks:` is empty — name what protection this removes")
        if not str(raw["find"]):
            raise SpecError(f"{where} ({mid}): `find:` is empty")
        if str(raw["find"]) == str(raw["replace"]):
            raise SpecError(f"{where} ({mid}): `find:` equals `replace:` — that is a no-op, not a mutation")

        anc = raw["anchor"]
        if not isinstance(anc, dict) or "pattern" not in anc or "produced_by" not in anc:
            raise SpecError(f"{where} ({mid}): `anchor:` needs both `pattern:` and `produced_by:`")
        if os.path.normpath(str(anc["produced_by"])) == os.path.normpath(str(raw["target"])):
            raise SpecError(
                f"{where} ({mid}): the anchor is produced by {anc['produced_by']}, which is the very "
                f"file the mutation edits. A guard cannot be its own anchor: mutating it deletes the "
                f"anchor, so the leg reports NOT_MEASURED exactly when it should report a finding "
                f"(#1794). Point an anchor at a file this mutation does not touch."
            )
        try:
            re.compile(str(anc["pattern"]))
        except re.error as e:
            raise SpecError(f"{where} ({mid}): anchor pattern is not a valid regex — {e}") from e

        try:
            disc = Discriminator(str(raw.get("discriminator", "tally")))
        except ValueError as e:
            raise SpecError(
                f"{where} ({mid}): unknown discriminator {raw.get('discriminator')!r}; "
                f"expected one of {[d.value for d in Discriminator]}"
            ) from e

        out.append(
            Mutation(
                id=mid,
                gate=str(raw["gate"]),
                command=[str(x) for x in raw["command"]],
                target=str(raw["target"]),
                find=str(raw["find"]),
                replace=str(raw["replace"]),
                breaks=str(raw["breaks"]).strip(),
                anchor=Anchor(pattern=str(anc["pattern"]), produced_by=str(anc["produced_by"])),
                discriminator=disc,
                occurrences=int(raw.get("occurrences", 1)),
                timeout=int(raw.get("timeout", 600)),
                note=str(raw.get("note", "")).strip(),
            )
        )
    if not out:
        raise SpecError(f"{path}: contains no mutations — an empty corpus is not a clean sweep (#266)")
    return out


def tally_counters(results: Iterable[LegResult]) -> dict[str, int]:
    """The three counters, always all three, even at zero (specification point 2).

    Emitting only the non-zero buckets is how ``NOT MEASURED`` disappears from a summary and a reader
    infers "everything passed" from a line that never mentioned it.
    """
    counts = {v.value: 0 for v in Verdict}
    for r in results:
        counts[r.verdict.value] += 1
    return counts


def exit_code(results: Sequence[LegResult]) -> int:
    """Map a sweep to :class:`lib.gate.ExitCode`, keeping all three verdicts separable from the shell.

    * any :attr:`Verdict.DECORATIVE` → ``1`` (``FINDING``): a gate that cannot fire is a real,
      actionable finding about this repo's audit fabric.
    * else any :attr:`Verdict.NOT_MEASURED` → ``3`` (``NO_VERDICT_PERMANENT``): we did not establish
      the subject. Not green — `#266`.
    * else ``0``: every leg ran, and every gate fired.
    """
    counts = tally_counters(results)
    if counts[Verdict.DECORATIVE.value]:
        return 1
    if counts[Verdict.NOT_MEASURED.value]:
        return 3
    return 0
