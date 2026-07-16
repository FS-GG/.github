#!/usr/bin/env python3
"""Assert a workflow's `pull_request.paths` and `push.paths` agree.

.github#880, epic #266 (coherence gates that fail open). Found by worker `finch-2e3f` while working
#860 (the suite selector), which derives its answer from these very filters.

THE RULE, AND IT IS THE WHOLE RULE
  For every workflow declaring BOTH `pull_request.paths` and `push.paths`, the two lists are
  identical as sets.

Nearly every workflow here duplicates its `paths:` list verbatim between the two triggers. That
duplication is invisible: nothing reads both copies, so when one is edited and the other is not, the
result is a gate that still passes its own tests and simply stops running on `main`. Green, and
wrong — the #266 signature, arrived at by a typo rather than a bug.

Two had already drifted when this gate was written:

  - `adr-coherence.yml`      — the push copy omitted `tests/adr-coherence/**` and its own workflow
                               file, so editing that fixture ON MAIN did not re-run the gate the
                               fixture belongs to.
  - `skill-registry-coherence.yml` — the push copy omitted `tests/skill-registry/**`.

WHY A GATE, AND NOT A FIFTH HAND-REPAIR
  This is the fifth instance of one class:
    #332 — repos-audit-selftest.yml did not trigger on repos-audit.yml.
    #334 — the kit digest gate's filter covered 1 of 3 kit sources. Its fix sketch asked, in as many
           words, for an assertion that each source is matched "on BOTH pull_request and push".
    #508 — sync-build-config-selftest.yml's filter omitted `dist/dotnet/**`, its own input. It
           closes by wondering aloud whether the other selftests have the same mismatch.
    these two.
  Nobody was careless. There is no gate on the gates' own triggers, so the class regenerates faster
  than it is repaired and each repair is one workflow wide. pnext-item §4: if a fix keeps
  regenerating the same finding, the finding is not the bug — the thing that regenerates it is.

WHAT THIS GATE DELIBERATELY DOES NOT DO
  The harder half — DOES A FILTER COVER THE WORKFLOW'S OWN SUBJECT? — is #334/#508's shape and is
  NOT mechanically decidable: it needs a human to say what a given gate's inputs are. A narrow gate
  that ships beats a broad one that does not, so that class stays a judgement call and this gate
  stays an equality test. Do not grow it.

SHAPES THIS GATE REFUSES RATHER THAN SKIPS
  Set equality is only a sound reading of `paths:` while the lists are plain allow-lists. Two shapes
  would break that, and both are exit 3 (no verdict) rather than a silent skip — a skip is how a
  coherence gate fails open, which is the whole of #266:

    - `paths-ignore:` INVERTS selection. A `pull_request.paths` and a `push.paths-ignore` are
      genuinely different filters, but they are not two lists this gate can compare. scripts/test
      refuses it for the same reason.
    - a NEGATED (`!`) pattern makes ORDER significant — GitHub lets a later pattern override an
      earlier one — so `[a, !b]` and `[!b, a]` are equal as sets and different as filters. Set
      equality would call them coherent, which is exactly the confident-wrong-answer this gate is
      supposed to end.

  Neither shape is live in this repo today. They are refused anyway, because the gate must not
  quietly start being unsound the day one appears.

THE ESCAPE HATCH
  A gate with no way to say "yes, on purpose" becomes a straitjacket that gets disabled. A workflow
  may diverge deliberately by carrying, anywhere in the file:

      # paths-coherence: allow-divergence — <reason>

  The reason is REQUIRED and must be non-empty: the point is not to permit divergence but to make it
  a decision somebody made and signed, rather than a typo nobody saw. A bare marker with no reason
  is itself a finding.

EXIT CODES — THE CONTRACT
  0  every workflow declaring both filters declares them identically.
  1  FINDING: a workflow's two copies have drifted.
  3  NO VERDICT (permanent): a workflow would not parse, a shape cannot be compared, or NOTHING was
     audited. Examining nothing is a failure to audit, not a clean audit (#266).

  There is deliberately no exit 2 ("no verdict, retryable"): this gate is pure and offline. It reads
  files, makes no network call, and has no condition a re-run could resolve.

usage:
  check-paths-coherence.py [--root <dir>]
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import sys
import traceback

import yaml

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

# The escape hatch, and it must match a USE of the marker rather than a MENTION of it.
#
# This took two goes, and the second one is the point. .github#683's lesson is that a parser cannot
# tell a mention from a use unless you MAKE it — and "make it" here means asking the YAML parser,
# not writing a cleverer regex.
#
#   draft 1  matched the text anywhere in the file. The first thing it licensed was
#            paths-coherence.yml itself, whose FINDING step prints the marker to document it: the
#            gate read its own documentation as a signed divergence.
#   draft 2  anchored to `^[ \t]*#`, which does exclude an `echo '# paths-coherence: …'` line — and
#            still licensed real drift from a SHELL comment inside a `run: |` block, because a shell
#            comment is `^[ \t]*#` too. Same fail-open, one layer down.
#
# A regex cannot see the difference: `# x` is a YAML comment at one indent and opaque block-scalar
# TEXT at another. So the line filter below asks PyYAML which lines are inside a block scalar, and
# the marker is only honoured on a line that is not. The anchor stays as the second half of the
# test — together they mean "a standalone YAML comment", which is what the hatch was always
# documented to be.
#
# `[ \t]*`, never `\s*`: `\s` matches a newline, so `\s*` would let the `#` sit on one line and the
# marker on the next, defeating the anchor it is standing next to.
#
# The separator before the reason is OPTIONAL and the reason is not: `— why`, `: why`, and a bare
# ` why` all sign the marker, while none of them is required for the marker to be RECOGNISED. That
# asymmetry is deliberate. If the separator were mandatory, a marker written with a reason but no
# dash would not match at all — so instead of "you forgot to sign this", the author would get an
# unrelated drift finding about their paths, which is a worse answer to a smaller mistake.
ALLOW_MARKER = re.compile(
    r"^[ \t]*#[ \t]*paths-coherence:[ \t]*allow-divergence[ \t]*[—:-]?[ \t]*(?P<reason>.*)$",
    re.MULTILINE,
)

# Returned by allow_divergence() when a marker is present but none of them is signed.
UNSIGNED = ""


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def load_yaml(text: str, what: str) -> dict:
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{what}: not parsable as YAML — {e}") from e
    if not isinstance(doc, dict):
        raise GateError(f"{what}: not a YAML mapping")
    return doc


def triggers(doc: dict, what: str) -> dict:
    """The `on:` block, with all three legal spellings normalised.

    PyYAML resolves the bare key `on` to the boolean True (YAML 1.1), so a plain doc["on"] misses it
    entirely. Same trap, same handling, as scripts/check-workflow-timeouts.py and
    scripts/check-workflow-permissions.py.

    `on: pull_request` and `on: [push, pull_request]` are as legal as the mapping form. Reading only
    the mapping form does not refuse them — it silently decides the workflow triggers on nothing,
    which here would mean skipping it. scripts/test made exactly that mistake (#879); do not repeat
    it. Anything that is none of the three spellings is refused, not guessed.
    """
    for key in ("on", True):
        if key in doc:
            got = doc[key]
            if isinstance(got, dict):
                return got
            if isinstance(got, list):
                return {str(k): None for k in got}
            if isinstance(got, str):
                return {got: None}
            raise GateError(
                f"{what}: `on:` is {type(got).__name__}, not a string, list, or mapping — this gate "
                f"cannot tell what triggers the workflow, and guessing would silently skip it (#266)."
            )
    return {}


def declared(on: dict, trigger: str) -> tuple[object, bool]:
    """`(<trigger>.paths as declared or None, whether it declares paths-ignore)`.

    `pull_request:` with a NULL value means EVERY PR, not "no PR trigger" (`coherence.yml` is in
    that state). Either way it declares no `paths:`, so it is not half of a pair — a workflow with no
    `paths:` on either trigger is not drift and must not be flagged.

    THIS READS AND DOES NOT JUDGE, and that is the entire fix for a real fail-closed bug.

    The first draft raised on `paths-ignore:` the moment it saw one, before it had established that
    the workflow was even in scope. So a workflow declaring ONLY `paths-ignore:` — no `paths:` at
    all, not half of a pair, plainly outside the rule — refused. And because a refusal aborts the
    run, ONE such file took the ENTIRE audit to exit 3 and every real drift in the repo went
    unreported. `paths-ignore:` is an ordinary Actions feature; the first person to add one would
    have blocked CI with a diagnostic naming three causes, none of them theirs.

    A gate may only refuse what it was actually asked to judge. Reading is not judging, so the read
    happens here and every refusal happens in main(), after scope is established.
    """
    t = on.get(trigger)
    if not isinstance(t, dict):
        return None, False
    return (t.get("paths") if "paths" in t else None), ("paths-ignore" in t)


def validated(raw: object, trigger: str, what: str) -> list[str]:
    """`<trigger>.paths` as a list of patterns this gate can soundly compare.

    Only ever called on a workflow that IS a pair. A one-sided workflow's patterns are never
    compared, so refusing them would be a false alarm about a file outside the rule.
    """
    if not isinstance(raw, list) or not raw:
        raise GateError(
            f"{what}: `{trigger}.paths:` is present but is not a non-empty list ({raw!r})."
        )

    pats = [str(p) for p in raw]
    for p in pats:
        if p.startswith("!"):
            raise GateError(
                f"{what}: `{trigger}.paths:` carries the negated pattern {p!r}. Negation makes ORDER "
                f"significant — a later pattern overrides an earlier one — so two lists can be equal "
                f"as SETS and different as FILTERS. This gate's equality test would call that "
                f"coherent, which is the confident-wrong-answer it exists to prevent (#266)."
            )
    return pats


def block_scalar_lines(text: str) -> set[int]:
    """The 0-based lines covered by a block scalar (`|` / `>`) value.

    A `#` inside a `run: |` block is shell TEXT, not a YAML comment, and nothing about the character
    says which. This is the only reliable way to tell: ask the parser where the opaque regions are.
    Without it the hatch reads a shell comment — or a heredoc line — as a signed divergence and
    licenses real drift (exit 0 on a broken workflow), which is the fail-open this gate exists to end.
    """
    try:
        node = yaml.compose(text)
    except yaml.YAMLError:
        # load_yaml() has already refused this file with a no-verdict; nothing to exclude.
        return set()

    covered: set[int] = set()

    def walk(n: object) -> None:
        if isinstance(n, yaml.ScalarNode):
            if n.style in ("|", ">"):
                covered.update(range(n.start_mark.line, n.end_mark.line + 1))
        elif isinstance(n, yaml.SequenceNode):
            for child in n.value:
                walk(child)
        elif isinstance(n, yaml.MappingNode):
            for key, value in n.value:
                walk(key)
                walk(value)

    walk(node)
    return covered


def allow_divergence(text: str, what: str) -> str | None:
    """The signed reason this workflow may diverge.

    None  — no marker at all.
    ""    — a marker is present but NONE of them is signed (UNSIGNED). The caller makes that a
            FINDING: the hatch exists to turn a typo nobody saw into a decision somebody made, and
            an unsigned marker does neither.
    str   — the reason.

    Every marker is considered, not just the first: a header comment may legitimately DOCUMENT the
    bare form above the file's real, signed marker, and `search()` would have read that first line
    and called the file unsigned — a confidently wrong verdict against a file that did exactly what
    the gate asked.
    """
    opaque = block_scalar_lines(text)
    markers = [
        m for m in ALLOW_MARKER.finditer(text)
        if text.count("\n", 0, m.start()) not in opaque
    ]
    if not markers:
        return None
    for m in markers:
        reason = (m.group("reason") or "").strip()
        if reason:
            return reason
    return UNSIGNED


def workflow_files(root: str) -> list[str]:
    d = os.path.join(root, ".github", "workflows")
    if not os.path.isdir(d):
        raise GateError(f"{d} is not a directory — there are no workflows to audit")
    files = sorted(f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(d, f"*.{ext}")))
    if not files:
        raise GateError(f"{d} contains no workflow files — there is nothing to audit")
    return files


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="the working tree to audit (default: .)")
    args = ap.parse_args(argv)

    findings: list[str] = []
    pairs_seen = 0
    allowed = 0

    for path in workflow_files(args.root):
        where = os.path.relpath(path, args.root)
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        doc = load_yaml(text, where)
        on = triggers(doc, where)

        pr_raw, pr_ignores = declared(on, "pull_request")
        push_raw, push_ignores = declared(on, "push")

        # An allow-list facing an ignore-list: two filters that both narrow the trigger, in opposite
        # directions, with no way to say whether they agree. Silently skipping it is how a coherence
        # gate fails open (#266), so it is refused.
        #
        # Note what guards this: `pr_raw is not None`. The refusal can only fire on a workflow that
        # HAS an allow-list — i.e. one this gate was actually asked to judge. See declared() for the
        # bug that shape exists to prevent.
        if (pr_raw is not None and push_ignores) or (push_raw is not None and pr_ignores):
            raise GateError(
                f"{where}: one trigger declares `paths:` and the other declares `paths-ignore:`. "
                f"An ignore-list INVERTS selection, so this gate cannot say whether the two agree — "
                f"and a silent skip is how a coherence gate fails open (#266). Refusing to guess."
            )

        # One-sided is a deliberate shape (`build-config-propagate.yml` is push-only,
        # `reusable-job-id-coherence.yml` is PR-only) and is not this gate's business.
        if pr_raw is None or push_raw is None:
            continue

        pr = validated(pr_raw, "pull_request", where)
        push = validated(push_raw, "push", where)

        pairs_seen += 1
        reason = allow_divergence(text, where)

        # A marker with no reason is a FINDING, not a no-verdict. The gate checked and found
        # something definite; exit 3 would tell the developer "I could not check" and hand them
        # paths-coherence.yml's no-verdict summary, which names three causes and none of them is
        # theirs. That is #266's conflation running backwards — a verdict dressed as a failure to
        # reach one.
        if reason == UNSIGNED:
            findings.append(
                f"{where}: carries `# paths-coherence: allow-divergence` with NO reason. The hatch "
                f"exists to make divergence a decision somebody made rather than a typo nobody saw "
                f"— an unsigned marker does neither. Write the reason after the marker."
            )
            continue

        if set(pr) == set(push):
            # A hatch on a workflow that does not diverge is stale licence — it will silently permit
            # a REAL drift the day one arrives, which is the hatch quietly becoming the hole.
            if reason:
                findings.append(
                    f"{where}: carries `# paths-coherence: allow-divergence` ({reason}) but its two "
                    f"`paths:` lists are IDENTICAL. The marker licenses a divergence that does not "
                    f"exist, so it would silently permit a real one later. Remove it."
                )
            else:
                print(f"  ok   {where:<44} {len(pr)} pattern(s), both triggers agree")
            continue

        if reason:
            allowed += 1
            print(f"  allow {where:<43} diverges on purpose: {reason}")
            continue

        only_pr = sorted(set(pr) - set(push))
        only_push = sorted(set(push) - set(pr))
        detail = []
        if only_pr:
            detail.append(f"the `push` copy omits {', '.join(repr(p) for p in only_pr)}")
        if only_push:
            detail.append(f"the `pull_request` copy omits {', '.join(repr(p) for p in only_push)}")
        findings.append(f"{where}: " + "; and ".join(detail) + ".")

    # #266, AND THIS IS THE LINE THAT MATTERS. A reader that breaks — a new `on:` spelling, a YAML
    # quirk, a bad glob — would find zero pairs and report a clean audit over a repo full of them.
    # Zero pairs is therefore a NO VERDICT, not an OK. If this repo ever legitimately drops to zero,
    # that is a deliberate change and this gate should be deleted in the same commit, not left
    # printing green about nothing.
    if pairs_seen == 0:
        raise GateError(
            "audited every workflow and found NO workflow declaring both `pull_request.paths` and "
            "`push.paths`. This repo HAS them, so the trigger reader is broken — examining nothing "
            "is a failure to audit, not a clean audit (#266)."
        )

    if findings:
        for f in findings:
            print(f"::error::check-paths-coherence: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} workflow(s) whose `paths:` copies disagree, of {pairs_seen} "
            f"declaring both.\n"
            "\nA workflow duplicates its `paths:` list between `pull_request` and `push`. When the "
            "copies drift,\nthe gate still passes its own tests and simply STOPS RUNNING on `main` "
            "— green, and wrong (#880).\n"
            "\n  fix:  make the two lists identical.\n"
            "        Diverging on purpose? Say so, and sign it:\n"
            "          # paths-coherence: allow-divergence — <why>",
            file=sys.stderr,
        )
        return FINDING

    print(
        f"ok: every workflow's `paths:` copies agree — {pairs_seen} workflow(s) declaring both "
        f"audited" + (f", {allowed} diverging on purpose" if allowed else "") + "."
    )
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a workflow has drifted". So a
    crash anywhere in here would be dressed up by paths-coherence.yml as a specific, confident,
    WRONG finding about somebody's workflow. That is the conflation the exit-code contract exists to
    prevent (#266, #320): "I could not check" must never share a code with "I checked, and it's
    broken".
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-paths-coherence: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-paths-coherence: the gate crashed, so it has NO VERDICT. This is not a "
            "finding about any workflow's filters — it is a bug in the gate. See the traceback.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
