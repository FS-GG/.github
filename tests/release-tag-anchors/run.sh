#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; DETECTOR="$ROOT/scripts/check-release-tag-anchors.py"
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
pass=0; fail=0
ok(){ echo "PASS  $1"; pass=$((pass+1)); }; bad(){ echo "FAIL  $1"; fail=$((fail+1)); }
fixture(){ printf '%b\n' "$1" > "$WORK/fleet.tsv"; }
fixture $'FS-GG/.github\tkit/v\tFS.GG.Kit\t1.0.0\ta\ta'
set +e; out="$(python3 "$DETECTOR" --fixture "$WORK/fleet.tsv" 2>&1)"; rc=$?; set -e
[ "$rc" -eq 1 ] && grep -q 'AGREE.*kit/v1.0.0' <<<"$out" && grep -q '^UNCOVERED' <<<"$out" && ok "agree is distinct and absent roster mappings are UNCOVERED" || bad "agree/uncovered"
for row in 'a,b' 'a,-' '-,b'; do
  anchor="${row%,*}"; tag="${row#*,}"
  fixture "FS-GG/.github\tkit/v\tFS.GG.Kit\t1.0.0-preview.1\t$anchor\t$tag"
  set +e; out="$(python3 "$DETECTOR" --fixture "$WORK/fleet.tsv" 2>&1)"; rc=$?; set -e
  [ "$rc" -eq 1 ] && grep -Eq 'DISAGREE|MISSING|UNRESOLVED' <<<"$out" && ok "non-agree verdict remains explicit" || bad "four verdicts"
done
fixture $'FS-GG/.github\tkit/v\tFS.GG.Kit\t1.0.0\ta\ta'
set +e; out="$(python3 "$DETECTOR" --fixture "$WORK/fleet.tsv" 2>&1)"; rc=$?; set -e
[ "$rc" -eq 1 ] && grep -q 'FS.GG.Rendering.*fs-gg-ui-template.*None' <<<"$out" && ok "declared package=None fleet mapping is emitted UNCOVERED" || bad "explicit uncovered"
grep -q 'FS.GG.Rendering.*fs-gg-ui-template' "$DETECTOR" && grep -q 'FS.GG.SDD.*FS.GG.Contracts' "$DETECTOR" && ok "Rendering mappings and full SDD sweep are tabled" || bad "fleet table"
grep -q '94b044b1e575fc9da0105c32bd063b0f387a5eef' "$DETECTOR" && grep -q '775a11eec882e2184ea9a18a5f759bb54a9ba143' "$DETECTOR" && ok "historical dual-publish commits are retained, never normalized" || bad "historical disagreements"
# Both WASM packages must join the same independent immutable tag.
fixture $'FS-GG/FS.GG.Game\twasm/v\tFS.GG.Wasm.Contracts\t0.1.1\tea015cbf884b01754bc6615476241450907b6b24\tea015cbf884b01754bc6615476241450907b6b24\nFS-GG/FS.GG.Game\twasm/v\tFS.GG.Wasm.Browser\t0.1.1\tea015cbf884b01754bc6615476241450907b6b24\tea015cbf884b01754bc6615476241450907b6b24'
set +e; out="$(python3 "$DETECTOR" --fixture "$WORK/fleet.tsv" 2>&1)"; rc=$?; set -e
[ "$rc" -eq 1 ] && grep -q '^AGREE.*wasm/v0.1.1.*FS.GG.Wasm.Contracts' <<<"$out" && grep -q '^AGREE.*wasm/v0.1.1.*FS.GG.Wasm.Browser' <<<"$out" && ! grep -q '^UNCOVERED.*FS.GG.Wasm' <<<"$out" && ok "both WASM members join wasm/v0.1.1 independently of uncovered fleet history" || bad "WASM anchor coverage"
fixture $'FS-GG/FS.GG.Game\twasm/v\tFS.GG.Wasm.Contracts\t0.1.1\ta\ta'
set +e; out="$(python3 "$DETECTOR" --fixture "$WORK/fleet.tsv" 2>&1)"; rc=$?; set -e
[ "$rc" -eq 1 ] && grep -q '^UNCOVERED.*wasm/v.*FS.GG.Wasm.Browser' <<<"$out" && ok "missing Browser anchor is explicitly uncovered" || bad "partial WASM anchor"
fixture $'FS-GG/FS.GG.Game\twasm/v\tFS.GG.Wasm.Browser\t0.1.1\ta\tb'
set +e; out="$(python3 "$DETECTOR" --fixture "$WORK/fleet.tsv" 2>&1)"; rc=$?; set -e
[ "$rc" -eq 1 ] && grep -q '^DISAGREE.*wasm/v0.1.1.*FS.GG.Wasm.Browser' <<<"$out" && ok "Browser source/tag disagreement refuses" || bad "WASM anchor disagreement"
echo "release-tag-anchors fixture — $pass passed, $fail failed"; [ "$fail" -eq 0 ]
