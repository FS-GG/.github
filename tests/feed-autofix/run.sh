#!/usr/bin/env bash
# Offline fixture for scripts/feed-autofix (.github#748, epic #266).
#
# The response half WRITES the registry, so it is the one artifact in this pair that can do damage:
# a detection gate that is wrong reports a false colour, but a reconcile that is wrong LANDS a false
# fact — and the registry is the org's record of what is restorable. So the bar is the one
# tests/feed-coherence sets for the detection half and then some: prove it goes red on every drift
# and fail-open condition BEFORE trusting a green, and prove the writes it does make are exactly the
# bytes intended and nothing else.
#
# Every case is hermetic: the feed and the tag store are canned JSON (--fixture), so no network, no
# token, and no dependence on what the org happens to have published today. A case that needed the
# live feed would be a test whose verdict changes when nobody changed the code.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
BOT="$ROOT/scripts/feed-autofix"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

export FSGG_FEED_FIXTURE_OK=1

pass=0; fail=0
ok()   { printf '  ok   %s\n' "$1"; pass=$((pass+1)); }
bad()  { printf '  FAIL %s\n     %s\n' "$1" "$2"; fail=$((fail+1)); }

# The 17 coherent-set members, mirroring the bot's FRAMEWORK_MEMBERS. Written out HERE rather than
# imported from the bot, deliberately: a fixture that derived its expectations from the code under
# test would ratify any edit to that list, including a wrong one. This is the independent copy that
# makes such an edit show up as a failure.
#
# So it must actually BE the fixture's source of truth. It is passed to build_feed below; there is
# no second copy. (There WAS, in review: this list sat unused beside a hardcoded duplicate inside
# build_feed's heredoc, so the comment above described a protection that did not exist — a lying
# comment in the fixture for a bot whose entire thesis is that prose rots silently.)
MEMBERS='FS.GG.UI FS.GG.UI.Build FS.GG.UI.Canvas FS.GG.UI.Controls FS.GG.UI.Controls.Elmish
FS.GG.UI.DesignSystem FS.GG.UI.Diagnostics FS.GG.UI.Elmish FS.GG.UI.KeyboardInput FS.GG.UI.Layout
FS.GG.UI.Scene FS.GG.UI.SkiaViewer FS.GG.UI.Symbology FS.GG.UI.Symbology.Render FS.GG.UI.Testing
FS.GG.UI.Themes.AntDesign FS.GG.UI.Themes.Default'

# build_feed <member-version> <template-versions...> -> JSON feed object
build_feed() {
  local mv="$1"; shift
  python3 - "$MEMBERS" "$mv" "$*" <<'PY'
import json, sys
members, mv, tmpl = sys.argv[1].split(), sys.argv[2], sys.argv[3].split()
feed = {m: [mv] for m in members}
feed["FS.GG.UI.Template"] = tmpl
print(json.dumps(feed))
PY
}

# The fixture's own guard: if the bot's list and this one ever diverge, every case below would be
# testing a feed that does not match the code, and would do it QUIETLY — the bot would just report
# whichever members the fixture happened to serve. Assert they are the same set, and name the delta.
python3 - "$MEMBERS" "$ROOT/scripts/feed-autofix" <<'PY' || exit 1
import re, sys
mine = set(sys.argv[1].split())
src = open(sys.argv[2], encoding="utf-8").read()
block = re.search(r"FRAMEWORK_MEMBERS: list\[str\] = \[(.*?)\]", src, re.S)
if not block:
    sys.exit("fixture: cannot find FRAMEWORK_MEMBERS in the bot — it was renamed.")
theirs = set(re.findall(r'"([^"]+)"', block.group(1)))
if mine != theirs:
    sys.exit(
        "fixture: the member list has DRIFTED from the bot's FRAMEWORK_MEMBERS.\n"
        f"  only in the fixture: {sorted(mine - theirs) or '-'}\n"
        f"  only in the bot:     {sorted(theirs - mine) or '-'}\n"
        "Reconcile them: every case below serves a feed built from the fixture's list."
    )
PY

