#!/usr/bin/env python3
"""Offline GS2-08.9 checks for the retired release/publication routes."""

from __future__ import annotations

import hashlib
import copy
import ast
import json
import os
import pathlib
import re
import subprocess
import tempfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
BOUND = {
    ".github/workflows/release-coord-engine.yml": "7973a57d19123afaafab7cc0cdc8e24a5cb3e891a97c00194be4fed9e1cc3ee5",
    ".github/workflows/release-drivers.yml": "b6e33e102ea9d7d30a8400887ab58a9953209c3dd65611eaf22a439de8b2866b",
    ".github/workflows/release-kit.yml": "caaa1f4dfad50930ccc91582be3fce87335d11bb72386ec9255d3ee62264f626",
    ".github/workflows/release-saga-prepare.yml": "704301a1ed74e863d638d016d835be956c95d68af579213cb5c4b21686877664",
    ".github/workflows/release-saga-start.yml": "316e019d8cec6c60cd78059e5b98abb85b925fa1564f6df9f86345cd00b6d912",
}
QUALIFICATION_ONLY = (
    ".github/workflows/kit-auto-publish.yml",
    ".github/workflows/release-telemetry-host.yml",
)
WIZARD_SUCCESSOR = ".github/workflows/release-new-sdd-workspace.yml"
FORBIDDEN = (
    "create-github-app-token",
    "NuGet/login",
    "actions/upload-artifact",
    "dotnet nuget push",
    "git push",
    "gh release create",
    "gh release upload",
    "gh release edit",
    "gh workflow run",
    "repository_dispatch",
)


def digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def executable_text(path: pathlib.Path) -> str:
    return "\n".join(line for line in path.read_text().splitlines() if not re.match(r"^\s*#", line))


for relative, expected in BOUND.items():
    actual = digest(ROOT / relative)
    assert actual == expected, f"0.90-bound workflow drifted: {relative}: {actual}"

# The accepted workflows remain byte-bound to the immutable 0.90 release, while a
# successor source candidate may advance the coherent scalar. The retained
# kit-auto-publish workflow itself still refuses any version other than 0.90.0;
# this test protects its exact bytes and the reachable effect refusals below.

for relative in QUALIFICATION_ONLY:
    text = executable_text(ROOT / relative)
    assert "workflow_dispatch:" in text
    assert not re.search(r"^\s+(push|schedule):", text, re.MULTILINE), relative
    assert "contents: read" in text
    assert not re.search(r"\b(?:contents|packages|actions|id-token|attestations|pull-requests|issues): write\b", text), relative
    for token in FORBIDDEN:
        assert token not in text, f"{relative} retained forbidden effect token {token!r}"

# SVG-D5 0.12.0 explicitly supersedes only the wizard filename's no-effect seal.
# Its old OIDC policy remains bound to this filename, but its new effect boundary
# is the exact candidate, live-main and protected-journal successor adapter.
wizard = executable_text(ROOT / WIZARD_SUCCESSOR)
assert "workflow_dispatch:" in wizard and not re.search(r"^\s+(push|schedule):", wizard, re.MULTILINE)
assert "github.ref == 'refs/heads/main' && github.actor == 'EHotwagner'" in wizard
assert "environment: release-successor" in wizard
assert "candidate_archive_sha256:" in wizard and "publish:" in wizard
assert "verify_nuget_login:" in wizard and "uses: NuGet/login@v1" in wizard
assert "scripts/new-sdd-workspace-successor-publish.py" in wizard
assert "--preflight-only" in wizard and "--publish" in wizard
for token in ("dotnet nuget push", "gh release create", "gh release upload", "gh release edit", "git push"):
    assert token not in wizard, f"wizard workflow bypasses the successor adapter: {token}"
admission = (ROOT / "scripts/new_sdd_workspace_successor_admission.py").read_text()
assert f'WORKFLOW = "{WIZARD_SUCCESSOR}"' in admission

# COORD-BOARD-V2-01.5 source successor: keep the accepted 0.13 route
# as history and bind current bytes only after proving the retained boundaries.
def verify_publication_writer_scope(document, source):
    """Recover the entire accepted021 route through only the reviewed lock move."""
    current = next(row for row in document["routes"] if row["path"] == WIZARD_SUCCESSOR)
    assert current["sha256"] == hashlib.sha256(source.encode()).hexdigest()
    amendment = current["publicationConcurrencyAmendment"]
    assert amendment == {
        "unit": "PUBLICATION-EFFICIENCY-4753", "sourceOnly": True,
        "previousAcceptedSource": "0ef654b4d80c1d933e47fb6bf06636c9fae4926c",
        "previousWorkflowSha256": "6073f2e469e84c8bcca6a47f8376feabec46290fd617e4d9d527877217f35312",
        "previousRouteCanonicalSha256": "515ad83cf9fc0174b3ab8e75131ff4ef6d835fea3426c83badf947ef6a8a1a4b",
        "previousHistoricalAttestationsCanonicalSha256": "2564ab86cb0f1adf21889715ac642fc581e0bd26b945b2b7f28a97edcaeeae24",
        "writerGroup": "release-new-sdd-workspace-successor-publish",
        "writerJobs": ["publish", "recovery-complete"],
        "observationJobs": ["recovery-diagnostic"],
        "operationAcceptance": "Source only; no new publication, recovery, credential, installation, observation or adoption operation admitted.",
    }
    canonical = lambda value: hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    histories = document["historicalRouteAttestations"]
    assert canonical(histories[:-1]) == amendment["previousHistoricalAttestationsCanonicalSha256"]
    assert histories[-1]["acceptedSource"] == amendment["previousAcceptedSource"]
    assert histories[-1]["status"] == "non-current-history"
    prior = histories[-1]["route"]
    assert canonical(prior) == amendment["previousRouteCanonicalSha256"]
    retained = copy.deepcopy(current)
    del retained["publicationConcurrencyAmendment"]
    retained["sha256"] = amendment["previousWorkflowSha256"]
    assert retained == prior, "publication lock amendment changed a retained release boundary"
    fence = ("    concurrency:\n"
             "      group: release-new-sdd-workspace-successor-publish\n"
             "      cancel-in-progress: false\n")
    assert "\nconcurrency:\n" not in source
    assert source.count("    concurrency:\n") == source.count(fence) == 2
    restored = source
    for name in ("publish", "recovery-complete"):
        marker = "\n  " + name + ":\n"
        assert restored.count(marker + fence) == 1
        restored = restored.replace(marker + fence, marker, 1)
    assert restored.count("\njobs:\n") == 1
    restored = restored.replace("\njobs:\n", "\nconcurrency:\n  group: release-new-sdd-workspace-successor-publish\n  cancel-in-progress: false\n\njobs:\n", 1)
    assert hashlib.sha256(restored.encode()).hexdigest() == amendment["previousWorkflowSha256"], "concurrency amendment failed whole-workflow inversion"
    historical_document = copy.deepcopy(document)
    historical_document["historicalRouteAttestations"].pop()
    historical_document["routes"][document["routes"].index(current)] = copy.deepcopy(prior)
    return historical_document, restored


# CREATOR-PUBLIC-023-3010: the outermost source-only profile cut. It inverts only the closed
# Creator023 identity tokens, then hands the exact prior (accepted022 + writer-lock) document and
# workflow to the unchanged lock and 022 cuts below. Neither the 022 history nor the 022r1 journal
# is rewritten; the 023 route names its own fresh journal.
CREATOR023_TOKENS = (
    ("format('Wizard 0.23 successor {0}', inputs.candidate_run_id)", "format('Wizard 0.22 successor {0}', inputs.candidate_run_id)"),
    ("# main-directory Creator 0.23.0 successor. Default publication remains false.",
     "# main-directory Creator 0.22.0 successor. Default publication remains false."),
    ("    name: new-sdd-workspace-023-publisher [org-publication-writer]\n", "    name: new-sdd-workspace-022-publisher [org-publication-writer]\n"),
    ("          name: creator023-publication-summary-${{ github.run_id }}\n",
     "          name: creator022-publication-summary-${{ github.run_id }}\n"),
    ("          path: ${{ runner.temp }}/creator023-publication-summary.json\n",
     "          path: ${{ runner.temp }}/creator022-publication-summary.json\n"),
)


