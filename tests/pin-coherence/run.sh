#!/usr/bin/env bash
# Fixture for scripts/check-pin-coherence.py — the gate that compares every Renovate-annotated
# version pin against the newest version on THE REGISTRY RENOVATE ACTUALLY READS (#263, #576, #266).
#
# The gate exists because the FS.GG.SDD.Cli validator pin froze FOUR times — 0.2.1 (#127), 0.5.0
# (#263), 0.9.0 (#566), 0.10.0 — while the registry row asserted `coherent: true` on the strength of
# that pin tracking registry-newest. Every time, a human found it, and every time it was closed by
# hand-advancing the literal and chasing a credential that was never the problem: the packages are
# PUBLIC on nuget.org, and the preset was routing them to an auth-required feed instead (#576).
#
# So this fixture spends nearly all its length on the FAILURE legs. It proves the gate goes red when
# the pin is behind the registry, ahead of it, ordered by substring, routed at a host the bot cannot
# read, routed at a host the gate cannot classify, invisible to the manager's regex, absent,
# unresolvable, or pointed at a registry that 404s or is empty — and that it does NOT demand a
# credential for a public route, which is the demand that hid the defect for four rounds.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than from
# the thing under test. `must_fail` therefore takes a required pattern, and every mutate helper
# refuses a no-op edit, so a fixture that stops exercising its own claim breaks loudly.
#
# Throwaway git repos under a temp dir, no network (the gate's --fixture flag serves a canned feed).
# Each case builds a real repo because the gate scans `git ls-files` — exactly what Renovate sees.
# Mirrors tests/feed-coherence/run.sh.

set -euo pipefail

# The gate imports scripts/fsgg_feed.py, which would otherwise litter scripts/__pycache__ into a
# repo that has no .gitignore.
export PYTHONDONTWRITEBYTECODE=1

# `--fixture` is locked to this harness: the gate refuses a canned feed unless this is set, so a
# stray `--fixture` in CI fails rather than silently reporting green. See the gate's docstring.
export FSGG_PIN_FIXTURE_OK=1

# The gate must never fall back to a real feed read from a developer's ambient credentials.
unset GITHUB_TOKEN GH_TOKEN || true

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-pin-coherence.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/pin-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# The one path REQUIRED_PINS names. The green baseline must reproduce it exactly, or the gate's
# required-pin assertion is what we would be testing instead of the freshness comparison.
PIN_FILE=".github/workflows/contract-coherence.yml"

# The feed. FS.GG.SDD.Cli deliberately serves BOTH 0.9.0 and 0.9.0-preview.1 (the substring trap),
# listed out of version order (the feed returns creation order, not version order).
#
# `_renovate` is the canned bot-evidence block (#566). When a pin is stale the gate no longer ASSERTS
# a cause — it reads Renovate's own dashboard + PRs and names the one the evidence supports. Offline,
# that evidence comes from here. The default is the LIVE defect: the bot detected the dependency
# (so the manager's regex is fine) and never opened a bump PR — i.e. it is blind to the feed.
FEED="$WORK/feed.json"
cat > "$FEED" <<'JSON'
{
  "FS.GG.SDD.Cli":   ["0.5.0", "0.9.0", "0.9.0-preview.1", "0.8.0", "0.6.0", "0.7.0"],
  "FS.GG.Contracts": ["1.4.0"],
  "_renovate": { "detected": true, "bump_prs": [], "dashboard": 54 }
}
JSON

# feed_evidence <name> <_renovate-json> — the standard feed, with a different bot-evidence block.
feed_evidence() {
  local out="$WORK/feed-ev-$1.json"
  python3 -c '
import json, sys
d = json.load(open(sys.argv[1]))
d["_renovate"] = json.loads(sys.argv[2])
json.dump(d, open(sys.argv[3], "w"))
' "$FEED" "$2" "$out"
  printf '%s' "$out"
}