# A registry stub carrying the row's real SHAPE — quoted values plus a trailing provenance comment,
# because preserving that comment is half of what the write must get right.
registry() {
  cat > "$1" <<YAML
contracts:
  - id: fsgg-contracts
    version: "2.0.1"
    package-version: "2.0.1"
  - id: fs-gg-ui-template
    version: "$2"   # template FsGgUiVersion FRAMEWORK pin — ADVANCED $3 (human provenance here)
    package-version: "$4"   # PUBLISHED package — ADVANCED $5. Judgement prose nobody may rewrite.
    package-tag: "$6"   # template-scoped coherent-set tag. PRIOR — ADVANCED 0.1.0 -> 0.2.0 history.
  - id: game-sim-core
    version: "0.5.0"
    package-version: "0.5.0"
YAML
}

fixture() { printf '{"feed": %s, "tags": %s}' "$1" "$2" > "$3"; }

TRIPLE_11='{"fs-gg-ui/v0.11.0":"dddddddd11","fs-gg-ui-template/v0.11.0":"dddddddd11","v0.11.0":"dddddddd11"}'

# --------------------------------------------------------------------------------------------
# 1. FRAMEWORK release — every member moved, so the pin moves WITH the package.
# --------------------------------------------------------------------------------------------
registry "$WORK/r1.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
fixture "$(build_feed 0.11.0 0.10.0 0.11.0)" "$TRIPLE_11" "$WORK/f1.json"
out="$(python3 "$BOT" "$WORK/r1.yml" --fixture "$WORK/f1.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -ne 0 ]; then bad "framework: exits 0" "rc=$rc: $out"; else ok "framework: exits 0"; fi
if grep -q 'version: "0.11.0"' "$WORK/r1.yml" && grep -q 'package-version: "0.11.0"' "$WORK/r1.yml" \
   && grep -q 'package-tag: "fs-gg-ui-template/v0.11.0"' "$WORK/r1.yml"; then
  ok "framework: all three fields advanced"
else
  bad "framework: all three fields advanced" "$(grep -E 'version|package-tag' "$WORK/r1.yml")"
fi
# The whole reason the write is a text substitution and not a YAML dump.
if grep -q "human provenance here" "$WORK/r1.yml" \
   && grep -q "Judgement prose nobody may rewrite" "$WORK/r1.yml" \
   && grep -q "PRIOR — ADVANCED 0.1.0 -> 0.2.0 history" "$WORK/r1.yml"; then
  ok "framework: every provenance comment survives the write"
else
  bad "framework: every provenance comment survives the write" "a comment was destroyed"
fi
# Neighbouring rows must be untouched — the write is anchored to one block.
if grep -q 'id: fsgg-contracts' "$WORK/r1.yml" && grep -q 'version: "2.0.1"' "$WORK/r1.yml" \
   && grep -q 'version: "0.5.0"' "$WORK/r1.yml"; then
  ok "framework: neighbouring contract rows untouched"
else
  bad "framework: neighbouring contract rows untouched" "an adjacent row moved"
fi

# --------------------------------------------------------------------------------------------
# 2. TEMPLATE-ONLY cut — no member moved, so the pin HOLDS and the two fields decouple.
#    The 0.3.1-preview.1 / 0.4.1 shape. Getting this wrong writes a pin nothing can restore.
# --------------------------------------------------------------------------------------------
registry "$WORK/r2.yml" "0.11.0" "0.10.0 -> 0.11.0" "0.11.0" "0.10.0 -> 0.11.0" "fs-gg-ui-template/v0.11.0"
PAIR_12='{"fs-gg-ui-template/v0.11.1":"eeeeeeee12","v0.11.1":"eeeeeeee12"}'
fixture "$(build_feed 0.11.0 0.11.0 0.11.1)" "$PAIR_12" "$WORK/f2.json"
out="$(python3 "$BOT" "$WORK/r2.yml" --fixture "$WORK/f2.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && echo "$out" | grep -q '"kind": "template-only"'; then
  ok "template-only: classified as template-only"
else
  bad "template-only: classified as template-only" "rc=$rc: $out"
fi
if grep -q 'version: "0.11.0"' "$WORK/r2.yml" && grep -q 'package-version: "0.11.1"' "$WORK/r2.yml"; then
  ok "template-only: the pin HOLDS while the package advances"
else
  bad "template-only: the pin HOLDS while the package advances" "$(grep -E 'version:' "$WORK/r2.yml")"
fi
# A template-only cut owes NO fs-gg-ui/v* library snapshot tag; demanding one would red every one.
if ! echo "$out" | grep -qi "fs-gg-ui/v0.11.1"; then
  ok "template-only: no library snapshot tag demanded"