def verify_creator023_profile(document, source):
    """Recover the entire accepted 022 route through only the 023 identity cut."""
    current = next(row for row in document["routes"] if row["path"] == WIZARD_SUCCESSOR)
    assert current["sha256"] == hashlib.sha256(source.encode()).hexdigest()
    amendment = current["creator023ProfileAmendment"]
    assert amendment == {
        "unit": "CREATOR-PUBLIC-023-3010", "sourceOnly": True,
        "previousAcceptedSource": "c799a9f9b21d52eb9766cf0950722e3e55145d39",
        "previousWorkflowSha256": "5983242c6154d2adfeb62e5d86fc7d9097d42bc5240ed6fb40bb728ec4792068",
        "previousRouteCanonicalSha256": "8aeadde14cea9d1859192e6dba782c1fc1d0f27a90e6129ade28c6b79aa17beb",
        "previousHistoricalAttestationsCanonicalSha256": "228e75d6f3056b47aba10a1909edbf86040b106ad114227b2a66d4f181087c00",
        "version": "0.23.0", "currentBinding": "CURRENT_023", "retainedBinding": "CURRENT_022",
        "publishedDependencyCoherentVersion": "0.113.0",
        "freshLogicalJournal": "board-v2-product-creator-023",
        "retainedJournals": ["board-v2-product-creator-022", "board-v2-product-creator-022r1"],
        "supersededRouteFields": "creator022ProfileAmendment and the 022 journal evidence retain the accepted022 profile so this route inverts exactly; this amendment selects CURRENT_023 as the current binding. Neither the abandoned 022 journal nor the published 022r1 journal is reused or reset.",
        "originalH4Accepted": False, "oldEffectGrantRenewed": False,
        "operationAcceptance": "Source only; no023 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance.",
    }
    canonical = lambda value: hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    histories = document["historicalRouteAttestations"]
    assert canonical(histories[:-1]) == amendment["previousHistoricalAttestationsCanonicalSha256"]
    assert histories[-1]["acceptedSource"] == amendment["previousAcceptedSource"]
    assert histories[-1]["status"] == "non-current-history"
    prior = histories[-1]["route"]
    assert canonical(prior) == amendment["previousRouteCanonicalSha256"]
    assert prior["creator022ProfileAmendment"]["freshLogicalJournal"] == "board-v2-product-creator-022r1"
    assert amendment["freshLogicalJournal"] not in (prior["creator022ProfileAmendment"]["freshLogicalJournal"],
                                                    prior["creator022ProfileAmendment"]["abandonedJournal"]["logicalRef"].rsplit("/", 1)[-1])
    retained = copy.deepcopy(current)
    del retained["creator023ProfileAmendment"]
    retained["sha256"] = amendment["previousWorkflowSha256"]
    assert retained == prior, "Creator023 profile amendment changed a retained release boundary"
    restored = source
    for new, old in CREATOR023_TOKENS:
        assert restored.count(new) == 1 and restored.count(old) == 0, "Creator023 identity token is not exact: " + new
        restored = restored.replace(new, old, 1)
    assert "0.23" not in restored and "023" not in restored
    assert hashlib.sha256(restored.encode()).hexdigest() == amendment["previousWorkflowSha256"], "Creator023 profile failed whole-workflow inversion"
    historical_document = copy.deepcopy(document)
    historical_document["historicalRouteAttestations"].pop()
    historical_document["routes"][document["routes"].index(current)] = copy.deepcopy(prior)
    return historical_document, restored


# CREATOR-PUBLIC-022-4811: the outermost source-only profile cut. It inverts only the
# closed Creator022 identity tokens, then hands the exact prior document/workflow to
# the unchanged writer-scope proof below.
CREATOR022_TOKENS = (
    ("format('Wizard 0.22 successor {0}', inputs.candidate_run_id)", "format('Wizard 0.21 successor {0}', inputs.candidate_run_id)"),
    ("# main-directory Creator 0.22.0 successor. Default publication remains false.",
     "# main-directory Creator 0.21.0 successor. Default publication remains false."),
    ("    name: new-sdd-workspace-022-publisher\n", "    name: new-sdd-workspace-021-publisher\n"),
    ("          name: creator022-publication-summary-${{ github.run_id }}\n",
     "          name: creator021-publication-summary-${{ github.run_id }}\n"),
    ("          path: ${{ runner.temp }}/creator022-publication-summary.json\n",
     "          path: ${{ runner.temp }}/creator021-publication-summary.json\n"),
)


def verify_creator022_profile(document, source):
    """Recover the entire accepted writer-scope route through only the022 identity cut."""
    current = next(row for row in document["routes"] if row["path"] == WIZARD_SUCCESSOR)
    assert current["sha256"] == hashlib.sha256(source.encode()).hexdigest()
    amendment = current["creator022ProfileAmendment"]
    assert amendment == {
        "unit": "CREATOR-PUBLIC-022-4811", "sourceOnly": True,
        "previousAcceptedSource": "c0ad3a3012013c41a4a64cf645907f9a7f797483",
        "previousWorkflowSha256": "f7ebde70da11a060c7a047f38e4033ccb5d60b70c25ebbd339eb51e75c77be08",
        "previousRouteCanonicalSha256": "9e26622594a0c322c8a9e6b578c0dce57588119d188fea2a4aa16525f265184b",
        "previousHistoricalAttestationsCanonicalSha256": "8424093d1a0bb0d4a3abb1b4154012ce5cdca1b161811260d5eb7be50ff01774",
        "version": "0.22.0", "currentBinding": "CURRENT_022", "retainedBinding": "CURRENT_021",
        "publishedDependencyCoherentVersion": "0.112.0",
        "freshLogicalJournal": "board-v2-product-creator-022r1",
        "abandonedJournal": {"logicalRef": "refs/heads/fsgg/v2/journal/release/board-v2-product-creator-022", "physicalDirectory": "state/releases/board-v2-product-creator-022", "status": "abandoned-unsent-intent", "candidateRunId": 39923792809, "candidateArtifactId": 12489061170, "candidateArchiveSha256": "9e7275110f8501675aaaa9b17cea2f74186991416addf999081cf80bf41f016a", "candidateSourceSha": "150d5c43fcd2108f2cceea3270bf1146afd9984b", "publisherRunId": 39925400140, "contentId": "sha256:6846038993c96bc6db33de872e47c3eaccdd4a6485b70dd2d0068b1bc0de4037", "generations": [{"generation": 1, "logicalCommit": "81c4f827235f9a9048c073ce75c22781a35f2732", "physicalCommit": "7874ba134aabc7d9f6e91dfefba847a4d22ca0b5", "effects": {}}, {"generation": 2, "logicalCommit": "ed5d034956170043cfd143d9d4c382945d9cbbea", "physicalCommit": "011386cff81dc4f540e94080682534160449670f", "effects": {"tag": "intent"}}], "finalStateSha256": "9885944dbbd38f56fa0a475a0d2688856a73775aeee53cadabd90be8be9c8d24", "intentNeverSentEvidence": "publisher summary lastDispatch null and complete false; Authority reads 52 attempted/51 succeeded; tag new-sdd-workspace/v0.22.0 404, no 0.22.0 release, nuget.org 0.22.0 404 at readback 2026-10-23", "disposition": "Never resumed: the engine treats intent plus absent as waiting, so this journal cannot settle; 022r1 is the fresh successor journal."},
        "supersededRouteFields": "successorAmendment and bindingIsolationProof.currentBinding retain the accepted021 profile so this route inverts exactly; this amendment selects CURRENT_022 as the current binding.",
        "originalH4Accepted": False, "oldEffectGrantRenewed": False,
        "operationAcceptance": "Source only; no022 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance.",
    }
    canonical = lambda value: hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    histories = document["historicalRouteAttestations"]
    assert canonical(histories[:-1]) == amendment["previousHistoricalAttestationsCanonicalSha256"]
    assert histories[-1]["acceptedSource"] == amendment["previousAcceptedSource"]
    assert histories[-1]["status"] == "non-current-history"
    prior = histories[-1]["route"]
    assert canonical(prior) == amendment["previousRouteCanonicalSha256"]
    retained = copy.deepcopy(current)
    del retained["creator022ProfileAmendment"]
    retained["sha256"] = amendment["previousWorkflowSha256"]
    assert retained == prior, "Creator022 profile amendment changed a retained release boundary"
    restored = source
    for new, old in CREATOR022_TOKENS:
        assert restored.count(new) == 1 and restored.count(old) == 0, "Creator022 identity token is not exact: " + new
        restored = restored.replace(new, old, 1)
    assert "0.22" not in restored and "022" not in restored
    assert hashlib.sha256(restored.encode()).hexdigest() == amendment["previousWorkflowSha256"], "Creator022 profile failed whole-workflow inversion"
    historical_document = copy.deepcopy(document)
    historical_document["historicalRouteAttestations"].pop()
    historical_document["routes"][document["routes"].index(current)] = copy.deepcopy(prior)
    return historical_document, restored



# PUBLICATION-WRITER-LOCK-4753 (ADR-0097): the outermost source-only cut replaces the two
# repository-scoped writer fences with the org-wide publication writer lock step. It inverts
# only that move, then hands the exact prior document/workflow to the unchanged proofs below.
LOCK_GATE = ("      - name: Acquire org publication writer lock\n"
             "        uses: ./.github/actions/publication-writer-lock\n"
             "        with:\n")


