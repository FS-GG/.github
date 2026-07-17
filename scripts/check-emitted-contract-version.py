#!/usr/bin/env python3
"""Gate the registry's `governance-handoff` version against SDD's emitted `contractVersion` constant.

WHAT THIS ASSERTS

    registry.contracts[governance-handoff].version  ==  Schemas.governanceHandoffContractVersion
                                                         (FS.GG.SDD src/FS.GG.Contracts/Schemas.fs, @main)

SDD stamps `governanceHandoffContractVersion` into every emitted
`readiness/<id>/governance-handoff.json` (`GovernanceHandoff.fs` and `ReleaseContract.fs` both READ
this one constant). So the constant IS the version the artifact self-declares, and this repo's
registry declares a `version` for the same contract. Those two are kept in step BY HAND across a repo
boundary, and until this gate existed nothing compared them.

WHY THIS GATE, AND WHY IT IS .github'S — .github#1085, epic #266

FS.GG.SDD#427 was two findings under one title. One — *the emitter stamps the wrong version* — was
paid inside SDD, structurally: #427 deleted a hand-kept mirror so the emitter and the constant are now
ONE literal that cannot drift, and SDD#387's own gate covers the rest. The OTHER — *nothing compares
SDD's constant to the version THIS registry declares* — is not SDD's to fix, because nothing in SDD can
see registry/dependencies.yml. Both repos wrote the gap down and both named .github as its owner; this
is that gate.

Left ungated, `governance-handoff` is free to advance here while SDD stamps something older — the same
silent drift the `fsgg-contracts` row already gates with check-source-coherence.py, one contract over.
At the ADR-0035 stage-3b flip (`--require-observed` default-on) that stops being harmless: an unbumped
emitter would hand a Governance consumer `ship.unobservedEvidence` under a `contractVersion` that never
declared it — the exact failure ADR-0035 ordered the bump to prevent.

WHY HERE AND NOT IN contract-coherence.yml — THE SAME REASON check-source-coherence.py LIVES HERE

The obvious home LOOKS like the reusable contract-coherence.yml, which already checks out FS.GG.SDD.
It is a trap, and .github#741 already sprang it once: that workflow is `workflow_call` and all six
FS-GG repos call it, so a disagreement between this repo's registry and FS.GG.SDD@main would be a
REQUIRED-check failure in every repo at once, at `enforce_admins` level, that NO landing order avoids
(no PR spans both repos). #741 moved exactly this class of assertion OUT into source-coherence.yml —
.github-only blast radius — and removed the SDD checkout from contract-coherence.yml along with it.
This gate runs from source-coherence.yml for the same reason and reuses its existing sparse checkout of
src/FS.GG.Contracts, where Schemas.fs already sits.

FAILS CLOSED (epic #266). An unreadable registry, an unreadable source tree, a missing or duplicated
constant, an absent `governance-handoff` row, or a `version` YAML coerced off a quoted string are all
ERRORS. "I could not look" and "I looked, and it is fine" must never share an exit code.

DO NOT "FIX" A RED HERE BY MOVING THE ASSERTION INTO A REUSABLE WORKFLOW, and do not pin the SDD
checkout to a tag to make a red go away. A red is the flip being owed against SDD's live `main`; a tag
pin would assert a version nobody is developing against (and would be a hand-maintained literal in a
repo whose pins have frozen — #576).

Pure-stdlib (PyYAML only, already a coherence-gate dependency); no network — the workflow does the
checkout, the gate reads a path. Exit 0 = coherent.

Usage: scripts/check-emitted-contract-version.py [registry/dependencies.yml] --contracts-src <dir>
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

CONTRACT_ID = "governance-handoff"

# `let governanceHandoffContractVersion = "1.1.0"` in Schemas.fs. Anchored to the NAME, not to a bare
# `let ... = "..."` — Schemas.fs holds many string-valued lets, and a loose match would read the wrong
# one and attach a confident number to it. `\b` (not `^`) because the binding is indented inside
# `module Schemas =`.
CONSTANT = re.compile(r'\blet\s+governanceHandoffContractVersion\s*=\s*"([^"]*)"')
CONSTANT_FILE = "Schemas.fs"


class GateError(Exception):
    """A definite, reportable failure. Never a skip."""


def _read(path: Path, what: str) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as e:
        raise GateError(
            f"cannot read {what} at {path}: {e}. That is a FAILED READ, not a coherent tree — "
            f"refusing to report green."
        ) from e


def emitted_version(contracts_src: Path) -> str:
    """SDD's `governanceHandoffContractVersion` — the version every emitted handoff self-declares."""
    schemas = contracts_src / CONSTANT_FILE
    text = _read(schemas, CONSTANT_FILE)
    hits = CONSTANT.findall(text)
    if not hits:
        raise GateError(
            f"could not find `governanceHandoffContractVersion` in {schemas}. The gate cannot assert "
            f"a version it cannot read — the constant may have been renamed or moved."
        )
    # More than one is not "take the first": it means the file's shape is not what this gate believes,
    # and reading the first would be a guess wearing a confident number. (This is also how the OLD
    # defect looked — the value hand-mirrored across two sites; #427 collapsed it to one, and this
    # refusal keeps a second copy from silently reappearing.)
    if len(set(hits)) > 1:
        raise GateError(
            f"{schemas} binds `governanceHandoffContractVersion` more than once, with DIFFERENT "
            f"values ({', '.join(sorted(set(hits)))}). Refusing to pick one."
        )
    value = hits[0].strip()
    if not value:
        raise GateError(f"`governanceHandoffContractVersion` in {schemas} is empty.")
    return value


