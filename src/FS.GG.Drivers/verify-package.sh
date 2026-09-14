#!/usr/bin/env bash
# verify-package.sh — the FS.GG.Drivers gate (ADR-0054 §Byte-transport, ADR-0062, ADR-0063). Run by
# .github/workflows/drivers-package.yml, and locally. Nothing executes a workflow, so the verification
# lives in a script the workflow CALLS — the same reason the kit/landable/coherence gates moved out of
# hand-copied YAML (#724).
#
# It proves the things the driver package must get right:
#   1. DERIVED, NOT RESTATED — the staged set is exactly registry/driver-skill-manifest.json's
#      `scope: driver` rows (ADR-0058); the packed manifest is byte-identical to the committed one; and
#      a `scope: operator` row (ADR-0057) carries NO bytes (delivered nowhere).
#   2. PACKS — `dotnet pack` produces a nupkg carrying the manifest, every driver SKILL.md, and the
#      consumer handle (build/FS.GG.Drivers.props) + README.
#   3. CONTENT-ADDRESSED — every packed SKILL.md's canonical digest matches its manifest sha256
#      (the ADR-0014 record the SDD CLI verifies against at scaffold time).
#   4. PACKAGE-CLOSED — every relative board-driver link resolves after staged and consumer-shaped
#      materialization, and a link into withheld operator bytes fails loud.
#   5. WORKSPACE-CLOSED — the routine policy/helper/native-check inventory derives from owner bytes,
#      is content-addressed and runnable after materialization with no producer checkout.
#   6. FAILS LOUD — a tampered driver or workspace byte is DETECTED, never silently delivered.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd "$HERE/../.." && pwd)"
MANIFEST="$SRC_ROOT/registry/driver-skill-manifest.json"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
fail() { echo "verify-package: FAIL — $*" >&2; exit 1; }

# A scaffold consumes board drivers offline. Every relative Markdown target from a delivered
# `work-board*` skill must therefore resolve inside the delivered skills tree, not merely inside
# this producer checkout. Other driver families may intentionally target separately materialized
# process skills; this gate owns the board-family closure exposed by #2180.
board_link_closure_ok() {
  python3 - "$1" <<'PY'
import pathlib, re, sys, urllib.parse

root = pathlib.Path(sys.argv[1]).resolve()
failed = False
for source in sorted(root.rglob("*.md")):
    relative_source = source.relative_to(root)
    if not relative_source.parts[0].startswith("work-board"):
        continue
    text = source.read_text(encoding="utf-8-sig")
    destinations = [match.group(1) for match in re.finditer(r"!?\[[^\]]*\]\(([^)]+)\)", text)]
    destinations.extend(
        match.group(1) or match.group(2)
        for match in re.finditer(
            r"(?m)^[ \t]{0,3}\[[^\]\n]+\]:[ \t]*(?:<([^>\n]+)>|(\S+))",
            text,
        )
    )
    for destination in destinations:
        raw = destination.strip()
        if raw.startswith("<") and raw.endswith(">"):
            raw = raw[1:-1]
        raw = raw.split(maxsplit=1)[0]
        if not raw or raw.startswith(("#", "/", "//")) or re.match(r"^[A-Za-z][A-Za-z0-9+.-]*:", raw):
            continue
        relative = urllib.parse.unquote(raw.split("#", 1)[0].split("?", 1)[0])
        target = (source.parent / relative).resolve()
        try:
            target.relative_to(root)
        except ValueError:
            print(f"relative link escapes delivered skills: {source.relative_to(root)} -> {raw}", file=sys.stderr)
            failed = True
            continue
        if not target.exists():
            print(f"relative link target is not delivered: {source.relative_to(root)} -> {raw}", file=sys.stderr)
            failed = True
raise SystemExit(1 if failed else 0)
PY
}

[ -f "$MANIFEST" ] || fail "registry/driver-skill-manifest.json not found (is this a .github checkout?)"

