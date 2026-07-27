#!/usr/bin/env python3
"""Assert every repo-scoped disable rule in `default.json` names exactly the repos that RECEIVE its files.

.github#1552, epic #266 (coherence gates that fail open). Filed from FS-GG/FS.GG.Net#33, which made
FS.GG.Net extend the org preset and found that one preset rule silently disabled its ENTIRE NuGet
surface.

THE DEFECT THIS CLOSES
  `default.json` disables Renovate for files a repo does not author, because a bump against a
  materialized file is a PR that structurally cannot merge (#678, #794). Which repos those are is a
  fact about the DELIVERY FABRICS, and it lived in the preset as a hand-written predicate:

      "matchRepositories": ["!FS-GG/.github"]        # "every repo except the one that authors these"

  That is not the same set as "every repo that RECEIVES these", and the two stopped being equal the
  moment FS.GG.Templates, FS.GG.Audio and FS.GG.Net were onboarded WITHOUT `build-config`. They
  hand-author their own build config, so the rule disabled files they own. FS.GG.Net's and
  FS.GG.Audio's entire NuGet surface went dark — 11 of 18 pins already behind, security updates
  suppressed too (measured; see the preset's own description) — and FS.GG.Audio sat on FS.GG.Kit
  0.6.0 while 0.8.0 shipped, with no bump PR ever proposed.

  NOTHING WENT RED, AND NOTHING COULD. Renovate detects the package file, applies the rule, proposes
  nothing, and reports success. A bump that is never proposed is not an error — it appears nowhere.
  That is the #1533 sentence, and this is another instance of it. The rule was correct when written;
  the roster moved underneath it and nothing re-checked it. `default.json` names that exact hazard
  about a DIFFERENT rule three paragraphs up — "a rule nothing re-checks is a rule that outlives its
  reason" (#943, #850) — and then carried this one unchecked.

WHY A GATE AND NOT A CAREFULLY-WRITTEN LIST
  The list is still a list; a preset cannot query a registry at run time. What a gate changes is
  WHERE THE AUTHORITY LIVES. `registry/repos.yml` is already the org's single roster — the file
  every fabric reads, gated by `repos.sh validate`, `check-roster-closure.py` and `repos-audit`. So
  the preset's repo list is DERIVED from it here, at CI time, and a drift between them is a red.
  Onboarding a repo becomes one roster row plus whatever this gate then demands, instead of a roster
  row and a silent, invisible wrong answer in a file nobody re-reads.

  This is deliberately the shape #1528 chose in this same touch-set: move the derivation into a
  GATE rather than ask the suspect surface to derive it at run time. #1538 declined to fold this
  assertion into `check-ignored-author-coherence.py`, and was right to — that gate compares one
  preset string to N workflow strings, offline and structural, with no registry in it. This one
  compares a preset predicate to a ROSTER, and its whole difficulty is deciding what "receives"
  means. One red must not carry two unrelated meanings.

WHAT IT ASSERTS
  For every `packageRules` entry in `default.json` that carries `enabled: false` AND `matchFileNames`:

    1. it declares its fabric, with a `fsgg-repo-scope: receives=<capability>` line in `description`;
    2. `<capability>` is a word `registry/repos.yml`'s `capabilities:` block actually defines;
    3. its `matchRepositories` equals, EXACTLY and in sorted order, the `full` names of the rostered
       repos whose `receives:` contains `<capability>`;
    4. if any file it disables is one the roster's `kit:` block DELIVERS (a `kind: config` row's
       `dest`), the declared fabric is `coordination-kit` — because that is the fabric that
       delivers it, and the preset does not get a vote.

  Rule 1 is the closure that makes 2 and 3 mean anything. Without it a fourth file-scoped disable
  rule could be added tomorrow with any repo list at all and this gate would pass, having graded the
  two it knew about — which is #626's failure exactly (`build-config` and `labels` were legal
  `receives:` words with NO capability row, so they were swept in neither direction while the
  guarantee read as though it held for all five).

  RULE 4 IS WHAT STOPS THE ANNOTATION BEING TAKEN ON TRUST, and without it this gate has a hole
  wide enough to drive #1552's most plausible WRONG fix through. Rules 1-3 check that a rule's repo
  list matches the fabric it CLAIMS; they cannot tell whether it claimed the right fabric. So
  scoping `.config/dotnet-tools.json` to `build-config` — the tidy repair, narrowing the old fused
  rule to the four build-config receivers — would satisfy 1, 2 and 3 completely, while re-enabling
  Renovate against a kit-materialized file in FS.GG.Templates, FS.GG.Audio and FS.GG.Net. Each of
  those holds a copy that `coordination-sync --check` compares byte-for-byte (verified live over the
  REST contents API on 2026-07-27: Audio's and Net's are byte-identical to this repo's canonical
  source), so every such bump is a guaranteed-red PR that Renovate re-files on close — the #794
  churn class, newly manufactured in three repos, under a commit message claiming to fix churn.

  The binding is DERIVED, not asserted here: `registry/repos.yml`'s `kit:` block names
  `dist/dotnet/.config/dotnet-tools.json` with `dest: .config/dotnet-tools.json`, and that block IS
  the coordination kit — the roster's own prose calls it "the coordination kit every
  `coordination-kit` receiver must hold". Adding a second `kind: config` row tomorrow extends this
  rule to that file with no edit here.

WHY THE ANNOTATION LIVES IN `description`
  Because it is the only place it CAN live. Renovate's own validator REJECTS an invented sibling
  field on a packageRule — measured against `renovate-config-validator` 43.281.1, not assumed, and
  the fixture drives that negative control rather than trusting this sentence. `default.json` is the
  preset every FS-GG repo extends, so a config error here breaks dependency management org-wide at
  once. The `fsgg-cap-expires-when:` annotation two rules up (#943) is the same trick for the same
  reason; this follows its precedent rather than inventing a second convention.

WHY A GREEN `renovate-config-validator` IS NOT THIS GATE
  The workflow runs it, and it is worth running. It is nowhere near sufficient, and the blind spot
  is precisely this gate's subject: THE VALIDATOR DOES NOT RESOLVE PRESETS (FS.GG.Net#33, and the
  fixture re-drives it), and it has no opinion whatever about whether a `matchRepositories` list
  names the right repos. Every wrong answer this gate exists to catch is a configuration the
  validator calls VALID. A green validator is evidence about SHAPE, and nothing else.

WHAT IT DELIBERATELY DOES NOT ASSERT
  WHETHER A REPO *SHOULD* RECEIVE A CAPABILITY. That is a decision about somebody else's build —
  `repos.yml` says so at length about templates and audio — and it is not a gate's to make. This
  gate asks only whether the preset AGREES with the roster, in whatever state the roster is in.

  IT ALSO DOES NOT ASSERT THAT THE TWO FILE CLASSES STAY SPLIT. It grades whatever rules exist. What
  it makes impossible is a file-scoped disable rule whose repo set nobody derived.

Usage:
  scripts/check-preset-repo-scope-coherence.py [--root .] [--preset default.json]
                                               [--roster registry/repos.yml]

Exit: 0 coherent · 1 finding · 3 no verdict (unreadable/unparsable/no subject).
"""