def verify_publication_lock_cut(document, source):
    current = next(row for row in document["routes"] if row["path"] == WIZARD_SUCCESSOR)
    assert current["sha256"] == hashlib.sha256(source.encode()).hexdigest()
    amendment = current["publicationWriterLockAmendment"]
    assert set(amendment) == {"unit", "sourceOnly", "previousAcceptedSource", "previousWorkflowSha256",
                              "previousRouteCanonicalSha256", "previousHistoricalAttestationsCanonicalSha256",
                              "decision", "lockAction", "gateStep", "replacedGroup", "writerJobs",
                              "observationJobs", "operationAcceptance", "jobNameMarker", "timeouts"}
    assert {k: amendment[k] for k in ("unit", "sourceOnly", "decision", "lockAction", "gateStep", "replacedGroup",
                                      "writerJobs", "observationJobs", "operationAcceptance", "jobNameMarker", "timeouts")} == {
        "jobNameMarker": "[org-publication-writer]",
        "timeouts": {"publish": {"from": 60, "to": 90}, "recovery-complete": {"from": 28, "to": 48}},
        "unit": "PUBLICATION-WRITER-LOCK-4753", "sourceOnly": True, "decision": "ADR-0097",
        "lockAction": ".github/actions/publication-writer-lock", "gateStep": "Acquire org publication writer lock",
        "replacedGroup": "release-new-sdd-workspace-successor-publish",
        "writerJobs": ["publish", "recovery-complete"], "observationJobs": ["recovery-diagnostic"],
        "operationAcceptance": "Source only; no publication, recovery, credential, installation, observation or adoption operation admitted.",
    }
    canonical = lambda value: hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    histories = document["historicalRouteAttestations"]
    assert canonical(histories[:-1]) == amendment["previousHistoricalAttestationsCanonicalSha256"]
    assert histories[-1]["acceptedSource"] == amendment["previousAcceptedSource"]
    assert histories[-1]["status"] == "non-current-history"
    prior = histories[-1]["route"]
    assert canonical(prior) == amendment["previousRouteCanonicalSha256"]
    retained = copy.deepcopy(current)
    del retained["publicationWriterLockAmendment"]
    retained["sha256"] = amendment["previousWorkflowSha256"]
    assert retained == prior, "publication lock cut changed a retained release boundary"
    fence = ("    concurrency:\n"
             "      group: release-new-sdd-workspace-successor-publish\n"
             "      cancel-in-progress: false\n")
    assert "concurrency:" not in source, "the repository-scoped group must be gone"
    gate = re.compile(re.escape(LOCK_GATE) + r"          max-wait-minutes: [0-9]+\n        timeout-minutes: [0-9]+\n")
    assert len(gate.findall(source)) == source.count("Acquire org publication writer lock") == 2
    restored = gate.sub("", source)
    marked = " [org-publication-writer]\n"
    assert restored.count(marked) == 2, "exactly the two writer jobs carry the name marker"
    restored = restored.replace(marked, "\n")
    for name, (was, now) in (("publish", (60, 90)), ("recovery-complete", (28, 48))):
        marker = "\n  " + name + ":\n"
        assert restored.count(marker) == 1
        head, tail = restored.split(marker, 1)
        body_end = tail.index("\n  recovery-" ) if "\n  recovery-" in tail else len(tail)
        job, rest = tail[:body_end], tail[body_end:]
        old_line, new_line = f"    timeout-minutes: {now}\n", f"    timeout-minutes: {was}\n"
        assert job.count(old_line) == 1, "writer job timeout carries only the declared lock wait"
        restored = head + marker + fence + job.replace(old_line, new_line, 1) + rest
    assert hashlib.sha256(restored.encode()).hexdigest() == amendment["previousWorkflowSha256"], "lock cut failed whole-workflow inversion"
    historical_document = copy.deepcopy(document)
    historical_document["historicalRouteAttestations"].pop()
    historical_document["routes"][document["routes"].index(current)] = copy.deepcopy(prior)
    return historical_document, restored


top_report = json.loads((ROOT / "docs/reports/gs2-08-9-release-route-dispositions.json").read_text())
top_workflow = (ROOT / WIZARD_SUCCESSOR).read_text()
full_report, full_workflow = verify_creator023_profile(top_report, top_workflow)
for old, new in (
    ("    name: new-sdd-workspace-023-publisher [org-publication-writer]", "    name: new-sdd-workspace-022-publisher [org-publication-writer]"),
    ("creator023-publication-summary.json", "creator022-publication-summary.json"),
    ("permission-contents: read", "permission-contents: write"),
    ("inputs.promotion_recovery == 'off'", "true"),
    ("environment: release-successor", "environment: other"),
):
    assert old in top_workflow
    mutant = top_workflow.replace(old, new, 1)
    repinned = copy.deepcopy(top_report)
    next(row for row in repinned["routes"] if row["path"] == WIZARD_SUCCESSOR)["sha256"] = hashlib.sha256(mutant.encode()).hexdigest()
    try:
        verify_creator023_profile(repinned, mutant)
    except AssertionError:
        pass
    else:
        raise AssertionError("repinned Creator023 profile mutant accepted: " + old)
for defect in ("history-pop", "history-status", "amendment-version", "retained-route", "reused-022r1-journal", "wrong-binding"):
    mutated = copy.deepcopy(top_report)
    row = next(row for row in mutated["routes"] if row["path"] == WIZARD_SUCCESSOR)
    if defect == "history-pop": mutated["historicalRouteAttestations"].pop()
    if defect == "history-status": mutated["historicalRouteAttestations"][-1]["status"] = "current"
    if defect == "amendment-version": row["creator023ProfileAmendment"]["version"] = "0.22.0"
    if defect == "retained-route": row["disposition"] = "forgiven"
    if defect == "reused-022r1-journal": row["creator023ProfileAmendment"]["freshLogicalJournal"] = "board-v2-product-creator-022r1"
    if defect == "wrong-binding": row["creator023ProfileAmendment"]["currentBinding"] = "CURRENT_022"
    try:
        verify_creator023_profile(mutated, top_workflow)
    except (AssertionError, KeyError, IndexError):
        pass
    else:
        raise AssertionError("Creator023 profile document defect accepted: " + defect)
print("3010 Creator023 profile cut: whole prior 022 workflow/history recovered; five repinned workflow and six document mutants refused")
current_report, current_workflow = verify_publication_lock_cut(full_report, full_workflow)
for old, new in (
    ("          max-wait-minutes: 30\n        timeout-minutes: 32\n", "          max-wait-minutes: 30\n        timeout-minutes: 32\n        env:\n          X: 1\n"),
    ("uses: ./.github/actions/publication-writer-lock", "uses: ./.github/actions/other-lock"),
    ("  publish:\n", "  publish:\n    concurrency:\n      group: other\n"),
    ("environment: release-successor", "environment: other"),
    ("github.actor == 'EHotwagner'", "true"),
):
    assert old in full_workflow
    mutant = full_workflow.replace(old, new, 1)
    repinned = copy.deepcopy(full_report)
    next(row for row in repinned["routes"] if row["path"] == WIZARD_SUCCESSOR)["sha256"] = hashlib.sha256(mutant.encode()).hexdigest()
    try:
        verify_publication_lock_cut(repinned, mutant)
    except AssertionError:
        pass
    else:
        raise AssertionError("repinned publication lock mutant accepted: " + old)
for defect in ("history-pop", "history-status", "amendment-unit", "retained-route", "extra-gate", "ungated-recovery"):
    mutated, source = copy.deepcopy(full_report), full_workflow
    row = next(row for row in mutated["routes"] if row["path"] == WIZARD_SUCCESSOR)
    if defect == "history-pop": mutated["historicalRouteAttestations"].pop()
    if defect == "history-status": mutated["historicalRouteAttestations"][-1]["status"] = "current"
    if defect == "amendment-unit": row["publicationWriterLockAmendment"]["unit"] = "other"
    if defect == "retained-route": row["disposition"] = "forgiven"
    if defect == "extra-gate":
        source = full_workflow.replace(LOCK_GATE + "          max-wait-minutes: 30\n        timeout-minutes: 32\n", (LOCK_GATE + "          max-wait-minutes: 30\n        timeout-minutes: 32\n") * 2, 1)
        row["sha256"] = hashlib.sha256(source.encode()).hexdigest()
    if defect == "ungated-recovery":
        tail = full_workflow.rindex(LOCK_GATE)
        source = full_workflow[:tail] + full_workflow[tail:].replace(LOCK_GATE + "          max-wait-minutes: 20\n        timeout-minutes: 22\n", "", 1)
        row["sha256"] = hashlib.sha256(source.encode()).hexdigest()
    try:
        verify_publication_lock_cut(mutated, source)
    except (AssertionError, KeyError, IndexError):
        pass
    else:
        raise AssertionError("publication lock document defect accepted: " + defect)
print("4753 org publication writer lock cut: prior workflow/history recovered by exact inversion; five repinned workflow and six document mutants refused")
actual_report, actual_workflow = verify_creator022_profile(current_report, current_workflow)
for old, new in (
    ("    name: new-sdd-workspace-022-publisher", "    name: new-sdd-workspace-021-publisher"),
    ("creator022-publication-summary.json", "creator021-publication-summary.json"),
    ("permission-contents: read", "permission-contents: write"),
    ("inputs.promotion_recovery == 'off'", "true"),
    ("environment: release-successor", "environment: other"),
):
    assert old in current_workflow
    mutant = current_workflow.replace(old, new, 1)
    repinned = copy.deepcopy(current_report)
    next(row for row in repinned["routes"] if row["path"] == WIZARD_SUCCESSOR)["sha256"] = hashlib.sha256(mutant.encode()).hexdigest()
    try:
        verify_creator022_profile(repinned, mutant)
    except AssertionError:
        pass
    else:
        raise AssertionError("repinned Creator022 profile mutant accepted: " + old)
for defect in ("history-pop", "history-status", "amendment-version", "retained-route"):
    mutated = copy.deepcopy(current_report)
    row = next(row for row in mutated["routes"] if row["path"] == WIZARD_SUCCESSOR)
    if defect == "history-pop": mutated["historicalRouteAttestations"].pop()
    if defect == "history-status": mutated["historicalRouteAttestations"][-1]["status"] = "current"
    if defect == "amendment-version": row["creator022ProfileAmendment"]["version"] = "0.21.0"
    if defect == "retained-route": row["disposition"] = "forgiven"
    try:
        verify_creator022_profile(mutated, current_workflow)
    except (AssertionError, KeyError, IndexError):
        pass
    else:
        raise AssertionError("Creator022 profile document defect accepted: " + defect)
print("4811 Creator022 profile cut: whole prior workflow/history recovered; five repinned workflow and four document mutants refused")
report, historical_workflow = verify_publication_writer_scope(actual_report, actual_workflow)
# Even repinning the current digest cannot excuse changes outside this exact move.
for old, new in (
    ("cancel-in-progress: false", "cancel-in-progress: true"),
    ("      group: release-new-sdd-workspace-successor-publish", "      group: ${{ inputs.candidate_run_id }}"),
    ("  recovery-diagnostic:\n", "  recovery-diagnostic:\n    concurrency: observe"),
    ("permission-contents: read", "permission-contents: write"),
    ("inputs.promotion_recovery == 'off'", "true"),
):
    assert old in actual_workflow
    mutant = actual_workflow.replace(old, new, 1)
    repinned = copy.deepcopy(actual_report)
    next(row for row in repinned["routes"] if row["path"] == WIZARD_SUCCESSOR)["sha256"] = hashlib.sha256(mutant.encode()).hexdigest()
    try:
        verify_publication_writer_scope(repinned, mutant)
    except AssertionError:
        pass
    else:
        raise AssertionError("repinned publication writer-scope mutant accepted: " + old)
