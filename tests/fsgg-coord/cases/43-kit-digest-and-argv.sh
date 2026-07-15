#!/usr/bin/env bash
# case: kit digest and argv
# tier: full
# covers: claim take who
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="43-kit-digest-and-argv"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #469 / #563 / #588: the kit-digest obligation is OBSERVED, not inferred from a declaration ---
#
# `repos.lock` pins a content digest of every kit source (ADR-0019, #527). Editing one invalidates it
# and reds `main` — and the obligation was invisible, because `verify-paths` only asks "did the PR stay
# INSIDE what you declared", never "was your declaration SUFFICIENT for what you touched" (#465/#469).
#
# The FIRST fix asked a question it could not answer: "is `registry/repos.yml` in your touch-set?" —
# and called the obligation MET if it was. #527 then moved the digests out of the authored `repos.yml`
# into the generated `repos.lock`, and the warning did not move with it. So:
#
#   * it FAILED OPEN — declare `repos.yml` and the warning went silent while `repos.lock` was still
#     stale and `main` was still red. Mute exactly where it was needed (#563; epic #266's shape).
#   * its ADVICE BROKE #309 — it told you to reserve `repos.yml` (the three-worker deadlock #527 was
#     merged to REMOVE, #428) and to run `repos.sh digest`, which still exists and now writes nothing.
#
# The old fixture asserted that fail-open AS A FEATURE ("declaring registry/repos.yml must SILENCE the
# warning"). It is gone. A DECLARATION is not the obligation; a MATCHING DIGEST is — so the tool now
# recomputes the digest and looks, and these assertions stand a tree up and make it genuinely stale.

KITROOT="$WORK/kitroot"
mkdir -p "$KITROOT/.claude/skills/pnext-item" "$KITROOT/.agents/skills/pnext-item" \
         "$KITROOT/scripts" "$KITROOT/registry"
kit_seed() {   # (re)write the tree and relock it, so the lock is HONEST before each scenario
  printf 'skill body v1\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
  cp "$KITROOT/.claude/skills/pnext-item/SKILL.md" "$KITROOT/.agents/skills/pnext-item/SKILL.md"
  printf '#!/usr/bin/env bash\n# client v1\n' >"$KITROOT/scripts/fsgg-coord"
  {
    printf '# registry/repos.lock — GENERATED.\n'
    printf '%s  .claude/skills/pnext-item\n' "$(sha256sum "$KITROOT/.claude/skills/pnext-item/SKILL.md" | cut -d' ' -f1)"
    printf '%s  scripts/fsgg-coord\n'        "$(sha256sum "$KITROOT/scripts/fsgg-coord" | cut -d' ' -f1)"
  } >"$KITROOT/registry/repos.lock"
}
kd() { FSGG_KIT_ROOT="$KITROOT" PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
         bash "$COORD" --worker kite-469 "$@"; }

# THE NEGATIVE CONTROL FIRST, because it is the one that can silently rot: a tree whose lock MATCHES
# must produce NO warning. If this ever goes green by accident (a broken root, an unreadable lock),
# every positive assertion below is vacuous.
kit_seed
w_clean="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord' 2>&1 || true)"
case "$w_clean" in
  *"KIT DIGEST"*) bad "#563: a lock that MATCHES must not warn — the obligation is met" "$w_clean" ;;
  *) ok "#563: a lock that MATCHES must not warn — the obligation is met" ;;
esac

# (1) A STALE CLIENT digest is observed and named — regardless of what the touch-set declares.
kit_seed; printf '# client v2 — edited\n' >>"$KITROOT/scripts/fsgg-coord"
w469="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord, tests/fsgg-coord/run.sh' 2>&1 || true)"
assert_contains "#469: widen NAMES a kit source whose digest is now STALE" "KIT DIGEST" "$w469"
assert_contains "#469: ...and prints the CURRENT regenerate command" "repos.sh relock" "$w469"
assert_contains "#469: ...and says which gate will red main" "repos-registry-selftest" "$w469"
# The post-#527 rule, in the advice itself: repos.lock is generated + CI-gated, so it must NOT be
# reserved. Telling a worker to declare it is telling them to re-create #428.
assert_contains "#469: ...and says NOT to reserve the generated lock (#309/#527)" \
  "do NOT reserve it" "$w469"
case "$w469" in
  *"repos.sh digest"*) bad "#588: the advice must not name \`repos.sh digest\` — it writes nothing now" "$w469" ;;
  *) ok "#588: the advice must not name \`repos.sh digest\` — it writes nothing now" ;;
