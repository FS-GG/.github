#!/usr/bin/env python3
"""Is THIS `FS.GG.Kit` bump PR mechanical? Decide it from the diff's SHAPE, derived from the package.

.github#1693, correcting one premise of .github#1587. #1587 makes automerge safe by asserting a class
rather than trusting an author: a bump PR may touch only (a) the kit pin and (b) what the materializer
itself produces. This script is that assertion, and it exists as its own file because #1587's clause (b)
was measured against a world that ended on 2026-07-27.

WHAT CHANGED, AND WHY A LITERAL READING OF #1587 WOULD REFUSE EVERY BUMP
  #1587 measured four landed bumps and found each was "exactly two files — a one-line version bump plus
  `scripts/fsgg-coord` as materialized output". A guard that derives (b) from `FsggKitSkillRoots` alone
  is correct for that world and WRONG for every bump after it:

    * `FS.GG.Kit` 0.14.0 (ADR-0067 §5, #1636) RETIRED `.codex/skills`. Materializing it writes skills
      into two roots instead of three AND DELETES the receiver's whole `.codex/skills` tree — the
      materializer doing it, precisely because ADR-0065's transport contract forbids a receiver
      hand-deleting a mirror.
    * `FS.GG.Kit` 0.15.0 (#1696) added a THIRD root kind, `FsggKitViewSkillRoots`, whose
      previously-materialized copies are swept the same way.

  So the bump carries the pin change PLUS deletions under roots that are not in `FsggKitSkillRoots`.
  A guard reading only that property sees them as contamination and refuses — on all seven receivers,
  blocking the one bump that carries the retirement, and implying the one repair ("hand-delete
  `.codex/skills` and re-run") that ADR-0065 §Retiring a root forbids in as many words.

  MEASURED, not argued (#1693) — three receiver COPIES materialized against the published 0.15.0, one
  for each of the three places #1587 AC 4 says a receiver pins, with the real checkouts untouched:

    FS.GG.Net        0.8.0  -> 0.15.0   35 paths: 1 pin (Directory.Packages.props),        9 M, 3 A, 23 D
    FS.GG.SDD        0.10.0 -> 0.15.0   34 paths: 1 pin (Directory.Packages.local.props),  8 M, 3 A, 23 D
    FS.GG.Templates  0.8.0  -> 0.15.0   35 paths: 1 pin (.config/kit/…receiver.proj),      9 M, 3 A, 23 D

  Every one of those 23 deletions is under `.codex/skills`. Not one of them is inside
  `FsggKitSkillRoots`.

THE CLASS THIS ADMITS, STATED SO A HUMAN CAN CHECK IT BY HAND
  Everything below is read from the TARGET version's package — the version the PR is bumping TO — and
  from the receiver's own effective MSBuild evaluation of it. Nothing here is a list.

    SKILLS  = the first path segment of every `kind: skill` destination in `kit/kit-manifest.tsv`
    FLAT    = the destination of every `kind: client` / `kind: config` row, plus the `kind: build-config`
              rows if and only if the receiver evaluates `FsggKitMaterializeBuildConfig` true
    LIVE    = FsggKitSkillRoots           (the roots this package materializes INTO)
    RETIRED = FsggKitRetiredSkillRoots    (roots that LEFT the runtime contract; swept)
    VIEW    = FsggKitViewSkillRoots       (roots still IN the contract whose content is GENERATED; swept)

  A changed path is admissible iff it is exactly one of:

    1. THE PIN — the single file whose changed lines are all `FS.GG.Kit` version declarations, every
       added one naming the target version. Modify only. The pin's LOCATION is derived from the diff,
       never from a list of the three places receivers pin (#1587 AC 4).
    2. A FLAT destination, added or modified. Exact path equality — not a prefix.
    3. `<root>/<dest>` for root in LIVE and `<dest>` a skill row's destination — added or modified.
    4. `<root>/<skill>/...` for root in LIVE and skill in SKILLS — DELETED. This is the materializer's
       managed-skill-directory sweep: "a skill directory is a closed transport unit", so a reference
       file dropped between versions is removed.
    5. `<root>/<skill>/...` for root in RETIRED or VIEW and skill in SKILLS — DELETED, AND NOTHING ELSE.
       A retired or view root admits deletions only.

  Anything else is a FINDING. In particular a deletion under a retired root of a directory that is NOT
  one of the kit's own skills is refused — the materializer leaves a receiver's own skill alone, so a
  bump PR that removes one is not a bump PR.

WHY VIEW ROOTS ARE ADMITTED, AND WHY THEY ARE STILL READ SEPARATELY
  Their DIFF disposition is identical to a retired root's — `FS.GG.Kit.targets` sweeps both by deleting
  the kit's own skill directories, and a view root's generated content (a symlink, or a copy carrying a
  `.skill-view` receipt) is never committed, because `scripts/skill-view` refuses to generate over a root
  git tracks. So a view root only ever loses files from a receiver's tree.

  They are nonetheless read from their OWN property rather than folded into the retired set, because the
  two differ where it matters elsewhere: a view root STAYS in the runtime contract (`agentSkillRoots`,
  `.agent-skill-roots`, `coordination-sync`, `KitDigest` keep counting it) and a retired root leaves it.
  Spelling them the same here would be a second copy waiting to disagree, which is the class #1693 was
  filed to avoid. `FsggKitViewSkillRoots` is EMPTY by default, so no receiver has one today; this admits
  the first one that does, with no edit here.

FAIL CLOSED (epic #266). "I could not decide" is never spelled like "I decided, and it's fine":

    exit 0  MECHANICAL     — the diff is exactly the admissible class. Safe to automerge.
    exit 1  CONTAMINATED   — a kit bump carrying something outside it. Automerge must not fire.
    exit 2  NOT-A-KIT-BUMP — no `FS.GG.Kit` pin change in this diff. The guard ABSTAINS; abstention is
                             not a pass, and #1587's automerge must treat it as "do not merge".
    exit 3  REFUSED        — the inputs cannot support a verdict (no manifest, no skill rows, a root
                             declared in two of the three properties, an unreadable diff, a rename).
                             Never guessed.

HOW A CALLER PRODUCES THE INPUTS — all three are derivations, none is a restatement:

    dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize      # so the pin is restored
    dotnet msbuild .config/kit/FS.GG.Kit.receiver.proj \\
        -getProperty:FsggKitSkillRoots,FsggKitRetiredSkillRoots,FsggKitViewSkillRoots,FsggKitMaterializeBuildConfig \\
        > /tmp/kit-props.json
    python3 check-kit-bump-shape.py --repo . --base "$BASE" --head "$HEAD" \\
        --kit-dir ~/.nuget/packages/fs.gg.kit/<target> --properties /tmp/kit-props.json

  `-getProperty` is the receiver's OWN evaluation of the target package's `build/FS.GG.Kit.props`, so a
  receiver that overrides a root set is judged by what it actually declares, not by the package default.
  The `--kit-dir` version is read from the package's nuspec and asserted equal to the pin the diff moves
  to, so pointing this at the wrong package is a refusal rather than a wrong verdict.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

MECHANICAL, CONTAMINATED, NOT_A_KIT_BUMP, REFUSED = 0, 1, 2, 3

PACKAGE = "FS.GG.Kit"

# The pin, in every location a receiver uses (#1587 AC 4 names three: `Directory.Packages.props`,
# `Directory.Packages.local.props`, and an inline `Version=` on the `PackageReference` in
# `.config/kit/FS.GG.Kit.receiver.proj`). This matches the DECLARATION, not the file — which is why
# the three locations need no list: whichever file carries the declaration is the pin file.
PIN_INCLUDE = re.compile(r'Include\s*=\s*"FS\.GG\.Kit"')
PIN_VERSION = re.compile(r'Version\s*=\s*"([^"]+)"')

PROPERTY_NAMES = (
    "FsggKitSkillRoots",
    "FsggKitRetiredSkillRoots",
    "FsggKitViewSkillRoots",
    "FsggKitMaterializeBuildConfig",
)


class Refused(Exception):
    """The inputs cannot support a verdict. Exit 3, never a guess."""


def git(repo: Path, *args: str) -> str:
    proc = subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        text=True,
        check=False,
    )
    if proc.returncode != 0:
        raise Refused(f"git {' '.join(args)} failed: {proc.stderr.strip()}")
    return proc.stdout


def read_package_version(kit_dir: Path) -> str:
    """The target package's own version, from its nuspec. Refuses rather than trusting the path."""
    nuspecs = sorted(kit_dir.glob("*.nuspec"))
    if len(nuspecs) != 1:
        raise Refused(
            f"{kit_dir} holds {len(nuspecs)} .nuspec file(s); a restored package holds exactly one. "
            "Point --kit-dir at the extracted package root (e.g. ~/.nuget/packages/fs.gg.kit/<version>)."
        )
    text = nuspecs[0].read_text(encoding="utf-8")
    found = re.search(r"<version>\s*([^<\s]+)\s*</version>", text, re.IGNORECASE)
    if not found:
        raise Refused(f"{nuspecs[0]} declares no <version>.")
    return found.group(1)


