#!/usr/bin/python3 -I
"""fdev wrapper for `fsgg-coord-engine board-v2 tend` (board-reconciler design section 8, #5136 PR 8).

Two subcommands, stdlib only:

  run        One reconciler run (or several, see the marker rule). Plan-only unless `--mode apply` is given.
  summarize  The daily observation record: reads the private run index, writes <out>/daily/<date>.{json,md}.
             It never posts anything.

`run` is what `fsgg-board-tend.service` executes. It exists because the engine alone cannot:
  * pin its environment: FSGG_KIT_ROOT must name the installed kit (the engine reads the contract marker under
    it, #5188 F1) and the working directory must be the fixed, non-checkout WorkingDirectory;
  * keep every output in a private directory (0700, one directory per run);
  * honour the post-merge marker: a `pending-post-merge` file in the reconciler's state directory means a merge
    happened since the last run, so the run goes ahead regardless of the 5-minute timer minimum, and the marker
    is cleared only by a run that started after it was written (a run that finds the marker newer than its own
    start runs once more, up to --max-passes).

The Project 3 writer lock is the engine's own flock(2) on $XDG_RUNTIME_DIR/fsgg/board-writer-<project>.lock,
taken with LOCK_NB and only under --apply; this wrapper never takes it. It only checks the path is satisfiable.
"""
from __future__ import annotations

import argparse
import datetime as dt
import fcntl
import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import time
from typing import Any

RUN_SCHEMA = "fsgg.board-tend-run/1"
DAILY_SCHEMA = "fsgg.board-tend-daily/1"
MARKER = "pending-post-merge"
PROJECT_ID = "PVT_kwDOEYAWY84Bldpa"
WRITER_LOCK_NAME = f"board-writer-{PROJECT_ID}.lock"
CONTRACT_MIRRORS = (
    ".claude/skills/work-board/references/lifecycle-contract.md",
    ".agents/skills/work-board/references/lifecycle-contract.md",
)
# What the child engine inherits: nothing else. In particular no GITHUB_TOKEN (the credential file's GH_TOKEN is the
# only token), no TMPDIR (the legacy lock is at the literal /tmp path) and no inherited FSGG_* beyond the pinned root.
CHILD_ENV_KEEP = ("HOME", "PATH", "LANG", "LC_ALL", "DOTNET_ROOT", "XDG_RUNTIME_DIR", "XDG_CONFIG_HOME", "XDG_STATE_HOME",
                  "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "DOTNET_CLI_HOME")
RUN_DIR = re.compile(r"\d{8}T\d{6}Z-\d{2}")
DEFAULT_MIN_INTERVAL = 300
DEFAULT_MAX_PASSES = 3
DEFAULT_KEEP = 600
DEFAULT_ENGINE_TIMEOUT = 600


class Refusal(Exception):
    """A precondition failed; nothing was run."""


def now_utc() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def stamp(moment: dt.datetime) -> str:
    return moment.strftime("%Y%m%dT%H%M%SZ")


def iso(moment: dt.datetime) -> str:
    return moment.strftime("%Y-%m-%dT%H:%M:%SZ")


def private_directory(path: pathlib.Path) -> pathlib.Path:
    """Create (0700) or verify a directory that is ours alone."""
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    info = os.lstat(path)
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid():
        raise Refusal(f"{path} is not a directory owned by this user")
    if stat.S_IMODE(info.st_mode) & 0o077:
        raise Refusal(f"{path} must be private to its owner (mode 0700); it is {stat.S_IMODE(info.st_mode):o}")
    return path


def atomic_write(path: pathlib.Path, text: str) -> None:
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=f".{path.name}.")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def read_token(credentials: pathlib.Path) -> str:
    """The GH_TOKEN of the 0600 credentials file (the same file the board-drift dispatcher reads)."""
    try:
        info = os.lstat(credentials)
    except OSError as error:
        raise Refusal(f"credentials file unreadable: {type(error).__name__}") from None
    if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) & 0o077:
        raise Refusal("credentials file must be a regular file owned by this user with mode 0600")
    try:
        token = json.loads(credentials.read_text(encoding="utf-8")).get("GH_TOKEN")
    except (OSError, ValueError, AttributeError):
        raise Refusal("credentials file is not a JSON object") from None
    if not isinstance(token, str) or not token.strip():
        raise Refusal("credentials file has no GH_TOKEN")
    return token.strip()


