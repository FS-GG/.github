#!/usr/bin/env bash
# Fixture for scripts/check-preset-repo-scope-coherence.py — the preset's repo scoping and
# registry/repos.yml's `receives:` rows are ONE fact, and this proves the gate can say NO (.github#1552).
#
# THE FAILURE LEGS ARE THE POINT. The defect this gate closes is invisible by construction: Renovate
# detects the package file, applies the rule, proposes nothing, and reports success. A bump that is
# never proposed is not an error — it shows up nowhere. FS.GG.Net's and FS.GG.Audio's ENTIRE NuGet
# surface was dark for as long as they extended the preset, and FS.GG.Audio sat two minors behind on
# FS.GG.Kit with no PR ever opened. So a gate that merely passes on the fixed tree proves nothing at
# all: it is what was there before (nothing) with a green tick on top.
#
# LEG 3 IS THE REGRESSION LEG AND THE MOST IMPORTANT ONE. It runs the gate against the ACTUAL
# pre-#1552 preset — reconstructed here as the fused three-file rule with `!FS-GG/.github` — and
# requires a RED. A gate written after a fix and never shown the defect is a fixture that passed
# over a live bug.
#
# LEG 8 IS THE OTHER HALF, AND IT IS THE ONE A TIDY FIX GETS WRONG. Scoping the fused rule to the
# four `build-config` receivers is the obvious repair and it is WRONG: `.config/dotnet-tools.json`
# is a coordination-KIT file (registry/repos.yml `kit:`, kind: config, #1077) held by all SEVEN
# receivers and byte-checked by coordination-sync --check. Re-enabling it in templates/audio/net
# manufactures the #794 churn class in three more repos, under a commit message that says it is
# fixing churn. The gate must red on that too, and for the opposite reason.
#
# THE VACUOUS-PASS FAMILY (legs 10-15) are states in which "every rule agrees with the roster" is
# TRUE and the preset is broken: no rules, no roster, an empty derivation, an undefined capability.
# Each must be a NO VERDICT (3), never green (#266 — "could not look" is never "looked, and fine").
set -u

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-preset-repo-scope-coherence.py"
PY="${PYTHON:-python3}"

fails=0
ok() { printf '  ok   — %s\n' "$1"; }
bad() { printf '  FAIL — %s\n     %s\n' "$1" "${2-}"; fails=$((fails + 1)); }

