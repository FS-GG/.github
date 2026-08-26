#!/usr/bin/env python3
"""Run Q0 authoring gates and emit a source-bound JUnit report."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import copy
import re
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
WORK = ROOT / "work/2953-gh-modernization-m0-invariants"
EVIDENCE = WORK / "q0-evidence.json"
OUTPUT = WORK / "artifacts/q0-verification.junit.xml"
DOWNSTREAM_JSON = [
    ROOT / "readiness/2953-gh-modernization-m0-invariants/verify.json",
    ROOT / "readiness/2953-gh-modernization-m0-invariants/ship.json",
    ROOT / "readiness/2953-gh-modernization-m0-invariants/governance-handoff.json",
]
EXPECTED_EVIDENCE_SNAPSHOTS = {
    "analysis": "readiness/2953-gh-modernization-m0-invariants/analysis.json",
    "checklist": "work/2953-gh-modernization-m0-invariants/checklist.md",
    "clarifications": "work/2953-gh-modernization-m0-invariants/clarifications.md",
    "plan": "work/2953-gh-modernization-m0-invariants/plan.md",
    "spec": "work/2953-gh-modernization-m0-invariants/spec.md",
    "tasks": "work/2953-gh-modernization-m0-invariants/tasks.yml",
}
COMMON_SDD_SOURCE_KINDS = {
    ".fsgg/agents.yml": "agentsConfig",
    ".fsgg/project.yml": "projectConfig",
    ".fsgg/sdd.yml": "sddConfig",
    "readiness/2953-gh-modernization-m0-invariants/analysis.json": "analysis",
    "readiness/2953-gh-modernization-m0-invariants/work-model.json": "workModel",
    "work/2953-gh-modernization-m0-invariants/checklist.md": "checklist",
    "work/2953-gh-modernization-m0-invariants/clarifications.md": "clarification",
    "work/2953-gh-modernization-m0-invariants/evidence.yml": "evidence",
    "work/2953-gh-modernization-m0-invariants/plan.md": "plan",
    "work/2953-gh-modernization-m0-invariants/spec.md": "specification",
    "work/2953-gh-modernization-m0-invariants/tasks.yml": "tasks",
}
EXPECTED_DOWNSTREAM_SOURCES = {
    DOWNSTREAM_JSON[0]: COMMON_SDD_SOURCE_KINDS,
    DOWNSTREAM_JSON[1]: {
        **COMMON_SDD_SOURCE_KINDS,
        "readiness/2953-gh-modernization-m0-invariants/verify.json": "source",
    },
    # The governance-handoff schema deliberately has no kind or schemaStatus.
    DOWNSTREAM_JSON[2]: {
        "readiness/2953-gh-modernization-m0-invariants/ship.json": None,
        "readiness/2953-gh-modernization-m0-invariants/verify.json": None,
        "readiness/2953-gh-modernization-m0-invariants/work-model.json": None,
    },
}
SDD_SOURCE_ROW_KEYS = frozenset({"path", "kind", "digest", "schemaVersion", "schemaStatus"})
HANDOFF_SOURCE_ROW_KEYS = frozenset({"path", "digest", "schemaVersion"})
LOWER_SHA256 = re.compile(r"[0-9a-f]{64}")


@dataclass
class Case:
    name: str
    command: list[str]
    expected: int = 0
    contains: str | None = None


def run(case: Case) -> tuple[bool, str, float]:
    started = time.monotonic()
    completed = subprocess.run(case.command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    elapsed = time.monotonic() - started
    ok = completed.returncode == case.expected and (case.contains is None or case.contains in completed.stdout)
    return ok, completed.stdout, elapsed


def whitespace_mutation_self_test() -> int:
    with tempfile.TemporaryDirectory() as directory:
        repo = Path(directory)
        def git(*args: str) -> subprocess.CompletedProcess[str]:
            return subprocess.run(["git", *args], cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if git("init", "-q").returncode != 0:
            return 1
        git("config", "user.name", "Q0 mutation")
        git("config", "user.email", "q0-mutation@example.invalid")
        subject = repo / "subject.md"
        subject.write_text("clean\n", encoding="utf-8")
        git("add", "subject.md")
        if git("commit", "-qm", "baseline").returncode != 0:
            return 1
        subject.write_text("committed trailing whitespace  \n", encoding="utf-8")
        git("add", "subject.md")
        if git("commit", "-qm", "mutation").returncode != 0:
            return 1
        measured = git("diff", "--check", "HEAD~1...HEAD")
        if measured.returncode == 0 or "trailing whitespace" not in measured.stdout:
            return 1
        print("committed whitespace mutation rejected by exact commit range")
        return 0


def coherent_sdd_analysis(payload: object) -> bool:
    if not isinstance(payload, dict):
        return False
    analysis = payload.get("analysis")
    return (
        payload.get("outcome") == "noChange"
        and payload.get("coherent") is True
        and isinstance(analysis, dict)
        and analysis.get("status") == "implementationReady"
        and analysis.get("readiness") == "implementationReady"
        and analysis.get("blockingCount") == 0
        and analysis.get("staleSourceCount") == 0
        and analysis.get("generatedViewFindingCount") == 0
        and payload.get("diagnostics") == []
    )


def sdd_analysis_check() -> int:
    completed = subprocess.run(
        ["fsgg-sdd", "analyze", "--work", "2953-gh-modernization-m0-invariants", "--json"],
        cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    )
    try:
        payload = json.loads(completed.stdout)
    except json.JSONDecodeError:
        print(completed.stdout)
        print("SDD-RED: analysis output is not exact JSON")
        return 1
    if completed.returncode != 0 or not coherent_sdd_analysis(payload):
        print(completed.stdout)
        print("SDD-RED: analysis must be noChange, coherent, implementationReady, and diagnostic-free")
        return 1
    print("SDD-GREEN: noChange, coherent, implementationReady, diagnostic-free")
    return 0


def sdd_analysis_mutation_self_test() -> int:
    incoherent = {
        "outcome": "noChange", "coherent": False, "diagnostics": [],
        "analysis": {"status": "implementationReady", "readiness": "implementationReady",
                     "blockingCount": 0, "staleSourceCount": 0, "generatedViewFindingCount": 0},
    }
    mutating = {**incoherent, "coherent": True, "outcome": "succeeded"}
    if coherent_sdd_analysis(incoherent) or coherent_sdd_analysis(mutating):
        return 1
    print("incoherent readiness-string and mutating analysis payloads rejected")
    return 0


def exact_source_digest(path: str) -> str | None:
    subject = ROOT / path
    if not subject.is_file():
        return None
    return hashlib.sha256(subject.read_bytes()).hexdigest()


def downstream_source_errors(
    evidence_text: str | None = None, json_documents: dict[Path, dict[str, object]] | None = None,
) -> list[str]:
    errors: list[str] = []
    text = evidence_text if evidence_text is not None else (WORK / "evidence.yml").read_text(encoding="utf-8")
    snapshot = re.search(r"(?ms)^sourceSnapshots:\n(?P<body>.*?)^evidence:\n", text)
    if snapshot is None:
        errors.append("evidence.yml: sourceSnapshots block is missing")
    else:
        rows: list[tuple[str, str, str]] = []
        chunks = re.findall(r"(?ms)^  - (?P<row>.*?)(?=^  - |\Z)", snapshot.group("body"))
        for index, chunk in enumerate(chunks):
            parsed = re.fullmatch(
                r"label: ([^\n]+)\n    path: ([^\n]+)\n    digest: ([0-9a-f]{64})\n    schemaVersion: 1\n?",
                chunk,
            )
            if parsed is None:
                errors.append(f"evidence.yml sourceSnapshots[{index}]: malformed")
            else:
                rows.append((parsed.group(1), parsed.group(2), parsed.group(3)))
        labels = [label for label, _, _ in rows]
        paths = [path for _, path, _ in rows]
        if len(labels) != len(set(labels)) or len(paths) != len(set(paths)):
            errors.append("evidence.yml: duplicate source snapshot label or path")
        actual_pairs = {(label, path) for label, path, _ in rows}
        expected_pairs = set(EXPECTED_EVIDENCE_SNAPSHOTS.items())
        for label, path in sorted(expected_pairs - actual_pairs):
            errors.append(f"evidence.yml: missing source snapshot {label}={path}")
        for label, path in sorted(actual_pairs - expected_pairs):
            errors.append(f"evidence.yml: unexpected source snapshot {label}={path}")
        for label, path, claimed in rows:
            actual = exact_source_digest(path)
            if actual != claimed:
                errors.append(f"evidence.yml[{label}]: {path} digest is stale")

    documents = json_documents or {path: json.loads(path.read_text(encoding="utf-8")) for path in DOWNSTREAM_JSON}
    for artifact, document in documents.items():
        rows = document.get("sources")
        if not isinstance(rows, list) or not rows:
            errors.append(f"{artifact.relative_to(ROOT)}: top-level sources are missing")
            continue
        paths: list[str] = []
        expected_rows = EXPECTED_DOWNSTREAM_SOURCES.get(artifact, {})
        handoff = artifact == DOWNSTREAM_JSON[2]
        for index, row in enumerate(rows):
            prefix = f"{artifact.relative_to(ROOT)} sources[{index}]"
            if not isinstance(row, dict):
                errors.append(f"{prefix}: malformed")
                continue
            expected_keys = HANDOFF_SOURCE_ROW_KEYS if handoff else SDD_SOURCE_ROW_KEYS
            if set(row) != expected_keys:
                errors.append(f"{prefix}: keys are not canonical")
            path = row.get("path")
            if not isinstance(path, str):
                errors.append(f"{prefix}: malformed")
                continue
            paths.append(path)
            expected_kind = expected_rows.get(path)
            if not handoff and (not isinstance(row.get("kind"), str) or row.get("kind") != expected_kind):
                errors.append(f"{prefix}: kind is not canonical")
            schema_version = row.get("schemaVersion")
            if type(schema_version) is not int or schema_version != 1:
                errors.append(f"{prefix}: schemaVersion is not canonical")
            if not handoff and (not isinstance(row.get("schemaStatus"), str) or row.get("schemaStatus") != "current"):
                errors.append(f"{prefix}: schemaStatus is not canonical")
            digest = row.get("digest")
            if handoff:
                claimed = digest.removeprefix("sha256:") if (
                    isinstance(digest, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", digest)
                ) else None
            else:
                claimed = digest.get("value") if (
                    isinstance(digest, dict)
                    and set(digest) == {"algorithm", "value"}
                    and digest.get("algorithm") == "sha256"
                    and isinstance(digest.get("value"), str)
                    and LOWER_SHA256.fullmatch(digest["value"])
                ) else None
            actual = exact_source_digest(path)
            if claimed is None:
                errors.append(f"{prefix}: digest shape is not canonical")
            elif actual != claimed:
                errors.append(f"{prefix}: {path} digest is stale")
        expected_paths = set(expected_rows)
        if len(paths) != len(set(paths)):
            errors.append(f"{artifact.relative_to(ROOT)}: duplicate source path")
        for path in sorted(expected_paths - set(paths)):
            errors.append(f"{artifact.relative_to(ROOT)}: missing source {path}")
        for path in sorted(set(paths) - expected_paths):
            errors.append(f"{artifact.relative_to(ROOT)}: unexpected source {path}")
    return errors


def downstream_source_digest_check() -> int:
    errors = downstream_source_errors()
    if errors:
        for error in errors:
            print(f"DOWNSTREAM-RED: {error}")
        return 1
    print("DOWNSTREAM-GREEN: every declared evidence/verify/ship/handoff source matches exact bytes")
    return 0


def stale_analysis_digest_mutation_self_test() -> int:
    evidence = (WORK / "evidence.yml").read_text(encoding="utf-8")
    analysis = str(ROOT / "readiness/2953-gh-modernization-m0-invariants/analysis.json")
    actual = hashlib.sha256(Path(analysis).read_bytes()).hexdigest()
    mutated = evidence.replace(f"    digest: {actual}\n", f"    digest: {'0' * 64}\n", 1)
    if mutated == evidence or not downstream_source_errors(evidence_text=mutated):
        return 1
    print("stale analysis source digest mutation rejected")
    return 0


def downstream_source_set_mutation_self_test() -> int:
    evidence = (WORK / "evidence.yml").read_text(encoding="utf-8")
    documents = {path: json.loads(path.read_text(encoding="utf-8")) for path in DOWNSTREAM_JSON}
    analysis_path = "readiness/2953-gh-modernization-m0-invariants/analysis.json"
    evidence_omission = re.sub(
        r"(?m)^  - label: analysis\n    path: readiness/2953-gh-modernization-m0-invariants/analysis\.json\n"
        r"    digest: [0-9a-f]{64}\n    schemaVersion: 1\n", "", evidence,
    )
    omission_documents = copy.deepcopy(documents)
    for artifact in DOWNSTREAM_JSON[:2]:
        omission_documents[artifact]["sources"] = [
            row for row in omission_documents[artifact]["sources"] if row.get("path") != analysis_path
        ]
    duplicate_documents = copy.deepcopy(documents)
    duplicate_documents[DOWNSTREAM_JSON[0]]["sources"].append(
        copy.deepcopy(duplicate_documents[DOWNSTREAM_JSON[0]]["sources"][0])
    )
    extra_documents = copy.deepcopy(documents)
    extra_path = "work/2953-gh-modernization-m0-invariants/q0-evidence.json"
    extra_documents[DOWNSTREAM_JSON[2]]["sources"].append({
        "path": extra_path, "digest": f"sha256:{exact_source_digest(extra_path)}", "schemaVersion": 1,
    })
    mutations = [
        ("omission", evidence_omission, omission_documents),
        ("duplicate", evidence, duplicate_documents),
        ("extra", evidence, extra_documents),
    ]
    failures = [name for name, text, docs in mutations if not downstream_source_errors(text, docs)]
    if failures:
        print(f"source-set mutations survived: {','.join(failures)}")
        return 1
    print("downstream omission, duplicate, and extra source-set mutations rejected")
    return 0


def downstream_source_schema_mutation_self_test() -> int:
    evidence = (WORK / "evidence.yml").read_text(encoding="utf-8")
    documents = {path: json.loads(path.read_text(encoding="utf-8")) for path in DOWNSTREAM_JSON}
    if downstream_source_errors(evidence, documents):
        print("canonical source-row baseline failed")
        return 1

    evidence_mutations = {
        "evidence-missing-kind": evidence.replace("  - label: analysis\n", "  - path: analysis\n", 1),
        "evidence-wrong-kind": evidence.replace("  - label: analysis\n", "  - label: not-analysis\n", 1),
        "evidence-missing-schema": evidence.replace("    schemaVersion: 1\n", "", 1),
        "evidence-wrong-schema": evidence.replace("    schemaVersion: 1\n", "    schemaVersion: 2\n", 1),
        "evidence-unexpected-key": evidence.replace("    schemaVersion: 1\n", "    schemaVersion: 1\n    extra: true\n", 1),
        "evidence-alternate-digest": re.sub(
            r"(?m)^    digest: ([0-9a-f]{64})$", r"    digest: sha256:\1", evidence, count=1,
        ),
        "evidence-uppercase-digest": re.sub(
            r"(?m)^    digest: ([0-9a-f]{64})$", lambda match: f"    digest: {match.group(1).upper()}",
            evidence, count=1,
        ),
        "evidence-malformed-type": evidence.replace("    schemaVersion: 1\n", "    schemaVersion: '1'\n", 1),
    }
    mutations: list[tuple[str, str, dict[Path, dict[str, object]]]] = [
        (name, text, documents) for name, text in evidence_mutations.items()
    ]

    for artifact in DOWNSTREAM_JSON:
        projection = artifact.stem
        handoff = artifact == DOWNSTREAM_JSON[2]
        variants: dict[str, dict[Path, dict[str, object]]] = {}

        def variant(name: str, mutate: object) -> None:
            candidate = copy.deepcopy(documents)
            row = candidate[artifact]["sources"][0]
            mutate(row)  # type: ignore[operator]
            variants[f"{projection}-{name}"] = candidate

        if handoff:
            variant("missing-kind", lambda row: row.update({"kind": None}))
            variant("wrong-kind", lambda row: row.update({"kind": "source"}))
        else:
            variant("missing-kind", lambda row: row.pop("kind"))
            variant("wrong-kind", lambda row: row.update({"kind": "not-canonical"}))
        variant("missing-schema", lambda row: row.pop("schemaVersion"))
        variant("wrong-schema", lambda row: row.update({"schemaVersion": 999}))
        variant("unexpected-key", lambda row: row.update({"unexpected": True}))
        if handoff:
            variant(
                "alternate-digest",
                lambda row: row.update({"digest": {"algorithm": "sha256", "value": row["digest"][7:]}}),
            )
            variant("wrong-algorithm", lambda row: row.update({"digest": row["digest"].replace("sha256:", "SHA256:")}))
            variant("uppercase-digest", lambda row: row.update({"digest": f"sha256:{row['digest'][7:].upper()}"}))
        else:
            variant("alternate-digest", lambda row: row.update({"digest": f"sha256:{row['digest']['value']}"}))
            variant("wrong-algorithm", lambda row: row["digest"].update({"algorithm": "SHA256"}))
            variant("digest-extra-key", lambda row: row["digest"].update({"encoding": "hex"}))
            variant("digest-missing-key", lambda row: row["digest"].pop("algorithm"))
            variant("uppercase-digest", lambda row: row["digest"].update({"value": row["digest"]["value"].upper()}))
            variant("missing-status", lambda row: row.pop("schemaStatus"))
            variant("wrong-status", lambda row: row.update({"schemaStatus": "stale"}))
        variant("malformed-type", lambda row: row.update({"schemaVersion": "1"}))
        mutations.extend((name, evidence, docs) for name, docs in variants.items())

    failures = [name for name, text, docs in mutations if not downstream_source_errors(text, docs)]
    if failures:
        print(f"source-row schema mutations survived: {','.join(failures)}")
        return 1
    print(f"canonical source-row baseline passed; {len(mutations)} schema mutations rejected across every projection")
    return 0


def main() -> int:
    evidence = json.loads(EVIDENCE.read_text(encoding="utf-8"))
    validator_spec = importlib.util.spec_from_file_location("q0_validator", WORK / "validate_q0.py")
    if validator_spec is None or validator_spec.loader is None:
        raise RuntimeError("cannot load Q0 validator")
    validator = importlib.util.module_from_spec(validator_spec)
    validator_spec.loader.exec_module(validator)
    _, review_errors = validator.discover_live_reviews(evidence["reviewFingerprint"])
    reviews_complete = not review_errors
    acceptance_expected = 0 if reviews_complete else 1
    acceptance_text = "Q0-GREEN: acceptance" if reviews_complete else "expected exactly one unedited accepted current-head attestation"
    cases = [
        Case("q0-candidate-and-mutation-controls", [sys.executable, str(WORK / "validate_q0.py"), str(EVIDENCE), "--self-test"], contains="Q0-GREEN: candidate"),
        Case("q0-review-boundary", [sys.executable, str(WORK / "validate_q0.py"), str(EVIDENCE), "--acceptance"], expected=acceptance_expected, contains=acceptance_text),
        Case("adr-corpus-coherence", [sys.executable, "scripts/check-adr-coherence.py"], contains="adr-coherence: OK"),
        Case("q0-json-parse", [sys.executable, "-m", "json.tool", str(EVIDENCE)]),
        Case("source-diff-whitespace", ["git", "diff", "--check", "origin/main...HEAD"]),
        Case("source-diff-whitespace-mutation", [sys.executable, str(Path(__file__)), "--whitespace-mutation-self-test"], contains="committed whitespace mutation rejected"),
        Case("sdd-coherent-implementation-ready", [sys.executable, str(Path(__file__)), "--sdd-analysis-check"], contains="SDD-GREEN: noChange, coherent"),
        Case("sdd-incoherent-readiness-mutation", [sys.executable, str(Path(__file__)), "--sdd-analysis-mutation-self-test"], contains="incoherent readiness-string"),
        Case("sdd-downstream-source-digests", [sys.executable, str(Path(__file__)), "--downstream-source-digest-check"], contains="DOWNSTREAM-GREEN"),
        Case("sdd-stale-analysis-digest-mutation", [sys.executable, str(Path(__file__)), "--stale-analysis-digest-mutation-self-test"], contains="stale analysis source digest mutation rejected"),
        Case("sdd-downstream-source-set-mutations", [sys.executable, str(Path(__file__)), "--downstream-source-set-mutation-self-test"], contains="omission, duplicate, and extra"),
        Case("sdd-downstream-source-schema-mutations", [sys.executable, str(Path(__file__)), "--downstream-source-schema-mutation-self-test"], contains="schema mutations rejected across every projection"),
        Case("source-tree-clean-after-gates", ["git", "diff", "--exit-code"]),
    ]

    suite = ET.Element("testsuite", name="GS2-00-Q0", tests=str(len(cases)), timestamp="2026-08-26T00:00:00+02:00")
    failures = 0
    total = 0.0
    for case in cases:
        ok, output, elapsed = run(case)
        total += elapsed
        node = ET.SubElement(suite, "testcase", name=case.name, time=f"{elapsed:.6f}")
        if not ok:
            failures += 1
            failure = ET.SubElement(node, "failure", message=f"expected exit {case.expected} and text {case.contains!r}")
            failure.text = output
        else:
            ET.SubElement(node, "system-out").text = output
    suite.set("failures", str(failures))
    suite.set("errors", "0")
    suite.set("skipped", "0")
    suite.set("time", f"{total:.6f}")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(suite).write(OUTPUT, encoding="utf-8", xml_declaration=True)
    digest = hashlib.sha256(OUTPUT.read_bytes()).hexdigest()
    print(f"q0-verification: {'PASS' if failures == 0 else 'FAIL'} {len(cases)-failures}/{len(cases)} sha256:{digest}")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    if "--whitespace-mutation-self-test" in sys.argv:
        raise SystemExit(whitespace_mutation_self_test())
    if "--sdd-analysis-check" in sys.argv:
        raise SystemExit(sdd_analysis_check())
    if "--sdd-analysis-mutation-self-test" in sys.argv:
        raise SystemExit(sdd_analysis_mutation_self_test())
    if "--downstream-source-digest-check" in sys.argv:
        raise SystemExit(downstream_source_digest_check())
    if "--stale-analysis-digest-mutation-self-test" in sys.argv:
        raise SystemExit(stale_analysis_digest_mutation_self_test())
    if "--downstream-source-set-mutation-self-test" in sys.argv:
        raise SystemExit(downstream_source_set_mutation_self_test())
    if "--downstream-source-schema-mutation-self-test" in sys.argv:
        raise SystemExit(downstream_source_schema_mutation_self_test())
    raise SystemExit(main())
