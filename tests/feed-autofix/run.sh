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

# The 17 coherent-set members. The bot no longer carries this list — it DERIVES the set from the
# FS.GG.UI BOM's nuspec (.github#933) — so this is no longer a mirror of a literal in the code. It is
# now the independent EXPECTATION the derivation is pinned against, which is a stronger thing to be:
# a fixture that derived its expectations from the code under test would ratify any change to that
# set, including a wrong one.
#
# It is the fixture's source of truth twice over, and there is no second copy of it:
#   - build_feed serves a feed built from it, and nuspecs_for derives the BOM's declared deps from
#     it, so every case below exercises the real derivation against this set;
#   - the recorded-nuspec case asserts the REAL FS.GG.UI@0.11.0 package declares exactly this set,
#     which is what stops this list and reality drifting apart in silence.
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

# THE RECORDED-NUSPEC PIN (.github#933). fixtures/FS.GG.UI.0.11.0.nuspec is the real package's real
# nuspec, fetched byte-for-byte off the org feed. Drive the bot's OWN derivation over it and assert
# it reproduces exactly the 17 above.
#
# This replaces a guard that grepped FRAMEWORK_MEMBERS out of the bot, which the derivation deleted.
# It is a better guard than the one it replaces, because it pins the derivation to REALITY rather
# than to another literal: the old one could only catch this file and the bot disagreeing, and would
# have reported a contented green while both drifted away from what the feed actually serves.
#
# It runs `nuspec_dependency_ids` — the shipped parser — not a re-implementation of it, so a parser
# that silently drops a dependency is a red here rather than a quietly shrunken coherent set.
python3 - "$MEMBERS" "$ROOT/scripts" "$HERE/fixtures/FS.GG.UI.0.11.0.nuspec" <<'PY' || exit 1
import sys
expected = set(sys.argv[1].split())
sys.path.insert(0, sys.argv[2])
from fsgg_feed import nuspec_dependency_ids

xml = open(sys.argv[3], encoding="utf-8-sig").read()
derived = set(nuspec_dependency_ids(xml)) | {"FS.GG.UI"}
if derived != expected:
    sys.exit(
        "fixture: the set derived from the RECORDED FS.GG.UI@0.11.0 nuspec no longer matches this\n"
        "fixture's expected members.\n"
        f"  only in the recorded nuspec: {sorted(derived - expected) or '-'}\n"
        f"  only in the fixture's list:  {sorted(expected - derived) or '-'}\n"
        "If FS.GG.Rendering really did change the coherent set, re-record the fixture and update\n"
        "MEMBERS. If it did not, the parser or the derivation has regressed."
    )