print("4753 exact writer-lock move: whole021 workflow/history retained; five repinned scope/authority mutants refused")
route = next(row for row in report["routes"] if row["path"] == WIZARD_SUCCESSOR)
assert route["sha256"] == hashlib.sha256(historical_workflow.encode()).hexdigest()
assert route["disposition"] == "exact-source-protected-journal-successor-only"
amendment = route["successorAmendment"]
assert amendment["unit"] == "CREATOR-PUBLIC-021-4741" and amendment["version"] == "0.21.0"
assert amendment["operationAcceptance"] == "Source only; no021 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance."
creator020_history = next(row for row in report["historicalRouteAttestations"]
                         if row["acceptedSource"] == amendment["previousAcceptedSource"])
history = next(row for row in report["historicalRouteAttestations"]
               if row["acceptedSource"] == route["bindingIsolationProof"]["creator020ProfileAmendment"]["previousAcceptedSource"])
assert history["status"] == "non-current-history"
assert creator020_history["route"]["sha256"] == amendment["previousSuccessorWorkflowSha256"]
for key in ("forwardRecoveryAmendment", "capabilityLoss", "historicalCapabilityLoss", "historicalDisposition"):
    assert route[key] == history["route"][key], f"retained recovery/retirement boundary changed: {key}"
proof = route["bindingIsolationProof"]
creator017_predecessor = next(row for row in report["historicalRouteAttestations"]
                             if row["acceptedSource"] == proof["creator017ProfileAmendment"]["previousAcceptedSource"])
assert history["route"]["bindingIsolationProof"]["currentBinding"] == "CURRENT_019"
creator019_predecessor = next(row for row in report["historicalRouteAttestations"]
                             if row["acceptedSource"] == proof["creator019ProfileAmendment"]["previousAcceptedSource"])


def verify_creator019_history(document, current):
    rows = [row for row in document["historicalRouteAttestations"]
            if row["acceptedSource"] == "cc1d3c12c1ed7e7128012063d7ab478c7198b523"]
    assert len(rows) == 1 and rows[0]["status"] == "non-current-history"
    retained = rows[0]["route"]
    canonical = lambda value: hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    assert canonical(retained) == "320b48ed6d783699513f82ed3cfd1864fa42006c8419d5723a1b41afc7235920"
    assert canonical([row for row in document["historicalRouteAttestations"] if row is not rows[0] and row["acceptedSource"] != "06b83a4695f5b5ca81bc8d0e974465cf801d1e03"]) == "090ac266bdc33e918148c2f301886a8144deff88087c6840516ac91ecb26fca3"
    older = dict(current["bindingIsolationProof"])
    del older["creator020ProfileAmendment"]
    older["currentBinding"] = "CURRENT_019"
    assert older == retained["bindingIsolationProof"], "historical proof was rewritten to admit020"
    return retained


verify_creator019_history(report, creator020_history["route"])
for mutation in ("missing019", "changed019", "changedOlder", "repinnedProof"):
    document, current = copy.deepcopy(report), copy.deepcopy(creator020_history["route"])
    if mutation == "missing019":
        document["historicalRouteAttestations"] = [row for row in document["historicalRouteAttestations"]
            if row["acceptedSource"] != "cc1d3c12c1ed7e7128012063d7ab478c7198b523"]
    elif mutation == "changed019":
        next(row for row in document["historicalRouteAttestations"] if row["acceptedSource"] == "cc1d3c12c1ed7e7128012063d7ab478c7198b523")["route"]["bindingIsolationProof"]["currentBinding"] = "CURRENT_020"
    elif mutation == "changedOlder":
        document["historicalRouteAttestations"][0]["status"] = "forgiven"
    else:
        current["bindingIsolationProof"]["creator019ProfileAmendment"]["currentPublishJobSourceSha256"] = "0" * 64
    try:
        verify_creator019_history(document, current)
    except (AssertionError, KeyError):
        pass
    else:
        raise AssertionError("immutable Creator019 attestation mutation passed: " + mutation)
creator018_predecessor = next(row for row in report["historicalRouteAttestations"]
                             if row["acceptedSource"] == proof["creator018ProfileAmendment"]["previousAcceptedSource"])
for name, expected in proof["currentRecoveryJobSha256"].items():
    raw_workflow = historical_workflow
    start = raw_workflow.index("  " + name + ":\n")
    end = raw_workflow.find("\n  recovery-", start + 1)
    assert hashlib.sha256(raw_workflow[start:end if end != -1 else None].encode()).hexdigest() == expected
# Selected draft-readable token capability is source-constrained GET-only, not
# a platform read-only credential. Parse the exact existing job permission map.
def workflow_jobs(source):
    body = source[source.index("jobs:\n") + 6:]
    matches = list(re.finditer(r"^  ([a-z][a-z-]+):\n", body, re.MULTILINE))
    assert len({m[1] for m in matches}) == len(matches), "duplicate job"
    return {m[1]: body[m.start():matches[i + 1].start() if i + 1 < len(matches) else None]
            for i, m in enumerate(matches)}

CREATOR_SUMMARY_STEP = """      - name: Retain only the bounded Creator publication summary
        if: always()
        timeout-minutes: 2
        uses: actions/upload-artifact@v7
        with:
          name: creator016-publication-summary-${{ github.run_id }}
          path: ${{ runner.temp }}/creator016-publication-summary.json
          if-no-files-found: error
          retention-days: 7

"""

def verify_creator_publish_job(job, amendment):
    # The original readback amendment remains an immutable historical snapshot.
    # Removing this exact summary step must recover its original ordinary job.
    assert set(amendment) == {"unit", "scope", "previousPublishJobSourceSha256", "currentPublishJobSourceSha256", "summaryArtifactName", "summaryPath", "originalH4Accepted", "oldEffectGrantRenewed", "operationAcceptance"}
    assert amendment["unit"] == "COORD-BOARD-V2-01.6"
    previous = proof["postCompletionReadbackAmendment"]["currentJobSourceSha256"]["publish"]
    assert previous == proof["draftReadCapabilityAmendment"]["currentJobSourceSha256"]["publish"] == "21c6dbe4b43845835a01062baa1c600eb087a1f96007cbf3a332142d0b85b797"
    assert amendment["previousPublishJobSourceSha256"] == previous
    assert amendment["summaryArtifactName"] == "creator016-publication-summary-${{ github.run_id }}"
    assert amendment["summaryPath"] == "${{ runner.temp }}/creator016-publication-summary.json"
    assert amendment["originalH4Accepted"] is False and amendment["oldEffectGrantRenewed"] is False
    assert amendment["operationAcceptance"] == "Source only; no candidate, journal initialization, protected preflight or Creator016 publication acceptance."
    assert job.count(CREATOR_SUMMARY_STEP) == 1
    assert hashlib.sha256(job.encode()).hexdigest() == amendment["currentPublishJobSourceSha256"]
    assert hashlib.sha256(job.replace(CREATOR_SUMMARY_STEP, "", 1).encode()).hexdigest() == previous

CREATOR017_SUMMARY_STEP = CREATOR_SUMMARY_STEP.replace("creator016-", "creator017-")

def verify_creator017_publish_job(job, amendment):
    assert set(amendment) == {"unit", "scope", "previousAcceptedSource", "previousSuccessorWorkflowSha256", "previousPublishJobSourceSha256", "currentPublishJobSourceSha256", "currentBinding", "retainedBinding", "coherentSourceVersion", "freshLogicalJournal", "originalH4Accepted", "oldEffectGrantRenewed", "operationAcceptance"}
    assert amendment["unit"] == "COORD-BOARD-V2-01.11"
    assert amendment["previousAcceptedSource"] == "2e60210be1e04d52fe0735a56333b3e3aef42284"
    assert amendment["previousSuccessorWorkflowSha256"] == creator017_predecessor["route"]["sha256"]
    assert amendment["previousPublishJobSourceSha256"] == proof["creator016PublicationSummaryAmendment"]["currentPublishJobSourceSha256"]
    assert amendment["currentBinding"] == "CURRENT_017"
    assert amendment["retainedBinding"] == "CURRENT_016"
    assert amendment["coherentSourceVersion"] == "0.107.0"
    assert amendment["freshLogicalJournal"] == "board-v2-product-creator-017"
    assert amendment["originalH4Accepted"] is False and amendment["oldEffectGrantRenewed"] is False
    assert amendment["operationAcceptance"] == "Source only; no candidate, journal initialization, protected preflight or Creator017 publication acceptance."
    assert hashlib.sha256(job.encode()).hexdigest() == amendment["currentPublishJobSourceSha256"]
    replacements = (
        ("    name: new-sdd-workspace-017-publisher\n", "    name: new-sdd-workspace-014-publisher\n"),
        ("          name: creator017-publication-summary-", "          name: creator016-publication-summary-"),
        ("          path: ${{ runner.temp }}/creator017-publication-summary.json\n", "          path: ${{ runner.temp }}/creator016-publication-summary.json\n"),
    )
    retained = job
    for current, previous in replacements:
        assert retained.count(current) == 1
        retained = retained.replace(current, previous, 1)
    assert hashlib.sha256(retained.encode()).hexdigest() == amendment["previousPublishJobSourceSha256"]
    verify_creator_publish_job(retained, proof["creator016PublicationSummaryAmendment"])