def read_manifest(kit_dir: Path) -> list[tuple[str, str]]:
    """(kind, receiver destination) for every row of the target package's kit-manifest.tsv."""
    manifest = kit_dir / "kit" / "kit-manifest.tsv"
    if not manifest.is_file():
        raise Refused(
            f"{manifest} is missing — this package carries no kit manifest, so there is nothing to "
            "derive the admissible set FROM. Refusing rather than admitting an empty set."
        )
    rows: list[tuple[str, str]] = []
    for number, line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        fields = line.split("\t")
        if len(fields) < 4:
            raise Refused(f"{manifest}:{number}: malformed row (expected >=4 tab-separated fields).")
        dest = fields[2].replace("\\", "/").strip()
        # Validated BEFORE any normalization: an absolute or escaping destination must be a refusal,
        # never something quietly rewritten into a plausible relative path. The materializer refuses
        # the same shapes for the same reason — there its consequence is a recursive delete outside
        # the root it was told to sweep.
        if not dest or dest.startswith("/") or dest.startswith("../") or "/../" in dest or dest == "..":
            raise Refused(f"{manifest}:{number}: destination {dest!r} is absolute or escapes the receiver root.")
        rows.append((fields[0], dest.rstrip("/")))
    if not rows:
        raise Refused(f"{manifest} names no members.")
    return rows