if "FS.GG.UI.Template" in derived:
    sys.exit(
        "fixture: the BOM now declares FS.GG.UI.Template as a member. The template is the SUBJECT "
        "the bot classifies, so counting it as evidence would let a template-only cut confirm "
        "itself as a framework release."
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

# fixture <feed-json> <tags-json> <path>
#
# Serves a nuspec for EVERY FS.GG.UI version the feed carries, synthesised from MEMBERS — so the
# canned BOM always declares the same set the canned feed serves, and the cases below keep testing
# what they were written to test rather than a BOM/feed mismatch of the fixture's own making.
#
# It serves nuspec XML, not a ready-made id list, because the bot parses it with the shipped
# `nuspec_dependency_ids`. A fixture that handed back a parsed set would route every case around the
# parser and leave it exercised only by the recorded-nuspec pin above.
#
# Deriving these from the feed rather than taking them as a 4th argument is deliberate: a feed
# without an FS.GG.UI entry then serves no BOM nuspec either, which is exactly what the live feed
# does when the BOM is missing — so the "unreadable member" case still gets the failure it asserts.
fixture() {
  python3 - "$1" "$2" "$MEMBERS" "$3" <<'PY'
import json, sys
feed, tags, members, out = json.loads(sys.argv[1]), json.loads(sys.argv[2]), sys.argv[3].split(), sys.argv[4]
# The generalized bot (.github#1260) reconciles EVERY package-bearing row, and the `registry` helper's
# snippet carries two GENERIC neighbours (fsgg-contracts, game-sim-core) alongside the template. Serve
# their packages at the snippet's declared versions so they reconcile to NO CHANGE — every template
# case below therefore also proves the generic rows are read and left alone. `setdefault` so a case
# that deliberately advances a neighbour (the multi-row cases) can override.
feed.setdefault("FS.GG.Contracts", ["2.0.1"])
feed.setdefault("FS.GG.Game.Core", ["0.5.0"])
deps = [m for m in members if m != "FS.GG.UI"]
nuspec = {}
for version in feed.get("FS.GG.UI", []):
    body = "\n".join(f'      <dependency id="{d}" version="[{version}]" />' for d in deps)
    nuspec[f"FS.GG.UI@{version}"] = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">\n'
        "  <metadata>\n"
        "    <id>FS.GG.UI</id>\n"
        f"    <version>{version}</version>\n"
        "    <dependencies>\n" + body + "\n    </dependencies>\n"
        "  </metadata>\n"
        "</package>\n"
    )
with open(out, "w", encoding="utf-8") as fh:
    json.dump({"feed": feed, "tags": tags, "nuspec": nuspec}, fh)
PY
}

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
# 3b. THE 18TH MEMBER — the bound this whole derivation exists to remove (.github#933).
#
# FS.GG.Rendering publishes a NEW member, and a partial publish leaves ONLY that new member behind.
# Against the old hand-maintained 17-id literal this was a SILENT false framework release: the 17
# ids it knew about were all at the candidate, so it classified "framework" and flipped the pin to a
# version the 18th member cannot serve. Nothing went red. It is the one shape the bot could not see,
# and every other partial shape was already caught, which is what made it a blind spot rather than a
# fail-open default.
#
# The BOM declares 18 members here, so the bot must now SEE the straggler and refuse. This case
# fails on the pre-#933 bot — that is the point of it.
# --------------------------------------------------------------------------------------------
registry "$WORK/r3b.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
python3 - "$MEMBERS" "$TRIPLE_11" "$WORK/f3b.json" <<'PY'
import json, sys
members, tags, out = sys.argv[1].split(), json.loads(sys.argv[2]), sys.argv[3]
NEW = "FS.GG.UI.Holograms"

# Every known member moved to 0.11.0. The NEW member did NOT — it is still at 0.10.0.
feed = {m: ["0.11.0"] for m in members}
feed[NEW] = ["0.10.0"]
feed["FS.GG.UI.Template"] = ["0.11.0"]
# The generic neighbours the `registry` snippet carries, at their no-change versions (.github#1260).
feed["FS.GG.Contracts"] = ["2.0.1"]
feed["FS.GG.Game.Core"] = ["0.5.0"]

# ...and the BOM at 0.11.0 declares it, because the upstream parity test locks the BOM's membership
# to the packable set. That declaration is the whole reason the bot can now see the straggler.
deps = [m for m in members if m != "FS.GG.UI"] + [NEW]
body = "\n".join(f'      <dependency id="{d}" version="[0.11.0]" />' for d in deps)
nuspec = {
    "FS.GG.UI@0.11.0": (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">\n'
        "  <metadata>\n    <id>FS.GG.UI</id>\n    <version>0.11.0</version>\n"
        "    <dependencies>\n" + body + "\n    </dependencies>\n  </metadata>\n</package>\n"
    )
}
with open(out, "w", encoding="utf-8") as fh:
    json.dump({"feed": feed, "tags": tags, "nuspec": nuspec}, fh)
PY
before="$(cat "$WORK/r3b.yml")"
out="$(python3 "$BOT" "$WORK/r3b.yml" --fixture "$WORK/f3b.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "PARTIAL PUBLISH"; then
  ok "18th member left behind: SEEN and refused (the .github#933 blind spot)"
else
  bad "18th member left behind: SEEN and refused (the .github#933 blind spot)" "rc=$rc: $out"