def registry_version(registry: Path) -> str:
    text = _read(registry, "registry")
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"cannot parse registry {registry}: {e}") from e

    if not isinstance(doc, dict):
        raise GateError(f"registry {registry} is not a mapping — this is not the schema.")

    contracts = doc.get("contracts") or []
    row = next((c for c in contracts if str(c.get("id", "")).strip() == CONTRACT_ID), None)
    if row is None:
        raise GateError(
            f"{CONTRACT_ID} is absent from the registry. An absent subject is not a coherent one."
        )

    declared = row.get("version")
    if declared is None:
        raise GateError(f"{CONTRACT_ID} declares no `version`.")

    # MUST be a quoted string, for the reason `package-version` must be: unquoted, YAML coerces
    # `1.10` to the float 1.1, and the gate would then compare "1.1" and report drift that is a YAML
    # artefact — or, worse, agree by accident.
    if not isinstance(declared, str):
        raise GateError(
            f"{CONTRACT_ID} `version` is {type(declared).__name__} ({declared!r}), not a quoted "
            f"string. Quote it: unquoted, YAML coerces `1.10` to the float 1.1."
        )
    return declared.strip()


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("registry", nargs="?", default="registry/dependencies.yml")
    ap.add_argument(
        "--contracts-src",
        required=True,
        help="path to a checkout of FS-GG/FS.GG.SDD's src/FS.GG.Contracts",
    )
    args = ap.parse_args(argv)

    try:
        want = emitted_version(Path(args.contracts_src))
        declared = registry_version(Path(args.registry))
    except GateError as e:
        print(f"::error::check-emitted-contract-version: {e}", file=sys.stderr)
        return 1

    if declared != want:
        print(
            f"::error::check-emitted-contract-version: {CONTRACT_ID} `version` is {declared!r} but "
            f"the version SDD's emitter stamps (`governanceHandoffContractVersion` in "
            f"FS.GG.Contracts/Schemas.fs) is {want!r}.\n"
            f"::error::These are the two values .github#1085 exists to keep in step. The emitted "
            f"artifact self-declares {want!r}; this registry declares {declared!r}. Flip this row's "
            f"`version` to {want!r} alongside SDD's constant, never one without the other — a "
            f"consumer meets a `contractVersion` its declared contract must match (ADR-0035).",
            file=sys.stderr,
        )
        return 1

    print(
        f"ok: {CONTRACT_ID} registry `version` == SDD emitted `governanceHandoffContractVersion` "
        f"== {want}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
