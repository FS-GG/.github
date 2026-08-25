#!/usr/bin/env bash
# Fixture for `parse` in scripts/NewSddWorkspace/Program.fs — the new-sdd-workspace CLI arg parser.
#
# The regression under guard is .github#388: `parse` is a pure `string list -> Result<Options,string>`,
# yet nothing exercised it, so a `--profile`/`--ref` flag-as-value bug and an unvalidated `--profile`
# shipped and were only caught late. This fixture pins the grammar so the next edit cannot silently
# reintroduce a fail-open parse — the same "a gate reports green on a missing subject" shape the org
# keeps finding late.
#
# It drives the REAL compiled CLI end-to-end (never retyping the parse logic) and reads the parse
# result off the process's exit-code contract, which `main` defines exactly:
#   exit 2   → parse REJECTED — main prints `error: <msg>` + usage        [Error leg]
#   exit 127 → parse ACCEPTED → run → preflight "fsgg-sdd is not on PATH"  [Ok leg]
# To make the Ok leg hermetic (no network, no scaffold, no side effects) the CLI is run with
# `fsgg-sdd` scrubbed from PATH, so a valid parse stops at that preflight instead of scaffolding for
# real. A meta-assertion at the end confirms the harness stayed hermetic (the target was never created).
#
# Deliberately asserts ONLY the committed grammar: it does not touch `--pinned` (an in-flight flag on
# an unmerged branch) — pinning a message for a flag mid-flight would red the moment that lands.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
PROJ="$REPO_ROOT/scripts/NewSddWorkspace/NewSddWorkspace.fsproj"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

[ -f "$PROJ" ] || { echo "::error::missing $PROJ — nothing to test"; exit 1; }

echo "new-sdd-workspace parse fixture — building the CLI (Release)…"
dotnet build "$PROJ" -c Release --nologo -v quiet 1>&2

# Newest *built artifact* wins by modification time. Lexical path order used to prefer a stale
# `publish/` DLL over the just-built `net*/` DLL, which let the mutation controls compile a changed
# subject and then execute yesterday's artifact — a false green in the guard that prevents false green.
DLL="$(find "$REPO_ROOT/scripts/NewSddWorkspace/bin/Release" -type f -name new-sdd-workspace.dll -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -1 | cut -d' ' -f2-)"
[ -n "$DLL" ] && [ -f "$DLL" ] || { echo "::error::could not locate built new-sdd-workspace.dll under bin/Release"; exit 1; }
echo "new-sdd-workspace parse fixture — dll='$DLL'"

# Exercise the wizard's pure decision boundary directly. The normal interactive path intentionally
# requires a TTY; this deterministic probe proves that the removed confirmations assemble the two
# established defaults (coordination ON, post-scaffold upgrade OFF) while preserving non-default
# board/repo/chore-lock values.
dotnet fsi --reference:"$DLL" "$HERE/wizard-defaults.fsx"

# Scrub `fsgg-sdd` from the child's PATH by handing it only the directory the dotnet muxer lives in
# (onPath does a non-recursive PATH scan, and fsgg-sdd is a global tool in a *different* dir). On a
# fresh CI runner fsgg-sdd is absent anyway; this keeps the fixture hermetic on a dev box that has it.
DOTNET_DIR="$(dirname "$(command -v dotnet)")"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/new-sdd-workspace-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
# A target dir that every VALID parse below must leave UNCREATED — proof the Ok leg stopped at the
# fsgg-sdd preflight and never reached scaffolding (step 1 is the first thing to create it).
TGT="$WORK/never-created"

# cli <args…> → rc; combined output in $OUT. stdin from /dev/null so the no-arg case stays on the
# non-interactive parse path (main only prompts on an interactive terminal), not the wizard.
cli() { local rc=0; OUT="$(PATH="$DOTNET_DIR" dotnet "$DLL" "$@" </dev/null 2>&1)" || rc=$?; return "$rc"; }

# expect_err <desc> <needle> -- <cli args…>   (parse must be rejected: exit 2 + message substring)
expect_err() {
  local desc="$1" needle="$2"; shift 3   # drop desc, needle, and the literal '--' separator
  local rc=0; cli "$@" || rc=$?
  if [ "$rc" -eq 2 ] && printf '%s' "$OUT" | grep -qF -- "$needle"; then
    ok "$desc"
  else
    bad "$desc" "want rc=2 + substring '$needle'; got rc=$rc"$'\n'"--- output ---"$'\n'"$OUT"
  fi
}

# expect_ok <desc> -- <cli args…>   (parse must be accepted: reaches preflight, exit 127, no scaffold)
expect_ok() {
  local desc="$1"; shift 2               # drop desc and the literal '--' separator
  local rc=0; cli "$@" || rc=$?
  if [ "$rc" -eq 127 ] && printf '%s' "$OUT" | grep -qF -- "fsgg-sdd is not on PATH"; then
    ok "$desc"
  else
    bad "$desc" "want rc=127 + preflight panel (parse accepted); got rc=$rc"$'\n'"--- output ---"$'\n'"$OUT"
  fi
}

# ── Error leg: the parse grammar rejects, exit 2 with the actionable message ──────────────────────

expect_err "no args → positionals required (non-interactive falls through to parse, not the wizard)" \
  "target dir and product name are required" --