fi
if echo "$out" | grep -q "FS.GG.UI.Holograms=0.10.0"; then
  ok "18th member left behind: the straggler is NAMED in the finding"
else
  bad "18th member left behind: the straggler is NAMED in the finding" "$out"
fi
if [ "$before" = "$(cat "$WORK/r3b.yml")" ]; then
  ok "18th member left behind: registry left BYTE-IDENTICAL"
else
  bad "18th member left behind: registry left BYTE-IDENTICAL" "the bot flipped the pin to a version the new member cannot serve"
fi

# The mirror: the same NEW member published WITH everything else is a clean framework release. A
# derivation that only ever refused would be useless — it must still say yes to the good shape.
registry "$WORK/r3c.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
python3 - "$MEMBERS" "$TRIPLE_11" "$WORK/f3c.json" <<'PY'
import json, sys
members, tags, out = sys.argv[1].split(), json.loads(sys.argv[2]), sys.argv[3]
NEW = "FS.GG.UI.Holograms"
feed = {m: ["0.11.0"] for m in members}
feed[NEW] = ["0.11.0"]          # this time it DID publish
feed["FS.GG.UI.Template"] = ["0.11.0"]
feed["FS.GG.Contracts"] = ["2.0.1"]      # generic neighbours, no-change (.github#1260)
feed["FS.GG.Game.Core"] = ["0.5.0"]
deps = [m for m in members if m != "FS.GG.UI"] + [NEW]
body = "\n".join(f'      <dependency id="{d}" version="[0.11.0]" />' for d in deps)
nuspec = {
    "FS.GG.UI@0.11.0": (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">\n'
        "  <metadata>\n    <id>FS.GG.UI</id>\n    <version>0.11.0</version>\n"
        "    <dependencies>\n" + body + "\n    </dependencies>\n  </metadata>\n</package>\n"
    )
}
with open(out, "w", encoding="utf-8") as fh:
    json.dump({"feed": feed, "tags": tags, "nuspec": nuspec}, fh)
PY
out="$(python3 "$BOT" "$WORK/r3c.yml" --fixture "$WORK/f3c.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && echo "$out" | grep -q '"kind": "framework"'; then
  ok "18th member published too: still a clean framework release"
else
  bad "18th member published too: still a clean framework release" "rc=$rc: $out"
fi
if echo "$out" | grep -q "all 18 FS.GG.UI"; then
  ok "18th member published too: the derived set GREW to 18 with no code edit"
else
  bad "18th member published too: the derived set GREW to 18 with no code edit" "$out"
fi

# --------------------------------------------------------------------------------------------
# 3d. THE BOM ITSELF IS THE THING THAT CAN LIE — so its failures must never become a guess.
# --------------------------------------------------------------------------------------------
# The BOM cannot be read (absent from the feed). The set is NOT assumed from a baked-in list: a
# stale hardcoded set is the bound this derivation removes, so reinstating it on failure would
# restore that bound at exactly the moment the derivation stopped working.
registry "$WORK/r3d.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
noBom="$(build_feed 0.11.0 0.10.0 0.11.0 | python3 -c \
  'import json,sys; f=json.load(sys.stdin); del f["FS.GG.UI"]; print(json.dumps(f))')"
fixture "$noBom" "$TRIPLE_11" "$WORK/f3d.json"
before="$(cat "$WORK/r3d.yml")"
out="$(python3 "$BOT" "$WORK/r3d.yml" --fixture "$WORK/f3d.json" --write 2>&1)"; rc=$?
# Assert on the SUBJECT, not merely on non-zero: "it failed" would be satisfied by any unrelated
# breakage, which is how a test ends up green about a thing it never exercised.
if [ "$rc" -ne 0 ] && echo "$out" | grep -q "FS.GG.UI"; then
  ok "BOM unreadable: refuses, naming the BOM it could not read"
else
  bad "BOM unreadable: refuses, naming the BOM it could not read" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r3d.yml")" ]; then
  ok "BOM unreadable: registry untouched — the set is never guessed from a baked-in list"
