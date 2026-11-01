#!/usr/bin/python3 -I
"""Install the fdev board-tend units (board-reconciler design section 8, #5136 PR 8). User-level only.

Three subcommands, each writing one kind of thing and nothing else. Every one has --dry-run, which prints what it
would write and changes nothing. None needs or uses root; every destination must be under $HOME.

  units        Renders fsgg-board-tend.{service,timer} and fsgg-board-tend-daily.{service,timer} into the user
               systemd directory and copies the wrapper to the libexec directory. Plan-only unless --mode apply,
               which is refused unless the enable file already names at least one rule. Without --enable it does
               not touch systemd; with it, it runs `systemctl --user daemon-reload` and enables the two timers.
  kit-root     Writes the two lifecycle-contract.md mirrors of one released ref into the pinned kit root, after
               checking each against the expected sha256 and for exactly one fsgg:board-tend-contract/v1 marker.
  enable-file  Writes the EMPTY enable file ({"apply": []}) with mode 0600 and never replaces an existing one.

The engine and the kit root are pinned to one release: the engine must live in .../coord-<version>/ and the kit
root must be named kit-<version>.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import stat
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
UNIT_TEMPLATES = ("fsgg-board-tend.service", "fsgg-board-tend.timer", "fsgg-board-tend-daily.service", "fsgg-board-tend-daily.timer")
TIMERS = ("fsgg-board-tend.timer", "fsgg-board-tend-daily.timer")
WRAPPER = "fsgg-board-tend-run.py"
ENABLE_TEMPLATE = "board-tend.json"
MIRRORS = (".claude/skills/work-board/references/lifecycle-contract.md", ".agents/skills/work-board/references/lifecycle-contract.md")
MARKER_LINE = re.compile(r"^\s*<!--\s*fsgg:board-tend-contract/(\S*?)\s*-->\s*$")
FENCE_LINE = re.compile(r"^\s*(```|~~~)")
SAFE_PATH = re.compile(r"/[A-Za-z0-9._+/-]+")  # no space, %, $ or quote: these paths go into unit files unescaped
RUNTIME_MAX_SECONDS = 900


class Refusal(Exception):
    pass


def home() -> pathlib.Path:
    return pathlib.Path(os.environ.get("HOME") or os.path.expanduser("~"))


def user_path(text: str, label: str) -> pathlib.Path:
    path = pathlib.Path(os.path.expanduser(text))
    if not path.is_absolute():
        raise Refusal(f"{label} must be an absolute path")
    path = pathlib.Path(os.path.normpath(path))
    if not SAFE_PATH.fullmatch(str(path)):
        raise Refusal(f"{label} may use only letters, digits and ._+/- (it is written into a unit file)")
    try:
        path.relative_to(home())
    except ValueError:
        raise Refusal(f"{label} must be under {home()}: this installer writes user files only") from None
    return path


def version_of(directory_name: str, prefix: str) -> str | None:
    match = re.fullmatch(re.escape(prefix) + r"(\d+\.\d+\.\d+)", directory_name)
    return match.group(1) if match else None


def render(template: str, values: dict[str, str]) -> str:
    out = template
    for key, value in values.items():
        out = out.replace(f"@{key}@", value)
    leftover = re.findall(r"@[A-Z_]+@", out)
    if leftover:
        raise Refusal(f"unrendered placeholders: {sorted(set(leftover))}")
    return out


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class Plan:
    """Writes are collected, shown, and only then applied (or not, under --dry-run)."""

    def __init__(self, dry_run: bool) -> None:
        self.dry_run = dry_run
        self.items: list[tuple[pathlib.Path, bytes, int]] = []

    def add(self, path: pathlib.Path, data: bytes, mode: int, force: bool) -> None:
        if path.exists() or path.is_symlink():
            if path.is_symlink() or not path.is_file():
                raise Refusal(f"{path} exists and is not a regular file")
            if path.read_bytes() == data and stat.S_IMODE(path.stat().st_mode) == mode:
                print(f"unchanged  {path}")
                return
            if not force:
                raise Refusal(f"{path} exists and differs; re-run with --force to replace it")
        self.items.append((path, data, mode))

    def apply(self) -> None:
        for path, data, mode in self.items:
            verb = "would write" if self.dry_run else "write"
            print(f"{verb:<12}{path}  mode {mode:04o}  sha256 {digest(data)[:16]}")
            if self.dry_run:
                continue
            path.parent.mkdir(parents=True, exist_ok=True)
            fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=f".{path.name}.")
            try:
                with os.fdopen(fd, "wb") as handle:
                    handle.write(data)
                    handle.flush()
                    os.fsync(handle.fileno())
                os.chmod(tmp, mode)
                os.replace(tmp, path)
            except BaseException:
                try:
                    os.unlink(tmp)
                except OSError:
                    pass
                raise


def enable_file_names_a_rule(path: pathlib.Path) -> bool:
    try:
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode) & 0o077:
            return False
        rules = json.loads(path.read_text(encoding="utf-8")).get("apply")
        return isinstance(rules, list) and len(rules) > 0
    except (OSError, ValueError, AttributeError):
        return False


def command_units(args: argparse.Namespace) -> int:
    if os.geteuid() == 0:
        raise Refusal("run as the fdev user, not root")
    kit = user_path(args.kit_root, "--kit-root")
    engine = user_path(args.engine, "--engine")
    workdir = user_path(args.workdir, "--workdir")
    credentials = user_path(args.credentials, "--credentials")
    unit_dir = user_path(args.unit_dir, "--unit-dir")
    libexec = user_path(args.libexec_dir, "--libexec-dir")
    engine_release, kit_release = version_of(engine.parent.name, "coord-"), version_of(kit.name, "kit-")
    if engine.name != "fsgg-coord-engine" or engine_release is None:
        raise Refusal("--engine must be .../coord-<version>/fsgg-coord-engine, the versioned path, not the moving link")
    if kit_release != engine_release:
        raise Refusal(f"--kit-root must be named kit-{engine_release} to match the engine release")
    if not engine.is_file() or not os.access(engine, os.X_OK):
        raise Refusal("--engine is not an executable file")
    if not any((kit / mirror).is_file() for mirror in MIRRORS):
        raise Refusal("--kit-root carries no lifecycle-contract.md mirror; run the kit-root subcommand first")
    if workdir == kit:
        raise Refusal("--workdir must not be the kit root")
    if (workdir / ".git").exists() or (kit / ".git").exists():
        raise Refusal("--workdir and --kit-root must not be git checkouts")
    if args.mode == "apply" and not enable_file_names_a_rule(user_path(args.enable_file, "--enable-file")):
        raise Refusal("--mode apply needs a 0600 enable file that names at least one rule; the installer never writes rules")
    values = {"MODE": args.mode, "WORKDIR": str(workdir), "KIT_ROOT": str(kit), "ENGINE": str(engine), "CREDENTIALS": str(credentials),
              "LIBEXEC": str(libexec), "RUNTIME_MAX": str(RUNTIME_MAX_SECONDS)}
    plan = Plan(args.dry_run)
    for name in UNIT_TEMPLATES:
        text = render((HERE / f"{name}.in").read_text(encoding="utf-8"), values)
        plan.add(unit_dir / name, text.encode("utf-8"), 0o644, args.force)
    plan.add(libexec / WRAPPER, (HERE / WRAPPER).read_bytes(), 0o755, args.force)
    if not args.dry_run and not workdir.exists():
        workdir.mkdir(parents=True, mode=0o700)
        print(f"created    {workdir}  mode 0700")
    elif args.dry_run and not workdir.exists():
        print(f"would create {workdir}  mode 0700")
    plan.apply()
    commands = [["systemctl", "--user", "daemon-reload"], ["systemctl", "--user", "enable", "--now", *TIMERS]]
    if not args.enable:
        print("systemd not touched; to enable: " + " && ".join(" ".join(c) for c in commands))
        return 0
    for command in commands:
        print(("would run  " if args.dry_run else "run        ") + " ".join(command))
        if not args.dry_run:
            subprocess.run(command, check=True, timeout=60)
    return 0


def markers(text: str) -> list[str]:
    fenced, found = False, []
    for line in text.replace("\r\n", "\n").split("\n"):
        if FENCE_LINE.match(line):
            fenced = not fenced
        elif not fenced:
            match = MARKER_LINE.match(line)
            if match:
                found.append(match.group(1))
    return found


def command_kit_root(args: argparse.Namespace) -> int:
    if os.geteuid() == 0:
        raise Refusal("run as the fdev user, not root")
    dest = user_path(args.dest, "--dest")
    if version_of(dest.name, "kit-") is None:
        raise Refusal("--dest must be named kit-<version>")
    expected = args.expect_sha256.lower()
    if not re.fullmatch(r"[0-9a-f]{64}", expected):
        raise Refusal("--expect-sha256 must be 64 hex digits")
    plan = Plan(args.dry_run)
    for mirror in MIRRORS:
        done = subprocess.run(["git", "-C", args.git_dir, "show", f"{args.ref}:{mirror}"], capture_output=True, timeout=60, check=False)
        if done.returncode != 0:
            raise Refusal(f"git could not read {args.ref}:{mirror}")
        if digest(done.stdout) != expected:
            raise Refusal(f"{mirror} at {args.ref} has sha256 {digest(done.stdout)[:16]}..., not the expected release contract")
        if markers(done.stdout.decode("utf-8")) != ["v1"]:
            raise Refusal(f"{mirror} at {args.ref} must carry exactly one fsgg:board-tend-contract/v1 marker")
        plan.add(dest / mirror, done.stdout, 0o444, False)
    plan.apply()
    return 0


def command_enable_file(args: argparse.Namespace) -> int:
    if os.geteuid() == 0:
        raise Refusal("run as the fdev user, not root")
    dest = user_path(args.dest, "--dest")
    template = (HERE / ENABLE_TEMPLATE).read_bytes()
    if json.loads(template).get("apply") != []:
        raise Refusal("the shipped enable file must list no rules")
    if dest.exists() or dest.is_symlink():
        raise Refusal(f"{dest} exists; the installer never replaces an enable file (Root edits it to enable rules)")
    plan = Plan(args.dry_run)
    plan.add(dest, template, 0o600, False)
    if not args.dry_run:
        dest.parent.mkdir(parents=True, exist_ok=True)
        os.chmod(dest.parent, 0o700)
    plan.apply()
    return 0


def parser() -> argparse.ArgumentParser:
    h = home()
    root = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = root.add_subparsers(dest="command", required=True)
    units = sub.add_parser("units")
    units.add_argument("--kit-root", required=True)
    units.add_argument("--engine", required=True)
    units.add_argument("--mode", choices=("plan-only", "apply"), default="plan-only")
    units.add_argument("--workdir", default=str(h / ".local/share/fs-gg/board-tend"))
    units.add_argument("--credentials", default=str(h / ".config/fdev/credentials.json"))
    units.add_argument("--unit-dir", default=str(pathlib.Path(os.environ.get("XDG_CONFIG_HOME") or h / ".config") / "systemd/user"))
    units.add_argument("--libexec-dir", default=str(h / ".local/libexec"))
    units.add_argument("--enable-file", default=str(pathlib.Path(os.environ.get("XDG_CONFIG_HOME") or h / ".config") / "fsgg/board-tend.json"))
    units.add_argument("--enable", action="store_true", help="daemon-reload and enable --now both timers")
    units.add_argument("--force", action="store_true", help="replace installed files that differ")
    units.add_argument("--dry-run", action="store_true")
    kit = sub.add_parser("kit-root")
    kit.add_argument("--git-dir", required=True, help="a local clone of FS-GG/.github that holds the ref")
    kit.add_argument("--ref", required=True, help="the release tag or commit, for example coherent-set/v0.114.2")
    kit.add_argument("--expect-sha256", required=True, help="sha256 of the released lifecycle-contract.md")
    kit.add_argument("--dest", required=True, help=".../kit-<version>")
    kit.add_argument("--dry-run", action="store_true")
    enable = sub.add_parser("enable-file")
    enable.add_argument("--dest", default=str(pathlib.Path(os.environ.get("XDG_CONFIG_HOME") or h / ".config") / "fsgg/board-tend.json"))
    enable.add_argument("--dry-run", action="store_true")
    return root


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        return {"units": command_units, "kit-root": command_kit_root, "enable-file": command_enable_file}[args.command](args)
    except Refusal as error:
        print(f"install.py: refused: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