else
  bad "template-only: no library snapshot tag demanded" "$out"
fi

# --------------------------------------------------------------------------------------------
# 3. PARTIAL publish — neither shape. The bot must REFUSE, not average.
# --------------------------------------------------------------------------------------------
registry "$WORK/r3.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
partial="$(build_feed 0.11.0 0.10.0 0.11.0 | python3 -c \
  'import json,sys; f=json.load(sys.stdin); f["FS.GG.UI.Canvas"]=["0.10.0"]; print(json.dumps(f))')"
fixture "$partial" "$TRIPLE_11" "$WORK/f3.json"
before="$(cat "$WORK/r3.yml")"
out="$(python3 "$BOT" "$WORK/r3.yml" --fixture "$WORK/f3.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "PARTIAL PUBLISH"; then
  ok "partial publish: refused as a finding (exit 1)"
else
  bad "partial publish: refused as a finding (exit 1)" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r3.yml")" ]; then
  ok "partial publish: registry left BYTE-IDENTICAL"
else
  bad "partial publish: registry left BYTE-IDENTICAL" "the bot wrote on a refusal"
fi

# --------------------------------------------------------------------------------------------
# 4. THE 0.9.1 PHANTOM — a tag with no package, and its inverse, a package with no tag.
#    `package-tag` is the field that carries this lie if nobody checks it.
# --------------------------------------------------------------------------------------------
registry "$WORK/r4.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
fixture "$(build_feed 0.11.0 0.10.0 0.11.0)" '{}' "$WORK/f4.json"
before="$(cat "$WORK/r4.yml")"
out="$(python3 "$BOT" "$WORK/r4.yml" --fixture "$WORK/f4.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "has no tag"; then
  ok "package with no tag: refused (exit 1)"
else
  bad "package with no tag: refused (exit 1)" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r4.yml")" ]; then
  ok "package with no tag: registry untouched"
else
  bad "package with no tag: registry untouched" "the bot wrote on a refusal"
fi

# The 0.9.1 PHANTOM proper: a LATER version exists as a full tag triple and is served by NO feed.
# The row is otherwise coherent at 0.10.0. The bot must stay at 0.10.0 and never reach for the
# phantom — it flips only to what the FEED serves, so a tag it can see but not restore is inert.
# (The registry is staged coherent, tags and all, so the ONLY thing under test is the phantom.)
registry "$WORK/r4b.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
phantom='{"fs-gg-ui/v0.10.0":"aaaaaaaa10","fs-gg-ui-template/v0.10.0":"aaaaaaaa10","v0.10.0":"aaaaaaaa10",
          "fs-gg-ui/v0.99.0":"ffffffff99","fs-gg-ui-template/v0.99.0":"ffffffff99","v0.99.0":"ffffffff99"}'
fixture "$(build_feed 0.10.0 0.9.2 0.10.0)" "$phantom" "$WORK/f4b.json"
out="$(python3 "$BOT" "$WORK/r4b.yml" --fixture "$WORK/f4b.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && echo "$out" | grep -q '"changed": false' && ! grep -q "0.99.0" "$WORK/r4b.yml"; then
  ok "phantom tags: a full tag triple with no package never becomes a candidate"
else
  bad "phantom tags: a full tag triple with no package never becomes a candidate" "rc=$rc: $out"
fi

# An INCOMPLETE triple — the library snapshot tag missing on a framework release.
registry "$WORK/r4c.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
fixture "$(build_feed 0.11.0 0.10.0 0.11.0)" \
  '{"fs-gg-ui-template/v0.11.0":"dddddddd11","v0.11.0":"dddddddd11"}' "$WORK/f4c.json"
out="$(python3 "$BOT" "$WORK/r4c.yml" --fixture "$WORK/f4c.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "does not exist"; then
  ok "framework with an incomplete tag triple: refused (exit 1)"
else
  bad "framework with an incomplete tag triple: refused (exit 1)" "rc=$rc: $out"
fi

# A triple that resolves to THREE commits — the tags disagree about what was released.
registry "$WORK/r4d.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
fixture "$(build_feed 0.11.0 0.10.0 0.11.0)" \
  '{"fs-gg-ui/v0.11.0":"aaaaaaaa01","fs-gg-ui-template/v0.11.0":"bbbbbbbb02","v0.11.0":"cccccccc03"}' \
  "$WORK/f4d.json"