# A tree with exactly the shape the gate reads: a preset, and a roster.
mktree() {
  local d
  d="$(mktemp -d)"
  mkdir -p "$d/registry"
  cat >"$d/registry/repos.yml" <<'YML'
schemaVersion: 8
authority: FS-GG/.github
repos:
  - { id: .github,    full: FS-GG/.github,         role: authority, receives: [labels] }
  - { id: sdd,        full: FS-GG/FS.GG.SDD,       role: framework, receives: [labels, coordination-kit, build-config] }
  - { id: rendering,  full: FS-GG/FS.GG.Rendering, role: framework, receives: [labels, coordination-kit, build-config] }
  - { id: audio,      full: FS-GG/FS.GG.Audio,     role: framework, receives: [labels, coordination-kit] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
  - { id: build-config,     materializer: build-config }
  - { id: labels,           push: true }
kit:
  - { id: pnext-item,            kind: skill,  source: .claude/skills/pnext-item }
  - { id: coord-engine-manifest, kind: config, source: dist/dotnet/.config/dotnet-tools.json, dest: .config/dotnet-tools.json }
YML
  cat >"$d/default.json" <<'JSON'
{
  "packageRules": [
    {
      "description": ["the two materialized props", "fsgg-repo-scope: receives=build-config"],
      "matchFileNames": ["Directory.Build.props", "Directory.Packages.props"],
      "matchRepositories": ["FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    },
    {
      "description": ["the engine tool manifest", "fsgg-repo-scope: receives=coordination-kit"],
      "matchFileNames": [".config/dotnet-tools.json"],
      "matchRepositories": ["FS-GG/FS.GG.Audio", "FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]
}
JSON
  printf '%s' "$d"
}

# Rewrite the preset's packageRules with the JSON literal given.
rules() { printf '{\n  "packageRules": %s\n}\n' "$2" >"$1/default.json"; }

run_gate() { "$PY" "$GATE" --root "$1" >/dev/null 2>&1; printf '%s' "$?"; }
gate_says() { "$PY" "$GATE" --root "$1" 2>&1; }

expect() {
  local tree="$1" want="$2" what="$3" rc
  rc="$(run_gate "$tree")"
  if [ "$rc" = "$want" ]; then ok "$what"; else bad "$what" "expected exit $want, got $rc"; fi
}

printf 'preset-repo-scope-coherence fixture\n'

# --------------------------------------------------------------------------------------------
# 1. THE SHIPPED TREE IS GREEN — checked FIRST, so a gate that only ever passes on synthetic trees
#    while the real preset has drifted is not a state this suite can reach.
rc="$(run_gate "$ROOT")"
[ "$rc" = "0" ] && ok "the SHIPPED tree is green" || bad "the shipped tree must be green" "exit $rc"

# The shipped tree must actually HAVE a subject, and the summary must say how many receivers each
# rule scopes to — a green over zero rules is the vacuous pass this gate exists to refuse.
out="$(gate_says "$ROOT")"
case "$out" in
  *"0 file-scoped disable rule"*) bad "the shipped tree must have subjects" "$out" ;;
  *"file-scoped disable rule"*"receivers"*) ok "the shipped tree reports its subject count: ${out#*OK — }" ;;
  *) bad "the shipped tree's summary must name a subject count" "$out" ;;
esac

# The shipped preset must scope the build-config fabric. If a future edit fuses it with anything else
# this leg reds, which is the intended outcome: the split is the finding, not an implementation detail.
case "$out" in
  *"build-config"*) ok "the shipped preset scopes a rule to build-config" ;;
  *) bad "the shipped preset must scope a rule to build-config" "$out" ;;
esac
# THERE IS NO LONGER A SECOND, coordination-kit FABRIC HERE, AND THAT IS THE POINT (#1798). This leg
# used to demand one unconditionally. The demand was correct while the kit delivered
# `.config/dotnet-tools.json`; ADR-0068 (#1615) ended that, and an unconditional demand would now
# require the very rule that switches the engine pin's only delivery path off in all seven receivers.
# Whether a coordination-kit rule is owed is a fact about the ROSTER, so it is asserted where the
# roster is read — leg 9 below, in both directions.
ok "no unconditional coordination-kit demand; leg 9 derives it from the roster instead"

# 2. A MINIMAL COHERENT TREE IS GREEN.
t="$(mktree)"
expect "$t" 0 "a coherent preset + roster is green"
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# 3. THE REGRESSION LEG: THE ACTUAL PRE-#1552 RULE. One fused rule over all three files, scoped by
#    the negation `!FS-GG/.github`. This is the shape that zeroed FS.GG.Net's and FS.GG.Audio's
#    whole NuGet surface. It MUST be red — a gate that greens this has proved nothing.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["all THREE files, one rule, one repo set"],
      "matchFileNames": [".config/dotnet-tools.json", "Directory.Build.props", "Directory.Packages.props"],
      "matchRepositories": ["!FS-GG/.github"],
      "enabled": false
    }
  ]'
expect "$t" 1 "the PRE-#1552 fused rule (\`!FS-GG/.github\`) is RED"
out="$(gate_says "$t")"
case "$out" in
  *"declares NO fabric"*) ok "the finding says the rule declared no fabric" ;;
  *) bad "the finding must say the rule declared no fabric" "$out" ;;
esac
rm -rf "$t"

# 4. THE SAME FUSED RULE, ANNOTATED. Declaring a fabric is not enough — `!FS-GG/.github` is still
#    not a derived receiver list, and the gate must say so with the repos NAMED.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": ["Directory.Build.props", "Directory.Packages.props"],
      "matchRepositories": ["!FS-GG/.github"],
      "enabled": false
    }
  ]'
expect "$t" 1 "an annotated rule still scoped by the NEGATION is RED"
out="$(gate_says "$t")"
case "$out" in
  *MISSING*"FS-GG/FS.GG.SDD"*) ok "the finding NAMES the receivers that lost their suppression" ;;
  *) bad "the finding must name the missing receivers" "$out" ;;