def read_properties(path: Path) -> dict[str, str]:
    """The receiver's own MSBuild evaluation, as `dotnet msbuild -getProperty:` emits it."""
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise Refused(f"cannot read {path}: {error}") from error
    if not isinstance(raw, dict):
        raise Refused(f"{path}: expected an object.")
    props = raw.get("Properties", raw)
    if not isinstance(props, dict):
        raise Refused(f"{path}: 'Properties' is not an object.")
    missing = [name for name in PROPERTY_NAMES if name not in props]
    if missing:
        raise Refused(
            f"{path}: missing evaluated propert(y|ies): {', '.join(missing)}. Produce it with "
            f"`dotnet msbuild <receiver.proj> -getProperty:{','.join(PROPERTY_NAMES)}` so the roots "
            "are the receiver's OWN evaluation of the target package, not a default typed here."
        )
    return {name: str(props[name] or "") for name in PROPERTY_NAMES}


def split_roots(value: str) -> list[str]:
    return [part.strip().strip("/") for part in value.split(";") if part.strip()]


def is_truthy(value: str) -> bool:
    return value.strip().lower() == "true"


class Shape:
    """The admissible class, derived from the target package and the receiver's evaluation."""

    def __init__(self, kit_dir: Path, properties: dict[str, str]) -> None:
        self.version = read_package_version(kit_dir)
        rows = read_manifest(kit_dir)

        self.live = split_roots(properties["FsggKitSkillRoots"])
        self.retired = split_roots(properties["FsggKitRetiredSkillRoots"])
        self.view = split_roots(properties["FsggKitViewSkillRoots"])
        self.build_config = is_truthy(properties["FsggKitMaterializeBuildConfig"])

        # The materializer REFUSES a root declared in more than one property rather than picking a
        # disposition (FS.GG.Kit.targets). A guard that resolved the ambiguity differently would
        # certify a tree the materializer will not produce, so it refuses in the same place.
        for a_name, a, b_name, b in (
            ("FsggKitSkillRoots", self.live, "FsggKitRetiredSkillRoots", self.retired),
            ("FsggKitSkillRoots", self.live, "FsggKitViewSkillRoots", self.view),
            ("FsggKitRetiredSkillRoots", self.retired, "FsggKitViewSkillRoots", self.view),
        ):
            both = sorted(set(a) & set(b))
            if both:
                raise Refused(
                    f"root(s) {', '.join(both)} declared in BOTH {a_name} and {b_name}. The "
                    "materializer refuses this rather than choosing a disposition; so does this guard."
                )
        if not self.live:
            raise Refused(
                "FsggKitSkillRoots is empty — the target package materializes into no root. "
                "Refusing: an empty live set would make every materialized path a finding."
            )

        # SKILLS: the top-level directory name of each skill destination. This, not the full
        # destination, is the unit the sweeps delete — `FS.GG.Kit.targets` removes
        # `<root>/<skillName>` recursively — so it is the unit the delete rules are keyed on.
        self.skills: set[str] = set()
        self.skill_dests: set[str] = set()
        self.flat: set[str] = set()
        for kind, dest in rows:
            if kind == "skill":
                self.skills.add(dest.split("/", 1)[0])
                self.skill_dests.add(dest)
            elif kind in ("client", "config"):
                self.flat.add(dest)
            elif kind == "build-config":
                # Opt-in, per receiver. A receiver that does not receive build-config never has these
                # written, so admitting them unconditionally would widen the class for four repos that
                # cannot produce them.
                if self.build_config:
                    self.flat.add(dest)
            else:
                raise Refused(f"unknown manifest kind {kind!r} — refusing to classify what it produces.")

        if not self.skills:
            raise Refused(
                "the target package's manifest names no skill rows — refusing to certify a bump "
                "against an empty skill set (epic #266: an empty subject must not satisfy itself)."
            )

    def writable(self) -> set[str]:
        """Paths the materializer WRITES: exact destinations only, never a prefix."""
        paths = set(self.flat)
        for root in self.live:
            for dest in self.skill_dests:
                paths.add(f"{root}/{dest}")
        return paths

    def deletable_prefixes(self) -> list[tuple[str, str]]:
        """(prefix, why) for every managed skill directory any sweep may delete from."""
        out: list[tuple[str, str]] = []
        for root in self.live:
            for skill in sorted(self.skills):
                out.append((f"{root}/{skill}/", f"managed-skill-directory sweep under live root {root}"))
        for root in self.retired:
            for skill in sorted(self.skills):
                out.append((f"{root}/{skill}/", f"retired-root sweep under {root} (deletions only)"))
        for root in self.view:
            for skill in sorted(self.skills):
                out.append((f"{root}/{skill}/", f"view-root sweep under {root} (deletions only)"))
        return out

    def delete_only_roots(self) -> list[str]:
        return self.retired + self.view