# make_repo <name> [pin-version] — a minimal repo carrying the real org preset, a renovate.json with
# the feed hostRules token, and an annotated FS.GG.SDD.Cli pin at the given version. Echoes its path.
make_repo() {
  local name="$1"
  local version="${2:-0.9.0}"
  local root="$WORK/$name"
  mkdir -p "$root/.github/workflows"

  # The REAL preset — the gate reads the manager's regex from it, so a copy would let this fixture
  # keep passing after someone edits default.json in a way that stops matching the pin.
  cp "$REPO_ROOT/default.json" "$root/default.json"

  cat > "$root/renovate.json" <<'JSON'
{
  "extends": ["github>FS-GG/.github"],
  "hostRules": [
    { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" }
  ]
}
JSON

  cat > "$root/$PIN_FILE" <<YAML
name: contract-coherence
jobs:
  coherence:
    steps:
      - run: |
          # renovate: datasource=nuget depName=FS.GG.SDD.Cli
          dotnet tool install --global FS.GG.SDD.Cli --version $version
YAML

  git -C "$root" init -q
  git -C "$root" add -A
  printf '%s' "$root"
}

# route_to <repo> <registry-url> — repoint the preset's FS.GG.* rule at another registry.
#
# The gate's invariant is no longer "a token is declared for THE feed" — it is "every host the
# preset routes FS.GG.* to is one Renovate can read" (.github#576). So the token legs below need a
# repo that is actually routed at an auth-required host; against the real preset, which now routes
# to public nuget.org, a missing token is not a defect and must not be failed.
route_to() {
  local root="$1" url="$2"
  edit_json "$root/default.json" "
for r in d['packageRules']:
    if any('FS.GG' in str(n) or 'FS\\\\.GG' in str(n) for n in r.get('matchPackageNames', [])) and 'registryUrls' in r:
        r['registryUrls'] = ['$url']
"
  git -C "$root" add -A
}

# repin <repo> <new-version> — rewrite the pin literal. Refuses a no-op.
repin() {
  local root="$1" new="$2"
  python3 - "$root/$PIN_FILE" "$new" <<'PY'
import re, sys
path, new = sys.argv[1:3]
text = open(path, encoding="utf-8").read()
m = re.search(r"--version (\S+)", text)
if not m:
    sys.exit("vacuous fixture: no --version literal to rewrite")
if m.group(1) == new:
    sys.exit(f"vacuous fixture: the pin is already {new!r} — this mutation is a no-op")
open(path, "w", encoding="utf-8").write(text[:m.start(1)] + new + text[m.end(1):])
PY
  git -C "$root" add -A
}

# edit_json <file> <python-expr-over-`d`> — mutate a JSON file in place. Refuses a no-op.
edit_json() {
  local path="$1" expr="$2"
  python3 - "$path" "$expr" <<'PY'
import json, sys
path, expr = sys.argv[1:3]
before = open(path, encoding="utf-8").read()
d = json.loads(before)
exec(expr)  # noqa: S102 — fixture-local, mutates `d`
after = json.dumps(d, indent=2)
if json.loads(after) == json.loads(before):
    sys.exit(f"vacuous fixture: {expr!r} changed nothing")
open(path, "w", encoding="utf-8").write(after)
PY
}

# feed_with <name> <json-version-list> — a feed serving those versions for FS.GG.SDD.Cli.
feed_with() {
  local out="$WORK/feed-$1.json"
  python3 -c 'import json,sys; json.dump({"FS.GG.SDD.Cli": json.loads(sys.argv[1])}, open(sys.argv[2],"w"))' "$2" "$out"
  printf '%s' "$out"
}

gate() { python3 "$GATE" --root "$1" --fixture "${2:-$FEED}" 2>&1; }

# must_pass <label> <repo> [feed]
must_pass() {
  local out rc
  out="$(gate "$2" "${3:-$FEED}")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$1"; else bad "$1 (expected exit 0, got $rc)" "$out"; fi
}

# must_fail <label> <repo> <feed> <required-pattern>
# The pattern is REQUIRED: exit 1 alone does not prove the gate failed for the reason claimed.
must_fail() {
  local out rc
  out="$(gate "$2" "$3")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$1 (expected non-zero exit, got 0)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$4"; then
    ok "$1"
  else
    bad "$1 (failed, but not for the stated reason: no match for '$4')" "$out"
  fi
}

echo "--- the green baseline: the pin equals feed-newest ---"
BASE="$(make_repo base 0.9.0)"
must_pass "a pin at feed-newest passes" "$BASE"

echo
echo "--- a frozen pin is caught (the #127 / #263 defect itself) ---"
FROZEN="$(make_repo frozen 0.9.0)"; repin "$FROZEN" 0.5.0
must_fail "pin behind the feed fails" "$FROZEN" "$FEED" "is pinned at '0.5.0' but the newest on the registry Renovate reads is '0.9.0'"
must_fail "...and names the freeze, not a generic mismatch" "$FROZEN" "$FEED" "BOT IS BLIND"

AHEAD="$(make_repo ahead 0.9.0)"; repin "$AHEAD" 1.0.0
must_fail "pin ahead of the feed fails" "$AHEAD" "$FEED" "AHEAD"

echo
echo "--- versions are compared by ORDER, never by substring (the #268 defect class) ---"
PRE="$(make_repo pre 0.9.0)"; repin "$PRE" 0.9.0-preview.1
must_fail "'0.9.0-preview.1' is not 'equal' to feed-newest '0.9.0'" "$PRE" "$FEED" "is pinned at '0.9.0-preview.1' but the newest on the registry Renovate reads is '0.9.0'"
must_pass "a release outranks its own prerelease" "$BASE" "$(feed_with onlyrelease '["0.9.0","0.9.0-preview.1"]')"
must_fail "a pin is AHEAD of a feed serving only its prerelease" "$BASE" "$(feed_with onlypre '["0.9.0-preview.1"]')" "AHEAD"

echo
echo "--- the bump MECHANISM is gated, not assumed (the #263 / #576 root cause) ---"
#
# The invariant is: EVERY host the preset routes FS.GG.* to must be one Renovate can actually read.
#
# It used to be "renovate.json declares a hostRules token for nuget.pkg.github.com", full stop. That
# demanded a credential unconditionally, and so it could never say the true thing — that the packages
# are PUBLIC on nuget.org and the credential was never needed. Four freezes were closed by chasing
# that token (#127, #263, #566, and again at 0.10.0). The legs below therefore assert BOTH halves:
# an auth-routed repo still needs its token (the #263 protection survives), and a public-routed one
# does not (the #576 fix is what makes the loop end).

# --- routed at the org feed: the token is still mandatory, exactly as before ---
NOTOKEN="$(make_repo notoken)"; route_to "$NOTOKEN" "https://nuget.pkg.github.com/FS-GG/index.json"
edit_json "$NOTOKEN/renovate.json" 'del d["hostRules"]'; git -C "$NOTOKEN" add -A
must_fail "an auth-routed preset with no hostRules token fails" "$NOTOKEN" "$FEED" \
  "routes FS.GG.* to nuget.pkg.github.com, which REQUIRES a credential"

# ...and the failure must SAY that the packages may simply be public, so the fifth freeze is not
# closed by chasing the credential a fifth time. This is the finding, encoded as a test.
must_fail "...and it points at nuget.org before the credential" "$NOTOKEN" "$FEED" \
  "check whether these packages are simply PUBLIC on nuget.org"

# --- routed at nuget.org: NO token is required, and demanding one is the bug (#576) ---
PUBLIC="$(make_repo publicroute)"; edit_json "$PUBLIC/renovate.json" 'del d["hostRules"]'
git -C "$PUBLIC" add -A
must_pass "a public-routed preset needs no hostRules token at all" "$PUBLIC"

# --- a host the gate does not know must fail CLOSED, not read as fine ---
UNKNOWN="$(make_repo unknownhost)"; route_to "$UNKNOWN" "https://packages.example.invalid/v3/index.json"
must_fail "a preset routed at an UNKNOWN host fails closed" "$UNKNOWN" "$FEED" \
  "does not know how to read"

# --- an UNNAMED nuget-wide registryUrls reroutes FS.GG.* too, and must NOT be invisible ---
#
# A rule with `registryUrls` and NO `matchPackageNames` applies to every nuget package — FS.GG.*
# included. A gate that only looked for rules NAMING FS.GG would report green while the bot was
# routed somewhere it cannot read: the #266 fails-open shape, rebuilt inside the fix for it.
WIDE="$(make_repo widerule)"
edit_json "$WIDE/default.json" "
d['packageRules'].append({
    'description': 'an unnamed nuget-wide route — reaches FS.GG.* without naming it',
    'matchDatasources': ['nuget'],
    'registryUrls': ['https://nuget.pkg.github.com/FS-GG/index.json'],
})
"
edit_json "$WIDE/renovate.json" 'del d["hostRules"]'; git -C "$WIDE" add -A
must_fail "an UNNAMED nuget-wide route to an auth host is still caught" "$WIDE" "$FEED" \
  "routes FS.GG.* to nuget.pkg.github.com, which REQUIRES a credential"

echo
echo "--- a STALE pin is DIAGNOSED, not merely reported (#566) --------------------------------------"
#
# This is the whole of #566. The gate used to print "the annotation manager did not bump it" for
# every stale pin — a cause it never checked, and one that reads identically whether the bot is BLIND
# to the feed or simply has not run since the release shipped. Three causes, three verdicts, and the
# RIGHT ONE has to come out, because the wrong one sends you to hand-bump the literal, which is what
# .github#127 and .github#263 both did — and the pin froze again both times.
#
# `hostRules` is PRESENT in every one of these repos. That is the point: presence is not resolution,
# and the old MECHANISM check reports green in all three.

# (a) BLIND — the bot detected the dep and never opened a PR.
BLIND_EV='{"detected": true, "bump_prs": [], "dashboard": 54}'
must_fail "stale + detected + no bump PR ever ⇒ the bot is BLIND to the feed" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "THE BOT IS BLIND TO THE FEED"
must_fail "...and says NOT to hand-bump (the .github#127/#263 paper-over)" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "do NOT hand-bump this literal"

# The REMEDIATION must depend on whether a credential is even in the path — naming one that is not
# there is naming a cause the gate did not check, which is #566 rebuilt inside the fix for #566.

# (a1) routed at the PUBLIC registry (the world after #576): there is no token to blame, and the
#      gate must not send anyone to the Mend dashboard. It must instead name the causes that remain
#      — including the CASE-SENSITIVE matcher that let `fs.gg.coord.cli` bump for months while
#      `FS.GG.SDD.Cli` froze, which is the tell that solved #576.
must_fail "a BLIND bot on a PUBLIC route is NOT diagnosed as a credential problem" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "it is NOT a credential problem"
must_fail "...and it names the CASE-SENSITIVE matcher as a live cause" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "CASE-SENSITIVE"

# (a2) routed at the AUTH feed (the world before #576): the credential IS the live hypothesis, and
#      the #263 diagnosis must survive — but it must now be preceded by "check nuget.org first",
#      because chasing the credential is what closed #127, #263 and #566 without fixing anything.
FROZEN_AUTH="$(make_repo frozenauth 0.9.0)"; repin "$FROZEN_AUTH" 0.5.0
route_to "$FROZEN_AUTH" "https://nuget.pkg.github.com/FS-GG/index.json"
must_fail "a BLIND bot on an AUTH route still names the CREDENTIAL (#263 survives)" \
  "$FROZEN_AUTH" "$(feed_evidence blind "$BLIND_EV")" \
  "is not the credential RESOLVING"
must_fail "...but tells you to check nuget.org BEFORE chasing it (#576)" \
  "$FROZEN_AUTH" "$(feed_evidence blind "$BLIND_EV")" \
  "CHECK WHETHER THE PACKAGE IS PUBLIC ON nuget.org"

# (b) BENIGN — the bot works; it opened a PR and nobody merged it. Hand-bumping here would be WRONG,
#     and the old gate told you to do exactly that.
must_fail "stale + detected + an open bump PR ⇒ benign, merge the PR" \
  "$FROZEN" "$(feed_evidence benign '{"detected": true, "bump_prs": [[91, "open", "chore(deps): update dependency fs.gg.sdd.cli to 0.9.0"]], "dashboard": 54}')" \
  "The bot IS working"
must_fail "...and points at the PR rather than the literal" \
  "$FROZEN" "$(feed_evidence benign '{"detected": true, "bump_prs": [[91, "open", "chore(deps): update dependency fs.gg.sdd.cli to 0.9.0"]], "dashboard": 54}')" \
  "#91 (open)"

# (b2) GROUPED — the bot opened no PR for THIS dep, but it plainly reaches the feed, because it has
#      opened other FS.GG.* bumps. Calling that BLIND would send someone to chase a working token.
#      This is not hypothetical: default.json carries `groupName: "FS.GG.UI coherent set"`, and a
#      grouped PR's title names the GROUP, not the member — so a member pin's name appears in no PR
#      title anywhere, exactly like a bot that never ran.
GROUPED='{"detected": true, "bump_prs": [], "feed_prs": [[77, "merged", "chore(deps): update fs.gg.ui coherent set"]], "dashboard": 54}'
must_fail "stale + no PR for this dep + OTHER FS.GG.* PRs ⇒ NOT blind (grouped/exempt)" \
  "$FROZEN" "$(feed_evidence grouped "$GROUPED")" \
  "The bot can SEE the feed"
out="$(gate "$FROZEN" "$(feed_evidence grouped "$GROUPED")")" || true
printf '%s' "$out" | grep -qF "BOT IS BLIND" \
  && bad "a GROUPED bump is not mistaken for a blind bot (the false-BLIND trap)" "$out" \
  || ok "a GROUPED bump is not mistaken for a blind bot (the false-BLIND trap)"

# (c) MANAGER BROKE — the dep is not in the dashboard at all, so the bot never saw the pin. A
#     different root cause with a different fix, and it must not be confused with a blind token.
must_fail "stale + NOT detected ⇒ the annotation manager's regex stopped matching" \
  "$FROZEN" "$(feed_evidence undetected '{"detected": false, "bump_prs": [], "dashboard": 54}')" \
  "does NOT list this dependency"

# (d) The three verdicts must be DISTINGUISHABLE, or the diagnosis is theatre. A blind bot must not
#     be described as a working one.
out="$(gate "$FROZEN" "$(feed_evidence benign '{"detected": true, "bump_prs": [[91, "open", "x fs.gg.sdd.cli x"]], "dashboard": 54}')")" || true
printf '%s' "$out" | grep -qF "BOT IS BLIND" \
  && bad "a WORKING bot is not reported as blind" "$out" \
  || ok "a WORKING bot is not reported as blind"

# (e) FAILS CLOSED. No evidence ⇒ the gate says the cause is UNVERIFIED. It must never fall back to
#     asserting one — that is the very defect (#566), and rebuilding it inside the fix would be a
#     bleak sort of joke.
NOEV="$WORK/feed-noevidence.json"
python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); d.pop("_renovate",None); json.dump(d,open(sys.argv[2],"w"))' "$FEED" "$NOEV"
must_fail "no bot evidence ⇒ the CAUSE is UNVERIFIED, never guessed" "$FROZEN" "$NOEV" "the CAUSE is UNVERIFIED"
out="$(gate "$FROZEN" "$NOEV")" || true
printf '%s' "$out" | grep -qE "BOT IS BLIND|bot IS working" \
  && bad "an unverifiable cause is not reported as a verified one" "$out" \
  || ok "an unverifiable cause is not reported as a verified one"