expect_err "a single positional is not enough" \
  "target dir and product name are required" -- "$TGT"
expect_err "a flag before the two positionals is rejected (target/product must come first)" \
  "target dir and product name are required" -- --profile game
# The #388 flag-as-value bug: a following --flag is a MISSING value, not the value.
expect_err "--profile swallowing the next flag as its value is caught (#388)" \
  "--profile needs a value (got flag '--ref')" -- "$TGT" P --profile --ref v1
expect_err "--ref swallowing the next flag as its value is caught (#388)" \
  "--ref needs a value (got flag '--upgrade')" -- "$TGT" P --ref --upgrade
# The #388 unvalidated-profile bug: an unknown profile is refused on the CLI path, not late in scaffold.
expect_err "an unknown --profile value is rejected against the known set (#388)" \
  "unknown profile 'bogus'" -- "$TGT" P --profile bogus
expect_err "--profile with no following token needs a value" \
  "--profile needs a value" -- "$TGT" P --profile
expect_err "--template with no following token needs a value" \
  "--template needs a value" -- "$TGT" P --template
expect_err "an unknown --template is rejected before any scaffold" \
  "unknown template 'bogus'" -- "$TGT" P --template bogus
expect_err "--lifecycle swallowing the next flag as its value is caught" \
  "--lifecycle needs a value (got flag '--ref')" -- "$TGT" P --lifecycle --ref v1
expect_err "--lifecycle with no following token needs a value" \
  "--lifecycle needs a value" -- "$TGT" P --lifecycle
expect_err "an unknown lifecycle is rejected before any scaffold" \
  "unknown lifecycle 'bogus'" -- "$TGT" P --lifecycle bogus
expect_err "a rendering profile is rejected for a non-rendering template" \
  "--profile is only supported by the rendering template" -- "$TGT" P --template console --profile game
expect_err "fable-bindings requires its package closure" \
  "requires both --npm-package and --npm-version" -- "$TGT" P --template fable-bindings
expect_err "fable-bindings requires its target" \
  "requires --binding-target" -- "$TGT" P --template fable-bindings --npm-package @babylonjs/core --npm-version 8.0.0
expect_err "fable-bindings validates its target" \
  "--binding-target must be browser, node, or universal" -- "$TGT" P --template fable-bindings --npm-package @babylonjs/core --npm-version 8.0.0 --binding-target wasm
expect_err "fable-bindings rejects a non-exact npm version" \
  "--npm-version must be an exact version" -- "$TGT" P --template fable-bindings --npm-package @babylonjs/core --npm-version latest --binding-target browser
expect_err "npm parameters are rejected outside fable-bindings" \
  "only supported by the fable-bindings" -- "$TGT" P --template console --npm-package @babylonjs/core --npm-version 8.0.0
expect_err "--ref with no following token needs a value" \
  "--ref needs a value" -- "$TGT" P --ref
expect_err "an unknown flag is named, not silently ignored" \
  "unknown argument: --bogus" -- "$TGT" P --bogus
# Coordination flags carry the same flag-as-value guard as --profile/--ref (#388).
expect_err "--board swallowing the next flag as its value is caught" \
  "--board needs a value (got flag '--upgrade')" -- "$TGT" P --board --upgrade
expect_err "--board with no following token needs a value" \
  "--board needs a value" -- "$TGT" P --board
expect_err "--chore-locks swallowing the next flag as its value is caught" \
  "--chore-locks needs a value (got flag '--no-governance')" -- "$TGT" P --chore-locks --no-governance
expect_err "--chore-locks with no following token needs a value" \
  "--chore-locks needs a value" -- "$TGT" P --chore-locks
expect_err "--repo swallowing the next flag as its value is caught" \
  "--repo needs a value (got flag '--board')" -- "$TGT" P --repo --board acme/R
expect_err "--repo with no following token needs a value" \
  "--repo needs a value" -- "$TGT" P --repo
expect_err "a public Project cannot omit its explicit writer allowlist" \
  "--public-board requires an explicit --trusted-writers allowlist" -- "$TGT" P --public-board

# ── Ok leg: the parse grammar accepts, reaching the fsgg-sdd preflight (exit 127, no scaffold) ─────

expect_ok "the bare two-positional form parses (Profile defaults to the provider default)" \
  -- "$TGT" P
for template in rendering console web fable-game; do
  expect_ok "--template $template parses" -- "$TGT" P --template "$template"
done