esac
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# 5. A RECEIVER IS DROPPED from the preset list. Its materialized file becomes proposable again and
#    Renovate re-files a guaranteed-red PR every cycle — #794, measured at a 2m34s re-file interval.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": ["Directory.Build.props"],
      "matchRepositories": ["FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "a build-config receiver MISSING from the preset list is RED"
rm -rf "$t"

# 6. THE ROSTER GROWS AND THE PRESET DOES NOT — the #1552 drift direction, replayed. A repo is
#    onboarded as a build-config receiver; nobody edits the preset. Must be red.
t="$(mktree)"
sed -i 's#^capabilities:#  - { id: game, full: FS-GG/FS.GG.Game, role: framework, receives: [labels, coordination-kit, build-config] }\ncapabilities:#' "$t/registry/repos.yml"
expect "$t" 1 "a NEWLY-ROSTERED build-config receiver absent from the preset is RED"
rm -rf "$t"

# 7. A NON-RECEIVER IS LISTED — the #1552 defect itself, in its narrow form: a repo that AUTHORS
#    the file has it disabled. The finding must say the repo authors it.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": ["Directory.Packages.props"],
      "matchRepositories": ["FS-GG/FS.GG.Audio", "FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "a NON-receiver listed in the preset is RED (it authors the file)"
out="$(gate_says "$t")"
case "$out" in
  *EXTRA*"FS-GG/FS.GG.Audio"*) ok "the finding NAMES the repo whose own pins were zeroed" ;;
  *) bad "the finding must name the wrongly-listed repo" "$out" ;;
esac
rm -rf "$t"

# 8. THE PLAUSIBLE WRONG FIX. `.config/dotnet-tools.json` scoped to the BUILD-CONFIG receivers —
#    the repair that "just" narrows the fused rule to the four. It re-enables a kit-materialized
#    file in every kit-only receiver, which is #794's churn newly manufactured in three repos.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": [".config/dotnet-tools.json"],
      "matchRepositories": ["FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
#    Rules 1-3 alone would GREEN this: the repo list really is the build-config receivers, so its
#    own derivation checks out. Rule 4 is what refuses it, by asking the roster which fabric
#    delivers that file instead of believing the annotation.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": [".config/dotnet-tools.json"],
      "matchRepositories": ["FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "a KIT-DELIVERED file scoped to build-config is RED (the plausible wrong fix)"
out="$(gate_says "$t")"
case "$out" in
  *"kit:\` block DELIVERS"*"FS-GG/FS.GG.Audio"*)
    ok "the finding names the kit fabric AND the receivers the wrong fix would re-enable" ;;
  *) bad "the finding must name the kit fabric and the affected receivers" "$out" ;;
esac
rm -rf "$t"