def check_pin(env: dict[str, str], workdir: pathlib.Path | None, cwd: pathlib.Path) -> pathlib.Path:
    """The environment pin: FSGG_KIT_ROOT absolute, existing and carrying a contract mirror; cwd the fixed path."""
    root = env.get("FSGG_KIT_ROOT", "")
    if not root or not os.path.isabs(root):
        raise Refusal("FSGG_KIT_ROOT must be set to the absolute installed kit root")
    kit = pathlib.Path(root)
    if not kit.is_dir():
        raise Refusal("FSGG_KIT_ROOT is not a directory")
    if not any((kit / mirror).is_file() for mirror in CONTRACT_MIRRORS):
        raise Refusal("FSGG_KIT_ROOT carries no lifecycle-contract.md mirror; it is not an installed kit root")
    if workdir is not None and cwd.resolve() != workdir.resolve():
        raise Refusal("the working directory differs from the pinned WorkingDirectory")
    runtime = env.get("XDG_RUNTIME_DIR", "")
    if not runtime or not os.path.isabs(runtime):
        raise Refusal("XDG_RUNTIME_DIR must be absolute: the shared writer lock lives under it")
    return kit


def marker_identity(state: pathlib.Path) -> tuple[int, int, int] | None:
    try:
        info = os.lstat(state / MARKER)
    except FileNotFoundError:
        return None
    return (info.st_ino, info.st_mtime_ns, info.st_size)


def child_environment(env: dict[str, str], token: str, kit: pathlib.Path) -> dict[str, str]:
    child = {key: env[key] for key in CHILD_ENV_KEEP if key in env}
    child["FSGG_KIT_ROOT"] = str(kit)
    child["GH_TOKEN"] = token
    return child


def engine_version(engine: str, child: dict[str, str]) -> str:
    try:
        done = subprocess.run([engine, "--version"], env=child, capture_output=True, text=True, timeout=60, check=False)
        return done.stdout.strip().splitlines()[0] if done.returncode == 0 and done.stdout.strip() else "unknown"
    except (OSError, subprocess.TimeoutExpired):
        return "unknown"


def run_once(engine: str, run_dir: pathlib.Path, mode: str, child: dict[str, str], timeout: int) -> tuple[int, str]:
    """One engine invocation; plan-only never passes --apply. Returns (exit, the engine's summary line)."""
    command = [engine, "board-v2", "tend", "--plan-file", str(run_dir / "plan.json")]
    if mode == "apply":
        command.append("--apply")
    out, err = run_dir / "engine.out", run_dir / "engine.err"
    def private_file(path: pathlib.Path):
        return os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600), "w", encoding="utf-8")

    with private_file(out) as stdout, private_file(err) as stderr:
        try:
            code = subprocess.run(command, env=child, stdout=stdout, stderr=stderr, timeout=timeout, check=False, umask=0o077).returncode
        except subprocess.TimeoutExpired:
            code = 124
        except OSError:
            code = 127
    lines = [line for line in out.read_text(encoding="utf-8").splitlines() if line.startswith("board-v2 tend:")]
    return code, (lines[-1] if lines else "")


def new_run_directory(runs: pathlib.Path, started: dt.datetime, first: int) -> pathlib.Path:
    """A run directory that did not exist before: two runs in one second never share one."""
    for suffix in range(first, 100):
        path = runs / f"{stamp(started)}-{suffix:02d}"
        try:
            path.mkdir(mode=0o700)
            return path
        except FileExistsError:
            continue
    raise Refusal("no free run directory name in this second")


def prune(runs: pathlib.Path, keep: int) -> int:
    names = sorted(name.name for name in runs.iterdir() if name.is_dir() and RUN_DIR.fullmatch(name.name))
    doomed = names[:-keep] if keep > 0 and len(names) > keep else []
    for name in doomed:
        shutil.rmtree(runs / name, ignore_errors=True)
    return len(doomed)