# Project production route. The double rejects the exact defects found in ordinary review: it
# requires a typed collaborator variable, nested gh fields for JSON object serialization, a selected
# mutation payload, and forbids node ids in GraphQL source. It also returns the same payload shape as
# GitHub's live ProjectV2 schema so durable state assertions exercise the built CLI, not a substring.
PROJECT_WORK="$WORK/project workspace's"; mkdir -p "$PROJECT_WORK/.fsgg" "$WORK/project-bin"
printf '%s\n' '{"securityObligations":[{"kind":"project-access","target":"acme/Roadmap"},{"kind":"repository-issue-policy","target":"acme/app"}]}' > "$PROJECT_WORK/.fsgg/scaffold-provenance.json"
cat > "$WORK/project-bin/gh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "${PROJECT_LOG:?}"
all=" $* "
case "$all" in
  *'updateProjectV2(input'*)
    case "${PROJECT_MODE:-success}" in
      visibility-change) : > "${PROJECT_STATE:?}" ;;
      visibility-stale) : ;;
      *) echo 'unexpected visibility mutation' >&2; exit 75 ;;
    esac
    printf '%s\n' '{"data":{"updateProjectV2":{"projectV2":{"public":true}}}}' ;;
  *'updateProjectV2Collaborators'*)
    python3 "${PROJECT_GRAPHQL_VALIDATOR:?}" "$@"
    case "${PROJECT_MODE:-success}" in
      mutation-failure) echo 'TOKEN-SHOULD-NOT-LEAK' >&2; exit 1 ;;
      grant-payload-mismatch) printf '%s\n' '{"data":{"updateProjectV2Collaborators":{"collaborators":{"totalCount":2,"nodes":[{"id":"U_1","login":"alice"},{"id":"U_bad","login":"mallory"}]}}}}' ;;
      *) printf '%s\n' '{"data":{"updateProjectV2Collaborators":{"collaborators":{"totalCount":2,"nodes":[{"id":"U_1","login":"alice"},{"id":"T_1","slug":"platform"}]}}}}' ;;
    esac ;;
  *'teams(first:100)'*) printf '%s\n' '{"data":{"organization":{"teams":{"nodes":[{"id":"T_1","slug":"platform"}]}}}}' ;;
  *'user(login:$login)'*) printf '%s\n' '{"data":{"user":{"id":"U_1"}}}' ;;
  *'projectsV2(first:100)'*)
    case "${PROJECT_MODE:-success}" in
      unreadable) exit 1 ;;
      missing) printf '%s\n' '{"data":{"viewer":{"login":"fixture"},"organization":{"projectsV2":{"nodes":[]}}}}' ;;
      private) printf '%s\n' '{"data":{"viewer":{"login":"fixture"},"organization":{"projectsV2":{"nodes":[{"id":"P_1","title":"Roadmap","public":false}]}}}}' ;;
      visibility-change) [ -f "${PROJECT_STATE:?}" ] && public=true || public=false; printf '{"data":{"viewer":{"login":"fixture"},"organization":{"projectsV2":{"nodes":[{"id":"P_1","title":"Roadmap","public":%s}]}}}}\n' "$public" ;;
      visibility-stale) printf '%s\n' '{"data":{"viewer":{"login":"fixture"},"organization":{"projectsV2":{"nodes":[{"id":"P_1","title":"Roadmap","public":false}]}}}}' ;;
      *) printf '%s\n' '{"data":{"viewer":{"login":"fixture"},"organization":{"projectsV2":{"nodes":[{"id":"P_1","title":"Roadmap","public":true}]}}}}' ;;
    esac ;;
  *) echo 'unexpected GraphQL route' >&2; exit 74 ;;
esac
EOF
chmod +x "$WORK/project-bin/gh"
cat > "$WORK/project-bin/new-sdd-workspace" <<'EOF'
#!/usr/bin/env bash
exec dotnet "${NEW_SDD_DLL:?}" "$@"
EOF
chmod +x "$WORK/project-bin/new-sdd-workspace"

if python3 "$HERE/validate_project_graphql.py" \
  'query=mutation($id:ID!,$collaborators:[ProjectV2Collaborator!]!){updateProjectV2Collaborators(input:{projectId:$id,collaborators:$collaborators}){collaborators{totalCount}}' \
  id=P_1 'collaborators[][userId]=U_1' 'collaborators[][role]=WRITER' >/dev/null 2>&1; then
  bad "Project GraphQL contract parser rejects syntax-invalid documents" "invalid GraphQL unexpectedly parsed"
else
  ok "Project GraphQL contract parser rejects syntax-invalid documents"
fi

run_project() {
  local mode="$1"; shift
  local rc=0
  PROJECT_MODE="$mode" PROJECT_STATE="$WORK/project-state-$mode" PROJECT_LOG="$WORK/project.log" PROJECT_GRAPHQL_VALIDATOR="$HERE/validate_project_graphql.py" NEW_SDD_DLL="$DLL" PATH="$WORK/project-bin:$DOTNET_DIR:/usr/bin:/bin" \
    dotnet "$DLL" "$@" >"$WORK/project.out" 2>&1 || rc=$?
  return "$rc"
}

project_rc=0; run_project success secure "$PROJECT_WORK" --project acme/Roadmap --public-board --trusted-writers alice,team:platform || project_rc=$?
if [ "$project_rc" -eq 1 ] && grep -q 'partial verified receipt:' "$WORK/project.out" \
  && [ "$(jq '[.securityObligations[] | select(.kind=="project-base-access-human-verification" and .target=="acme/Roadmap")] | length' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")" -eq 1 ] \
  && [ "$(jq '[.verifiedSecurityReceipts[] | select(.kind=="project-access" and .verificationState=="partial" and .basePermission=="unverified")] | length' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")" -eq 1 ] \
  && jq -e '.verifiedSecurityReceipts[] | select(.kind=="project-access") | .unverifiedFacts | index("effective-exclusive-writer-set")' "$PROJECT_WORK/.fsgg/scaffold-provenance.json" >/dev/null \
  && [ "$(jq '.verifiedSecurityReceipts[] | select(.kind=="project-access") | .trustedWriters | length' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")" -eq 2 ] \
  && jq -e '.securityObligations[] | select(.kind=="repository-issue-policy")' "$PROJECT_WORK/.fsgg/scaffold-provenance.json" >/dev/null; then
  ok "secure Project uses typed variables and persists one partial receipt plus exact human obligation"