CREATOR018_SUMMARY_STEP = CREATOR_SUMMARY_STEP.replace("creator016-", "creator018-")
CURRENT_PIN_PREFLIGHT_STEP = """      - name: Refuse absent promoted dependency pin before credentials or effects
        run: python3 scripts/creator-current-coord-dependencies.py preflight
"""

def verify_creator018_publish_job(job, amendment):
    assert amendment["unit"] == "CBC-01"
    assert amendment["currentBinding"] == "CURRENT_018"
    assert amendment["retainedBindings"] == ["CURRENT_017", "CURRENT_016", "HISTORICAL_013"]
    assert amendment["coherentSourceVersion"] == "0.108.0"
    assert amendment["freshLogicalJournal"] == "board-v2-product-creator-018"
    assert amendment["dependencyMode"] == "published-dependency"
    assert amendment["realPinPresent"] is False and amendment["oldEffectGrantRenewed"] is False
    assert amendment["previousAcceptedSource"] == creator018_predecessor["acceptedSource"]
    assert amendment["previousSuccessorWorkflowSha256"] == creator018_predecessor["route"]["sha256"]
    assert amendment["previousPublishJobSourceSha256"] == proof["creator017ProfileAmendment"]["currentPublishJobSourceSha256"]
    assert hashlib.sha256(job.encode()).hexdigest() == amendment["currentPublishJobSourceSha256"]
    assert job.count(CURRENT_PIN_PREFLIGHT_STEP) == 1
    assert job.index(CURRENT_PIN_PREFLIGHT_STEP) < job.index("actions/setup-dotnet") < job.index("Mint the ordinary")
    retained = job.replace(CURRENT_PIN_PREFLIGHT_STEP, "", 1)
    for current, previous in (("new-sdd-workspace-018-publisher", "new-sdd-workspace-017-publisher"),
                              ("creator018-publication-summary-", "creator017-publication-summary-"),
                              ("creator018-publication-summary.json", "creator017-publication-summary.json")):
        assert retained.count(current) == 1
        retained = retained.replace(current, previous, 1)
    assert hashlib.sha256(retained.encode()).hexdigest() == amendment["previousPublishJobSourceSha256"]
    verify_creator017_publish_job(retained, proof["creator017ProfileAmendment"])

CREATOR019_SUMMARY_STEP = CREATOR_SUMMARY_STEP.replace("creator016-", "creator019-")

def verify_creator019_publish_job(job, amendment):
    assert amendment["unit"] == "CBC-05-REPAIR019"
    assert amendment["currentBinding"] == "CURRENT_019"
    assert amendment["retainedBindings"] == ["CURRENT_018", "CURRENT_017", "CURRENT_016", "HISTORICAL_013"]
    assert amendment["coherentSourceVersion"] == "0.108.0"
    assert amendment["freshLogicalJournal"] == "board-v2-product-creator-019"
    assert amendment["dependencyMode"] == "published-dependency"
    assert amendment["realPinPresent"] is True and amendment["oldEffectGrantRenewed"] is False
    assert amendment["operationAcceptance"] == "Source only; no019 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance."
    assert amendment["previousAcceptedSource"] == creator019_predecessor["acceptedSource"] == "f704a3e6e6b9b0149fe5b8e39b4fefb5d42cddb1"
    assert amendment["previousSuccessorWorkflowSha256"] == creator019_predecessor["route"]["sha256"]
    assert amendment["previousPublishJobSourceSha256"] == proof["creator018ProfileAmendment"]["currentPublishJobSourceSha256"]
    assert hashlib.sha256(job.encode()).hexdigest() == amendment["currentPublishJobSourceSha256"]
    retained = job
    for current, previous in (("new-sdd-workspace-019-publisher", "new-sdd-workspace-018-publisher"),
                              ("creator019-publication-summary-", "creator018-publication-summary-"),
                              ("creator019-publication-summary.json", "creator018-publication-summary.json")):
        assert retained.count(current) == 1
        retained = retained.replace(current, previous, 1)
    assert hashlib.sha256(retained.encode()).hexdigest() == amendment["previousPublishJobSourceSha256"]
    verify_creator018_publish_job(retained, proof["creator018ProfileAmendment"])

CREATOR020_SUMMARY_STEP = CREATOR_SUMMARY_STEP.replace("creator016-", "creator020-")
CREATOR020_JOB_REVERSE = (
    ("new-sdd-workspace-020-publisher", "new-sdd-workspace-019-publisher"),
    ("creator020-publication-summary-", "creator019-publication-summary-"),
    ("creator020-publication-summary.json", "creator019-publication-summary.json"),
)


def reverse_creator020(source, replacements):
    for current, previous in replacements:
        assert source.count(current) == 1
        source = source.replace(current, previous, 1)
    return source


def verify_creator020_publish_job(job, amendment):
    assert set(amendment) == {"unit", "previousAcceptedSource", "previousSuccessorWorkflowSha256",
        "previousPublishJobSourceSha256", "currentPublishJobSourceSha256", "previousRouteCanonicalSha256",
        "previousHistoricalAttestationsCanonicalSha256", "currentBinding", "retainedBindings", "coherentSourceVersion",
        "publishedDependencyVersion", "dependencyPinFileSha256", "freshLogicalJournal", "dependencyMode",
        "realPinPresent", "oldEffectGrantRenewed", "operationAcceptance"}
    assert amendment["unit"] == "BOARD-LIFECYCLE-01.3"
    assert amendment["currentBinding"] == creator020_history["route"]["bindingIsolationProof"]["currentBinding"] == "CURRENT_020"
    assert amendment["retainedBindings"] == ["CURRENT_019", "CURRENT_018", "CURRENT_017", "CURRENT_016", "HISTORICAL_013"]
    assert amendment["coherentSourceVersion"] == "0.109.0" and amendment["publishedDependencyVersion"] == "0.108.0"
    assert amendment["dependencyPinFileSha256"] == digest(ROOT / "scripts/creator-current-coord-dependencies.json") == "4e6c2ed8f0d2dc164e74fc205b1aae233dae194035bbf811f069413ba7dd80a6"
    assert amendment["freshLogicalJournal"] == "board-v2-product-creator-020"
    assert amendment["dependencyMode"] == "published-dependency" and amendment["realPinPresent"] is True
    assert amendment["oldEffectGrantRenewed"] is False
    assert amendment["operationAcceptance"] == "Source only; no020 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance."
    assert amendment["previousAcceptedSource"] == history["acceptedSource"] == "cc1d3c12c1ed7e7128012063d7ab478c7198b523"
    assert amendment["previousSuccessorWorkflowSha256"] == history["route"]["sha256"] == "32187c27adc67b178accc2e0cf6a395138de378d77d6cba4b2f133a9e7ada40d"
    assert amendment["previousRouteCanonicalSha256"] == "320b48ed6d783699513f82ed3cfd1864fa42006c8419d5723a1b41afc7235920"
    assert amendment["previousHistoricalAttestationsCanonicalSha256"] == "090ac266bdc33e918148c2f301886a8144deff88087c6840516ac91ecb26fca3"
    assert hashlib.sha256(job.encode()).hexdigest() == amendment["currentPublishJobSourceSha256"]
    retained = reverse_creator020(job, CREATOR020_JOB_REVERSE)
    assert hashlib.sha256(retained.encode()).hexdigest() == amendment["previousPublishJobSourceSha256"] == "f4613eadfa75007cef8eb6deabfb6f8779a8495d902f3df48617c23053a57729"
    verify_creator019_publish_job(retained, history["route"]["bindingIsolationProof"]["creator019ProfileAmendment"])


def verify_creator020_workflow(source):
    retained = reverse_creator020(source, CREATOR020_JOB_REVERSE + (
        ("format('Wizard 0.20 successor", "format('Wizard 0.19 successor"),
        ("main-directory Creator 0.20.0 successor", "main-directory Creator 0.19.0 successor"),
    ))
    assert hashlib.sha256(retained.encode()).hexdigest() == "32187c27adc67b178accc2e0cf6a395138de378d77d6cba4b2f133a9e7ada40d", "020 workflow does not recover immutable019 source"

CREATOR021_JOB_REVERSE = (
    ("new-sdd-workspace-021-publisher", "new-sdd-workspace-020-publisher"),
    ("creator021-publication-summary-", "creator020-publication-summary-"),
    ("creator021-publication-summary.json", "creator020-publication-summary.json"),
)
CREATOR021_SUMMARY_STEP = CREATOR_SUMMARY_STEP.replace("creator016-", "creator021-")


def canonical_digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def verify_creator021_history(document, current):
    rows = [row for row in document["historicalRouteAttestations"]
            if row["acceptedSource"] == "06b83a4695f5b5ca81bc8d0e974465cf801d1e03"]
    assert len(rows) == 1 and rows[0]["status"] == "non-current-history"
    retained = rows[0]["route"]
    assert canonical_digest(retained) == "8122798beb575a68f2d53a019e54fd6ed520d034bb2563e2cfea00370451a0d4"
    assert canonical_digest([row for row in document["historicalRouteAttestations"] if row is not rows[0]]) == "87f0f64627c977b9359442d677aaaa9b6826435837f8a29eeb5cbbd3cb3da84e"
    older = dict(current["bindingIsolationProof"])
    del older["creator021ProfileAmendment"]
    older["currentBinding"] = "CURRENT_020"
    assert older == retained["bindingIsolationProof"], "historical020 proof was rewritten to admit021"
    return retained


