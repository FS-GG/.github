#!/usr/bin/env bash
# Fixture for scripts/check-skillmirror-freshness.py — .github#1546, epic #266.
#
# THE FAILURE LEGS ARE THE POINT. This gate's whole reason to exist is that NOTHING noticed when
# `Fsgg.SkillMirror`'s source moved out from under the conformance table, so a suite that only ever
# watched it pass would be what was there before (nothing) with a green tick on top. Every leg below
# asserts the EXIT CODE and the REASON, because tests/feed-coherence/run.sh:10 names the trap: a
# "must fail" leg whose non-zero exit comes from a path guard rather than from the thing under test
# would pass against a gate broken in a completely different way.
#
# OFFLINE, AND THE VERDICT IS COMPUTED WITHOUT THE GATE'S OWN LOGIC (#1546 criterion 4).
#   * The expected digest is produced by `sha256sum`, never by importing anything from the gate. A
#     fixture that asked the gate what the answer should be and then asked whether it agreed would
#     be measuring nothing.
#   * `gh` is STUBBED ON PATH and FAILS LIKE THE REAL ONE — a 404 is an answer, a rate limit is not
#     — so the fail-closed legs exercise the real transport rather than a convenience shim. The
#     shapes it reproduces were MEASURED against the live API while this gate was written:
#         gh api repos/FS-GG/FS.GG.SDD/contents/<gone>   -> `gh: Not Found (HTTP 404)` on stderr
#         gh api repos/FS-GG/FS.GG.NoSuchRepo9f3a        -> the same string
#     which is exactly why the gate may not read one 404 without the other.
#   * The stub ASSERTS THE REQUEST rather than answering anything asked of it. It refuses a repo or
#     path it was not told to serve, refuses a read carrying `?ref=`, and refuses a read without the
#     raw media type. So a gate that hardcoded the subject, or pinned the read to the recorded
#     commit, fails these legs instead of quietly passing them.
#
# THE `?ref=` LEG IS THE VACUOUS-PASS GUARD. Reading the file AT `derivedFrom.commit` would compare
# the recorded digest to the bytes it was computed from — green forever, in every world, including
# the one this gate was filed about. Leg 4 is what makes that unimplementable rather than merely
# discouraged.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-skillmirror-freshness.py"
TABLE="$ROOT/tests/skill-union/skillmirror.fixtures.json"
PY="${PYTHON:-python3}"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skillmirror-freshness-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0
failcount=0
ok() { printf 'PASS  %s\n' "$1"; pass=$((pass + 1)); }
bad() {
  printf 'FAIL  %s\n' "$1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'
  failcount=$((failcount + 1))
}

# The subject the synthetic table names. Deliberately NOT the real repository or the real path: if
# the gate spelled either of them itself instead of reading `derivedFrom`, the stub would refuse the
# request and every green leg here would go red.
REPO="Fixture-Org/Fixture.Lib"
LIBPATH="src/Fixture.Contracts/SkillMirror.fs"
COMMIT="0123456789abcdef0123456789abcdef01234567"
REDRIVE="bash tests/skill-union/skillmirror-oracle.sh --lib <checkout>/src/FS.GG.Contracts"

# ---------------------------------------------------------------------------------------------
# The `gh` stub. Serves a WORLD directory:
#   $WORLD/blob              the bytes the contents endpoint returns
#   $WORLD/repo              the `owner/name` it will answer for; anything else is refused
#   $WORLD/path              the contents path it will answer for; anything else is refused
#   $WORLD/path-missing      the contents endpoint 404s (the file moved)
#   $WORLD/repo-invisible    the repository endpoint 404s too (we cannot see the repo at all)
#   $WORLD/ratelimit         every call fails 403 rate-limited — an answer about nothing
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"
mkdir -p "$STUB"
cat >"$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail

notfound() { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
apifail()  { echo "gh: API rate limit exceeded for installation (HTTP 403)" >&2; exit 1; }
refuse()   { echo "stub: $1" >&2; exit 9; }

[ "${1:-}" = "api" ] || refuse "expected \`gh api\`, got: $*"
shift

raw=no
path=""
while [ $# -gt 0 ]; do
  case "$1" in
    -H) case "$2" in *vnd.github.raw*) raw=yes ;; esac; shift 2 ;;
    --jq) shift 2 ;;
    repos/*) path="$1"; shift ;;
    *) shift ;;
  esac
done
[ -n "$path" ] || refuse "no repos/... path in the call"

[ -e "$WORLD/ratelimit" ] && apifail

want_repo="$(cat "$WORLD/repo")"
want_path="$(cat "$WORLD/path")"

case "$path" in
  *\?*) refuse "the read carries a query string ($path). This gate must read the DEFAULT BRANCH; pinning it to derivedFrom.commit compares the recorded digest to itself and is green forever." ;;
esac

case "$path" in
  "repos/$want_repo/contents/$want_path")
    # The contents read. Raw media type or it is not the file's bytes.
    [ "$raw" = yes ] || refuse "the contents read did not ask for application/vnd.github.raw, so its body is the JSON envelope and its sha256 is not the file's"
    [ -e "$WORLD/path-missing" ] && notfound
    cat "$WORLD/blob"
    ;;
  "repos/$want_repo")
    # The visibility probe.
    [ -e "$WORLD/repo-invisible" ] && notfound
    echo "$want_repo"
    ;;
  *)
    refuse "asked for '$path', which is neither repos/$want_repo/contents/$want_path nor repos/$want_repo — the gate is not reading the subject the table declares"
    ;;
esac
STUB
chmod +x "$STUB/gh"

# world <dir> [flag…] — a fresh world serving REPO/LIBPATH, with the given marker files touched.
world() {
  local d="$1"
  shift
  mkdir -p "$d"
  printf '%s' "$REPO" >"$d/repo"
  printf '%s' "$LIBPATH" >"$d/path"
  printf 'module Fixture.SkillMirror\nlet verify () = ()\n' >"$d/blob"
  local flag
  for flag in "$@"; do : >"$d/$flag"; done
  printf '%s' "$d"
}

# digest_of <file> — computed by sha256sum, NOT by the gate. This is criterion 4's "without the
# checker's own logic": the expectation and the thing being checked share no code at all.
digest_of() { sha256sum "$1" | cut -d' ' -f1; }

# table <file> <digest> — a synthetic conformance table carrying just the block the gate reads.
table() {
  "$PY" - "$1" "$2" "$REPO" "$LIBPATH" "$COMMIT" "$REDRIVE" <<'PY'
import json, sys
out, digest, repo, path, commit, redrive = sys.argv[1:7]
json.dump(
    {
        "derivedFrom": {
            "library": "Fixture.SkillMirror.verify",
            "repo": repo,
            "path": path,
            "commit": commit,
            "skillMirrorFsSha256": digest,
            "libraryFiles": {path: digest},
            "howToRedrive": redrive,
        },
        "fixtures": [],
    },
    open(out, "w", encoding="utf-8"),
    indent=2,
)
PY
}

# run <world> <table> — the gate, with the stub first on PATH and the retry loop wound right down.
run_gate() {
  PATH="$STUB:$PATH" WORLD="$1" \
    FSGG_SKILLMIRROR_TRIES=1 FSGG_SKILLMIRROR_RETRY_DELAY=0 FSGG_SKILLMIRROR_TIMEOUT=20 \
    "$PY" "$GATE" --root "$ROOT" --fixtures "$2" 2>&1
}

# expect <name> <want-rc> <needle> <world> <table> — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3" w="$4" t="$5" out rc=0
  out="$(run_gate "$w" "$t")" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF -- "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

printf 'skillmirror-freshness fixture\n'

# ---------------------------------------------------------------------------------------------
# 0. THE STUB ITSELF IS NOT VACUOUS. Every leg below is an argument about what the gate did with
#    the stub's answer, and none of it means anything if the stub answers the same way regardless.
#    Checked here, in shell, before the gate is invoked once.
w="$(world "$WORK/w0")"
served="$(PATH="$STUB:$PATH" WORLD="$w" gh api -H "Accept: application/vnd.github.raw" \
  "repos/$REPO/contents/$LIBPATH" | sha256sum | cut -d' ' -f1)"
if [ "$served" = "$(digest_of "$w/blob")" ]; then
  ok "the stub serves the blob's exact bytes (sha256sum agrees end to end)"
else
  bad "the stub must serve the blob byte-for-byte" "served=$served want=$(digest_of "$w/blob")"
fi
if PATH="$STUB:$PATH" WORLD="$w" gh api "repos/Someone-Else/Other" >/dev/null 2>&1; then
  bad "the stub must REFUSE a repo it was not told to serve" "it answered"
else
  ok "the stub refuses a repo it was not told to serve (so a hardcoded subject cannot pass)"
fi

# ---------------------------------------------------------------------------------------------
# 1. AGREEMENT IS GREEN — and it is green because two independently-computed digests are equal,
#    not because the gate was asked whether it agreed with itself.
w="$(world "$WORK/w1")"
table "$WORK/t1.json" "$(digest_of "$w/blob")"
expect "a live file matching the recorded digest is GREEN" 0 "still hashes to" "$w" "$WORK/t1.json"

# The green must SAY it compared bytes and not behaviour (#1546 criterion 5 — a green here is
# narrow, and a summary that read as a behavioural all-clear would be the lie the criterion names).
out="$(run_gate "$w" "$WORK/t1.json")"
case "$out" in
  *"BYTES, not behaviour"*) ok "the green says it compared BYTES, not behaviour" ;;
  *) bad "the green must not read as a behavioural all-clear" "$out" ;;
esac

# ---------------------------------------------------------------------------------------------
# 2. THE RECORDED DIGEST IS MUTATED BY ONE CHARACTER. #1546 criterion 4's first half, and the
#    smallest possible drift: a table that is one nibble wrong is still a table nobody re-derived.
w="$(world "$WORK/w2")"
d="$(digest_of "$w/blob")"
mutated="$([ "${d:0:1}" = "a" ] && printf 'b%s' "${d:1}" || printf 'a%s' "${d:1}")"
table "$WORK/t2.json" "$mutated"
expect "a mutated recorded digest is RED" 1 "the SkillMirror conformance table is DATED" "$w" "$WORK/t2.json"

# CRITERION 1, IN FULL: the finding must name BOTH digests, the recorded commit, and the remedy.
out="$(run_gate "$w" "$WORK/t2.json")"
for needle in "$mutated" "$d" "$COMMIT" "$REDRIVE"; do
  if grep -qF -- "$needle" <<<"$out"; then
    ok "the finding names '${needle:0:46}'"
  else
    bad "the finding must name '${needle:0:46}'" "$out"
  fi
done
# ...and it must say what it is NOT, or a scheduled red gets read as a false accusation (#238/#698).
case "$out" in
  *"not an accusation against the diff"*) ok "the finding disclaims the diff that triggered it" ;;
  *) bad "the finding must say it is not about the triggering diff" "$out" ;;
esac

# 3. THE LIBRARY'S BYTES CHANGE. The realistic direction: the table is untouched and the subject
#    moves underneath it. This is the event #1546 was filed about, and it is REAL rather than
#    hypothetical — measured on the live API the day this gate was written, FS.GG.SDD's
#    SkillMirror.fs had already grown from 4371 to 18807 bytes since the table was derived.
w="$(world "$WORK/w3")"
table "$WORK/t3.json" "$(digest_of "$w/blob")"
printf 'module Fixture.SkillMirror\nlet verify () = ()\nlet somethingNew () = ()\n' >"$w/blob"
expect "a library whose bytes changed is RED" 1 "the SkillMirror conformance table is DATED" "$w" "$WORK/t3.json"

# ...INCLUDING A PURELY ADDITIVE CHANGE, which is what leg 3 actually is: `verify` is byte-identical
# and the gate still reds. That is the documented behaviour, not a defect — a digest cannot know the
# addition is unreachable, and the alternative is reading F# for intent (#683).
out="$(run_gate "$w" "$WORK/t3.json")"
case "$out" in
  *"can never say whether the change altered"*) ok "the red admits it cannot judge BEHAVIOUR (criterion 5)" ;;
  *) bad "the red must admit it compares a digest, not behaviour" "$out" ;;
esac

# 4. THE READ IS OF THE DEFAULT BRANCH, NEVER OF `derivedFrom.commit`. The stub refuses any query
#    string, so a gate that pinned the read would land here with the stub's exit 9 rather than a
#    verdict. Leg 1 passing at all is the positive half of this proof; this is the statement of it.
w="$(world "$WORK/w4")"
table "$WORK/t4.json" "$(digest_of "$w/blob")"
out="$(run_gate "$w" "$WORK/t4.json")"
rc=$?
if [ "$rc" -ne 0 ]; then
  bad "the no-\`?ref=\` leg must reach a verdict at all (exit $rc)" "$out"
elif grep -qF -- "query string" <<<"$out"; then
  bad "the gate pinned its read to the recorded commit — green forever" "$out"
else
  ok "the read carries no \`?ref=\` (a self-comparison would be green in every world)"
fi

# 4b. A TRANSPORT KNOB THAT WILL NOT PARSE IS A NO VERDICT, NOT A FINDING. Found by adversarial
#     review of this gate, and it is the harness's own invariant one level down: an `int(os.environ
#     [...])` evaluated at MODULE level raises during IMPORT, which is before `run()` exists to
#     catch anything — so Python exits 1, the FINDING code, and a typo in a workflow env: block
#     would report "the conformance table is DATED" having read nothing at all. MEASURED at exit 1
#     against that shape while this fixture was written; these legs are what keep it at 3.
w="$(world "$WORK/w4b")"
table "$WORK/t4b.json" "$(digest_of "$w/blob")"
#     The floors differ per knob and that is asserted too: a zero BACKOFF is legitimate (this whole
#     fixture runs at one), while zero TRIES means the gate never reads its subject.
for bad_knob in FSGG_SKILLMIRROR_TRIES=x FSGG_SKILLMIRROR_TIMEOUT=soon FSGG_SKILLMIRROR_TRIES=0; do
  out="$(PATH="$STUB:$PATH" WORLD="$w" env "$bad_knob" \
    "$PY" "$GATE" --root "$ROOT" --fixtures "$WORK/t4b.json" 2>&1)"
  rc=$?
  if [ "$rc" -eq 3 ]; then
    ok "an unparsable \`$bad_knob\` is a NO VERDICT, not a confident finding"
  else
    bad "\`$bad_knob\` must be exit 3, got $rc" "$out"
  fi
done

# ---------------------------------------------------------------------------------------------
# 5. THE PATH NO LONGER RESOLVES, IN A REPOSITORY WE CAN READ. #1546 criterion 3's second
#    sentence: that is not an unreachable read, it is an ANSWER — the library moved.
w="$(world "$WORK/w5" path-missing)"
table "$WORK/t5.json" "$(digest_of "$w/blob")"
expect "a path that 404s in a VISIBLE repo is a FINDING (the library moved)" 1 \
  "THE LIBRARY MOVED" "$w" "$WORK/t5.json"
out="$(run_gate "$w" "$WORK/t5.json")"
if grep -qF -- "$REDRIVE" <<<"$out"; then
  ok "the moved-library finding also names the re-derivation command"
else
  bad "the moved-library finding must name the remedy" "$out"
fi

# 6. ...AND THE SAME 404 IS A NO VERDICT WHEN THE REPOSITORY ITSELF IS INVISIBLE. This is the leg
#    that makes leg 5 sound. GitHub answers 404 for "no such path" and for "you may not know
#    whether this exists" alike, so grading the first without ruling out the second would render a
#    token problem as a confident accusation about someone else's source (#266, #238).
w="$(world "$WORK/w6" path-missing repo-invisible)"
table "$WORK/t6.json" "$(digest_of "$w/blob")"
expect "a 404 on an INVISIBLE repository is a NO VERDICT, not 'the library moved'" 2 \
  "404 for the repository ITSELF" "$w" "$WORK/t6.json"

# 7. A RATE LIMIT IS NOT AN ANSWER. Retryable — exit 2, never 1 and never 0.
w="$(world "$WORK/w7" ratelimit)"
table "$WORK/t7.json" "$(digest_of "$w/blob")"
expect "a rate-limited read is a NO VERDICT (retryable)" 2 "no verdict" "$w" "$WORK/t7.json"

# 8. NO `gh` AT ALL. Permanent — there is nothing to ask, and retrying asks it again.
#    PATH is emptied to a directory that exists and holds nothing, and the interpreter is invoked by
#    the path IT reports for itself. A leg whose 127 comes from a missing `python3` proves nothing
#    at all about `gh` — that is the vacuous-failure trap this file's header names, one level down,
#    and it is not hypothetical: `command -v python3` can name a PATH-dependent wrapper (a venv
#    shim, a pyenv shard) that stops working the moment PATH is emptied. `sys.executable` is the
#    real interpreter, which needs no PATH to start.
PY_ABS="$("$PY" -c 'import sys; print(sys.executable)')"
EMPTY="$WORK/empty"
mkdir -p "$EMPTY"
w="$(world "$WORK/w8")"
table "$WORK/t8.json" "$(digest_of "$w/blob")"
out="$(WORLD="$w" PATH="$EMPTY" FSGG_SKILLMIRROR_TRIES=1 FSGG_SKILLMIRROR_RETRY_DELAY=0 \
  "$PY_ABS" "$GATE" --root "$ROOT" --fixtures "$WORK/t8.json" 2>&1)"
rc=$?
if [ "$rc" -eq 3 ] && grep -qF -- "not on PATH" <<<"$out"; then
  ok "a missing \`gh\` is a NO VERDICT (permanent), not a pass"
else
  bad "a missing \`gh\` must be exit 3 naming the cause (got $rc)" "$out"
fi

# ---------------------------------------------------------------------------------------------
# THE VACUOUS-PASS FAMILY. Each of these is a table in which "the live file matches what we
# recorded" is satisfiable while nothing at all was compared. NO VERDICT (3), never green.

mutate_table() { # mutate_table <out> <python-expression-over-`d`>
  "$PY" - "$WORK/t1.json" "$1" "$2" <<'PY'
import json, sys
src, out, expr = sys.argv[1:4]
d = json.load(open(src, encoding="utf-8"))
exec(expr, {"d": d})  # noqa: S102 — fixture-local, and the expression is a literal below.
json.dump(d, open(out, "w", encoding="utf-8"), indent=2)
PY
}

w="$(world "$WORK/w9")"
table "$WORK/t1.json" "$(digest_of "$w/blob")"

mutate_table "$WORK/m-noblock.json" 'd.pop("derivedFrom")'
expect "a table with NO derivedFrom block is a NO VERDICT" 3 "has no \`derivedFrom\` object" "$w" "$WORK/m-noblock.json"

mutate_table "$WORK/m-notobj.json" 'd["derivedFrom"] = "a066e0b"'
expect "a derivedFrom that is not an object is a NO VERDICT" 3 "has no \`derivedFrom\` object" "$w" "$WORK/m-notobj.json"

for field in repo path commit skillMirrorFsSha256 howToRedrive; do
  mutate_table "$WORK/m-no-$field.json" "d['derivedFrom'].pop('$field')"
  expect "a derivedFrom missing \`$field\` is a NO VERDICT" 3 "derivedFrom.$field" "$w" "$WORK/m-no-$field.json"
  mutate_table "$WORK/m-blank-$field.json" "d['derivedFrom']['$field'] = '   '"
  expect "a BLANK \`$field\` is a NO VERDICT (same state as absent)" 3 "derivedFrom.$field" "$w" "$WORK/m-blank-$field.json"
done

mutate_table "$WORK/m-shortsha.json" 'd["derivedFrom"]["skillMirrorFsSha256"] = "b1c7e94d"'
expect "a TRUNCATED digest is a NO VERDICT, not a permanent inexplicable red" 3 \
  "64 lowercase hex" "$w" "$WORK/m-shortsha.json"

mutate_table "$WORK/m-upper.json" \
  'd["derivedFrom"]["skillMirrorFsSha256"] = d["derivedFrom"]["skillMirrorFsSha256"].upper()'
expect "an UPPERCASE digest is a NO VERDICT (it could never match a computed one)" 3 \
  "64 lowercase hex" "$w" "$WORK/m-upper.json"

mutate_table "$WORK/m-slug.json" 'd["derivedFrom"]["repo"] = "not-a-slug"'
expect "a repo that is not \`owner/name\` is a NO VERDICT" 3 "owner/name" "$w" "$WORK/m-slug.json"

mutate_table "$WORK/m-abspath.json" 'd["derivedFrom"]["path"] = "/etc/passwd"'
expect "an absolute recorded path is a NO VERDICT" 3 "repository-relative path" "$w" "$WORK/m-abspath.json"

mutate_table "$WORK/m-dotdot.json" 'd["derivedFrom"]["path"] = "src/../../secrets"'
expect "a \`..\` in the recorded path is a NO VERDICT" 3 "repository-relative path" "$w" "$WORK/m-dotdot.json"

printf '{ "derivedFrom": {\n' >"$WORK/m-unparsable.json"
expect "an unparsable table is a NO VERDICT" 3 "not parsable as JSON" "$w" "$WORK/m-unparsable.json"

expect "a table that is not there is a NO VERDICT" 3 "FAILED READ" "$w" "$WORK/does-not-exist.json"

# ---------------------------------------------------------------------------------------------
# THE SHIPPED TABLE, OFFLINE. The legs above all run against synthetic tables, so on their own they
# would pass just as happily beside a real table this gate can no longer read. This one grades the
# file the repo actually ships: the world makes BOTH endpoints 404, so reaching exit 2 proves the
# real `derivedFrom` block parsed and passed every validation on the way there. Exit 3 here would
# mean the shipped table is not gradeable at all.
w="$(world "$WORK/w-real" path-missing repo-invisible)"
"$PY" - "$TABLE" "$w" <<'PY'
import json, sys
block = json.load(open(sys.argv[1], encoding="utf-8"))["derivedFrom"]
open(sys.argv[2] + "/repo", "w", encoding="utf-8").write(block["repo"])
open(sys.argv[2] + "/path", "w", encoding="utf-8").write(block["path"])
PY
expect "the SHIPPED conformance table is gradeable (its derivedFrom validates)" 2 \
  "Schemas.fs" "$w" "$TABLE"

# ...and its remedy really is the command #1546 criterion 1 requires, spelled out. The gate prints
# whatever the table says, so pinning the criterion means pinning the table's string.
want="bash tests/skill-union/skillmirror-oracle.sh --lib <checkout>/src/FS.GG.Contracts"
got="$("$PY" -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["derivedFrom"]["howToRedrive"])' "$TABLE")"
if [ "$got" = "$want" ]; then
  ok "the shipped table's howToRedrive is the oracle command the finding must name"
else
  bad "the shipped table's howToRedrive drifted from the criterion" "want: $want
 got: $got"
fi

printf '\n'
if [ "$failcount" -eq 0 ]; then
  printf 'skillmirror-freshness fixture: OK (%d checks)\n' "$pass"
  exit 0
fi
printf 'skillmirror-freshness fixture: %d FAILURE(S) of %d checks\n' "$failcount" "$((pass + failcount))"
exit 1