else
  bad "secure Project production route must persist typed partial provenance" "rc=$project_rc: $(cat "$WORK/project.out")"
fi

# A second partial run replaces the receipt/obligation instead of appending. The human completion
# route then re-executes the observable production mutation and clears only that exact obligation.
run_project success secure "$PROJECT_WORK" --project acme/Roadmap --public-board --trusted-writers alice,team:platform || true
if [ "$(jq '[.securityObligations[] | select(.kind=="project-base-access-human-verification")] | length' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")" -eq 1 ]; then
  ok "Project partial resume is idempotent and deduplicates its human obligation"
else
  bad "Project partial resume must not grow duplicate obligations" "$(cat "$PROJECT_WORK/.fsgg/scaffold-provenance.json")"
fi
# A human observation that includes an unexpected effective writer cannot discharge the obligation.
project_rc=0; run_project success secure "$PROJECT_WORK" --project acme/Roadmap --trusted-writers alice,team:platform --verified-base-permission READ --verified-exclusive-writers alice,team:platform,mallory || project_rc=$?
if [ "$project_rc" -ne 0 ] && [ "$(jq '[.securityObligations[] | select(.kind=="project-base-access-human-verification")] | length' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")" -eq 1 ]; then
  ok "unexpected effective writer keeps the exact human obligation pending"
else
  bad "human completion must reject an effective writer outside the allowlist" "rc=$project_rc: $(cat "$WORK/project.out")"
fi
project_resume="$(jq -r '.securityObligations[] | select(.kind=="project-base-access-human-verification") | .resume' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")"
project_rc=0
PROJECT_MODE=success PROJECT_STATE="$WORK/project-state-success" PROJECT_LOG="$WORK/project.log" PROJECT_GRAPHQL_VALIDATOR="$HERE/validate_project_graphql.py" NEW_SDD_DLL="$DLL" PATH="$WORK/project-bin:$DOTNET_DIR:/usr/bin:/bin" \
  bash -c "$project_resume" >"$WORK/project.out" 2>&1 || project_rc=$?
if [ "$project_rc" -eq 0 ] \
  && [[ "$project_resume" != *'<workspace>'* ]] \
  && [ "$(jq '[.securityObligations[] | select(.kind=="project-base-access-human-verification")] | length' "$PROJECT_WORK/.fsgg/scaffold-provenance.json")" -eq 0 ] \
  && jq -e '.verifiedSecurityReceipts[] | select(.kind=="project-access" and .verificationState=="verified-with-human-access-review" and .basePermission=="READ" and (.humanVerifiedFacts.effectiveExclusiveWriters | length)==2)' "$PROJECT_WORK/.fsgg/scaffold-provenance.json" >/dev/null \
  && jq -e '.securityObligations[] | select(.kind=="repository-issue-policy")' "$PROJECT_WORK/.fsgg/scaffold-provenance.json" >/dev/null; then
  ok "recorded Project resume preserves its workspace identity, executes, and clears only its base obligation"
else
  bad "recorded Project human-verification resume must converge exact provenance" "command=$project_resume; rc=$project_rc: $(cat "$WORK/project.out")"
fi

# Production no-verdict matrix: each route must leave provenance byte-identical and redact tool
# output. The unexpected-writer leg proves the mutation payload itself is checked, not just reached.
for project_case in mutation-failure grant-payload-mismatch unreadable missing; do
  CASE_PROJECT="$WORK/project-$project_case"; mkdir -p "$CASE_PROJECT/.fsgg"
  printf '%s\n' '{"securityObligations":[{"kind":"project-access","target":"acme/Roadmap"}]}' > "$CASE_PROJECT/.fsgg/scaffold-provenance.json"
  cp "$CASE_PROJECT/.fsgg/scaffold-provenance.json" "$CASE_PROJECT/before.json"
  project_rc=0; run_project "$project_case" secure "$CASE_PROJECT" --project acme/Roadmap --public-board --trusted-writers alice,team:platform || project_rc=$?
  if [ "$project_rc" -ne 0 ] && cmp -s "$CASE_PROJECT/before.json" "$CASE_PROJECT/.fsgg/scaffold-provenance.json" && ! grep -q 'TOKEN-SHOULD-NOT-LEAK' "$WORK/project.out"; then
    ok "Project $project_case is a redacted no-verdict with durable state unchanged"
  else
    bad "Project $project_case must fail closed without corrupting provenance" "rc=$project_rc: $(cat "$WORK/project.out")"
  fi
done

