#!/usr/bin/env bash
# Fixture for scripts/check-pr-concurrency.py (.github#4730 CI load). Offline, credential-free.
#
# CAUSAL CONTROL. The gate derives "a superseded PR head is cancellable and a main push is not" from the
# workflow text by modelling GitHub's group semantics. This fixture proves it can say NO: it copies the
# real governed workflows, applies each of the mutations below to EACH one (a generator, so a newly
# governed workflow is mutated automatically), and demands the gate refuses every mutant with the reason
# that mutation introduces — not merely a non-zero exit (tests/feed-coherence/run.sh:10, #266).
#
#   M1 delete the concurrency block               -> "no workflow-level `concurrency:` mapping"
#   M2 cancel-in-progress true (unconditional)    -> "cancel-in-progress is True"
#   M3 cancel-in-progress false (never cancels)   -> MODEL: a PR push does NOT cancel the superseded run
#   M4 fallback github.ref (Root's first key)     -> MODEL: a main push's PENDING run IS dropped
#   M5 group without the PR number                -> MODEL: a PR push does NOT cancel the superseded run
#   M6 group keyed on another workflow's name     -> "concurrency.group is"
#   M7 an extra concurrency key                   -> "unexpected concurrency key"
#   M8 `<name>-${{ github.ref }}` + cancel true   -> MODEL: a main push IS cancelled (the shape adr-coherence uses)
#   M9 a job-level concurrency block, the workflow-level one left clean -> "job-level concurrency"
#   M10 fallback github.sha (the #5001 key)      -> MODEL: two dispatches at one commit DO drop the older pending run
#   M11 the pre-#5159 PR-wide key on a workflow a body edit starts (head-keyed workflow: drop the head guard;
#       any other: add `edited` to its pull_request types) -> MODEL: an `edited` run DOES cancel the `opened` run
#   M12 (head-keyed workflows only) `edited` added to the head-action list -> MODEL: the same
#   D1 (sweep) an ungoverned workflow a body edit starts -> "not governed"; D2 one only head actions start -> clean
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
TOOL="$ROOT/scripts/check-pr-concurrency.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/pr-concurrency-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A. The real tree is clean.
rc=0; out="$(python3 "$TOOL" --root "$ROOT" 2>&1)" || rc=$?
if [ "$rc" = 0 ]; then ok; echo "PASS  A1 the real governed workflows are clean: $out"; else bad "A1 the real tree is clean" "$out"; fi

# B. Mutant generator: N workflows x 8 mutations.
mapfile -t NAMES < <(python3 "$TOOL" --list)
mapfile -t PTNAMES < <(python3 "$TOOL" --list-pr-only)
ALL=("${NAMES[@]}" "${PTNAMES[@]}")
[ "${#NAMES[@]}" -gt 0 ] || { echo "FAIL  no governed workflows listed — a gate that governs nothing is not a clean gate"; exit 1; }

mutants=0
mapfile -t METANAMES < <(python3 "$TOOL" --root "$ROOT" --list-head-keyed)
[ "${#METANAMES[@]}" -gt 0 ] || { echo "FAIL  no head-keyed workflows listed — #5159's body-edit claims would govern nothing"; exit 1; }
is_meta() { local n; for n in "${METANAMES[@]}"; do [ "$n" = "$1" ] && return 0; done; return 1; }
EDITED_KEY="a PR \`edited\` event never cancels or replaces the \`opened\` run for the same head"
for name in "${NAMES[@]}"; do
  ms="M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M11"; is_meta "$name" && ms="$ms M12"
  for m in $ms; do
    d="$WORK/$name-$m"; mkdir -p "$d/.github/workflows"
    # every governed file must exist in the mutant root, so copy them all and mutate only one
    for other in "${ALL[@]}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
    python3 - "$d/.github/workflows/$name.yml" "$m" "$name" <<'PY'