# canonical_digest: BOM-stripped body sha256, byte-parity with generate-driver-manifest / stage-drivers.
# A tiny python helper keeps the digest correct even for a BOM'd body (sha256sum would not).
digest() { python3 - "$1" <<'PY'
import hashlib, sys
raw = open(sys.argv[1], "rb").read()
if raw.startswith(b"\xef\xbb\xbf"):
    raw = raw[3:]
print(hashlib.sha256(raw).hexdigest())
PY
}

# The delivered set the manifest declares: (id, sha256) for every `scope: driver` row, and the ids of
# every `scope: operator` row (which must carry NO bytes). Parsed once, in python, from the ONE source.
mapfile -t DRIVER_ROWS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1]))
for s in doc.get("skills", []):
    if s.get("scope") == "driver":
        print(f"{s['id']}\t{s['sha256']}")
PY
)
mapfile -t OPERATOR_IDS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1]))
for s in doc.get("skills", []):
    if s.get("scope") == "operator":
        print(s["id"])
PY
)
[ "${#DRIVER_ROWS[@]}" -gt 0 ] || fail "manifest declares no scope:driver rows — nothing to deliver"

echo "== 1. stage + derive parity (scope:driver rows staged & content-addressed; operator carries no bytes) =="
python3 "$HERE/stage-drivers.py" "$WORK/stage" >/dev/null
# The manifest is carried VERBATIM.
diff -q "$MANIFEST" "$WORK/stage/driver-skill-manifest.json" >/dev/null \
  || fail "staged driver-skill-manifest.json is not byte-identical to registry/driver-skill-manifest.json"
# Every driver row: staged, and its bytes match the recorded sha256.
for row in "${DRIVER_ROWS[@]}"; do
  id="${row%%$'\t'*}"; want="${row##*$'\t'}"
  f="$WORK/stage/skills/$id/SKILL.md"
  [ -f "$f" ] || fail "driver '$id' not staged (skills/$id/SKILL.md missing)"
  got="$(digest "$f")"
  [ "$got" = "$want" ] || fail "driver '$id' staged sha256 $got != manifest $want"
done
# Every v2 directory member is staged with its raw digest and executable mode.
while IFS=$'\t' read -r id rel want executable; do
  f="$WORK/stage/skills/$id/$rel"
  [ -f "$f" ] || fail "driver '$id' file not staged: $rel"
  [ "$(sha256sum "$f" | cut -d' ' -f1)" = "$want" ] || fail "driver '$id/$rel' raw digest mismatch"
  [ -x "$f" ] && got_exec=true || got_exec=false
  [ "$got_exec" = "$executable" ] || fail "driver '$id/$rel' executable=$got_exec != $executable"