esac
# ...and it still widens. Advisory, never fatal: `repos-registry-selftest` is the authority.
assert_contains "#469: ...while STILL widening (advisory, not fatal)" "widened FS.GG.SDD#74" "$w469"

# (2) THE FAIL-OPEN, PINNED. Declaring `registry/repos.yml` used to SILENCE this. It must not: the
#     lock is still stale, and main is still red. This is the assertion #563 exists for.
w_yml="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord, registry/repos.yml' 2>&1 || true)"
assert_contains "#563: declaring registry/repos.yml must NOT silence a genuinely stale lock" \
  "KIT DIGEST" "$w_yml"

# (3) A STALE SKILL digest is observed too — the coupling is not client-specific.
kit_seed; printf 'skill body v2\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
cp "$KITROOT/.claude/skills/pnext-item/SKILL.md" "$KITROOT/.agents/skills/pnext-item/SKILL.md"
w469s="$(kd widen 'FS.GG.SDD#74' --paths '.claude/skills/pnext-item/**' 2>&1 || true)"
assert_contains "#469: a SKILL source is content-addressed too, and is named" \
  ".claude/skills/pnext-item" "$w469s"

# (4) SKILL ROOTS — the byte-identical union (ADR-0011/0014) is OBSERVED, not inferred. Edit one root
#     and not the other: the `roots` gate reds main, and the tool must say so. Previously this hung off
#     the digest gap's early return, so declaring `repos.yml` suppressed BOTH obligations at once.
kit_seed; printf 'skill body v2 — one root only\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
w_roots="$(kd widen 'FS.GG.SDD#74' --paths '.claude/skills/pnext-item/**' 2>&1 || true)"
assert_contains "#563: diverged skill roots are NAMED" "SKILL ROOTS" "$w_roots"
assert_contains "#563: ...with the mirror command that fixes it" ".agents/skills/pnext-item" "$w_roots"
# ...and a CLIENT kit has no mirror, so a client-only staleness must NOT nag about roots.
kit_seed; printf '# client v2\n' >>"$KITROOT/scripts/fsgg-coord"
w_client="$(kd widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord' 2>&1 || true)"
case "$w_client" in
  *"SKILL ROOTS"*) bad "#469: a CLIENT kit must NOT be told to mirror skill roots" "$w_client" ;;
  *) ok "#469: a CLIENT kit must NOT be told to mirror skill roots" ;;
esac

# (5) No lock to read — a RECEIVER repo mirrors the kit but not the registry. Stay silent rather than
#     nagging every worker in every downstream repo about a file they do not have.
w469r="$(FSGG_KIT_ROOT="$WORK/no-such-root" PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
           bash "$COORD" --worker kite-469 widen 'FS.GG.SDD#74' --paths 'scripts/fsgg-coord' 2>&1 || true)"
case "$w469r" in
  *"KIT DIGEST"*) bad "#469: no lock to read -> silent (receiver repos have no registry)" "$w469r" ;;
  *) ok "#469: no lock to read -> silent (receiver repos have no registry)" ;;
esac
kit_seed

# ================================================================================================
# THE CLAIM SCAN MUST NOT TRAVEL THROUGH `argv` (FS-GG/.github#497)
# ================================================================================================
# `active_claims` reads each candidate's full BODY on purpose — arm B carries it so a touch-set costs
# zero extra reads. It then used to pass that whole set back through the jq COMMAND LINE, both to
# accumulate arm B repo-by-repo and to merge the two arms. On Linux a SINGLE argument is capped at
# MAX_ARG_STRLEN (128 KiB) — independently of the far larger total ARG_MAX — so past that, `execve`
# returns E2BIG, jq never runs, the `$( )` yields the EMPTY STRING, and the next loop iteration feeds
# it back as `--argjson acc ''` ("invalid JSON text").
#
# This is not a corner case: the org crossed 128 KiB of open-issue bodies in July 2026 and EVERY
# claim-aware read — who, reap, batch, take, inbox, widen, overlap --active — died at once. `take`
# could not schedule, so no worker could pick up work through the protocol at all. It failed CLOSED
# (#461's guard refused to report the empty claim set as "nobody holds anything"), so it was a loud
# outage rather than a double-claim — but an outage that no amount of waiting would clear.
#
# The fixture therefore serves a candidate set BIGGER THAN THE CAP and asserts the scan still reads
# it. The size assertion below is load-bearing: if a later edit shrinks these bodies under 128 KiB,
# the test would still pass while no longer exercising the bug, which is worse than not having it.
echo "--- .github#497: a claim scan larger than MAX_ARG_STRLEN is still readable ---"