out="$(python3 "$BOT" "$WORK/r4d.yml" --fixture "$WORK/f4d.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "ONE commit"; then
  ok "tag triple spanning three commits: refused (exit 1)"
else
  bad "tag triple spanning three commits: refused (exit 1)" "rc=$rc: $out"
fi

# --------------------------------------------------------------------------------------------
# 5. THE FR-007 INVERSION — the registry AHEAD of the feed. A bot must never wind it back.
# --------------------------------------------------------------------------------------------
registry "$WORK/r5.yml" "0.11.0" "0.10.0 -> 0.11.0" "0.11.0" "0.10.0 -> 0.11.0" "fs-gg-ui-template/v0.11.0"
fixture "$(build_feed 0.10.0 0.9.2 0.10.0)" "$TRIPLE_11" "$WORK/f5.json"
before="$(cat "$WORK/r5.yml")"
out="$(python3 "$BOT" "$WORK/r5.yml" --fixture "$WORK/f5.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "AHEAD of the feed"; then
  ok "registry ahead of the feed: refused (exit 1)"
else
  bad "registry ahead of the feed: refused (exit 1)" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r5.yml")" ]; then
  ok "registry ahead of the feed: NOT wound back"
else
  bad "registry ahead of the feed: NOT wound back" "the bot rewrote history"
fi

# --------------------------------------------------------------------------------------------
# 6. ALREADY COHERENT — the everyday case. No diff, no PR, no noise.
# --------------------------------------------------------------------------------------------
registry "$WORK/r6.yml" "0.11.0" "0.10.0 -> 0.11.0" "0.11.0" "0.10.0 -> 0.11.0" "fs-gg-ui-template/v0.11.0"
fixture "$(build_feed 0.11.0 0.10.0 0.11.0)" "$TRIPLE_11" "$WORK/f6.json"
before="$(cat "$WORK/r6.yml")"
out="$(python3 "$BOT" "$WORK/r6.yml" --fixture "$WORK/f6.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && echo "$out" | grep -q '"changed": false'; then
  ok "already coherent: changed=false"
else
  bad "already coherent: changed=false" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r6.yml")" ]; then
  ok "already coherent: file untouched"
else
  bad "already coherent: file untouched" "the bot rewrote an unchanged file"
fi

# --------------------------------------------------------------------------------------------
# 7. STALE PROSE — the #856 defect. The bot must REPORT it and must NOT rewrite it.
# --------------------------------------------------------------------------------------------
# #856 exactly: package-version's value moved to 0.11.0, its comment still says 0.9.2 -> 0.10.0.
registry "$WORK/r7.yml" "0.11.0" "0.10.0 -> 0.11.0" "0.11.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.11.0"
fixture "$(build_feed 0.11.0 0.10.0 0.11.0)" "$TRIPLE_11" "$WORK/f7.json"
out="$(python3 "$BOT" "$WORK/r7.yml" --fixture "$WORK/f7.json" --write --json 2>&1)"; rc=$?
if echo "$out" | grep -q "still describes"; then
  ok "stale prose: reported"
else
  bad "stale prose: reported" "rc=$rc: $out"
fi
if grep -q "ADVANCED 0.9.2 -> 0.10.0" "$WORK/r7.yml"; then
  ok "stale prose: NOT rewritten (judgement stays human)"
else
  bad "stale prose: NOT rewritten (judgement stays human)" "the bot edited prose it may not write"
fi
# ...and prose that AGREES with its value must not be flagged, or every run cries wolf.
registry "$WORK/r7b.yml" "0.11.0" "0.10.0 -> 0.11.0" "0.11.0" "0.10.0 -> 0.11.0" "fs-gg-ui-template/v0.11.0"
out="$(python3 "$BOT" "$WORK/r7b.yml" --fixture "$WORK/f7.json" --write --json 2>&1)"
if echo "$out" | grep -q '"stale_prose": \[\]'; then
  ok "coherent prose: not flagged"
else
  bad "coherent prose: not flagged" "$out"
fi