# Visibility is a separate typed state transition: the successful leg must mutate then re-read the
# requested value; the stale leg must fail and preserve provenance byte-for-byte.
for visibility_case in visibility-change visibility-stale; do
  VIS_WORK="$WORK/project-$visibility_case"; mkdir -p "$VIS_WORK/.fsgg"
  printf '%s\n' '{"securityObligations":[{"kind":"project-access","target":"acme/Roadmap"}]}' > "$VIS_WORK/.fsgg/scaffold-provenance.json"
  cp "$VIS_WORK/.fsgg/scaffold-provenance.json" "$VIS_WORK/before.json"
  rm -f "$WORK/project-state-$visibility_case"
  project_rc=0; run_project "$visibility_case" secure "$VIS_WORK" --project acme/Roadmap --public-board --trusted-writers alice,team:platform || project_rc=$?
  if [ "$visibility_case" = visibility-change ]; then
    [ "$project_rc" -eq 1 ] && jq -e '.verifiedSecurityReceipts[] | select(.kind=="project-access" and .observedVisibility=="public")' "$VIS_WORK/.fsgg/scaffold-provenance.json" >/dev/null \
      && ok "Project visibility mutation is re-read before partial receipt" \
      || bad "changed Project visibility must be re-read and persisted" "rc=$project_rc: $(cat "$WORK/project.out")"
  else
    [ "$project_rc" -ne 0 ] && cmp -s "$VIS_WORK/before.json" "$VIS_WORK/.fsgg/scaffold-provenance.json" \
      && ok "stale Project visibility reread fails closed with provenance unchanged" \
      || bad "stale Project visibility must not produce a receipt" "rc=$project_rc: $(cat "$WORK/project.out")"
  fi
done

PROJECT_BAD_PROVENANCE="$WORK/project-bad-provenance"; mkdir -p "$PROJECT_BAD_PROVENANCE/.fsgg"
printf '{broken' > "$PROJECT_BAD_PROVENANCE/.fsgg/scaffold-provenance.json"
project_rc=0; run_project success secure "$PROJECT_BAD_PROVENANCE" --project acme/Roadmap --public-board --trusted-writers alice,team:platform || project_rc=$?
if [ "$project_rc" -ne 0 ] && grep -q 'security provenance persistence failed' "$WORK/project.out" && [ "$(cat "$PROJECT_BAD_PROVENANCE/.fsgg/scaffold-provenance.json")" = '{broken' ]; then
  ok "Project verified API result fails closed when durable provenance cannot be parsed"
else
  bad "Project persistence failure must not become console-only success" "rc=$project_rc: $(cat "$WORK/project.out")"
fi

# A private board already at the requested visibility is preserved: only the explicit writer route
# runs, and the partial receipt records `private` without a visibility mutation.
PRIVATE_WORK="$WORK/project-private"; mkdir -p "$PRIVATE_WORK/.fsgg"
printf '%s\n' '{"securityObligations":[{"kind":"project-access","target":"acme/Roadmap"}]}' > "$PRIVATE_WORK/.fsgg/scaffold-provenance.json"
: > "$WORK/project.log"
project_rc=0; run_project private secure "$PRIVATE_WORK" --project acme/Roadmap --private-board --trusted-writers alice,team:platform || project_rc=$?
if [ "$project_rc" -eq 1 ] && jq -e '.verifiedSecurityReceipts[] | select(.kind=="project-access" and .observedVisibility=="private")' "$PRIVATE_WORK/.fsgg/scaffold-provenance.json" >/dev/null \
  && ! grep -q 'updateProjectV2(input' "$WORK/project.log"; then
  ok "private Project visibility is preserved while explicit writers get a partial receipt"
else
  bad "private Project route must preserve visibility" "rc=$project_rc: $(cat "$WORK/project.out")"
fi
expect_ok "fable-bindings parses with its exact npm package closure" \
  -- "$TGT" P --template fable-bindings --npm-package @babylonjs/core --npm-version 8.0.0 --binding-target browser
# Each id in `profiles` (Program.fs) must parse. Kept in lockstep with that list by hand; a removed
# profile red-flags here, and the unknown-profile assertion above pins the closed set.
for prof in game app headless-scene governed sample-pack; do
  expect_ok "--profile $prof parses" -- "$TGT" P --profile "$prof"
done
expect_ok "--upgrade toggles" -- "$TGT" P --upgrade
expect_ok "--no-governance toggles" -- "$TGT" P --no-governance
expect_ok "--no-coordination toggles" -- "$TGT" P --no-coordination
expect_ok "--board takes an owner/title value" -- "$TGT" P --board acme/Roadmap
expect_ok "--board accepts an owner-only value (defaults the title)" -- "$TGT" P --board acme
expect_ok "--repo takes an owner/repo value" -- "$TGT" P --repo acme/Product.X
expect_ok "a public board carries an explicit writer allowlist" -- "$TGT" P --public-board --trusted-writers acme/platform,alice
expect_ok "--chore-locks takes a value" -- "$TGT" P --board acme/Roadmap --repo acme/Product.X --chore-locks "acme/Product.X#5,acme/Product.Y#7"
expect_ok "flags combine, and --ref takes a value" -- "$TGT" P --upgrade --no-governance --ref v1.2.3

