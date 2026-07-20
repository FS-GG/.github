#!/usr/bin/env python3
"""The contract -> package(s) map, and the "which rows are package-bearing" definition, SHARED by
the two gates that read the org feed: `check-feed-coherence.py` (DETECTION — reds when the registry
falls behind the feed) and `feed-autofix` (RESPONSE — opens the flip PR). ADR-0060 §Amendment
(.github#1260).

WHY THIS IS ONE FILE AND NOT TWO COPIES. Before this, `check-feed-coherence.py` held the full map
and `feed-autofix` hardcoded a SINGLE row (`CONTRACT = "fs-gg-ui-template"`). The detection half knew
about eight package-bearing contracts; the response half could flip exactly one, so the other seven
reddened `main` on every publish and were reconciled BY HAND — the churn ADR-0060 P1 exists to
remove. Generalizing the bot means it must reconcile the SAME subject set the gate detects, from the
SAME map — otherwise "the gate reds but the bot has nothing to fix" is a new silent gap (epic #266).
So the map, the "is this row a subject?" test, and the "is this mapping stale?" test live here, once,
and both gates import them. A row added here is detected AND auto-flipped in one edit.

WHY THE MAP IS HERE AND NOT IN THE REGISTRY. Adding a `package-id` field to dependencies.yml is a
change to the registry SCHEMA — a versioned cross-repo contract owned by FS.GG.Contracts
(`Fsgg.Registry`) — which is a `contract-change` in its own right and not worth coupling to these
gates (that is ADR-0060's Option C, deferred to P2 / .github#1261). The cost is that a NEW
package-bearing contract must be added below — and forgetting to is an ERROR, not a silent skip
(see `packages_for` / `stale_mappings`).
"""
from __future__ import annotations

import os
import sys

# `scripts/` is not a package, so put this file's own directory on the path: the test harnesses load
# these modules by path via importlib, which sets sys.path[0] to the TEST's directory, not scripts/.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import GateError  # noqa: E402  (path shim above must run first)

# contract id -> the package id(s) whose newest feed version `package-version` names.
#
# fs-gg-ui-template is the awkward one: its `version` is the FRAMEWORK pin (FS.GG.UI.*) and its
# `package-version` is the TEMPLATE package. The two decouple across template-only releases, so
# only the template package is feed-comparable here — and it is the one row `feed-autofix` keeps a
# BESPOKE strategy for (it moves `version` on a framework release; every other row's `version` is
# NOT feed-derived and the bot never writes it).
CONTRACT_PACKAGES: dict[str, list[str]] = {
    "fsgg-contracts": ["FS.GG.Contracts"],
    "governance-reference-gate-set": ["FS.GG.Governance.ReferenceGateSet"],
    "fs-gg-ui-template": ["FS.GG.UI.Template"],
    "game-sim-core": ["FS.GG.Game.Core"],
    "game-scene-adapter": ["FS.GG.Game.Render"],
    # All four ship as one coherent set at one version; a partial publish is a real defect and
    # should be reported, so every member is compared rather than just .Core.
    "fs-gg-audio": [
        "FS.GG.Audio.Core",
        "FS.GG.Audio.Host",
        "FS.GG.Audio.Engine",
        "FS.GG.Audio.Elmish",
    ],
    # `.github` is a producer too (ADR-0039 §5) and had no row here, so this gate had no subject for
    # the one package whose staleness degrades every worker in the fleet (.github#1067).
    #
    # WHAT THIS ENTRY DOES AND DOES NOT BUY. It catches the registry falling behind the feed — publish
    # 0.4.0 and forget to flip the row, and this reds. That is real and it is the .github#250 class,
    # which has hit other rows 7+ times. It does NOT catch the engine's OWN recurring failure (source
    # merged, never published), and no entry here could: this gate compares the registry to the feed
    # and never looks at source. See the coord-engine row in registry/dependencies.yml, and .github#1075
    # for the detector that measures commits since the release tag instead of comparing two scalars.
    "coord-engine": ["FS.GG.Coord.Cli"],
    # `.github`'s second producer package (ADR-0016's single org scaffolder). Registered with
    # `consumers: []` under schemaVersion 2 (.github#1067 → SDD#508 → .github#1114): nothing restores
    # it (a `dotnet new` tool humans install), but it IS published and must be checked against the feed
    # — a package-bearing contract with no mapping here is the unchecked-subject error (epic #266).
    "new-sdd-workspace": ["FS.GG.NewSddWorkspace"],
    # The six FS.GG.Net.* transport packages ship as one coherent set at one version (ADR-0052); a
    # partial publish is a real defect, so every member is compared rather than just .Core.
    "fs-gg-net": [
        "FS.GG.Net.Core",
        "FS.GG.Net.WebSocket",
        "FS.GG.Net.WebSocket.Server",
        "FS.GG.Net.Protobuf",
        "FS.GG.Net.Grpc",
        "FS.GG.Net.Elmish",
    ],
}

# The ONE row whose `version` is feed-derived and whose reconcile is bespoke (framework/template-only
# classification, triple-tag provenance). Named here so `feed-autofix` and its fixture agree on which
# row takes the special path and which take the generic one, without either hardcoding the string.
BESPOKE_CONTRACT = "fs-gg-ui-template"


def packages_for(contract_id: str) -> list[str]:
    """The package id(s) a contract's `package-version` names. Raises on an unmapped subject —
    a package-bearing contract with no mapping is the unchecked-subject error (epic #266), never a
    silent skip."""
    pkgs = CONTRACT_PACKAGES.get(contract_id)
    if not pkgs:
        raise GateError(
            f"contract {contract_id!r} declares a `package-version` but no package id is mapped "
            f"in CONTRACT_PACKAGES (scripts/registry_packages.py). Add it — an unmapped "
            f"package-bearing contract is exactly the unchecked subject epic #266 is about."
        )
    return pkgs


def subjects(contracts: list[dict]) -> list[dict]:
    """The package-bearing rows: every contract carrying a `package-version`. This is the subject set
    BOTH gates act on — detection compares each against the feed, response flips each that is behind.
    Defining it once is what keeps the two from disagreeing about what a subject is."""
    return [c for c in contracts if c.get("package-version") is not None]


def stale_mappings(contracts: list[dict]) -> list[str]:
    """Mapping entries whose contract has vanished from the registry. A stale mapping is how the next
    unchecked subject hides, so it is an ERROR to report, not a skip. Returns the offending ids."""
    known = {str(c.get("id", "")).strip() for c in contracts}
    return sorted(set(CONTRACT_PACKAGES) - known)