def changed_paths(repo: Path, base: str, head: str) -> list[tuple[str, str]]:
    """(status, path) for the PR's diff. Renames are REFUSED, not decomposed by guesswork."""
    raw = git(repo, "diff", "--no-renames", "--name-status", "-z", f"{base}...{head}")
    fields = [f for f in raw.split("\0") if f]
    if len(fields) % 2 != 0:
        raise Refused("git diff --name-status -z produced an odd number of fields.")
    out: list[tuple[str, str]] = []
    for status, path in zip(fields[0::2], fields[1::2]):
        code = status[0]
        if code not in ("A", "M", "D"):
            raise Refused(
                f"diff status {status!r} on {path} is not add/modify/delete. The materializer produces "
                "only those three; anything else is outside this guard's competence, so it refuses."
            )
        out.append((code, path.replace("\\", "/")))
    return out


def pin_files(repo: Path, base: str, head: str, modified: list[str]) -> dict[str, dict]:
    """Find the pin BY ITS CONTENT. Returns {path: {"to": …, "from": …, "clean": bool}}."""
    found: dict[str, dict] = {}
    for path in modified:
        patch = git(repo, "diff", "-U0", "--no-renames", f"{base}...{head}", "--", path)
        added = [ln[1:] for ln in patch.splitlines() if ln.startswith("+") and not ln.startswith("+++")]
        removed = [ln[1:] for ln in patch.splitlines() if ln.startswith("-") and not ln.startswith("---")]
        if not any(PIN_INCLUDE.search(ln) for ln in added + removed):
            continue
        # Every changed line in this file must BE a pin declaration. A bump PR that also edits a
        # neighbouring line of the same file is not a one-line version bump, and #1587's whole claim
        # is that the class is provably mechanical.
        stray = [ln for ln in added + removed if not PIN_INCLUDE.search(ln)]
        versions_to = [PIN_VERSION.search(ln) for ln in added]
        versions_from = [PIN_VERSION.search(ln) for ln in removed]
        found[path] = {
            "clean": not stray and all(versions_to) and all(versions_from) and bool(added),
            "removes_only": bool(removed) and not added,
            "stray": stray,
            "to": sorted({m.group(1) for m in versions_to if m}),
            "from": sorted({m.group(1) for m in versions_from if m}),
        }
    return found