# --------------------------------------------------------------------------------------------
# 8. FAIL CLOSED. "Nothing to check" and "checked, and it's fine" must not share an exit code.
# --------------------------------------------------------------------------------------------
# The fixture hook must be unreachable without the env guard, or the bot can be silenced.
unset FSGG_FEED_FIXTURE_OK
out="$(python3 "$BOT" "$WORK/r6.yml" --fixture "$WORK/f6.json" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ] && echo "$out" | grep -q "FSGG_FEED_FIXTURE_OK"; then
  ok "fixture hook refuses without FSGG_FEED_FIXTURE_OK=1"
else
  bad "fixture hook refuses without FSGG_FEED_FIXTURE_OK=1" "rc=$rc: $out"
fi
export FSGG_FEED_FIXTURE_OK=1

# A live run with no token must refuse, not report "coherent" from a feed it never read.
out="$(env -u GITHUB_TOKEN python3 "$BOT" "$WORK/r6.yml" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ] && echo "$out" | grep -q "GITHUB_TOKEN"; then
  ok "no token: refuses rather than reporting green"
else
  bad "no token: refuses rather than reporting green" "rc=$rc: $out"
fi

# A feed that serves a member the fixture does not know is an ERROR, never an empty list.
registry "$WORK/r8.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
fixture '{"FS.GG.UI.Template": ["0.11.0"]}' "$TRIPLE_11" "$WORK/f8.json"
out="$(python3 "$BOT" "$WORK/r8.yml" --fixture "$WORK/f8.json" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ]; then
  ok "unreadable member: exits 2 rather than classifying on partial data"
else
  bad "unreadable member: exits 2 rather than classifying on partial data" "rc=$rc: $out"
fi

# A vanished row must not read as "nothing to do".
cat > "$WORK/r9.yml" <<'YAML'
contracts:
  - id: game-sim-core
    version: "0.5.0"
YAML
out="$(python3 "$BOT" "$WORK/r9.yml" --fixture "$WORK/f6.json" 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "renamed or removed"; then
  ok "vanished row: reported as a finding, not silence"
else
  bad "vanished row: reported as a finding, not silence" "rc=$rc: $out"
fi

# A member with NO stable version, while the registry tracks the stable channel. Must be a FINDING,
# not a silent fall-back to comparing against the prereleases — check-feed-coherence.py raises on
# this exact condition, and two gates reading one feed must not disagree about what it says.
registry "$WORK/r11.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
prerel="$(build_feed 0.11.0 0.10.0 0.11.0 | python3 -c \
  'import json,sys; f=json.load(sys.stdin); f["FS.GG.UI.Scene"]=["0.11.0-preview.1"]; print(json.dumps(f))')"
fixture "$prerel" "$TRIPLE_11" "$WORK/f11.json"
before="$(cat "$WORK/r11.yml")"
out="$(python3 "$BOT" "$WORK/r11.yml" --fixture "$WORK/f11.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "no stable version of FS.GG.UI.Scene"; then
  ok "member with only prereleases: refused as a finding"
else
  bad "member with only prereleases: refused as a finding" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r11.yml")" ]; then
  ok "member with only prereleases: registry untouched"
else
  bad "member with only prereleases: registry untouched" "the bot wrote on a refusal"
fi

# A MISSING field and a MIS-TYPED one need different remedies, so they must not share a message:
# telling a human to "quote" a key that is not there sends them after a bug that does not exist.
cat > "$WORK/r12.yml" <<'YAML'
contracts:
  - id: fs-gg-ui-template
    version: "0.10.0"
    package-version: "0.10.0"
YAML
out="$(python3 "$BOT" "$WORK/r12.yml" --fixture "$WORK/f6.json" 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "has no .package-tag."; then
  ok "missing field: reported as MISSING, not as a quoting bug"
else
  bad "missing field: reported as MISSING, not as a quoting bug" "rc=$rc: $out"
fi

# An unquoted version is a float — the .github#267 trap. A bot must not write one back.
cat > "$WORK/r10.yml" <<'YAML'
contracts:
  - id: fs-gg-ui-template
    version: 1.10
    package-version: "0.11.0"
    package-tag: "fs-gg-ui-template/v0.11.0"
YAML
out="$(python3 "$BOT" "$WORK/r10.yml" --fixture "$WORK/f6.json" 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "not a quoted string"; then
  ok "unquoted version: refused as a finding"
else
  bad "unquoted version: refused as a finding" "rc=$rc: $out"
fi

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
