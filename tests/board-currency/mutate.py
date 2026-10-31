#!/usr/bin/env python3
"""Mutation controls for scripts/board-currency-rules.py (Refs #4730).

Each mutant is one deliberate defect in a rule's verdict. The fixture is re-run against a mutated copy
of the module (BOARD_CURRENCY_RULES); a mutant that leaves every test green means no test pins that
verdict, and this script exits 1 naming it. The original module must pass first.
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[1] / "scripts/board-currency-rules.py"

# (name, exact original text, replacement). Every original must occur exactly once.
MUTANTS = [
    ("R1 accepts zero Deliveries", "    if not deliveries:\n", "    if False:\n"),
    ("R1 accepts several Deliveries", "    if len(deliveries) > 1:\n", "    if len(deliveries) > 2:\n"),
    ("R1 ignores the closed Delivery", 'if pr.get("state") == "OPEN" and d.get("state") != "OPEN":', "if False:"),
    ("R1 ignores the Done Delivery", 'elif pr.get("state") == "OPEN" and d.get("status") == "Done":', "elif False:"),
    ("R1 drops the parent check", 'elif parent_card is None or parent_card.get("workLevel") != "Outcome":', "elif False:"),
    ("R1 scopes non-routine PRs", 'if subject in seen or not is_routine(pr.get("body")):', "if subject in seen:"),
    ("R1 ignores the merged window", "            if now - merged_at > merged_window:\n", "            if False:\n"),
    ("Refs inside code spans count", 'return _INLINE.sub(" ", _FENCE.sub(" ", body if isinstance(body, str) else ""))',
     'return body if isinstance(body, str) else ""'),
    ("closing keywords are not references", "for pattern in (_REFS, _CLOSING):", "for pattern in (_REFS,):"),
    ("R2 closed-not-done silenced", 'if not is_open and status != "Done":', 'if False:'),
    ("R2 done-open silenced", 'if is_open and status == "Done":', 'if False:'),
    ("R2 acceptance retroactive", "and closed_at is not None and closed_at >= effective):", "):"),
    ("R2 accepts any author", 'picked = [c for c in comments if str(c.get("author", "")).lower() in roots and parse_time(c.get("createdAt"))]',
     'picked = [c for c in comments if parse_time(c.get("createdAt"))]'),
    ("R2 unread comments pass", 'if comments is None:\n                emit("U", subject, "r2-comments-unread"',
     'if False:\n                emit("U", subject, "r2-comments-unread"'),
    ("R3 closed blocker counts as open", 'if any(s == "OPEN" for s in blockers.values()) or via_child:',
     'if blockers or via_child:'),
    ("R3 truncated list trusted", "native_complete = native.get(\"total\") == len(native_nodes)", "native_complete = True"),
    ("R3 oldest marker wins", "            newest = sorted(", "            newest = newest if newest is not None else sorted("),
    ("R3 no child route", 'via_child = level == "Outcome" and any(', 'via_child = False and any('),
    ("R4 window off by one", "if not known or now - max(known) > activity:", "if not known or now - max(known) >= activity:"),
    ("R4 In review accepts drafts", 'evidence = open_prs if status == "In progress" else ready_prs', "evidence = open_prs"),
    ("R4 outcome active child silenced", 'if status == "In progress" and not any(s in ACTIVE for s in child_status):',
     'if False:'),
    ("R4 outcome review silenced", 'if status == "In review" and any(s != "In review" for s in child_status):', 'if False:'),
    ("R5 silenced", "if children and children_complete and not open_children:", "if False:"),
    ("R5 pending flag dropped", 'pending = {"pending": "closure-proposed"} if comments and has_marker(comments, PROPOSED) else {}',
     "pending = {}"),
    ("R6 truncated children trusted", "                if not children_complete:\n                    emit(\"U\", subject, \"r6-children-unread\"",
     "                if False:\n                    emit(\"U\", subject, \"r6-children-unread\""),
    ("R6 any open child counts", 'elif not any(c is not None and c.get("workLevel") == "Delivery" for c in open_child_items):',
     "elif not open_child_items:"),
    ("R7 Track not required", '("Track", item.get("track"))', '("Track", "x")'),
    ("R7 drafts pass", 'emit("R7", subject, "draft"', 'pass  # emit("R7", subject, "draft"'),
    ("R8 draft PR counts", 'ready_prs = [x for x in open_prs if not x.get("draft")]', "ready_prs = open_prs"),
    ("R8 pre-action silenced", 'if level == "Delivery" and ready_prs and status in PRE_ACTION:', "if False:"),
    ("R8 closed Delivery silenced", 'if level == "Delivery" and ready_prs:\n                emit("R8"', 'if False:\n                emit("R8"'),
    ("R9 orphan silenced", 'emit("R9", subject, "orphan-delivery"', 'pass  # emit("R9", subject, "orphan-delivery"'),
    ("R9 outcome-under-delivery silenced", 'elif level == "Outcome" and parent_item is not None and parent_item.get("workLevel") == "Delivery":',
     "elif False:"),
    ("unreadable card passes", 'emit("U", subject, "unreadable"', 'pass  # emit("U", subject, "unreadable"'),
    ("R1 unreadable parent read as no parent", 'or parent_card is not None and parent_card.get("unreadable")):', "or False):"),
    ("R2 truncated comment trusted", "            elif any_truncated(comments):\n", "            elif False:\n"),
    ("R3 truncated marker trusted", "truncated = comments is not None and any_truncated(comments)", "truncated = False"),
    ("R4 unread comments guessed", 'if not evidence and item.get("comments") is None:', "if False:"),
    ("R9 unreadable parent read as off-board", 'or parent_item is not None and parent_item.get("unreadable"))', "or False)"),
    ("stale findings file kept", "            Path(args.out).unlink(missing_ok=True)\n", ""),
    ("override: two matches accepted", "if len(matches) != 1 or not matches[0].group(\"reason\").strip():",
     "if not matches:"),
    ("override: any author", 'if str(pr.get("author") or "").lower() not in roots or not isinstance(pr.get("edited"), bool):',
     'if not isinstance(pr.get("edited"), bool):'),
    ("override: any editor", 'if pr["edited"] and str(pr.get("editor") or "").lower() not in roots:', "if False:"),
    ("override: code span counts", 'return visible_text(text.replace("\\r\\n", "\\n").replace("\\r", "\\n"))',
     'return text.replace("\\r\\n", "\\n").replace("\\r", "\\n")'),
    ("override: CRLF not normalised", 'return visible_text(text.replace("\\r\\n", "\\n").replace("\\r", "\\n"))',
     "return visible_text(text)"),
    ("overridden silenced", '            failures = [("R1", "overridden")]\n', '            failures = []\n'),
    ("carry ignores carryFrom", "in_carry = carry_from is not None and merged_at >= carry_from and now - merged_at <= carry",
     "in_carry = now - merged_at <= carry"),
    ("carry ignores carryDays", "in_carry = carry_from is not None and merged_at >= carry_from and now - merged_at <= carry",
     "in_carry = carry_from is not None and merged_at >= carry_from"),
    ("override not carried", "if not (in_carry or override is not None):", "if not in_carry:"),
    ("anonymous card ignored for references", "or (anonymous_unreadable and None in cards)", "or False"),
    ("anonymous card ignored for children", "        if board.anonymous_unreadable and any(ref_key(n.get(\"repo\"), n.get(\"number\")) not in board.by_ref\n",
     "        if False and any(ref_key(n.get(\"repo\"), n.get(\"number\")) not in board.by_ref\n"),
    ("anonymous card ignored for the R1 parent", "if parent and (parent_card is None and anonymous_unreadable or parent_card",
     "if parent and (False or parent_card"),
    ("anonymous card ignored for the R9 parent", "parent_unknown = parent and (parent_item is None and board.anonymous_unreadable",
     "parent_unknown = parent and (False"),
    ("PR-typed card links as a Delivery", 'return None if isinstance(found, dict) and found.get("type") == "PullRequest" else found',
     "return found"),
    ("partial snapshot accepted", 'if snapshot.get("complete") is not True:', "if False:"),
    ("count mismatch accepted", "if not isinstance(total, int) or total != len(items):", "if not isinstance(total, int):"),
    ("private subject published", 'return flag is False', "return flag is not True"),
]


# Mutants of scripts/board-linkage.py, run against test_board_linkage.py with BOARD_LINKAGE_SCRIPT.
LINKAGE = HERE.parents[1] / "scripts/board-linkage.py"
LINKAGE_MUTANTS = [
    ("check: non-routine not skipped", 'if not rules.is_routine(pr.get("body")):\n        return "skip", None\n', ""),
    ("check: override ignored", '    if rules.override_of(pr, roots) is not None:\n        return "overridden", None\n', ""),
    ("check: other projects count", "if isinstance(i, dict) and (i.get(\"project\") or {}).get(\"number\") == PROJECT_NUMBER",
     "if isinstance(i, dict)"),
    ("check: truncated items trusted", 'if not isinstance(nodes, list) or items.get("totalCount") != len(nodes):',
     "if not isinstance(nodes, list):"),
    ("check: read failure passes", '        return "unknown", "board-read"\n', '        return "linked", None\n'),
    ("check: required never fails", 'return 1 if args.mode == "required" and verdict in ("refused", "unknown") else 0', "return 0"),
    ("check: precheck skips unreadable payloads", "    except (OSError, json.JSONDecodeError, AttributeError):\n        routine = True\n",
     "    except (OSError, json.JSONDecodeError, AttributeError):\n        routine = False\n"),
    ("check: precheck skips routine PRs", 'routine = rules.is_routine(body) if isinstance(event.get("pull_request"), dict) else True',
     'routine = False'),
    ("check: advisory fails", 'return 1 if args.mode == "required" and verdict in ("refused", "unknown") else 0',
     'return 1 if verdict in ("refused", "unknown") else 0'),
]


def run_linkage(script: Path) -> bool:
    env = dict(os.environ, BOARD_LINKAGE_SCRIPT=str(script))
    proc = subprocess.run([sys.executable, "-I", "-m", "unittest", "discover", "-s", str(HERE), "-p", "test_board_linkage.py"],
                          capture_output=True, text=True, env=env, cwd=HERE.parents[1], timeout=300)
    return proc.returncode == 0


def run(module: Path) -> bool:
    env = dict(os.environ, BOARD_CURRENCY_RULES=str(module))
    proc = subprocess.run([sys.executable, "-I", "-m", "unittest", "discover", "-s", str(HERE), "-p", "test_*.py"],
                          capture_output=True, text=True, env=env, cwd=HERE.parents[1], timeout=300)
    return proc.returncode == 0


def main() -> int:
    original = SOURCE.read_text(encoding="utf-8")
    if not run(SOURCE):
        print("mutation controls: the ORIGINAL module fails its fixture; fix that first")
        return 1
    survivors = []
    with tempfile.TemporaryDirectory() as tmp:
        for name, old, new in MUTANTS:
            count = original.count(old)
            if count != 1:
                print(f"mutation controls: mutant {name!r} anchor occurs {count} time(s), expected 1")
                return 1
            mutated = Path(tmp) / "board-currency-rules.py"
            mutated.write_text(original.replace(old, new), encoding="utf-8")
            killed = not run(mutated)
            print(f"{'killed  ' if killed else 'SURVIVED'} {name}")
            if not killed:
                survivors.append(name)
        linkage = LINKAGE.read_text(encoding="utf-8")
        if not run_linkage(LINKAGE):
            print("mutation controls: the ORIGINAL board-linkage.py fails its fixture; fix that first")
            return 1
        for name, old, new in LINKAGE_MUTANTS:
            count = linkage.count(old)
            if count != 1:
                print(f"mutation controls: mutant {name!r} anchor occurs {count} time(s), expected 1")
                return 1
            work = Path(tmp) / "linkage"
            work.mkdir(exist_ok=True)
            (work / "board-currency-rules.py").write_text(original, encoding="utf-8")
            mutated = work / "board-linkage.py"
            mutated.write_text(linkage.replace(old, new), encoding="utf-8")
            killed = not run_linkage(mutated)
            print(f"{'killed  ' if killed else 'SURVIVED'} {name}")
            if not killed:
                survivors.append(name)
    total = len(MUTANTS) + len(LINKAGE_MUTANTS)
    print(f"mutation controls: {total - len(survivors)}/{total} killed")
    return 1 if survivors else 0


if __name__ == "__main__":
    sys.exit(main())