def verify_creator021_route(document, current, source):
    verify_creator021_history(document, current)
    assert current["path"] == WIZARD_SUCCESSOR
    assert current["sha256"] == hashlib.sha256(source.encode()).hexdigest()
    assert current["disposition"] == "exact-source-protected-journal-successor-only"
    selected = current["successorAmendment"]
    assert selected["unit"] == "CREATOR-PUBLIC-021-4741" and selected["version"] == "0.21.0"
    assert selected["previousAcceptedSource"] == "06b83a4695f5b5ca81bc8d0e974465cf801d1e03"
    assert selected["previousSuccessorWorkflowSha256"] == "d599d438d17084107aa7d2d3cc1147d947ac32957d729260719f97068066a10e"
    assert selected["operationAcceptance"] == "Source only; no021 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance."
    verify_creator021_workflow(source)
    verify_creator021_publish_job(workflow_jobs(source)["publish"], current["bindingIsolationProof"]["creator021ProfileAmendment"])


def verify_creator021_publish_job(job, amendment):
    assert set(amendment) == set(proof["creator020ProfileAmendment"])
    assert amendment["unit"] == "CREATOR-PUBLIC-021-4741"
    assert amendment["currentBinding"] == proof["currentBinding"] == "CURRENT_021"
    assert amendment["retainedBindings"] == ["CURRENT_020", "CURRENT_019", "CURRENT_018", "CURRENT_017", "CURRENT_016", "HISTORICAL_013"]
    assert amendment["coherentSourceVersion"] == amendment["publishedDependencyVersion"] == "0.110.0"
    assert amendment["dependencyPinFileSha256"] == digest(ROOT / "scripts/creator021-coord-dependencies.json") == "7073b80d7159f7574c7ff294e7164384d6d2a9a07a22c87984e2f16099bda8b3"
    assert amendment["freshLogicalJournal"] == "board-v2-product-creator-021"
    assert amendment["dependencyMode"] == "published-dependency" and amendment["realPinPresent"] is True
    assert amendment["oldEffectGrantRenewed"] is False
    assert amendment["operationAcceptance"] == "Source only; no021 candidate, journal initialization, protected preflight, publication, installation or native adoption acceptance."
    assert amendment["previousAcceptedSource"] == creator020_history["acceptedSource"] == "06b83a4695f5b5ca81bc8d0e974465cf801d1e03"
    assert amendment["previousSuccessorWorkflowSha256"] == creator020_history["route"]["sha256"] == "d599d438d17084107aa7d2d3cc1147d947ac32957d729260719f97068066a10e"
    assert amendment["previousRouteCanonicalSha256"] == "8122798beb575a68f2d53a019e54fd6ed520d034bb2563e2cfea00370451a0d4"
    assert amendment["previousHistoricalAttestationsCanonicalSha256"] == "87f0f64627c977b9359442d677aaaa9b6826435837f8a29eeb5cbbd3cb3da84e"
    assert hashlib.sha256(job.encode()).hexdigest() == amendment["currentPublishJobSourceSha256"]
    retained = reverse_creator020(job, CREATOR021_JOB_REVERSE)
    assert hashlib.sha256(retained.encode()).hexdigest() == amendment["previousPublishJobSourceSha256"] == "fd0a298108bb9dbfb42883ba6e157a89daa8105b0fc925a43a8144e42c47c423"
    verify_creator020_publish_job(retained, proof["creator020ProfileAmendment"])


def verify_creator021_workflow(source):
    retained = reverse_creator020(source, CREATOR021_JOB_REVERSE + (
        ("format('Wizard 0.21 successor", "format('Wizard 0.20 successor"),
        ("main-directory Creator 0.21.0 successor", "main-directory Creator 0.20.0 successor"),
    ))
    assert hashlib.sha256(retained.encode()).hexdigest() == "d599d438d17084107aa7d2d3cc1147d947ac32957d729260719f97068066a10e", "021 workflow does not recover immutable020 source"
    verify_creator020_workflow(retained)


def verify_diagnostic_job(source):
    verify_creator021_workflow(source)
    global_permissions = source.split("\npermissions:\n", 1)[1].split("\nconcurrency:\n", 1)[0]
    assert global_permissions == "  actions: read\n  contents: write\n  packages: write\n  id-token: write\n"
    jobs = workflow_jobs(source)
    assert set(jobs) == {"publish", "recovery-diagnostic", "recovery-complete"}
    job = jobs["recovery-diagnostic"]
    assert job.count("    permissions:\n") == 1
    permissions = job.split("    permissions:\n", 1)[1].split("    steps:\n", 1)[0]
    rows = [line for line in permissions.splitlines() if line.strip() and not line.lstrip().startswith("#")]
    parsed = [re.fullmatch(r"      ([a-z-]+): (read|write|none)", line) for line in rows]
    assert all(parsed) and len({m[1] for m in parsed}) == len(parsed), "exact permission map"
    assert {m[1]: m[2] for m in parsed} == {"actions": "read", "contents": "write", "packages": "read"}
    assert "    name: new-sdd-workspace-013-recovery-diagnostic\n" in job
    guard = "    if: github.ref == 'refs/heads/main' && github.actor == 'EHotwagner' && (inputs.promotion_recovery == 'diagnostic' || inputs.promotion_recovery == 'post-completion-readback') && !inputs.publish && !inputs.verify_nuget_login\n"
    assert guard in job and "    environment: release-successor\n" in job
    assert "inputs.promotion_recovery == 'complete' && !inputs.publish && !inputs.verify_nuget_login" in jobs["recovery-complete"]
    assert "          persist-credentials: false\n" in job
    assert "          GH_TOKEN: ${{ github.token }}\n" in job
    assert "          ORDINARY_LEDGER_TOKEN: ${{ steps.ledger.outputs.token }}\n" in job
    mint = job.split("      - name: Mint the scoped ordinary Authority read token\n", 1)[1].split("      - name:", 1)[0]
    for field, value in (("owner", "FS-GG"), ("repositories", "FS.GG.Coordination.Authority"), ("permission-contents", "read")):
        assert mint.count("          " + field + ": " + value + "\n") == 1
    assert "--promotion-recovery diagnostic --recovery-binding" in job
    assert "if [ \"$PROMOTION_RECOVERY_MODE\" = post-completion-readback ]; then" in job
    assert "python3 scripts/new_sdd_workspace_promote_recovery.py \\\n              --post-completion-readback \"$RECOVERY_BINDING\"" in job
    for name, expected in proof["postCompletionReadbackAmendment"]["currentJobSourceSha256"].items():
        if name == "publish":
            verify_creator021_publish_job(jobs[name], proof["creator021ProfileAmendment"])
        else:
            assert hashlib.sha256(jobs[name].encode()).hexdigest() == expected, name

readback_amendment = proof["postCompletionReadbackAmendment"]
assert set(readback_amendment) == {"unit", "scope", "previousWorkflowSha256", "previousRecoveryJobSha256", "previousDraftReadJobSourceSha256", "currentJobSourceSha256", "originalH4Source", "sourceQualification", "role", "journalGeneration", "method", "body", "releaseMutation", "journalMutation", "install", "originalH4Accepted", "oldEffectGrantRenewed", "freshRootAdmissionRequired", "readerSourceSpanSha256"}
assert {k: readback_amendment[k] for k in ("role", "journalGeneration", "method", "body", "releaseMutation", "journalMutation", "install", "originalH4Accepted", "oldEffectGrantRenewed", "freshRootAdmissionRequired")} == {"role": "post-completion-readback", "journalGeneration": 17, "method": "GET", "body": "None", "releaseMutation": False, "journalMutation": False, "install": False, "originalH4Accepted": False, "oldEffectGrantRenewed": False, "freshRootAdmissionRequired": True}
assert readback_amendment["previousDraftReadJobSourceSha256"] == proof["draftReadCapabilityAmendment"]["currentJobSourceSha256"]
assert readback_amendment["previousWorkflowSha256"] == "b3d61bfbabbc0f7eed13c4cc224352d913d444843800d2d7c2391ecce3a820c8"
assert readback_amendment["originalH4Source"] == "49a5e93668d4647999cd7171a26d4ad4d151b66b"
assert readback_amendment["previousRecoveryJobSha256"]["recovery-complete"] == proof["currentRecoveryJobSha256"]["recovery-complete"]
capability_amendment = proof["draftReadCapabilityAmendment"]
assert capability_amendment["decisionSha256"] == "42c6a8e8cf7e0ad947ec2c4ff1ee26e0fe1221536c0355b52bf85cac09e94893"
assert capability_amendment["failedRun"] == 37273763257
assert capability_amendment["originalErrorBodySha256"] == "7caac76bcb938ccbb5aa84f338b2f4ce5b59c2f31d7b524231d699a62d3673d2"
assert capability_amendment["previousWorkflowSha256"] == "060d63be16394bca11cbb8ced3d05b1916c07b029ae065399c2c1b2e581fc9da"
assert capability_amendment["previousRecoveryJobSha256"] == proof["historicalRecoveryJobSha256"]
raw_workflow = historical_workflow
verify_creator021_route(report, route, raw_workflow)
for defect in ("missing020", "changed020", "changedOlder", "wrongHash", "wrongVersion", "wrongRoute", "wrongBinding", "wrongJournal", "effectClaim"):
    document, current = copy.deepcopy(report), copy.deepcopy(route)
    if defect == "missing020":
        document["historicalRouteAttestations"] = document["historicalRouteAttestations"][:-1]
    elif defect == "changed020":
        document["historicalRouteAttestations"][-1]["route"]["successorAmendment"]["version"] = "0.21.0"
    elif defect == "changedOlder":
        document["historicalRouteAttestations"][0]["status"] = "forgiven"
    elif defect == "wrongHash": current["sha256"] = "0" * 64
    elif defect == "wrongVersion": current["successorAmendment"]["version"] = "0.20.0"
    elif defect == "wrongRoute": current["path"] = ".github/workflows/release-kit.yml"
    elif defect == "wrongBinding": current["bindingIsolationProof"]["creator021ProfileAmendment"]["currentBinding"] = "SOURCE_021"
    elif defect == "wrongJournal": current["bindingIsolationProof"]["creator021ProfileAmendment"]["freshLogicalJournal"] = "board-v2-product-creator-020"
    else: current["successorAmendment"]["operationAcceptance"] = "Publication accepted"
    try:
        verify_creator021_route(document, current, raw_workflow)
    except (AssertionError, KeyError):
        pass
    else:
        raise AssertionError("Creator021 route/history mutant accepted: " + defect)