from __future__ import annotations

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import json  # noqa: E402

from lib.gate import (  # noqa: E402
    ExitCode,
    GateError,
    base_parser,
    load_yaml,
    read_text,
    report_findings,
    report_ok,
    run,
)

NAME = "check-preset-repo-scope-coherence"

PRESET = "default.json"
ROSTER = "registry/repos.yml"

# What `check-paths-coherence.py` rule (c) reds the workflow against: everything this gate READS.
PATHS_SUBJECT = (PRESET, ROSTER)

# The annotation a repo-scoped rule declares its fabric with. Anchored and whitespace-tolerant, but
# NOT a substring search: a `description` paragraph that merely MENTIONS the convention (this file's
# own docstring quotes it, and so does the preset's prose) must not be mistaken for a declaration.
# That is #683 — a parser cannot tell a mention from a use — so the annotation must be the WHOLE
# paragraph, which is exactly how `fsgg-cap-expires-when:` is spelled two rules up.
SCOPE_ANNOTATION = re.compile(r"^fsgg-repo-scope:\s*receives=([A-Za-z0-9][A-Za-z0-9._-]*)$")

# The capability the roster's `kit:` block delivers. This is a fact about the registry's SCHEMA, not
# a policy choice: `kit:` is documented there as "the coordination kit every `coordination-kit`
# receiver must hold", and scripts/coordination-sync gates its distribution on
# `repos.sh list --receives coordination-kit`. Named once, here, so rule 4 below derives rather than
# guesses which fabric a delivered file belongs to.
KIT_CAPABILITY = "coordination-kit"

