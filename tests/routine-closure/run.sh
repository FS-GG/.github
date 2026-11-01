#!/usr/bin/env bash
# Behavioral controls for scripts/routine-closure.py (.github#5202): atomic adoption, mixed-set refusal,
# stale repo-local digest pins, and agreement between the two canonical sources.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOL="$ROOT/scripts/routine-closure.py"
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
PASS=0; FAIL=0
ok()  { PASS=$((PASS+1)); echo "  ok   $1"; }
bad() { FAIL=$((FAIL+1)); echo "  FAIL $1"; }
expect() { # name expected-rc cmd...
  local name=$1 want=$2; shift 2; local out rc; out="$("$@" 2>&1)"; rc=$?
  if [ "$rc" = "$want" ]; then ok "$name"; else bad "$name (rc=$rc want=$want)"; echo "$out" | sed 's/^/       /'; fi
  LAST="$out"
}
has() { grep -qF -- "$2" <<<"$LAST" && ok "$1" || { bad "$1 (missing: $2)"; echo "$LAST" | sed 's/^/       /'; }; }
P=(.fsgg/routine-development.json .github/workflows/routine-eligibility.yml scripts/check-claim-generation.py scripts/check-routine-eligibility-envelope.py scripts/lib/gate.py)
git_env=(-c user.name=t -c user.email=t@example.invalid -c commit.gpgsign=false)

# A source checkout with real history: v1 of the closure, then v2 (current).
SRC="$T/src"; mkdir -p "$SRC"; git -C "$SRC" init -q -b main
write() { for p in "${P[@]}"; do mkdir -p "$SRC/$(dirname "$p")"; printf '%s %s\n' "$1" "$p" > "$SRC/$p"; done; chmod 755 "$SRC/scripts/check-claim-generation.py"; chmod 644 "$SRC/.fsgg/routine-development.json" "$SRC/.github/workflows/routine-eligibility.yml" "$SRC/scripts/check-routine-eligibility-envelope.py" "$SRC/scripts/lib/gate.py"; }
write v1; git -C "$SRC" add -A; git -C "${SRC}" "${git_env[@]}" commit -q -m v1
write v2; git -C "$SRC" add -A; git -C "$SRC" "${git_env[@]}" commit -q -m v2
sha() { sha256sum "$1" | cut -c1-64; }
OLD_WF="$(git -C "$SRC" show HEAD~1:.github/workflows/routine-eligibility.yml | sha256sum | cut -c1-64)"
NEW_WF="$(sha "$SRC/.github/workflows/routine-eligibility.yml")"

recv() { # name -> fresh receiver at v1 with a bystander file
  R="$T/$1"; mkdir -p "$R"; git -C "$R" init -q -b main
  git -C "$SRC" archive HEAD~1 | tar -x -C "$R"; echo keep > "$R/README.md"; git -C "$R" add -A; git -C "$R" "${git_env[@]}" commit -q -m init; }

recv r1
expect "old closure is drift" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "mixed or old set is named" "differs .github/workflows/routine-eligibility.yml"
expect "apply writes the canonical set" 0 python3 "$TOOL" apply "$R" --source "$SRC"
git -C "$R" add -A
expect "applied receiver checks clean" 0 python3 "$TOOL" check "$R" --source "$SRC"
[ "$(stat -c %a "$R/scripts/check-claim-generation.py")" = 755 ] && ok "executable bit applied" || bad "executable bit applied"
[ "$(cat "$R/README.md")" = keep ] && ok "bystander file untouched" || bad "bystander file untouched"

recv r2   # mixed: four new, one old
python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null; git -C "$SRC" show HEAD~1:scripts/lib/gate.py > "$R/scripts/lib/gate.py"
expect "four new plus one old file is drift" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "mixed set is called out" "mixed set: 4 of 5"

recv r3   # absent file
python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null; rm "$R/scripts/lib/gate.py"
expect "absent file is drift" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "absent file named" "absent scripts/lib/gate.py"

recv r4   # mode drift
python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null; chmod 644 "$R/scripts/check-claim-generation.py"
expect "executable-bit drift is refused" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "mode drift named" "mode scripts/check-claim-generation.py"