workflow_mutations = (
    ("github.ref == 'refs/heads/main'", "true"),
    ("github.actor == 'EHotwagner'", "true"),
    ("environment: release-successor", "environment: unprotected"),
    ("candidate_archive_sha256:", "other_archive_sha256:"),
    ("--candidate-artifact-id", "--unbound-artifact-id"),
    ("scripts/new-sdd-workspace-successor-publish.py", "scripts/release-saga-ci.sh"),
)
for original, changed in workflow_mutations:
    mutant = raw_workflow.replace(original, changed, 1)
    assert mutant != raw_workflow
    current = copy.deepcopy(route)
    current["sha256"] = hashlib.sha256(mutant.encode()).hexdigest()
    current["bindingIsolationProof"]["creator021ProfileAmendment"]["currentPublishJobSourceSha256"] = hashlib.sha256(workflow_jobs(mutant)["publish"].encode()).hexdigest()
    try:
        verify_creator021_route(report, current, mutant)
    except AssertionError:
        pass
    else:
        raise AssertionError("repinned Creator021 workflow guard mutant accepted: " + original)
print("Creator021 immutable020/history and current route: 9 metadata/history and 6 repinned workflow guard mutants refused")
verify_diagnostic_job(raw_workflow)
# Causal controls also repin the candidate metadata: unrelated ordinary-job
# changes still fail the reconstructed historical seal, rather than being
# excused by a new current hash. Recovery jobs retain their original full seals.
publish_job = workflow_jobs(raw_workflow)["publish"]
summary_amendment = proof["creator021ProfileAmendment"]
publish_mutations = (
    ("        if: always()", "        if: success()"),
    ("          retention-days: 7", "          retention-days: 8"),
    ("creator021-publication-summary.json", "unbounded-directory/"),
    ("inputs.promotion_recovery == 'off'", "true"),
    ("creator-current-coord-dependencies.py preflight", "creator-frozen-coord-dependencies.py preflight"),
    ("          persist-credentials: false", "          persist-credentials: true"),
)
for original, changed in publish_mutations:
    assert original in publish_job
    mutant = publish_job.replace(original, changed, 1)
    repinned = {**summary_amendment, "currentPublishJobSourceSha256": hashlib.sha256(mutant.encode()).hexdigest()}
    try:
        verify_creator021_publish_job(mutant, repinned)
    except AssertionError:
        pass
    else:
        raise AssertionError("repinned Creator ordinary-job mutant accepted: " + original)
for mutant in (publish_job.replace(CREATOR021_SUMMARY_STEP, "", 1), publish_job + CREATOR021_SUMMARY_STEP):
    try:
        verify_creator021_publish_job(mutant, {**summary_amendment, "currentPublishJobSourceSha256": hashlib.sha256(mutant.encode()).hexdigest()})
    except AssertionError:
        pass
    else:
        raise AssertionError("missing/duplicate Creator summary step accepted")
for job_name in ("recovery-diagnostic", "recovery-complete"):
    mutant = raw_workflow.replace("  " + job_name + ":\n", "  " + job_name + ":\n    # unrelated historical job change\n", 1)
    try:
        verify_diagnostic_job(mutant)
    except AssertionError:
        pass
    else:
        raise AssertionError("historical recovery-job drift accepted: " + job_name)
print(f"Creator021 current profile and immutable020/019/018/017/016 summary seals: {len(publish_mutations) + 4} repinned job/summary and recovery mutants refused")
diagnostic = workflow_jobs(raw_workflow)["recovery-diagnostic"]
permission_mutations = (
    ("      contents: write", "      contents: read"),
    ("      packages: read", "      packages: write"),
    ("      actions: read", "      actions: write"),
    ("      packages: read", "      packages: read\n      id-token: write"),
    ("      contents: write", "      contents: write\n      contents: read"),
    ("permission-contents: read", "permission-contents: write"),
    ("repositories: FS.GG.Coordination.Authority", "repositories: .github"),
    ("persist-credentials: false", "persist-credentials: true"),
    ("GH_TOKEN: ${{ github.token }}", "GH_TOKEN: ${{ secrets.OTHER_TOKEN }}"),
    ("github.actor == 'EHotwagner'", "true"),
    ("&& !inputs.publish", ""),
    ("--promotion-recovery diagnostic", "--promotion-recovery complete"),
    ("inputs.promotion_recovery == 'post-completion-readback'", "true"),
    ("--post-completion-readback", "--worker"),
)
for original, changed in permission_mutations:
    assert original in diagnostic
    mutated = raw_workflow.replace(diagnostic, diagnostic.replace(original, changed, 1), 1)
    try:
        verify_diagnostic_job(mutated)
    except AssertionError:
        pass
    else:
        raise AssertionError("diagnostic permission/role mutant accepted: " + original)
print(f"Wizard draft-read capability seal: {len(permission_mutations)} permission/role mutants refused")
assert "format('Wizard 0.13 recovery {0} {1} {2}', inputs.promotion_recovery, inputs.recovery_correlation, inputs.recovery_binding_sha256)" in wizard
assert "default: false" in wizard and "default: 'off'" in wizard
recovery_source = (ROOT / "scripts/new_sdd_workspace_promote_recovery.py").read_text()
def verify_recovery_source_spans(source, expected_proof):
    """Verify exact production spans; metadata does not exempt amended guards."""
    parsed = ast.parse(source)
    assignments = {",".join(target.id for target in node.targets): node
                   for node in parsed.body if isinstance(node, ast.Assign)
                   and all(isinstance(target, ast.Name) for target in node.targets)}
    functions = {node.name: node for node in parsed.body if isinstance(node, ast.FunctionDef)}
    methods = {owner.name + "." + node.name: node
               for owner in parsed.body if isinstance(owner, ast.ClassDef)
               for node in owner.body if isinstance(node, ast.FunctionDef)}
    # AST locates exact bytes; interpreter-dependent serialization is not evidence.
    for spans, expected_spans in ((assignments, expected_proof["historicalRecoveryAssignmentSourceSha256"]),
                                  (functions, expected_proof["historicalGuardFunctionSourceSha256"]),
                                  (methods, expected_proof["pagedAncestryMethodSourceSha256"])):
        for name, expected in expected_spans.items():
            source_span = ast.get_source_segment(source, spans[name])
            assert source_span is not None
            assert hashlib.sha256(source_span.encode("utf-8")).hexdigest() == expected, name
    return parsed

ancestry_amendment = proof["pagedAncestryAmendment"]
assert ancestry_amendment["unit"] == "TSDD-KNOWLEDGE-01.4"
assert ancestry_amendment["originalItem"] == "TEMPLATES-H2"
assert ancestry_amendment["sourceCandidate"] == "44667c54a81ed9fdc63ab5bc4174b8448bb49f32"
assert ancestry_amendment["acceptedReviewSha256"] == "05928c08a8bc9ab59c76d65b0d2a5cac25f3458e37f43769bf38f2ada2e298e3"
assert ancestry_amendment["previousOriginalRunsSourceSha256"] == "0a5cbcb4ec4143998834aee2018ca303d87b00f37da757fdc1089badb35385c6"
assert ancestry_amendment["currentOriginalRunsSourceSha256"] == proof["historicalGuardFunctionSourceSha256"]["original_runs"]
assert {"ancestry_url", "validate_ancestry", "original_runs"} <= set(proof["historicalGuardFunctionSourceSha256"])
assert set(proof["pagedAncestryMethodSourceSha256"]) == {"FiniteAPI.request", "FiniteAPI.capture_http_error", "Redirect.redirect_request"}
error_amendment = proof["httpErrorCustodyAmendment"]
assert error_amendment["decisionSha256"] == "f2cd587a96859d7d8ab30e130f6563a63ef1fcd1c5b90f9bc579d683f2569798"
assert error_amendment["failedRun"] == 37269305353
assert error_amendment["previousRequestSourceSha256"] == "116cad46812e920fb713681ad37143450a1f4fcaadcc8fa81b90de14af18c775"
assert "diagnostic contents:read unchanged" in error_amendment["scope"]
def verify_readback_source_spans(source):
    found = {}
    for node in ast.parse(source).body:
        name = node.name if isinstance(node, (ast.FunctionDef, ast.ClassDef)) else node.targets[0].id if isinstance(node, ast.Assign) and isinstance(node.targets[0], ast.Name) else None
        if name in readback_amendment["readerSourceSpanSha256"]:
            found[name] = hashlib.sha256(ast.get_source_segment(source, node).encode()).hexdigest()
    assert found == readback_amendment["readerSourceSpanSha256"]
verify_readback_source_spans(recovery_source)
wrapping_amendment = proof["apiContentsWrappingAmendment"]
assert wrapping_amendment["unit"] == "TSDD-KNOWLEDGE-01.4"
assert wrapping_amendment["sourceCandidate"] == "b9418c5555e3c9a1978f6f217d9b93c795219c8b"
assert wrapping_amendment["currentApiContentsBytesSourceSha256"] == readback_amendment["readerSourceSpanSha256"]["api_contents_bytes"]
previous_reader_spans = wrapping_amendment["previousReaderSourceSpanSha256"]
assert set(previous_reader_spans) == set(readback_amendment["readerSourceSpanSha256"]) - {"api_contents_bytes"}
for name, previous_sha in previous_reader_spans.items():
    if name not in {"ReadbackAPI", "readback_native"}:
        assert readback_amendment["readerSourceSpanSha256"][name] == previous_sha