ANNOTATION_HELP = (
    'add a description paragraph reading exactly `fsgg-repo-scope: receives=<capability>`, naming '
    "the registry/repos.yml capability whose receivers this rule is about"
)


def _paragraphs(description) -> list[str]:
    """`description` is a string or a list of strings — Renovate accepts both. Anything else is a
    no-verdict: a rule whose description this gate cannot read is a rule it cannot grade."""
    if description is None:
        return []
    if isinstance(description, str):
        return [description]
    if isinstance(description, list):
        if not all(isinstance(p, str) for p in description):
            raise GateError(
                f"{PRESET}: a packageRule `description` list contains a non-string entry — cannot "
                f"read the rule's annotations, so refusing to report on it."
            )
        return description
    raise GateError(f"{PRESET}: a packageRule `description` is {type(description).__name__}, not a string or list")


def roster_receivers(roster: dict) -> tuple[dict[str, list[str]], set[str]]:
    """Derive `capability -> sorted full repo names` and the set of DEFINED capability words.

    Every degenerate shape is a GateError rather than an empty answer. An empty derivation is the
    dangerous case here: it would make "the preset names nobody" look coherent, which is the
    vacuous pass this whole gate exists to refuse (#266, #503).
    """
    repos = roster.get("repos")
    if not isinstance(repos, list) or not repos:
        raise GateError(f"{ROSTER}: `repos:` is missing or not a non-empty list — no roster to derive from")

    capabilities = roster.get("capabilities")
    if not isinstance(capabilities, list) or not capabilities:
        raise GateError(f"{ROSTER}: `capabilities:` is missing or not a non-empty list")
    defined: set[str] = set()
    for index, row in enumerate(capabilities):
        if not isinstance(row, dict) or not isinstance(row.get("id"), str) or not row["id"].strip():
            raise GateError(f"{ROSTER}: capabilities[{index}] has no usable `id`")
        defined.add(row["id"])

    by_capability: dict[str, list[str]] = {}
    for index, row in enumerate(repos):
        if not isinstance(row, dict):
            raise GateError(f"{ROSTER}: repos[{index}] is not a mapping")
        full = row.get("full")
        if not isinstance(full, str) or not full.strip():
            raise GateError(f"{ROSTER}: repos[{index}] has no usable `full` name")
        receives = row.get("receives", [])
        if not isinstance(receives, list) or not all(isinstance(w, str) for w in receives):
            raise GateError(f"{ROSTER}: {full} has a `receives:` that is not a list of strings")
        for word in receives:
            # A `receives:` word with no capability row is #626's hole. `repos.sh validate` is the
            # gate that owns that closure; this one refuses to DERIVE from an undefined word rather
            # than duplicating the check, because deriving from it would silently produce a set no
            # capability vouches for.
            if word not in defined:
                raise GateError(
                    f"{ROSTER}: {full} declares `receives: {word}`, which has no `capabilities:` row. "
                    f"Refusing to derive a repo set from an undefined capability (run: scripts/repos.sh validate)."
                )
            by_capability.setdefault(word, []).append(full)

    return {word: sorted(names) for word, names in by_capability.items()}, defined


