#!/usr/bin/env python3
"""Validate registry/dependencies.yml against the FS-GG registry schema.

This is the schema gate the reusable contract-coherence workflow (.github#18) calls.
It validates the registry's REAL on-disk grammar and mirrors the coherence rules that
FS.GG.Contracts' `Fsgg.Registry` validator reports (missing required field, unknown
component, malformed version) — see Registry.fsi (#27, fsgg-contracts@1.0.0).

NOTE (cross-repo gap, FS-GG/FS.GG.SDD): `Fsgg.Registry` cannot itself parse this file —
it has no YAML loader and its abstract model (Components{Id,Version} / Edges{Consumer,
Provider,CompatibleRange}, strict SemVer triples) does not match the registry's actual
shape (contracts[]/dependencies[]/coherence[]) or its version grammar (which also permits
bare integer schema-versions such as governance-policy `1`). Until the typed validator
converges to the real schema, this script is the authority; it intentionally enforces the
same *kinds* of rule so the two cannot disagree in spirit.

Usage:
  scripts/validate-registry.py [registry/dependencies.yml]
  scripts/validate-registry.py registry/dependencies.yml --expect fsgg-contracts=1.0.0 ...

`--expect <id>=<version>` asserts the registry's declared contract version equals the
caller's actual pin (the "pins == registry's declared values; fail on drift" gate). It may
be repeated. Exit code is 1 on any diagnostic, 2 on usage error.
"""

from __future__ import annotations

import argparse
import re
import sys

import yaml

# A SemVer-ish version: major.minor.patch with optional -prerelease (+build ignored),
# OR a bare integer schema-version (governance config schemas declare e.g. `1`/`2`).
SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$")
INT_VERSION_RE = re.compile(r"^\d+$")
# A range like "1.x" or a SemVer comparator set; permissive, only used for the `range` field.
RANGE_RE = re.compile(r"^[\d.xX*\s<>=~^|.-]+$")

# Owner of a contract may be any repo key, or the org `.github` repo itself.
EXTRA_OWNERS = {"github"}


def is_version(value: object) -> bool:
    s = str(value)
    return bool(SEMVER_RE.match(s) or INT_VERSION_RE.match(s))


class Diagnostics:
    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, entry: str, rule: str, message: str) -> None:
        self.items.append(f"  [{rule}] {entry}: {message}")

    def missing(self, entry: str, field: str) -> None:
        self.add(entry, "MissingField", f"missing required field '{field}'.")


