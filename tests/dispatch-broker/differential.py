#!/usr/bin/env python3
"""The transcription differential: the broker's opkey domain against `Operation.compose`, EXECUTED.

WHY THIS EXISTS
---------------
`.github/workflows/fsgg-dispatch-broker.yml` carries a second implementation of
`src/FS.GG.Coord.Core/Operation.fs` — its own header says so at `:204` ("transcribed from
`Operation.preimage`"). Two implementations of one digest that must agree byte-for-byte or NO DEDUPE
EVER MATCHES, pinned only by a `grep` for the separator literal, is the shape the board analyst named
when it reopened `.github#2720`:

    "Either have the broker call the engine, or add a test that fails when the transcription and
     `Operation.preimage` disagree. A grep-based constant leg is not that test."

This is that test. It executes BOTH implementations over one corpus and compares three things: the
accept/refuse verdict, the 64-hex digest wherever both accept, and the refusal class.

NEITHER SIDE IS RE-TYPED, AND THAT IS THE WHOLE POINT
-----------------------------------------------------
* The engine side is `FS.GG.Coord.Core.dll` — the shipped assembly, called through the public
  `Operation.compose` in `Operation.fsi`, by `engine_opkey.fsx`.
* The broker side is the `run:` block LIFTED OUT OF THE REAL WORKFLOW by `yaml.safe_load`, its embedded
  `python3` heredoc extracted and `exec`'d. A re-typed copy would keep agreeing after somebody edited
  the workflow, which rebuilds the fails-open defect this slice exists to close.

WHY THE BROKER RUNS IN-PROCESS RATHER THAN AS A SUBPROCESS
----------------------------------------------------------
Two of the domain's six refusals are about ill-formed text. An unpaired surrogate is by construction
NOT representable in UTF-8, so it cannot survive `execve`'s `char **envp` — an environment variable
cannot carry the corpus faithfully, and a harness that let the OS repair the input would make the
surrogate legs vacuous. `exec`ing the same extracted source in-process with an exact-code-unit
environment is the only way to drive the shipped code over the whole domain.

That trade is not taken on trust: `--cross-check` runs every ENV-EXPRESSIBLE vector through the real
`bash`/`python3` subprocess boundary as well and refuses on any disagreement with the in-process
answer, so the in-process harness is itself measured rather than assumed. Two methods, because this
session measured twenty-two instrument faults and most were caught only by a second one.

WHY THE PRESENTED OPKEY IS DELIBERATELY WRONG
---------------------------------------------
Every vector is driven with `OPKEY = "0" * 64`. The broker's step 3 then always refuses, and its
refusal PRINTS the key it recomputed — so the digest is observed from the real script with no network,
no stub world, and no reachable `gh` call. Step 3 sits after the domain checks (step 1) and the closed
vocabulary (step 2) and before the first REST read (step 5), which is exactly the window this
differential needs. `subprocess.run` is stubbed to raise for the same reason: if a leg ever reaches the
network, that is a harness defect and must be loud rather than slow.

EXIT CODES
----------
0  the two implementations agree on every vector in every compared dimension
1  a DISAGREEMENT — the finding this file exists to make
3  NO VERDICT: the corpus could not be driven at all (the engine would not build, the `run:` block
   could not be extracted, a refusal could not be classified). Never a pass. A gate that cannot reach
   its subject is not a gate (#266).
"""

from __future__ import annotations

import argparse
import contextlib
import io
import os
import re
import subprocess
import sys
import tempfile

# --------------------------------------------------------------------------------------------------
# The corpus.
#
# Each vector is (id, item, generation, receiver, op-kind, payload, expectation). The expectation is
# what the BROKER should do, and it exists because the broker is deliberately NARROWER than the engine
# on exactly one axis — it brokers `dispatch:` only — so `merge` and `publish:` must be asserted as an
# intended narrowing rather than silently scored as a disagreement.
#
#   "digest"   both admit; the recomputed key must match the engine's to all 64 hex characters
#   "refuse"   the engine refuses; the broker must refuse too, for a class the engine also holds
#   "narrowed" the engine admits, and the broker refuses BY NAME because this broker does not carry
#              that operation. Asserted, not tolerated.
#
# The boundary cases below are chosen where the two languages' primitives are NOT obviously the same
# function: `String.IsNullOrWhiteSpace`/`Char.IsControl`/`Char.IsAsciiDigit` against `str.strip()`/
# `unicodedata.category(...) == "Cc"`/`"0" <= ch <= "9"`. Each was measured, not assumed.
# --------------------------------------------------------------------------------------------------