def kit_delivered_files(roster: dict) -> set[str]:
    """Receiver-relative paths the `kit:` block DELIVERS — the `dest` of every `kind: config` row.

    These are the files whose fabric is not the preset's to declare (rule 4). A `kit:` block that is
    absent or malformed is a GateError rather than an empty set: an empty set silently switches rule
    4 off, and a check that disables itself on a bad read is the fail-open this gate is an instance
    of (#266). A kit with no `kind: config` row at all is legitimate, though — the block is mostly
    `kind: skill` rows, which are not files Renovate would ever manage — so THAT case is an honest
    empty set, not a refusal.
    """
    kit = roster.get("kit")
    if not isinstance(kit, list) or not kit:
        raise GateError(f"{ROSTER}: `kit:` is missing or not a non-empty list — cannot tell which files the kit delivers")
    dests: set[str] = set()
    for index, row in enumerate(kit):
        if not isinstance(row, dict):
            raise GateError(f"{ROSTER}: kit[{index}] is not a mapping")
        if row.get("kind") != "config":
            continue
        dest = row.get("dest")
        if not isinstance(dest, str) or not dest.strip():
            # `repos.sh validate` owns this rule; coordination-sync also dies on it. Refusing here
            # too, rather than skipping the row, keeps a dest-less row from quietly shrinking the
            # set rule 4 compares against.
            raise GateError(
                f"{ROSTER}: kit[{index}] is `kind: config` with no usable `dest` ({dest!r}) — "
                f"cannot tell which receiver path it delivers to (run: scripts/repos.sh validate)."
            )
        dests.add(dest.strip())
    return dests


def scoped_rules(preset: dict) -> list[tuple[int, dict]]:
    """Every packageRule that DISABLES a set of files — the rules whose repo scope is load-bearing.

    The subject is derived from the rule's own shape (`enabled: false` + `matchFileNames`), never
    from a list of rule indices or file names kept here. A fifth such rule written next month is
    graded the day it is written, with no edit to this file — the same reason
    `check-ignored-author-coherence.py` derives WHICH workflows it grades.
    """
    rules = preset.get("packageRules")
    if not isinstance(rules, list) or not rules:
        raise GateError(f"{PRESET}: `packageRules` is missing or not a non-empty list")
    found = []
    for index, rule in enumerate(rules):
        if not isinstance(rule, dict):
            raise GateError(f"{PRESET}: packageRules[{index}] is not an object")
        if rule.get("enabled") is False and "matchFileNames" in rule:
            found.append((index, rule))
    return found


def grade(
    index: int,
    rule: dict,
    receivers: dict[str, list[str]],
    defined: set[str],
    kit_files: set[str],
) -> list[str]:
    findings: list[str] = []
    where = f"{PRESET} packageRules[{index}]"
    files = rule.get("matchFileNames")
    files_note = ", ".join(files) if isinstance(files, list) else repr(files)

    declared = [
        m.group(1)
        for m in (SCOPE_ANNOTATION.match(p.strip()) for p in _paragraphs(rule.get("description")))
        if m is not None
    ]

    if not declared:
        return [
            f"{where} disables {files_note} but declares NO fabric — {ANNOTATION_HELP}. Without it "
            f"nothing can check that its `matchRepositories` names the repos that actually receive "
            f"those files, which is the #1552 defect."
        ]
    if len(declared) > 1:
        return [
            f"{where} declares {len(declared)} `fsgg-repo-scope:` annotations ({', '.join(declared)}). "
            f"A rule scopes to exactly one fabric; two derivations cannot both be its repo set."
        ]

    capability = declared[0]
    if capability not in defined:
        return [
            f"{where} declares `fsgg-repo-scope: receives={capability}`, which is not a capability "
            f"defined in {ROSTER}'s `capabilities:` block. Legal words: {', '.join(sorted(defined))}."
        ]

    # RULE 4. A file the kit DELIVERS has its fabric decided by the roster, not by this rule's
    # author. Checked BEFORE the repo list, because if the declared fabric is wrong then the repo
    # list derived from it is wrong too, and reporting the list diff would name the wrong cause.
    if isinstance(files, list):
        delivered = sorted(f for f in files if isinstance(f, str) and f in kit_files)
        if delivered and capability != KIT_CAPABILITY:
            return [
                f"{where} disables {', '.join(delivered)}, which {ROSTER}'s `kit:` block DELIVERS to "
                f"every `{KIT_CAPABILITY}` receiver — but declares `receives={capability}`. Those "
                f"receivers hold a materialized copy that `coordination-sync --check` compares "
                f"byte-for-byte, so a bump against it is a guaranteed-red PR in every one of them "
                f"(#794). Scoping a kit-delivered file to `{capability}` re-enables it in the "
                f"{sorted(set(receivers.get(KIT_CAPABILITY, [])) - set(receivers.get(capability, [])))} "
                f"receivers that take the kit but not `{capability}`. Declare "
                f"`fsgg-repo-scope: receives={KIT_CAPABILITY}`, in its own rule if this one also "
                f"covers files another fabric delivers."
            ]

    expected = receivers.get(capability, [])
    if not expected:
        # A capability with zero receivers would derive an EMPTY matchRepositories, and an empty
        # list matches nothing — silently un-disabling the file everywhere. Never green.
        raise GateError(
            f"{where} scopes to `{capability}`, but NO rostered repo in {ROSTER} declares "
            f"`receives: {capability}`. Deriving an empty repo list would silently re-enable "
            f"{files_note} in every repo — refusing to report a verdict on that."
        )

    actual = rule.get("matchRepositories")
    if not isinstance(actual, list) or not all(isinstance(r, str) for r in actual):
        return [
            f"{where} has a `matchRepositories` that is not a list of strings ({actual!r}); it must "
            f"be the {len(expected)} `{capability}` receivers: {expected}."
        ]

    if actual != expected:
        missing = sorted(set(expected) - set(actual))
        extra = sorted(set(actual) - set(expected))
        detail = []
        if missing:
            detail.append(
                f"MISSING {missing} — these repos receive `{capability}` per {ROSTER}, so a Renovate "
                f"bump against their {files_note} is a PR that cannot merge, and it is no longer suppressed"
            )
        if extra:
            detail.append(
                f"EXTRA {extra} — these repos do NOT receive `{capability}`, so they AUTHOR "
                f"{files_note}; disabling it zeroes pins they own, with no error anywhere (#1552)"
            )
        if not detail:
            detail.append(f"same members, wrong ORDER — expected sorted {expected}, got {actual}")
        findings.append(f"{where} `matchRepositories` disagrees with {ROSTER}: " + "; ".join(detail) + ".")

    return findings