import re, sys
path, m, name = sys.argv[1:4]
t = open(path, encoding="utf-8").read()
block = re.search(r"^concurrency:\n(?:  .*\n)+", t, re.M)
assert block, f"{path}: the clean tree has no concurrency block to mutate"
b = block.group(0)
GUARD = "contains(fromJSON('[\"opened\",\"synchronize\",\"reopened\"]'), github.event.action) && "
if m in ("M11", "P6") and GUARD not in b:
    # a workflow a body edit does not start: make one start it, leaving the PR-wide key alone
    import yaml
    doc = yaml.safe_load(t)
    on = doc.get("on", doc.get(True))
    if isinstance(on, (str, list)):
        on = {e: None for e in ([on] if isinstance(on, str) else on)}
    event = "pull_request_target" if "pull_request_target" in on else "pull_request"
    spec = on[event] if isinstance(on[event], dict) else {}
    spec["types"] = list(spec.get("types") or ["opened", "synchronize", "reopened"]) + ["edited"]
    on[event] = spec
    doc.pop(True, None); doc["on"] = on
    open(path, "w", encoding="utf-8").write(yaml.safe_dump(doc, sort_keys=False))
    sys.exit(0)
if m in ("M11", "P6"):
    open(path, "w", encoding="utf-8").write(t.replace(b, b.replace(GUARD, ""), 1))
    sys.exit(0)
if m in ("M12", "P7"):
    assert GUARD in b, f"{m} applies only to a head-keyed workflow"
    open(path, "w", encoding="utf-8").write(t.replace(b, b.replace('"reopened"]', '"reopened","edited"]'), 1))
    sys.exit(0)
if m == "M9":
    # leave the workflow-level block alone; give the FIRST job its own, cancelling, concurrency
    first = re.search(r"^jobs:\n(?:[ \t]*(?:#.*)?\n)*  [A-Za-z0-9_-]+:\n", t, re.M)
    assert first, f"{path}: cannot find the first job"
    open(path, "w", encoding="utf-8").write(t.replace(first.group(0), first.group(0) + "    concurrency:\n      group: job-level-x\n      cancel-in-progress: true\n", 1))
    sys.exit(0)
new = {
  "M1": "",
  "M2": b.replace("${{ github.event_name == 'pull_request' }}", "true"),
  "M3": b.replace("${{ github.event_name == 'pull_request' }}", "false"),
  "M4": b.replace("github.run_id", "github.ref"),
  "M10": b.replace("github.run_id", "github.sha"),
  "M5": re.sub(r"group: .*", f"group: {name}-${{{{ github.run_id }}}}", b),
  "M6": re.sub(r"group: .*", "group: some-other-workflow-${{ github.event.pull_request.number || github.run_id }}", b),
  "M7": b + "  queue: max\n",
  "M8": re.sub(r"group: .*", f"group: {name}-${{{{ github.ref }}}}", b).replace("${{ github.event_name == 'pull_request' }}", "true"),
}[m]
assert new != b or m == "M1" and new == "", f"{m} did not change {path}"
open(path, "w", encoding="utf-8").write(t.replace(b, new, 1))
PY
    want=""
    case "$m" in
      M1) want="no workflow-level \`concurrency:\` mapping" ;;
      M2) want="cancel-in-progress is True" ;;
      M3) want="model says it is NOT true that a PR push (new head) cancels the superseded head's run" ;;
      M4) want="model says it IS true that a main push does not drop the previous main push's PENDING run" ;;
      M5) want="model says it is NOT true that a PR push (new head) cancels the superseded head's run" ;;
      M6) want="concurrency.group is" ;;
      M7) want="unexpected concurrency key" ;;
      M8) want="model says it IS true that a main push does not cancel the previous main push run" ;;
      M9) want="job-level concurrency" ;;
      M10) want="model says it IS true that two workflow_dispatch runs at one commit do not drop the older PENDING run" ;;
      M11|M12) want="model says it IS true that $EDITED_KEY" ;;
    esac
    rc=0; out="$(python3 "$TOOL" --root "$d" 2>&1)" || rc=$?
    mutants=$((mutants+1))
    key="${want#model says it IS true that }"; key="${key#model says it is NOT true that }"
    mine="$(grep -F -- "$name.yml" <<<"$out" || true)"
    case "$mine" in
      *"$key"*) [ "$rc" = 1 ] && ok || bad "$name $m exited $rc, want 1" "$out" ;;
      *) bad "$name $m was not refused for the right reason (rc=$rc, want '$key')" "$(sed -n 1,5p <<<"$out")" ;;
    esac
  done
done
[ "$failcount" != 0 ] || echo "PASS  B  $mutants mutant(s) over ${#NAMES[@]} governed workflow(s) were each refused for the reason that mutation introduces"

