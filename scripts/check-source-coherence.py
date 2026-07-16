#!/usr/bin/env python3
"""Gate the registry's `fsgg-contracts` version against FS.GG.Contracts' SOURCE (.github#741, epic #266).

WHAT THIS ASSERTS

    registry.contracts[fsgg-contracts].version  ==  FS.GG.Contracts source SemVer on FS-GG/FS.GG.SDD

where the source SemVer is `FS.GG.Contracts.fsproj <Version>`, cross-checked against
`Fsgg.ContractVersion.value`. `version` is DEFINED as that source SemVer (see the schema comment in
registry/dependencies.yml), so this gate asserts the field means what it says.

IT DOES NOT ASSERT ANYTHING ABOUT THE PACKAGE. `main` is not a release and a source tree is not a
package. What a consumer can actually restore is `package-version`, gated against the live feed by
scripts/check-feed-coherence.py (.github#267). The two gates are two angles on one release and are
deliberately separate: this one needs no network beyond a checkout, that one needs the feed.

WHY IT LIVES HERE AND NOT IN contract-coherence.yml — THE WHOLE POINT OF .github#741

This assertion used to run inside the REUSABLE contract-coherence.yml, which all six FS-GG repos
call. That made a disagreement between `FS-GG/.github@main`'s registry and `FS-GG/FS.GG.SDD@main`'s
source a REQUIRED-check failure in every repo at once — `enforce_admins`-level, not even an admin
merges past it. And no landing order avoided it, because no PR spans both repos (FS.GG.SDD#432):

  * bump SDD first  -> every caller reds the moment it merges, until .github advances the pin;
  * advance .github first -> the gate reads SDD main's OLD version against the new pin. Also red,
    and now every .github PR is wedged instead.

SDD calls contract-coherence.yml too, so a bare source bump wedged SDD's OWN merges as well.

Moving it here does not weaken it — the same equality is still asserted, on every PR that touches
the registry, on every push to main, and daily. What changes is the BLAST RADIUS: a red here reds
`.github`, the repo that owns registry/dependencies.yml and is the only one that can fix it. During
a publish-before-flip window (FR-007: source bumped + published, registry not yet flipped) `.github`
goes red and says exactly what is owed, while the other five repos keep merging. That is the same
trade feed-coherence.yml already makes, deliberately, for the same reason.

DO NOT "FIX" A RED HERE BY MOVING THE ASSERTION BACK INTO A REUSABLE WORKFLOW. A red is the flip
being owed. The whole defect was that this fact could stop six repos from merging.

FAIL-CLOSED (epic #266). An unreadable registry, an unreadable source tree, a missing `<Version>`,
an absent fsgg-contracts row, or a source tree whose two version literals disagree are all ERRORS.
"I could not look" and "I looked, and it is fine" must never share an exit code.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

CONTRACT_ID = "fsgg-contracts"

# `<Version>2.0.1</Version>` in FS.GG.Contracts.fsproj. Anchored to the tag, not to a bare SemVer
# match: a loose search would happily read <PackageVersion> or a comment and report a confident
# wrong number.
FSPROJ_VERSION = re.compile(r"<Version>\s*([^<\s]+)\s*</Version>")

# `let value = "2.0.1"` in ContractVersion.fs. Indented inside `module ContractVersion =`, which is
# why this is not anchored to the start of a line.
CONTRACT_VERSION_VALUE = re.compile(r'\blet\s+value\s*=\s*"([^"]*)"')


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


def _one(pattern: re.Pattern[str], text: str, what: str, path: Path) -> str:
    hits = pattern.findall(text)
    if not hits:
        raise GateError(
            f"could not find {what} in {path}. The gate cannot assert a version it cannot read."
        )
    # More than one is not "take the first": it means the file's shape is not what this gate
    # believes, and reading the first would be a guess with a confident number attached.
    if len(set(hits)) > 1:
        raise GateError(
            f"{path} declares {what} more than once, with DIFFERENT values ({', '.join(sorted(set(hits)))}). "
            f"Refusing to pick one."
        )
    value = hits[0].strip()
    if not value:
        raise GateError(f"{what} in {path} is empty.")
    return value


def source_version(contracts_src: Path) -> str:
    """The FS.GG.Contracts source SemVer, cross-checked across its two literals."""
    fsproj = contracts_src / "FS.GG.Contracts.fsproj"
    cvfs = contracts_src / "ContractVersion.fs"

    fsproj_ver = _one(FSPROJ_VERSION, _read(fsproj, "FS.GG.Contracts.fsproj"), "<Version>", fsproj)
    src_ver = _one(CONTRACT_VERSION_VALUE, _read(cvfs, "ContractVersion.fs"), "ContractVersion.value", cvfs)

    if fsproj_ver != src_ver:
        # SDD#387 added this same assertion inside SDD, so the break is caught by the PR that causes
        # it. Keep it here anyway: this gate must derive ONE source version, and if the tree offers
        # two it has no answer to give. (SDD 1.4.1 shipped exactly this incoherence and red every
        # .github PR — see the fsgg-contracts history in registry/dependencies.yml.)
        raise GateError(
            f"FS.GG.Contracts is internally incoherent: fsproj <Version>={fsproj_ver!r} != "
            f"ContractVersion.value={src_ver!r}. There is no single source version to assert against."
        )
    return fsproj_ver


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
    # `1.10` to the float 1.1, and the gate would then compare "1.1" and report drift that is a
    # YAML artefact — or, worse, agree by accident.
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
        want = source_version(Path(args.contracts_src))
        declared = registry_version(Path(args.registry))
    except GateError as e:
        print(f"::error::check-source-coherence: {e}", file=sys.stderr)
        return 1

    if declared != want:
        print(
            f"::error::check-source-coherence: {CONTRACT_ID} `version` is {declared!r} but the "
            f"FS.GG.Contracts SOURCE on FS-GG/FS.GG.SDD is {want!r}.\n"
            f"::error::This is the registry disagreeing with the source it names. If SDD has bumped "
            f"and published, flip BOTH `version` and `package-version` to {want!r} here "
            f"(publish-before-flip, FR-007). If SDD has bumped and NOT published, the publish is "
            f"owed first — feed-coherence.yml is the gate that says so.",
            file=sys.stderr,
        )
        return 1

    print(f"ok: {CONTRACT_ID} registry `version` == FS.GG.Contracts source SemVer == {want}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