# Production recovery route: a typed successful `secure <workspace> --repo` must clear ONLY its
# matching durable repository obligation. This drives the built CLI through a hermetic gh GraphQL
# response, rather than testing the JSON helper in isolation.
SECURE="$WORK/secure-workspace"; mkdir -p "$SECURE/.fsgg" "$WORK/secure-bin"
printf '%s\n' '{"securityObligations":[{"kind":"repository-issue-policy","target":"acme/app"},{"kind":"project-access","target":"acme/Roadmap"}]}' > "$SECURE/.fsgg/scaffold-provenance.json"
cat > "$WORK/secure-bin/gh" <<'EOF'
#!/usr/bin/env bash
case " $* " in
  *'mutation('* ) printf '%s\n' '{"data":{"updateRepository":{"repository":{"issueCreationPolicy":"COLLABORATORS_ONLY"}}}}' ;;
  * ) printf '%s\n' '{"data":{"viewer":{"login":"fixture"},"repository":{"id":"R_1","issueCreationPolicy":"COLLABORATORS_ONLY"}}}' ;;
esac
EOF
chmod +x "$WORK/secure-bin/gh"
set +e
secure_out="$(PATH="$WORK/secure-bin:$DOTNET_DIR:/usr/bin:/bin" dotnet "$DLL" secure "$SECURE" --repo acme/app 2>&1)"; secure_rc=$?
set -e
if [ "$secure_rc" -eq 0 ] && printf '%s' "$secure_out" | grep -q 'verified:' && grep -q 'project-access' "$SECURE/.fsgg/scaffold-provenance.json" && grep -q 'verifiedSecurityReceipts' "$SECURE/.fsgg/scaffold-provenance.json" && grep -q '"priorPolicy": "COLLABORATORS_ONLY"' "$SECURE/.fsgg/scaffold-provenance.json" && grep -q '"actor": "fixture"' "$SECURE/.fsgg/scaffold-provenance.json"; then
  ok "secure resume clears only its verified repository obligation and persists its receipt"
else
  bad "secure resume must converge matching provenance" "rc=$secure_rc: $secure_out"
fi

# Changed-and-verified repository path: the double is stateful so the production route must mutate,
# re-read, retain prior OPEN in its receipt, and replace that receipt on an idempotent rerun.
CHANGED="$WORK/changed-workspace"; mkdir -p "$CHANGED/.fsgg" "$WORK/changed-bin"
printf '%s\n' '{"securityObligations":[{"kind":"repository-issue-policy","target":"acme/app"}]}' > "$CHANGED/.fsgg/scaffold-provenance.json"
cat > "$WORK/changed-bin/gh" <<'EOF'
#!/usr/bin/env bash
case " $* " in
  *'updateRepository'*) : > "${REPO_CHANGED_STATE:?}"; printf '%s\n' '{"data":{"updateRepository":{"repository":{"issueCreationPolicy":"COLLABORATORS_ONLY"}}}}' ;;
  *)
    policy=OPEN; [ -f "${REPO_CHANGED_STATE:?}" ] && policy=COLLABORATORS_ONLY
    printf '{"data":{"viewer":{"login":"fixture"},"repository":{"id":"R_1","issueCreationPolicy":"%s"}}}\n' "$policy" ;;
esac
EOF
chmod +x "$WORK/changed-bin/gh"
changed_rc=0; changed_out="$(REPO_CHANGED_STATE="$WORK/repo-changed" PATH="$WORK/changed-bin:$DOTNET_DIR:/usr/bin:/bin" dotnet "$DLL" secure "$CHANGED" --repo acme/app 2>&1)" || changed_rc=$?
if [ "$changed_rc" -eq 0 ] && jq -e '.verifiedSecurityReceipts[] | select(.kind=="repository-issue-policy" and .priorPolicy=="OPEN" and .finalPolicy=="COLLABORATORS_ONLY")' "$CHANGED/.fsgg/scaffold-provenance.json" >/dev/null; then
  ok "repository OPEN policy is changed, re-read, and persisted with its prior policy"
else
  bad "repository changed-and-verified route must persist the typed receipt" "rc=$changed_rc: $changed_out"
fi
REPO_CHANGED_STATE="$WORK/repo-changed" PATH="$WORK/changed-bin:$DOTNET_DIR:/usr/bin:/bin" dotnet "$DLL" secure "$CHANGED" --repo acme/app >/dev/null
if [ "$(jq '[.verifiedSecurityReceipts[] | select(.kind=="repository-issue-policy" and .repository=="acme/app")] | length' "$CHANGED/.fsgg/scaffold-provenance.json")" -eq 1 ]; then
  ok "repository secure rerun idempotently replaces its durable receipt"
else
  bad "repository secure rerun must not duplicate receipts" "$(cat "$CHANGED/.fsgg/scaffold-provenance.json")"
fi

# A successful remote policy read is not a successful resume when its durable target is missing or
# malformed. These cases pin F1's fail-closed persistence boundary through the production command.
for provenance_case in missing malformed; do
  PROV_WORK="$WORK/provenance-$provenance_case"; mkdir -p "$PROV_WORK"
  if [ "$provenance_case" = malformed ]; then mkdir -p "$PROV_WORK/.fsgg"; printf '{broken' > "$PROV_WORK/.fsgg/scaffold-provenance.json"; fi
  prov_rc=0; prov_out="$(PATH="$WORK/secure-bin:$DOTNET_DIR:/usr/bin:/bin" dotnet "$DLL" secure "$PROV_WORK" --repo acme/app 2>&1)" || prov_rc=$?
  if [ "$prov_rc" -ne 0 ] && printf '%s' "$prov_out" | grep -q 'durable receipt failed'; then
    ok "repository $provenance_case provenance cannot be reported as secured"
  else
    bad "repository $provenance_case provenance must fail closed" "rc=$prov_rc: $prov_out"
  fi