def classify(shape: Shape, entries: list[tuple[str, str]], pin: str) -> list[tuple[str, str, str]]:
    """(status, path, reason-or-'') — reason non-empty means the path is a finding."""
    writable = shape.writable()
    prefixes = shape.deletable_prefixes()
    delete_only = set(shape.delete_only_roots())
    verdicts: list[tuple[str, str, str]] = []
    for status, path in entries:
        if path == pin:
            verdicts.append((status, path, ""))
            continue
        if status in ("A", "M"):
            if path in writable:
                verdicts.append((status, path, ""))
                continue
            root = next((r for r in delete_only if path == r or path.startswith(r + "/")), None)
            if root is not None:
                verdicts.append((
                    status,
                    path,
                    f"{'added' if status == 'A' else 'modified'} under {root}, which the target "
                    "version declares RETIRED or VIEW — such a root admits DELETIONS ONLY. The "
                    "materializer never writes there, so this content came from somewhere else.",
                ))
                continue
            verdicts.append((
                status,
                path,
                "not a destination the target package materializes. The admissible write set is the "
                "manifest's client/config destinations plus <live root>/<skill destination>.",
            ))
            continue
        # status == "D"
        why = next((why for prefix, why in prefixes if path.startswith(prefix)), None)
        if why is not None:
            verdicts.append((status, path, ""))
            continue
        verdicts.append((
            status,
            path,
            "deleted outside every managed skill directory the target version declares. Deletions are "
            f"admissible only under <root>/<skill>/ for root in {shape.live + shape.retired + shape.view} "
            "and skill in the target manifest's own skill set.",
        ))
    return verdicts


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Decide whether an FS.GG.Kit bump PR's diff is the mechanical, automergeable shape."
    )
    parser.add_argument("--repo", required=True, type=Path, help="receiver checkout to read the diff from")
    parser.add_argument("--base", required=True, help="the PR's base ref")
    parser.add_argument("--head", required=True, help="the PR's head ref")
    parser.add_argument("--kit-dir", required=True, type=Path, help="the TARGET version's extracted package")
    parser.add_argument("--properties", required=True, type=Path, help="`dotnet msbuild -getProperty:` JSON")
    parser.add_argument("--json", action="store_true", help="emit the verdict as one object")
    args = parser.parse_args(argv)

    try:
        entries = changed_paths(args.repo, args.base, args.head)
        modified = [path for status, path in entries if status == "M"]
        pins = pin_files(args.repo, args.base, args.head, modified)

        if not pins:
            verdict = {
                "verdict": "not-a-kit-bump",
                "reason": f"no {PACKAGE} version declaration changes in {args.base}...{args.head}",
                "changed": len(entries),
            }
            code = NOT_A_KIT_BUMP
        else:
            shape = Shape(args.kit_dir, read_properties(args.properties))
            if len(pins) != 1:
                raise Refused(
                    f"{len(pins)} files change a {PACKAGE} pin ({', '.join(sorted(pins))}). A receiver "
                    "pins in exactly one place; two is a receiver defect, not a mechanical bump."
                )
            pin_path, pin_info = next(iter(pins.items()))
            if pin_path in shape.writable():
                raise Refused(
                    f"{pin_path} is BOTH the pin and a destination the target package materializes. "
                    "Re-materializing would overwrite the pin; refusing rather than certifying it."
                )
            if pin_info["removes_only"]:
                raise Refused(
                    f"{pin_path} REMOVES a {PACKAGE} pin declaration and adds none. That is an "
                    "offboarding, not a bump; this guard has no mechanical class for it."
                )
            if not pin_info["clean"]:
                raise Refused(
                    f"{pin_path} changes lines that are not {PACKAGE} version declarations "
                    f"({len(pin_info['stray'])} of them) or a declaration whose version cannot be read."
                )
            if pin_info["to"] != [shape.version]:
                raise Refused(
                    f"the diff moves the pin to {pin_info['to']} but --kit-dir is {PACKAGE} "
                    f"{shape.version}. Restore the version this PR bumps TO, then re-run."
                )

            verdicts = classify(shape, entries, pin_path)
            findings = [(s, p, why) for s, p, why in verdicts if why]
            counts = {"A": 0, "M": 0, "D": 0}
            for status, _path in entries:
                counts[status] += 1
            verdict = {
                "verdict": "mechanical" if not findings else "contaminated",
                "target": shape.version,
                "pin": {"path": pin_path, "from": pin_info["from"], "to": pin_info["to"]},
                "roots": {
                    "live": shape.live,
                    "retired": shape.retired,
                    "view": shape.view,
                },
                "buildConfig": shape.build_config,
                "skills": sorted(shape.skills),
                "changed": {"added": counts["A"], "modified": counts["M"], "deleted": counts["D"]},
                "findings": [{"status": s, "path": p, "why": why} for s, p, why in findings],
            }
            code = MECHANICAL if not findings else CONTAMINATED
    except Refused as error:
        if args.json:
            print(json.dumps({"verdict": "refused", "reason": str(error)}, indent=2))
        else:
            print(f"::error::check-kit-bump-shape: REFUSED — {error}", file=sys.stderr)
        return REFUSED

    if args.json:
        print(json.dumps(verdict, indent=2))
        return code

    if code == NOT_A_KIT_BUMP:
        print(
            f"check-kit-bump-shape: ABSTAINS — {verdict['reason']}. This is NOT a pass: an automerge "
            "rule must treat an abstention as 'do not merge'."
        )
        return code
    if code == MECHANICAL:
        changed = verdict["changed"]
        print(
            f"ok: mechanical {PACKAGE} bump to {verdict['target']} — pin {verdict['pin']['path']} "
            f"{verdict['pin']['from']} -> {verdict['pin']['to']}, plus {changed['added']} added / "
            f"{changed['modified']} modified materialized output(s) and {changed['deleted']} "
            f"deletion(s) confined to swept skill directories under "
            f"{verdict['roots']['live'] + verdict['roots']['retired'] + verdict['roots']['view']}. "
            "Every path is derived from the target package's kit-manifest.tsv and the receiver's own "
            "evaluation of its root declarations."
        )
        return code

    lines = "\n".join(f"    {f['status']}  {f['path']}\n        {f['why']}" for f in verdict["findings"])
    print(
        f"::error::check-kit-bump-shape: this {PACKAGE} bump to {verdict['target']} carries "
        f"{len(verdict['findings'])} change(s) OUTSIDE the mechanical class, so it must not "
        f"automerge (.github#1587 AC 2):\n{lines}\n"
        f"Admissible: the pin; the target manifest's client/config destinations; "
        f"<root>/<skill destination> for root in {verdict['roots']['live']}; and DELETIONS ONLY under "
        f"<root>/<skill>/ for root in {verdict['roots']['live'] + verdict['roots']['retired'] + verdict['roots']['view']}.",
        file=sys.stderr,
    )
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