else
  bad "BOM unreadable: registry untouched — the set is never guessed from a baked-in list" "the bot wrote without knowing the set"
fi

# A BOM that declares NO dependencies. An empty coherent set would make `len(at_candidate) ==
# len(members)` vacuously TRUE and classify EVERY publish as a clean framework release — the
# emptiest possible fail-open, and the reason nuspec_dependency_ids' [] is a caller's problem.
registry "$WORK/r3e.yml" "0.10.0" "0.9.2 -> 0.10.0" "0.10.0" "0.9.2 -> 0.10.0" "fs-gg-ui-template/v0.10.0"
python3 - "$MEMBERS" "$TRIPLE_11" "$WORK/f3e.json" <<'PY'
import json, sys
members, tags, out = sys.argv[1].split(), json.loads(sys.argv[2]), sys.argv[3]
feed = {m: ["0.11.0"] for m in members}
feed["FS.GG.UI.Template"] = ["0.11.0"]
feed["FS.GG.Contracts"] = ["2.0.1"]      # generic neighbours, no-change (.github#1260)
feed["FS.GG.Game.Core"] = ["0.5.0"]
nuspec = {
    "FS.GG.UI@0.11.0": (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">\n'
        "  <metadata>\n    <id>FS.GG.UI</id>\n    <version>0.11.0</version>\n"
        "  </metadata>\n</package>\n"
    )
}
with open(out, "w", encoding="utf-8") as fh:
    json.dump({"feed": feed, "tags": tags, "nuspec": nuspec}, fh)
PY
before="$(cat "$WORK/r3e.yml")"
out="$(python3 "$BOT" "$WORK/r3e.yml" --fixture "$WORK/f3e.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "declares NO dependencies"; then
  ok "BOM with an empty dependency set: refused, not read as 'everything published'"
else
  bad "BOM with an empty dependency set: refused, not read as 'everything published'" "rc=$rc: $out"
fi
if [ "$before" = "$(cat "$WORK/r3e.yml")" ]; then
  ok "BOM with an empty dependency set: registry untouched"
else
  bad "BOM with an empty dependency set: registry untouched" "the bot wrote on a refusal"
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

# A registry with NO package-bearing subject must not read as "nothing to do" — the generalized bot
# (.github#1260) reconciles whatever package-bearing rows exist, so "no subject at all" is a loud
# DEFECT (rc 2), never a silent green. (The single-row bot's "I expected fs-gg-ui-template and it is
# gone" is not a concept here: the bot expects no fixed row.)
cat > "$WORK/r9.yml" <<'YAML'
contracts:
  - id: game-sim-core
    version: "0.5.0"
YAML
out="$(python3 "$BOT" "$WORK/r9.yml" --fixture "$WORK/f6.json" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ] && echo "$out" | grep -q "no contract carries a .package-version."; then
  ok "no package-bearing subject: a loud defect, not silence"
else
  bad "no package-bearing subject: a loud defect, not silence" "rc=$rc: $out"
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

# ============================================================================================
# GENERIC ROWS (.github#1260). Every package-bearing row EXCEPT fs-gg-ui-template takes the generic
# strategy: flip `package-version` (only) to the feed's newest. The load-bearing safety property is
# that it NEVER writes `version` — for these rows `version` is source/component-owned, not the feed's
# to decide — so each case below asserts the `version` line survives the flip byte-for-byte.
# ============================================================================================

# GEN-1. A single-package generic row BEHIND the feed: flip package-version, leave version untouched.
cat > "$WORK/g1.yml" <<'YAML'
contracts:
  - id: game-sim-core
    version: "0.5.0"   # component version — NOT feed-derived; the bot must not touch it
    package-version: "0.5.0"   # published — flips to the feed's newest
YAML
fixture '{"FS.GG.Game.Core":["0.6.0"]}' '{}' "$WORK/g1.json"
out="$(python3 "$BOT" "$WORK/g1.yml" --fixture "$WORK/g1.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && echo "$out" | grep -q '"kind": "single"'; then
  ok "generic single: classified single, exits 0"