done

# Production failure matrix: the secure route must fail closed for a mutation failure and for a
# stale post-write read, leaving the exact durable obligation in place and never echoing a token.
for secure_case in mutation-failure stale-reread; do
  CASE_WORK="$WORK/$secure_case-workspace"; mkdir -p "$CASE_WORK/.fsgg" "$WORK/$secure_case-bin"
  printf '%s\n' '{"securityObligations":[{"kind":"repository-issue-policy","target":"acme/app"}]}' > "$CASE_WORK/.fsgg/scaffold-provenance.json"
  cat > "$WORK/$secure_case-bin/gh" <<'EOF'
#!/usr/bin/env bash
case " $* " in
  *'mutation('* ) printf '%s\n' '{"errors":[{"message":"TOKEN-SHOULD-NOT-LEAK"}]}' ; exit 1 ;;
  * ) printf '%s\\n' '{"data":{"viewer":{"login":"fixture"},"repository":{"id":"R_1","issueCreationPolicy":"OPEN"}}}' ;;
esac
EOF
  if [ "$secure_case" = stale-reread ]; then
    cat > "$WORK/$secure_case-bin/gh" <<'EOF'
#!/usr/bin/env bash
case " $* " in
  *'mutation('* ) printf '%s\n' '{"data":{"updateRepository":{"repository":{"issueCreationPolicy":"COLLABORATORS_ONLY"}}}}' ;;
  * ) printf '%s\n' '{"data":{"viewer":{"login":"fixture"},"repository":{"id":"R_1","issueCreationPolicy":"OPEN"}}}' ;;
esac
EOF
  fi
  chmod +x "$WORK/$secure_case-bin/gh"
  set +e
  case_out="$(PATH="$WORK/$secure_case-bin:$DOTNET_DIR:/usr/bin:/bin" dotnet "$DLL" secure "$CASE_WORK" --repo acme/app 2>&1)"; case_rc=$?
  set -e
  if [ "$case_rc" -ne 0 ] && grep -q 'repository-issue-policy' "$CASE_WORK/.fsgg/scaffold-provenance.json" && ! printf '%s' "$case_out" | grep -q 'TOKEN-SHOULD-NOT-LEAK'; then
    ok "secure $secure_case fails closed and preserves its obligation without token output"
  else
    bad "secure $secure_case must fail closed" "rc=$case_rc: $case_out"
  fi
done

# ── Execution leg: a local descriptor server + stub fsgg-sdd prove real provider routing ─────────
# Parser acceptance alone is insufficient: each invocation below fetches the selected descriptor,
# runs the actual orchestrator, and records the scaffold/doctor argv in the stub. The local HTTP
# server makes descriptor selection deterministic and avoids depending on unpublished providers.
HTTP_ROOT="$WORK/templates"
mkdir -p "$HTTP_ROOT/main/providers"
for template in rendering console web fable-game fable-bindings; do
  printf 'source: fixture/%s\nprovider: %s\n' "$template" "$template" > "$HTTP_ROOT/main/providers/$template.providers.yml"
done
PORT_FILE="$WORK/http-port"
python3 - "$HTTP_ROOT" "$PORT_FILE" <<'PY' &
import functools
import http.server
import pathlib
import sys

root, port_file = sys.argv[1:]
server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(http.server.SimpleHTTPRequestHandler, directory=root))
pathlib.Path(port_file).write_text(str(server.server_port))
server.serve_forever()
PY
HTTP_PID=$!
trap 'kill "$HTTP_PID" 2>/dev/null || true; rm -rf "$WORK"' EXIT
while [ ! -s "$PORT_FILE" ]; do sleep 0.05; done
RAW_BASE="http://127.0.0.1:$(cat "$PORT_FILE")"
STUB_DIR="$WORK/stub-bin"
mkdir -p "$STUB_DIR"
printf '%s\n' '#!/bin/sh' 'printf "%s\n" "$*" >> "$FSGG_SDD_LOG"' > "$STUB_DIR/fsgg-sdd"
chmod +x "$STUB_DIR/fsgg-sdd"

expect_execution() {
  local desc="$1" template="$2" expected_params="$3"; shift 3
  local target="$WORK/real-$template"
  local log="$WORK/$template.log"
  local rc=0
  OUT="$(PATH="$STUB_DIR:$DOTNET_DIR" FSGG_TEMPLATES_RAW_BASE="$RAW_BASE" FSGG_SDD_LOG="$log" dotnet "$DLL" "$target" Product --pinned --no-governance --no-coordination "$@" 2>&1)" || rc=$?
  local params_ok=1
  grep -qF "$expected_params" "$log" || params_ok=0
  if [ "$template" = "fable-bindings" ]; then
    grep -qF "npmPackage=@babylonjs/core" "$log" && grep -qF "npmVersion=8.0.0" "$log" || params_ok=0
  fi
  if [ "$rc" -ne 0 ] || ! grep -qF "source: fixture/$template" "$target/.fsgg/providers.yml" || ! grep -qF "scaffold --root $target --provider $template" "$log" || [ "$params_ok" -ne 1 ]; then
    bad "$desc" "want successful real route, selected descriptor, provider '$template', and params '$expected_params'; got rc=$rc"$'\n'"--- output ---"$'\n'"$OUT"$'\n'"--- fsgg-sdd ---"$'\n'"$(cat "$log" 2>/dev/null || true)"
  else
    ok "$desc"
  fi
}