done < <(jq -r '.skills[] | select(.scope == "driver" and (.files | type) == "array")
  | .id as $id | .files[] | [$id, .path, .sha256, (.executable | tostring)] | @tsv' "$MANIFEST")
# Every operator row: NOT staged (delivered nowhere, ADR-0057).
for id in "${OPERATOR_IDS[@]}"; do
  [ -n "$id" ] || continue
  [ ! -e "$WORK/stage/skills/$id" ] || fail "operator skill '$id' was staged — it must be delivered nowhere"
done
echo "   ${#DRIVER_ROWS[@]} driver skill(s) staged & content-addressed; ${#OPERATOR_IDS[@]} operator row(s) correctly withheld"
board_link_closure_ok "$WORK/stage/skills" \
  || fail "staged board-driver skills contain a relative link outside the delivered package closure"
echo "   staged board-driver links resolve using delivered bytes only"

echo "== 2. pack + content assert =="
dotnet pack "$HERE/FS.GG.Drivers.csproj" -c Release -o "$WORK/out" >/dev/null
nupkg="$(echo "$WORK"/out/FS.GG.Drivers.*.nupkg)"
[ -f "$nupkg" ] || fail "no nupkg produced"
entries="$(unzip -Z1 "$nupkg")"
for want in "build/FS.GG.Drivers.props" "README.md" "drivers/driver-skill-manifest.json" \
  "workspace/workspace-files.json"; do
  grep -qx "$want" <<<"$entries" || fail "nupkg is missing $want"
done
for row in "${DRIVER_ROWS[@]}"; do
  id="${row%%$'\t'*}"
  grep -qx "drivers/skills/$id/SKILL.md" <<<"$entries" || fail "nupkg is missing drivers/skills/$id/SKILL.md"
done
while IFS=$'\t' read -r id rel; do
  grep -qx "drivers/skills/$id/$rel" <<<"$entries" || fail "nupkg is missing drivers/skills/$id/$rel"
done < <(jq -r '.skills[] | select(.scope == "driver" and (.files | type) == "array")
  | .id as $id | .files[] | [$id, .path] | @tsv' "$MANIFEST")
echo "   nupkg carries the manifest + every driver SKILL.md + the consumer handle + README"

# The workspace manifest is computed directly from the bounded owner inventory. Assert its exact shape,
# order, raw digests, executable modes, and payload closure before exercising the installed form below.
python3 - "$HERE/workspace-inventory.json" "$SRC_ROOT" "$nupkg" <<'PY'
import hashlib, json, pathlib, stat, sys, zipfile

inventory_path, source_root, archive_path = map(pathlib.Path, sys.argv[1:])
inventory = json.loads(inventory_path.read_text(encoding="utf-8"))
with zipfile.ZipFile(archive_path) as archive:
    manifest = json.loads(archive.read("workspace/workspace-files.json"))
    assert manifest["schema"] == "fsgg/driver-workspace-files/v1"
    expected = []
    expected_entries = {"workspace/workspace-files.json"}
    for item in inventory["files"]:
        raw = (source_root / item["source"]).read_bytes()
        executable = bool((source_root / item["source"]).stat().st_mode & stat.S_IXUSR)
        expected.append({"path": item["path"], "sha256": hashlib.sha256(raw).hexdigest(), "executable": executable})
        entry = "workspace/files/" + item["path"]
        expected_entries.add(entry)
        assert archive.read(entry) == raw, f"workspace payload differs from owner source: {item['path']}"
    assert manifest["files"] == sorted(expected, key=lambda row: row["path"]), "workspace manifest drift"
    actual_entries = {name for name in archive.namelist() if name.startswith("workspace/") and not name.endswith("/")}
    assert actual_entries == expected_entries, f"workspace payload is not closed: {sorted(actual_entries ^ expected_entries)}"
PY
echo "   workspace manifest is sorted, closed, content-addressed, and derived from exact owner bytes"

# content_addressed_ok <drivers-dir>: returns 0 iff every `scope: driver` SKILL.md under
# <drivers-dir>/skills/ digests to its manifest sha256; non-zero (naming the first mismatch) otherwise.
# This IS the check the SDD CLI performs at scaffold time — the load-bearing content-addressed verify —
# so the gate both asserts it PASSES on the real package (step 3) and asserts it FIRES on a tampered byte
# (step 5). Asserting the digest merely "changed" would be tautological (any appended byte changes a
# sha256); asserting this function's VERDICT flips is what proves the verify.
content_addressed_ok() {
  local dir="$1" id rel want executable got got_exec
  while IFS=$'\t' read -r id rel want executable; do
    [ -f "$dir/skills/$id/$rel" ] || return 1
    got="$(sha256sum "$dir/skills/$id/$rel" | cut -d' ' -f1)" || return 1
    [ -x "$dir/skills/$id/$rel" ] && got_exec=true || got_exec=false
    [ "$got" = "$want" ] && [ "$got_exec" = "$executable" ] \
      || { echo "      content-address mismatch: $id/$rel sha256/mode differs from manifest" >&2; return 1; }
  done < <(jq -r '.skills[] | select(.scope == "driver")
    | .id as $id | (.files // [{path:"SKILL.md", sha256:.sha256, executable:false}])[]
    | [$id, .path, .sha256, (.executable | tostring)] | @tsv' "$dir/driver-skill-manifest.json")
  return 0
}

# workspace_content_addressed_ok <workspace-dir>: validate the exact installed manifest shape,
# path safety, closed file set, and raw digests. Executable modes are carried as manifest data because
# NuGet extraction normalizes archive modes; the materialization leg below applies and checks them.
# both the clean and corruption legs below call this same function.
workspace_content_addressed_ok() {
  python3 - "$1" <<'PY'
import hashlib, json, pathlib, re, sys

root = pathlib.Path(sys.argv[1])
try:
    manifest = json.loads((root / "workspace-files.json").read_text(encoding="utf-8"))
    assert list(manifest) == ["schema", "files"]
    assert manifest["schema"] == "fsgg/driver-workspace-files/v1"
    rows = manifest["files"]
    assert isinstance(rows, list) and rows
    paths = [row["path"] for row in rows]
    assert paths == sorted(paths) and len(paths) == len(set(paths))
    expected = set()
    for row in rows:
        assert list(row) == ["path", "sha256", "executable"]
        path = row["path"]
        assert isinstance(path, str) and path and "\\" not in path and not path.startswith(("/", "./"))
        assert ".." not in path.split("/") and "//" not in path
        assert isinstance(row["executable"], bool)
        assert isinstance(row["sha256"], str) and re.fullmatch(r"[0-9a-f]{64}", row["sha256"])
        source = root / "files" / path
        raw = source.read_bytes()
        assert hashlib.sha256(raw).hexdigest() == row["sha256"]
        expected.add(source.relative_to(root).as_posix())
    actual = {p.relative_to(root).as_posix() for p in (root / "files").rglob("*") if p.is_file()}
    assert actual == expected
except (AssertionError, OSError, ValueError, KeyError, TypeError, json.JSONDecodeError):
    raise SystemExit(1)
PY
}

echo "== 3. content-addressed: every packed byte matches its manifest sha256 =="
unzip -q "$nupkg" "drivers/*" -d "$WORK/unpacked"
# The packed manifest is byte-identical to the committed one.
diff -q "$MANIFEST" "$WORK/unpacked/drivers/driver-skill-manifest.json" >/dev/null \
  || fail "packed drivers/driver-skill-manifest.json is not byte-identical to the committed manifest"
content_addressed_ok "$WORK/unpacked/drivers" \
  || fail "a packed driver SKILL.md does not match its manifest sha256 (the ADR-0014 record)"
echo "   every packed driver SKILL.md verifies against the manifest — the ADR-0014 record the CLI uses"

echo "== 4. consumer materialization is link-closed, and the closure gate fails loud =="
mkdir -p "$WORK/consumer/.agents"
cp -r "$WORK/unpacked/drivers/skills" "$WORK/consumer/.agents/skills"
board_link_closure_ok "$WORK/consumer/.agents/skills" \
  || fail "consumer-materialized board-driver skills contain a dead relative link"
printf '%s\n' '[broken](../withheld-operator/SKILL.md)' > "$WORK/consumer/.agents/skills/work-board-normal/broken-link.md"
if board_link_closure_ok "$WORK/consumer/.agents/skills" >/dev/null 2>&1; then
  fail "consumer closure gate accepted a relative link to withheld bytes"
fi
printf '%s\n' '[broken][operator]' '' '[operator]: ../withheld-operator/SKILL.md' \
  > "$WORK/consumer/.agents/skills/work-board-normal/broken-link.md"
if board_link_closure_ok "$WORK/consumer/.agents/skills" >/dev/null 2>&1; then
  fail "consumer closure gate accepted a reference-style relative link to withheld bytes"
fi
rm "$WORK/consumer/.agents/skills/work-board-normal/broken-link.md"
echo "   consumer-shaped materialization is closed; inline and reference-style withheld-sibling links are rejected"

echo "== 5. installed workspace helper, policy validator and native-check closure work without source checkout =="
unzip -q "$nupkg" "workspace/*" -d "$WORK/installed-package"
workspace_content_addressed_ok "$WORK/installed-package/workspace" \
  || fail "clean installed workspace payload does not satisfy its receiver contract"
python3 - "$WORK/installed-package/workspace" "$WORK/receiver" <<'PY'
import hashlib, json, pathlib, sys

package, receiver = map(pathlib.Path, sys.argv[1:])
manifest = json.loads((package / "workspace-files.json").read_text(encoding="utf-8"))
assert manifest["schema"] == "fsgg/driver-workspace-files/v1"
for item in manifest["files"]:
    source = package / "files" / item["path"]
    raw = source.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == item["sha256"]
    target = receiver / item["path"]
    assert not target.exists(), f"no-clobber precondition violated: {item['path']}"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(raw)
    target.chmod(0o755 if item["executable"] else 0o644)
    assert bool(target.stat().st_mode & 0o111) == item["executable"]
PY

git -C "$WORK/receiver" init -q -b main
git -C "$WORK/receiver" config user.name fixture
git -C "$WORK/receiver" config user.email fixture@example.invalid
git -C "$WORK/receiver" add .
git -C "$WORK/receiver" commit -qm base
base="$(git -C "$WORK/receiver" rev-parse HEAD)"
printf '%s\n' changed > "$WORK/receiver/change.txt"
git -C "$WORK/receiver" add change.txt
git -C "$WORK/receiver" commit -qm candidate
head="$(git -C "$WORK/receiver" rev-parse HEAD)"
printf '<!-- fsgg:routine-development/v1 head=%s operation=source-change -->\n' "$head" > "$WORK/receiver/pr-body.md"
python3 "$WORK/receiver/scripts/check-routine-eligibility-envelope.py" \
  --git-dir "$WORK/receiver" --repository fixture/receiver --default-branch main --base-ref main \
  --base-revision "$base" --base-sha "$base" --head-ref routine/fixture --head-revision "$head" \
  --head-sha "$head" --body "$WORK/receiver/pr-body.md" \
  --validator "$WORK/receiver/scripts/check-claim-generation.py" >/dev/null
mkdir -p "$WORK/fake-bin"
printf '%s\n' '#!/usr/bin/env python3' \
  'import json' \
  'print(json.dumps({"state":"open","draft":False,"merged":False,"mergeable":True,"mergeable_state":"clean","head":{"sha":"'"$head"'"},"base":{"ref":"main","sha":"'"$base"'"}}))' \
  > "$WORK/fake-bin/gh"
chmod +x "$WORK/fake-bin/gh"
XDG_CONFIG_HOME="$WORK/no-host-config" PATH="$WORK/fake-bin:$PATH" \
  python3 "$WORK/receiver/tools/routine-delivery.py" \
    --repo fixture/receiver --pr 1 --head "$head" \
    --telemetry-feature SVG-WORKSPACE-01 --telemetry-item SVG-WORKSPACE-01.2 \
    --telemetry-attempt installed-package-fixture \
    > "$WORK/routine-delivery.json" 2> "$WORK/routine-delivery.stderr"
grep -Fq '"outcome":"ready"' "$WORK/routine-delivery.json" \
  || fail "installed routine-delivery helper did not complete a stubbed native dry run"
grep -Fq '"telemetryHealth":"not-configured"' "$WORK/routine-delivery.json" \
  || fail "installed routine-delivery helper did not load its sibling telemetry dependency"
for referenced in scripts/check-claim-generation.py scripts/check-routine-eligibility-envelope.py scripts/lib/gate.py; do
  grep -Fq "$referenced" "$WORK/receiver/.github/workflows/routine-eligibility.yml" \
    || fail "installed native workflow does not reference $referenced"
done
echo "   clean receiver imports the helper closure and admits an exact-head routine source change"

echo "== 6. tampered driver and workspace bytes are REJECTED (fail-loud) =="
cp -r "$WORK/unpacked/drivers" "$WORK/tampered"
first_id="${DRIVER_ROWS[0]%%$'\t'*}"
echo "CORRUPT" >> "$WORK/tampered/skills/$first_id/SKILL.md"     # bytes drift from the recorded sha256
if content_addressed_ok "$WORK/tampered"; then
  fail "the content-addressed verify PASSED against a tampered '$first_id' — it is not firing"
fi
echo "   tampered driver '$first_id' rejected by the content-addressed verify, as required"
first_workspace_path="$(jq -r '.files[0].path' "$WORK/installed-package/workspace/workspace-files.json")"
echo CORRUPT >> "$WORK/installed-package/workspace/files/$first_workspace_path"
if workspace_content_addressed_ok "$WORK/installed-package/workspace"; then
  fail "the installed workspace verifier PASSED a tampered '$first_workspace_path'"
fi
echo "   tampered workspace byte is rejected by the installed receiver verification path"

echo "verify-package: OK"