else
  bad "generic single: classified single, exits 0" "rc=$rc: $out"
fi
if grep -q 'package-version: "0.6.0"' "$WORK/g1.yml"; then
  ok "generic single: package-version flipped to the feed newest"
else
  bad "generic single: package-version flipped to the feed newest" "$(grep version "$WORK/g1.yml")"
fi
if grep -q 'version: "0.5.0"   # component version' "$WORK/g1.yml"; then
  ok "generic single: version is LEFT UNTOUCHED (not the feed's to decide)"
else
  bad "generic single: version is LEFT UNTOUCHED" "the bot wrote a version it has no authority for: $(grep version "$WORK/g1.yml")"
fi

# GEN-2. A generic row AHEAD of the feed is the FR-007 inversion — refuse, do not wind back.
cat > "$WORK/g2.yml" <<'YAML'
contracts:
  - id: game-sim-core
    version: "0.9.0"
    package-version: "0.9.0"
YAML
before="$(cat "$WORK/g2.yml")"
fixture '{"FS.GG.Game.Core":["0.6.0"]}' '{}' "$WORK/g2.json"
out="$(python3 "$BOT" "$WORK/g2.yml" --fixture "$WORK/g2.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "AHEAD of the feed"; then
  ok "generic ahead: refused as a finding (exit 1), not wound back"
else
  bad "generic ahead: refused as a finding (exit 1)" "rc=$rc: $out"
fi
[ "$before" = "$(cat "$WORK/g2.yml")" ] && ok "generic ahead: registry untouched" \
  || bad "generic ahead: registry untouched" "the bot wound the registry back"

# GEN-3. A COHERENT-SET row (fs-gg-net, 6 members) with every member at ONE new version: flip.
NET='FS.GG.Net.Core FS.GG.Net.WebSocket FS.GG.Net.WebSocket.Server FS.GG.Net.Protobuf FS.GG.Net.Grpc FS.GG.Net.Elmish'
cat > "$WORK/g3.yml" <<'YAML'
contracts:
  - id: fs-gg-net
    version: "0.1.0"
    package-version: "0.1.0"
YAML
feed_net="$(python3 -c 'import json,sys; print(json.dumps({p:["0.2.0"] for p in sys.argv[1].split()}))' "$NET")"
fixture "$feed_net" '{}' "$WORK/g3.json"
out="$(python3 "$BOT" "$WORK/g3.yml" --fixture "$WORK/g3.json" --write --json 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && echo "$out" | grep -q '"kind": "coherent-set"' && grep -q 'package-version: "0.2.0"' "$WORK/g3.yml"; then
  ok "coherent set: every member at one version -> flip"
else
  bad "coherent set: every member at one version -> flip" "rc=$rc: $out"
fi

# GEN-4. The same set with ONE member left behind is a PARTIAL PUBLISH — refuse, name the straggler.
cat > "$WORK/g4.yml" <<'YAML'
contracts:
  - id: fs-gg-net
    version: "0.1.0"
    package-version: "0.1.0"
YAML
before="$(cat "$WORK/g4.yml")"
feed_partial="$(python3 -c 'import json,sys
p=sys.argv[1].split(); f={x:["0.2.0"] for x in p}; f["FS.GG.Net.Grpc"]=["0.1.0"]; print(json.dumps(f))' "$NET")"
fixture "$feed_partial" '{}' "$WORK/g4.json"
out="$(python3 "$BOT" "$WORK/g4.yml" --fixture "$WORK/g4.json" --write 2>&1)"; rc=$?
if [ "$rc" -eq 1 ] && echo "$out" | grep -q "PARTIAL PUBLISH" && echo "$out" | grep -q "FS.GG.Net.Grpc=0.1.0"; then
  ok "coherent set partial: refused, straggler named"
else
  bad "coherent set partial: refused, straggler named" "rc=$rc: $out"
fi
[ "$before" = "$(cat "$WORK/g4.yml")" ] && ok "coherent set partial: registry untouched" \
  || bad "coherent set partial: registry untouched" "the bot flipped to a version a member cannot serve"

