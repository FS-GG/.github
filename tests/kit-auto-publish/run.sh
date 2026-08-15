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
# .github#2498 round-1 repair 1: the sibling-tag check now runs on EVERY path that can reach a tag
# write, including this FRESH one — so every fixture below that is meant to reach "tag" needs a
# verified-absent `siblingTags` observation and a `sourceSha` (the commit the fresh tag will name),
# not just the fixtures that were already exercising the `tagExists:true` repair branch.
base='"provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false,"sourceSha":"cccccccccccccccccccccccccccccccccccccc","siblingTags":{"drivers":"","coordEngine":""}'
case_run eligible tag "{\"version\":\"0.27.1\",$base}"
case_run major refuse "{\"version\":\"1.0.0\",$base}"
case_run partial stickyEscalate "{\"version\":\"0.27.1\",${base/\"orgFeed\":\"absent\"/\"orgFeed\":\"present\"},\"nugetFeed\":\"absent\"}"
case_run older-gap refuse '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.28.0","nugetLatest":"0.28.0","tagExists":false}'
case_run frontier-disagree stickyEscalate '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.28.0","tagExists":false}'
case_run unrelated-later-pr refuse '{"version":"0.27.1","provenance":{"mergedReachable":true,"introducedVersion":"0.27.0","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false}'
case_run coherent-set-minor refuse '{"version":"0.51.0","provenance":{"mergedReachable":true,"introducedVersion":"0.51.0","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.50.2","nugetLatest":"0.50.2","tagExists":false}'

# ---- .github#2495: `kit/v$version` alone leaves FS.GG.Kit unpublished forever — release-kit.yml's
#      sibling-tag precondition (.github#2409 DEC-004) refuses until `drivers/v$version` and
#      `coord-engine/v$version` ALSO resolve to the kit tag's commit. These fixtures reproduce the
#      real, currently-owed live state (kit/v0.50.3 tagged alone at 42c63af9, both siblings absent,
#      neither feed published yet) and its neighboring cases. ----
assert_reason() {
  local name="$1" expected="$2" actual
  actual="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/$name.json" --json | jq -r .reason)"
  [ "$actual" = "$expected" ] || { echo "$name: expected reason $expected, got $actual" >&2; exit 1; }
  checks=$((checks + 1))
}
tagged_base='"provenance":{"mergedReachable":true,"introducedVersion":"0.50.3","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.50.2","nugetLatest":"0.50.2","tagExists":true,"sourceSha":"deadbeef"'

# Both siblings missing — the live 0.50.3 shape: repair by tagging both.
case_run siblings-missing tagSiblings "{\"version\":\"0.50.3\",$tagged_base,\"siblingTags\":{\"drivers\":\"\",\"coordEngine\":\"\"}}"
assert_reason siblings-missing kit-tagged-siblings-missing

# One sibling already present (at the SAME commit) — repair only the other.
case_run siblings-partial tagSiblings "{\"version\":\"0.50.3\",$tagged_base,\"siblingTags\":{\"drivers\":\"deadbeef\",\"coordEngine\":\"\"}}"
assert_reason siblings-partial kit-tagged-siblings-missing

# All three tags present at the same commit, feeds not yet published — genuinely just waiting on
# release-kit.yml to run, nothing left for kit-auto-publish to write.
case_run siblings-complete stickyEscalate "{\"version\":\"0.50.3\",$tagged_base,\"siblingTags\":{\"drivers\":\"deadbeef\",\"coordEngine\":\"deadbeef\"}}"
assert_reason siblings-complete tag-exists-without-both-feed-publication

# A sibling tag exists but names a DIFFERENT commit than kit's — an anomaly, never silently overwritten.
case_run siblings-mismatch stickyEscalate "{\"version\":\"0.50.3\",$tagged_base,\"siblingTags\":{\"drivers\":\"wrongsha\",\"coordEngine\":\"\"}}"
assert_reason siblings-mismatch sibling-tag-commit-mismatch

# `siblingTags` absent entirely (an observation-step regression) — fail closed rather than treat a
# missing read as "both siblings verified absent, safe to tag".
case_run siblings-observation-missing refuse "{\"version\":\"0.50.3\",$tagged_base}"
assert_reason siblings-observation-missing sibling-tag-observation-missing

# `sourceSha` absent while `tagExists` is true — the same fail-closed rule; there is no commit to pin
# a repair tag to.
tagged_no_source_sha='"provenance":{"mergedReachable":true,"introducedVersion":"0.50.3","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.50.2","nugetLatest":"0.50.2","tagExists":true'
case_run siblings-no-source-sha refuse "{\"version\":\"0.50.3\",$tagged_no_source_sha,\"siblingTags\":{\"drivers\":\"\",\"coordEngine\":\"\"}}"
assert_reason siblings-no-source-sha sibling-tag-observation-missing

# ---- .github#2498 round-1 repair 1: independent review reproduced, end to end against a real git
#      remote, a blind spot in the FIRST draft of the sibling-tag check above: it lived entirely inside
#      `if facts.get("tagExists"):`, so a candidate with kit NOT yet tagged but a SIBLING already
#      present (e.g. left behind by a non-atomic partial push, or any out-of-band source) fell straight
#      through to `"tag"` unchecked — the exact mirror of the case this item exists to repair, and
#      reachable by this PR's own new multi-ref push before the `--atomic` fix below closed that
#      specific cause. These fixtures are the FRESH path (`tagExists:false`) with a sibling already
#      present, which the shipped code above now also covers (the check runs before the `tagExists`
#      branch, not inside it). ----
fresh_base='"provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.27.0","nugetLatest":"0.27.0","tagExists":false,"sourceSha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"'

# A sibling tag already exists at a DIFFERENT commit than the one this run's fresh kit tag would name
# — exactly the critic's reproduction (drivers seeded at commit A, candidate about to tag at commit B).
case_run fresh-sibling-mismatch stickyEscalate "{\"version\":\"0.27.1\",$fresh_base,\"siblingTags\":{\"drivers\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"coordEngine\":\"\"}}"
assert_reason fresh-sibling-mismatch sibling-tag-commit-mismatch

# `siblingTags` absent entirely on the fresh path — fail closed, same as the already-tagged path.
case_run fresh-siblings-observation-missing refuse "{\"version\":\"0.27.1\",$fresh_base}"
assert_reason fresh-siblings-observation-missing sibling-tag-observation-missing

# A sibling ALREADY exists but at the SAME commit the fresh kit tag would name — consistent, not an
# anomaly (e.g. one sibling landed from an earlier successful partial push whose other refs failed);
# `tag` proceeds, and `tag_one`'s own per-ref existence check will skip re-creating it.
case_run fresh-siblings-already-consistent tag "{\"version\":\"0.27.1\",$fresh_base,\"siblingTags\":{\"drivers\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"coordEngine\":\"\"}}"

# ---- GATE-INVERSION EVIDENCE (pnext-item §3) for round-1 repair 1: reproduce the EXACT pre-repair
#      structure — the sibling-tag check moved back to living ONLY inside `if facts.get("tagExists"):`
#      — by relocating the marked `sibling-tag-check` block from its current (unconditional) position to
#      immediately inside that `if`, byte-identical to the round-1-reviewed shape. This must (a) let
#      `fresh-sibling-mismatch` above fall through to `"tag"` again — the regression the critic found —
#      while (b) leaving the already-tagged-path fixtures (`siblings-missing` etc., above) working
#      exactly as before, since round 1's bug was ONLY that the check was misplaced, not that it was
#      wrong. A mutation that cannot reproduce both halves has not isolated the real defect. ----
mutate_to_round1_structure() {
  python3 - "$root/scripts/kit-auto-publish.py" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
start_marker = "    # BEGIN sibling-tag-check"
end_marker = "    # END sibling-tag-check\n"
start = text.index(start_marker)
end = text.index(end_marker, start) + len(end_marker)
block = text[start:end]
without_block = text[:start] + text[end:]
indented_block = "".join(("    " + line if line.strip() else line) for line in block.splitlines(keepends=True))
anchor = '    if facts.get("tagExists"):\n'
anchor_idx = without_block.index(anchor)
if anchor_idx > start:
    anchor_idx -= len(block)
insert_at = anchor_idx + len(anchor)
mutated = without_block[:insert_at] + indented_block + without_block[insert_at:]
if mutated == text:
    sys.exit("mutate_to_round1_structure: sibling-tag-check block not found/unchanged")
open(dst, "w", encoding="utf-8").write(mutated)
PY
}
round1_script="$work/kit-auto-publish-round1.py"
mutate_to_round1_structure "$round1_script"
python3 -c "import ast; ast.parse(open('$round1_script').read())" \
  || { echo "gate-inversion (round-1 structure): mutated script is not valid Python" >&2; exit 1; }
checks=$((checks + 1))

# (a) the regression reappears: the fresh-path mismatch is no longer caught.
round1_action="$(python3 "$round1_script" --facts "$work/fresh-sibling-mismatch.json" --json | jq -r .action)"
[ "$round1_action" = tag ] \
  || { echo "gate-inversion (round-1 structure): reverted logic should let fresh-sibling-mismatch fall through to 'tag' (the exact regression), got $round1_action" >&2; exit 1; }
checks=$((checks + 1))

# (b) the already-tagged path is UNCHANGED by this mutation — isolates that round 1's bug was placement,
#     not the check's own logic.
round1_repair_action="$(python3 "$round1_script" --facts "$work/siblings-missing.json" --json | jq -r .action)"
[ "$round1_repair_action" = tagSiblings ] \
  || { echo "gate-inversion (round-1 structure): the already-tagged repair path must be unaffected by this mutation, got $round1_repair_action" >&2; exit 1; }
checks=$((checks + 1))
round1_mismatch_action="$(python3 "$round1_script" --facts "$work/siblings-mismatch.json" --json | jq -r .action)"
[ "$round1_mismatch_action" = stickyEscalate ] \
  || { echo "gate-inversion (round-1 structure): the already-tagged mismatch path must be unaffected by this mutation, got $round1_mismatch_action" >&2; exit 1; }
checks=$((checks + 1))

# ---- .github#2498 round-1 repair 1, REAL-REMOTE REPRODUCTION: the critic's own reproduction, re-run
#      against the FIXED code, gathering facts the way the REAL observation step does (git rev-parse
#      against a real remote), not hand-typed JSON. Seed `drivers/v0.60.1` at commit A on a real bare
#      remote, advance local HEAD to commit B (the fresh candidate's `sourceSha`/`$GITHUB_SHA`), and
#      confirm the shipped decision engine now refuses to authorize `"tag"` in this state. ----
real_remote_check_work="$esc_work/real-remote-sibling-check"
mkdir -p "$real_remote_check_work"
real_remote="$real_remote_check_work/origin.git"
git init --quiet --bare "$real_remote"
rr_home="$real_remote_check_work/home"; mkdir -p "$rr_home"
rr_repo="$real_remote_check_work/repo"
git -c init.defaultBranch=main clone --quiet "$real_remote" "$rr_repo"
( cd "$rr_repo" && HOME="$rr_home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test commit --quiet --allow-empty -m commit-a \
    && HOME="$rr_home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
commit_a="$(git -C "$rr_repo" rev-parse HEAD)"
( cd "$rr_repo" && HOME="$rr_home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test tag -a "drivers/v0.60.1" "$commit_a" -m seed \
    && HOME="$rr_home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin refs/tags/drivers/v0.60.1 )
( cd "$rr_repo" && HOME="$rr_home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test commit --quiet --allow-empty -m commit-b \
    && HOME="$rr_home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
commit_b="$(git -C "$rr_repo" rev-parse HEAD)"
[ "$commit_a" != "$commit_b" ] || { echo "real-remote sibling check: sanity failed — commit A and B are identical" >&2; exit 1; }
checks=$((checks + 1))
# Read siblingTags exactly the way the real observation step does (peeled ^{commit}, against the clone
# that already fetched everything via `clone`/`push` above — no separate fetch needed).
rr_drivers_sha="$(git -C "$rr_repo" rev-parse -q --verify 'refs/tags/drivers/v0.60.1^{commit}' 2>/dev/null || true)"
rr_coord_sha="$(git -C "$rr_repo" rev-parse -q --verify 'refs/tags/coord-engine/v0.60.1^{commit}' 2>/dev/null || true)"
[ "$rr_drivers_sha" = "$commit_a" ] || { echo "real-remote sibling check: expected drivers tag to resolve to commit A, got $rr_drivers_sha" >&2; exit 1; }
checks=$((checks + 1))
python3 -c "
import json
json.dump({
    'version': '0.60.1',
    'provenance': {'mergedReachable': True, 'introducedVersion': '0.60.1', 'prArm': 'pass'},
    'orgFeed': 'absent', 'nugetFeed': 'absent', 'orgLatest': '0.60.0', 'nugetLatest': '0.60.0',
    'tagExists': False, 'sourceSha': '$commit_b',
    'siblingTags': {'drivers': '$rr_drivers_sha', 'coordEngine': '$rr_coord_sha'},
}, open('$real_remote_check_work/facts.json', 'w'))
"
rr_verdict="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$real_remote_check_work/facts.json" --json)"
rr_action="$(jq -r .action <<<"$rr_verdict")"; rr_reason="$(jq -r .reason <<<"$rr_verdict")"
[ "$rr_action" = stickyEscalate ] && [ "$rr_reason" = sibling-tag-commit-mismatch ] \
  || { echo "real-remote sibling check: expected stickyEscalate/sibling-tag-commit-mismatch, got $rr_action/$rr_reason" >&2; cat "$real_remote_check_work/facts.json" >&2; exit 1; }
checks=$((checks + 1))

# ---- .github#2442/.github#2435: a `candidate-not-next-patch` refusal on a coherent-set minor bump
#      is the frontier rail working AS DESIGNED, not a defect (maintainer decision on #2442) — but
#      .github#2435's own visibility fixes (open escalation target, streak-bound job failure) now
#      apply to this reason exactly like any other actionable one, so without an explanation it reads
#      as an unexplained problem at the point someone actually sees it. `SCOPE_NOTES` attaches that
#      explanation to the decision itself. ----
note="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/coherent-set-minor.json" --json | jq -r '.note // empty')"
[ -n "$note" ] \
  || { echo 'coherent-set-minor: expected a scope note explaining the deliberate refusal, got none' >&2; exit 1; }
checks=$((checks + 1))
case "$note" in *2442*) : ;; *) echo "coherent-set-minor: note does not reference .github#2442, got: $note" >&2; exit 1 ;; esac
checks=$((checks + 1))

# An ORDINARY, actionable refusal reason must carry no note — the field is deliberately reason-scoped,
# not a general refusal-explainer, so it cannot dilute into noise on the reasons that ARE bugs.
has_note="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/major.json" --json | jq -r 'has("note")')"
[ "$has_note" = "false" ] \
  || { echo 'major (version-not-stable-0x-patch): unexpected scope note on an ordinary, actionable refusal' >&2; exit 1; }
checks=$((checks + 1))