for owner in ast.parse(recovery_source).body:
    if isinstance(owner, ast.ClassDef) and owner.name == "FiniteAPI":
        primer = next(node for node in owner.body if isinstance(node, ast.FunctionDef) and node.name == "prime_journal")
        assert hashlib.sha256(ast.get_source_segment(recovery_source, primer).encode()).hexdigest() == wrapping_amendment["currentOriginal16PrimerSourceSha256"]
readback_mutations = (
    ('content.replace("\\r", "").replace("\\n", ""), validate=True', 'content.replace("\\r", "").replace("\\n", ""), validate=False'),
    ('method=="GET" and body is None,"readback request write forbidden"', 'True,"readback request write forbidden"'),
    ('request.get_method()=="GET" and request.data is None', 'True'),
    ('req.get_method()=="GET" and req.data is None', 'True'),
    ('result.get_method()=="GET" and result.data is None', 'True'),
    ('require(False,"readback journal write forbidden")', 'require(True,"readback journal write forbidden")'),
    ('require(False,"readback release write forbidden")', 'require(True,"readback release write forbidden")'),
)
for original, changed in readback_mutations:
    assert original in recovery_source
    try:
        verify_readback_source_spans(recovery_source.replace(original, changed, 1))
    except AssertionError:
        pass
    else:
        raise AssertionError("readback transport/effect mutant accepted: " + original)
print(f"Wizard post-completion readback seal: {len(readback_mutations)} transport/effect mutants refused")
recovery = verify_recovery_source_spans(recovery_source, proof)

# Causal offline negatives mutate production bytes, not fabricated native receipts.
# The same span verifier must refuse an accidental weakening of the new role and
# drift of a retained source/effect guard, even though metadata names an amendment.
mutations = (
    ('if failure is not None:report["httpFailure"]=failure', 'if False:report["httpFailure"]=failure'),
    ('kind if kind in kinds else "unclassified"', 'kind'),
    ('self.budget.mode!="diagnostic" or (method=="GET" and body is None)', 'True'),
    ('error.read(min(65536,cap-row["bodyBytesRetained"]+1))', 'error.read()'),
    ('retained=data[:cap-row["bodyBytesRetained"]]', 'retained=data'),
    ('row["bodyOverflow"]=True;break', 'row["bodyComplete"]=True;break'),
    ('try:error.close()', 'try:pass'),
    ('"retry-after","x-github-api-version-selected"', '"authorization","x-github-api-version-selected"'),

    ("total>=2", "total>=1"),
    ("?per_page=1&page=2", "?per_page=1&page=1"),
    ('response["behind_by"]==0', 'response["behind_by"]>=0'),
    ("headers is None", "True"),
    ("body is None and headers", "True and headers"),
    ('row["status"]==200', 'row["status"]==201'),
    ('stream.write(data)', 'stream.write(data[:1])'),
    ('"/compare" not in urllib.parse.unquote', '"/unrelated" not in urllib.parse.unquote'),
    ('CANDIDATE_SOURCE="f891b5b0723070c67e08d1a87b7d12b0b4d8bebe"', 'CANDIDATE_SOURCE="' + "a" * 40 + '"'),
    ('original.get("conclusion")=="failure"', 'original.get("conclusion")=="success"'),
)
for original, changed in mutations:
    assert original in recovery_source, f"negative control no longer selects production bytes: {original}"
    mutated = recovery_source.replace(original, changed, 1)
    try:
        verify_recovery_source_spans(mutated, proof)
    except AssertionError:
        pass
    else:
        raise AssertionError(f"unexpected source/effect seal weakening accepted: {original}")
print(f"Wizard paged ancestry source seals: {len(mutations)} mutated production guards refused")
calls = [node for node in ast.walk(recovery) if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)]
for function, keyword, count in (("effects", "binding", 3), ("WizardAdmission", "release_binding", 1)):
    selected = [call for call in calls if call.func.id == function]
    assert len(selected) == count
    assert all(any(arg.arg == keyword and isinstance(arg.value, ast.Name) and arg.value.id == "HISTORICAL_013"
                   for arg in call.keywords) for call in selected)
assert "self.release_binding is not CURRENT_023" in admission
assert "self.release_binding is not HISTORICAL_013" in admission
execution = (ROOT / "scripts/new_sdd_workspace_successor_execution.py").read_text()
assert 'CURRENT_014 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.14.0", "new-sdd-workspace/v0.14.0")' in execution
assert 'CURRENT_015 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.15.0", "new-sdd-workspace/v0.15.0")' in execution
assert 'CURRENT_016 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.16.0", "new-sdd-workspace/v0.16.0")' in execution
assert 'HISTORICAL_013 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.13.0", "new-sdd-workspace/v0.13.0")' in execution
assert 'CURRENT_017 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.17.0", "new-sdd-workspace/v0.17.0")' in execution
assert 'CURRENT_018 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.18.0", "new-sdd-workspace/v0.18.0")' in execution
assert 'CURRENT_019 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.19.0", "new-sdd-workspace/v0.19.0")' in execution
assert 'CURRENT_021 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.21.0", "new-sdd-workspace/v0.21.0")' in execution
assert 'CURRENT_022 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.22.0", "new-sdd-workspace/v0.22.0")' in execution
assert 'CURRENT_023 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.23.0", "new-sdd-workspace/v0.23.0")' in execution
assert 'CURRENT_020 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.20.0", "new-sdd-workspace/v0.20.0")' in execution
assert "binding is not CURRENT_023 and binding is not CURRENT_022 and binding is not CURRENT_021 and binding is not CURRENT_020 and binding is not CURRENT_019 and binding is not CURRENT_017 and binding is not CURRENT_016 and binding is not HISTORICAL_013" in execution

with tempfile.TemporaryDirectory(prefix="gs2-08-9-release-seal.") as temporary:
    work = pathlib.Path(temporary)
    calls = work / "calls"
    fake_bin = work / "bin"
    fake_bin.mkdir()
    credential_names = (
        "GH_TOKEN", "GITHUB_TOKEN", "NUGET_API_KEY", "NUGET_USER",
        "FSGG_DISPATCH_APP_ID", "FSGG_DISPATCH_APP_PRIVATE_KEY",
    )
    inherited = os.environ.copy()
    inherited.update({name: f"real-looking-inherited-{name.lower()}" for name in credential_names})
    env = {key: value for key, value in inherited.items() if key not in credential_names}
    env.update({
        "GH_TOKEN": "fake-gs2-08-9-gh-token",
        "GITHUB_TOKEN": "fake-gs2-08-9-github-token",
        "NUGET_API_KEY": "fake-gs2-08-9-nuget-key",
        "NUGET_USER": "fake-gs2-08-9-nuget-user",
        "FSGG_DISPATCH_APP_ID": "fake-gs2-08-9-app-id",
        "FSGG_DISPATCH_APP_PRIVATE_KEY": "fake-gs2-08-9-private-key",
        "PATH": f"{fake_bin}:{os.environ['PATH']}",
        "FSGG_FAKE_CALLS": str(calls),
    })
    assert all(not env[name].startswith("real-looking-inherited-") for name in credential_names)
    credential_check = (
        '[ "$GH_TOKEN" = fake-gs2-08-9-gh-token ] && '
        '[ "$GITHUB_TOKEN" = fake-gs2-08-9-github-token ] && '
        '[ "$NUGET_API_KEY" = fake-gs2-08-9-nuget-key ] || exit 98\n'
    )
    for command in ("gh", "dotnet", "curl"):
        stub = fake_bin / command
        stub.write_text(f'#!/usr/bin/env bash\n{credential_check}echo {command} "$@" >> "$FSGG_FAKE_CALLS"\nexit 99\n')
        stub.chmod(0o755)
    adapter = ROOT / "scripts/release-saga-ci.sh"
    for version in ("0.90.0", "0.91.0"):
        for command in ("github", "nuget-probe", "nuget-record"):
            result = subprocess.run(
                ["bash", str(adapter), command, "FS.GG.Kit", version, "a" * 40],
                cwd=ROOT, env=env, text=True, capture_output=True, check=False,
            )
            assert result.returncode == 78, (version, command, result.returncode, result.stderr)
            assert "sealed legacy release effect" in result.stderr
            assert not calls.exists() or not calls.read_text().strip(), f"{version} {command} reached an external command"

    gh = fake_bin / "gh"
    gh.write_text(
        "#!/usr/bin/env bash\n"
        + credential_check
        + 'echo gh "$@" >> "$FSGG_FAKE_CALLS"\n'
        + "if [ \"$1 $2\" = \"release view\" ]; then printf '%s\\n' '{\"isDraft\":true,\"isImmutable\":false}'; exit 0; fi\n"
        + "exit 99\n"
    )
    gh.chmod(0o755)
    calls.unlink(missing_ok=True)
    manifest = work / "manifest.json"
    channel = work / "channel.json"
    manifest.write_text("{}")
    channel.write_text("{}")
    for version in ("0.90.0", "0.91.0"):
        calls.unlink(missing_ok=True)
        tag = f"coherent-set/v{version}"
        result = subprocess.run(
            ["bash", str(ROOT / "scripts/release-saga-promote-release.sh"), "FS-GG/.github", tag, str(manifest), str(channel)],
            cwd=ROOT, env=env, text=True, capture_output=True, check=False,
        )
        assert result.returncode == 78, (version, result.returncode, result.stderr)
        assert calls.read_text().splitlines() == [f"gh release view {tag} --repo FS-GG/.github --json isDraft,isImmutable"]

print("GS2-08.9 release/publication sealing: offline qualification passed")