# GEN-5. MULTI-ROW aggregation: one row flips, one row is a finding, in ONE pass. The flip lands and
# the finding is reported — a refused row does not withhold a clean row's flip.
cat > "$WORK/g5.yml" <<'YAML'
contracts:
  - id: game-sim-core
    version: "0.5.0"
    package-version: "0.5.0"
  - id: game-scene-adapter
    version: "0.9.0"
    package-version: "0.9.0"
YAML
fixture '{"FS.GG.Game.Core":["0.6.0"],"FS.GG.Game.Render":["0.6.0"]}' '{}' "$WORK/g5.json"
out="$(python3 "$BOT" "$WORK/g5.yml" --fixture "$WORK/g5.json" --write --json \
  --pr-body "$WORK/g5-pr.md" --findings-body "$WORK/g5-iss.md" --run-url http://run 2>&1)"; rc=$?
if [ "$rc" -eq 1 ]; then ok "multi-row: rc 1 (a finding exists)"; else bad "multi-row: rc 1" "rc=$rc: $out"; fi
if grep -q 'package-version: "0.6.0"' "$WORK/g5.yml" && grep -q 'version: "0.9.0"' "$WORK/g5.yml"; then
  ok "multi-row: the clean row flipped, the refused row is byte-identical"
else
  bad "multi-row: clean flip + refused untouched" "$(grep -E 'id:|version:' "$WORK/g5.yml")"
fi
if grep -q 'game-sim-core' "$WORK/g5-pr.md" && grep -q 'game-scene-adapter' "$WORK/g5-iss.md"; then
  ok "multi-row: PR body carries the flip, issue body carries the finding"
else
  bad "multi-row: PR body / issue body split" "pr=$(cat "$WORK/g5-pr.md") iss=$(cat "$WORK/g5-iss.md")"
fi

# GEN-6. A package-bearing row with NO mapping in CONTRACT_PACKAGES is the epic #266 unchecked
# subject — a DEFECT (rc 2), never a silent skip.
cat > "$WORK/g6.yml" <<'YAML'
contracts:
  - id: totally-unmapped-contract
    version: "1.0.0"
    package-version: "1.0.0"
YAML
out="$(python3 "$BOT" "$WORK/g6.yml" --fixture "$WORK/g1.json" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ] && echo "$out" | grep -q "no package id is mapped"; then
  ok "unmapped subject: a loud defect (rc 2), not a silent skip"
else
  bad "unmapped subject: a loud defect (rc 2)" "rc=$rc: $out"
fi

# --- #1081 option 2 CLOSURE: a flip is not done when the YAML is written — it is done when the
# projections it feeds are regenerated. This is the exact bug #1081 names: feed-autofix wrote the
# registry and the REQUIRED `projection` gate stayed red, so no flip it opened could ever merge. Every
# case above checks the YAML write, which the issue says "reproduces this bug exactly". So drive the
# WHOLE closure: flip the row(s) → the required gate is RED on the write alone → regenerate → GREEN.
#
# TWICE-MOVED FEED (the #1081 thread): a second step-2 flip (coord-engine 0.4.0) came due while the
# first (fs-gg-ui-template) sat, and the bot never noticed the moving feed. So move BOTH — a fixture
# that moves the feed once reproduces this bug's easier half and misses the one that kept `main` red.
CHECKPROJ="$ROOT/scripts/check-projection.py"

# The flip, applied to a COPY of the REAL registry: two contracts advanced, each `version` and any
# `package-version`. A regex substitution (not a YAML round-trip) so the file the gate reads keeps the
# real shape; bounded to each `- id:` block so it cannot bleed into a sibling that shares a version.
FLIPPED="$WORK/registry-twice-moved.yml"
VERS="$WORK/closure-versions.txt"
# DERIVE the from-versions from the live registry and bump each, rather than hardcoding them — a
# hardcoded pair silently ROTS when the registry moves past it (the bump becomes a no-op, the "flip"
# writes nothing, and this #1081 guard passes on a registry it never actually flipped, testing
# nothing). That is exactly how it was found dead here (.github#1260): the registry had advanced to
# 0.15.0/0.7.0 while the test still bumped 0.12.0/0.4.0. Deriving keeps the guard live by construction.
python3 - "$ROOT/registry/dependencies.yml" "$FLIPPED" "$VERS" <<'PY'
import re, sys
s = open(sys.argv[1], encoding="utf-8").read()
def current(cid):
    m = re.search(
        r'- id: ' + re.escape(cid) + r'\n(?:(?!\s*- id:).*\n)*?\s*version: "([^"]*)"', s)
    if not m:
        sys.exit(f"closure test: no `version:` for {cid} — the registry shape changed.")
    return m.group(1)