# 8b. THE SAME TRAP IN ITS FUSED FORM: props and the manifest in ONE rule, correctly scoped to the
#     build-config four. This is precisely "narrow the old rule and ship it", and rule 4 reds it
#     because the rule still covers a kit-delivered file.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": [".config/dotnet-tools.json", "Directory.Build.props", "Directory.Packages.props"],
      "matchRepositories": ["FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "the FUSED rule narrowed to the build-config four is RED (it still covers a kit file)"
rm -rf "$t"

# 8c. AND THE CONVERSE MUST STILL PASS: the same file, correctly scoped to coordination-kit.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=coordination-kit"],
      "matchFileNames": [".config/dotnet-tools.json"],
      "matchRepositories": ["FS-GG/FS.GG.Audio", "FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 0 "the kit-delivered file scoped to coordination-kit is GREEN"
rm -rf "$t"

# 8d. RULE 4 IS DERIVED, NOT HARDCODED. Rename the kit's `dest` in the roster and the SAME preset
#     that just passed must now red — proving the file list comes from the registry, not from a
#     constant in the gate.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": ["Directory.Build.props"],
      "matchRepositories": ["FS-GG/FS.GG.Rendering", "FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 0 "a non-kit file scoped to build-config is green"
sed -i 's#dest: .config/dotnet-tools.json#dest: Directory.Build.props#' "$t/registry/repos.yml"
expect "$t" 1 "…and reds unchanged once the ROSTER says the kit delivers that file"
rm -rf "$t"

# 8e. A `kind: config` row with no `dest` must be a NO VERDICT, not a silently smaller file set —
#     an empty rule-4 subject switches the check off exactly where it is needed.
t="$(mktree)"
sed -i 's#, dest: .config/dotnet-tools.json##' "$t/registry/repos.yml"
expect "$t" 3 "a kit config row with no \`dest\` is NO VERDICT (rule 4 must not switch itself off)"
rm -rf "$t"

# 9. WHAT ACTUALLY CATCHES LEG 8, ON THE REAL TREE — AND IT IS AN `IF AND ONLY IF` (#1798).
#
#    THIS LEG USED TO ASSERT THE MANIFEST RULE EXISTS, and that was right for as long as the kit
#    delivered the manifest. It asserted the SEVEN-receiver scoping precisely so the tidy #1552 fix —
#    narrowing to the build-config four — could not re-enable a byte-checked kit file in templates,
#    audio and net. That defect is real and this leg still refuses it.
#
#    But written as "the rule must exist" it was HALF a check, and the missing half is what #1798 is.
#    #1615/ADR-0068 took `.config/dotnet-tools.json` off the `kit:` block. The rule's entire
#    justification — "a receiver's copy is materialized, so a bump is a guaranteed-red PR" — went with
#    it, and the rule stayed. Nothing reddened: not this fixture (it wanted the rule THERE), not
#    check-preset-repo-scope-coherence (its rule 4 fires only when a file the kit DOES deliver
#    declares the wrong fabric — a file the kit no longer delivers is inert to it), not
#    renovate-config-validator (the config is valid). ADR-0068's only delivery path for the engine pin
#    was switched off in all seven receivers, and the way anyone found out was a human asking why
#    Renovate was quiet in FS.GG.Net.
#
#    So the assertion is now BICONDITIONAL and DERIVED FROM THE ROSTER, in both directions:
#      * the kit delivers the manifest  => the preset MUST disable it, scoped to coordination-kit;
#      * the kit does not deliver it    => the preset MUST NOT disable it, because the only thing
#                                          such a rule can still do is stop the pin ever moving.
#    Neither direction is a judgement call a future editor has to remember, and re-adding the kit row
#    flips this leg's demand automatically rather than leaving a rule nothing re-checks.
"$PY" - "$ROOT/default.json" "$ROOT/registry/repos.yml" <<'PY'
import json, re, sys

doc = json.load(open(sys.argv[1], encoding="utf-8"))
roster = open(sys.argv[2], encoding="utf-8").read()

MANIFEST_DEST = ".config/dotnet-tools.json"

# Does the kit DELIVER it? Read the roster's `kind: config` rows the same narrow way
# check-pin-coherence.py does — a regex over the owning file, not a new YAML dependency.
delivered = False
for row in re.finditer(r"^\s*-\s*\{[^}]*\bkind:\s*config\b[^}]*\}", roster, re.MULTILINE):
    d = re.search(r"\bdest:\s*([^,}\s]+)", row.group(0))
    if d and d.group(1) == MANIFEST_DEST:
        delivered = True

rules = [r for r in doc["packageRules"]
         if r.get("enabled") is False and MANIFEST_DEST in (r.get("matchFileNames") or [])]

if delivered:
    if len(rules) != 1:
        print(f"registry/repos.yml's kit: block DELIVERS {MANIFEST_DEST}, so every receiver holds a "
              f"materialized copy and a Renovate bump there is a guaranteed-red PR (#794). The preset "
              f"must carry exactly one rule disabling it; found {len(rules)}.")
        sys.exit(1)
    rule = rules[0]
    if rule["matchFileNames"] != [MANIFEST_DEST]:
        print(f"the manifest must be scoped ALONE, not fused with the props: {rule['matchFileNames']}")
        sys.exit(1)
    if not any(p.strip() == "fsgg-repo-scope: receives=coordination-kit" for p in rule["description"]):
        print("the manifest rule must declare receives=coordination-kit, not build-config")
        sys.exit(1)
    for repo in ("FS-GG/FS.GG.Audio", "FS-GG/FS.GG.Net", "FS-GG/FS.GG.Templates"):
        if repo not in rule["matchRepositories"]:
            print(f"{repo} holds a byte-checked {MANIFEST_DEST} and must stay suppressed")
            sys.exit(1)
    print(f"kit DELIVERS {MANIFEST_DEST}; the preset disables it, coordination-kit-scoped")
else:
    if rules:
        print(f"registry/repos.yml's kit: block does NOT deliver {MANIFEST_DEST} (ADR-0068 took it off "
              f"in #1615), so every receiver AUTHORS its own copy and nothing compares it to anything. "
              f"A disable here cannot be suppressing churn — there is none left to suppress. What it "
              f"does is stop Renovate ever proposing the fs.gg.coord.cli bump, which ADR-0068 made the "
              f"pin's ONLY delivery path to the fleet. Found {len(rules)} such rule(s): "
              f"{[r.get('matchRepositories') for r in rules]}. Delete it, or put the manifest back on "
              f"the kit and mean it (.github#1798).")
        sys.exit(1)
    print(f"kit does NOT deliver {MANIFEST_DEST}; the preset correctly leaves it managed")
PY
if [ "$?" -eq 0 ]; then
  ok "the preset disables the manifest IF AND ONLY IF the roster's kit: block delivers it (#1798)"
else
  bad "preset/roster disagree about who owns .config/dotnet-tools.json" "see above"
fi

# --------------------------------------------------------------------------------------------
# THE VACUOUS-PASS FAMILY. Each is a tree where "every rule agrees with the roster" is TRUE and the
# preset is broken. NO VERDICT (3), never green.

# 10. NO FILE-SCOPED RULE AT ALL — the gate is reading the wrong file, or the rules were deleted.
t="$(mktree)"
rules "$t" '[{"description": ["unrelated"], "matchPackageNames": ["Expecto"], "allowedVersions": "<1.0.0"}]'
expect "$t" 3 "a preset with NO file-scoped disable rule is NO VERDICT"
rm -rf "$t"

# 11. AN UNDEFINED CAPABILITY. A word with no `capabilities:` row derives a set nothing vouches for.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=not-a-capability"],
      "matchFileNames": ["Directory.Build.props"],
      "matchRepositories": ["FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "a capability with no registry row is RED"
rm -rf "$t"

# 12. A CAPABILITY NO REPO RECEIVES. The derivation is EMPTY, and an empty matchRepositories matches
#     nothing — silently re-enabling the file everywhere. Never green.
t="$(mktree)"
sed -i 's/, build-config\]/]/g' "$t/registry/repos.yml"
expect "$t" 3 "a capability with ZERO rostered receivers is NO VERDICT (empty list matches nothing)"
rm -rf "$t"

# 13. THE ROSTER IS UNREADABLE. A failed read is not a coherent tree.
t="$(mktree)"
rm -f "$t/registry/repos.yml"
expect "$t" 3 "a MISSING roster is NO VERDICT"
rm -rf "$t"

t="$(mktree)"
printf 'repos: [oh: [dear\n' >"$t/registry/repos.yml"
expect "$t" 3 "an UNPARSABLE roster is NO VERDICT"
rm -rf "$t"

# 14. THE PRESET IS UNREADABLE / NOT JSON.
t="$(mktree)"
printf '{ "packageRules": [ \n' >"$t/default.json"
expect "$t" 3 "an UNPARSABLE preset is NO VERDICT"
rm -rf "$t"

t="$(mktree)"
rm -f "$t/default.json"
expect "$t" 3 "a MISSING preset is NO VERDICT"
rm -rf "$t"

# 15. TWO ANNOTATIONS ON ONE RULE. Two derivations cannot both be its repo set.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config", "fsgg-repo-scope: receives=coordination-kit"],
      "matchFileNames": ["Directory.Build.props"],
      "matchRepositories": ["FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "TWO fabric annotations on one rule is RED"
rm -rf "$t"

# 16. A MENTION IS NOT A DECLARATION (#683). A paragraph that talks ABOUT the convention must not be
#     read as using it — the annotation is the whole paragraph, exactly like fsgg-cap-expires-when.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["a rule like this would carry fsgg-repo-scope: receives=build-config one day"],
      "matchFileNames": ["Directory.Build.props"],
      "matchRepositories": ["FS-GG/FS.GG.SDD"],
      "enabled": false
    }
  ]'
expect "$t" 1 "a MENTION of the annotation inside prose does not declare a fabric"
rm -rf "$t"

# 17. ORDER IS PART OF THE CONTRACT — the derived list is sorted, so a diff is a real diff.
t="$(mktree)"
rules "$t" '[
    {
      "description": ["fsgg-repo-scope: receives=build-config"],
      "matchFileNames": ["Directory.Build.props"],
      "matchRepositories": ["FS-GG/FS.GG.SDD", "FS-GG/FS.GG.Rendering"],
      "enabled": false
    }
  ]'
expect "$t" 1 "a correctly-membered but UNSORTED list is RED"
out="$(gate_says "$t")"
case "$out" in
  *"wrong ORDER"*) ok "the finding distinguishes wrong order from wrong membership" ;;
  *) bad "the finding must say the order is wrong" "$out" ;;
