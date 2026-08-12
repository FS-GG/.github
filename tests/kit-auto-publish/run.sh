#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
esc_work="$(mktemp -d)"
trap 'rm -rf "$work" "$esc_work"' EXIT
checks=0
case_run() {
  local name="$1" expected="$2" facts="$3"
  printf '%s' "$facts" > "$work/$name.json"
  actual="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/$name.json" --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["action"])')"
  [ "$actual" = "$expected" ] || { echo "$name: expected $expected, got $actual" >&2; exit 1; }
  checks=$((checks + 1))
}
base='"provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false'
case_run eligible tag "{\"version\":\"0.27.1\",$base}"
case_run major refuse "{\"version\":\"1.0.0\",$base}"
case_run partial stickyEscalate "{\"version\":\"0.27.1\",${base/\"orgFeed\":\"absent\"/\"orgFeed\":\"present\"},\"nugetFeed\":\"absent\"}"
case_run older-gap refuse '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.28.0","nugetLatest":"0.28.0","tagExists":false}'
case_run frontier-disagree stickyEscalate '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.28.0","tagExists":false}'
case_run unrelated-later-pr refuse '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.0","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false}'
facts='{ "version":"0.27.1", "provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"}, "orgFeed":"unknown", "nugetFeed":"absent", "orgLatest":"0.27.0", "nugetLatest":"0.27.0", "tagExists":false }'
case_run released-after-later-merge openEvidencePr '{"version":"0.27.1","sourceSha":"later","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass","mergeCommit":"authoring"},"orgFeed":"present","nugetFeed":"present","orgLatest":"0.27.1","nugetLatest":"0.27.1","tagExists":true,"releaseRun":{"id":"42","url":"https://example.test/run/42","nuspecCommit":"later"}}'
printf '%s' "$facts" > "$work/escalate.json"
printf '%s' '{"valid":true,"streak":2,"action":"refuse","reason":"feed-observation-unknown","version":"0.27.1","lastRun":"1"}' > "$work/previous.json"
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 1 | jq -c .escalation)"
[ "$(jq -r .streak <<<"$state")" = 2 ] || { echo 'same run did not stay idempotent' >&2; exit 1; }; checks=$((checks + 1))
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 2 | jq -c .escalation)"
[ "$(jq -r .streak <<<"$state")" = 3 ] || { echo 'new run did not increment streak' >&2; exit 1; }; checks=$((checks + 1))
printf '%s' '{"valid":true,"streak":2,"action":"refuse","reason":"other","version":"0.27.1","lastRun":"1"}' > "$work/previous.json"
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 3 | jq -c .escalation)"
[ "$(jq -r .streak <<<"$state")" = 3 ] || { echo 'transition did not increment streak' >&2; exit 1; }; checks=$((checks + 1))
printf '%s' 'not-json' > "$work/previous.json"
state="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/escalate.json" --previous-escalation "$work/previous.json" --run 4 | jq -c .escalation)"
[ "$(jq -r .valid <<<"$state")" = false ] || { echo 'malformed marker did not fail closed' >&2; exit 1; }; checks=$((checks + 1))

# ---- Escalation body construction round-trips through this workflow's own readers (.github#2346) --
#
# The escalation step in kit-auto-publish.yml crashed at exit 127 before it ever wrote the sticky
# marker or the `blocked` label: a double-quoted assignment left its fence backticks live for command
# substitution and its `\n` as a literal backslash-n rather than a newline. Both refusals the branch
# has ever reached died there, silently — a red run on `main` that read like any other CI failure.
#
# This extracts the REAL "Escalate a non-eligible or partial state once" step out of the REAL
# workflow and executes it end to end against a stubbed `gh`, so a regression here reds THIS test
# instead of shipping latent until the next live refusal (acceptance criterion 5).
extract_escalation_step() {
  python3 - "$root/.github/workflows/kit-auto-publish.yml" "$1" <<'PY'
import sys
import yaml

wf_path, out_path = sys.argv[1], sys.argv[2]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)
steps = (wf.get("jobs") or {}).get("decide", {}).get("steps") or []
step = next((s for s in steps if s.get("name") == "Escalate a non-eligible or partial state once"), None)
if step is None:
    sys.exit("the escalation step is GONE from kit-auto-publish.yml")
with open(out_path, "w", encoding="utf-8") as out:
    out.write("#!/usr/bin/env bash\n")
    out.write(step["run"])
