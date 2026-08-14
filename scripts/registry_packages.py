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
(see `packages_for` / `stale_mappings` / `unkeyed_subjects`, and `classify_mappings` for why those
three states are the whole of it).
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
    # .github#2070 (epic #2067 terminal activation): the contract id is `fs-gg-workspace-template`
    # (matching this registry's other kebab-case ids), but the PUBLISHED package is
    # `FS.GG.Workspace.Template` — renamed from the planned `FS.GG.Templates` package (ADR-0072 §1)
    # before its first publish. Map the id that actually exists on the feed, not the contract id;
    # this is exactly the kind of rename this map exists to make explicit rather than silently miss.
    "fs-gg-workspace-template": ["FS.GG.Workspace.Template"],
    # .github#2070: FS.GG.Game's independently-versioned skill-delivery package (owner-sourced
    # fs-gg-game-fable skill, materialized by SDD's production scaffold materializer).
    "game-skills": ["FS.GG.Game.Skills"],
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


def _is_subject(contract: dict) -> bool:
    """The ONE definition of "this row is package-bearing", used by `subjects()` AND by
    `classify_mappings()` below. It is a private helper rather than an inlined comprehension because
    the .github#2567 defect was precisely two functions asking DIFFERENT questions about the same
    row: one tested the key, the other tested the id, and nothing tested both. Routing every "is this
    a subject?" decision through one predicate is what makes them structurally unable to disagree."""
    return contract.get("package-version") is not None


def subjects(contracts: list[dict]) -> list[dict]:
    """The package-bearing rows: every contract carrying a `package-version`. This is the subject set
    BOTH gates act on — detection compares each against the feed, response flips each that is behind.
    Defining it once is what keeps the two from disagreeing about what a subject is."""
    return [c for c in contracts if _is_subject(c)]


def classify_mappings(contracts: list[dict]) -> tuple[list[str], list[str], list[str]]:
    """Reconcile CONTRACT_PACKAGES against the registry rows, and return
    `(checked, unkeyed, stale)` — the ids that are compared, the ids that silently stopped being
    compared, and the ids whose row is gone.

    WHY THIS EXISTS, AND WHY IT IS ONE FUNCTION (.github#2567). `subjects()` and `stale_mappings()`
    looked like a partition of the world and were not. `subjects()` selected on the KEY's presence;
    `stale_mappings()` subtracted on the ROW's `id`. So a row that still EXISTS but has LOST its
    `package-version` was in `known` — hence not a stale mapping — and was not a subject either. It
    left both halves' scope simultaneously, the gate compared one fewer subject, and it still exited
    0. Neither function was wrong on its own; the gap was BETWEEN them, and invisible from either
    side. That is the exact shape `stale_mappings()`' own comment warns about ("a stale mapping is
    how the next unchecked subject hides") — it just hid through a door that check was not watching.

    WHY A ROW CAN NO LONGER FALL BETWEEN THEM. This asks both questions, of the same id, in one
    place. It iterates `CONTRACT_PACKAGES` — the org's record of which contracts are package-bearing
    — and every mapped id lands in exactly one of three buckets, because the two questions form a
    complete truth table (the fourth cell, "absent row with a key", is unreachable: a row that is not
    there carries nothing):

        row present + `package-version` present  -> checked   (the only healthy state)
        row present + `package-version` MISSING  -> unkeyed   (the .github#2567 gap; an ERROR)
        row absent                               -> stale     (the pre-existing ERROR)

    So `checked + unkeyed + stale` is always a permutation of `CONTRACT_PACKAGES`'s keys — an
    invariant tests/feed-coherence/run.sh asserts directly, rather than trusting this prose.

    The converse direction — a package-bearing row that CONTRACT_PACKAGES does not map — stays with
    `packages_for()`, which raises per row. Between the two, the subject-id set and the mapping's key
    set are forced into exact EQUALITY on a healthy registry: every way for one to drift from the
    other is now an error with its own message. Note that equality is on IDENTITY, not size; a
    registry that dropped one mapped row and added another package-bearing row keeps the same subject
    COUNT and is caught here regardless, which is why nothing downstream should assert a count.

    DUPLICATE IDS, STATED RATHER THAN ASSUMED AWAY. Two rows sharing an id is itself a registry
    defect and is gated elsewhere (contract-coherence validates the schema). Here the FIRST such row
    decides the bucket, so a duplicated id whose first row lost its key is reported as `unkeyed` even
    though the second row would still be compared. That is the fail-CLOSED direction — it reports a
    registry nobody should have — and it is deliberate rather than incidental.
    """
    rows: dict[str, dict] = {}
    for c in contracts:
        rows.setdefault(str(c.get("id", "")).strip(), c)

    checked: list[str] = []
    unkeyed: list[str] = []
    stale: list[str] = []
    for cid in sorted(CONTRACT_PACKAGES):
        row = rows.get(cid)
        if row is None:
            stale.append(cid)
        elif _is_subject(row):
            checked.append(cid)
        else:
            unkeyed.append(cid)
    return checked, unkeyed, stale


def unkeyed_subjects(contracts: list[dict]) -> list[str]:
    """Mapped contracts whose row is STILL IN the registry but has lost its `package-version`
    (.github#2567). The row is not a subject, so nothing compares it against the feed and nothing
    flips it; and its id is still `known`, so `stale_mappings()` does not see it either. It is an
    ERROR to report, not a skip — "nothing to check" and "checked, and it's fine" must not share an
    exit code (epic #266). Returns the offending ids."""
    return classify_mappings(contracts)[1]


def stale_mappings(contracts: list[dict]) -> list[str]:
    """Mapping entries whose contract has vanished from the registry. A stale mapping is how the next
    unchecked subject hides, so it is an ERROR to report, not a skip. Returns the offending ids."""
    return classify_mappings(contracts)[2]


def unkeyed_problems(contracts: list[dict]) -> list[str]:
    """One ready-to-print message per row that is present, mapped, and has lost its
    `package-version`. Written once and SHARED by both halves (.github#2567 criterion 3): `subjects()`
    is shared, so the row stops being detected AND stops being flipped in the same instant, and a
    repair that taught only the gate would leave the bot narrowed by the identical row."""
    return [
        f"contract {cid!r} is in the registry but carries no `package-version`, while "
        f"CONTRACT_PACKAGES (scripts/registry_packages.py) maps it to {CONTRACT_PACKAGES[cid]}. A "
        f"mapped row that has lost its key leaves BOTH scopes at once — it is not a subject, so "
        f"nothing compares it against the feed or flips it, and its id is still known, so it is not "
        f"reported as a stale mapping either. Restore the row's `package-version`, or drop the "
        f"CONTRACT_PACKAGES mapping if the contract genuinely stopped shipping a package."
        for cid in classify_mappings(contracts)[1]
    ]


def stale_problems(contracts: list[dict]) -> list[str]:
    """One ready-to-print message per mapping whose contract has vanished from the registry."""
    return [
        f"CONTRACT_PACKAGES maps {cid!r}, which is not a contract in the registry. Remove the stale "
        f"mapping."
        for cid in classify_mappings(contracts)[2]
    ]


def mapping_problems(contracts: list[dict]) -> list[str]:
    """Every way the mapping and the registry can have drifted apart, as ready-to-print messages, in
    a fixed order — the whole of `classify_mappings`' two error buckets. This is what the DETECTION
    half reports; the bot reports only `unkeyed_problems`, for the reason stated at its call site."""
    return unkeyed_problems(contracts) + stale_problems(contracts)
