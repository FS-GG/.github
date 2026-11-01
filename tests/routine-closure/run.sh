#!/usr/bin/env bash
# Behavioral controls for scripts/routine-closure.py (.github#5202): atomic adoption, mixed-set refusal,
# stale repo-local digest pins, and agreement between the two canonical sources.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOL="${ROUTINE_CLOSURE_TOOL:-$ROOT/scripts/routine-closure.py}"
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
OLD_GATE="$(git -C "$SRC" show HEAD~1:scripts/lib/gate.py | sha256sum | cut -c1-64)"
waive() { # file digest... -> writes the receiver's own declaration (path evidence/pins.json for each digest)
  local f=$1; shift; local first=1
  { printf '{"schema":"fsgg.routine-closure-waivers/1","waivers":['
    for d in "$@"; do [ $first = 1 ] || printf ','; first=0; printf '{"path":"%s","sha256":"%s","reason":"historical record, not a live pin"}' "$f" "$d"; done
    printf ']}\n'; } > "$R/.fsgg/routine-closure-waivers.json"; git -C "$R" add -A; }
waive evidence/pins.json "$OLD_WF"
expect "an in-receiver waiver of that exact digest is accepted, visibly" 0 python3 "$TOOL" check "$R" --source "$SRC"
has "waiver is reported with its reason" "waived-pin evidence/pins.json:3"
printf '{\n "a": "%s",\n "b": "%s"\n}\n' "$OLD_WF" "$OLD_GATE" > "$R/evidence/pins.json"; git -C "$R" add -A
expect "a live stale pin hidden behind a waiver in the same file still fails" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "the unwaived digest is named" "stale-pin evidence/pins.json:3"
waive evidence/other.json "$OLD_WF"
printf '{\n "a": "%s"\n}\n' "$OLD_WF" > "$R/evidence/pins.json"; git -C "$R" add -A
expect "a waiver for another path does not cover this file" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "the unused waiver is reported" "unused-waiver evidence/other.json"
RA="$R"
recv r5b; python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null
mkdir -p "$R/evidence"; printf '{\n "a": "%s"\n}\n' "$OLD_WF" > "$R/evidence/pins.json"; git -C "$R" add -A
expect "scope: another receiver without its own declaration fails on the same pin" 1 python3 "$TOOL" check "$R" --source "$SRC"
printf '{"schema":"fsgg.routine-closure-waivers/1","waivers":[{"path":"evidence/pins.json","sha256":"nothex","reason":"x"}]}' > "$R/.fsgg/routine-closure-waivers.json"
expect "a malformed waiver declaration is exit 2" 2 python3 "$TOOL" check "$R" --source "$SRC"
R="$RA"
printf '{\n "a": "%s"\n}\n' "$(printf %s "$OLD_WF" | tr a-f A-F)" > "$R/evidence/pins.json"; rm -f "$R/.fsgg/routine-closure-waivers.json"; git -C "$R" add -A
expect "an UPPERCASE stale digest is flagged" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "uppercase pin located" "stale-pin evidence/pins.json:2"
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
[ -z "$(git -C "$R" status --porcelain)" ] && ok "no partial write after missing-file refusal" || bad "no partial write after missing-file refusal"

# Shallow history cannot name stale pins: refuse to call it clean.
git clone -q --depth 1 "file://$SRC" "$T/shallow"
recv r8; python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null
expect "shallow source history is not a clean result" 2 python3 "$TOOL" check "$R" --source "$T/shallow"
expect "shallow history accepted only when asked" 0 python3 "$TOOL" check "$R" --source "$T/shallow" --allow-shallow

# Header and shallow wording.
recv r9; python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null
expect "check prints the source identity and digests first" 0 python3 "$TOOL" check "$R" --source "$SRC"
has "source identity shown" "canonical source $SRC"
has "a digest line shown" "$(sha "$SRC/scripts/lib/gate.py" | cut -c1-12)"
expect "--allow-shallow says detection is off" 0 python3 "$TOOL" check "$R" --source "$T/shallow" --allow-shallow
has "stale-pin detection off is stated" "stale-pin detection OFF"

# Files that cannot be scanned are reported, not silently ignored.
printf '\xff\xfe\x00binary' > "$R/blob.bin"; dd if=/dev/zero of="$R/big.txt" bs=1M count=9 status=none; ln -s README.md "$R/link.md"; git -C "$R" add -A
expect "unscannable files do not fail the check" 0 python3 "$TOOL" check "$R" --source "$SRC"
has "non-UTF-8 file reported as skipped" "skipped blob.bin (not UTF-8)"
has "oversized file reported as skipped" "skipped big.txt (over 8 MiB)"
has "symlink reported as skipped" "skipped link.md (symlink)"

# check refuses a symlinked closure file (CI's base blob would be the link text).
recv r10; python3 "$TOOL" apply "$R" --source "$SRC" >/dev/null
cp "$R/scripts/lib/gate.py" "$T/gate-copy.py"; rm "$R/scripts/lib/gate.py"; ln -s "$T/gate-copy.py" "$R/scripts/lib/gate.py"
expect "a symlinked closure file is drift even with identical bytes" 1 python3 "$TOOL" check "$R" --source "$SRC"
has "symlink named" "not-regular scripts/lib/gate.py (symlink"