# GATE-INVERSION for the note itself: strip `SCOPE_NOTES` back to empty in a mutated copy of the REAL
# script and show the same coherent-set-minor fixture now loses its explanation — a test that cannot
# fail on the absence of this mapping has not tested it.
mutate_drop_scope_note() {
  python3 - "$root/scripts/kit-auto-publish.py" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
start = text.index('SCOPE_NOTES = {')
end = text.index('\n}\n', start) + len('\n}\n')
mutated = text[:start] + 'SCOPE_NOTES = {}\n' + text[end:]
if mutated == text:
    sys.exit("mutate_drop_scope_note: SCOPE_NOTES block not found/unchanged")
open(dst, 'w', encoding='utf-8').write(mutated)
PY
}
no_note_script="$work/kit-auto-publish-no-note.py"
mutate_drop_scope_note "$no_note_script"
has_note_mutated="$(python3 "$no_note_script" --facts "$work/coherent-set-minor.json" --json | jq -r 'has("note")')"
[ "$has_note_mutated" = "false" ] \
  || { echo 'gate-inversion (scope note): stripping SCOPE_NOTES should remove the note, it did not' >&2; exit 1; }
checks=$((checks + 1))

facts='{ "version":"0.27.1", "provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass"}, "orgFeed":"unknown", "nugetFeed":"absent", "orgLatest":"0.27.0", "nugetLatest":"0.27.0", "tagExists":false }'
case_run released-after-later-merge openEvidencePr '{"version":"0.27.1","sourceSha":"later","provenance":{"mergedReachable":true,"introducedVersion":"0.27.1","prArm":"pass","mergeCommit":"authoring"},"orgFeed":"present","nugetFeed":"present","orgLatest":"0.27.1","nugetLatest":"0.27.1","tagExists":true,"releaseRun":{"id":"42","url":"https://example.test/run/42","nuspecCommit":"later"}}'

# M3 recovery workflows check out current main, so their run head can differ from the immutable
# prepared/nuspec source. The workflow must accept that topology only through the published stable
# saga's content-addressed receipt and full dual-feed verified manifest. These assertions pin every
# load-bearing arm: removing any one must make a future review revisit the fallback rather than
# silently restoring the legacy headSha-only false negative.
auto_workflow="$root/.github/workflows/kit-auto-publish.yml"
for required in \
  'coherent-set/v${version}' \
  '--pattern release-manifest.json --pattern stable-channel.json' \
  '.descriptor.sourceSha == $source' \
  '.descriptor.policyVersion == "release-saga/1"' \
  '(.state.phase == "feeds-complete" or .state.phase == "promoted")' \
  '.state.feeds.github.state == "verified"' \
  '.state.feeds.nuget.state == "verified"' \
  '.version == $version and .sourceSha == $source and .contentId == $content'; do
  grep -qF -- "$required" "$auto_workflow" \
    || { echo "saga release evidence fallback lost required assertion: $required" >&2; exit 1; }
  checks=$((checks + 1))
done

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
cnp_esc_facts='{"version":"0.51.0","provenance":{"mergedReachable":true,"introducedVersion":"0.51.0","prArm":"pass"},"orgFeed":"absent","nugetFeed":"absent","orgLatest":"0.50.2","nugetLatest":"0.50.2","tagExists":false}'  # coherent-set minor: candidate-not-next-patch, deliberately out of scope (.github#2442).

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

# `version-not-stable-0x-patch` carries no scope note (.github#2435/.github#2442) — the field must
# stay reason-scoped and not leak into an ordinary, actionable refusal's sticky comment.
grep -q '2442' "$d1/last-body.txt" \
  && { echo 'fresh escalation: unexpected .github#2442 reference for a reason with no scope note' >&2; exit 1; }
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

# ---- 8b. The scope note reaches BOTH surfaces a reader hits without opening #2106: the sticky
#          comment (below threshold, still green) and the `::error::` annotation itself (at/above
#          threshold, job fails). This is the concrete fix for the gap #2442's closing comment named:
#          the refusal text did not say a coherent-set minor is deliberately out of scope. ----
d9="$(sandbox note-fresh '[]')"
rc=0; run_escalation "$d9" "$cnp_esc_facts" 7001 "$esc_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "note escalation: expected exit 0 (streak 1 below threshold), got $rc" >&2; cat "$d9/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -q '2442' "$d9/last-body.txt" \
  || { echo 'note escalation: expected the sticky comment to reference .github#2442 for candidate-not-next-patch' >&2; cat "$d9/last-body.txt" >&2; exit 1; }
checks=$((checks + 1))

