#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-ship-verdict-provenance.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/ship-verdict-provenance.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; fail=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; fail=$((fail + 1)); }
expect() {
  local name="$1" want="$2" needle="$3" root="$4" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ] || ! grep -qF "$needle" <<<"$out"; then bad "$name"; printf '%s\n' "$out"; else ok "$name"; fi
}
make_root() { mkdir -p "$1/readiness/sample"; printf '%s\n' '{"status":"shipReady"}' > "$1/readiness/sample/ship-verdict.json"; }

GOOD="$WORK/good"; make_root "$GOOD"
expect "clean hand-authored verdict passes" 0 "carry no unverifiable" "$GOOD"

STALE="$WORK/stale"; make_root "$STALE"
printf '%s\n' '{"sources":[{"path":"work/spec.md","digest":{"algorithm":"sha256","value":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]}' > "$STALE/readiness/sample/ship.json"
printf '%s\n' '{"status":"shipReady","sourcesDigest":{"algorithm":"sha256","value":"0000000000000000000000000000000000000000000000000000000000000000"}}' > "$STALE/readiness/sample/ship-verdict.json"
expect "REGRESSION #2208: a stale digest is red" 1 "does not match" "$STALE"

VERIFIED="$WORK/verified"; make_root "$VERIFIED"
python3 - "$VERIFIED/readiness/sample/ship.json" "$VERIFIED/readiness/sample/ship-verdict.json" <<'PY'
import hashlib, json, sys

sources = [
    {"path":"work/z.md", "digest":{"algorithm":"sha256", "value":"7" * 64}},
    {"path":"work/a.md", "digest":{"algorithm":"sha256", "value":"a" * 64}},
]
preimage = "\n".join(
    f"{source['path']}|{source['digest']['algorithm']}:{source['digest']['value']}"
    for source in sorted(sources, key=lambda source: source["path"])
)
value = hashlib.sha256(preimage.encode("utf-8")).hexdigest()
open(sys.argv[1], "w", encoding="utf-8").write(json.dumps({"sources": sources}, indent=2))
open(sys.argv[2], "w", encoding="utf-8").write(
    json.dumps({"status":"shipReady", "sourcesDigest":{"algorithm":"sha256", "value":value}}, indent=2)
)
PY
expect "a generated digest is independently verified from ship.json" 0 "carry no unverifiable" "$VERIFIED"

rewrite_source_and_aggregate() {
  python3 - "$VERIFIED/readiness/sample/ship.json" "$VERIFIED/readiness/sample/ship-verdict.json" "$1" <<'PY'
import hashlib, json, sys
ship_path, verdict_path, mode = sys.argv[1:]
ship = json.load(open(ship_path, encoding="utf-8"))
if mode == "algorithm":
    ship["sources"][0]["digest"]["algorithm"] = "sha512"
elif mode == "value":
    ship["sources"][0]["digest"]["value"] = "not-a-sha256"
elif mode == "source":
    ship["sources"][0]["digest"]["value"] = "b" * 64
else:
    raise SystemExit(f"unknown mode: {mode}")
preimage = "\n".join(
    f"{source['path']}|{source['digest']['algorithm']}:{source['digest']['value']}"
    for source in sorted(ship["sources"], key=lambda source: source["path"])
)
verdict = json.load(open(verdict_path, encoding="utf-8"))
verdict["sourcesDigest"]["value"] = hashlib.sha256(preimage.encode("utf-8")).hexdigest()
open(ship_path, "w", encoding="utf-8").write(json.dumps(ship, indent=2))
open(verdict_path, "w", encoding="utf-8").write(json.dumps(verdict, indent=2))
PY
}

rewrite_source_and_aggregate algorithm
expect "a non-sha256 nested source digest is red despite a matching aggregate" 1 "digest must use sha256" "$VERIFIED"

git_like_valid="$WORK/verified-value"
cp -R "$WORK/verified" "$git_like_valid"
python3 - "$git_like_valid/readiness/sample/ship.json" "$git_like_valid/readiness/sample/ship-verdict.json" <<'PY'
import hashlib, json, sys
ship_path, verdict_path = sys.argv[1:]
ship = json.load(open(ship_path, encoding="utf-8"))
ship["sources"][0]["digest"] = {"algorithm":"sha256", "value":"not-a-sha256"}
preimage = "\n".join(
    f"{source['path']}|{source['digest']['algorithm']}:{source['digest']['value']}"
    for source in sorted(ship["sources"], key=lambda source: source["path"])
)
verdict = json.load(open(verdict_path, encoding="utf-8"))
verdict["sourcesDigest"]["value"] = hashlib.sha256(preimage.encode("utf-8")).hexdigest()
open(ship_path, "w", encoding="utf-8").write(json.dumps(ship, indent=2))
open(verdict_path, "w", encoding="utf-8").write(json.dumps(verdict, indent=2))
PY
expect "a noncanonical nested sha256 value is red despite a matching aggregate" 1 "64 lowercase hexadecimal" "$git_like_valid"