# B2. The PR-event-only workflows (pull_request_target or pull_request) have their own shape (PR number alone, unconditional cancel).
#   P1 delete the block -> "no workflow-level"   P2 never cancel -> MODEL: a PR push does NOT cancel
#   P3 group = sha only -> MODEL: a PR push does NOT cancel   P4 group = default-branch ref -> MODEL: another PR IS cancelled
#   P5 job-level concurrency (workflow-level left clean) -> "job-level concurrency"
#   P6 the pre-#5159 PR-wide key on a workflow a body edit starts -> MODEL: an `edited` run DOES cancel the `opened` run
#   P7 (head-keyed only) `edited` added to the head-action list -> MODEL: the same
ptm=0
for name in "${PTNAMES[@]}"; do
  ms="P1 P2 P3 P4 P5 P6"; is_meta "$name" && ms="$ms P7"
  for m in $ms; do
    d="$WORK/$name-$m"; mkdir -p "$d/.github/workflows"
    for other in "${ALL[@]}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
    python3 - "$d/.github/workflows/$name.yml" "$m" "$name" <<'PY'
import re, sys
path, m, name = sys.argv[1:4]
t = open(path, encoding="utf-8").read()
block = re.search(r"^concurrency:\n(?:  .*\n)+", t, re.M)
assert block, f"{path}: the clean tree has no concurrency block to mutate"
b = block.group(0)
GUARD = "contains(fromJSON('[\"opened\",\"synchronize\",\"reopened\"]'), github.event.action) && "
if m == "P6" and GUARD not in b:
    import yaml
    doc = yaml.safe_load(t)
    on = doc.get("on", doc.get(True))
    event = "pull_request_target" if "pull_request_target" in on else "pull_request"
    spec = on[event] if isinstance(on[event], dict) else {}
    spec["types"] = list(spec.get("types") or ["opened", "synchronize", "reopened"]) + ["edited"]
    on[event] = spec
    doc.pop(True, None); doc["on"] = on
    open(path, "w", encoding="utf-8").write(yaml.safe_dump(doc, sort_keys=False))
    sys.exit(0)
if m == "P6":
    open(path, "w", encoding="utf-8").write(t.replace(b, b.replace(GUARD, ""), 1))
    sys.exit(0)
if m == "P7":
    open(path, "w", encoding="utf-8").write(t.replace(b, b.replace('"reopened"]', '"reopened","edited"]'), 1))
    sys.exit(0)
if m == "P5":
    first = re.search(r"^jobs:\n(?:[ \t]*(?:#.*)?\n)*  [A-Za-z0-9_-]+:\n", t, re.M)
    assert first, "no first job"
    open(path, "w", encoding="utf-8").write(t.replace(first.group(0), first.group(0) + "    concurrency:\n      group: job-level-x\n      cancel-in-progress: true\n", 1))
    sys.exit(0)
new = {
  "P1": "",
  "P2": b.replace("cancel-in-progress: true", "cancel-in-progress: false"),
  "P3": re.sub(r"group: .*", f"group: {name}-${{{{ github.sha }}}}", b),
  "P4": re.sub(r"group: .*", f"group: {name}-${{{{ github.ref }}}}", b),
}[m]
open(path, "w", encoding="utf-8").write(t.replace(b, new, 1))
PY
    case "$m" in
      P1) key="no workflow-level \`concurrency:\` mapping" ;;
      P2) key="model says it is NOT true that a PR push (new head) cancels the superseded head's run" ;;
      P3) key="model says it is NOT true that a PR push (new head) cancels the superseded head's run" ;;
      P4) key="concurrency.group is" ;;
      P5) key="job-level concurrency" ;;
      P6|P7) key="model says it IS true that $EDITED_KEY" ;;
    esac
    rc=0; out="$(python3 "$TOOL" --root "$d" 2>&1)" || rc=$?
    ptm=$((ptm+1))
    mine="$(grep -F -- "$name.yml" <<<"$out" || true)"
    case "$mine" in
      *"$key"*) [ "$rc" = 1 ] && ok || bad "$name $m exited $rc, want 1" "$out" ;;
      *) bad "$name $m was not refused for the right reason (rc=$rc, want '$key')" "$(sed -n 1,5p <<<"$out")" ;;
    esac
  done
done
[ "$failcount" != 0 ] || echo "PASS  B2 $ptm mutant(s) over ${#PTNAMES[@]} PR-event-only workflow(s) were each refused for the reason that mutation introduces"