seed_note_comment() {
  local streak="$1" run="$2"
  printf '%s\n\n```json\n%s\n```\n\nAuto-publish performed no tag, feed, or evidence-PR write. [Run](x).' \
    "$marker_literal" "{\"valid\":true,\"streak\":$streak,\"action\":\"refuse\",\"reason\":\"candidate-not-next-patch\",\"version\":\"0.51.0\",\"lastRun\":\"$run\"}"
}
d10="$(sandbox note-at-threshold "$(jq -n --arg b "$(seed_note_comment 2 8001)" '[{id:901, body:$b}]')")"
rc=0; run_escalation "$d10" "$cnp_esc_facts" 8002 "$esc_script" || rc=$?
[ "$rc" -eq 1 ] || { echo "note escalation (at threshold): expected exit 1 (streak 3 >= 3), got $rc" >&2; cat "$d10/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -q '2442' "$d10/stdout.log" \
  || { echo 'note escalation (at threshold): expected the ::error:: annotation to reference .github#2442 without opening the issue' >&2; cat "$d10/stdout.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- 8c. GATE-INVERSION for the note plumbing: strip the note extraction/suffix wiring back out of
#          a copy of the REAL step and show the same two surfaces above lose the explanation — a test
#          that cannot fail on the absence of this wiring has not tested it. ----
mutate_strip_note_surfacing() {
  python3 - "$esc_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out = []
touched = 0
drop_prefixes = (
    'note="$(jq -r',
    'note_suffix=""',
    '[ -z "$note" ] || note_suffix=',
    'note_annotation=""',
    '[ -z "$note" ] || note_annotation=',
)
for line in lines:
    stripped = line.strip()
    if any(stripped.startswith(p) for p in drop_prefixes):
        touched += 1
        continue
    if stripped.startswith('body="$(printf'):
        out.append(
            'body="$(printf \'%s\\n\\n```json\\n%s\\n```\\n\\nAuto-publish performed no tag, feed, or '
            'evidence-PR write. [Run](%s/%s/actions/runs/%s).\' "$marker" "$state" "$GITHUB_SERVER_URL" '
            '"$GITHUB_REPOSITORY" "$GITHUB_RUN_ID")"\n'
        )
        touched += 1
        continue
    if 'note_annotation' in line:
        out.append(line.replace('${note_annotation}', ''))
        touched += 1
        continue
    out.append(line)
if touched < 6:
    sys.exit(f"mutate_strip_note_surfacing: expected to touch at least 6 lines, touched {touched}")
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}
no_note_script="$esc_work/escalate-no-note.sh"
mutate_strip_note_surfacing "$no_note_script"

d11="$(sandbox no-note-fresh '[]')"
rc=0; run_escalation "$d11" "$cnp_esc_facts" 7002 "$no_note_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "gate-inversion (note plumbing, body): expected exit 0, got $rc" >&2; cat "$d11/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -q '2442' "$d11/last-body.txt" \
  && { echo 'gate-inversion (note plumbing, body): without the wiring, the note must NOT reach the sticky comment' >&2; exit 1; }
checks=$((checks + 1))

d12="$(sandbox no-note-at-threshold "$(jq -n --arg b "$(seed_note_comment 2 9001)" '[{id:1001, body:$b}]')")"
rc=0; run_escalation "$d12" "$cnp_esc_facts" 9002 "$no_note_script" || rc=$?
[ "$rc" -eq 1 ] || { echo "gate-inversion (note plumbing, annotation): expected exit 1, got $rc" >&2; cat "$d12/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -q '2442' "$d12/stdout.log" \
  && { echo 'gate-inversion (note plumbing, annotation): without the wiring, the ::error:: annotation must NOT reference .github#2442' >&2; exit 1; }
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

# ---- 11. `provenance_evidence`/`gh_retry`/`find_provenance`: RETRY ROOT CAUSE (.github#2443) PLUS
#          its round-1 review repairs. A transient TLS/network blip on `gh run list`/`gh run view`
#          inside the provenance loop previously fell straight through the loop's `|| continue` and
#          read exactly like a verified-absent `pr-arm` job. Round 1 of independent review caught two
#          regressions in the first draft's retry wrapper: (1) folding `gh run list`'s call — which was
#          previously UNGUARDED and so crashed the whole job LOUDLY on any failure under `set -e` —
#          into the same `|| return 1` as everything else made a persistent, genuinely-unreachable `gh`
#          call read exactly like a verified-absent candidate; (2) `gh_retry`'s `2>/dev/null` discarded
#          the real diagnostic (the TLS error text this very issue quotes as evidence) even on the
#          FINAL, exhausted attempt. `provenance_evidence` now returns THREE distinguishable outcomes
#          (0 = evidence found, 1 = verified absent, 2 = could not verify) and `gh_retry` preserves the
#          last attempt's stderr — this extracts the REAL functions out of the REAL workflow (between
#          the `kit-provenance-search` markers) and runs them against a stubbed `gh` that can be told to
#          fail its first N calls of a given kind before succeeding, plus a REAL synthetic git repo for
#          the `git merge-base --is-ancestor` check. ----
extract_provenance_retry_fns() {
  awk '/^[[:space:]]*# BEGIN kit-provenance-search$/{flag=1; next} /^[[:space:]]*# END kit-provenance-search$/{flag=0} flag' \
    "$root/.github/workflows/kit-auto-publish.yml" > "$1"
  grep -q 'gh_retry()' "$1" \
    || { echo "gh_retry() function is GONE from kit-auto-publish.yml" >&2; exit 1; }
  grep -q 'provenance_evidence()' "$1" \
    || { echo "provenance_evidence() function is GONE from kit-auto-publish.yml" >&2; exit 1; }
  grep -q 'find_provenance()' "$1" \
    || { echo "find_provenance() function is GONE from kit-auto-publish.yml" >&2; exit 1; }
}

prov_work="$esc_work/provenance"
mkdir -p "$prov_work/bin" "$prov_work/repo"
git -C "$prov_work" init -q repo
git -C "$prov_work/repo" config user.email test@example.test
git -C "$prov_work/repo" config user.name test
git -C "$prov_work/repo" commit -q --allow-empty -m init
prov_merge_sha="$(git -C "$prov_work/repo" rev-parse HEAD)"
# A REAL two-commit version bump for `find_provenance`'s own `csproj_version` matching (11h/11i,
# below): base introduces 0.27.0, the child ("real match") introduces 0.27.1 — this is the ONE
# candidate `find_provenance` should find for version 0.27.1.
mkdir -p "$prov_work/repo/src/FS.GG.Kit"
printf '<Project><PropertyGroup><Version>0.27.0</Version></PropertyGroup></Project>' > "$prov_work/repo/src/FS.GG.Kit/FS.GG.Kit.csproj"
git -C "$prov_work/repo" add -A && git -C "$prov_work/repo" commit -q -m base
printf '<Project><PropertyGroup><Version>0.27.1</Version></PropertyGroup></Project>' > "$prov_work/repo/src/FS.GG.Kit/FS.GG.Kit.csproj"
git -C "$prov_work/repo" add -A && git -C "$prov_work/repo" commit -q -m 'real match'
prov_real_match_sha="$(git -C "$prov_work/repo" rev-parse HEAD)"
# `origin/main` here is a plain local ref standing in for the real remote-tracking ref the live
# workflow's checkout provides; `merge-base --is-ancestor` only cares that it resolves and is
# reachable, and a commit is its own ancestor, so pointing it at the same commit is sufficient.
git -C "$prov_work/repo" update-ref refs/remotes/origin/main "$prov_real_match_sha"

prov_fn="$prov_work/provenance_evidence.sh"
extract_provenance_retry_fns "$prov_fn"

# A stub `gh` driven by two env vars, `RUN_LIST_FAIL`/`RUN_VIEW_FAIL`: the first N calls of that kind
# fail with a message shaped like the live TLS blip this fix targets, then every call after succeeds.
# Per-kind call counts persist under `$STUB_DIR` so retries within one `provenance_evidence` call are
# visible afterward.
cat > "$prov_work/bin/gh" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$STUB_DIR/calls.log"
if [ "$1" = "run" ] && [ "$2" = "list" ]; then
  n_file="$STUB_DIR/run_list_n"; n=0; [ -f "$n_file" ] && n="$(cat "$n_file")"; n=$((n + 1)); printf '%s' "$n" > "$n_file"
  if [ "$n" -le "${RUN_LIST_FAIL:-0}" ]; then
    echo "failed to get run: tls: failed to verify certificate: x509: certificate is not valid for any names, but wanted to match api.github.com" >&2
    exit 1
  fi
  printf '999'
  exit 0
fi
if [ "$1" = "run" ] && [ "$2" = "view" ]; then
  n_file="$STUB_DIR/run_view_n"; n=0; [ -f "$n_file" ] && n="$(cat "$n_file")"; n=$((n + 1)); printf '%s' "$n" > "$n_file"
  if [ "$n" -le "${RUN_VIEW_FAIL:-0}" ]; then
    echo "failed to get run: tls: failed to verify certificate: x509: certificate is not valid for any names, but wanted to match api.github.com" >&2
    exit 1
  fi
  printf '12345'
  exit 0
fi
# `PR_LIST_TSV` stands in for `gh pr list --jq '.[] | [...] | @tsv'`'s real output — jq's raw output
# terminates EVERY emitted record with `\n`, including the last, so a trailing `\n` here (not just
# between rows) is load-bearing: it is what round-2 review repair 3 (11h/11i below) checks survives
# `gh_retry`'s own re-emission.
if [ "$1" = "pr" ] && [ "$2" = "list" ]; then
  printf '%s\n' "${PR_LIST_TSV:-}"
  exit 0
fi
echo "unstubbed gh invocation: $*" >&2
exit 99
STUB
chmod +x "$prov_work/bin/gh"

# run_provenance_evidence <dir> <script> <run-list-fail> <run-view-fail> -> exit code; stdout/stderr
# and per-kind call counts land under <dir> so a scenario can assert on them afterward. Sourced under
# the SAME `set -euo pipefail` the real workflow step runs under — that option is exactly what makes
# an unguarded failing assignment crash instead of silently falling through, which is the crux of
# round-1 review repair 1, so a test that ran the extracted function WITHOUT it could pass on a
# reintroduced regression that only bites in the real step.
run_provenance_evidence() {
  local dir="$1" script="$2" rl="$3" rv="$4" rc=0
  mkdir -p "$dir"
  rm -f "$dir/calls.log" "$dir/run_list_n" "$dir/run_view_n"
  ( cd "$prov_work/repo" \
    && STUB_DIR="$dir" PATH="$prov_work/bin:$PATH" \
       RUN_LIST_FAIL="$rl" RUN_VIEW_FAIL="$rv" GITHUB_REPOSITORY=FS-GG/.github \
       bash -c "set -euo pipefail; source '$script'; provenance_evidence '123' '$prov_merge_sha' 'deadbeef' '0.27.1'" \
  ) >"$dir/stdout.log" 2>"$dir/stderr.log" || rc=$?
  return "$rc"
}

# run_find_provenance <dir> <script> <pr-list-tsv> -> exit code; stdout/stderr land under <dir>.
# Exercises `find_provenance` THROUGH THE REAL PIPE — `gh_retry gh pr list ... | find_provenance
# "$version"` — the exact production wiring at the bottom of kit-auto-publish.yml, rather than calling
# `find_provenance` directly with a pre-split argument list. Round-2 review repair 3 (11h/11i) is
# invisible to any test that does not go through this pipe: `gh_retry`'s missing trailing newline only
# bites `find_provenance`'s `while read` on the LAST record of exactly this kind of piped input.
# `find_provenance` calls `csproj_version` (a DIFFERENT extracted block, `kit-csproj-version` —
# section 9/10 above), so `$csproj_version_fn` is sourced alongside `$script` here.
run_find_provenance() {
  local dir="$1" script="$2" pr_list_tsv="$3" rc=0
  mkdir -p "$dir"
  rm -f "$dir/calls.log" "$dir/run_list_n" "$dir/run_view_n"
  ( cd "$prov_work/repo" \
    && STUB_DIR="$dir" PATH="$prov_work/bin:$PATH" \
       RUN_LIST_FAIL=0 RUN_VIEW_FAIL=0 PR_LIST_TSV="$pr_list_tsv" GITHUB_REPOSITORY=FS-GG/.github \
       bash -c "set -euo pipefail; source '$csproj_version_fn'; source '$script'; \
         gh_retry gh pr list --repo \"\$GITHUB_REPOSITORY\" --state merged --limit 100 \
           --json number,mergeCommit,headRefOid --jq '.[] | [.number, .mergeCommit.oid, .headRefOid] | @tsv' \
         | find_provenance '0.27.1'" \
  ) >"$dir/stdout.log" 2>"$dir/stderr.log" || rc=$?
  return "$rc"
}

# 11a. Baseline: `gh` never fails -> evidence is found on the first attempt of each call.
d="$prov_work/baseline"
rc=0; run_provenance_evidence "$d" "$prov_fn" 0 0 || rc=$?
[ "$rc" -eq 0 ] || { echo "provenance_evidence baseline: expected exit 0, got $rc" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .prArm "$d/stdout.log")" = pass ] \
  || { echo "provenance_evidence baseline: expected prArm=pass, got: $(cat "$d/stdout.log")" >&2; exit 1; }
checks=$((checks + 1))

# 11b. RETRY RECOVERS: `gh run list`'s first call fails transiently, its second succeeds. The FIXED
#      function must still find evidence — the exact case that misread as `merged-pr-provenance-missing`
#      live at 4bb788e6.
d="$prov_work/run-list-transient"
rc=0; run_provenance_evidence "$d" "$prov_fn" 1 0 || rc=$?
[ "$rc" -eq 0 ] || { echo "provenance_evidence run-list-transient: expected exit 0 (retry recovers), got $rc" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .prArm "$d/stdout.log")" = pass ] \
  || { echo "provenance_evidence run-list-transient: expected recovered evidence, got: $(cat "$d/stdout.log")" >&2; exit 1; }
checks=$((checks + 1))
[ "$(grep -c '^run list' "$d/calls.log")" -eq 2 ] \
  || { echo "provenance_evidence run-list-transient: expected exactly 2 'gh run list' calls (1 failure + 1 retry), got: $(cat "$d/calls.log")" >&2; exit 1; }
checks=$((checks + 1))

# 11c. Same shape for `gh run view`'s transient failure.
d="$prov_work/run-view-transient"
rc=0; run_provenance_evidence "$d" "$prov_fn" 0 1 || rc=$?
[ "$rc" -eq 0 ] || { echo "provenance_evidence run-view-transient: expected exit 0 (retry recovers), got $rc" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .prArm "$d/stdout.log")" = pass ] \
  || { echo "provenance_evidence run-view-transient: expected recovered evidence, got: $(cat "$d/stdout.log")" >&2; exit 1; }
checks=$((checks + 1))

# 11d. A PERSISTENT (not transient) `gh run list` failure still gives up, bounded (no infinite retry
#      loop) — but per round-1 repair 1 it must now report 2 (COULD NOT VERIFY), distinguishable from 1
#      (verified absent), and per repair 2 the real TLS diagnostic must survive to stderr.
d="$prov_work/run-list-persistent"
rc=0; run_provenance_evidence "$d" "$prov_fn" 99 0 || rc=$?
[ "$rc" -eq 2 ] || { echo "provenance_evidence run-list-persistent: expected exit 2 (inconclusive, distinguishable from verified-absent), got $rc" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(grep -c '^run list' "$d/calls.log")" -eq 3 ] \
  || { echo "provenance_evidence run-list-persistent: expected exactly 3 attempts (1 + 2 retries), got: $(cat "$d/calls.log")" >&2; exit 1; }
checks=$((checks + 1))
grep -qi 'tls' "$d/stderr.log" \
  || { echo "provenance_evidence run-list-persistent: expected the real TLS diagnostic to survive to stderr (repair 2), got: $(cat "$d/stderr.log")" >&2; exit 1; }
checks=$((checks + 1))

# 11d2. The same shape for a PERSISTENT `gh run view` failure — repair 1/2 apply symmetrically to both
#       calls, not just the one round 1 happened to name first.
d="$prov_work/run-view-persistent"
rc=0; run_provenance_evidence "$d" "$prov_fn" 0 99 || rc=$?
[ "$rc" -eq 2 ] || { echo "provenance_evidence run-view-persistent: expected exit 2 (inconclusive), got $rc" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qi 'tls' "$d/stderr.log" \
  || { echo "provenance_evidence run-view-persistent: expected the real TLS diagnostic to survive to stderr, got: $(cat "$d/stderr.log")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 11e. GATE-INVERSION EVIDENCE (pnext-item §3) for the RETRY mechanism itself: strip `gh_retry`
#           back out of a copy of the real functions — reproducing pre-.github#2443 bare `gh run
#           list`/`gh run view` calls textually — and show the IDENTICAL one-shot transient blip (11c's
#           scenario) now costs a LOUD, avoidable job failure (2) instead of a clean recovery (0): even
#           with repair 1/2's 1-vs-2 distinction in place, retry is still what tells a one-off blip
#           apart from something worth failing loudly over. A test that cannot fail on the pre-#2443
#           shape has not tested the retry fix. ----
mutate_to_no_retry() {
  python3 - "$1" "$2" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
mutated = text.replace("gh_retry gh run list", "gh run list").replace("gh_retry gh run view", "gh run view")
if mutated == text:
    sys.exit("mutate_to_no_retry: nothing to strip — gh_retry call sites are GONE")
open(dst, "w", encoding="utf-8").write(mutated)
PY
}
prov_buggy_fn="$prov_work/provenance_evidence_no_retry.sh"
mutate_to_no_retry "$prov_fn" "$prov_buggy_fn"

d="$prov_work/gate-inversion-run-view"
rc=0; run_provenance_evidence "$d" "$prov_buggy_fn" 0 1 || rc=$?
[ "$rc" -eq 2 ] \
  || { echo "gate-inversion (retry): pre-#2443 bare gh call on a one-shot transient blip should cost an avoidable inconclusive failure (2), got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(grep -c '^run view' "$d/calls.log")" -eq 1 ] \
  || { echo "gate-inversion (retry): pre-#2443 must make exactly ONE 'gh run view' call — no retry — got: $(cat "$d/calls.log")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 11f. GATE-INVERSION EVIDENCE for ROUND-1 REPAIR 1: reproduce the exact shape independent review
#           flagged in this fix's FIRST draft — `gh_retry gh run list ...` folded into a uniform
#           `|| return 1`, the same outcome as a verified-absent candidate — and show a PERSISTENT
#           failure (11d's scenario) now reads 1 instead of 2: indistinguishable from genuine absence,
#           reproducing the regression the critic caught before this repair existed. ----
prov_round1_fn="$prov_work/provenance_evidence_round1.sh"
{
  awk '/^[[:space:]]*gh_retry\(\) \{$/,/^[[:space:]]*\}$/' "$prov_fn"
  cat <<'ROUND1'
provenance_evidence() {
  local number="$1" merge="$2" head="$3" version="$4" run_id
  run_id="$(gh_retry gh run list --repo "$GITHUB_REPOSITORY" --workflow kit-published-coherence.yml --commit "$head" --event pull_request --status success --limit 20 --json databaseId --jq '.[0].databaseId // empty')" || return 1
  [ -n "$run_id" ] || return 1
  gh_retry gh run view "$run_id" --repo "$GITHUB_REPOSITORY" --json jobs --jq '.jobs[] | select(.name == "pr-arm" and .conclusion == "success") | .databaseId' | grep -q . || return 1
  git merge-base --is-ancestor "$merge" origin/main || return 1
  jq -n --arg n "$number" --arg merge "$merge" --arg head "$head" --arg version "$version" '{number:($n|tonumber),mergeCommit:$merge,head:$head,mergedReachable:true,introducedVersion:$version,prArm:"pass"}'
}
ROUND1
} > "$prov_round1_fn"
grep -q 'gh_retry()' "$prov_round1_fn" \
  || { echo "gate-inversion (repair 1): failed to carry gh_retry() into the round-1 fixture — awk extraction shape changed upstream" >&2; exit 1; }

d="$prov_work/gate-inversion-repair1"
rc=0; run_provenance_evidence "$d" "$prov_round1_fn" 99 0 || rc=$?
[ "$rc" -eq 1 ] \
  || { echo "gate-inversion (repair 1): round-1 shape must read a persistent gh run list failure as verified-absent (1), not inconclusive (2) — got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- 11g. GATE-INVERSION EVIDENCE for ROUND-1 REPAIR 2: reproduce `gh_retry`'s original
#           unconditional `2>/dev/null` (discards the real diagnostic even on the final, exhausted
#           attempt) with EVERY OTHER control-flow fix (including the `else`-branch `rc=$?` fix this
#           very repair round also needed inside `gh_retry` itself — see the doc comment on the real
#           function) held constant, isolating stderr handling as the ONLY variable under test. Paired
#           with the CURRENT (post-repair-1) `provenance_evidence`, the SAME persistent-failure scenario
#           (11d) must still leave stderr EMPTY — the diagnostic this issue itself cites as evidence is
#           gone, exactly what a future recurrence would leave no trace of. ----
prov_no_stderr_fn="$prov_work/provenance_evidence_no_stderr.sh"
cat > "$prov_no_stderr_fn" <<GHRETRY
gh_retry() {
  local attempt=0 max_retries=2 rc=0 out
  while :; do
    if out="\$("\$@" 2>/dev/null)"; then
      printf '%s' "\$out"
      return 0
    else
      rc=\$?
    fi
    attempt=\$((attempt + 1))
    [ "\$attempt" -gt "\$max_retries" ] && return "\$rc"
    sleep 1
  done
}
GHRETRY
awk '/^[[:space:]]*provenance_evidence\(\) \{$/,/^[[:space:]]*\}$/' "$prov_fn" >> "$prov_no_stderr_fn"
grep -q 'provenance_evidence()' "$prov_no_stderr_fn" \
  || { echo "gate-inversion (repair 2): failed to carry provenance_evidence() into the no-stderr fixture — awk extraction shape changed upstream" >&2; exit 1; }

d="$prov_work/gate-inversion-repair2"
rc=0; run_provenance_evidence "$d" "$prov_no_stderr_fn" 99 0 || rc=$?
[ "$rc" -eq 2 ] \
  || { echo "gate-inversion (repair 2): expected exit 2 (provenance_evidence itself is unchanged), got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
# `provenance_evidence`'s OWN `::error::` annotation still fires on rc 2 regardless of `gh_retry`'s
# stderr handling, so the assertion is specifically the ABSENCE of the underlying `gh` diagnostic text
# — not overall stderr emptiness (that would also pass on the annotation alone and prove nothing).
grep -qi 'tls' "$d/stderr.log" \
  && { echo "gate-inversion (repair 2): pre-fix gh_retry must NOT surface the underlying TLS diagnostic on the final exhausted attempt — got: $(cat "$d/stderr.log")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 11h/11i/11j. ROUND-2 REPAIR 3 (.github#2443, review round 2): `find_provenance` was never
#      EXECUTED by this file before round 2 — only `grep -q`'d for by name (11's earlier
#      `extract_provenance_retry_fns` check). That is precisely how two review rounds missed a defect
#      a single execution reveals: `gh_retry`'s success path was `printf '%s' "$out"` (no trailing
#      newline — `$(...)` already stripped whatever the wrapped command originally ended with), and
#      piped straight into `find_provenance`'s `while read` loop (the real production wiring,
#      `gh_retry gh pr list ... | find_provenance "$version"`), bash's `read` silently drops the FINAL
#      record of input with no trailing newline. In the realistic single-candidate case (exactly one
#      merged PR introduced the version), that dropped record IS the real match, so `find_provenance`
#      completed normally with `provenance={}` — no error, no exit code, just the wrong, silently
#      empty answer. These legs run `find_provenance` FOR REAL, through the real pipe, against a REAL
#      two-commit git history (0.27.0 -> 0.27.1) so `csproj_version`'s own matching is exercised too. ----

# 11h. Single candidate (the realistic shape) — this is the record round-2 review found DROPPED.
prov_single_tsv="42	$prov_real_match_sha	deadbeefhead"
d="$prov_work/find-provenance-single"
rc=0; run_find_provenance "$d" "$prov_fn" "$prov_single_tsv" || rc=$?
[ "$rc" -eq 0 ] || { echo "find_provenance single-candidate: expected exit 0, got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .prArm "$d/stdout.log" 2>/dev/null)" = pass ] \
  || { echo "find_provenance single-candidate: expected the real match's evidence, got: $(cat "$d/stdout.log")" >&2; exit 1; }
checks=$((checks + 1))

# 11i. Two candidates, real match FIRST and a non-matching decoy LAST (the decoy has no
#      FS.GG.Kit.csproj at all, so `csproj_version` resolves empty and it never matches `0.27.1`
#      regardless). This is the ordering round-2 review's own repro noted as accidentally "working"
#      even on the buggy code, because the record dropped by the missing newline is the decoy the loop
#      was always going to skip — included so the suite documents exactly why this escaped twice
#      rather than only proving the fix in the one ordering that exposes it.
prov_two_tsv="42	$prov_real_match_sha	deadbeefhead
99	$prov_merge_sha	decoyhead"
d="$prov_work/find-provenance-two-decoy-last"
rc=0; run_find_provenance "$d" "$prov_fn" "$prov_two_tsv" || rc=$?
[ "$rc" -eq 0 ] || { echo "find_provenance two-candidate (decoy last): expected exit 0, got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(jq -r .prArm "$d/stdout.log" 2>/dev/null)" = pass ] \
  || { echo "find_provenance two-candidate (decoy last): expected the real match's evidence, got: $(cat "$d/stdout.log")" >&2; exit 1; }
checks=$((checks + 1))

# ---- 11j. GATE-INVERSION EVIDENCE (pnext-item §3) for round-2 repair 3: restore the exact pre-repair
#           mutation — `printf '%s\n' "$out"` back to `printf '%s' "$out"` — and show 11h's
#           single-candidate scenario goes RED (silently wrong: exit 0 but `provenance={}`, the real
#           match dropped), then restore and show it goes GREEN again. Named, red, restore, green — a
#           test that cannot fail on the exact reverted line has not tested this repair. ----
mutate_drop_trailing_newline() {
  python3 - "$1" "$2" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
target = "printf '%s\\n' \"$out\"\n"
replacement = "printf '%s' \"$out\"\n"
if target not in text:
    sys.exit("mutate_drop_trailing_newline: the fixed \"printf '%s\\n' \\\"$out\\\"\" line is GONE from gh_retry")
mutated = text.replace(target, replacement, 1)
open(dst, "w", encoding="utf-8").write(mutated)
PY
}
prov_no_newline_fn="$prov_work/provenance_evidence_no_newline.sh"
mutate_drop_trailing_newline "$prov_fn" "$prov_no_newline_fn"

# RED: the named mutation reverts exactly round-2 repair 3 — the single-candidate case must now
# silently lose its only record.
d="$prov_work/gate-inversion-repair3-red"
rc=0; run_find_provenance "$d" "$prov_no_newline_fn" "$prov_single_tsv" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "gate-inversion (repair 3, RED phase): pre-fix gh_retry should complete NORMALLY (exit 0) while silently losing the record, got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
observed_red="$(cat "$d/stdout.log")"
[ "$observed_red" = '{}' ] \
  || { echo "gate-inversion (repair 3, RED phase): expected the pre-fix mutation to silently drop the real match and emit '{}', got: $observed_red" >&2; exit 1; }
checks=$((checks + 1))

# GREEN (restore): the unmutated, currently-shipped function on the IDENTICAL scenario finds the
# real match, per 11h above — re-asserted here beside the red observation so both halves of the
# named-mutation/red/restore/green sequence live together.
d="$prov_work/gate-inversion-repair3-green"
rc=0; run_find_provenance "$d" "$prov_fn" "$prov_single_tsv" || rc=$?
[ "$rc" -eq 0 ] || { echo "gate-inversion (repair 3, GREEN/restore phase): expected exit 0, got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
observed_green="$(jq -r .prArm "$d/stdout.log" 2>/dev/null)"
[ "$observed_green" = pass ] \
  || { echo "gate-inversion (repair 3, GREEN/restore phase): expected the restored function to find the real match (prArm=pass), got: $(cat "$d/stdout.log")" >&2; exit 1; }
checks=$((checks + 1))

# ---- .github#2492/.github#2495: "Tag the coherent-set siblings" (originally "Tag the sole eligible
#      state") never configured a committer identity before `git tag -a` (.github#2492), and on its
#      own only ever tagged `kit/v$VERSION` — leaving `drivers/v$VERSION`/`coord-engine/v$VERSION`
#      permanently missing, which is what stops release-kit.yml's sibling-tag precondition from ever
#      clearing (.github#2495). This extracts the REAL step out of the REAL workflow and runs it end
#      to end against a real local git remote — a fixture that could tell only "the commands were
#      invoked" from "a tag actually landed [at the right commit]" would not have caught either defect
#      (the .github#2492 issue's own acceptance criterion 3; the same standard applies here).
tag_work="$esc_work/tag-identity"
mkdir -p "$tag_work"

# GitHub Actions substitutes `${{ expression }}` textually BEFORE the shell ever sees the script; a
# raw extraction that skipped this would hand bash a `${{ ... }}` token it cannot parse (`bad
# substitution`), not a faithful reproduction of what actually runs on a runner. Stand in a fixed
# stub app-slug exactly where the runner would have substituted the live one.
extract_tag_step() {
  python3 - "$root/.github/workflows/kit-auto-publish.yml" "$1" <<'PY'
import sys
import yaml

wf_path, out_path = sys.argv[1], sys.argv[2]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)
# Searched across EVERY job rather than pinned to one job id. .github#2654 moved this step out of
# `decide` and into its own `tag` job (so it could `needs:` the preparation job), and a pinned
# extractor would have read that as "the tag step is GONE" — a red for a structural change that is
# the whole point of the repair, and a green-by-vacuity if the pin were ever "fixed" by pointing it
# at a job that no longer holds it. The step's NAME is the stable identity here; which job hosts it
# is exactly what the topology legs below are for.
steps = [s for job in (wf.get("jobs") or {}).values() if isinstance(job, dict) for s in (job.get("steps") or [])]
matches = [s for s in steps if s.get("name") == "Tag the coherent-set siblings"]
if len(matches) != 1:
    sys.exit(f"expected exactly ONE 'Tag the coherent-set siblings' step in kit-auto-publish.yml, found {len(matches)}")
step = matches[0]
run = step["run"].replace("${{ steps.app-token.outputs.app-slug }}", "test-bot")
with open(out_path, "w", encoding="utf-8") as out:
    out.write("#!/usr/bin/env bash\n")
    out.write(run)
PY
}

tag_script="$tag_work/tag.sh"
extract_tag_step "$tag_script"
chmod +x "$tag_script"

# The MUTATED copy: strip the two `git config` lines back out, reproducing the exact pre-fix defect
# textually so the failure mode is a permanent, re-runnable gate-inversion leg (pnext-item §3)
# rather than a one-time manual observation.
mutate_drop_identity() {
  python3 - "$tag_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out, dropped = [], 0
for line in lines:
    if line.lstrip().startswith('git config user.name') or line.lstrip().startswith('git config user.email'):
        dropped += 1
        continue
    out.append(line)
if dropped != 2:
    sys.exit(f"mutate_drop_identity: expected to drop exactly 2 git config lines, dropped {dropped}")
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}
tag_no_identity_script="$tag_work/tag-no-identity.sh"
mutate_drop_identity "$tag_no_identity_script"
chmod +x "$tag_no_identity_script"

# A REAL local git remote — not a stub — so the assertion is "a tag actually landed", the exact gap
# acceptance criterion 3 names.
#
# .github#2508: EACH leg below gets its OWN bare remote, created fresh inside `make_worktree` itself,
# rather than the single shared "$tag_work/origin.git" every leg used to clone and push
# "HEAD -> main" against. Sharing one remote is what let a later leg's independent history collide
# with an earlier leg's already-pushed tip: once one clone's checkout of the (no-longer-empty, but
# differently-HEADed) bare repo failed — `git init --bare` leaves the remote's own symbolic HEAD
# following the runner's `init.defaultBranch` default, which need not be "main" — that clone landed
# on an unrelated unborn branch, and its own seed commit became an orphan with no shared ancestry.
# Pushing that orphan to "main" is the exact non-fast-forward rejection .github#2508 measured on 7 of
# 8 kit-auto-publish runs. No leg's push can depend on another leg's history when no leg shares a
# remote with another, so the fix here is isolation, not reordering. `--initial-branch=main` also
# makes every fresh bare remote's own HEAD name "main" explicitly, removing the runner-default
# dependency entirely rather than merely making it unreachable through sharing.
#
# make_worktree <name> -> a fresh bare remote plus a fresh clone of it with a seed commit, printed.
# HOME points at an empty, per-clone directory and GIT_CONFIG_NOSYSTEM=1 is set so neither this
# host's own ~/.gitconfig identity nor any system config leaks in — matching a hosted runner's clean
# environment, which is exactly the environment the live bug required. The seed commit's sha is left
# in `$d/seed-sha` for callers that need it as SOURCE_SHA, and the leg's own remote always lives at
# the deterministic path `$d/origin.git` for callers that need to inspect it directly.
make_worktree() {
  local d="$tag_work/$1"
  mkdir -p "$d/home"
  git init --quiet --bare --initial-branch=main "$d/origin.git"
  git -c init.defaultBranch=main clone --quiet "$d/origin.git" "$d/repo"
  ( cd "$d/repo" \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test commit --quiet --allow-empty -m seed \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
  git -C "$d/repo" rev-parse HEAD > "$d/seed-sha"
  printf '%s' "$d"
}

# advance_head <dir> -> commits one more empty commit on top of the seed (NOT pushed to the remote —
# a real run's checkout only ever needs to resolve commit-ishes locally), printing the new sha. Models
# main having moved on since an earlier, partial kit-auto-publish run tagged kit alone: the seed commit
# (still what the pre-existing kit tag names) is no longer HEAD.
advance_head() {
  local d="$1"
  ( cd "$d/repo" && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test commit --quiet --allow-empty -m advance )
  git -C "$d/repo" rev-parse HEAD
}

# push_tag_at <dir> <tag> <sha> -> creates an annotated tag at <sha> and pushes it, standing in for an
# earlier kit-auto-publish run (or, for kit itself, a human) having already tagged that ref.
push_tag_at() {
  local d="$1" tag="$2" sha="$3"
  ( cd "$d/repo" \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test tag -a "$tag" "$sha" -m seed \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin "refs/tags/$tag" )
}

# run_tag_step <dir> <version> <action> <source-sha> <script> -> exit code; runs INSIDE the clone with
# the same isolated HOME/GIT_CONFIG_NOSYSTEM so neither this host's global git identity nor any
# credential helper can mask the defect.
run_tag_step() {
  local dir="$1" version="$2" action="$3" source_sha="$4" script="$5" rc=0
  ( cd "$dir/repo" \
      && HOME="$dir/home" GIT_CONFIG_NOSYSTEM=1 ACTION="$action" VERSION="$version" SOURCE_SHA="$source_sha" bash "$script" >"$dir/stdout.log" 2>"$dir/stderr.log" ) || rc=$?
  return "$rc"
}

# ---- RED: the pre-fix step (no identity configured) fails closed exactly like the live runs cited
#      in .github#2492, and no tag reaches the remote. ----
d="$(make_worktree red)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
rc=0; run_tag_step "$d" "9.9.1" tag "$seed_sha" "$tag_no_identity_script" || rc=$?
[ "$rc" -ne 0 ] \
  || { echo "gate-inversion (tag identity, RED phase): pre-fix step should have failed closed, exited 0" >&2; exit 1; }
checks=$((checks + 1))
# git's exact fatal tail (`empty ident name` vs. `unable to auto-detect email address`) depends on
# whether the HOST can guess an email at all, which varies by environment — the .github#2492 runs
# hit the former; this sandbox can hit either. Both share this identical preamble, which is the part
# that identifies "no identity configured" as the actual failure, independent of that guess.
grep -q 'Committer identity unknown' "$d/stderr.log" \
  || { echo "gate-inversion (tag identity, RED phase): expected 'Committer identity unknown' in stderr, got:" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
git -C "$remote" tag -l 'kit/v9.9.1' | grep -q . \
  && { echo "gate-inversion (tag identity, RED phase): no tag should have reached the remote" >&2; exit 1; }
checks=$((checks + 1))

# ---- GREEN: the REAL, currently-shipped step (identity configured, action='tag', fresh candidate)
#      succeeds, and ALL THREE coherent-set tags actually land on the remote at the SAME commit — not
#      merely that the commands were invoked without error, and not only kit (.github#2495's gap). ----
d="$(make_worktree green)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
rc=0; run_tag_step "$d" "9.9.2" tag "$seed_sha" "$tag_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "gate-inversion (tag identity, GREEN phase): expected exit 0, got $rc" >&2; cat "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
for tag in kit/v9.9.2 drivers/v9.9.2 coord-engine/v9.9.2; do
  git -C "$remote" tag -l "$tag" | grep -qx "$tag" \
    || { echo "gate-inversion (tag identity, GREEN phase): expected $tag to have reached the remote" >&2; exit 1; }
  checks=$((checks + 1))
  landed_sha="$(git -C "$remote" rev-parse "refs/tags/$tag^{commit}")"
  [ "$landed_sha" = "$seed_sha" ] \
    || { echo "gate-inversion (tag identity, GREEN phase): $tag landed at $landed_sha, expected the seed commit $seed_sha" >&2; exit 1; }
  checks=$((checks + 1))
  tagger="$(git -C "$remote" for-each-ref --format='%(taggername) %(taggeremail)' "refs/tags/$tag")"
  [ "$tagger" = 'test-bot[bot] <297630107+test-bot[bot]@users.noreply.github.com>' ] \
    || { echo "gate-inversion (tag identity, GREEN phase): unexpected tagger identity for $tag: $tagger" >&2; exit 1; }
  checks=$((checks + 1))
done

# ---- .github#2495 leg 1 — TOCTOU guard is unaffected: 'tag' still refuses when kit's tag appears
#      between observation and this write, and (new) no sibling tag reaches the remote either. ----
d="$(make_worktree race)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
push_tag_at "$d" "kit/v9.9.3" "$seed_sha"
rc=0; run_tag_step "$d" "9.9.3" tag "$seed_sha" "$tag_script" || rc=$?
[ "$rc" -ne 0 ] \
  || { echo ".github#2495 leg 1 (TOCTOU guard): expected the race guard to refuse, exited 0" >&2; cat "$d/stdout.log" >&2; exit 1; }
checks=$((checks + 1))
grep -q 'tag appeared after observation' "$d/stdout.log" \
  || { echo ".github#2495 leg 1 (TOCTOU guard): expected the original race-refusal message, got:" >&2; cat "$d/stdout.log" >&2; exit 1; }
checks=$((checks + 1))
for tag in drivers/v9.9.3 coord-engine/v9.9.3; do
  git -C "$remote" tag -l "$tag" | grep -q . \
    && { echo ".github#2495 leg 1 (TOCTOU guard): $tag must not have been created on a refused run" >&2; exit 1; }
  checks=$((checks + 1))
done

# ---- .github#2495 leg 2 — the REPAIR path: kit already tagged (by an earlier, partial run) at a
#      commit that is NO LONGER HEAD (main has since advanced), both siblings missing. `tagSiblings`
#      must supply exactly the two missing tags, pinned to kit's EXISTING commit — never re-touch kit,
#      never point the siblings at whatever HEAD happens to be now. This is the live, currently-owed
#      0.50.3 shape (kit/v0.50.3 already on origin at 42c63af9, both siblings absent) reproduced
#      end-to-end against a real remote. ----
d="$(make_worktree repair)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
push_tag_at "$d" "kit/v9.9.4" "$seed_sha"
advanced_sha="$(advance_head "$d")"
[ "$advanced_sha" != "$seed_sha" ] \
  || { echo ".github#2495 leg 2 (repair): sanity check failed — advance_head did not move HEAD" >&2; exit 1; }
checks=$((checks + 1))
rc=0; run_tag_step "$d" "9.9.4" tagSiblings "$seed_sha" "$tag_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo ".github#2495 leg 2 (repair): expected exit 0, got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
for tag in drivers/v9.9.4 coord-engine/v9.9.4; do
  git -C "$remote" tag -l "$tag" | grep -qx "$tag" \
    || { echo ".github#2495 leg 2 (repair): expected $tag to have been created and pushed" >&2; exit 1; }
  checks=$((checks + 1))
  landed_sha="$(git -C "$remote" rev-parse "refs/tags/$tag^{commit}")"
  [ "$landed_sha" = "$seed_sha" ] \
    || { echo ".github#2495 leg 2 (repair): $tag landed at $landed_sha, expected kit's EXISTING commit $seed_sha (not the advanced HEAD $advanced_sha)" >&2; exit 1; }
  checks=$((checks + 1))
done
# kit itself must be untouched — still the ORIGINAL tag object pushed by push_tag_at, tagged by "seed",
# not re-created by this run (which would have overwritten the tagger identity to test-bot[bot]).
kit_tagger="$(git -C "$remote" for-each-ref --format='%(taggername) %(taggeremail)' refs/tags/kit/v9.9.4)"
[ "$kit_tagger" = 'seed <seed@example.test>' ] \
  || { echo ".github#2495 leg 2 (repair): kit/v9.9.4 must remain untouched (tagger 'seed'), got: $kit_tagger" >&2; exit 1; }
checks=$((checks + 1))

# ---- .github#2495 leg 3 — the defensive "nothing to tag/push" guard: all three already present at
#      the target commit (should be unreachable in production, since the decision engine's own
#      `siblings-complete` case above never emits `tagSiblings` there — this exercises the step's own
#      independent defense-in-depth). ----
d="$(make_worktree complete)"
seed_sha="$(cat "$d/seed-sha")"
for tag in kit/v9.9.5 drivers/v9.9.5 coord-engine/v9.9.5; do push_tag_at "$d" "$tag" "$seed_sha"; done
rc=0; run_tag_step "$d" "9.9.5" tagSiblings "$seed_sha" "$tag_script" || rc=$?
[ "$rc" -ne 0 ] \
  || { echo ".github#2495 leg 3 (nothing-to-do guard): expected a non-zero exit, got 0" >&2; exit 1; }
checks=$((checks + 1))
grep -q 'nothing to tag/push' "$d/stdout.log" \
  || { echo ".github#2495 leg 3 (nothing-to-do guard): expected the 'nothing to tag/push' error, got:" >&2; cat "$d/stdout.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- .github#2495 leg 4 — GATE-INVERSION EVIDENCE for pinning to SOURCE_SHA rather than HEAD: mutate
#      the REAL, currently-shipped step to drop the explicit commit-ish from `git tag -a` (so it
#      defaults to HEAD, the naive/pre-fix-shaped mistake), and show that in the SAME repair scenario as
#      leg 2 — kit already tagged at a commit that is no longer HEAD — the mutated step now tags the
#      siblings at the WRONG (current, advanced) commit instead of kit's actual commit. A test that
#      cannot fail on this exact reversion has not tested why SOURCE_SHA, not HEAD, is load-bearing. ----
mutate_to_head() {
  python3 - "$tag_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
target = 'git tag -a "$tag" "$SOURCE_SHA" -m'
replacement = 'git tag -a "$tag" -m'
if target not in text:
    sys.exit("mutate_to_head: the SOURCE_SHA-pinned tag_one command is GONE from the real step")
mutated = text.replace(target, replacement, 1)
open(dst, "w", encoding="utf-8").write(mutated)
PY
}
tag_head_script="$tag_work/tag-head-not-source-sha.sh"
mutate_to_head "$tag_head_script"

d="$(make_worktree gate-inversion-source-sha)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
push_tag_at "$d" "kit/v9.9.6" "$seed_sha"
advanced_sha="$(advance_head "$d")"
rc=0; run_tag_step "$d" "9.9.6" tagSiblings "$seed_sha" "$tag_head_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo ".github#2495 leg 4 (gate-inversion, SOURCE_SHA): mutated step should still exit 0 (git tag -a succeeds against HEAD), got $rc" >&2; cat "$d/stdout.log" "$d/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
landed_sha="$(git -C "$remote" rev-parse 'refs/tags/drivers/v9.9.6^{commit}')"
[ "$landed_sha" = "$advanced_sha" ] \
  || { echo ".github#2495 leg 4 (gate-inversion, SOURCE_SHA): expected the mutated (HEAD-defaulting) step to land drivers/v9.9.6 at the WRONG advanced commit $advanced_sha, got $landed_sha" >&2; exit 1; }
checks=$((checks + 1))
[ "$landed_sha" != "$seed_sha" ] \
  || { echo ".github#2495 leg 4 (gate-inversion, SOURCE_SHA): mutation had no effect — landed at the correct commit despite dropping SOURCE_SHA" >&2; exit 1; }
checks=$((checks + 1))

# ---- .github#2498 round-1 repair 1 (write side) — ATOMIC PUSH: `git push origin "${push_refs[@]}"`
#      (no `--atomic`) is not all-or-nothing. GitHub can accept some ref updates in one invocation and
#      reject others — concretely here, one tag name already existing on `origin` at a DIFFERENT commit
#      (a real race this step's single observation cannot fully rule out) is rejected while the OTHER
#      refs in the SAME push still land, manufacturing exactly the split coherent-set trio this item
#      exists to prevent. This reproduces that against a REAL remote: `kit/v0.60.2` is pre-created
#      DIRECTLY on the bare remote (never fetched into the worktree clone, so `tag_one`'s local
#      existence check cannot see it and will still attempt to create+push it) at a decoy commit, while
#      `drivers`/`coord-engine` are genuinely new. ----
d="$(make_worktree atomic-push-fixed)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
decoy_sha="$(advance_head "$d")"
( cd "$d/repo" && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
( HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -C "$remote" -c user.name=decoy -c user.email=decoy@example.test tag -a "kit/v0.60.2" "$decoy_sha" -m decoy )
rc=0; run_tag_step "$d" "0.60.2" tag "$seed_sha" "$tag_script" || rc=$?
[ "$rc" -ne 0 ] \
  || { echo ".github#2498 atomic push (fixed, RED-for-kit phase): expected the rejected kit ref to fail the whole step, got exit 0" >&2; exit 1; }
checks=$((checks + 1))
git -C "$remote" tag -l 'kit/v0.60.2' | grep -qx 'kit/v0.60.2' \
  || { echo ".github#2498 atomic push (fixed): the pre-existing decoy kit tag must still be present" >&2; exit 1; }
checks=$((checks + 1))
kit_landed="$(git -C "$remote" rev-parse 'refs/tags/kit/v0.60.2^{commit}')"
[ "$kit_landed" = "$decoy_sha" ] \
  || { echo ".github#2498 atomic push (fixed): kit/v0.60.2 must remain at the decoy commit $decoy_sha (untouched), got $kit_landed" >&2; exit 1; }
checks=$((checks + 1))
# THE POINT: with --atomic, the two refs that COULD have landed (drivers, coord-engine) must NOT have,
# because kit's ref update in the same push was rejected.
for tag in drivers/v0.60.2 coord-engine/v0.60.2; do
  git -C "$remote" tag -l "$tag" | grep -q . \
    && { echo ".github#2498 atomic push (fixed): $tag must NOT have landed — --atomic should have made the whole push all-or-nothing" >&2; exit 1; }
  checks=$((checks + 1))
done

# ---- GATE-INVERSION EVIDENCE (pnext-item §3) for `--atomic`: strip it from a copy of the REAL step and
#      re-run the IDENTICAL scenario. Without it, the same rejected kit ref must NOT stop the other two
#      from landing — reproducing, on a real remote, the exact split/mismatched trio independent review
#      named as reachable. A test that cannot fail on the dropped flag has not tested why it is there. ----
mutate_drop_atomic() {
  python3 - "$tag_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
target = "git push --atomic origin"
replacement = "git push origin"
if target not in text:
    sys.exit("mutate_drop_atomic: the --atomic push is GONE from the real step")
mutated = text.replace(target, replacement, 1)
open(dst, "w", encoding="utf-8").write(mutated)
PY
}
tag_no_atomic_script="$tag_work/tag-no-atomic.sh"
mutate_drop_atomic "$tag_no_atomic_script"

d="$(make_worktree atomic-push-mutated)"
remote="$d/origin.git"
seed_sha="$(cat "$d/seed-sha")"
decoy_sha="$(advance_head "$d")"
( cd "$d/repo" && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
( HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -C "$remote" -c user.name=decoy -c user.email=decoy@example.test tag -a "kit/v0.60.3" "$decoy_sha" -m decoy )
rc=0; run_tag_step "$d" "0.60.3" tag "$seed_sha" "$tag_no_atomic_script" || rc=$?
[ "$rc" -ne 0 ] \
  || { echo "gate-inversion (atomic push): expected the overall push to still report non-zero (kit's ref is still rejected), got exit 0" >&2; exit 1; }
checks=$((checks + 1))
kit_landed_mutated="$(git -C "$remote" rev-parse 'refs/tags/kit/v0.60.3^{commit}')"
[ "$kit_landed_mutated" = "$decoy_sha" ] \
  || { echo "gate-inversion (atomic push): kit/v0.60.3 must remain at the decoy commit (git itself still refuses to overwrite it), got $kit_landed_mutated" >&2; exit 1; }
checks=$((checks + 1))
# THE REGRESSION: without --atomic, drivers/coord-engine DO land even though kit's own ref in the SAME
# push was rejected — a real, split, mismatched coherent-set trio on the remote.
for tag in drivers/v0.60.3 coord-engine/v0.60.3; do
  git -C "$remote" tag -l "$tag" | grep -qx "$tag" \
    || { echo "gate-inversion (atomic push): expected $tag to have landed WITHOUT --atomic (demonstrating the partial-push hazard), got none" >&2; git -C "$remote" tag -l >&2; exit 1; }
  checks=$((checks + 1))
  landed="$(git -C "$remote" rev-parse "refs/tags/$tag^{commit}")"
  [ "$landed" = "$seed_sha" ] \
    || { echo "gate-inversion (atomic push): expected $tag to have landed at $seed_sha, got $landed" >&2; exit 1; }
  checks=$((checks + 1))
done

# ---- GATE-INVERSION EVIDENCE (pnext-item §3) for the ISOLATION FIX ITSELF (.github#2508) — DRIVEN
#      THROUGH THE REAL `make_worktree`, NOT A REIMPLEMENTATION. Round-1 review (.github#2508 PR #2509)
#      found the block that used to live here material-defective: it built its own standalone
#      `clone_and_seed` helper and a private `shared_remote`, never called `make_worktree` or touched
#      `tag_remote`, and — proved by reverting ONLY `make_worktree`'s isolation in place and rerunning —
#      never fired even once across 16 regression runs (8/8 undetected under this sandbox's own ambient
#      `init.defaultBranch`, 5/8 undetected even under a forced mismatched ambient, where "detected" in
#      the surviving 3/8 was unrelated pre-existing flakiness in the atomic-push legs, not this leg).
#
#      The property .github#2508 actually added to `make_worktree` is structural and observable
#      WITHOUT any push-timing race or ambient-config dependency at all: every call gets its OWN bare
#      remote, so two consecutive real calls must (a) report different `origin` URLs and (b) never see
#      each other's commit. Asserting that directly, through two ordinary `make_worktree` calls exactly
#      as every real leg above makes them, is what the round-1 finding asked for — "assert two
#      consecutive real make_worktree calls produce distinct origin.git realpaths ... so that reverting
#      the real fix is what reds the suite." If `make_worktree` is ever reverted back to cloning one
#      shared `$tag_remote` (with the same `make_worktree <name>` call signature the reverted code also
#      had), BOTH calls below resolve to the identical remote path and each other's commit becomes
#      trivially visible — failing check (a), (b), or both, in ANY ambient config, deterministically. ----
gi_iso_a="$(make_worktree gate-inversion-isolation-a)"
gi_iso_a_remote="$(git -C "$gi_iso_a/repo" remote get-url origin)"
gi_iso_a_seed="$(cat "$gi_iso_a/seed-sha")"
gi_iso_b="$(make_worktree gate-inversion-isolation-b)"
gi_iso_b_remote="$(git -C "$gi_iso_b/repo" remote get-url origin)"
gi_iso_b_seed="$(cat "$gi_iso_b/seed-sha")"

gi_iso_a_realpath="$(realpath "$gi_iso_a_remote")"
gi_iso_b_realpath="$(realpath "$gi_iso_b_remote")"
[ "$gi_iso_a_realpath" != "$gi_iso_b_realpath" ] \
  || { echo "gate-inversion (tag-identity isolation): two consecutive make_worktree calls resolved to the SAME remote ($gi_iso_a_realpath) — per-leg isolation is gone" >&2; exit 1; }
checks=$((checks + 1))

# Each leg's own remote must carry EXACTLY the one commit ITS OWN make_worktree call pushed — never
# more (a shared remote, by this point in the file, already carries every prior leg's history) and
# never the other leg's ref state. Deliberately NOT a same-commit-SHA comparison: both calls commit an
# empty tree with the same message/identity, so two calls landing in the same wall-clock second can
# produce byte-identical commit objects even when fully isolated — a coincidence that would make a
# content-equality check pass trivially and prove nothing. Counting each remote's own history is
# ambient-independent and immune to that coincidence.
gi_iso_a_count="$(git -C "$gi_iso_a_remote" rev-list --count refs/heads/main)"
[ "$gi_iso_a_count" = 1 ] \
  || { echo "gate-inversion (tag-identity isolation): leg A's remote carries $gi_iso_a_count commit(s) on main, expected exactly 1 — consistent with a SHARED remote that already carries prior legs' history" >&2; exit 1; }
checks=$((checks + 1))
gi_iso_b_count="$(git -C "$gi_iso_b_remote" rev-list --count refs/heads/main)"
[ "$gi_iso_b_count" = 1 ] \
  || { echo "gate-inversion (tag-identity isolation): leg B's remote carries $gi_iso_b_count commit(s) on main, expected exactly 1" >&2; exit 1; }
checks=$((checks + 1))
[ "$(git -C "$gi_iso_a_remote" rev-parse refs/heads/main)" = "$gi_iso_a_seed" ] \
  || { echo "gate-inversion (tag-identity isolation): leg A's remote main does not point at leg A's own seed commit" >&2; exit 1; }
checks=$((checks + 1))
[ "$(git -C "$gi_iso_b_remote" rev-parse refs/heads/main)" = "$gi_iso_b_seed" ] \
  || { echo "gate-inversion (tag-identity isolation): leg B's remote main does not point at leg B's own seed commit" >&2; exit 1; }
checks=$((checks + 1))

# ---- DOCUMENTATION OF THE GENERAL HAZARD (kept per round-1 review: "legitimate as documentation ...
#      but cannot be the thing standing in for AC-3"). This is a SEPARATE, self-contained demonstration
#      that two independent clones of one remote taken before either pushes will always race — a real
#      git fact, not specific to this fixture's own code, and not itself gate-inversion evidence for
#      the fix above (that role belongs to the two checks immediately preceding this block). ----
gate_inversion_coupling_work="$tag_work/gate-inversion-shared-remote"
mkdir -p "$gate_inversion_coupling_work"
shared_remote="$gate_inversion_coupling_work/origin.git"
git init --quiet --bare --initial-branch=main "$shared_remote"
clone_and_seed() {
  local d="$gate_inversion_coupling_work/$1"
  mkdir -p "$d/home"
  git -c init.defaultBranch=main clone --quiet "$shared_remote" "$d/repo"
  ( cd "$d/repo" && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test commit --quiet --allow-empty -m "seed-$1" )
  printf '%s' "$d"
}
gi_first="$(clone_and_seed alpha)"
gi_second="$(clone_and_seed beta)"
( cd "$gi_first/repo" && HOME="$gi_first/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
gi_rc=0
( cd "$gi_second/repo" && HOME="$gi_second/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main ) \
  2>"$gi_second/push-stderr.log" || gi_rc=$?
[ "$gi_rc" -ne 0 ] \
  || { echo "documentation (shared-remote hazard): reintroduced shared-remote coupling should have rejected the second leg's push, exited 0" >&2; exit 1; }
checks=$((checks + 1))
grep -qi 'non-fast-forward\|rejected' "$gi_second/push-stderr.log" \
  || { echo "documentation (shared-remote hazard): expected a non-fast-forward rejection, got:" >&2; cat "$gi_second/push-stderr.log" >&2; exit 1; }
checks=$((checks + 1))

# =================================================================================================
# .github#2558 — THE EVIDENCE-PR STEP. Three defects whose combined effect was that no auto-publish
# evidence entry had EVER landed on `main`: `grep -cF "auto-publish evidence" registry/CHANGELOG.md`
# returned **0** across every kit publish this repository had ever made.
#
#   1. `sed -i "2i\\${entry}"` — a HARDCODED line 2, while `## Entries` sits around line 20. Every
#      entry landed ABOVE the heading, the exact shape `check-registry-changelog.py::top_date`
#      refuses. The gate was right; the writer was wrong. PR #2246 was born red on 2026-08-05 and
#      was still red, and still being force-pushed, eight days later.
#   2. The PR body was a DOUBLE-QUOTED bash string containing `\n`, which bash never interprets — so
#      the served body was one single line of literal backslash-n sequences.
#   3. The refresh branch called `gh pr edit --body` with NO `--title`, while the create branch
#      passed `--title` — so PR #2246's title still read `0.39.0` while its body had been rewritten
#      to `0.51.1`.
#
# WHY THESE LEGS EXTRACT AND EXECUTE THE REAL STEP against a REAL local git remote, rather than
# grepping the workflow: defect 2 is INVISIBLE in the source (`\n` inside double quotes looks like a
# newline to a reader) and only appears in the bytes actually handed to `gh`. A fixture asserting on
# the workflow's text would have been green against all three defects — the #266 green-by-absence
# shape. Same mechanism and same reasoning as extract_tag_step above.
#
# WHY A REAL CHECKER RUN, not a "the entry is below the heading" assertion: fixing ONLY the insertion
# point is NOT sufficient to make the gate pass. `check-registry-changelog.py` has a SECOND arm that
# compares `dependencies.yml::updated:` against the top changelog date, and an insertion-only repair
# still reds on every publish that does not land on the same UTC day as the last registry flip. The
# `date-arm` leg below measures that directly, so the pairing cannot be silently dropped later.
# =================================================================================================
ev_work="$esc_work/evidence-pr"
mkdir -p "$ev_work"

extract_evidence_step() {
  python3 - "$root/.github/workflows/kit-auto-publish.yml" "$1" <<'PY'
import sys
import yaml

wf_path, out_path = sys.argv[1], sys.argv[2]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)
steps = (wf.get("jobs") or {}).get("decide", {}).get("steps") or []
step = next((s for s in steps if s.get("name") == "Open or update the publish evidence PR"), None)
if step is None:
    sys.exit("the evidence-PR step is GONE from kit-auto-publish.yml")
run = step["run"].replace("${{ steps.app-token.outputs.app-slug }}", "test-bot")
if "${{" in run:
    sys.exit("the evidence-PR step grew an unhandled ${{ }} expression; teach this extractor about it")
# Carry the step's OWN static `env:` defaults (BRANCH) instead of restating them here, so a rename in
# the workflow follows through to the fixture rather than silently diverging from it. Values that are
# `${{ }}` expressions (VERSION, SOURCE_SHA) are supplied per-leg by the caller.
env_lines = []
for key, value in (step.get("env") or {}).items():
    if isinstance(value, str) and "${{" not in value:
        env_lines.append(f'export {key}="{value}"\n')
with open(out_path, "w", encoding="utf-8") as out:
    out.write("#!/usr/bin/env bash\n")
    out.writelines(env_lines)
    out.write(run)
PY
}

ev_script="$ev_work/evidence.sh"
extract_evidence_step "$ev_script"
chmod +x "$ev_script"

# A `gh` stub for the evidence step's three calls. It records the EXACT title and body bytes the step
# handed over — which is the only place defect 2 was ever observable — and records separately whether
# `--title` was passed AT ALL, because defect 3 is an ABSENT flag, not a wrong value.
write_evidence_gh_stub() {
  cat > "$1/bin/gh" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
log="$STUB_DIR/calls.log"
sub="${1:-} ${2:-}"
capture() {
  local title="" body="" saw_title=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --title) saw_title=1; title="${2:-}"; shift 2 ;;
      --body)  body="${2:-}"; shift 2 ;;
      --repo|--base|--head) shift 2 ;;
      *) shift ;;
    esac
  done
  printf '%s' "$title" > "$STUB_DIR/last-title.txt"
  printf '%s' "$body" > "$STUB_DIR/last-body.txt"
  printf '%s' "$saw_title" > "$STUB_DIR/last-title-present"
}
case "$sub" in
  "pr list")
    echo "PR-LIST" >> "$log"
    [ -s "$STUB_DIR/existing-pr" ] && cat "$STUB_DIR/existing-pr" || printf ''
    exit 0 ;;
  "pr edit")
    num="${3:-}"; shift 3; capture "$@"
    echo "EDIT $num" >> "$log"
    exit 0 ;;
  "pr create")
    shift 2; capture "$@"
    echo "CREATE" >> "$log"
    exit 0 ;;
esac
echo "unstubbed gh invocation: $*" >&2
exit 99
STUB
  chmod +x "$1/bin/gh"
}

# evidence_sandbox <name> [skewed-updated-date] -> a REAL bare remote + clone (via make_worktree, the
# same helper every tag leg uses) whose `main` carries the LIVE registry/ files and the REAL scripts
# the step invokes by relative path. Copying the real scripts is itself an assertion: if the workflow
# ever calls something this repository does not ship, these legs fail rather than silently stubbing it
# in.
#
# THE OPTIONAL SKEW ARGUMENT EXISTS BECAUSE LIVE SEED DATA MADE A LEG BLIND (round-1 M1). A caller
# that passes a date rewrites the seeded `dependencies.yml`'s `updated:` to it. Without that, a
# property about the writer MOVING `updated:` can be satisfied by the calendar alone: `.github#2552`
# set the live value to 2026-08-13 and this fixture ran on 2026-08-13 UTC, so a writer that never
# touched the file at all still produced a tree whose dates agreed, and the suite stayed green under
# exactly the mutation the leg named. A hostile seed removes the coincidence from the measurement.
evidence_sandbox() {
  local d; d="$(make_worktree "$1")"
  mkdir -p "$d/bin" "$d/repo/registry" "$d/repo/scripts"
  write_evidence_gh_stub "$d"
  cp "$root/registry/CHANGELOG.md" "$root/registry/dependencies.yml" "$d/repo/registry/"
  cp "$root/scripts/prepend-registry-changelog-entry.py" "$root/scripts/check-registry-changelog.py" "$d/repo/scripts/"
  if [ -n "${2:-}" ]; then
    sed -i "s/^updated: *\"[0-9-]*\"/updated: \"$2\"/" "$d/repo/registry/dependencies.yml"
  fi
  : > "$d/existing-pr"
  : > "$d/calls.log"
  ( cd "$d/repo" \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git add registry scripts \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git -c user.name=seed -c user.email=seed@example.test commit --quiet -m "seed registry and scripts" \
      && HOME="$d/home" GIT_CONFIG_NOSYSTEM=1 git push --quiet origin HEAD:main )
  printf '%s' "$d"
}

# run_evidence <dir> <version> <source-sha> <script> -> exit code. Runs INSIDE the clone with an
# isolated HOME, exactly like run_tag_step, so no host git identity can mask an identity defect.
run_evidence() {
  local dir="$1" version="$2" source_sha="$3" script="$4" rc=0
  printf '{"releaseRun":{"url":"https://github.example.test/run/%s"}}' "$version" > "$dir/repo/facts.json"
  ( cd "$dir/repo" \
      && HOME="$dir/home" GIT_CONFIG_NOSYSTEM=1 STUB_DIR="$dir" PATH="$dir/bin:$PATH" \
         GH_TOKEN=fake-token GITHUB_REPOSITORY=FS-GG/.github \
         VERSION="$version" SOURCE_SHA="$source_sha" \
         bash "$script" >"$dir/stdout.log" 2>"$dir/stderr.log" ) || rc=$?
  return "$rc"
}

# ---- GREEN: a first publish. The entry must land BELOW the heading, the REAL gate must accept the
#      produced tree, and the commit must actually reach the remote. ----
ev1="$(evidence_sandbox evidence-create)"
rc=0; run_evidence "$ev1" "7.7.1" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" "$ev_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "evidence step (create): expected exit 0, got $rc" >&2; cat "$ev1/stderr.log" >&2; exit 1; }
checks=$((checks + 1))

ev1_log="$ev1/repo/registry/CHANGELOG.md"
ev_heading_line="$(grep -n '^## Entries$' "$ev1_log" | head -1 | cut -d: -f1)"
ev_entry_line="$(grep -n 'auto-publish evidence: FS.GG.Kit 7.7.1' "$ev1_log" | head -1 | cut -d: -f1)"
[ -n "$ev_entry_line" ] \
  || { echo "evidence step (create): no entry was written to CHANGELOG.md at all" >&2; exit 1; }
checks=$((checks + 1))
# Deliberately a COMPARISON against the heading's own discovered line, never a constant: acceptance
# criterion 1 forbids a fixed offset, and registry/CHANGELOG.md moves under this fixture constantly.
[ "$ev_entry_line" -gt "$ev_heading_line" ] \
  || { echo "evidence step (create): entry at line $ev_entry_line is NOT below the Entries heading at line $ev_heading_line" >&2; exit 1; }
checks=$((checks + 1))

# THE GATE ITSELF, on the produced tree — acceptance criterion 2's "passes on a produced entry".
( cd "$ev1/repo" && python3 scripts/check-registry-changelog.py \
    --changelog registry/CHANGELOG.md --dependencies registry/dependencies.yml \
    --changed registry/CHANGELOG.md --changed registry/dependencies.yml >"$ev1/gate.log" 2>&1 ) \
  || { echo "evidence step (create): the REAL registry-changelog gate refused the produced tree" >&2; cat "$ev1/gate.log" >&2; exit 1; }
checks=$((checks + 1))

# The write must have reached the REMOTE, not merely the working tree — "a PR was opened" and "a
# commit landed carrying the entry" are different claims, and only the second one is the subject.
ev1_remote="$ev1/origin.git"
# The branch name is read back out of the step's OWN extracted `env:`, never restated here, so a
# rename in the workflow cannot leave this assertion silently probing a ref that no longer exists —
# which would read as "the entry never landed" and send a reader after the wrong defect.
ev_branch="$(sed -n 's/^export BRANCH="\(.*\)"$/\1/p' "$ev_script")"
[ -n "$ev_branch" ] \
  || { echo "evidence step: could not read BRANCH out of the extracted step's env" >&2; exit 1; }
checks=$((checks + 1))
ev_remote_log="$(git -C "$ev1_remote" show "refs/heads/$ev_branch:registry/CHANGELOG.md" 2>&1)" \
  || { echo "evidence step (create): cannot read registry/CHANGELOG.md from refs/heads/$ev_branch on the remote:" >&2
       printf '%s\n' "$ev_remote_log" >&2; git -C "$ev1_remote" for-each-ref >&2; exit 1; }
grep -qF 'auto-publish evidence: FS.GG.Kit 7.7.1' <<<"$ev_remote_log" \
  || { echo "evidence step (create): the entry never reached the remote branch $ev_branch" >&2
       git -C "$ev1_remote" for-each-ref >&2; exit 1; }
checks=$((checks + 1))
# NOTE THE CAPTURE, and do not "simplify" this back into `git show ... | grep -q`. This file runs
# under `set -o pipefail`, and `grep -q` exits at its FIRST match — which closes the pipe while
# `git show` is still writing a 200-300 KB registry file. `git show` then dies of SIGPIPE (141), the
# pipeline inherits that status, and the leg reports "never reached the remote" for a file that is
# demonstrably there. The size of these two files is what makes it reproducible rather than flaky:
# small outputs finish writing before grep can exit, which is why the same idiom is safe elsewhere in
# this fixture and unsafe here.
ev_remote_dep="$(git -C "$ev1_remote" show "refs/heads/$ev_branch:registry/dependencies.yml" 2>&1)" \
  || { echo "evidence step (create): cannot read registry/dependencies.yml from refs/heads/$ev_branch:" >&2
       printf '%s\n' "$ev_remote_dep" >&2; exit 1; }
# Assert the two files AGREE on the remote, not merely that an `updated:` line exists there — the
# seed already carried one, so a presence check was decorative and would have passed against a writer
# that never touched dependencies.yml (round-1 M1). Both dates are read out of the pushed content, so
# this holds whatever today's date is.
ev_remote_dep_date="$(sed -n 's/^updated: *"\([0-9-]*\)".*/\1/p' <<<"$ev_remote_dep")"
ev_remote_top="$(awk '/^## Entries$/{f=1;next} f && /^- \*\*[0-9]{4}-[0-9]{2}-[0-9]{2}\*\*/{print substr($0,5,10); exit}' <<<"$ev_remote_log")"
[ -n "$ev_remote_dep_date" ] && [ -n "$ev_remote_top" ] \
  || { echo "evidence step (create): could not read updated:='$ev_remote_dep_date' / top entry='$ev_remote_top' from $ev_branch" >&2; exit 1; }
[ "$ev_remote_dep_date" = "$ev_remote_top" ] \
  || { echo "evidence step (create): on $ev_branch, dependencies.yml updated=$ev_remote_dep_date disagrees with the top changelog entry $ev_remote_top — the pushed commit would be refused by the gate's second arm" >&2; exit 1; }
checks=$((checks + 1))

# Defect 2, on the bytes actually handed to `gh`.
ev1_body_lines="$(wc -l < "$ev1/last-body.txt")"
[ "$ev1_body_lines" -gt 1 ] \
  || { echo "evidence step (create): body is $ev1_body_lines line(s); the \\n were not interpreted" >&2; cat "$ev1/last-body.txt" >&2; exit 1; }
checks=$((checks + 1))
grep -qF '\n' "$ev1/last-body.txt" \
  && { echo "evidence step (create): body still contains literal backslash-n sequences" >&2; cat "$ev1/last-body.txt" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'Closes #2106' "$ev1/last-body.txt" \
  || { echo "evidence step (create): the closing keyword is not on its own line" >&2; cat "$ev1/last-body.txt" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'CREATE' "$ev1/calls.log" \
  || { echo "evidence step (create): expected a gh pr create" >&2; cat "$ev1/calls.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- GREEN: acceptance criterion 4 — TWO PUBLISHES BETWEEN MERGES, exercised rather than assumed.
#      The same open PR is refreshed by a second run at a higher version; its TITLE must move too. ----
printf '2246' > "$ev1/existing-pr"
rc=0; run_evidence "$ev1" "7.7.2" "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" "$ev_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "evidence step (refresh): expected exit 0, got $rc" >&2; cat "$ev1/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qx 'EDIT 2246' "$ev1/calls.log" \
  || { echo "evidence step (refresh): expected a gh pr edit of the existing PR" >&2; cat "$ev1/calls.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(cat "$ev1/last-title-present")" = 1 ] \
  || { echo "evidence step (refresh): gh pr edit was called WITHOUT --title (defect 3)" >&2; exit 1; }
checks=$((checks + 1))
grep -qF '7.7.2' "$ev1/last-title.txt" \
  || { echo "evidence step (refresh): title still names the old version: $(cat "$ev1/last-title.txt")" >&2; exit 1; }
checks=$((checks + 1))
grep -qF '7.7.1' "$ev1/last-title.txt" \
  && { echo "evidence step (refresh): title still carries the FIRST version — the .github#2246 shape" >&2; exit 1; }
checks=$((checks + 1))
# Both entries present, both below the heading, and the gate still accepts the two-entry tree.
for v in 7.7.1 7.7.2; do
  grep -qF "auto-publish evidence: FS.GG.Kit $v" "$ev1_log" \
    || { echo "evidence step (refresh): entry for $v is missing after the second run" >&2; exit 1; }
  checks=$((checks + 1))
done
( cd "$ev1/repo" && python3 scripts/check-registry-changelog.py \
    --changelog registry/CHANGELOG.md --dependencies registry/dependencies.yml >"$ev1/gate2.log" 2>&1 ) \
  || { echo "evidence step (refresh): the gate refused the two-entry tree" >&2; cat "$ev1/gate2.log" >&2; exit 1; }
checks=$((checks + 1))

# Idempotence: a THIRD run at an already-recorded version must not stack a duplicate entry.
ev_calls_before_idempotence="$(wc -l < "$ev1/calls.log")"
rc=0; run_evidence "$ev1" "7.7.2" "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" "$ev_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "evidence step (idempotence): expected exit 0, got $rc" >&2; cat "$ev1/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
ev_dupes="$(grep -cF 'auto-publish evidence: FS.GG.Kit 7.7.2' "$ev1_log")"
[ "$ev_dupes" -eq 1 ] \
  || { echo "evidence step (idempotence): $ev_dupes copies of the 7.7.2 entry, expected 1" >&2; exit 1; }
checks=$((checks + 1))
[ "$(wc -l < "$ev1/calls.log")" -eq "$ev_calls_before_idempotence" ] \
  || { echo "evidence step (idempotence): a zero-diff rerun still called gh PR mutation" >&2; cat "$ev1/calls.log" >&2; exit 1; }
checks=$((checks + 1))
git -C "$ev1_remote" show-ref --verify --quiet "refs/heads/$ev_branch" \
  && { echo "evidence step (idempotence): stale zero-diff branch $ev_branch remains on the remote" >&2; exit 1; }
checks=$((checks + 1))

# ---- GATE-INVERSION 1 (pnext-item §3, .github#2551): reintroduce the EXACT pre-fix `sed -i "2i..."`
#      and prove the REAL gate refuses the result FOR THE STATED REASON. A "must fail" leg that only
#      checked a non-zero exit would pass against a gate broken in a different way — the trap
#      tests/feed-coherence/run.sh:10 names — so this asserts the line-2 refusal text specifically. ----
mutate_to_sed_line2() {
  python3 - "$ev_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out, replaced, skipping = [], False, False
for line in lines:
    if line.lstrip().startswith("python3 scripts/prepend-registry-changelog-entry.py"):
        # The pre-fix pair: compose the entry, then splat it at a hardcoded line 2.
        out.append(
            'entry="$(printf \'%s\' "- **$(date -u +%F)** — **auto-publish evidence: FS.GG.Kit '
            '${VERSION}** (owner github): [release run](${run}) published it.")"\n'
        )
        out.append(
            'grep -Fq "auto-publish evidence: FS.GG.Kit ${VERSION}" registry/CHANGELOG.md '
            '|| sed -i "2i\\\\${entry}" registry/CHANGELOG.md\n'
        )
        replaced, skipping = True, line.rstrip("\n").endswith("\\")
        continue
    if skipping:
        skipping = line.rstrip("\n").endswith("\\")
        continue
    out.append(line)
if not replaced:
    sys.exit("mutate_to_sed_line2: could not find the prepend-script invocation to revert")
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}
ev_sed_script="$ev_work/evidence-sed-line2.sh"
mutate_to_sed_line2 "$ev_sed_script"
chmod +x "$ev_sed_script"

ev2="$(evidence_sandbox evidence-gate-inversion-line2)"
rc=0; run_evidence "$ev2" "7.7.3" "cccccccccccccccccccccccccccccccccccccccc" "$ev_sed_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "gate-inversion (line-2 insert): the pre-fix step itself should still exit 0 — its failure is SILENT, which is the whole defect; got $rc" >&2; cat "$ev2/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
ev2_gate_rc=0
( cd "$ev2/repo" && python3 scripts/check-registry-changelog.py \
    --changelog registry/CHANGELOG.md --dependencies registry/dependencies.yml >"$ev2/gate.log" 2>&1 ) || ev2_gate_rc=$?
[ "$ev2_gate_rc" -ne 0 ] \
  || { echo "gate-inversion (line-2 insert): the gate ACCEPTED an entry written above the heading — the check is not measuring this" >&2; cat "$ev2/gate.log" >&2; exit 1; }
checks=$((checks + 1))
grep -qF 'has dated entry before Entries heading at line 2' "$ev2/gate.log" \
  || { echo "gate-inversion (line-2 insert): refused, but NOT for the stated reason; got:" >&2; cat "$ev2/gate.log" >&2; exit 1; }
checks=$((checks + 1))

# ---- GATE-INVERSION 2: the pre-fix double-quoted body. This is the defect that is invisible in the
#      workflow source, so the assertion is on the bytes the step handed `gh`. ----
mutate_to_unquoted_body() {
  python3 - "$ev_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out, replaced = [], False
for line in lines:
    if line.lstrip().startswith('body="$(printf'):
        out.append(
            'body="Automated evidence for FS.GG.Kit ${VERSION}.\\n\\n- release run: ${run}\\n'
            '\\nHuman/critic review is required before merge.\\n\\nCloses #2106"\n'
        )
        replaced = True
        continue
    out.append(line)
if not replaced:
    sys.exit("mutate_to_unquoted_body: could not find the printf body= line to revert")
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}
ev_body_script="$ev_work/evidence-unquoted-body.sh"
mutate_to_unquoted_body "$ev_body_script"
chmod +x "$ev_body_script"

ev3="$(evidence_sandbox evidence-gate-inversion-body)"
rc=0; run_evidence "$ev3" "7.7.4" "dddddddddddddddddddddddddddddddddddddddd" "$ev_body_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "gate-inversion (uninterpreted body): pre-fix step should still exit 0; got $rc" >&2; cat "$ev3/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
ev3_lines="$(wc -l < "$ev3/last-body.txt")"
[ "$ev3_lines" -le 1 ] \
  || { echo "gate-inversion (uninterpreted body): expected the pre-fix body to be a SINGLE line, got $ev3_lines — this leg is not measuring defect 2" >&2; exit 1; }
checks=$((checks + 1))
grep -qF '\n' "$ev3/last-body.txt" \
  || { echo "gate-inversion (uninterpreted body): expected literal backslash-n sequences in the pre-fix body" >&2; cat "$ev3/last-body.txt" >&2; exit 1; }
checks=$((checks + 1))

# ---- GATE-INVERSION 3: drop `--title` from the refresh branch, reproducing defect 3 exactly. ----
mutate_drop_title() {
  python3 - "$ev_script" "$1" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
needle = 'gh pr edit "$existing" --repo "$GITHUB_REPOSITORY" --title "$title" --body "$body"'
if needle not in text:
    sys.exit("mutate_drop_title: the refresh branch no longer matches the expected shape")
text = text.replace(needle, 'gh pr edit "$existing" --repo "$GITHUB_REPOSITORY" --body "$body"', 1)
open(dst, "w", encoding="utf-8").write(text)
PY
}
ev_title_script="$ev_work/evidence-no-title.sh"
mutate_drop_title "$ev_title_script"
chmod +x "$ev_title_script"

ev4="$(evidence_sandbox evidence-gate-inversion-title)"
rc=0; run_evidence "$ev4" "7.7.5" "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" "$ev_title_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "gate-inversion (no title): first run failed: $rc" >&2; cat "$ev4/stderr.log" >&2; exit 1; }
printf '2246' > "$ev4/existing-pr"
rc=0; run_evidence "$ev4" "7.7.6" "ffffffffffffffffffffffffffffffffffffffff" "$ev_title_script" || rc=$?
[ "$rc" -eq 0 ] || { echo "gate-inversion (no title): refresh run failed: $rc" >&2; cat "$ev4/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
[ "$(cat "$ev4/last-title-present")" = 0 ] \
  || { echo "gate-inversion (no title): expected the pre-fix refresh to pass NO --title; it passed one — this leg is not measuring defect 3" >&2; exit 1; }
checks=$((checks + 1))

# ---- 4. DID THE WRITER MOVE dependencies.yml? This is the leg that catches an INSERTION-ONLY repair
#      — one that positions the entry correctly below the heading and leaves registry/dependencies.yml
#      untouched. Such a repair satisfies "the entry is below the heading" while still producing a PR
#      the gate refuses on its second arm, which is the same never-lands outcome reached by a different
#      refusal.
#
#      ROUND-1 REVIEW FOUND THE PREVIOUS VERSION OF THIS LEG DID NOT TEST THAT, and the way it failed
#      is worth keeping written down. It took the tree the real step produced, mutated THAT tree's
#      `updated:` to a stale date, and asserted the gate refused. That only re-proves the gate's second
#      arm is live — which was never in doubt — while never observing whether the WRITER had touched
#      dependencies.yml at all. A critic ran the mutation nobody had (an insertion-only writer: no
#      `set_updated`, no self-check, no second `write`) and the whole suite still reported
#      `183 passed`, exit 0.
#
#      It was blind for a specific and instructive reason: the sandbox seeds the LIVE registry files,
#      `.github#2552` had set `updated:` to 2026-08-13 earlier that same day, and the fixture ran on
#      2026-08-13 UTC — so the entry's date and the untouched `updated:` agreed BY CALENDAR
#      COINCIDENCE. The leg would have started catching the defect the next day and nobody would have
#      known why. A property whose sensitivity depends on the wall clock is not a gate.
#
#      So this leg now asserts on the WRITER'S OUTPUT, from a HOSTILE seed:
#        - the sandbox's `updated:` is seeded to 1999-01-01, which no publish can ever agree with;
#        - the file's bytes must CHANGE across the run (the direct "the writer touched it" claim);
#        - its `updated:` must end up equal to the top entry's OWN date, both read out of the produced
#          files rather than from this script's clock;
#        - and the real gate must accept the result.
#      An insertion-only writer fails the second and third of those on every calendar day. ----
ev5_skew="1999-01-01"
ev5="$(evidence_sandbox evidence-writer-moves-dependencies "$ev5_skew")"
ev5_dep="$ev5/repo/registry/dependencies.yml"
ev5_seeded="$(sed -n 's/^updated: *"\([0-9-]*\)".*/\1/p' "$ev5_dep")"
[ "$ev5_seeded" = "$ev5_skew" ] \
  || { echo "writer-moves-dependencies: the hostile seed did not take (updated=$ev5_seeded, wanted $ev5_skew) — this leg would be measuring nothing" >&2; exit 1; }
checks=$((checks + 1))
ev5_before="$(sha256sum "$ev5_dep" | cut -d' ' -f1)"
rc=0; run_evidence "$ev5" "7.7.7" "1111111111111111111111111111111111111111" "$ev_script" || rc=$?
[ "$rc" -eq 0 ] \
  || { echo "writer-moves-dependencies: the step failed: $rc" >&2; cat "$ev5/stderr.log" >&2; exit 1; }
checks=$((checks + 1))
ev5_after="$(sha256sum "$ev5_dep" | cut -d' ' -f1)"
[ "$ev5_before" != "$ev5_after" ] \
  || { echo "writer-moves-dependencies: registry/dependencies.yml is byte-identical after the run — the writer never touched it, which is precisely the insertion-only repair this leg exists to catch" >&2; exit 1; }
checks=$((checks + 1))
ev5_dep_date="$(sed -n 's/^updated: *"\([0-9-]*\)".*/\1/p' "$ev5_dep")"
ev5_top="$(awk '/^## Entries$/{f=1;next} f && /^- \*\*[0-9]{4}-[0-9]{2}-[0-9]{2}\*\*/{print substr($0,5,10); exit}' "$ev5/repo/registry/CHANGELOG.md")"
[ -n "$ev5_top" ] \
  || { echo "writer-moves-dependencies: no dated entry below the heading to compare against" >&2; exit 1; }
[ "$ev5_dep_date" != "$ev5_skew" ] \
  || { echo "writer-moves-dependencies: updated: is still the hostile seed $ev5_skew — the writer did not move it" >&2; exit 1; }
checks=$((checks + 1))
# Both sides read from the PRODUCED files, never from this script's own clock: the leg is about the
# two agreeing, and it holds identically on the day of a registry flip and on any other day.
[ "$ev5_dep_date" = "$ev5_top" ] \
  || { echo "writer-moves-dependencies: updated=$ev5_dep_date disagrees with the top entry $ev5_top" >&2; exit 1; }
checks=$((checks + 1))
( cd "$ev5/repo" && python3 scripts/check-registry-changelog.py \
    --changelog registry/CHANGELOG.md --dependencies registry/dependencies.yml >"$ev5/gate.log" 2>&1 ) \
  || { echo "writer-moves-dependencies: the real gate refused the produced tree" >&2; cat "$ev5/gate.log" >&2; exit 1; }
checks=$((checks + 1))
# The gate's second arm being live at all is asserted date-independently by
# tests/registry-changelog/run.sh ("an entry correctly placed but with a stale updated: is still
# refused"), which is where that property belongs; it is deliberately not restated here.


# ──────────────────────────────────────────────────────────────────────────────────────────────────
# .github#2654: THE TAG WRITE MUST BE DOWNSTREAM OF THE BYTES IT STARTS CONSUMING.
#
# EVERY LEG ABOVE SCORES `decide()`, AND EVERY ONE OF THEM STAYED GREEN THROUGH THIS DEFECT. That is
# not a gap in their coverage; it is the shape of the bug. `decide()` answered `tag` correctly, the
# workflow tagged correctly, and all three sibling tags landed at the right commit — and then all
# three release workflows died on `gh release download "coherent-set/v0.58.1" ... release not found`,
# because since .github#2600 `release-kit.yml` DOWNLOADS the packed set instead of packing it, and
# the only thing that creates that set was `workflow_dispatch`-only. Nothing joined the two rails.
# The subject of the legs below is therefore the JOB GRAPH, not the decision program: a leg that
# scored `decide()` could not have caught this and cannot catch its recurrence.
#
# The invariant, stated once so the mutations below each falsify one clause of it:
#
#   (1) at least one job in kit-auto-publish.yml pushes a `refs/tags/` ref  [else no subject]
#   (2) at least one job calls release-saga-prepare.yml                     [else no bytes]
#   (3) every tagging job's TRANSITIVE `needs:` closure contains a preparing job
#   (4) each tagging job's `if:` is identical to that preparing job's `if:` — so no decided action
#       reaches the tag write without also reaching preparation
#   (5) neither condition uses `always()`/`!cancelled()`/`failure()`, which would defeat (3) by
#       running the dependent even when its `needs:` was skipped or failed
#   (6) release-saga-prepare.yml admits `workflow_call`, and declares every input the caller passes
#   (7) it BINDS to that input rather than merely declaring it: the checkout `ref:` reads it, and no
#       `run:` block reaches for `$GITHUB_SHA` — which under `workflow_call` is the CALLER's head,
#       not the commit the coherent-set tags name
#
# Clause (7) is not hypothetical tidiness. `release-saga-ci.sh init` asserts the manifest's
# `sourceSha` equals the tag's commit, so on the `tagSiblings` repair path — where the existing kit
# tag names a commit `main` has moved past — a `$GITHUB_SHA` left behind binds the manifest to the
# wrong tree and every release workflow then refuses it. It is a mistake that reads as correct.
# ──────────────────────────────────────────────────────────────────────────────────────────────────
topo_work="$(mktemp -d)"
trap 'rm -rf "$work" "$esc_work" "$topo_work"' EXIT
topo_checker="$topo_work/topology.py"
cat > "$topo_checker" <<'PY'
"""Score the kit-auto-publish job graph against the .github#2654 invariant.

Reads the two workflow files given on argv and prints one line per violation, exiting 1 if there
are any. It restates no job id, no action name and no condition text: the tagging jobs are found by
what their steps DO, the preparing jobs by what they CALL, and the conditions are compared to each
other rather than to a literal. That is deliberate — a leg that hardcoded `tag`/`prepare` would go
green the moment someone renamed a job while decoupling it, which is the failure it exists to catch.
"""
import re
import sys

import yaml

kap_path, prep_path = sys.argv[1], sys.argv[2]
kap = yaml.safe_load(open(kap_path, encoding="utf-8"))
prep = yaml.safe_load(open(prep_path, encoding="utf-8"))
prep_text = open(prep_path, encoding="utf-8").read()
prep_file = prep_path.replace("\\", "/").rsplit("/", 1)[-1]
problems = []

jobs = {k: v for k, v in (kap.get("jobs") or {}).items() if isinstance(v, dict)}


def triggers(doc):
    # PyYAML resolves the bare key `on:` to the BOOLEAN True (YAML 1.1 truthy), so a plain
    # doc["on"] misses it and every reusable workflow looks like a non-callee. Same trap the repo's
    # own timeout gate documents.
    return doc.get("on") or doc.get(True) or {}


def needs_of(job):
    declared = job.get("needs")
    if declared is None:
        return []
    return [declared] if isinstance(declared, str) else list(declared)


def closure(start):
    seen, stack = set(), list(needs_of(jobs.get(start, {})))
    while stack:
        current = stack.pop()
        if current in seen or current not in jobs:
            continue
        seen.add(current)
        stack.extend(needs_of(jobs[current]))
    return seen


def pushes_tags(job):
    for step in job.get("steps") or []:
        run = step.get("run") or ""
        if "git push" in run and "refs/tags/" in run:
            return True
    return False


def condition(name):
    return " ".join(str(jobs.get(name, {}).get("if") or "").split())


tagging = sorted(name for name, job in jobs.items() if pushes_tags(job))
preparing = sorted(
    name for name, job in jobs.items()
    if str(job.get("uses") or "").replace("\\", "/").rsplit("/", 1)[-1] == prep_file
)

# (1) and (2): a leg whose subject has vanished must be RED, never a vacuous green. Losing either
# side is indistinguishable, from the graph alone, from a correctly-joined graph with nothing in it.
if not tagging:
    problems.append(
        f"no job in {kap_path} pushes a `refs/tags/` ref — this leg's subject is gone. That is a "
        f"no-verdict, not a pass."
    )
if not preparing:
    problems.append(
        f"no job in {kap_path} calls {prep_file} — the tag write has nothing to be downstream of, "
        f"which is precisely the .github#2654 state (tags pushed, `coherent-set/v<version>` never "
        f"prepared, all three release workflows dead on `release not found`)."
    )

for name in tagging:
    reached = closure(name) & set(preparing)
    # (3) A `needs:` job that is skipped or fails skips its dependents. That propagation IS the
    # control; without the edge there is nothing stopping a tag push on a failed preparation.
    if not reached:
        problems.append(
            f"job {name!r} pushes coherent-set tags, but no job calling {prep_file} is in its "
            f"transitive `needs:` closure {sorted(closure(name))} — a run can therefore tag "
            f"without prepared bytes (.github#2654)."
        )
        continue
    for prepared in sorted(reached):
        # (4) Identical conditions. `needs:` alone only covers "prepare ran and failed"; it does not
        # cover "prepare was never selected". If the tag job's condition admitted an action the
        # prepare job's did not, that action would skip prepare — and a skipped need skips the
        # dependent, so the tag would not happen either. The real cost is the reverse asymmetry and
        # every future edit to one condition alone, which is why they are pinned to each other
        # rather than to a literal list of actions.
        if condition(name) != condition(prepared):
            problems.append(
                f"job {name!r} and its preparing job {prepared!r} have different `if:` conditions "
                f"({condition(name)!r} vs {condition(prepared)!r}) — the two must select exactly "
                f"the same runs, or the tag write and its bytes can be decided independently."
            )

for name in sorted(set(tagging) | set(preparing)):
    # (5) `always()`, `!cancelled()` and `failure()` all run a dependent whose `needs:` did not
    # succeed, which silently converts clause (3) into decoration.
    text = condition(name)
    for token in ("always(", "cancelled(", "failure("):
        if token in text:
            problems.append(
                f"job {name!r} guards itself with `{token})` ({text!r}) — that runs the job even "
                f"when a `needs:` job was skipped or failed, defeating the ordering this leg "
                f"asserts."
            )

prep_triggers = triggers(prep)
if "workflow_call" not in prep_triggers:
    # (6) Without it the caller's `uses:` cannot resolve at all — and the failure would read as a
    # broken workflow reference rather than as the fail-closed refusal it must be.
    problems.append(
        f"{prep_file} does not admit `workflow_call` (triggers: {sorted(prep_triggers)}) — "
        f"kit-auto-publish cannot call it, so preparation is operator-only again and the patch "
        f"line tags without bytes."
    )

declared_inputs = set(((prep_triggers.get("workflow_call") or {}).get("inputs") or {}))
for name in preparing:
    for passed in sorted((jobs[name].get("with") or {})):
        if passed not in declared_inputs:
            problems.append(
                f"job {name!r} passes `{passed}` to {prep_file}, which declares no such "
                f"`workflow_call` input (declared: {sorted(declared_inputs)})."
            )

# (7) Declaring an input and ignoring it is the most expensive shape available here: it looks
# joined, resolves cleanly, and binds the manifest to the wrong commit only on the repair path.
if declared_inputs:
    checkout_refs = [
        str((step.get("with") or {}).get("ref") or "")
        for job in (prep.get("jobs") or {}).values() if isinstance(job, dict)
        for step in (job.get("steps") or [])
        if str(step.get("uses") or "").startswith("actions/checkout")
    ]
    if not any(re.search(r"inputs\.[A-Za-z0-9_-]+", ref) for ref in checkout_refs):
        problems.append(
            f"{prep_file} declares `workflow_call` input(s) {sorted(declared_inputs)} but no "
            f"`actions/checkout` step resolves its `ref:` from an input — it would pack the "
            f"CALLER's head no matter what the caller asked for."
        )

for job in (prep.get("jobs") or {}).values():
    if not isinstance(job, dict):
        continue
    for step in job.get("steps") or []:
        run = step.get("run") or ""
        if "GITHUB_SHA" in run:
            problems.append(
                f"{prep_file} step {step.get('name') or step.get('uses')!r} reads `$GITHUB_SHA` — "
                f"under `workflow_call` that is the CALLER's head, not the commit the coherent-set "
                f"tags name. `release-saga-ci.sh init` asserts the manifest's `sourceSha` equals "
                f"the tag's commit, so on the repair path this binds the manifest to a tree no tag "
                f"names and every release workflow then refuses it."
            )

for problem in problems:
    print(problem)
sys.exit(1 if problems else 0)
PY

real_kap="$root/.github/workflows/kit-auto-publish.yml"
real_prep="$root/.github/workflows/release-saga-prepare.yml"

# assert_topology <label> <expect: ok|red> <kap> <prep> [needle-that-must-appear-in-the-finding]
assert_topology() {
  local label="$1" expect="$2" kap="$3" prep="$4" needle="${5:-}" rc=0 out
  out="$(python3 "$topo_checker" "$kap" "$prep" 2>&1)" || rc=$?
  if [ "$expect" = ok ]; then
    [ "$rc" -eq 0 ] || { echo "$label: expected the topology invariant to HOLD, but:" >&2; echo "$out" >&2; exit 1; }
  else
    [ "$rc" -ne 0 ] || { echo "$label: expected the topology invariant to be VIOLATED, but it passed — a mutation that cannot turn this leg red has not tested it" >&2; exit 1; }
    if [ -n "$needle" ]; then
      case "$out" in
        *"$needle"*) : ;;
        *) echo "$label: the leg went red for the WRONG reason — no finding mentioned '$needle'. Got:" >&2; echo "$out" >&2; exit 1 ;;
      esac
    fi
  fi
  checks=$((checks + 1))
}

# The shipped files must satisfy every clause.
assert_topology topology-holds ok "$real_kap" "$real_prep"

# ---- GATE-INVERSION EVIDENCE (pnext-item §3), one committed mutation per clause. Each rewrites a
#      COPY of the real workflow through a YAML round-trip, changing exactly one key, and each must
#      turn the leg red for its OWN reason — an inversion that reds for someone else's reason has
#      not isolated the clause it claims to cover. ----
mutate_kap() {
  python3 - "$real_kap" "$1" "$2" <<'PY'
import sys
import yaml

src, dst, mode = sys.argv[1], sys.argv[2], sys.argv[3]
doc = yaml.safe_load(open(src, encoding="utf-8"))
jobs = doc["jobs"]

def tagging_job():
    for name, job in jobs.items():
        for step in (job.get("steps") or []):
            run = step.get("run") or ""
            if "git push" in run and "refs/tags/" in run:
                return name
    raise SystemExit("mutation precondition failed: no tagging job to mutate")

def preparing_job():
    for name, job in jobs.items():
        if str(job.get("uses") or "").endswith("release-saga-prepare.yml"):
            return name
    raise SystemExit("mutation precondition failed: no preparing job to mutate")

if mode == "decouple-needs":
    # The pre-repair shape, structurally: the tag write no longer waits on the bytes.
    tag = tagging_job()
    jobs[tag]["needs"] = [n for n in jobs[tag]["needs"] if n != preparing_job()]
elif mode == "drift-condition":
    # The asymmetry clause (4) exists for: the tag job selects a run the prepare job does not.
    tag = tagging_job()
    jobs[tag]["if"] = jobs[tag]["if"] + " || needs.decide.outputs.action == 'openEvidencePr'"
elif mode == "always":
    # `always()` runs the dependent even when its `needs:` failed — clause (3) with the teeth out.
    tag = tagging_job()
    jobs[tag]["if"] = "always()"
elif mode == "drop-prepare-job":
    # The literal .github#2654 state: a rail that tags and prepares nothing.
    del jobs[preparing_job()]
else:
    raise SystemExit(f"unknown mutation {mode}")

with open(dst, "w", encoding="utf-8") as out:
    yaml.safe_dump(doc, out, sort_keys=False)
PY
}

# The mutant keeps the callee's ORIGINAL BASENAME, in a directory of its own. The checker matches a
# preparing job by comparing the caller's `uses:` basename against the callee it was handed, so a
# mutant written as `prep-undeclared.yml` reds with "no job calls it" — a DIFFERENT clause, which
# would leave the clause under test unexercised behind a red that looks like the right one.
mutate_prep() {
  mkdir -p "$1"
  python3 - "$real_prep" "$1/release-saga-prepare.yml" "$2" <<'PY'
import sys
import yaml

src, dst, mode = sys.argv[1], sys.argv[2], sys.argv[3]
doc = yaml.safe_load(open(src, encoding="utf-8"))
on_key = "on" if "on" in doc else True
triggers = doc[on_key]

if mode == "dispatch-only":
    # Exactly the pre-repair trigger surface: `workflow_dispatch` and nothing else — the sentence
    # this workflow's header used to state as an assumption.
    doc[on_key] = {"workflow_dispatch": None}
elif mode == "undeclare-input":
    del triggers["workflow_call"]["inputs"]["source-sha"]
elif mode == "ignore-input":
    # Declared, resolvable, and silently ignored: the checkout stops honouring it and the steps go
    # back to `$GITHUB_SHA`.
    for job in doc["jobs"].values():
        for step in job.get("steps") or []:
            if str(step.get("uses") or "").startswith("actions/checkout"):
                step.pop("with", None)
            if "run" in step:
                step["run"] = step["run"].replace("$FSGG_SOURCE_SHA", "$GITHUB_SHA")
else:
    raise SystemExit(f"unknown mutation {mode}")

with open(dst, "w", encoding="utf-8") as out:
    yaml.safe_dump(doc, out, sort_keys=False)
PY
}

mutate_kap "$topo_work/kap-decoupled.yml" decouple-needs
assert_topology inversion-decoupled-needs red "$topo_work/kap-decoupled.yml" "$real_prep" \
  "transitive \`needs:\` closure"

mutate_kap "$topo_work/kap-drifted.yml" drift-condition
assert_topology inversion-drifted-condition red "$topo_work/kap-drifted.yml" "$real_prep" \
  "different \`if:\` conditions"

mutate_kap "$topo_work/kap-always.yml" always
assert_topology inversion-always red "$topo_work/kap-always.yml" "$real_prep" \
  "defeating the ordering"

mutate_kap "$topo_work/kap-noprepare.yml" drop-prepare-job
assert_topology inversion-no-preparing-job red "$topo_work/kap-noprepare.yml" "$real_prep" \
  "never prepared"

mutate_prep "$topo_work/dispatch-only" dispatch-only
assert_topology inversion-dispatch-only red "$real_kap" "$topo_work/dispatch-only/release-saga-prepare.yml" \
  "does not admit \`workflow_call\`"

mutate_prep "$topo_work/undeclared" undeclare-input
assert_topology inversion-undeclared-input red "$real_kap" "$topo_work/undeclared/release-saga-prepare.yml" \
  "declares no such"

mutate_prep "$topo_work/ignores-input" ignore-input
assert_topology inversion-ignored-input red "$real_kap" "$topo_work/ignores-input/release-saga-prepare.yml" \
  "reads \`\$GITHUB_SHA\`"

# ---- THE JOIN TO `decide()`. The clauses above are about the graph; this is the one assertion that
#      ties them to the patch line the defect actually fired on. `decide()` must still answer `tag`
#      for a next-patch candidate — that is the `eligible` fixture at the top of this file — and the
#      action it answers must be one the tagging job's condition selects. Neither half is
#      interesting alone: a graph that orders jobs nobody reaches is inert, and a decision nothing
#      acts on is what .github#2654 measured from the other side. ----
python3 - "$real_kap" "$work/eligible.json" <<'PY'
import json
import subprocess
import sys

import yaml

kap_path, facts_path = sys.argv[1], sys.argv[2]
action = json.loads(
    subprocess.run(
        [sys.executable, "scripts/kit-auto-publish.py", "--facts", facts_path, "--json"],
        capture_output=True, text=True, check=True,
    ).stdout
)["action"]
if action != "tag":
    sys.exit(f"the eligible next-patch fixture no longer decides `tag` (got {action!r})")

jobs = yaml.safe_load(open(kap_path, encoding="utf-8"))["jobs"]
tagging = [
    name for name, job in jobs.items()
    if any("git push" in (s.get("run") or "") and "refs/tags/" in (s.get("run") or "")
           for s in (job.get("steps") or []))
]
if not tagging:
    sys.exit("no tagging job to join the decision to")
for name in tagging:
    if f"'{action}'" not in str(jobs[name].get("if") or ""):
        sys.exit(
            f"`decide()` answers {action!r} on the patch line, but job {name!r}'s condition "
            f"({jobs[name].get('if')!r}) does not select it — the decision and the act have come "
            f"apart, which is the .github#2654 class of defect."
        )
PY
checks=$((checks + 1))

echo "kit auto-publish state machine: $checks passed"