esac
rm -rf "$t"

# --------------------------------------------------------------------------------------------
# 18. THE VALIDATOR'S BLIND SPOT, ASSERTED RATHER THAN ASSUMED. Every wrong answer above is a
#     configuration `renovate-config-validator` calls VALID — which is the whole reason this gate
#     exists alongside the schema run. Skipped unless the workflow exported the pinned binary.
VALIDATOR="${RENOVATE_CONFIG_VALIDATOR:-}"
if [ -z "$VALIDATOR" ] || [ ! -x "$VALIDATOR" ]; then
  printf '  skip — validator legs (RENOVATE_CONFIG_VALIDATOR unset; the workflow sets it)\n'
else
  v="$(mktemp -d)"

  # 18a. NEGATIVE CONTROL FIRST. A binary that reds nothing reads exactly like a clean preset, so
  #      its green is never taken on trust.
  "$PY" - "$ROOT/default.json" "$v/invalid.json" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1], encoding="utf-8"))
doc["packageRules"][0]["thisIsNotARenovateField"] = True
json.dump(doc, open(sys.argv[2], "w", encoding="utf-8"), indent=2)
PY
  if "$VALIDATOR" --no-global "$v/invalid.json" >/dev/null 2>&1; then
    bad "negative control FAILED" "the validator greened an invented packageRule field — its verdicts are worthless here"
  else
    ok "negative control: the validator REDS an invented packageRule field"
  fi

  # 18b. …WHICH IS ALSO WHY THE ANNOTATION LIVES IN \`description\`. 18a is the measurement behind
  #      that choice: a sibling field would be rejected outright, so the fabric declaration has to
  #      ride a field Renovate already accepts.
  ok "recorded: an invented sibling field is rejected, so fsgg-repo-scope rides \`description\`"

  # 18c. THE BLIND SPOT. The pre-#1552 repo scoping validates perfectly clean while zeroing two
  #      repos' entire NuGet surface. If a future renovate ever DID catch this, this leg reds and
  #      the docstring gets rewritten — the right outcome either way.
  "$PY" - "$ROOT/default.json" "$v/wrongscope.json" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1], encoding="utf-8"))
for rule in doc["packageRules"]:
    if rule.get("enabled") is False and "matchFileNames" in rule:
        rule["matchRepositories"] = ["!FS-GG/.github"]
json.dump(doc, open(sys.argv[2], "w", encoding="utf-8"), indent=2)
PY
  if "$VALIDATOR" --no-global "$v/wrongscope.json" >/dev/null 2>&1; then
    ok "recorded: the validator is BLIND to the wrong repo scope (so this gate is required)"
  else
    bad "the validator's blind spot has changed" "it now reds the wrong scope — update the docstring"
  fi

  rm -rf "$v"
fi

printf '\n'
if [ "$fails" -eq 0 ]; then
  printf 'preset-repo-scope-coherence fixture: OK\n'
  exit 0
fi
printf 'preset-repo-scope-coherence fixture: %d FAILURE(S)\n' "$fails"
exit 1