def validate(model: dict, expect: dict[str, str]) -> list[str]:
    d = Diagnostics()

    # --- Top-level shape ---
    if not isinstance(model, dict):
        d.add("<root>", "MalformedDocument", "registry must be a mapping.")
        return d.items

    if "schemaVersion" not in model:
        d.missing("<root>", "schemaVersion")
    elif not isinstance(model["schemaVersion"], int):
        d.add("<root>", "MalformedVersion", "'schemaVersion' must be an integer.")

    repos = model.get("repos") or {}
    if not isinstance(repos, dict) or not repos:
        d.add("repos", "MissingField", "'repos' must be a non-empty mapping.")
        repos = repos if isinstance(repos, dict) else {}
    for key, repo in repos.items():
        if not isinstance(repo, dict):
            d.add(f"repos.{key}", "MalformedDocument", "repo entry must be a mapping.")
            continue
        for field in ("name", "role"):
            if not str(repo.get(field, "")).strip():
                d.missing(f"repos.{key}", field)

    repo_keys = set(repos.keys())
    valid_owners = repo_keys | EXTRA_OWNERS

    # --- Contracts ---
    contracts = model.get("contracts")
    if not isinstance(contracts, list) or not contracts:
        d.add("contracts", "MissingField", "'contracts' must be a non-empty list.")
        contracts = contracts if isinstance(contracts, list) else []

    seen_ids: set[str] = set()
    declared: dict[str, str] = {}
    for i, c in enumerate(contracts):
        if not isinstance(c, dict):
            d.add(f"contracts[{i}]", "MalformedDocument", "contract must be a mapping.")
            continue
        cid = str(c.get("id", "")).strip()
        entry = cid or f"contracts[{i}]"

        if not cid:
            d.missing(entry, "id")
        elif cid in seen_ids:
            d.add(entry, "DuplicateComponent", f"duplicate contract id '{cid}'.")
        else:
            seen_ids.add(cid)

        version = c.get("version", "")
        if not str(version).strip():
            d.missing(entry, "version")
        elif not is_version(version):
            d.add(entry, "MalformedVersion",
                  f"non-SemVer/non-integer 'version': '{version}'.")
        elif cid:
            declared[cid] = str(version)

        for field in ("owner", "surface", "consumers"):
            if field not in c or (isinstance(c.get(field), str) and not c[field].strip()):
                d.missing(entry, field)

        owner = str(c.get("owner", "")).strip()
        if owner and owner not in valid_owners:
            d.add(entry, "UnknownComponent",
                  f"owner '{owner}' is not a known repo (got {sorted(valid_owners)}).")

        consumers = c.get("consumers")
        if consumers is not None:
            if not isinstance(consumers, list):
                d.add(entry, "MalformedDocument", "'consumers' must be a list.")
            else:
                for cons in consumers:
                    if str(cons).strip() not in repo_keys:
                        d.add(entry, "UnknownComponent",
                              f"consumer '{cons}' is not a known repo.")

        # Optional version-shaped fields, when present, must be well-formed.
        for field in ("package-version",):
            if field in c and not is_version(c[field]):
                d.add(entry, "MalformedVersion",
                      f"'{field}' is not a valid version: '{c[field]}'.")
        if "range" in c and not RANGE_RE.match(str(c["range"])):
            d.add(entry, "MalformedVersion", f"'range' is malformed: '{c['range']}'.")

    # --- Dependency edges (downstream -> upstream); both ends must be known repos. ---
    for i, e in enumerate(model.get("dependencies") or []):
        if not isinstance(e, dict):
            d.add(f"dependencies[{i}]", "MalformedDocument", "edge must be a mapping.")
            continue
        frm, to = str(e.get("from", "")).strip(), str(e.get("to", "")).strip()
        entry = f"{frm or '<blank>'} -> {to or '<blank>'}"
        if not frm:
            d.missing(entry, "from")
        elif frm not in repo_keys:
            d.add(entry, "UnknownComponent", f"'from' references unknown repo '{frm}'.")
        if not to:
            d.missing(entry, "to")
        elif to not in repo_keys:
            d.add(entry, "UnknownComponent", f"'to' references unknown repo '{to}'.")
        if not str(e.get("via", "")).strip():
            d.missing(entry, "via")

    # --- Coherence entries ---
    for i, ch in enumerate(model.get("coherence") or []):
        if not isinstance(ch, dict):
            d.add(f"coherence[{i}]", "MalformedDocument", "entry must be a mapping.")
            continue
        cid = str(ch.get("id", "")).strip()
        entry = cid or f"coherence[{i}]"
        if not cid:
            d.missing(entry, "id")
        if "coherent" not in ch or not isinstance(ch["coherent"], bool):
            d.add(entry, "MissingField", "'coherent' must be present and boolean.")
        if not str(ch.get("owner", "")).strip():
            d.missing(entry, "owner")

    # --- Pin-drift assertions: declared registry version == caller's actual pin. ---
    for cid, want in expect.items():
        if cid not in declared:
            d.add(cid, "UnknownComponent",
                  f"--expect names contract '{cid}' absent from the registry.")
        elif declared[cid] != want:
            d.add(cid, "PinDrift",
                  f"registry declares version '{declared[cid]}' but actual pin is '{want}'.")

    return d.items


def parse_expect(pairs: list[str]) -> dict[str, str]:
    out: dict[str, str] = {}
    for p in pairs:
        if "=" not in p:
            sys.exit(f"--expect must be <id>=<version>, got: {p!r}")
        k, v = p.split("=", 1)
        out[k.strip()] = v.strip()
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description="Validate the FS-GG registry schema.")
    ap.add_argument("path", nargs="?", default="registry/dependencies.yml")
    ap.add_argument("--expect", action="append", default=[],
                    metavar="ID=VERSION",
                    help="assert registry's declared version for ID equals VERSION "
                         "(repeatable); fails on drift.")
    args = ap.parse_args()

    try:
        with open(args.path, encoding="utf-8") as f:
            model = yaml.safe_load(f)
    except FileNotFoundError:
        print(f"registry not found: {args.path}", file=sys.stderr)
        return 2
    except yaml.YAMLError as exc:
        print(f"registry is not valid YAML: {exc}", file=sys.stderr)
        return 1

    diagnostics = validate(model, parse_expect(args.expect))
    if diagnostics:
        print(f"Registry coherence FAILED ({len(diagnostics)} diagnostic(s)) in {args.path}:",
              file=sys.stderr)
        print("\n".join(diagnostics), file=sys.stderr)
        return 1

    print(f"Registry coherence OK: {args.path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