PY
}

esc_script="$esc_work/escalate.sh"
extract_escalation_step "$esc_script"
chmod +x "$esc_script"

# The MUTATED copy: the exact pre-fix defect, reintroduced textually into a copy of the real step so
# gate-inversion evidence (pnext-item §3) is a permanent, re-runnable leg rather than a one-time manual
# observation. Built with python (not shell) so the literal fence backticks below are never re-parsed
# by THIS script's own shell — they are file bytes, not a command substitution here either.
mutate_to_predefect() {
  python3 - "$esc_script" "$1" <<'PY'
import sys

src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out, replaced = [], False
for line in lines:
    if line.lstrip().startswith('body="$(printf'):
        out.append(
            'body="$marker\\n\\n```json\\n$state\\n```\\n\\nAuto-publish performed no tag, feed, '
            'or evidence-PR write. [Run]($GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/'
            '$GITHUB_RUN_ID)."\n'
        )
        replaced = True
    else:
        out.append(line)
if not replaced:
    sys.exit("could not locate the fixed body= line to mutate back to the pre-fix defect")
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}

# A stub `gh`: no network, no real issue. Every call is logged to $STUB_DIR/calls.log and a captured
# comment body lands in $STUB_DIR/last-body.txt, so assertions read exactly what the script under
# test would have sent GitHub.
write_gh_stub() {
  cat > "$1/bin/gh" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
log="$STUB_DIR/calls.log"
if [ "$1" = "api" ] && [ "$2" = "-X" ]; then
  method="$3"; target="$4"; shift 4
  body=""
  for arg in "$@"; do case "$arg" in body=*) body="${arg#body=}" ;; esac; done
  printf '%s' "$body" > "$STUB_DIR/last-body.txt"
  echo "$method $target" >> "$log"
  echo '{}'
  exit 0
fi
if [ "$1" = "api" ]; then
  target="$2"
  echo "GET $target" >> "$log"
  case "$target" in
    */comments) cat "$STUB_DIR/comments.json" ;;
    *) cat "$STUB_DIR/issue-state.json" ;;
  esac
  exit 0
fi
if [ "$1" = "issue" ] && [ "$2" = "reopen" ]; then
  echo "REOPEN $3" >> "$log"
  exit 0
fi
if [ "$1" = "issue" ] && [ "$2" = "edit" ]; then
  echo "LABEL $3 blocked" >> "$log"
  exit 0
fi
echo "unstubbed gh invocation: $*" >&2
exit 99
STUB
  chmod +x "$1/bin/gh"
}

# sandbox <name> <comments-json> [<issue-state-json>] -> prints the sandbox dir. Copies the REAL
# kit-auto-publish.py in (relative `scripts/kit-auto-publish.py` is what the step itself invokes) and
# seeds the stubbed `gh api .../comments` and `gh api .../issues/<n>` reads. Issue state defaults to
# open, matching #2106's steady state; a test opts into "closed" explicitly.
sandbox() {
  local d="$esc_work/$1" state="${3:-}"
  mkdir -p "$d/bin" "$d/scripts"
  cp "$root/scripts/kit-auto-publish.py" "$d/scripts/"
  write_gh_stub "$d"
  printf '%s' "$2" > "$d/comments.json"
  [ -n "$state" ] || state='{"state":"open"}'
  printf '%s' "$state" > "$d/issue-state.json"
  printf '%s' "$d"
}

# run_escalation <dir> <facts-json> <run-id> <script> -> exit code of the step; logs under <dir>.
run_escalation() {
  local dir="$1" facts="$2" run_id="$3" script="$4" rc=0
  printf '%s' "$facts" > "$dir/facts.json"
  ( cd "$dir" \
    && STUB_DIR="$dir" PATH="$dir/bin:$PATH" GH_TOKEN=fake-token \
       GITHUB_REPOSITORY=FS-GG/.github GITHUB_SERVER_URL=https://github.example.test \
       GITHUB_RUN_ID="$run_id" bash "$script" >stdout.log 2>stderr.log ) || rc=$?
  return "$rc"
}

esc_facts="{\"version\":\"1.0.0\",$base}"   # the existing "major" case: a stable refuse, every run.

