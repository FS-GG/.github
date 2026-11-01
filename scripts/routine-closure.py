#!/usr/bin/env python3
"""routine-closure: apply and check the five-file routine-eligibility producer closure in a receiver (.github#5202).

The closure is exactly these five repository-relative paths. Their canonical bytes and executable bits come from
one of two equivalent sources, which must agree with each other:

  --source DIR     a checkout of FS-GG/.github (default: the checkout holding this script)
  --payload DIR    a restored FS.GG.Drivers `workspace/` directory (workspace-files.json + files/<path>);
                   stale-pin detection then needs --history DIR, a .github checkout with full git history

A receiver adopts all five or none (#5191 review F2: a mixed old/new set breaks).

  apply   RECEIVER   validate all five canonical files first, then write all five (never a partial set).
  check   RECEIVER   exit 1 unless the five files equal canonical (bytes and executable bit), and unless no
                     repo-local file pins an OLD digest of any of the five. A pin to the current digest is
                     reported as `pin-current` and accepted.
  sweep   REPO...    shallow-clone each `owner/name` (default owner FS-GG) with `gh` and run `check`.

Repo-local pins are 64-hex sha256 tokens in tracked text files other than the five. A digest is OLD when it is the
sha256 of an earlier revision of one of the five paths in the --source checkout's git history and is not the
current digest of any of them. `check` needs that history; with a shallow --source it says so and treats only the
current digests as known (it then cannot name stale pins, and exits 2 rather than reporting a clean result).

Exit: 0 clean; 1 drift or stale pin; 2 usage or an input that cannot be trusted (unreadable payload, shallow history).
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
HEX64 = re.compile(r"(?<![0-9a-fA-F])[0-9a-f]{64}(?![0-9a-fA-F])")
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


def canonical(args: argparse.Namespace) -> dict[str, tuple[bytes, bool]]:
    if args.payload and args.source:
        fail("give --source or --payload, not both")
    if args.payload:
        return load_payload(Path(args.payload))
    return load_source(Path(args.source) if args.source else Path(__file__).resolve().parent.parent)


def git(root: Path, *argv: str) -> subprocess.CompletedProcess:
    return subprocess.run(["git", "-C", str(root), *argv], capture_output=True, check=False)


def historical_digests(root: Path) -> tuple[dict[str, str], bool]:
    """Map sha256 -> `path@rev` for every earlier revision of a closure path, and whether history is complete."""
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
    return [p for p in receiver.rglob("*") if p.is_file() and ".git" not in p.parts]


def pin_scan(receiver: Path, current: dict[str, str], old: dict[str, str],
             waived: frozenset[str] = frozenset()) -> tuple[list[str], list[str]]:
    stale, fine = [], []
    skip = {receiver / p for p in CLOSURE}
    for file in tracked_files(receiver):
        if file in skip or not file.is_file() or file.is_symlink() or file.stat().st_size > MAX_PIN_FILE:
            continue
        try:
            text = file.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for number, line in enumerate(text.splitlines(), 1):
            for token in HEX64.findall(line):
                where = f"{file.relative_to(receiver)}:{number}"
                if token in current:
                    fine.append(f"pin-current {where} = {current[token]}")
                elif token in old and str(file.relative_to(receiver)) in waived:
                    fine.append(f"waived-pin {where} = {old[token]} (receiver-declared historical record)")
                elif token in old:
                    stale.append(f"stale-pin {where} = {old[token]} (not current)")
    return stale, fine


def do_apply(args: argparse.Namespace) -> int:
    receiver = Path(args.receiver)
    if not receiver.is_dir():
        fail(f"receiver {receiver} is not a directory")
    files = canonical(args)  # every file is loaded and verified before the first write
    for path, (data, executable) in files.items():
        dest = receiver / path
        if dest.is_symlink():
            fail(f"refusing to write through symlink {path}")
    staged = []
    for path, (data, executable) in files.items():
        dest = receiver / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp = tempfile.mkstemp(dir=dest.parent, prefix=".routine-closure.")
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
        os.chmod(tmp, 0o755 if executable else 0o644)
        staged.append((tmp, dest))
    for tmp, dest in staged:
        os.replace(tmp, dest)
    print(f"routine-closure: applied {len(files)} files to {receiver}")
    return 0


def do_check(args: argparse.Namespace) -> int:
    receiver = Path(args.receiver)
    if not receiver.is_dir():
        fail(f"receiver {receiver} is not a directory")
    files = canonical(args)
    drift = []
    for path, (data, executable) in files.items():
        file = receiver / path
        if not file.is_file():
            drift.append(f"absent {path}")
        elif file.read_bytes() != data:
            drift.append(f"differs {path} (receiver {sha(file.read_bytes())[:12]}, canonical {sha(data)[:12]})")
        elif bool(file.stat().st_mode & stat.S_IXUSR) != executable:
            drift.append(f"mode {path} (canonical executable={executable})")
    present = sum(1 for p in CLOSURE if (receiver / p).is_file())
    equal = len(CLOSURE) - len(drift)
    if drift and equal:
        drift.append(f"mixed set: {equal} of {len(CLOSURE)} files equal canonical; adopt all five atomically")
    current = {sha(data): path for path, (data, _) in files.items()}
    source = Path(args.history or args.source or Path(__file__).resolve().parent.parent)
    old, complete = historical_digests(source)
    old = {digest: label for digest, label in old.items() if digest not in current}
    stale, fine = pin_scan(receiver, current, old, frozenset(args.waive or ()))
    for line in drift + stale + fine:
        print(line)
    if not complete and not args.allow_shallow:
        print("routine-closure: source history is shallow or absent, so stale pins cannot be named; "
              "fetch full history or pass --allow-shallow to accept that gap", file=sys.stderr)
        return 1 if drift else 2
    status = "drift" if (drift or stale) else "ok"
    print(f"routine-closure: {getattr(args, 'label', None) or receiver.name}: {status} (present {present}/5, equal {equal}/5, stale pins {len(stale)})")
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
            sub = argparse.Namespace(receiver=tmp + "/r", source=args.source, payload=args.payload, history=args.history,
                                     allow_shallow=args.allow_shallow, waive=args.waive, label=slug)
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
        p.add_argument("--waive", action="append", metavar="PATH",
                       help="receiver-relative file whose OLD digests are a historical record, not a live pin (repeatable)")
        p.set_defaults(fn=fn)
    p = sub.add_parser("sweep")
    p.add_argument("repos", nargs="+")
    p.add_argument("--source")
    p.add_argument("--payload")
    p.add_argument("--history")
    p.add_argument("--allow-shallow", action="store_true")
    p.add_argument("--waive", action="append", metavar="PATH")
    p.set_defaults(fn=do_sweep)
    args = parser.parse_args(argv)
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