GOOD_ITEM = "FS-GG/FS.GG.Net#58"
GOOD_GEN = "5177045416"
GOOD_RECV = "FS-GG/FS.GG.Net"

CORPUS = [
    # ---- both admit: the digest must agree exactly ----
    ("a01 the canonical vector", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "fs-gg-effect", "digest"),
    ("a02 a well-formed astral pair in the payload is ADMITTED by both",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "effect-\U0001D11E", "digest"),
    ("a03 an interior U+2028 (Zl, not Cc) is ADMITTED by both",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\u2028b", "digest"),
    ("a04 an interior U+2029 (Zp, not Cc) is ADMITTED by both",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\u2029b", "digest"),
    ("a05 an interior U+00A0 (Zs, not Cc) is ADMITTED by both",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\u00a0b", "digest"),
    ("a06 an interior U+200B (Cf, neither blank nor control) is ADMITTED by both",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\u200bb", "digest"),
    ("r33 an INTERIOR U+001C is `control` to BOTH — `str.strip()` only ever reaches the ends",
     "FS-GG/a\u001cb#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    # The vector that EXERCISES `ALLOWED_CLASS_SWAPS`. A component that is NOTHING BUT U+001C strips
    # to empty in Python (so the broker says `blank`) and is not whitespace in .NET (so the engine
    # says `control`). It is the one measured place where the two domains refuse for DIFFERENT
    # stated reasons. Without this vector the allowlist would be an untested tolerance — a hole
    # wearing a comment — and a later reader could not tell whether it described a difference that
    # had been measured or one that had been imagined.
    ("r34 a SOLE U+001C is `blank` to the broker and `control` to the engine, and BOTH refuse",
     "\u001c", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("a08 a colon inside the event-type payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a:b", "digest"),
    ("a09 a very long payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "e" * 512, "digest"),
    ("a10 a long generation", GOOD_ITEM, "9" * 30, GOOD_RECV, "dispatch", "e", "digest"),
    ("a11 non-ASCII in every component at once",
     "FS-GG/rép☃#12", "12", "FS-GG/rép☃", "dispatch", "événement", "digest"),
    ("a12 a payload that itself spells another operation", GOOD_ITEM, GOOD_GEN, GOOD_RECV,
     "dispatch", "merge", "digest"),

    # ---- the engine admits, the broker deliberately does not ----
    ("n01 op=merge is a whole-key-valid operation this broker does not carry",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "merge", "", "narrowed"),
    ("n02 op=publish:<package> is a whole-key-valid operation this broker does not carry",
     GOOD_ITEM, GOOD_GEN, GOOD_RECV, "publish", "FS.GG.Contracts", "narrowed"),

    # ---- item shape ----
    ("r01 the board's <repo>#N shorthand", ".github#2720", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r02 an item with no owner", "FS-GG#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r03 an item with two slashes", "FS-GG/a/b#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r04 an item with two hashes", "FS-GG/r#1#2", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r05 an item with an empty repo half", "FS-GG/#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r06 an item whose number has a leading zero", "FS-GG/r#01", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r07 an item with a space in the owner", "FS GG/r#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r08 a blank item", "", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),

    # ---- generation shape ----
    ("r09 a leading-zero generation keys one tenancy two ways", GOOD_ITEM, "0012", GOOD_RECV, "dispatch", "e", "refuse"),
    ("r10 the engine's `released` sentinel is not a generation", GOOD_ITEM, "released", GOOD_RECV, "dispatch", "e", "refuse"),
    ("r11 generation zero", GOOD_ITEM, "0", GOOD_RECV, "dispatch", "e", "refuse"),
    ("r12 an Arabic-Indic digit generation is not an ASCII digit",
     GOOD_ITEM, "\u0661\u0662", GOOD_RECV, "dispatch", "e", "refuse"),
    ("r13 a fullwidth digit generation is not an ASCII digit",
     GOOD_ITEM, "\uff11\uff12", GOOD_RECV, "dispatch", "e", "refuse"),
    ("r14 a signed generation", GOOD_ITEM, "+12", GOOD_RECV, "dispatch", "e", "refuse"),
    ("r15 a blank generation", GOOD_ITEM, "", GOOD_RECV, "dispatch", "e", "refuse"),
    # Reached by `check_corpus` and not by inspiration: the corpus self-check refused until this
    # vector existed, because no other generation vector carries a control character.
    ("r35 a tab in the generation", GOOD_ITEM, "1\t2", GOOD_RECV, "dispatch", "e", "refuse"),

    # ---- receiver shape ----
    ("r16 a receiver spelled owner/repo#N", GOOD_ITEM, GOOD_GEN, "FS-GG/FS.GG.Net#1", "dispatch", "e", "refuse"),
    ("r17 a receiver with no slash", GOOD_ITEM, GOOD_GEN, "FS.GG.Net", "dispatch", "e", "refuse"),
    ("r18 a receiver with an empty half", GOOD_ITEM, GOOD_GEN, "FS-GG/", "dispatch", "e", "refuse"),
    ("r19 a blank receiver", GOOD_ITEM, GOOD_GEN, "", "dispatch", "e", "refuse"),

    # ---- the separator's own domain: no component may carry a control character ----
    ("r20 a literal newline in the item would forge an ambiguous pre-image",
     "FS-GG/a\nb#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r21 a carriage return in the receiver", GOOD_ITEM, GOOD_GEN, "FS-GG/a\rb", "dispatch", "e", "refuse"),
    ("r22 a tab in the payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\tb", "refuse"),
    ("r23 U+007F DEL in the item", "FS-GG/a\u007fb#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r24 U+009F, a C1 control, in the item", "FS-GG/a\u009fb#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r25 U+0001 in the payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\u0001b", "refuse"),

    # ---- the encoder's own domain: UTF-8 folds every unpaired surrogate onto U+FFFD ----
    ("r26 a lone HIGH surrogate in the item", "FS-GG/r\ud800#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r27 a lone LOW surrogate in the item", "FS-GG/r\udc00#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),
    ("r28 a lone HIGH surrogate in the receiver", GOOD_ITEM, GOOD_GEN, "FS-GG/r\ud800", "dispatch", "e", "refuse"),
    ("r29 a lone LOW surrogate in the payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "a\udc00b", "refuse"),
    ("r30 a REVERSED surrogate pair — low then high — is two lone surrogates",
     "FS-GG/r\udc00\ud800#1", GOOD_GEN, GOOD_RECV, "dispatch", "e", "refuse"),

    # ---- payload emptiness, which the broker reaches by a different route than the engine ----
    ("r31 a blank dispatch payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "", "refuse"),
    ("r32 a whitespace-only dispatch payload", GOOD_ITEM, GOOD_GEN, GOOD_RECV, "dispatch", "   ", "refuse"),
]

# --------------------------------------------------------------------------------------------------
# Refusal classification.
# --------------------------------------------------------------------------------------------------

# The broker names the WHOLE wire string `op` in its step-1 domain sweep and the payload separately in
# step 2; the engine only ever validates the payload. For a `dispatch:` operation those are the same
# assertion, because the `dispatch:` prefix is fixed ASCII and can contribute no control character, no
# surrogate and no blankness of its own — so the two part names are folded onto one. This fold is
# stated rather than assumed, and it is the ONLY renaming this comparer performs.
BROKER_PARTS = {
    "item": "item",
    "generation": "generation",
    "receiver": "receiver",
    "op": "payload",
    "the dispatch:<event-type> payload": "payload",
}

BROKER_MARKERS = (
    ("is blank", "blank"),
    ("carries a control character", "control"),
    ("carries an unpaired surrogate", "surrogate"),
)

# VERDICT-PRESERVING CLASS SWAPS, DECLARED RATHER THAN DRESSED UP.
#
# `str.strip()` and `String.IsNullOrWhiteSpace` are not the same predicate. Measured on this tree:
# Python strips U+001C-U+001F (the file/group/record/unit separators) as whitespace and .NET does not,
# while both treat U+0085/U+000B/U+000C/U+00A0/U+2028/U+2029 as whitespace and neither treats U+007F or
# U+200B as whitespace. So a component consisting SOLELY of U+001C-U+001F is `blank` to the broker and
# `control` to the engine.
#
# BOTH REFUSE, which is the property the fence depends on — this is a diagnostic difference, not a
# domain difference, and it cannot admit a key on one side that the other refuses. It is listed here so
# that it is an ACKNOWLEDGED pair and not a hole: any class disagreement NOT in this list is a finding.
ALLOWED_CLASS_SWAPS = {
    ("blank", "control"),
}


# Every refusal class `Operation.Refusal` can produce. The corpus must reach all of them, and
# `check_corpus` refuses if it does not — a differential that never drives a branch of the domain
# cannot report that branch has drifted, and a corpus is exactly the thing a later edit shrinks without
# anyone noticing. This is the ABSENCE-ASSERTION discipline `single_caller.py` already applies to the
# caller scan, pointed at the domain instead.
ENGINE_CLASSES = {
    "blank:item", "blank:generation", "blank:receiver", "blank:payload",
    "control:item", "control:generation", "control:receiver", "control:payload",
    "surrogate:item", "surrogate:receiver", "surrogate:payload",
    "itemNotQualified", "receiverNotQualified", "generationNotServerAssigned",
}


def check_corpus(engine: dict) -> None:
    """The corpus must actually reach what it claims to cover, or its clean report means nothing."""
    ids = [vector[0].split()[0] for vector in CORPUS]
    if len(set(ids)) != len(ids):
        die(f"the corpus has duplicate vector ids, so answers collide: {sorted({i for i in ids if ids.count(i) > 1})}")
    reached = set()
    for vid, answer in engine.items():
        if answer[0] == "refused":
            reached.update(answer[1])
    unreached = ENGINE_CLASSES - reached
    # `surrogate:generation` is unreachable BY CONSTRUCTION and is therefore not in ENGINE_CLASSES: a
    # generation carrying any surrogate fails `serverAssignedId` too, but `wellFormed` short-circuits
    # first, so the class IS reachable — it is listed. What is NOT reachable is a well-formed astral
    # generation, which no vector needs. Stated so the omission is a decision and not an oversight.
    if unreached:
        die(
            f"the corpus never drives these engine refusal classes: {sorted(unreached)}. A differential "
            "that cannot reach a branch of the domain cannot report that branch has drifted"
        )
    kinds = {vector[6] for vector in CORPUS}
    if kinds != {"digest", "refuse", "narrowed"}:
        die(f"the corpus no longer carries all three expectations, only {sorted(kinds)}")
    if sum(1 for v in CORPUS if v[6] == "digest") < 8:
        die("fewer than eight digest vectors remain — the digest half of this differential has been gutted")


def classify_broker(message: str) -> list[str]:
    """The refusal classes the broker's own message reports, in the engine's vocabulary."""
    classes = []
    for part_text, canonical in BROKER_PARTS.items():
        for marker, name in BROKER_MARKERS:
            if f"{part_text} {marker}" in message:
                classes.append(f"{name}:{canonical}")
    if "is not owner/repo#N" in message:
        classes.append("itemNotQualified")
    if "is not a canonical server-assigned comment id" in message:
        classes.append("generationNotServerAssigned")
    if re.search(r"is not owner/repo(?!#N)", message):
        classes.append("receiverNotQualified")
    return sorted(set(classes))


# --------------------------------------------------------------------------------------------------
# Driving the two implementations.
# --------------------------------------------------------------------------------------------------

WIRE = {"merge": lambda p: "merge", "dispatch": lambda p: "dispatch:" + p, "publish": lambda p: "publish:" + p}


def units(value: str) -> str:
    """UTF-16 CODE UNITS, not code points, and the difference is not academic.

    A Python `str` is indexed by code POINT, so `ord("\U0001D11E")` is `0x1D11E` — five hex digits and
    one element. A `.NET` string is indexed by UTF-16 code UNIT, so the same character is two elements,
    `D834 DD1E`. Emitting code points here silently truncated every astral character to its low 16 bits
    and handed the oracle a different string than the broker was driven with; the differential caught
    that on its first run, against itself. `surrogatepass` is what carries the lone surrogates the
    ill-formed-text vectors depend on — a plain UTF-16 encode raises on them.
    """
    raw = value.encode("utf-16-be", "surrogatepass")
    return ",".join("%04x" % int.from_bytes(raw[i:i + 2], "big") for i in range(0, len(raw), 2))


def from_units(field: str) -> str:
    if field == "":
        return ""
    raw = b"".join(int(unit, 16).to_bytes(2, "big") for unit in field.split(","))
    return raw.decode("utf-16-be", "surrogatepass")


def die(message: str) -> "None":
    print(f"no verdict: {message}", file=sys.stderr)
    sys.exit(3)


def extract_broker_source(workflow_path: str) -> str:
    """The authorize step's embedded Python, lifted out of the REAL workflow."""
    try:
        import yaml
    except ImportError:  # pragma: no cover - the fixture installs it
        die("PyYAML is not importable, so the workflow cannot be parsed structurally")
    with open(workflow_path, encoding="utf-8") as handle:
        workflow = yaml.safe_load(handle)
    try:
        steps = workflow["jobs"]["authorize"]["steps"]
    except (KeyError, TypeError):
        die(f"{workflow_path} has no jobs.authorize.steps — the fence has been dismantled")
    matching = [s for s in steps if isinstance(s, dict) and s.get("id") == "authorize" and "run" in s]
    if len(matching) != 1:
        die(f"expected exactly one `run:` step with id `authorize`, found {len(matching)}")
    run = matching[0]["run"]
    found = re.search(r"(?ms)^python3 - <<'PY'\n(.*?)\n^PY\s*$", run)
    if not found:
        die(
            "the authorize step no longer embeds its logic as a `python3 - <<'PY'` heredoc. This "
            "differential drives the SHIPPED source and refuses to guess at a new shape"
        )
    return found.group(1)


class Exited(Exception):
    def __init__(self, code):
        self.code = 0 if code is None else code


def run_broker_in_process(source: str, environ: dict) -> tuple[int, str]:
    """Execute the extracted broker source with an exact-code-unit environment."""
    captured_out, captured_err = io.StringIO(), io.StringIO()

    def exit_(code=0):
        raise Exited(code)

    def no_network(*args, **kwargs):
        raise AssertionError(
            "the broker reached `subprocess.run` in a domain-only leg. Every vector here presents a "
            "deliberately wrong opkey and must refuse at step 3 or earlier, so this is a harness "
            "defect and not a result"
        )

    real_environ, real_exit, real_run = os.environ, sys.exit, subprocess.run
    code = 0
    try:
        os.environ = environ
        sys.exit = exit_
        subprocess.run = no_network
        with contextlib.redirect_stdout(captured_out), contextlib.redirect_stderr(captured_err):
            try:
                exec(compile(source, "<fsgg-dispatch-broker.yml::authorize>", "exec"), {"__name__": "__main__"})
            except Exited as exited:
                code = exited.code
            except AssertionError:
                raise
            except BaseException as crash:  # noqa: BLE001 - a crash is a RESULT here, not an accident
                # A traceback is not a refusal. `Operation.compose` refuses ill-formed text BEFORE
                # anything is hashed; if the broker's own guards are ever weakened, the same input
                # reaches `str.encode("utf-8")` and raises instead. That must be reported as the
                # disagreement it is rather than taking the harness down with it — and it is how the
                # M4 control below (which deletes the surrogate guard) is observed.
                code = 70
                captured_err.write(f"::error::the broker CRASHED rather than refusing: {crash!r}\n")
    finally:
        os.environ, sys.exit, subprocess.run = real_environ, real_exit, real_run
    return code, captured_out.getvalue() + captured_err.getvalue()


def broker_environ(item, generation, receiver, wire, payload, output_path) -> dict:
    return {
        "ITEM": item,
        "GENERATION": generation,
        "RECEIVER": receiver,
        "OP": wire,
        # DELIBERATELY WRONG, so step 3 prints the key it recomputed. See the module docstring.
        "OPKEY": "0" * 64,
        "GRANT": "4242424242",
        "EVENT_TYPE": payload,
        "GH_TOKEN_LOCAL": "local-token",
        "GH_TOKEN_APP": "app-token",
        "SELF_REPO": "FS-GG/.github",
        "OP_LOCK_ISSUES": '{"FS-GG/FS.GG.Net": 72}',
        "GITHUB_OUTPUT": output_path,
        "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
    }


def engine_answers(fsx: str, dll: str) -> dict:
    """Run `Operation.compose` over the corpus and return {id: ('ok', key, wire) | ('refused', classes)}."""
    if not os.path.isfile(dll):
        die(
            f"the engine assembly {dll} is not built, so `Operation.compose` cannot be executed. This "
            "leg REFUSES rather than skipping: an oracle that is absent is not an oracle that agrees"
        )
    payload = "\n".join(
        "\t".join([vector[0].split()[0], units(vector[1]), units(vector[2]), units(vector[3]), vector[4], units(vector[5])])
        for vector in CORPUS
    )
    # `-r:` rather than a `#r` inside the script: the assembly lives OUTSIDE the checkout, because
    # building it into the checkout is `.github#2653` — tier 2a would prefer that artifact and
    # `stale_guard` would then refuse every board write from the worktree. `run.sh` obtains this path
    # from `scripts/build-gate-engine`, the one sanctioned route.
    proc = subprocess.run(
        ["dotnet", "fsi", "--nologo", f"-r:{os.path.abspath(dll)}", fsx],
        input=payload,
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        die(f"the engine oracle did not run (exit {proc.returncode}):\n{proc.stdout}\n{proc.stderr}")
    answers = {}
    for line in proc.stdout.splitlines():
        if not line.strip():
            continue
        fields = line.split("\t")
        if fields[1] == "ok":
            answers[fields[0]] = ("ok", fields[2], from_units(fields[3]))
        else:
            answers[fields[0]] = ("refused", sorted(set(filter(None, fields[2].split(",")))))
    missing = {v[0].split()[0] for v in CORPUS} - set(answers)
    if missing:
        die(f"the engine oracle returned no answer for {sorted(missing)} — an incomplete oracle is no verdict")
    return answers


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--fsx", required=True)
    parser.add_argument("--dll", required=True)
    parser.add_argument("--cross-check", action="store_true",
                        help="also drive every env-expressible vector through the real subprocess boundary")
    args = parser.parse_args()

    source = extract_broker_source(args.workflow)
    engine = engine_answers(args.fsx, args.dll)
    check_corpus(engine)

    findings = []
    compared = {"digest": 0, "refuse": 0, "narrowed": 0}
    with tempfile.TemporaryDirectory() as work:
        output_path = os.path.join(work, "step_output")
        for name, item, generation, receiver, kind, payload, expectation in CORPUS:
            vid = name.split()[0]
            wire = WIRE[kind](payload)
            open(output_path, "w").close()
            code, message = run_broker_in_process(
                source, broker_environ(item, generation, receiver, wire, payload, output_path)
            )
            expected = engine[vid]

            if expectation == "digest":
                if expected[0] != "ok":
                    findings.append(f"{name}: the corpus says both admit, but the ENGINE refused it ({expected[1]})")
                    continue
                if expected[2] != wire:
                    findings.append(
                        f"{name}: `Operation.wire` rendered {expected[2]!r} but the broker was driven with "
                        f"{wire!r} — the corpus and the engine disagree about the operation vocabulary"
                    )
                    continue
                found = re.search(r"recomputed '([0-9a-f]{64})'", message)
                if not found:
                    findings.append(
                        f"{name}: the engine composed a key but the broker never recomputed one. It said: "
                        f"{message.strip()!r}"
                    )
                    continue
                if found.group(1) != expected[1]:
                    findings.append(
                        f"{name}: THE DIGESTS DISAGREE. engine={expected[1]} broker={found.group(1)}. "
                        "The two implementations key one operation two ways and no dedupe will ever match"
                    )
                compared["digest"] += 1

            elif expectation == "narrowed":
                if expected[0] != "ok":
                    findings.append(f"{name}: the corpus says the engine admits it, but it refused ({expected[1]})")
                    continue
                if code == 0:
                    findings.append(f"{name}: the broker did NOT refuse an operation it does not carry")
                    continue
                if "not brokered here" not in message and "publication effect" not in message:
                    findings.append(
                        f"{name}: the broker refused, but not as an out-of-scope operation. It said: "
                        f"{message.strip()!r}"
                    )
                compared["narrowed"] += 1

            else:  # "refuse"
                if expected[0] != "refused":
                    findings.append(
                        f"{name}: the ENGINE ADMITTED a vector the corpus says it refuses (key "
                        f"{expected[1]}) — the corpus is wrong or the engine's domain moved"
                    )
                    continue
                if code == 0:
                    findings.append(
                        f"{name}: THE BROKER ADMITTED WHAT THE ENGINE REFUSES. This is a fence hole: the "
                        f"broker would compose and act on a key the engine's domain excludes"
                    )
                    continue
                if "the broker CRASHED" in message:
                    findings.append(
                        f"{name}: the broker CRASHED on an input the engine refuses cleanly ({expected[1]}). "
                        "A traceback is not a refusal: the guard that should have caught this input before "
                        f"anything was hashed is gone. {message.strip()!r}"
                    )
                    continue
                if "recomputed '" in message:
                    findings.append(
                        f"{name}: the broker refused at the opkey comparison rather than in its domain — "
                        "it composed a key over an input the engine refuses to compose over"
                    )
                    continue
                broker_classes = classify_broker(message)
                if not broker_classes:
                    die(
                        f"{name}: the broker refused with a message this comparer cannot classify. That is "
                        f"a NO-VERDICT, never a pass — the message was {message.strip()!r}"
                    )
                extra = [c for c in broker_classes if c not in expected[1]]
                for cls in extra:
                    kind_b, _, part_b = cls.partition(":")
                    swapped = any(
                        (kind_b, other.partition(":")[0]) in ALLOWED_CLASS_SWAPS and other.partition(":")[2] == part_b
                        for other in expected[1]
                    )
                    if not swapped:
                        findings.append(
                            f"{name}: the broker refused as {cls!r}, which is not a reason the engine holds "
                            f"({expected[1]}). Both refused, so the fence held — but the two domains now "
                            "disagree about WHY, and a reason that has drifted is a domain that is drifting"
                        )
                compared["refuse"] += 1

        if args.cross_check:
            findings.extend(cross_check(source, work))

    print(
        f"compared {compared['digest']} digest vectors, {compared['refuse']} refusal vectors and "
        f"{compared['narrowed']} deliberate narrowings against the executed engine"
    )
    if findings:
        print("\nDISAGREEMENTS between the broker's transcription and Operation.compose:", file=sys.stderr)
        for finding in findings:
            print(f"  - {finding}", file=sys.stderr)
        return 1
    return 0


def cross_check(source: str, work: str) -> list[str]:
    """Drive the env-expressible vectors through the REAL subprocess boundary too.

    The in-process harness is the only way to reach the ill-formed-text half of the domain, so it must
    not also be the only evidence that it reproduces the shipped behaviour. Every vector whose
    components survive a UTF-8 round trip is run BOTH ways and the verdicts must be identical.
    """
    script = os.path.join(work, "authorize.py")
    with open(script, "w", encoding="utf-8") as handle:
        handle.write(source)
    output_path = os.path.join(work, "cross_output")
    findings = []
    checked = 0
    for name, item, generation, receiver, kind, payload, _ in CORPUS:
        components = [item, generation, receiver, payload]
        if any("\x00" in c or c != c.encode("utf-8", "surrogatepass").decode("utf-8", "replace") for c in components):
            continue
        wire = WIRE[kind](payload)
        open(output_path, "w").close()
        env = broker_environ(item, generation, receiver, wire, payload, output_path)
        proc = subprocess.run([sys.executable, script], capture_output=True, text=True, env=env)
        open(output_path, "w").close()
        in_code, in_message = run_broker_in_process(
            source, broker_environ(item, generation, receiver, wire, payload, output_path)
        )
        subprocess_refused = proc.returncode != 0
        if subprocess_refused != (in_code != 0):
            findings.append(
                f"{name}: the in-process harness and the real subprocess DISAGREE on the verdict "
                f"(subprocess exit {proc.returncode}, in-process exit {in_code}) — the harness is not "
                "reproducing the shipped behaviour and no leg driven through it means anything"
            )
            continue
        sub_key = re.search(r"recomputed '([0-9a-f]{64})'", proc.stdout + proc.stderr)
        in_key = re.search(r"recomputed '([0-9a-f]{64})'", in_message)
        if (sub_key is None) != (in_key is None) or (sub_key and sub_key.group(1) != in_key.group(1)):
            findings.append(f"{name}: the in-process harness and the real subprocess recomputed different keys")
        checked += 1
    if checked == 0:
        die("the cross-check ran over ZERO vectors, so it establishes nothing — that is not a pass")
    print(f"cross-checked {checked} env-expressible vectors against the real subprocess boundary")
    return findings


if __name__ == "__main__":
    sys.exit(main())
