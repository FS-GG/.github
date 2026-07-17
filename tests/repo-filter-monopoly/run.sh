#!/usr/bin/env bash
# Fixture for scripts/check-repo-filter-monopoly.py — `Scan.scope` is the engine's ONLY `--repo` filter (#979).
#
# The gate exists because a verb that filters `--repo` by hand is a SILENT FAIL-OPEN: a `--repo` naming
# no rostered repo resolves to itself, matches no row, and reports an empty queue with a GREEN EXIT —
# indistinguishable from a repo that genuinely has no items. The engine had five copies of that filter
# (snapshot, ready, who, inbox, lint), and the class had already produced four issues (#381, #446, #962,
# #979), each repaired by adding the missing verb rather than removing the list.
#
# THE FAILURE LEGS ARE THE POINT. A gate that cannot say NO is the #266 defect it was written to close.
# The legs below are the states this gate must tell apart, and two of them are the ones that make it
# worth having rather than a grep:
#
#   * the REF-TO-REF comparison. `Lanes.fs`'s `a.Ref.Repo = b.Ref.Repo` and `widen`'s
#     `String.Equals(r.Ref.Repo, ref.Repo, …)` are not `--repo` filters at all. A gate that flagged them
#     would be demanding the engine stop asking a question `--repo` has no part in — so a naive grep is
#     not merely noisy, it is WRONG.
#   * the NO_VERDICT leg. `Scan.scope` contains a subject by construction, so zero subjects means the
#     WALKER broke. Reporting green there is the exact defect the gate exists to close, inside the gate.
#
# The last leg runs the gate against THIS REPO's real source and requires green. Without it every leg
# above is synthetic, and the gate could pass its own tests while a shipped verb filters by hand.
set -u

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-repo-filter-monopoly.py"

fails=0
ok() { printf '  ok   — %s\n' "$1"; }
bad() { printf '  FAIL — %s\n     %s\n' "$1" "${2-}"; fails=$((fails + 1)); }

# A tree with just enough shape for the gate: a HOME carrying the funnel, plus whatever the leg adds.
mktree() {
  local d
  d="$(mktemp -d)"
  mkdir -p "$d/src/FS.GG.Coord.GitHub" "$d/src/FS.GG.Coord.Cli" "$d/src/FS.GG.Coord.Core"
  cat >"$d/src/FS.GG.Coord.GitHub/Scan.fs" <<'FS'
module Scan =
    let scope (repo: string option) (rows: Row list) : Scoped =
        match repo with
        | None -> { Rows = rows; Advisory = None }
        | Some name ->
            let kept =
                rows
                |> List.filter (fun r -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase))
            { Rows = kept; Advisory = None }
FS
  printf '%s' "$d"
}

run() { python3 "$GATE" --root "$1" >/dev/null 2>&1; printf '%s' "$?"; }

printf 'repo-filter-monopoly fixture\n'

# 1. THE HOME ALONE IS GREEN. The funnel's own filter is the one that is allowed to exist.
t="$(mktree)"
rc="$(run "$t")"
[ "$rc" = "0" ] && ok "the funnel alone is green (the home may filter)" \
  || bad "the home alone must be green" "exit $rc"
rm -rf "$t"

# 2. A HAND-ROLLED COPY IS RED. The bug, in the shape all five copies had.
t="$(mktree)"
cat >"$t/src/FS.GG.Coord.Cli/Client.fs" <<'FS'
    let ready (ctx: Context) (opts: Options) : int =
        let wanted =
            rows
            |> List.filter (fun r ->
                match opts.Repo with
                | Some name -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase)
                | None -> true)
        wanted
FS
rc="$(run "$t")"
[ "$rc" = "1" ] && ok "a hand-rolled copy in another verb is RED (#979's bug)" \
  || bad "a hand-rolled --repo filter must be a finding" "exit $rc"
rm -rf "$t"

# 3. THE REF-TO-REF `String.Equals` IS NOT A SUBJECT WHEN EXEMPT. `widen` asks "which other in-flight
#    items live in THIS ref's repo?" — a question `--repo` has no part in. It opts out explicitly.
t="$(mktree)"
cat >"$t/src/FS.GG.Coord.Cli/Client.fs" <<'FS'
                let inFlight =
                    rows
                    |> List.filter (fun r ->
                        r.Ref.Number <> ref.Number
                        // repo-filter-monopoly: exempt — REF-to-REF, not a `--repo` filter.
                        && String.Equals(r.Ref.Repo, ref.Repo, StringComparison.OrdinalIgnoreCase))
