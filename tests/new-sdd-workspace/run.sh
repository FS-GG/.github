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

# Newest match wins, so a leftover older-TFM output dir can't feed a stale dll to the run below.
DLL="$(find "$REPO_ROOT/scripts/NewSddWorkspace/bin/Release" -name new-sdd-workspace.dll 2>/dev/null | sort | tail -1)"
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
if [ "$secure_rc" -eq 0 ] && printf '%s' "$secure_out" | grep -q 'verified:' && ! grep -q 'repository-issue-policy' "$SECURE/.fsgg/scaffold-provenance.json" && grep -q 'project-access' "$SECURE/.fsgg/scaffold-provenance.json"; then
  ok "secure resume clears only its verified repository obligation"
else
  bad "secure resume must converge matching provenance" "rc=$secure_rc: $secure_out"
fi

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

echo "new-sdd-workspace parse fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::new-sdd-workspace parse fixture FAILED"; exit 1; }
echo "new-sdd-workspace parse fixture — OK"
