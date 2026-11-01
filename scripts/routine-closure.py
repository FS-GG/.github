#!/usr/bin/env python3
"""routine-closure: apply and check the five-file routine-eligibility producer closure in a receiver (.github#5202).

The closure is these five repository-relative paths. Their canonical bytes and executable bits come from one of:

  --source DIR     a checkout of FS-GG/.github (default: the checkout holding this script)
  --payload DIR    a restored FS.GG.Drivers `workspace/` directory (workspace-files.json + files/<path>);
                   stale-pin detection then also needs --history DIR, a .github checkout with full git history

A receiver adopts all five or none (#5191 review F2: a mixed old/new set breaks).

  apply   RECEIVER   validate all five canonical files, then replace all five or none. Every original is kept in
                     memory; if any step fails, every file already replaced is restored (and a file that did not
                     exist is removed), and every temporary file is removed. Refuses a closure path that is a
                     symlink or a directory, and any symlinked parent directory under the receiver.
  check   RECEIVER   exit 1 unless the five files are regular files equal to canonical (bytes and executable bit;
                     a symlink is drift, because CI's base blob would be the link text), and unless no repo-local
                     file pins an OLD digest of any of the five. A pin to a current digest is `pin-current`.
  sweep   REPO...    shallow-clone each `owner/name` (default owner FS-GG) with `gh` and run `check`.

Repo-local pins are 64-hex sha256 tokens (any case) in tracked text files other than the five. A digest is OLD
when it is the sha256 of an earlier revision of one of the five paths in the history clone and is not a current
digest. Blind spot: a receiver-local digest that is in NO .github history (for example the pre-adoption copies that
Game and Audio carried) is not recognised as a pin. Files that cannot be scanned (not UTF-8, over 8 MiB, symlinks)
are listed as `skipped`, never silently ignored.

Waivers. A receiver may declare, in its own `.fsgg/routine-closure-waivers.json`, that an OLD digest in one file
is a historical record rather than a live pin:

  {"schema": "fsgg.routine-closure-waivers/1",
   "waivers": [{"path": "evidence/x.json", "sha256": "<64 hex>", "reason": "why this is a record"}]}

A waiver matches one digest in one file. Any other old digest in the same file, or the same digest in another
file, still fails. `sweep` reads each repository's own declaration. A malformed declaration is exit 2.

Exit: 0 clean; 1 drift or stale pin; 2 usage or an input that cannot be trusted (unreadable payload or waiver
file, shallow history, a payload digest that is not a revision in the history clone).
Pure stdlib; `sweep` additionally needs `gh` and network.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

CLOSURE = (
    ".fsgg/routine-development.json",
    ".github/workflows/routine-eligibility.yml",
    "scripts/check-claim-generation.py",
    "scripts/check-routine-eligibility-envelope.py",
    "scripts/lib/gate.py",
)
WAIVERS_PATH = ".fsgg/routine-closure-waivers.json"
WAIVERS_SCHEMA = "fsgg.routine-closure-waivers/1"
HEX64 = re.compile(r"(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])")
MAX_PIN_FILE = 8 * 1024 * 1024


def fail(message: str, code: int = 2) -> "None":
    print(f"routine-closure: {message}", file=sys.stderr)
    raise SystemExit(code)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_source(root: Path) -> dict[str, tuple[bytes, bool]]:
    out = {}
    for path in CLOSURE:
        file = root / path
        if not file.is_file() or file.is_symlink():
            fail(f"canonical source lacks regular file {path} under {root}")
        out[path] = (file.read_bytes(), bool(file.stat().st_mode & stat.S_IXUSR))
    return out


def load_payload(root: Path) -> dict[str, tuple[bytes, bool]]:
    try:
        manifest = json.loads((root / "workspace-files.json").read_text(encoding="utf-8"))
        rows = {row["path"]: row for row in manifest["files"]}
    except (OSError, ValueError, KeyError, TypeError) as error:
        fail(f"cannot read {root}/workspace-files.json: {error}")
    out = {}
    for path in CLOSURE:
        row = rows.get(path)
        if row is None:
            fail(f"payload manifest lacks {path}")
        file = root / "files" / path
        if not file.is_file() or file.is_symlink():
            fail(f"payload lacks regular file files/{path}")
        data = file.read_bytes()
        if sha(data) != row.get("sha256"):
            fail(f"payload digest mismatch for {path}")
        out[path] = (data, bool(row.get("executable")))
    return out


def git(root: Path, *argv: str) -> subprocess.CompletedProcess:
    return subprocess.run(["git", "-C", str(root), *argv], capture_output=True, check=False)


def source_root(args: argparse.Namespace) -> Path:
    return Path(args.source) if args.source else Path(__file__).resolve().parent.parent


def canonical(args: argparse.Namespace) -> tuple[dict[str, tuple[bytes, bool]], str]:
    """The five canonical files and a one-line identity of where they came from."""
    if args.payload and args.source:
        fail("give --source or --payload, not both")
    if args.payload:
        root = Path(args.payload)
        files = load_payload(root)
        manifest = sha((root / "workspace-files.json").read_bytes())[:12]
        return files, f"payload {root} (workspace-files.json sha256 {manifest})"
    root = source_root(args)
    files = load_source(root)
    head = git(root, "rev-parse", "HEAD").stdout.decode().strip() or "no git revision"
    dirty = git(root, "status", "--porcelain", "--", *CLOSURE).stdout.strip()
    return files, f"source {root} ({head[:12]}{', closure files modified in the working tree' if dirty else ''})"


def announce(identity: str, files: dict[str, tuple[bytes, bool]]) -> None:
    print(f"routine-closure: canonical {identity}")
    for path, (data, executable) in files.items():
        print(f"routine-closure:   {sha(data)[:12]} {'x' if executable else '-'} {path}")


def historical_digests(root: Path) -> tuple[dict[str, str], bool]:
    """Map sha256 -> `path@rev` for every revision of a closure path, and whether history is complete."""
    if git(root, "rev-parse", "--git-dir").returncode != 0:
        return {}, False
    shallow = git(root, "rev-parse", "--is-shallow-repository").stdout.decode().strip() == "true"
    seen: dict[str, str] = {}
    for path in CLOSURE:
        revs = git(root, "log", "--format=%H", "--", path).stdout.decode().split()
        for rev in revs:
            blob = git(root, "show", f"{rev}:{path}")
            if blob.returncode == 0:
                seen.setdefault(sha(blob.stdout), f"{path}@{rev[:8]}")
    return seen, not shallow


def tracked_files(receiver: Path) -> list[Path]:
    listed = git(receiver, "ls-files", "-z")
    if listed.returncode == 0:
        return [receiver / name for name in listed.stdout.decode("utf-8", "replace").split("\0") if name]
    return [p for p in receiver.rglob("*") if (p.is_file() or p.is_symlink()) and ".git" not in p.parts]


def path_problem(receiver: Path, rel: str) -> str | None:
    """Why `rel` is not a plain regular file reachable without a symlink, or None."""
    current = receiver
    parts = Path(rel).parts
    for index, part in enumerate(parts):
        current = current / part
        if current.is_symlink():
            return "symlink" if index == len(parts) - 1 else f"symlinked parent {Path(*parts[:index + 1])}"
        if index < len(parts) - 1 and current.exists() and not current.is_dir():
            return f"parent {Path(*parts[:index + 1])} is not a directory"
    if current.exists() and current.is_dir():
        return "a directory"
    return None


def load_waivers(receiver: Path) -> dict[tuple[str, str], str]:
    file = receiver / WAIVERS_PATH
    if not file.exists():
        return {}
    if file.is_symlink() or not file.is_file():
        fail(f"{WAIVERS_PATH} must be a regular file")
    try:
        data = json.loads(file.read_text(encoding="utf-8"))
        if data.get("schema") != WAIVERS_SCHEMA or not isinstance(data.get("waivers"), list):
            raise ValueError(f"schema must be {WAIVERS_SCHEMA} with a waivers list")
        out = {}
        for entry in data["waivers"]:
            path, digest, reason = entry["path"], entry["sha256"], entry["reason"]
            if (not isinstance(path, str) or not path or not isinstance(reason, str) or not reason.strip()
                    or not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest)):
                raise ValueError(f"bad entry {entry!r}: need path, lowercase 64-hex sha256 and a reason")
            out[(path, digest)] = reason.strip()
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        fail(f"{WAIVERS_PATH} cannot be trusted: {error}")
    return out


def pin_scan(receiver: Path, current: dict[str, str], old: dict[str, str],
             waivers: dict[tuple[str, str], str]) -> tuple[list[str], list[str], list[str], set]:
    stale, notes, skipped, used = [], [], [], set()
    skip = {receiver / p for p in CLOSURE} | {receiver / WAIVERS_PATH}
    for file in tracked_files(receiver):
        rel = str(file.relative_to(receiver))
        if file in skip:
            continue
        if file.is_symlink():
            skipped.append(f"skipped {rel} (symlink)")
            continue
        if not file.is_file():
            continue
        if file.stat().st_size > MAX_PIN_FILE:
            skipped.append(f"skipped {rel} (over {MAX_PIN_FILE // (1024 * 1024)} MiB)")
            continue
        try:
            text = file.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            skipped.append(f"skipped {rel} (not UTF-8)")
            continue
        except OSError as error:
            skipped.append(f"skipped {rel} (unreadable: {error.strerror})")
            continue
        for number, line in enumerate(text.splitlines(), 1):
            for raw in HEX64.findall(line):
                token, where = raw.lower(), f"{rel}:{number}"
                if token in current:
                    notes.append(f"pin-current {where} = {current[token]}")
                elif token in old and (rel, token) in waivers:
                    used.add((rel, token))
                    notes.append(f"waived-pin {where} = {old[token]} ({waivers[(rel, token)]})")
                elif token in old:
                    stale.append(f"stale-pin {where} = {old[token]} (not current)")
    return stale, notes, skipped, used


def discard(temps: list[str]) -> None:
    for tmp in list(temps):
        try:
            os.unlink(tmp)
        except FileNotFoundError:
            pass
        except OSError:
            continue
        temps.remove(tmp)


def do_apply(args: argparse.Namespace) -> int:
    receiver = Path(args.receiver)
    if not receiver.is_dir():
        fail(f"receiver {receiver} is not a directory")
    files, identity = canonical(args)  # every file is loaded and verified before anything is touched
    announce(identity, files)
    for path in CLOSURE:
        problem = path_problem(receiver, path)
        if problem is not None:
            fail(f"refusing to write {path}: {problem}")
    originals: dict[str, tuple[bytes, int] | None] = {}
    for path in CLOSURE:
        dest = receiver / path
        originals[path] = (dest.read_bytes(), stat.S_IMODE(dest.stat().st_mode)) if dest.exists() else None
    temps: list[str] = []
    created_dirs: list[Path] = []
    replaced: list[str] = []
    try:
        staged = []
        for path, (data, executable) in files.items():
            dest = receiver / path
            missing = [d for d in reversed(dest.parents) if d != receiver and receiver in d.parents and not d.exists()]
            dest.parent.mkdir(parents=True, exist_ok=True)
            created_dirs.extend(missing)
            fd, tmp = tempfile.mkstemp(dir=dest.parent, prefix=".routine-closure.")
            temps.append(tmp)
            with os.fdopen(fd, "wb") as stream:
                stream.write(data)
            os.chmod(tmp, 0o755 if executable else 0o644)
            staged.append((tmp, dest, path))
        for tmp, dest, path in staged:
            os.replace(tmp, dest)
            temps.remove(tmp)
            replaced.append(path)
    except BaseException as error:
        problems = []
        discard(temps)  # staged temporaries first, so the directories this run created can be removed empty
        for path in reversed(replaced):
            dest = receiver / path
            try:
                original = originals[path]
                if original is None:
                    dest.unlink()
                else:
                    fd, tmp = tempfile.mkstemp(dir=dest.parent, prefix=".routine-closure.")
                    temps.append(tmp)
                    with os.fdopen(fd, "wb") as stream:
                        stream.write(original[0])
                    os.chmod(tmp, original[1])
                    os.replace(tmp, dest)
                    temps.remove(tmp)
            except OSError as restore_error:
                problems.append(f"{path}: {restore_error}")
        for directory in reversed(created_dirs):
            try:
                directory.rmdir()
            except OSError:
                pass
        message = f"apply failed ({type(error).__name__}: {error}); "
        message += ("rolled back, the receiver is unchanged" if not problems
                    else "ROLLBACK INCOMPLETE, restore these from git: " + "; ".join(problems))
        fail(message)
    finally:
        discard(temps)
    print(f"routine-closure: applied {len(files)} files to {receiver}")
    return 0


def do_check(args: argparse.Namespace) -> int:
    receiver = Path(args.receiver)
    if not receiver.is_dir():
        fail(f"receiver {receiver} is not a directory")
    files, identity = canonical(args)
    announce(identity, files)
    drift = []
    for path, (data, executable) in files.items():
        problem = path_problem(receiver, path)
        file = receiver / path
        if problem is not None:
            drift.append(f"not-regular {path} ({problem}; CI reads the base blob, which would not be this file)")
        elif not file.is_file():
            drift.append(f"absent {path}")
        elif file.read_bytes() != data:
            drift.append(f"differs {path} (receiver {sha(file.read_bytes())[:12]}, canonical {sha(data)[:12]})")
        elif bool(file.stat().st_mode & stat.S_IXUSR) != executable:
            drift.append(f"mode {path} (canonical executable={executable})")
    present = sum(1 for p in CLOSURE if path_problem(receiver, p) is None and (receiver / p).is_file())
    equal = len(CLOSURE) - len(drift)
    if drift and equal:
        drift.append(f"mixed set: {equal} of {len(CLOSURE)} files equal canonical; adopt all five atomically")
    current = {sha(data): path for path, (data, _) in files.items()}
    old_all, complete = historical_digests(Path(args.history) if args.history else source_root(args))
    missing_revision = [p for d, p in ((sha(data), path) for path, (data, _) in files.items()) if d not in old_all]
    if old_all and missing_revision:
        for line in drift:
            print(line)
        fail("canonical digest of " + ", ".join(missing_revision) + " is not a revision in the history clone; "
             "the history does not belong to this payload, so stale pins cannot be judged")
    old = {digest: label for digest, label in old_all.items() if digest not in current}
    waivers = load_waivers(receiver)
    stale, notes, skipped, used = pin_scan(receiver, current, old, waivers)
    unused = [f"unused-waiver {p} {d[:12]}" for (p, d) in sorted(waivers) if (p, d) not in used]
    for line in drift + stale + notes + skipped + unused:
        print(line)
    if not complete:
        if not args.allow_shallow:
            print("routine-closure: source history is shallow or absent, so stale pins cannot be named; "
                  "fetch full history or pass --allow-shallow to accept that gap", file=sys.stderr)
            return 1 if drift else 2
        print("routine-closure: stale-pin detection OFF (shallow or absent history, --allow-shallow)")
    print("routine-closure: note: a receiver-local digest that is in no .github history (a pre-adoption copy) is not "
          "recognised as a pin")
    status = "drift" if (drift or stale) else "ok"
    print(f"routine-closure: {getattr(args, 'label', None) or receiver.name}: {status} "
          f"(present {present}/5, equal {equal}/5, stale pins {len(stale)}, skipped {len(skipped)})")
    return 1 if (drift or stale) else 0


def do_sweep(args: argparse.Namespace) -> int:
    worst = 0
    for name in args.repos:
        slug = name if "/" in name else f"FS-GG/{name}"
        with tempfile.TemporaryDirectory(prefix="routine-closure-") as tmp:
            clone = subprocess.run(["gh", "repo", "clone", slug, tmp + "/r", "--", "--depth", "1", "-q"],
                                   capture_output=True, text=True, check=False)
            if clone.returncode != 0:
                print(f"{slug}: cannot clone: {clone.stderr.strip()[:200]}")
                worst = max(worst, 2)
                continue
            sub = argparse.Namespace(receiver=tmp + "/r", source=args.source, payload=args.payload,
                                     history=args.history, allow_shallow=args.allow_shallow, label=slug)
            print(f"== {slug}")
            worst = max(worst, do_check(sub))
    return worst


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(prog="routine-closure", description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, fn in (("apply", do_apply), ("check", do_check)):
        p = sub.add_parser(name)
        p.add_argument("receiver")
        p.add_argument("--source")
        p.add_argument("--payload")
        p.add_argument("--history", help="git checkout of FS-GG/.github whose history names OLD digests (default: --source)")
        p.add_argument("--allow-shallow", action="store_true")
        p.set_defaults(fn=fn)
    p = sub.add_parser("sweep")
    p.add_argument("repos", nargs="+")
    p.add_argument("--source")
    p.add_argument("--payload")
    p.add_argument("--history")
    p.add_argument("--allow-shallow", action="store_true")
    p.set_defaults(fn=do_sweep)
    args = parser.parse_args(argv)
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