# B3. The issue-keyed workflow (check-then-act auto-comment) must serialise events for ONE issue.
#   V1 drop the issue operand -> MODEL: two `issues` events for one issue no longer share a group
#   V2 cancel-in-progress true -> MODEL: a running `issues` run IS cancelled by the next event for its issue
#   V3 key on the issue alone  -> MODEL: a PR push no longer cancels the superseded head
mapfile -t ISSUENAMES < <(python3 "$TOOL" --list-issue-keyed)
[ "${#ISSUENAMES[@]}" -gt 0 ] || { echo "FAIL  no issue-keyed workflows listed"; exit 1; }
ivm=0
for name in "${ISSUENAMES[@]}"; do
  for m in V1 V2 V3; do
    d="$WORK/$name-$m"; mkdir -p "$d/.github/workflows"
    for other in "${ALL[@]}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
    python3 - "$d/.github/workflows/$name.yml" "$m" <<'PY'
import re, sys
path, m = sys.argv[1:3]
t = open(path, encoding="utf-8").read()
block = re.search(r"^concurrency:\n(?:  .*\n)+", t, re.M)
b = block.group(0)
new = {
  "V1": b.replace("github.event.issue.number || ", ""),
  "V2": b.replace("${{ github.event_name == 'pull_request' }}", "true"),
  "V3": b.replace("github.event.pull_request.number || ", ""),
}[m]
assert new != b, f"{m} did not change {path}"
open(path, "w", encoding="utf-8").write(t.replace(b, new, 1))
PY
    case "$m" in
      V1) key="two \`issues\` events for ONE issue share a group" ;;
      V2) key="a running \`issues\` run is never cancelled" ;;
      V3) key="a PR push (new head) cancels the superseded head's run" ;;
    esac
    rc=0; out="$(python3 "$TOOL" --root "$d" 2>&1)" || rc=$?
    ivm=$((ivm+1))
    mine="$(grep -F -- "$name.yml" <<<"$out" || true)"
    case "$mine" in
      *"$key"*) [ "$rc" = 1 ] && ok || bad "$name $m exited $rc, want 1" "$out" ;;
      *) bad "$name $m was not refused for the right reason (rc=$rc, want '$key')" "$(sed -n 1,5p <<<"$out")" ;;
    esac
  done
done
[ "$failcount" != 0 ] || echo "PASS  B3 $ivm mutant(s) over ${#ISSUENAMES[@]} issue-keyed workflow(s) were each refused for the reason that mutation introduces"

# D. The repo-wide sweep (#5159): a workflow a body edit starts must be governed, whatever its name.
for m in D1 D2; do
  d="$WORK/sweep-$m"; mkdir -p "$d/.github/workflows"
  for other in "${ALL[@]}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
  types="[opened, synchronize, reopened, edited]"; [ "$m" = D2 ] && types="[opened, synchronize, reopened]"
  cat >"$d/.github/workflows/stray-body-reader.yml" <<YML
name: stray-body-reader
on:
  pull_request:
    types: $types
concurrency:
  group: stray-body-reader-\${{ github.event.pull_request.number }}
  cancel-in-progress: true
jobs:
  read:
    runs-on: ubuntu-latest
    steps:
      - run: echo body
YML
  rc=0; out="$(python3 "$TOOL" --root "$d" 2>&1)" || rc=$?
  if [ "$m" = D1 ]; then
    case "$(grep -F stray-body-reader.yml <<<"$out" || true)" in
      *"fires on edited but it is not governed"*) [ "$rc" = 1 ] && { ok; echo "PASS  D1 an ungoverned workflow a body edit starts is refused"; } || bad "D1 exited $rc, want 1" "$out" ;;
      *) bad "D1 an ungoverned body-edit workflow was not refused for the right reason (rc=$rc)" "$(sed -n 1,5p <<<"$out")" ;;
    esac
  else
    [ "$rc" = 0 ] && { ok; echo "PASS  D2 an ungoverned workflow only head actions start is left alone"; } || bad "D2 a head-only stray must not be refused (rc=$rc)" "$out"
  fi
done

# C. A missing governed workflow is NO VERDICT (3), never a pass.
d="$WORK/missing"; mkdir -p "$d/.github/workflows"
for other in "${ALL[@]:1}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
rc=0; python3 "$TOOL" --root "$d" >/dev/null 2>&1 || rc=$?
if [ "$rc" = 3 ]; then ok; echo "PASS  C1 a missing governed workflow is exit 3, not a pass"; else bad "C1 missing workflow must be exit 3" "rc=$rc"; fi

echo "pr-concurrency fixture — $((pass+failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" = 0 ] && echo "pr-concurrency fixture — OK"
exit "$failcount"