# These three are all AUTH-routed: an empty token, a token for the wrong host, and a token hidden in
# a lower-precedence file are only defects when a credential is actually in the path. Routed at
# public nuget.org they are non-events, and failing them there would be the gate demanding a secret
# it does not need — which is #576 itself.
EMPTYTOK="$(make_repo emptytok)"; route_to "$EMPTYTOK" "https://nuget.pkg.github.com/FS-GG/index.json"
edit_json "$EMPTYTOK/renovate.json" 'd["hostRules"][0]["token"] = ""'; git -C "$EMPTYTOK" add -A
must_fail "an auth-routed repo with an empty token fails" "$EMPTYTOK" "$FEED" "declares no \`hostRules\` token"

OTHERHOST="$(make_repo otherhost)"; route_to "$OTHERHOST" "https://nuget.pkg.github.com/FS-GG/index.json"
edit_json "$OTHERHOST/renovate.json" 'd["hostRules"][0]["matchHost"] = "nuget.org"'; git -C "$OTHERHOST" add -A
must_fail "an auth-routed repo whose token names the wrong host fails" "$OTHERHOST" "$FEED" \
  "declares no \`hostRules\` token"

NOCFG="$(make_repo nocfg)"; rm "$NOCFG/renovate.json"; git -C "$NOCFG" add -A
must_fail "a repo with pins and no Renovate config fails" "$NOCFG" "$FEED" "no Renovate configuration"

