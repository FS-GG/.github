#!/usr/bin/env python3
"""Deterministic ShellCheck JSON selection, subject manifests, and run receipts."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import re
import sys
from pathlib import Path
from typing import Any

SCHEMA_MANIFEST = "fsgg.shell-lint-manifest/v1"
SCHEMA_RESULT = "fsgg.shellcheck-selection/v1"
SCHEMA_RECEIPT = "fsgg.shell-lint-receipt/v1"
LEVELS = {"error": 0, "warning": 1, "info": 2, "style": 3}
CODE = re.compile(r"^SC\d{4}$")


def canonical(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical(value) + b"\n")


def relative(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.resolve().as_posix()


def manifest(args: argparse.Namespace) -> int:
    root = Path(args.root)
    raw_files = Path(args.file_list0).read_bytes().split(b"\0")
    file_paths = [os.fsdecode(raw) for raw in raw_files if raw]
    workflows: list[tuple[str, str]] = []
    for line in Path(args.workflow_manifest).read_text(encoding="utf-8").splitlines():
        if not line:
            continue
        pieces = line.split("\t", 1)
        if len(pieces) != 2 or not all(pieces):
            raise ValueError("workflow manifest contains a malformed row")
        workflows.append((pieces[0], pieces[1]))

    targeted = sorted(set(args.targeted))
    if any(not CODE.fullmatch(code) for code in targeted):
        raise ValueError("targeted codes must use SCdddd spelling")
    payload = {
        "schema": SCHEMA_MANIFEST,
        "shellcheckVersion": args.shellcheck_version,
        "policy": {
            "fileAnalysisSeverity": "info",
            "verdictSeverity": args.severity,
            "targetedCodes": targeted,
            "sourceFollowRefusal": "SC1091",
            "workflowAnalysisSeverity": args.severity,
            "workflowOccurrenceFilter": "SC2050-sidecar-span/v1",
        },
        "implementation": {
            name: {"path": relative(root, Path(path)), "sha256": sha(Path(path))}
            for name, path in (
                ("extractor", args.extractor),
                ("occurrenceFilter", args.occurrence_filter),
                ("selector", Path(__file__)),
                ("lint", args.implementation),
            )
        },
        "subjects": {
            "files": [
                {"path": path, "sha256": sha(root / path)} for path in sorted(file_paths)
            ],
            "workflowEmbedded": [
                {"label": label, "sha256": sha(Path(path))}
                for path, label in sorted(workflows, key=lambda row: row[1])
            ],
        },
    }
    digest = hashlib.sha256(canonical(payload)).hexdigest()
    document = {**payload, "digest": digest}
    write_json(Path(args.output), document)
    print(digest)
    return 0


def load_occurrence_filter(path: str):
    spec = importlib.util.spec_from_file_location("shell_lint_occurrence_filter", path)
    if spec is None or spec.loader is None:
        raise ValueError("could not load the configured occurrence filter")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not callable(getattr(module, "is_protected", None)):
        raise ValueError("configured occurrence filter does not expose is_protected")
    return module


def select(args: argparse.Namespace) -> int:
    raw = json.loads(Path(args.input).read_text(encoding="utf-8"))
    if not isinstance(raw, dict) or set(raw) != {"comments"} or not isinstance(raw["comments"], list):
        raise ValueError("ShellCheck JSON1 output must be an object containing only a comments array")
    threshold = LEVELS[args.severity]
    targeted = set(args.targeted)
    occurrence_filter = load_occurrence_filter(args.occurrence_filter) if args.occurrence_filter else None
    selected: list[dict[str, Any]] = []
    source_refusal = False
    required = {"file", "line", "column", "level", "code", "message"}
    for index, finding in enumerate(raw["comments"]):
        if not isinstance(finding, dict) or not required.issubset(finding):
            raise ValueError(f"ShellCheck comment {index} is incomplete")
        path = finding["file"]
        level = finding["level"]
        code_number = finding["code"]
        if (not isinstance(path, str) or not path
                or not isinstance(level, str) or level not in LEVELS
                or not isinstance(code_number, int) or isinstance(code_number, bool)
                or not 1 <= code_number <= 9999
                or not isinstance(finding["message"], str)):
            raise ValueError(f"ShellCheck comment {index} has invalid field types")
        if (not isinstance(finding["line"], int) or isinstance(finding["line"], bool)
                or not isinstance(finding["column"], int) or isinstance(finding["column"], bool)
                or finding["line"] < 1 or finding["column"] < 1):
            raise ValueError(f"ShellCheck comment {index} has invalid coordinates")
        code = f"SC{code_number:04d}"
        if args.sc1091_refusal and code == "SC1091":
            source_refusal = True
            continue
        if occurrence_filter is not None and code == "SC2050":
            if occurrence_filter.is_protected(path, finding["line"], finding["column"]):
                continue
        if LEVELS[level] <= threshold or code in targeted:
            selected.append(finding)

    for finding in selected:
        gcc_level = "note" if finding["level"] in ("info", "style") else finding["level"]
        print(
            f"{finding['file']}:{finding['line']}:{finding['column']}: "
            f"{gcc_level}: {finding['message']} [SC{finding['code']:04d}]"
        )
    result = {
        "schema": SCHEMA_RESULT,
        "inputCount": len(raw["comments"]),
        "selectedCount": len(selected),
        "sourceFollowRefusal": source_refusal,
    }
    write_json(Path(args.result), result)
    if source_refusal:
        return 4
    return 1 if selected else 0


def receipt(args: argparse.Namespace) -> int:
    manifest_doc = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    expected = manifest_doc.pop("digest", None)
    actual = hashlib.sha256(canonical(manifest_doc)).hexdigest()
    if expected != actual:
        raise ValueError("manifest digest is missing or does not match its canonical content")
    durations = json.loads(args.durations)
    required_phases = {
        "discovery", "manifest", "fileShellcheck", "fileSelection",
        "workflowShellcheck", "workflowSelection", "total",
    }
    if set(durations) != required_phases or any(not isinstance(v, int) or v < 0 for v in durations.values()):
        raise ValueError("receipt durations are incomplete or invalid")
    invocations = {"files": args.file_invocations, "workflowEmbedded": args.workflow_invocations}
    subject_counts = {
        "files": len(manifest_doc["subjects"]["files"]),
        "workflowEmbedded": len(manifest_doc["subjects"]["workflowEmbedded"]),
    }
    expected_invocations = {name: int(count > 0) for name, count in subject_counts.items()}
    if invocations != expected_invocations:
        raise ValueError("each non-empty subject projection must be analyzed exactly once, and each empty projection zero times")
    document = {
        "schema": SCHEMA_RECEIPT,
        "manifestDigest": expected,
        "shellcheckVersion": manifest_doc["shellcheckVersion"],
        "subjectCounts": subject_counts,
        "invocationCounts": {**invocations, "total": sum(invocations.values())},
        "phaseDurationsMs": durations,
        "verdict": {"exitCode": args.exit_code, "name": args.verdict},
    }
    write_json(Path(args.output), document)
    print(canonical(document).decode())
    return 0


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser()
    sub = root.add_subparsers(dest="command", required=True)
    make = sub.add_parser("manifest")
    make.add_argument("--root", required=True)
    make.add_argument("--file-list0", required=True)
    make.add_argument("--workflow-manifest", required=True)
    make.add_argument("--shellcheck-version", required=True)
    make.add_argument("--severity", choices=LEVELS, required=True)
    make.add_argument("--targeted", action="append", default=[])
    make.add_argument("--extractor", required=True)
    make.add_argument("--occurrence-filter", required=True)
    make.add_argument("--implementation", required=True)
    make.add_argument("--output", required=True)
    make.set_defaults(run=manifest)

    choose = sub.add_parser("select")
    choose.add_argument("--input", required=True)
    choose.add_argument("--result", required=True)
    choose.add_argument("--severity", choices=LEVELS, required=True)
    choose.add_argument("--targeted", action="append", default=[])
    choose.add_argument("--sc1091-refusal", action="store_true")
    choose.add_argument("--occurrence-filter")
    choose.set_defaults(run=select)

    rec = sub.add_parser("receipt")
    rec.add_argument("--manifest", required=True)
    rec.add_argument("--output", required=True)
    rec.add_argument("--durations", required=True)
    rec.add_argument("--file-invocations", type=int, required=True)
    rec.add_argument("--workflow-invocations", type=int, required=True)
    rec.add_argument("--exit-code", type=int, required=True)
    rec.add_argument("--verdict", choices=("clean", "findings", "source-refusal", "no-verdict"), required=True)
    rec.set_defaults(run=receipt)
    return root


def main() -> int:
    args = parser().parse_args()
    return args.run(args)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, UnicodeError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(f"::error::shellcheck structured-output boundary refused input: {error}", file=sys.stderr)
        sys.exit(2)