# Rebuild the valid fixture, then mutate one canonical source digest without updating the aggregate.
rm -rf "$VERIFIED"
cp -R "$WORK/verified-value" "$VERIFIED"
python3 - "$VERIFIED/readiness/sample/ship.json" "$VERIFIED/readiness/sample/ship-verdict.json" <<'PY'
import json, sys
ship_path, verdict_path = sys.argv[1:]
ship = json.load(open(ship_path, encoding="utf-8"))
ship["sources"][0]["digest"] = {"algorithm":"sha256", "value":"7" * 64}
open(ship_path, "w", encoding="utf-8").write(json.dumps(ship, indent=2))
PY
expect "mutating a generated verdict source makes the gate red" 1 "does not match" "$VERIFIED"

BAD="$WORK/bad"; make_root "$BAD"
printf '%s\n' '{not json' > "$BAD/readiness/sample/ship-verdict.json"
expect "invalid subject is no verdict, not clean" 3 "invalid JSON" "$BAD"

EMPTY="$WORK/empty"; mkdir -p "$EMPTY/readiness"
expect "no subjects is no verdict, never a vacuous green" 3 "no ship-verdict.json" "$EMPTY"

# REGRESSION #2393: retain the compatibility repair for a legacy digest whose source inventory is
# unavailable. The default gate now verifies generated digests instead of requiring their absence.
FIXESC="$WORK/fix-escape"; mkdir -p "$FIXESC/readiness/sample"
VERDICT="$FIXESC/readiness/sample/ship-verdict.json"
python3 - "$VERDICT" <<'PY'
import json, sys

doc = {
    "schemaVersion": 1,
    "workId": "sample",
    "stage": "ship",
    "status": "shipReady",
    "generator": "FS.GG.SDD.Artifacts/1.0.0",
    "sourcesDigest": {
        "algorithm": "sha256",
        "value": "82cc645d0e19011af10ab9c077fe03c6a156f116c474ed0d18f7594a64e600fc",
    },
    "verificationReadiness": {"status": "verificationReady"},
    "readiness": "shipReady",
}
open(sys.argv[1], "w", encoding="utf-8").write(json.dumps(doc, indent=2))
PY
# Independently (no call into the gate) compute what a byte-faithful strip must produce, so this
# is a check against stdlib `json`, not a tautology against the code under test.
python3 - "$VERDICT" <<'PY' > "$WORK/expected-after-fix.json"
import json, sys

doc = json.load(open(sys.argv[1], encoding="utf-8"))
del doc["sourcesDigest"]
print(json.dumps(doc, indent=2), end="")
PY

rc=0
out="$(python3 "$GATE" --root "$FIXESC" --fix 2>&1)" || rc=$?
if [ "$rc" -ne 0 ] || ! grep -qF "carry no unverifiable" <<<"$out" || ! grep -qF "stripped sourcesDigest from 1 verdict" <<<"$out"; then
  bad "REGRESSION #2393: --fix repairs a freshly re-added sourcesDigest and reports it"; printf '%s\n' "$out"
else
  ok "REGRESSION #2393: --fix repairs a freshly re-added sourcesDigest and reports it"
fi

if diff -u "$WORK/expected-after-fix.json" "$VERDICT" >/dev/null; then
  ok "--fix output is byte-identical to an independently computed strip"
else
  bad "--fix output is byte-identical to an independently computed strip"; diff -u "$WORK/expected-after-fix.json" "$VERDICT" || true
fi

rc2=0
out2="$(python3 "$GATE" --root "$FIXESC" --fix 2>&1)" || rc2=$?
if [ "$rc2" -ne 0 ] || ! grep -qF "nothing to strip" <<<"$out2"; then
  bad "--fix is idempotent once clean"; printf '%s\n' "$out2"
else
  ok "--fix is idempotent once clean"
fi

if python3 "$GATE" --root "$ROOT" >/dev/null; then ok "the shipped readiness surface passes"; else bad "the shipped readiness surface passes"; fi
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