recv r5   # stale repo-local pin (the Coordination execution-pin / Rendering fence shape)
python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null
mkdir -p "$R/evidence"; printf '{\n "path": ".github/workflows/routine-eligibility.yml",\n "sha256": "%s"\n}\n' "$OLD_WF" > "$R/evidence/pins.json"; git -C "$R" add -A
expect "stale pin to an old digest is refused" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "stale pin names file and line" "stale-pin evidence/pins.json:3"
expect "a declared historical record is waived, visibly" 0 python3 "$TOOL" check "$R" --source "$SRC" --waive evidence/pins.json
has "waiver is reported" "waived-pin evidence/pins.json:3"
printf '{\n "path": ".github/workflows/routine-eligibility.yml",\n "sha256": "%s"\n}\n' "$NEW_WF" > "$R/evidence/pins.json"
expect "pin to the current digest is accepted" 0 python3 "$TOOL" check "$R" --source "$SRC"
has "current pin is reported" "pin-current evidence/pins.json:3"
printf 'sha %s\n' "$(printf unrelated | sha256sum | cut -c1-64)" > "$R/evidence/pins.json"
expect "an unrelated digest is ignored" 0 python3 "$TOOL" check "$R" --source "$SRC"

# Two canonical sources must agree: a Drivers-shaped payload built from the same bytes.
PL="$T/payload"; mkdir -p "$PL/files"
python3 - "$SRC" "$PL" <<'PY'
import hashlib, json, os, shutil, stat, sys
src, out = sys.argv[1], sys.argv[2]
P = ".fsgg/routine-development.json .github/workflows/routine-eligibility.yml scripts/check-claim-generation.py scripts/check-routine-eligibility-envelope.py scripts/lib/gate.py".split()
rows = []
for p in P:
    d = os.path.join(out, "files", p); os.makedirs(os.path.dirname(d), exist_ok=True); shutil.copy(os.path.join(src, p), d)
    rows.append({"path": p, "sha256": hashlib.sha256(open(d, "rb").read()).hexdigest(), "executable": bool(os.stat(os.path.join(src, p)).st_mode & stat.S_IXUSR)})
json.dump({"schema": "fsgg/driver-workspace-manifest/v1", "files": rows}, open(os.path.join(out, "workspace-files.json"), "w"))
PY
recv r6
expect "apply from a Drivers-shaped payload" 0 python3 "$TOOL" apply "$R" --payload "$PL"
git -C "$R" add -A
expect "payload result checks clean against --source" 0 python3 "$TOOL" check "$R" --source "$SRC"
expect "payload result checks clean against --payload" 0 python3 "$TOOL" check "$R" --payload "$PL" --history "$SRC"

# Atomicity: a corrupt payload writes nothing at all.
recv r7; before="$(git -C "$R" status --porcelain | wc -l)"
cp -r "$PL" "$T/corrupt"; echo tamper >> "$T/corrupt/files/scripts/lib/gate.py"
expect "corrupt payload is refused" 2 python3 "$TOOL" apply "$R" --payload "$T/corrupt"
[ "$(git -C "$R" status --porcelain | wc -l)" = "$before" ] && ok "nothing written from a corrupt payload" || bad "nothing written from a corrupt payload"
expect "payload lacking a file is refused" 2 bash -c "cp -r '$PL' '$T/short' && rm '$T/short/files/scripts/lib/gate.py' && python3 '$TOOL' apply '$R' --payload '$T/short'"
git -C "$R" status --porcelain | grep -q . && bad "no partial write after missing-file refusal" || ok "no partial write after missing-file refusal"

# Shallow history cannot name stale pins: refuse to call it clean.
git clone -q --depth 1 "file://$SRC" "$T/shallow"
recv r8; python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null
expect "shallow source history is not a clean result" 2 python3 "$TOOL" check "$R" --source "$T/shallow"
expect "shallow history accepted only when asked" 0 python3 "$TOOL" check "$R" --source "$T/shallow" --allow-shallow

# The real tree: the five canonical files in this checkout agree with their Drivers inventory entries.
python3 - "$ROOT" <<'PY' && ok "workspace-inventory.json lists exactly the five closure paths" || bad "workspace-inventory.json lists the five closure paths"
import json, sys
inv = json.load(open(sys.argv[1] + "/src/FS.GG.Drivers/workspace-inventory.json"))
paths = {f["path"] for f in inv["files"]}
tool = open(sys.argv[1] + "/scripts/routine-closure.py").read()
for p in ".fsgg/routine-development.json .github/workflows/routine-eligibility.yml scripts/check-claim-generation.py scripts/check-routine-eligibility-envelope.py scripts/lib/gate.py".split():
    assert p in paths, p
    assert p in tool, p
PY
echo "routine-closure: passed=$PASS failed=$FAIL"
[ "$FAIL" = 0 ]