def main(argv) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    ap.add_argument("--preset", default=None, help=f"path to the preset (default: <root>/{PRESET})")
    ap.add_argument("--roster", default=None, help=f"path to the roster (default: <root>/{ROSTER})")
    args = ap.parse_args(argv)

    preset_path = args.preset or os.path.join(args.root, PRESET)
    roster_path = args.roster or os.path.join(args.root, ROSTER)

    raw_preset = read_text(preset_path, PRESET)
    try:
        preset = json.loads(raw_preset)
    except json.JSONDecodeError as e:
        raise GateError(f"{PRESET} at {preset_path}: not parsable as JSON — {e}") from e
    if not isinstance(preset, dict):
        raise GateError(f"{PRESET} at {preset_path}: top level is not an object")

    roster = load_yaml(read_text(roster_path, ROSTER), ROSTER)
    receivers, defined = roster_receivers(roster)
    kit_files = kit_delivered_files(roster)

    rules = scoped_rules(preset)

    # NO SUBJECT IS A BROKEN GATE, NOT A CLEAN PRESET. A preset with no file-scoped disable rule at
    # all means this gate is reading the wrong file, or that the rules were deleted — either way
    # green would be the loudest possible pass over the least possible work (#266).
    if not rules:
        raise GateError(
            f"{PRESET} at {preset_path} has NO packageRule with `enabled: false` and `matchFileNames`. "
            f"This gate exists to scope those rules to their receivers; finding none means it is "
            f"looking at the wrong file, not that the preset is coherent."
        )

    findings: list[str] = []
    graded: list[str] = []
    for index, rule in rules:
        rule_findings = grade(index, rule, receivers, defined, kit_files)
        findings.extend(rule_findings)
        if not rule_findings:
            files = ", ".join(rule["matchFileNames"])
            capability = next(
                m.group(1)
                for m in (SCOPE_ANNOTATION.match(p.strip()) for p in _paragraphs(rule.get("description")))
                if m is not None
            )
            graded.append(f"packageRules[{index}] ({files}) -> {capability}: {len(rule['matchRepositories'])} receivers")

    if findings:
        return report_findings(NAME, findings)

    return report_ok(
        NAME,
        f"{len(rules)} file-scoped disable rule(s) in {os.path.abspath(preset_path)}, each scoped to "
        f"exactly the {ROSTER} receivers of the capability it declares — " + "; ".join(graded),
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