def command_run(args: argparse.Namespace, env: dict[str, str] | None = None, cwd: pathlib.Path | None = None) -> int:
    env = dict(os.environ) if env is None else env
    cwd = pathlib.Path.cwd() if cwd is None else cwd
    state_home = pathlib.Path(env.get("XDG_STATE_HOME") or os.path.join(env.get("HOME", "~"), ".local", "state"))
    state = pathlib.Path(args.state_dir) if args.state_dir else state_home / "fsgg" / "board-tend"
    out = pathlib.Path(args.out_dir) if args.out_dir else state_home / "fsgg" / "board-tend-out"
    try:
        kit = check_pin(env, pathlib.Path(args.workdir) if args.workdir else None, cwd)
        token = read_token(pathlib.Path(args.credentials))
        private_directory(out)
        runs = private_directory(out / "runs")
        private_directory(state)
    except Refusal as error:
        print(f"fsgg-board-tend-run: refused: {error}", file=sys.stderr)
        return 2
    child = child_environment(env, token, kit)
    lock = os.open(out / "run.lock", os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        os.close(lock)
        print("fsgg-board-tend-run: another run holds the run lock; the marker, if any, is kept", file=sys.stderr)
        return 0
    try:
        last = out / "last-run.json"
        passes, code = 0, 0
        while True:
            seen = marker_identity(state)
            if passes == 0 and seen is None:
                try:
                    started_epoch = json.loads(last.read_text(encoding="utf-8")).get("startedEpoch", 0)
                except (OSError, ValueError, AttributeError):
                    started_epoch = 0
                if isinstance(started_epoch, (int, float)) and time.time() - started_epoch < args.min_interval:
                    skipped = {"event": "skipped", "at": iso(now_utc()), "reason": "min-interval"}
                    with open(out / "index.jsonl", "a", encoding="utf-8") as index:
                        index.write(json.dumps(skipped, sort_keys=True) + "\n")
                    print("fsgg-board-tend-run: skipped: the last run started inside the minimum interval and no merge is pending")
                    return 0
            started = now_utc()
            run_dir = new_run_directory(runs, started, passes)
            trigger = "post-merge" if seen is not None else "timer"
            code, summary = run_once(args.engine, run_dir, args.mode, child, args.engine_timeout)
            after = marker_identity(state)
            if after is None:
                disposition = "none"
            elif seen is not None and after == seen and code == 0:
                try:
                    os.unlink(state / MARKER)
                    disposition = "cleared"
                except FileNotFoundError:
                    disposition = "cleared"
            elif after != seen:
                disposition = "newer"
            else:
                disposition = "kept-failed"
            record: dict[str, Any] = {
                "schema": RUN_SCHEMA, "event": "run", "mode": args.mode, "trigger": trigger,
                "startedAt": iso(started), "startedEpoch": started.timestamp(), "finishedAt": iso(now_utc()),
                "exit": code, "runDir": str(run_dir), "plan": str(run_dir / "plan.json"),
                "summary": summary, "marker": disposition, "pass": passes,
                "kitRoot": str(kit), "engine": args.engine, "engineVersion": engine_version(args.engine, child),
                "writerLock": str(pathlib.Path(env["XDG_RUNTIME_DIR"]) / "fsgg" / WRITER_LOCK_NAME),
            }
            atomic_write(run_dir / "meta.json", json.dumps(record, indent=2, sort_keys=True) + "\n")
            atomic_write(last, json.dumps(record, indent=2, sort_keys=True) + "\n")
            with open(out / "index.jsonl", "a", encoding="utf-8") as index:
                index.write(json.dumps({k: record[k] for k in ("event", "mode", "trigger", "startedAt", "finishedAt", "exit", "runDir", "summary", "marker", "pass")}, sort_keys=True) + "\n")
                index.flush()
                os.fsync(index.fileno())
            print(f"fsgg-board-tend-run: {args.mode} {trigger} pass={passes} exit={code} marker={disposition} run={run_dir}")
            if summary:
                print(summary)
            passes += 1
            if disposition != "newer" or passes >= args.max_passes:
                break
        prune(runs, args.keep)
        return code
    finally:
        os.close(lock)  # closing the descriptor releases the flock


# ---- the daily observation record ----

def rate(pairs: int, disagreements: int) -> float | None:
    return round(disagreements / pairs, 4) if pairs else None


def summarize(out: pathlib.Path, date: str) -> dict[str, Any]:
    entries: list[dict[str, Any]] = []
    try:
        for line in (out / "index.jsonl").read_text(encoding="utf-8").splitlines():
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if isinstance(entry, dict) and str(entry.get("startedAt", entry.get("at", ""))).startswith(date):
                entries.append(entry)
    except FileNotFoundError:
        pass
    runs = [e for e in entries if e.get("event") == "run"]
    plans, unreadable = [], 0
    for entry in runs:
        try:
            plan = json.loads((pathlib.Path(entry["runDir"]) / "plan.json").read_text(encoding="utf-8"))
            if plan.get("schema") != "fsgg.board-tend-plan/1":
                raise ValueError
            plans.append((entry, plan))
        except (OSError, ValueError, KeyError, AttributeError):
            unreadable += 1
    n = len(plans)

    def stable(counter: dict[str, int]) -> list[str]:
        return sorted(key for key, seen in counter.items() if seen == n and n > 0)

    would_apply: dict[str, dict[str, Any]] = {}
    would_close: dict[str, dict[str, Any]] = {}
    seen_apply: dict[str, int] = {}
    seen_close: dict[str, int] = {}
    propose: dict[str, int] = {}
    refuse: dict[str, dict[str, Any]] = {}
    for _, plan in plans:
        for effect in plan.get("wouldApply", []):
            key = effect["id"]
            would_apply[key] = {k: effect.get(k) for k in ("id", "ref", "field", "preimage", "target", "rules")}
            seen_apply[key] = seen_apply.get(key, 0) + 1
        for closure in plan.get("wouldClose", []):
            key = closure["id"]
            would_close[key] = {k: closure.get(k) for k in ("id", "ref", "mergedPrs")}
            seen_close[key] = seen_close.get(key, 0) + 1
        for item in plan.get("propose", []):
            propose[item["id"]] = propose.get(item["id"], 0) + 1
        for item in plan.get("refuse", []):
            refuse[item["id"]] = {k: item.get(k) for k in ("id", "ref", "rule", "reason")}
    by_rule: dict[str, list[str]] = {}
    for key, effect in would_apply.items():
        for rule in effect.get("rules") or ["?"]:
            by_rule.setdefault(rule, []).append(key)
    t1i = [(p["t1iComparison"]["pairs"], p["t1iComparison"]["disagreements"]) for _, p in plans if isinstance(p.get("t1iComparison"), dict)]
    rates = [r for r in (rate(a, b) for a, b in t1i) if r is not None]
    exits: dict[str, int] = {}
    for entry in runs:
        exits[str(entry.get("exit"))] = exits.get(str(entry.get("exit")), 0) + 1
    last_plan = plans[-1][1] if plans else {}
    return {
        "schema": DAILY_SCHEMA, "date": date,
        "runs": len(runs), "plansRead": n, "plansUnreadable": unreadable,
        "skipped": sum(1 for e in entries if e.get("event") == "skipped"),
        "exitCodes": dict(sorted(exits.items())),
        "applyModeRuns": sum(1 for e in runs if e.get("mode") == "apply"),
        "postMergeRuns": sum(1 for e in runs if e.get("trigger") == "post-merge"),
        "plansWithApply": sum(1 for _, p in plans if p.get("apply") or p.get("close")),
        "lastRun": {"startedAt": runs[-1].get("startedAt"), "summary": runs[-1].get("summary")} if runs else None,
        "wouldApplyByRule": {rule: {"distinct": len(ids), "stableAllDay": len([i for i in ids if seen_apply[i] == n]), "ids": sorted(ids)}
                             for rule, ids in sorted(by_rule.items())},
        "wouldClose": {"distinct": len(would_close), "stableAllDay": stable(seen_close), "items": sorted(would_close.values(), key=lambda c: c["id"]),
                       "seenRuns": {k: v for k, v in sorted(seen_close.items())}},
        "propose": {"distinct": len(propose)},
        "refuse": {"distinct": len(refuse), "items": sorted(refuse.values(), key=lambda r: r["id"])},
        "t1i": {"lastPairs": t1i[-1][0] if t1i else None, "lastDisagreements": t1i[-1][1] if t1i else None,
                "lastRate": rate(*t1i[-1]) if t1i else None, "minRate": min(rates) if rates else None, "maxRate": max(rates) if rates else None},
        "lastPlanObservedAt": last_plan.get("observedAt"),
    }


def public_ref(ref: str) -> str | None:
    """Only FS-GG/.github references are written into a comment body; any other repository may be private."""
    return ref if re.fullmatch(r"FS-GG/\.github#\d+", ref) else None


def render_comment(daily: dict[str, Any]) -> str:
    def refs(items: list[str]) -> str:
        shown = [r for r in (public_ref(i) for i in items) if r]
        hidden = len(items) - len(shown)
        text = ", ".join(shown) if shown else "none"
        return text + (f" (+{hidden} in other repositories, in the private record)" if hidden else "")

    lines = [f"<!-- fsgg:board-tend-observation/v1 date={daily['date']} -->",
             f"**board-v2 tend, plan-only observation, {daily['date']} (UTC).** Counts are the reconciler's own output.", "",
             f"- Runs: {daily['runs']} ({daily['postMergeRuns']} post-merge), plans read {daily['plansRead']}, unreadable {daily['plansUnreadable']}, skipped {daily['skipped']}, exit codes {daily['exitCodes'] or 'none'}.",
             f"- Apply-mode runs: {daily['applyModeRuns']}; plans that applied or closed anything: {daily['plansWithApply']} (both must be 0 during the observation).",
             f"- Last run: {daily['lastRun']['summary'] if daily['lastRun'] else 'none'}", "",
             "Would-apply, by rule (distinct effects; stable = present in every run of the day):", ""]
    if daily["wouldApplyByRule"]:
        for rule, info in daily["wouldApplyByRule"].items():
            lines.append(f"- {rule}: {info['distinct']} distinct, {info['stableAllDay']} stable")
    else:
        lines.append("- none")
    closes = daily["wouldClose"]
    lines += ["", f"Would-close (C1): {closes['distinct']} distinct, {len(closes['stableAllDay'])} stable all day.",
              f"- {refs([c['ref'] for c in closes['items']])}", ""]
    t1i = daily["t1i"]
    lines += [f"T1i disagreement: last {t1i['lastDisagreements']}/{t1i['lastPairs']} (rate {t1i['lastRate']}); day range {t1i['minRate']} to {t1i['maxRate']}.",
              f"Propose: {daily['propose']['distinct']} distinct. Refuse: {daily['refuse']['distinct']} distinct "
              f"({'; '.join(sorted({str(r['rule']) + ' ' + str(r['reason']) for r in daily['refuse']['items']})) or 'none'}): "
              f"{refs([r['ref'] for r in daily['refuse']['items']])}.", ""]
    return "\n".join(lines)


def command_summarize(args: argparse.Namespace, env: dict[str, str] | None = None) -> int:
    env = dict(os.environ) if env is None else env
    state_home = pathlib.Path(env.get("XDG_STATE_HOME") or os.path.join(env.get("HOME", "~"), ".local", "state"))
    out = pathlib.Path(args.out_dir) if args.out_dir else state_home / "fsgg" / "board-tend-out"
    today = now_utc().date()
    date = {"today": today.isoformat(), "yesterday": (today - dt.timedelta(days=1)).isoformat()}.get(args.date, args.date)
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", date):
        print("fsgg-board-tend-run: --date must be YYYY-MM-DD, today or yesterday", file=sys.stderr)
        return 2
    try:
        private_directory(out)
        daily_dir = private_directory(out / "daily")
    except Refusal as error:
        print(f"fsgg-board-tend-run: refused: {error}", file=sys.stderr)
        return 2
    daily = summarize(out, date)
    atomic_write(daily_dir / f"{date}.json", json.dumps(daily, indent=2, sort_keys=True) + "\n")
    atomic_write(daily_dir / f"{date}.md", render_comment(daily))
    print(f"fsgg-board-tend-run: daily {date}: runs={daily['runs']} wouldClose={daily['wouldClose']['distinct']} record={daily_dir / (date + '.json')}")
    return 0


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = root.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run")
    run.add_argument("--mode", choices=("plan-only", "apply"), default="plan-only")
    run.add_argument("--engine-path", dest="engine", required=True, help="absolute path of fsgg-coord-engine")
    run.add_argument("--credentials", required=True, help="0600 JSON file with GH_TOKEN")
    run.add_argument("--workdir", help="the fixed WorkingDirectory the process must have been started in")
    run.add_argument("--out-dir", help="private output directory (default $XDG_STATE_HOME/fsgg/board-tend-out)")
    run.add_argument("--state-dir", help="reconciler state directory holding the marker (default $XDG_STATE_HOME/fsgg/board-tend)")
    run.add_argument("--min-interval", type=int, default=DEFAULT_MIN_INTERVAL, help="seconds; applies to timer runs only")
    run.add_argument("--max-passes", type=int, default=DEFAULT_MAX_PASSES)
    run.add_argument("--keep", type=int, default=DEFAULT_KEEP, help="run directories to keep")
    run.add_argument("--timeout-seconds", dest="engine_timeout", type=int, default=DEFAULT_ENGINE_TIMEOUT)
    daily = sub.add_parser("summarize")
    daily.add_argument("--date", default="yesterday", help="YYYY-MM-DD (UTC), today or yesterday")
    daily.add_argument("--out-dir")
    return root


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    os.umask(0o077)
    try:
        return command_run(args) if args.command == "run" else command_summarize(args)
    except Refusal as error:
        print(f"fsgg-board-tend-run: refused: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