def bumped(v):  # advance the last numeric segment: a definitely-higher version that still parses
    p = v.split(".")
    p[-1] = str(int(re.match(r"\d+", p[-1]).group()) + 1)
    return ".".join(p)
def bump(text, cid, frm, to):
    # inside `- id: <cid>` up to (not into) the next `- id:`, advance version/package-version == frm
    pat = re.compile(
        r'(- id: ' + re.escape(cid) + r'\n(?:(?!\s*- id:).*\n)*?\s*(?:package-)?version: ")'
        + re.escape(frm) + r'(")')
    n = 1
    while n:
        text, n = pat.subn(r'\g<1>' + to + r'\g<2>', text, count=1)
    return text
tf, cf = current("fs-gg-ui-template"), current("coord-engine")
tt, ct = bumped(tf), bumped(cf)
s = bump(s, "fs-gg-ui-template", tf, tt)
s = bump(s, "coord-engine", cf, ct)
open(sys.argv[2], "w", encoding="utf-8").write(s)
open(sys.argv[3], "w", encoding="utf-8").write(f"{tt}\n{ct}\n")
PY
TMPL_TO="$(sed -n 1p "$VERS")"
CE_TO="$(sed -n 2p "$VERS")"

# RED half — no engine: the write alone leaves the REQUIRED gate red against the un-regenerated
# region. The direct #1081 regression guard; it must fail while the region is stale, naming both moves.
if red="$(python3 "$CHECKPROJ" "$FLIPPED" "$ROOT/docs/registry/compatibility.md" 2>&1)"; then
  bad "closure: a flip WITHOUT regeneration must red the required projection gate" \
      "the gate passed on a stale region — the write-only bug #1081 names is not caught: $red"
elif printf '%s' "$red" | grep -q "$TMPL_TO" && printf '%s' "$red" | grep -q "$CE_TO"; then
  ok "closure: a flip without regeneration reds the required gate (both moved contracts)"
else
  bad "closure: gate red, but not for the two moved contracts ($TMPL_TO / $CE_TO)" "$red"
fi

# GREEN half — regenerate from the flipped registry and assert the required `projection` gate AND
# `generate-projections --check` both go green. Needs the engine (the CI fixture job builds it); a
# local run without one SKIPs, leaving the always-on RED half as the guard. Runs in a throwaway copy
# of the tracked tree so it never touches the real checkout.
ENGINE="${FSGG_COORD_ENGINE_BIN:-$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"
if [ -x "$ENGINE" ]; then
  TREE="$WORK/closure-tree"
  mkdir -p "$TREE"
  git -C "$ROOT" archive HEAD | tar -x -C "$TREE"
  cp "$FLIPPED" "$TREE/registry/dependencies.yml"
  if FSGG_COORD_ENGINE_BIN="$ENGINE" bash "$TREE/scripts/generate-projections" >/dev/null 2>&1 \
     && python3 "$CHECKPROJ" "$TREE/registry/dependencies.yml" "$TREE/docs/registry/compatibility.md" >/dev/null 2>&1 \
     && FSGG_COORD_ENGINE_BIN="$ENGINE" bash "$TREE/scripts/generate-projections" --check >/dev/null 2>&1; then
    ok "closure: regenerating greens BOTH the projection gate and generate-projections --check"
  else
    bad "closure: regeneration did not green the gates" \
        "$(python3 "$CHECKPROJ" "$TREE/registry/dependencies.yml" "$TREE/docs/registry/compatibility.md" 2>&1)"
  fi