ARG_CAP=131072                      # MAX_ARG_STRLEN: the per-argument ceiling, not ARG_MAX
seed_fat_issue() {                  # <num> <body-bytes> — an open, chatty issue with a BIG body
  local n="$1" bytes="$2" repo="FS-GG/FS.GG.Audio" filler body
  # Each body stays well under GitHub's 65,536-CHARACTER cap, so it is the ACCUMULATED set — not any
  # one issue — that breaches MAX_ARG_STRLEN here. That is the outage this section pins. (A single
  # body CAN breach it on its own once the characters are multi-byte: 65,536 CJK chars is ~196 KB.
  # A different defect, on a per-item argv path this fix does not touch — filed as #507.)
  filler="$(head -c "$bytes" /dev/zero | tr '\0' 'x')"
  body="Paths: src/Fat$n/**

$filler"
  jq -n --argjson n "$n" --arg t "fat body $n" --arg b "$body" --arg r "$repo" \
    '{id:($n + 1000), number:$n, title:$t, body:$b, assignees:[], state:"open", repo:$r,
      html_url:("https://github.com/" + $r + "/issues/" + ($n|tostring))}' >"$STORE/issue-$n.json"
  echo '[]' >"$STORE/comments-$n.json"
}
fat() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }

for n in 530 531 532; do seed_fat_issue "$n" 50000; done
# `open_claim_candidates` prunes `comments == 0` — a claim marker IS a comment, so a silent issue can
# hold no lock. These must therefore be CHATTY to enter the candidate set (and carry their bodies in
# with them): #530 gets a real claim marker, #531/#532 merely get talked at, exactly as a live board
# looks. Route both through the tool so the comment schema cannot drift from the real one.
fat --worker kite-497 claim 'FS-GG/FS.GG.Audio#530' >/dev/null 2>&1 || true
for n in 531 532; do
  fat --worker kite-497 say "FS-GG/FS.GG.Audio#$n" --to '*' 'chatter, no marker' >/dev/null 2>&1 || true
done
# Assert the SEED landed before asserting anything about the scan. Without this, a regression in
# `claim` shows up below as `expected='kite-497' actual=''` — which reads as a broken claim SCAN and
# sends the next reader into the wrong function entirely.
assert_eq "#497: (fixture) the marker seeded onto the fat issue" "kite-497" "$(workers_on 530)"

# The fixture really is over the cap — otherwise everything below is vacuous.
fatbytes="$(jq -c -s '[.[] | {number, title, url: .html_url, body}]' \
              "$STORE"/issue-530.json "$STORE"/issue-531.json "$STORE"/issue-532.json | wc -c)"
if [ "$fatbytes" -gt "$ARG_CAP" ]; then
  ok "#497: the fixture candidate set really exceeds MAX_ARG_STRLEN ($fatbytes > $ARG_CAP bytes)"
else
  bad "#497: the fixture candidate set must EXCEED $ARG_CAP bytes or it tests nothing" "$fatbytes"
fi

# The scan reads it. Pre-fix this died with `Argument list too long` / `invalid JSON text passed to
# --argjson`, and #461's guard turned that into a hard `cannot read the claim set`.
fatwho="$(fat who --repo FS-GG/FS.GG.Audio --json 2>&1 || true)"
case "$fatwho" in
  *"cannot read the claim set"*|*"Argument list too long"*|*"--argjson"*)
    bad "#497: a claim set over the arg cap must still be READ, not die" "$fatwho" ;;
  *) ok "#497: a claim set over the arg cap must still be READ, not die" ;;
esac
assert_eq "#497: ...and the claim inside that oversized set is reported, with its holder" \
  "kite-497" "$(printf '%s' "$fatwho" | jq -r '.[] | select(.number==530) | .worker' 2>/dev/null)"
# The scan stays HONEST at size: the two chatty-but-markerless issues are not in-flight work, and a
# body big enough to break the plumbing must not become a claim. Scoped to the fat fixtures — Audio
# also holds the #422/#424 overlap section's board item, which `who` reports for its own good reason.
assert_eq "#497: ...and chatty markerless issues in that set are still not claims" "[530]" \
  "$(printf '%s' "$fatwho" | jq -c '[.[] | select(.number >= 530 and .number <= 532) | .number] | sort' 2>/dev/null)"


harness_report