expect_execution "omitted --template keeps the rendering compatibility route" rendering "profile=game" --profile game
expect_execution "omitted lifecycle forwards the Standard SDD default" rendering "lifecycle=sdd"
expect_execution "explicit Standard SDD remains distinct" rendering "lifecycle=sdd" --lifecycle sdd
expect_execution "explicit Typed SDD is forwarded unchanged" rendering "lifecycle=typed-sdd" --lifecycle typed-sdd
expect_execution "explicit Freeform is forwarded unchanged" rendering "lifecycle=none" --lifecycle none
expect_execution "legacy Spec Kit remains selectable and frozen" rendering "lifecycle=spec-kit" --lifecycle spec-kit
expect_execution "console routes to its provider and descriptor" console "productName=Product" --template console
expect_execution "web routes to its provider and descriptor" web "productName=Product" --template web
expect_execution "fable-game routes to its provider and descriptor" fable-game "productName=Product" --template fable-game
expect_execution "fable-bindings forwards its scoped closure and target" fable-bindings "target=universal" --template fable-bindings --npm-package @babylonjs/core --npm-version 8.0.0 --binding-target universal

# ── retrofit subcommand: its own parser, then a clean no-network refusal (#1343) ──────────────────
# `retrofit <target> …` wires coordination onto an EXISTING workspace. Its parser is separate from the
# scaffold parser, carries the same #388 flag-as-value guard, and — critically — refuses cleanly (exit
# 2, no partial state, no network) when the target is not a scaffolded workspace (no .fsgg/). Both the
# parse-reject and the refusal exit 2, distinguished here by the message substring. All stay hermetic:
# a rejected parse never runs, and the refusal returns BEFORE any kit fetch (it checks .fsgg/ first).

# Parse-reject leg (exit 2, parser message):
expect_err "retrofit with no target is rejected" \
  "retrofit needs a target directory" -- retrofit
expect_err "retrofit with a leading flag (no target) is rejected" \
  "retrofit needs a target directory" -- retrofit --board acme/Roadmap
expect_err "retrofit --board swallowing the next flag as its value is caught (#388)" \
  "--board needs a value (got flag '--repo')" -- retrofit "$TGT" --board --repo acme/R
expect_err "retrofit --board with no following token needs a value" \
  "--board needs a value" -- retrofit "$TGT" --board
expect_err "retrofit --repo swallowing the next flag as its value is caught (#388)" \
  "--repo needs a value (got flag '--ref')" -- retrofit "$TGT" --repo --ref v1
expect_err "retrofit --chore-locks with no following token needs a value" \
  "--chore-locks needs a value" -- retrofit "$TGT" --chore-locks
expect_err "retrofit --ref with no following token needs a value" \
  "--ref needs a value" -- retrofit "$TGT" --ref
expect_err "retrofit rejects a scaffold-only flag as unknown" \
  "unknown argument: --profile" -- retrofit "$TGT" --profile game
# Refusal leg (parse ACCEPTED → runRetrofit → no .fsgg/ ⇒ clean refuse, exit 2, no network, no writes):
expect_err "retrofit refuses a non-scaffolded target (no .fsgg/) cleanly" \
  "not a scaffolded workspace (no .fsgg/ directory)" -- retrofit "$TGT"
expect_err "retrofit parses the full coord flag-set, then refuses the non-scaffolded target" \
  "not a scaffolded workspace (no .fsgg/ directory)" -- retrofit "$TGT" --board acme/Roadmap --repo acme/Product.X --chore-locks "acme/Product.X#5" --ref v1.2.3

# ── main's non-parse dispatch: -h / --help print usage and exit 0 ─────────────────────────────────

for h in --help -h; do
  rc=0; cli "$h" || rc=$?
  if [ "$rc" -eq 0 ] && printf '%s' "$OUT" | grep -qF "new-sdd-workspace"; then
    ok "$h prints usage and exits 0"
  else
    bad "$h should print usage and exit 0" "got rc=$rc"$'\n'"$OUT"
  fi
done

# ── Meta: the hermetic harness held — no valid parse leaked a real scaffold ───────────────────────
if [ ! -e "$TGT" ]; then
  ok "hermetic: no accepted parse created the target dir (all stopped at preflight)"
else
  bad "a valid parse scaffolded for real — the PATH scrub failed to hide fsgg-sdd" "found: $TGT"
fi

# The outer acceptance run also proves its own sensitivity. The child runs compile the real CLI
# after changing one production expression at a time; the guard prevents recursive mutation runs.
if [ -z "${FSGG_MUTATION_CHILD:-}" ]; then
  if bash "$HERE/mutation-controls.sh"; then
    ok "wrong-default and lifecycle-loss mutation controls go red and restore exactly"
  else
    bad "wrong-default and lifecycle-loss mutation controls go red and restore exactly"
  fi
fi

echo "new-sdd-workspace parse fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::new-sdd-workspace parse fixture FAILED"; exit 1; }
echo "new-sdd-workspace parse fixture — OK"