else
  printf '  SKIP closure GREEN half — no engine at %s (the CI fixture job builds it)\n' "$ENGINE"
fi

# --- #1081 ACCEPTANCE 4: the CLOSURE GUARD — the NEXT #913 must not be able to silently break the
# writer. #913 added a generated consumer of registry/dependencies.yml (the architecture-map versions
# region) and nothing told the WRITER; the bot's flip left it stale and reddened a required gate
# (#1081). Slice B closed the KNOWN instance by running the WHOLE of generate-projections on a flip, so
# any region that generator owns is covered. This guard closes the NEXT one: a registry generator the
# bot's flip does NOT run.
#
# "Generates from the registry" is decided by EXECUTION, never grep — the check-generator-list docstring
# and #683 are explicit that scanning source/`run:` text for a path cannot tell a MENTION from a USE. A
# generator is registry-sourced iff flipping the registry MOVES what it emits. So flip the registry, run
# every rostered generator, and collect the ones whose output changes. That set MUST equal the bot's
# flip-closure — the generators feed-autofix.yml's regenerate step runs. One outside it is the #913
# shape, and it reds HERE, in the bot's own suite.
#
# BOT_CLOSURE_GENERATORS is the generators feed-autofix.yml's "Regenerate the projections ..." step
# runs. Hand-kept and coupled to that step ON PURPOSE, exactly like projections.yml's region census:
# adding a registry generator to the bot must cost this line too, or the sets diverge and this reds.
BOT_CLOSURE_GENERATORS="scripts/generate-projections"

if [ -x "$ENGINE" ]; then
  GTREE="$WORK/accept4-tree"
  mkdir -p "$GTREE"
  git -C "$ROOT" archive HEAD | tar -x -C "$GTREE"
  cp "$FLIPPED" "$GTREE/registry/dependencies.yml"   # the twice-moved flip from the closure case above

  reacted=""
  # `generated-paths --roster` names each generator's `--list` invocation; strip `--list` to get the
  # REGENERATE invocation, run it in the flipped tree, and see whether it moved any file.
  while IFS= read -r inv; do
    [ -n "$inv" ] || continue
    regen="${inv% --list}"
    gen="${regen%% *}"
    before="$(cd "$GTREE" && find . -type f -exec sha256sum {} + | sort)"
    FSGG_COORD_ENGINE_BIN="$ENGINE" bash -c "cd '$GTREE' && $regen" >/dev/null 2>&1 || true
    after="$(cd "$GTREE" && find . -type f -exec sha256sum {} + | sort)"
    [ "$before" != "$after" ] && reacted="$reacted $gen"
  done < <(bash "$ROOT/scripts/generated-paths" --roster)

  reacted="$(printf '%s' "$reacted" | tr ' ' '\n' | sed '/^$/d' | sort -u | tr '\n' ' ' | sed 's/ *$//')"
  expected="$(printf '%s' "$BOT_CLOSURE_GENERATORS" | tr ' ' '\n' | sed '/^$/d' | sort -u | tr '\n' ' ' | sed 's/ *$//')"
  if [ "$reacted" = "$expected" ]; then
    ok "closure guard: exactly the bot's flip-closure regenerates on a registry flip [$reacted]"
  else
    bad "closure guard: the generators that react to a registry flip are not the bot's flip-closure" \
        "reacted on a registry flip: [$reacted]
bot flip-closure (BOT_CLOSURE_GENERATORS): [$expected]
A generator generates from the registry that the bot's flip does not run (the #913 shape), or the
closure list names one that does not. If you added a registry-sourced projection, teach
feed-autofix.yml's regenerate step to run its generator AND add it to BOT_CLOSURE_GENERATORS here —
otherwise the bot's flips leave it stale and red the required gate (#1081/#913)."
  fi
else
  printf '  SKIP closure guard — no engine at %s (the CI fixture job builds it)\n' "$ENGINE"
fi

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