echo
echo "--- the config file read is the one Renovate would read (resolution order) ---"
# Renovate resolves .github/renovate.json BEFORE .renovaterc. Reading the wrong one answers a
# question about a config the bot never uses — and a token-bearing .renovaterc masking a token-less
# .github/renovate.json would report green while the bot goes on 401'ing.
# Auth-routed on purpose: against a public route the token is not required, so this leg would pass
# whether or not the gate ever found it — a green tick over no subject, which is the #266 shape.
DOTGH="$(make_repo dotgh)"
route_to "$DOTGH" "https://nuget.pkg.github.com/FS-GG/index.json"
rm "$DOTGH/renovate.json"
cat > "$DOTGH/.github/renovate.json" <<'JSON'
{ "extends": ["github>FS-GG/.github"],
  "hostRules": [ { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" } ] }
JSON
printf '{ "extends": ["github>FS-GG/.github"] }' > "$DOTGH/.renovaterc"
git -C "$DOTGH" add -A
must_pass "the token in .github/renovate.json is found (it outranks .renovaterc)" "$DOTGH"

MASKED="$(make_repo masked)"
route_to "$MASKED" "https://nuget.pkg.github.com/FS-GG/index.json"
rm "$MASKED/renovate.json"
printf '{ "extends": ["github>FS-GG/.github"] }' > "$MASKED/.github/renovate.json"
cat > "$MASKED/.renovaterc" <<'JSON'
{ "hostRules": [ { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" } ] }
JSON
git -C "$MASKED" add -A
must_fail "a token in the LOWER-precedence .renovaterc cannot mask a token-less .github/renovate.json" \
  "$MASKED" "$FEED" "declares no \`hostRules\` token"

echo
echo "--- a pin the manager can no longer SEE is a failure, not a shrunken gate ---"
# Cause (1) of #263: the manager's regex stops matching. A gate that scans with that same regex sees
# nothing and would report green on nothing — REQUIRED_PINS is what turns that silence into red.
#
# The repo keeps a SECOND, still-visible pin, so the subject set SHRINKS rather than empties. That is
# the fail-open shape that matters: a repo-wide "zero pins" tripwire would not have caught it, and
# the surviving pin would have reported a cheerful green.
BROKEN="$(make_repo broken)"
cat >> "$BROKEN/$PIN_FILE" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=FS.GG.Contracts
          dotnet tool install --global FS.GG.Contracts --version 1.4.0
YAML
python3 - "$BROKEN/$PIN_FILE" <<'PY'
import sys
p = sys.argv[1]
t = open(p, encoding="utf-8").read().replace("# renovate: datasource=nuget depName=FS.GG.SDD.Cli", "# (annotation removed)")
open(p, "w", encoding="utf-8").write(t)
PY
git -C "$BROKEN" add -A
must_fail "a required pin gone invisible, while another pin still passes, fails" "$BROKEN" "$FEED" "no longer sees 1 known pin(s): FS.GG.SDD.Cli in .github/workflows/contract-coherence.yml"

GONE="$(make_repo gone)"; rm "$GONE/$PIN_FILE"; git -C "$GONE" add -A
must_fail "zero pins repo-wide fails rather than reporting green" "$GONE" "$FEED" "matched ZERO pins"

echo
echo "--- the manager itself must exist and be readable ---"
NOMGR="$(make_repo nomgr)"; edit_json "$NOMGR/default.json" 'del d["customManagers"]'; git -C "$NOMGR" add -A
must_fail "a preset with no customManagers fails" "$NOMGR" "$FEED" "declares no \`customManagers\`"

NOANNO="$(make_repo noanno)"
edit_json "$NOANNO/default.json" 'd["customManagers"] = [m for m in d["customManagers"] if not any("(?<depName>" in s for s in m.get("matchStrings", []))]'
git -C "$NOANNO" add -A
must_fail "a preset with no annotation-driven manager fails" "$NOANNO" "$FEED" "no annotation-driven custom manager"

BADJSON="$(make_repo badjson)"; printf '{ not json' > "$BADJSON/default.json"; git -C "$BADJSON" add -A
must_fail "an unparsable preset fails" "$BADJSON" "$FEED" "is not valid JSON"

echo
echo "--- an unreadable or unresolvable feed fails closed ---"
must_fail "a package absent from the feed fails (404)" "$BASE" "$(feed_with empty404 '[]')" "zero versions"
NOPKG="$WORK/feed-nopkg.json"; printf '{}' > "$NOPKG"
must_fail "a feed that does not serve the package fails" "$BASE" "$NOPKG" "not on the registry"

echo
echo "--- a pin this gate cannot resolve is an error, never a skip ---"
NONNUGET="$(make_repo nonnuget)"
python3 - "$NONNUGET/$PIN_FILE" <<'PY'
import sys
p = sys.argv[1]
t = open(p, encoding="utf-8").read().replace("datasource=nuget", "datasource=github-releases")
open(p, "w", encoding="utf-8").write(t)
PY
git -C "$NONNUGET" add -A
must_fail "an unresolvable datasource fails" "$NONNUGET" "$FEED" "is not one this gate can resolve"

THIRDPARTY="$(make_repo thirdparty)"
python3 - "$THIRDPARTY/$PIN_FILE" <<'PY'
import sys
p = sys.argv[1]
t = open(p, encoding="utf-8").read()
t = t.replace("depName=FS.GG.SDD.Cli", "depName=Expecto").replace("--global FS.GG.SDD.Cli", "--global Expecto")
open(p, "w", encoding="utf-8").write(t)
PY
git -C "$THIRDPARTY" add -A
# Two failures are correct here and both matter: the required pin vanished, AND a non-FS.GG package
# cannot be resolved against the org feed. The first fires first; assert it, then assert the second
# in isolation by adding the third-party pin ALONGSIDE the required one.
must_fail "renaming the required pin away fails" "$THIRDPARTY" "$FEED" "no longer sees 1 known pin(s): FS.GG.SDD.Cli in .github/workflows/contract-coherence.yml"

EXTRA="$(make_repo extra)"
cat >> "$EXTRA/$PIN_FILE" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=Expecto
          dotnet tool install --global Expecto --version 10.2.1
YAML
git -C "$EXTRA" add -A
must_fail "a non-FS.GG package cannot be resolved against the org feed" "$EXTRA" "$FEED" "is not an FS.GG.* package"

echo
echo "--- the gate scans exactly what the bot scans (Renovate's ignorePaths) ---"
# default.json extends config:recommended -> :ignoreModulesAndTests, whose ignorePaths exclude
# **/tests/** and friends. A pin there is one Renovate will never bump, so reddening over it would
# make the gate stricter than the bot. This fixture is itself a `.sh` file matching the manager's
# managerFilePatterns, and its heredocs carry annotation-shaped pins — which is how this was found.
IGNORED="$(make_repo ignored)"
mkdir -p "$IGNORED/tests/fixtures"
cat > "$IGNORED/tests/fixtures/sample.sh" <<'YAML'
# renovate: datasource=nuget depName=Expecto
dotnet tool install --global Expecto --version 10.2.1
YAML
git -C "$IGNORED" add -A
must_pass "a pin under tests/ is skipped, as Renovate skips it" "$IGNORED"

# ...but the skip must never swallow a REQUIRED pin. Move the operative pin into an ignored path and
# leave a second, visible pin behind, so the repo is not merely empty: the zero-pins tripwire must not
# be what saves us here. Only REQUIRED_PINS can catch a pin that was parked somewhere unscanned.
IGNOREDREQ="$(make_repo ignoredreq)"
mkdir -p "$IGNOREDREQ/tests"
mv "$IGNOREDREQ/$PIN_FILE" "$IGNOREDREQ/tests/contract-coherence.yml"
cat > "$IGNOREDREQ/.github/workflows/other.yml" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=FS.GG.Contracts
          dotnet tool install --global FS.GG.Contracts --version 1.4.0
YAML
git -C "$IGNOREDREQ" add -A
must_fail "an ignored path cannot hide a REQUIRED pin" "$IGNOREDREQ" "$FEED" "no longer sees 1 known pin(s)"

echo
echo "--- fixture mode announces itself, and is locked to this harness ---"
out="$(gate "$BASE")"
printf '%s' "$out" | grep -q "FIXTURE MODE" \
  && ok "fixture mode prints a banner" \
  || bad "fixture mode prints a banner" "$out"

out="$(FSGG_PIN_FIXTURE_OK= python3 "$GATE" --root "$BASE" --fixture "$FEED" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && printf '%s' "$out" | grep -q "Refusing to run"; then
  ok "--fixture refuses to run without FSGG_PIN_FIXTURE_OK"
else
  bad "--fixture refuses to run without FSGG_PIN_FIXTURE_OK" "$out"
fi

echo
echo "--- fails CLOSED on an unreadable feed (no --fixture, no token) ---"
out="$(python3 "$GATE" --root "$BASE" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && printf '%s' "$out" | grep -q "no GITHUB_TOKEN/GH_TOKEN"; then
  ok "a missing token fails the gate rather than skipping it"
else
  bad "a missing token fails the gate rather than skipping it" "$out"
fi

echo
echo "--- CI guard on the real repo (no network: structure only) ---"
# Proves REQUIRED_PINS still names a pin that exists here, and that this repo's own routing is one
# Renovate can actually read — the two things a refactor of this repo could quietly break. Feed
# comparison needs network and is covered by the workflow's live run.
#
# It also asserts the ROUTE itself, because that is the fact #576 turned on: if someone repoints the
# preset back at nuget.pkg.github.com, this repo needs a credential again and the pin can freeze a
# fifth time. That must break a test, not a release.
out="$(python3 - "$REPO_ROOT" "$GATE" <<'PY' 2>&1
import importlib.util, sys
root, gate_path = sys.argv[1:3]
spec = importlib.util.spec_from_file_location("gate", gate_path)
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
cfg, hosts = gate.check_bump_mechanism(root, f"{root}/default.json")
assert hosts, "the preset routes FS.GG.* nowhere at all"
unreadable = [h for h in hosts if h not in gate.PUBLIC_HOSTS and h not in gate.AUTH_HOSTS]
assert not unreadable, f"the preset routes FS.GG.* to unreadable host(s): {unreadable}"
rx, mp = gate.load_annotation_manager(f"{root}/default.json")
pins = gate.scan_pins(root, rx, mp)
gate.assert_required_pins(pins)
assert pins, "no pins found in the real repo"
print(f"ok: {len(pins)} pin(s); every REQUIRED_PINS entry present; FS.GG.* routed to {', '.join(hosts)}")
PY
)" && rc=0 || rc=$?
if [ "${rc:-0}" -eq 0 ]; then ok "real repo: required pins present, bump mechanism configured"; else bad "real repo structure" "$out"; fi

echo
echo "$pass passed, $failcount failed."
[ "$failcount" -eq 0 ]
