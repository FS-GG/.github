#!/usr/bin/env python3
"""Mutation controls for the board-currency merge gate in tools/routine-delivery.py (Refs #5094).

Each mutant is one deliberate defect in the gate, its post-merge Status transition, the shared writer lock or
the deferred-transition replay (#5136 PR 1). BoardGateTests and BoardWriterLockReplayTests are re-run against a
mutated copy of the helper (ROUTINE_DELIVERY_UNDER_TEST); a mutant that leaves every
test green means no test pins that behaviour, and this script exits 1 naming it. The unmutated helper
must pass first.
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SOURCE = ROOT / "tools/routine-delivery.py"

# (name, exact original text, replacement). Every original must occur exactly once.
MUTANTS = [
    ("delivery marker: required mode never refuses",
     'if delivery_marker == "required" and board["deliveryMarker"]["declared"] in ("none", "both"):',
     'if False:'),
    ("delivery marker: both counted as one declaration",
     '{(True, False): "continues", (False, True): "completes", (True, True): "both"}.get(found, "none")',
     '{(True, False): "continues", (False, True): "completes", (True, True): "completes"}.get(found, "none")'),
    ("delivery marker: quoted markers count",
     '        visible = rules.visible_text(text) if rules is not None else text\n',
     '        visible = text\n'),
    ("delivery marker: release step not warned",
     '        if done is not None and _DONE_WHEN_STEP.search(done):\n',
     '        if False:\n'),
    ("delivery marker: advisory flag wrong",
     'return {"declared": declared, "advisory": mode == "advisory", "warning": warning}',
     'return {"declared": declared, "advisory": True, "warning": warning}'),
    ("custody write failure ignored", '    except Exception:\n        return "refused-custody"\n',
     '    except Exception:\n        pass\n'),
    ("existing custody record overwritten (resend)",
     "        if custody.existing(repo, pr) is not None:\n            return \"uncertain\"\n",
     "        if False:\n            return \"uncertain\"\n"),
    ("merged-PR crash recovery skipped",
     '            recovered = board_custody_readback(api, board_custody, repo, pr_number)\n',
     '            recovered = None\n'),
    ("verified rewrite dropped after readback",
     '        custody.write(repo, pr, dict(record, state="verified", verifiedAt=datetime.now(timezone.utc).isoformat()))\n    except Exception:\n        pass  # the possible-send',
     '        pass\n    except Exception:\n        pass  # the possible-send'),
    ("recovery trusts a mismatched readback",
     '    if not (_board_item_valid(after) and after.get("status") == record.get("to")):\n        return "uncertain"\n', ""),
    ("origin back to a suffix match", '    return path.lower() == BOARD_ENFORCE_REPOSITORY',
     '    return text.lower().removesuffix("/").removesuffix(".git").endswith(BOARD_ENFORCE_REPOSITORY)'),
    ("origin host not checked", '(parts.hostname or "").lower() != "github.com"', 'False'),
    ("summarize does not check the enforce copy",
     '    if board_gate == "enforce" and not board_enforce_allowed():\n        raise RuntimeError(', '    if False:\n        raise RuntimeError('),
    ("custody leaf not tightened", '        if stat.S_IMODE(info.st_mode) != 0o700:\n            os.chmod(self.root, 0o700)\n', ""),
    ("foreign custody record trusted", '                or record.get("repo", "").lower() != repo.lower() or record.get("pr") != pr\n', ""),
    ("enforce allowed from any copy", '    return proc.returncode == 0 and board_origin_is_github(proc.stdout)', '    return True'),
    ("PR-typed card is a Delivery", '    if node.get("__typename") == "PullRequest":\n        return None\n', ""),
    ("distributed default flipped to enforce",
     'result.add_argument("--board-gate", choices=BOARD_GATE_MODES, default="advisory",',
     'result.add_argument("--board-gate", choices=BOARD_GATE_MODES, default="enforce",'),
    ("enforce refusal still merges", 'if board_gate == "enforce" and gate.verdict == "refused":', "if False:"),
    ("enforce unknown merges (fail-open)",
     'if board_gate == "enforce" and gate.verdict in ("unknown", "unavailable"):',
     'if board_gate == "enforce" and gate.verdict in ("unavailable",):'),
    ("override evaluated after the board read",
     "        if rules.override_of(pr, roots) is not None:\n            return BoardVerdict(\"overridden\", \"valid-override\")\n",
     "        if False:\n            pass\n"),
    ("Unknown linkage treated as refusal", 'return BoardVerdict("unknown", f"U {unknown[0]}")',
     'return BoardVerdict("refused", f"R1 {unknown[0]}")'),
    ("moved body between reads trusted", "if (not isinstance(pr, dict) or pr.get(\"body\") != rest_body or pr.get(\"state\") != \"OPEN\"",
     "if (not isinstance(pr, dict) or pr.get(\"state\") != \"OPEN\""),
    ("Blocked preimage is writable", 'BOARD_WRITABLE_PREIMAGES = ("Backlog", "Ready", "In progress", "In review")',
     'BOARD_WRITABLE_PREIMAGES = ("Backlog", "Ready", "In progress", "In review", "Blocked")'),
    ("equal preimage still sends", '    if before["status"] == target:\n        return "noop"\n', ""),
    ("closed Delivery is written", '    if before["state"] == "CLOSED":\n        return "not-applicable"\n', ""),
    ("closing keyword ignored", "        if delivery.lower() in board_closing_refs(rules, repo, body):\n            return \"not-applicable\"\n", ""),
    ("advisory writes", '    if mode != "enforce":\n        return f"would-write:{target}"\n', ""),
    ("continues marker ignored", 'target = "In progress" if BOARD_CONTINUES in rules.visible_text(body) else "In review"',
     'target = "In review"'),
    ("continues marker counted in code spans", 'BOARD_CONTINUES in rules.visible_text(body)', "BOARD_CONTINUES in str(body)"),
    ("adjacent preimage not compared", "    if adjacent != before:\n        return \"refused-preimage\"\n", ""),
    ("readback not verified", '    if after != dict(before, status=target):\n        return "uncertain"\n', ""),
    ("failed send is retried",
     "    except Exception:\n        return \"uncertain\"\n    try:\n        after = api.board_item(delivery)",
     "    except Exception:\n        api.board_set_status(before[\"projectId\"], before[\"itemId\"], before[\"statusFieldId\"], options[target])\n"
     "        return \"uncertain\"\n    try:\n        after = api.board_item(delivery)"),
    ("transition runs for overridden PRs", 'if gate is not None and gate.verdict == "linked" and gate.delivery is not None:',
     'if gate is not None and gate.verdict in ("linked", "overridden"):'),
    ("possible-send recorded after the send",
     '    print("fsgg routine board transition: possible-send recorded; one Status send follows", file=sys.stderr)\n    try:\n        api.board_set_status(before["projectId"], before["itemId"], before["statusFieldId"], options[target])\n',
     '    try:\n        api.board_set_status(before["projectId"], before["itemId"], before["statusFieldId"], options[target])\n        print("fsgg routine board transition: possible-send recorded; one Status send follows", file=sys.stderr)\n'),
    ("pre-gate prior results rejected",
     "    shapes = [current - board - marker for board in (frozenset(), BOARD_RESULT_FIELDS) for marker in (frozenset(), MARKER_RESULT_FIELDS)]",
     "    shapes = [current - marker for marker in (frozenset(), MARKER_RESULT_FIELDS)]"),
    ("pre-minor-1 prior results rejected",
     "    shapes = [current - board - marker for board in (frozenset(), BOARD_RESULT_FIELDS) for marker in (frozenset(), MARKER_RESULT_FIELDS)]",
     "    shapes = [current - board for board in (frozenset(), BOARD_RESULT_FIELDS)]"),
    ("truncated nested value trusted", '            raise RuntimeError("board item value is truncated")', "            pass"),
    ("live option identity not checked", 'if not isinstance(status, str) or by_name.get(status) != status_nodes[0].get("optionId"):',
     "if not isinstance(status, str):"),
    ("exiting rules module ends the helper", "    except (Exception, SystemExit):\n        # A broken", "    except Exception:\n        # A broken"),
    # ---- the shared writer lock, deferral, sweep and replay (board-reconciler design §6/§8, #5136 PR 1) ----
    ("send without the writer lock", '    if not locked:\n        try:\n            created = custody.create_exclusive(',
     '    if False:\n        try:\n            created = custody.create_exclusive('),
    ("lock error treated as held", '    try:\n        locked = lock.acquire()\n    except Exception:\n        return "refused-lock"\n',
     '    try:\n        locked = lock.acquire()\n    except Exception:\n        locked = True\n'),
    ("legacy engine locks not taken",
     '    return [directory / BOARD_WRITER_LOCK_NAME, legacy / BOARD_LEGACY_LOCK_NAME, legacy / BOARD_LEGACY_OBSERVATION_LOCK_NAME]',
     '    return [directory / BOARD_WRITER_LOCK_NAME, legacy / BOARD_LEGACY_LOCK_NAME]'),
    ("relative XDG_RUNTIME_DIR accepted", "        if not runtime or not os.path.isabs(runtime):", "        if not runtime:"),
    ("symlinked lock file followed", "os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC", "os.O_RDWR | os.O_CREAT | os.O_CLOEXEC"),
    ("configuration error looks busy", "        except BaseException:\n            for fd in taken:\n                os.close(fd)\n            raise",
     "        except OSError:\n            for fd in taken:\n                os.close(fd)\n            return False"),
    ("replay lock error counted as busy", '    except Exception:\n        counts["lock-error"] += 1\n        return counts\n',
     '    except Exception:\n        counts["lock-busy"] += 1\n        return counts\n'),
    ("lock does not use LOCK_NB", "fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)", "fcntl.flock(fd, fcntl.LOCK_SH | fcntl.LOCK_NB)"),
    ("bounded wait gives up at once", "            if self.clock() >= deadline:\n                return False\n",
     "            return False\n"),
    ("production lock without XDG_RUNTIME_DIR", '        if not runtime or not os.path.isabs(runtime):\n            raise RuntimeError(', '        if False:\n            raise RuntimeError('),
    ("deferred record overwrites an existing one", "            except FileExistsError:\n                return False\n",
     "            except FileExistsError:\n                os.replace(tmp, path)\n                return True\n"),
    ("deferred record lacks mergedAt", '"state": "deferred", "mergedAt": merged_at,', '"state": "deferred",'),
    ("validator rejects deferred states",
     'BOARD_CUSTODY_STATES = ("possible-send", "verified", "deferred", "superseded", "refused", "not-applicable")',
     'BOARD_CUSTODY_STATES = ("possible-send", "verified")'),
    ("start-of-run replay skipped", "        replay = +board_replay(api, board_custody, board_lock, (repo, pr_number))  # drop zero counts\n",
     "        replay = Counter()\n"),
    ("replay runs without the lock", '    if not locked:\n        counts["lock-busy"] += 1\n        return counts\n', ""),
    ("sweep resends", '            outcome = board_custody_readback(api, custody, record["repo"], record["pr"])\n',
     '            api.board_set_status("PVT_project", record["itemId"], "PVTSSF_status", "opt3")\n'
     '            outcome = board_custody_readback(api, custody, record["repo"], record["pr"])\n'),
    ("older deferred not superseded", '                if older["state"] == "deferred":\n                    custody.write(',
     '                if False:\n                    custody.write('),
    ("supersession ignores records past deferred", '            if group[-1]["state"] == "deferred":\n                pending.append(group[-1])\n',
     '            deferred = [r for r in group if r["state"] == "deferred"]\n            if deferred:\n                pending.append(deferred[-1])\n'),
    ("replay not in merge order", "        for record in sorted(pending, key=_board_order):", "        for record in reversed(pending):"),
    ("missing mergedAt ordered newest", "    return (1, merged) if isinstance(merged, str) else (0, \"\")",
     "    return (1, merged) if isinstance(merged, str) else (2, \"\")"),
    ("replay ignores the recorded preimage", ' or item["status"] != record.get("from")\n', "\n"),
    ("closed Delivery replayed", '    if item["state"] == "CLOSED":\n        custody.write(repo, pr, dict(record, state="not-applicable"))\n        return "not-applicable"\n', ""),
    ("possible-send not written before the replay send",
     '    custody.write(repo, pr, dict(record, state="possible-send"))\n    print("fsgg routine board replay', '    print("fsgg routine board replay'),
    ("replay readback not verified", '    if after != dict(item, status=target):\n        return "uncertain"\n    try:\n        custody.write(repo, pr, dict(record, state="verified"',
     '    try:\n        custody.write(repo, pr, dict(record, state="verified"'),
    ("sweep reads the current PR's own record", "                    current is not None and (record[\"repo\"].lower(), record[\"pr\"]) == (current[0].lower(), current[1])):",
     "                    False):"),
    # ---- the post-merge board-tend trigger (board-reconciler design section 8, #5136 PR 8) ----
    ("tend start blocks on the unit", '"start", "--no-block", BOARD_TEND_UNIT', '"start", BOARD_TEND_UNIT'),
    ("tend start has no timeout", "                   timeout=BOARD_TEND_START_TIMEOUT_SECONDS, check=False)", "                   check=False)"),
    ("tend start failure reported as started", '    return "start-failed" if outcome == "started" else "marker-failed+start-failed"', "    return outcome"),
    ("tend marker state directory not private", "root.mkdir(mode=0o700, parents=True, exist_ok=True)\n        fd, tmp = tempfile.mkstemp(dir=root, prefix=f\".{BOARD_TEND_MARKER}.\")",
     "root.mkdir(mode=0o755, parents=True, exist_ok=True)\n        fd, tmp = tempfile.mkstemp(dir=root, prefix=f\".{BOARD_TEND_MARKER}.\")"),
    ("tend marker rewritten in place", "            os.replace(tmp, root / BOARD_TEND_MARKER)  # a new inode each time: the unit tells markers apart by identity",
     "            (root / BOARD_TEND_MARKER).write_text('x', encoding='utf-8')"),
    ("tend trigger error escapes the merge", '            except Exception:\n                print("fsgg routine board tend trigger: start-failed", file=sys.stderr)',
     '            except KeyboardInterrupt:\n                print("fsgg routine board tend trigger: start-failed", file=sys.stderr)'),
    ("tend trigger wired in every mode", 'board_tend_trigger=(board_tend_post_merge if args.board_gate == "enforce" else None)', "board_tend_trigger=board_tend_post_merge"),
    ("tend trigger skipped after a merge", "        if board_tend_trigger is not None:\n            try:\n                print(", "        if False:\n            try:\n                print("),
]


def run(source: Path) -> bool:
    env = dict(os.environ, ROUTINE_DELIVERY_UNDER_TEST=str(source), PYTHONDONTWRITEBYTECODE="1")
    done = subprocess.run([sys.executable, str(HERE / "routine-delivery.py"), "BoardGateTests", "BoardWriterLockReplayTests", "BoardTendTriggerTests"],
                          env=env, capture_output=True, text=True, timeout=300, check=False)
    return done.returncode == 0


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory(prefix="routine-delivery-board-mutate-") as scratch:
        # A link farm of the checkout with a real tools/routine-delivery.py: its sibling loaders
        # (pr-lane-admission, the evidence reader, scripts/board-currency-rules.py) resolve unchanged.
        mirror = Path(scratch)
        for entry in ROOT.iterdir():
            if entry.name != "tools":
                (mirror / entry.name).symlink_to(entry)
        (mirror / "tools").mkdir()
        for entry in (ROOT / "tools").iterdir():
            if entry.name != "routine-delivery.py":
                (mirror / "tools" / entry.name).symlink_to(entry)
        target = mirror / "tools/routine-delivery.py"
        target.write_text(text, encoding="utf-8")
        if not run(target):
            print("routine-delivery board mutate: the unmutated helper fails the board tests", file=sys.stderr)
            return 1
        survivors = []
        for name, original, replacement in MUTANTS:
            if text.count(original) != 1:
                print(f"routine-delivery board mutate: mutant anchor not unique: {name}", file=sys.stderr)
                return 1
            target.write_text(text.replace(original, replacement), encoding="utf-8")
            killed = not run(target)
            print(f"{'killed  ' if killed else 'SURVIVED'} {name}")
            if not killed:
                survivors.append(name)
    print(f"routine-delivery board mutate: {len(MUTANTS) - len(survivors)}/{len(MUTANTS)} mutants killed")
    return 1 if survivors else 0


if __name__ == "__main__":
    sys.exit(main())