# apply refuses a symlinked parent and writes nothing outside the receiver.
R="$T/r11"; mkdir -p "$R" "$T/outside"; git -C "$R" init -q -b main; ln -s "$T/outside" "$R/.fsgg"
expect "apply refuses a symlinked parent directory" 2 python3 "$TOOL" apply "$R" --source "$SRC"
has "refusal names the parent" "symlinked parent .fsgg"
[ -z "$(ls -A "$T/outside")" ] && ok "nothing written outside the receiver" || bad "nothing written outside the receiver"
[ ! -e "$R/.github" ] && ok "nothing written inside the receiver either" || bad "nothing written inside the receiver either"
expect "check also flags the symlinked parent" 1 python3 "$TOOL" check "$R" --source "$SRC"

# apply refuses a closure path that is a directory, or a symlink, before touching anything.
recv r12; rm "$R/scripts/lib/gate.py"; mkdir "$R/scripts/lib/gate.py"
before="$(cat "$R/.github/workflows/routine-eligibility.yml")"
expect "apply refuses a directory in place of a closure file" 2 python3 "$TOOL" apply "$R" --source "$SRC"
[ "$(cat "$R/.github/workflows/routine-eligibility.yml")" = "$before" ] && ok "no file was replaced before the refusal" || bad "no file was replaced before the refusal"

# Forced replace-phase failure: the original set is intact and no temp file is left behind (B1).
rollback() { # name receiver-prep-command failing-call-number
  recv "$1"; eval "$2"; git -C "$R" add -A; git -C "$R" "${git_env[@]}" commit -q -m prep --allow-empty
  python3 - "$TOOL" "$R" "$SRC" "$3" <<'PY'
import importlib.util, os, sys
tool, receiver, source, nth = sys.argv[1:]
spec = importlib.util.spec_from_file_location("rc", tool); rc = importlib.util.module_from_spec(spec); spec.loader.exec_module(rc)
real, calls = os.replace, {"n": 0}
def flaky(a, b):
    calls["n"] += 1
    if calls["n"] == int(nth):
        raise OSError("forced replace failure")
    return real(a, b)
rc.os.replace = flaky
try:
    rc.main(["apply", receiver, "--source", source])
except SystemExit as stop:
    sys.exit(0 if stop.code == 2 else 9)
sys.exit(8)
PY
}
rollback r13 ':' 5; rc=$?
[ "$rc" = 0 ] && ok "apply exits 2 when the fifth replace fails" || bad "apply exits 2 when the fifth replace fails (rc=$rc)"
[ -z "$(git -C "$R" status --porcelain)" ] && ok "original set intact after a failure on the fifth replace" || { bad "original set intact after a failure on the fifth replace"; git -C "$R" status --porcelain | sed 's/^/       /'; }
[ -z "$(find "$R" -name '.routine-closure.*' -not -path '*/.git/*')" ] && ok "no temporary file left behind" || bad "no temporary file left behind"
rollback r14 'rm -rf "$R/scripts" "$R/.github" "$R/.fsgg"' 3; rc=$?
[ "$rc" = 0 ] && ok "apply exits 2 when the third replace fails on a fresh receiver" || bad "apply exits 2 when the third replace fails on a fresh receiver (rc=$rc)"
[ -z "$(git -C "$R" status --porcelain)" ] && [ ! -e "$R/scripts" ] && [ ! -e "$R/.github" ] && [ ! -e "$R/.fsgg" ] && ok "files and directories created by the failed apply are removed" || bad "files and directories created by the failed apply are removed"
[ -z "$(find "$R" -name '.routine-closure.*' -not -path '*/.git/*')" ] && ok "no temporary file left on a fresh receiver" || bad "no temporary file left on a fresh receiver"

# A payload that does not belong to the history clone is exit 2, not a judgement on stale pins (B2.3).
cp -r "$PL" "$T/foreign"
python3 - "$T/foreign" <<'PY'
import hashlib, json, sys
root = sys.argv[1]
with open(root + "/files/scripts/lib/gate.py", "ab") as f:
    f.write(b"# not a .github revision\n")
m = json.load(open(root + "/workspace-files.json"))
for row in m["files"]:
    if row["path"] == "scripts/lib/gate.py":
        row["sha256"] = hashlib.sha256(open(root + "/files/scripts/lib/gate.py", "rb").read()).hexdigest()
json.dump(m, open(root + "/workspace-files.json", "w"))
PY
recv r15
expect "a payload digest absent from the history clone is exit 2" 2 python3 "$TOOL" check "$R" --payload "$T/foreign" --history "$SRC"
has "the unrelated history is named as the reason" "not a revision in the history clone"
# N1: a full, non-shallow repo that never touched the five paths must not pass as history either.
UNREL="$T/unrelated"; mkdir -p "$UNREL"; git -C "$UNREL" init -q -b main; echo hi > "$UNREL/README.md"; git -C "$UNREL" add -A; git -C "$UNREL" "${git_env[@]}" commit -q -m one
[ "$(git -C "$UNREL" rev-parse --is-shallow-repository)" = false ] && ok "the unrelated history is a full repo" || bad "the unrelated history is a full repo"
expect "an unrelated full history with no closure revisions is exit 2" 2 python3 "$TOOL" check "$R" --payload "$PL" --history "$UNREL"
has "unrelated history named as the reason" "not a revision in the history clone"
expect "an unrelated history is exit 2 even with --allow-shallow" 2 python3 "$TOOL" check "$R" --payload "$PL" --history "$UNREL" --allow-shallow

# The real tree: the Drivers workspace inventory lists the five closure paths (among its other entries).
python3 - "$ROOT" <<'PY' && ok "workspace-inventory.json lists the five closure paths among its entries" || bad "workspace-inventory.json lists the five closure paths among its entries"
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