# ---- 1. A fresh refusal: no prior comment, so this must POST once, label once, and round-trip. ----
d1="$(sandbox fresh '[]')"
rc=0; run_escalation "$d1" "$esc_facts" 1001 "$esc_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "fresh escalation: expected exit 0, got $rc" >&2; cat "$d1/stderr.log" >&2; exit 1; }
checks=$((checks + 1))

grep -qx 'POST repos/FS-GG/.github/issues/2106/comments' "$d1/calls.log" \
  || { echo 'fresh escalation: expected a POST of a new sticky comment' >&2; cat "$d1/calls.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qE '^PATCH' "$d1/calls.log" \
  && { echo 'fresh escalation: no comment existed yet — a PATCH must not happen' >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'LABEL 2106 blocked' "$d1/calls.log" \
  || { echo 'fresh escalation: expected the blocked label to be applied' >&2; cat "$d1/calls.log" >&2; exit 1; }
checks=$((checks + 1))

# The reader at line 192 of the real step: does the posted body start with the sticky marker?
first_line="$(head -1 "$d1/last-body.txt")"
[ "$first_line" = '<!-- fsgg:kit-auto-publish-escalation -->' ] \
  || { echo "fresh escalation: body does not start with the marker, got: $first_line" >&2; exit 1; }
checks=$((checks + 1))

# The reader at line 194 of the real step: does the fenced JSON round-trip byte-for-byte through the
# SAME sed extraction the step itself uses to recover a prior escalation's state?
extracted="$(sed -n '/^```json$/,/^```$/p' "$d1/last-body.txt" | sed '1d;$d')"
expected="$(jq -c .escalation "$d1/escalation-decision.json")"
[ "$extracted" = "$expected" ] \
  || { echo "fresh escalation: fenced JSON did not round-trip. got: $extracted want: $expected" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .streak <<<"$extracted")" = 1 ] \
  || { echo "fresh escalation: first-ever streak must be 1, got $(jq -r .streak <<<"$extracted")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 2. A second refusal with the sticky comment already present: update in place, not duplicate. -
d2="$(sandbox repeat "$(jq -n --rawfile b "$d1/last-body.txt" '[{id:555, body:$b}]')")"
rc=0; run_escalation "$d2" "$esc_facts" 1002 "$esc_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "repeat escalation: expected exit 0, got $rc" >&2; cat "$d2/stderr.log" >&2; exit 1; }
checks=$((checks + 1))

grep -qx 'PATCH repos/FS-GG/.github/issues/comments/555' "$d2/calls.log" \
  || { echo 'repeat escalation: expected the existing sticky comment to be PATCHed' >&2; cat "$d2/calls.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qE '^POST' "$d2/calls.log" \
  && { echo 'repeat escalation: a sticky comment already existed — must not POST a duplicate' >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .escalation.streak "$d2/escalation-decision.json")" = 2 ] \
  || { echo "repeat escalation: streak must increment to 2, got $(jq -r .escalation.streak "$d2/escalation-decision.json")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 3. GATE-INVERSION EVIDENCE (pnext-item §3): the pre-fix defect must fail, and fail exactly as
#         the two live runs (31395781442, 31424562713) did — exit 127, before ANY label or comment
#         write. A test that cannot fail on the original defect has not tested the fix.
d3="$(sandbox mutated '[]')"
buggy_script="$esc_work/escalate-buggy.sh"
mutate_to_predefect "$buggy_script"
rc=0; run_escalation "$d3" "$esc_facts" 2001 "$buggy_script" || rc=$?
[ "$rc" -eq 127 ] \
  || { echo "gate-inversion: reintroduced defect must exit 127, got $rc" >&2; cat "$d3/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qi 'command not found' "$d3/stderr.log" \
  || { echo 'gate-inversion: expected a "command not found" failure from the live command substitution' >&2; cat "$d3/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ ! -f "$d3/calls.log" ] || ! grep -qE '^(LABEL|POST|PATCH)' "$d3/calls.log" \
  || { echo 'gate-inversion: the escalation is total on the real defect — no label or comment write may occur' >&2; cat "$d3/calls.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- 4. The escalation TARGET must be OPEN (.github#2435 AC2): a closed #2106 is reopened as part
#         of writing the sticky comment, so a live streak always reads from an open-issue view. ----
d4="$(sandbox closed-target '[]' '{"state":"closed"}')"
rc=0; run_escalation "$d4" "$esc_facts" 3001 "$esc_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "closed-target escalation: expected exit 0, got $rc" >&2; cat "$d4/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'REOPEN 2106' "$d4/calls.log" \
  || { echo 'closed-target escalation: expected the escalation target to be reopened' >&2; cat "$d4/calls.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- 5. An already-open target must not be needlessly reopened. ----
d5="$(sandbox open-target '[]' '{"state":"open"}')"
rc=0; run_escalation "$d5" "$esc_facts" 3002 "$esc_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "open-target escalation: expected exit 0, got $rc" >&2; cat "$d5/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'REOPEN 2106' "$d5/calls.log" \
  && { echo 'open-target escalation: an already-open issue must not be reopened' >&2; exit 1; }
checks=$((checks + 1))

marker_literal='<!-- fsgg:kit-auto-publish-escalation -->'
seed_comment() {
  local streak="$1" run="$2"
  printf '%s\n\n```json\n%s\n```\n\nAuto-publish performed no tag, feed, or evidence-PR write. [Run](x).' \
    "$marker_literal" "{\"valid\":true,\"streak\":$streak,\"action\":\"refuse\",\"reason\":\"major\",\"version\":\"1.0.0\",\"lastRun\":\"$run\"}"
}

# ---- 6. Below the bound: a prior streak of 1 becomes 2 on a new run (< threshold 3) — stays green.
d6="$(sandbox below-threshold "$(jq -n --arg b "$(seed_comment 1 4001)" '[{id:601, body:$b}]')")"
rc=0; run_escalation "$d6" "$esc_facts" 4002 "$esc_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "below-threshold escalation: expected exit 0 (streak 2 < 3), got $rc" >&2; cat "$d6/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .escalation.streak "$d6/escalation-decision.json")" = 2 ] \
  || { echo "below-threshold escalation: expected streak 2, got $(jq -r .escalation.streak "$d6/escalation-decision.json")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 7. At the bound: a prior streak of 2 becomes 3 (>= threshold 3) — the job must fail, but only
#         AFTER the sticky comment and label are written: the durable record is not sacrificed to the
#         CI-visibility signal (acceptance criterion 1: "either the run fails ... or the escalation
#         opens/updates an open tracking row, or both" — this exercises both together). ----
d7="$(sandbox at-threshold "$(jq -n --arg b "$(seed_comment 2 5001)" '[{id:701, body:$b}]')")"
rc=0; run_escalation "$d7" "$esc_facts" 5002 "$esc_script" || rc=$?
[ "$rc" -eq 1 ] || { echo "at-threshold escalation: expected exit 1 (streak 3 >= 3), got $rc" >&2; cat "$d7/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'PATCH repos/FS-GG/.github/issues/comments/701' "$d7/calls.log" \
  || { echo 'at-threshold escalation: the sticky comment must still be written before the job fails' >&2; cat "$d7/calls.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'LABEL 2106 blocked' "$d7/calls.log" \
  || { echo 'at-threshold escalation: the label must still be applied before the job fails' >&2; cat "$d7/calls.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qi 'streak' "$d7/stdout.log" \
  || { echo 'at-threshold escalation: expected a streak-bound error annotation' >&2; cat "$d7/stdout.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- 8. GATE-INVERSION for the streak bound: strip the threshold check back out of a copy of the
#         real step and show a streak of 5 (well past the bound) now stays silently green — exactly
#         the invisible-refusal shape .github#2435 documents. A test that cannot fail on the absence
#         of this check has not tested it. ----
mutate_remove_threshold() {
  python3 - "$esc_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out, skipping, removed = [], False, False
for line in lines:
    if line.lstrip().startswith('streak="$(jq -r'):
        skipping = True
        removed = True
        continue
    if skipping:
        if line.strip() == 'fi':
            skipping = False
        continue
    out.append(line)
if not removed:
    sys.exit("could not locate the streak-threshold block to strip")
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}
no_threshold_script="$esc_work/escalate-no-threshold.sh"
mutate_remove_threshold "$no_threshold_script"
d8="$(sandbox no-threshold "$(jq -n --arg b "$(seed_comment 4 6001)" '[{id:801, body:$b}]')")"
rc=0; run_escalation "$d8" "$esc_facts" 6002 "$no_threshold_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "gate-inversion (streak bound): without the check, a streak past the bound should stay green like before the fix, got $rc" >&2; cat "$d8/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .escalation.streak "$d8/escalation-decision.json")" -ge 3 ] \
  || { echo "gate-inversion (streak bound): sanity check that the streak is really at/above the bound failed" >&2; exit 1; }
checks=$((checks + 1))

# ---- 9. `csproj_version`: PROVENANCE ROOT CAUSE (.github#2435). .github#2410 (coherent-set
#         versioning) turned FS.GG.Kit.csproj's <Version> into an MSBuild property REFERENCE,
#         `$(FsggCoherentSetVersion)`. A raw regex over just the csproj reads that reference as its
#         own literal text, which never matches the msbuild-resolved `$version` computed elsewhere in
#         the observation step — so the provenance search never found a matching merge for ANY future
#         version, and the state machine refused every version forever. This extracts the REAL helper
#         out of the REAL workflow and runs it against a synthetic git repo covering both the plain
#         literal form and the property-indirection form. ----
extract_csproj_version_fn() {
  awk '/^[[:space:]]*# BEGIN kit-csproj-version$/{flag=1; next} /^[[:space:]]*# END kit-csproj-version$/{flag=0} flag' \
    "$root/.github/workflows/kit-auto-publish.yml" > "$1"
  grep -q 'csproj_version()' "$1" \
    || { echo "csproj_version() function is GONE from kit-auto-publish.yml" >&2; exit 1; }
}

fn_work="$esc_work/fn"
mkdir -p "$fn_work"
csproj_version_fn="$fn_work/csproj_version.sh"
extract_csproj_version_fn "$csproj_version_fn"

repo="$fn_work/repo"
mkdir -p "$repo/src/FS.GG.Kit"
git -C "$fn_work" init -q repo
git -C "$repo" config user.email test@example.test
git -C "$repo" config user.name test
printf '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>' > "$repo/src/FS.GG.Kit/FS.GG.Kit.csproj"
git -C "$repo" add -A && git -C "$repo" commit -q -m literal
literal_commit="$(git -C "$repo" rev-parse HEAD)"
printf '<Project><PropertyGroup><FsggCoherentSetVersion>1.3.0</FsggCoherentSetVersion></PropertyGroup></Project>' > "$repo/Directory.Build.props"
printf '<Project><PropertyGroup><Version>$(FsggCoherentSetVersion)</Version></PropertyGroup></Project>' > "$repo/src/FS.GG.Kit/FS.GG.Kit.csproj"
git -C "$repo" add -A && git -C "$repo" commit -q -m indirection
indirection_commit="$(git -C "$repo" rev-parse HEAD)"

run_csproj_version() {
  ( cd "$repo" && bash -c "source '$1'; csproj_version '$2' 'src/FS.GG.Kit/FS.GG.Kit.csproj'" )
}

got="$(run_csproj_version "$csproj_version_fn" "$literal_commit")"
[ "$got" = "1.2.3" ] \
  || { echo "csproj_version: literal form expected 1.2.3, got '$got'" >&2; exit 1; }
checks=$((checks + 1))

got="$(run_csproj_version "$csproj_version_fn" "$indirection_commit")"
[ "$got" = "1.3.0" ] \
  || { echo "csproj_version: property-indirection form expected 1.3.0 resolved through Directory.Build.props, got '$got'" >&2; exit 1; }
checks=$((checks + 1))

# ---- 10. GATE-INVERSION for the provenance fix: strip the property-indirection branch back to the
#          pre-fix naive regex and show it now returns the UNRESOLVED literal property-reference text
#          instead of the real version — reproducing exactly the defect that made kit-auto-publish
#          refuse FS.GG.Kit 0.50.0 forever (streak 17 and rising, per .github#2435's live evidence).
naive_csproj_version="$fn_work/csproj_version_naive.sh"
cat > "$naive_csproj_version" <<'NAIVE'
csproj_version() {
  local commit="$1" path="$2"
  git show "$commit:$path" 2>/dev/null | sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' | tail -1
}
NAIVE
got="$(run_csproj_version "$naive_csproj_version" "$indirection_commit")"
[ "$got" = '$(FsggCoherentSetVersion)' ] \
  || { echo "gate-inversion (provenance): the pre-fix regex should misread the property reference as its own literal text, got '$got'" >&2; exit 1; }
checks=$((checks + 1))
[ "$got" != "1.3.0" ] \
  || { echo "gate-inversion (provenance): the naive form must NOT resolve to the real version" >&2; exit 1; }
checks=$((checks + 1))

echo "kit auto-publish state machine: $checks passed"
