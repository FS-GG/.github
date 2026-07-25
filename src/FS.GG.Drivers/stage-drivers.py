#!/usr/bin/env python3
"""stage-drivers.py — stage the `.github`-authored DRIVER skill bytes into a directory the package
packs from (ADR-0054 §Byte-transport, resolving .github#1300; ADR-0062 generalized one consumer over).

WHAT THIS IS. `.github` authors a third `skill-registry` class — a `.github`-authored,
product-materialized `scope: driver` skill (ADR-0054; the first is `work-roadmap`, ADR-0053/#1224). The
scaffold-time materializer in the SDD CLI cannot reach `.github` (the `fsgg-sdd scaffold` inner loop is
OFFLINE and generic SDD is barred from embedding a cross-repo source — FS.GG.SDD/CLAUDE.md, scaffold
FR-002/SC-005), so `.github` PUBLISHES the driver bytes as a versioned package the CLI pins, restores at
build/publish time (online), and materializes into a scaffold's skill roots at scaffold time (offline),
with the ADR-0014 content-addressed sha256 as the integrity guarantee. This is the ADR-0062 `FS.GG.Kit`
package-delivery pattern, one consumer over (SDD CLI → scaffolded product trees, not Renovate → framework
repos; materialize at scaffold time, not restore time).

DERIVED, NOT RESTATED (ADR-0058). The delivered set lives in exactly ONE authored place —
`registry/driver-skill-manifest.json`, emitted by `scripts/generate-driver-manifest` from the authored
SKILL.md bodies. This stager reads that manifest and stages exactly its `scope: driver` rows; it restates
no list of driver names, so a driver added/retired in the emitter needs no edit here.

WHAT IT STAGES, under <out-dir> (the package packs it under `drivers/`):

  driver-skill-manifest.json          the manifest VERBATIM — the delivered set's authority + sha256s
  skills/<id>/<relative file>         the complete directory for each `scope: driver` row

Only `scope: driver` rows are staged. A `scope: operator` row (ADR-0057, e.g. `drive-board`) is
`.github`-authored but materialized NOWHERE — it runs only in the operator checkout where every repo is a
sibling — so it is never DELIVERED to a scaffold and its bytes are deliberately not carried. The manifest
still lists it (it is the emitter's single output); the consumer materializes by predicate, and an
operator row's `materializes-when: false` gates it out of every tree, so carrying the full manifest is
safe and un-restated.

INTEGRITY AT STAGE TIME. V2 verifies every file's raw digest and executable bit plus the deterministic
tree digest; v1 retains the canonical SKILL.md check during publish-before-flip. Drift is a build
FAILURE, never a silently mis-staged byte — the same fail-loud
contract stage-kit.sh holds for the coordination kit.

  stage-drivers.py <out-dir>

Exit: 0 staged; 2 on any misconfiguration (manifest missing/unparseable, a source SKILL.md missing, a
digest mismatch). Pure stdlib; no network, no tokens.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import sys

# Repo root = two levels up from this file (src/FS.GG.Drivers/stage-drivers.py -> repo root).
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MANIFEST_PATH = os.path.join(REPO_ROOT, "registry", "driver-skill-manifest.json")

# The delivered class. `operator` (ADR-0057) is authored-but-materialized-nowhere, so it is not carried.
DELIVERED_SCOPES = {"driver"}


def die(msg: str) -> "None":
    sys.stderr.write(f"stage-drivers: {msg}\n")
    raise SystemExit(2)


def canonical_digest(raw: bytes) -> str:
    """sha256 over the body text's UTF-8 bytes, BOM-free — byte-parity with generate-driver-manifest."""
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    return hashlib.sha256(raw).hexdigest()


def main(argv: list) -> int:
    if len(argv) != 1:
        die("usage: stage-drivers.py <out-dir>")
    out = argv[0]

    try:
        with open(MANIFEST_PATH, "rb") as handle:
            manifest_bytes = handle.read()
    except OSError as exc:
        die(f"driver manifest not found at {MANIFEST_PATH}: {exc} (is this a .github checkout? "
            "run scripts/generate-driver-manifest --write)")
    try:
        doc = json.loads(manifest_bytes)
    except json.JSONDecodeError as exc:
        die(f"driver manifest is not valid JSON: {exc}")

    skills = doc.get("skills")
    if not isinstance(skills, list):
        die("driver manifest has no 'skills' array — the emitter's shape changed?")

    # Fresh staging every run — a driver a prior manifest named and this one does not must never linger
    # (the staleness a byte-copy fabric suffers, kept out of the producer).
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)

    # The manifest itself is the delivered set's authority + integrity record — carry it VERBATIM.
    with open(os.path.join(out, "driver-skill-manifest.json"), "wb") as handle:
        handle.write(manifest_bytes)

    staged = 0
    for row in skills:
        scope = row.get("scope")
        if scope not in DELIVERED_SCOPES:
            continue  # operator (or any future never-delivered scope): authored here, delivered nowhere.
        skill_id = row.get("id")
        supplied_by = row.get("supplied-by")
        want_sha = row.get("sha256")
        if not skill_id or not supplied_by or not want_sha:
            die(f"driver row is missing id/supplied-by/sha256: {row!r}")

        dest_dir = os.path.join(out, "skills", skill_id)
        file_rows = row.get("files")
        v2 = isinstance(file_rows, list)
        if not v2:
            file_rows = [{"path": "SKILL.md", "sha256": want_sha, "executable": False}]
        tree_sha = row.get("tree-sha256")
        if tree_sha:
            got_tree = hashlib.sha256(
                json.dumps(file_rows, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            ).hexdigest()
            if got_tree != tree_sha:
                die(f"driver skill {skill_id}: files manifest digest {got_tree} != tree-sha256 {tree_sha}")
        for file_row in file_rows:
            rel = file_row.get("path")
            file_want = file_row.get("sha256")
            executable = file_row.get("executable")
            if (
                not isinstance(rel, str)
                or not rel
                or os.path.isabs(rel)
                or ".." in rel.split("/")
                or not isinstance(file_want, str)
                or not isinstance(executable, bool)
            ):
                die(f"driver skill {skill_id}: malformed file row: {file_row!r}")
            src = os.path.join(REPO_ROOT, supplied_by, *rel.split("/"))
            try:
                with open(src, "rb") as handle:
                    raw = handle.read()
            except OSError as exc:
                die(f"driver skill source missing: {supplied_by}/{rel} ({exc})")
            got_sha = hashlib.sha256(raw).hexdigest() if v2 else canonical_digest(raw)
            # The legacy SKILL.md field is BOM-canonical; v2's file digest is deliberately raw.
            if file_want != got_sha:
                die(f"driver skill {skill_id}/{rel}: sha256 {got_sha} != manifest {file_want}")
            if bool(os.stat(src).st_mode & 0o111) != executable:
                die(f"driver skill {skill_id}/{rel}: executable mode differs from manifest")
            dest = os.path.join(dest_dir, *rel.split("/"))
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with open(dest, "wb") as handle:
                handle.write(raw)
            os.chmod(dest, 0o755 if executable else 0o644)
        staged += 1

    if staged == 0:
        die("no scope:driver rows in the manifest — nothing to deliver (a truncated/empty manifest?).")

    sys.stdout.write(f"stage-drivers: staged {staged} driver skill(s) + the manifest into {out}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