FS
rc="$(run "$t")"
[ "$rc" = "0" ] && ok "an EXPLICITLY exempt ref-to-ref comparison is green" \
  || bad "an exempt ref-to-ref comparison must not be a finding" "exit $rc"
rm -rf "$t"

# 4. ...AND WITHOUT THE EXEMPTION IT IS RED. The opt-out is explicit, never inferred: the gate must not
#    guess that `ref.Repo` means "this one is fine", or the marker buys nothing.
t="$(mktree)"
cat >"$t/src/FS.GG.Coord.Cli/Client.fs" <<'FS'
                let inFlight =
                    rows
                    |> List.filter (fun r ->
                        String.Equals(r.Ref.Repo, ref.Repo, StringComparison.OrdinalIgnoreCase))
FS
rc="$(run "$t")"
[ "$rc" = "1" ] && ok "the same line WITHOUT the marker is red — the opt-out is explicit, not inferred" \
  || bad "an unexempted String.Equals on .Ref.Repo must be a finding" "exit $rc"
rm -rf "$t"

# 5. A BARE REF-TO-REF `=` NEEDS NO EXEMPTION. `Lanes.fs`'s `a.Ref.Repo = b.Ref.Repo` is excluded
#    STRUCTURALLY — an RHS naming `.Repo` is comparing two refs by construction. A gate that made real
#    code carry markers for a question it never asked would be taxing the innocent.
t="$(mktree)"
cat >"$t/src/FS.GG.Coord.Core/Lanes.fs" <<'FS'
                if a.Ref.Owner = b.Ref.Owner && a.Ref.Repo = b.Ref.Repo then
                    merge a b
FS
rc="$(run "$t")"
[ "$rc" = "0" ] && ok "a bare ref-to-ref '=' is excluded structurally, with no marker needed" \
  || bad "a ref-to-ref '=' must not be a finding" "exit $rc"
rm -rf "$t"

# 6. A BARE `=` AGAINST A NON-REF IS RED. The case-sensitive spelling of the same bug.
t="$(mktree)"
cat >"$t/src/FS.GG.Coord.Cli/Client.fs" <<'FS'
                let items = rows |> List.filter (fun r -> r.Ref.Repo = name)
FS
rc="$(run "$t")"
[ "$rc" = "1" ] && ok "a bare '=' filter against a plain name is red (the case-sensitive spelling)" \
  || bad "a bare '=' --repo filter must be a finding" "exit $rc"
rm -rf "$t"

# 7. NO VERDICT, AND IT IS NOT GREEN. A walker that finds nothing has BROKEN — `Scan.scope` carries a
#    subject by construction. Green here would be the gate committing the defect it exists to catch.
t="$(mktemp -d)"; mkdir -p "$t/src"
rc="$(run "$t")"
[ "$rc" = "3" ] && ok "zero subjects is NO_VERDICT (3), never a clean green (#266)" \
  || bad "an empty tree must be NO_VERDICT, not green" "exit $rc"
rm -rf "$t"

# 8. BUILD OUTPUT IS NOT SOURCE. A stale obj/ copy of a since-fixed file must not red the tree.
t="$(mktree)"
mkdir -p "$t/src/FS.GG.Coord.Cli/obj/Release"
cat >"$t/src/FS.GG.Coord.Cli/obj/Release/Client.fs" <<'FS'
                | Some name -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase)
FS
rc="$(run "$t")"
[ "$rc" = "0" ] && ok "obj/ build output is not walked" \
  || bad "a stale obj/ copy must not be a finding" "exit $rc"
rm -rf "$t"

# 9. THE SHIPPED TREE. Without this every leg above is synthetic, and the gate could pass its own
#    fixture while a real verb filters `--repo` by hand.
rc="$(run "$ROOT")"
[ "$rc" = "0" ] && ok "the REAL source tree is green" \
  || bad "this repo's src/ must pass its own gate" "exit $rc — run: python3 scripts/check-repo-filter-monopoly.py --root ."

printf '\n'
if [ "$fails" -ne 0 ]; then
  printf 'repo-filter-monopoly fixture: %d leg(s) FAILED\n' "$fails"
  exit 1
fi
printf 'repo-filter-monopoly fixture: all legs passed\n'
