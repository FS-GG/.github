#!/usr/bin/env bash
# Fixture for the `receiver-validate` job in `.github/workflows/kit-materialize.yml` — slice 6 of the
# GitHub-native executor fencing design
# (`docs/reports/2026-08-04-github-native-executor-fencing-design.md` §5.3, §6.3, §9.1, §11.2), filed
# as `.github#2721` under `.github#1858` — Critical, open since 2026-07-28: two workers ran
# `.github#1853` to completion concurrently under ONE claim marker, and the second executor MERGED
# pull requests in six repositories.
#
# ==================================================================================================
# WHY THIS FIXTURE IS SHAPED THE WAY IT IS
# ==================================================================================================
#
# THIS PARENT HAS ABSORBED TWO FENCING READERS THAT LANDED INERT. Slice 3 wrote the
# `fsgg:pr-authorization` marker but not the merge election, so slice 4's check 4 has no producer;
# slice 2 landed `Client.OpLock.acquire` with no reachable caller, so no dispatch grant can exist.
# Both are CLOSED rows that pass every test written against them — because the tests supply the
# subject the production path never will. That pattern is SELF-CONCEALING: a fixture that builds its
# own marker and then asserts the gate reads it proves only that the fixture and the gate agree.
#
# So section F below is the leg that matters most here, and it is not optional decoration: it parses
# the PRODUCER — `authorizationMarker` in `src/FS.GG.Coord.Cli/Client.fs` — and asserts, IN BOTH
# DIRECTIONS, that the field set this gate requires is exactly the field set the production path
# emits. A producer that stops writing a field this gate demands reds here, and a gate that starts
# demanding a field no producer writes reds here too. That is the check neither slice 2 nor slice 3
# had.
#
# IT TESTS THE SHIPPED SCRIPT, NOT A COPY. Every behaviour leg extracts the `validate` step's real
# `run:` block from the real workflow, BY JOB ID AND STEP ID, and executes it. A re-typed copy would
# keep passing after someone edited the workflow — the fails-open shape epic #266 exists to close,
# rebuilt inside the fixture meant to close it. Selecting by id rather than by position matters for
# the same reason: an assertion that quietly starts testing a different step is that defect again.
#
# EVERY NEGATIVE LEG ASSERTS THE REASON, not merely a non-green verdict. #266's measured vacuous
# failure was a "must fail" test whose non-zero exit came from a path guard rather than from the
# thing under test.
#
# AND EVERY GATE HERE SHIPS WITH EVIDENCE IT CAN FAIL. Sections B, D and E each carry MUTATION legs
# that apply a specific, realistic regression to the extracted subject and assert the leg above it is
# CAUGHT. A gate whose inversion survives is a material finding by definition.
#
# ON SPELLINGS. Where this fixture must decide whether a piece of text is a marker, it does NOT
# enumerate spellings: section E binds the absence assertion with a CORPUS — spellings that must
# match and lookalikes that must not, the negatives lifted from live production code in this
# repository — and sections A/B ask the PARSED workflow document the question rather than asking it
# for a string. Where it can, it does the better thing still: it EXECUTES the real rule and observes
# the real exit status (sections C and D).
#
# No network and no runner: `gh` is STUBBED, reproducing exactly the contract the script depends on.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
WF="$REPO_ROOT/.github/workflows/kit-materialize.yml"
SELFTEST_WF="$REPO_ROOT/.github/workflows/fsgg-receiver-validate-selftest.yml"
CLIENT_FS="$REPO_ROOT/src/FS.GG.Coord.Cli/Client.fs"
WRITES_FS="$REPO_ROOT/src/FS.GG.Coord.GitHub/Writes.fs"
GATE_PY="$REPO_ROOT/scripts/lib/gate.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/receiver-validate-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() {
  echo "FAIL  $1"
  # `[ -n ... ] && printf` returns 1 on an empty $2 and, under `set -e`, would abort the fixture
  # mid-run — truncating the remaining legs and the summary. Spell it as an `if`.
  if [ -n "${2:-}" ]; then printf '%s\n' "$2" | sed 's/^/    | /'; fi
  failcount=$((failcount+1))
}

for f in "$WF" "$SELFTEST_WF" "$CLIENT_FS" "$WRITES_FS" "$GATE_PY"; do
  [ -f "$f" ] || { echo "FAIL  a subject this fixture stands on is missing: $f"; exit 1; }
done

# ==================================================================================================
# Pull the REAL script + structure out of the REAL workflow
# ==================================================================================================
VALIDATE="$WORK/validate.sh"
META="$WORK/meta.json"

python3 - "$WF" "$SELFTEST_WF" "$CLIENT_FS" "$WRITES_FS" "$GATE_PY" "$VALIDATE" "$META" <<'PY'
import json, re, sys, yaml

wf_path, selftest_path, client_path, writes_path, gate_path, validate_out, meta_out = sys.argv[1:8]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)
with open(selftest_path, encoding="utf-8") as fh:
    selftest = yaml.safe_load(fh)

jobs = wf.get("jobs") or {}
if "receiver-validate" not in jobs:
    sys.exit("the `receiver-validate` job is GONE from kit-materialize.yml — the receiver-side fence "
             "has been dismantled, and with it the only route that reaches all seven receivers.")
job = jobs["receiver-validate"]

steps = job.get("steps") or []
by_id = {s.get("id"): s for s in steps if isinstance(s, dict)}
if "validate" not in by_id or "run" not in by_id["validate"]:
    sys.exit("the `validate` verdict step is GONE from the receiver-validate job.")
step = by_id["validate"]
with open(validate_out, "w", encoding="utf-8") as fh:
    fh.write(step["run"])

run = step["run"]

# `on:` parses as the YAML 1.1 boolean True unless quoted. Read whichever key is present rather than
# assuming — an assertion that silently read `None` here would pass for the wrong reason.
def trigger_block(doc):
    return doc.get("on", doc.get(True)) or {}

# THE PRODUCER'S OWN FIELD SET, parsed from `authorizationMarker` in Client.fs rather than restated.
# This is the anti-inert-reader leg's input.
with open(client_path, encoding="utf-8") as fh:
    client_src = fh.read()
producer = re.search(
    r"let authorizationMarker[^=]*=\s*\n\s*\$\"(?P<body>[^\"]*)\"", client_src
)
producer_fields = []
if producer:
    producer_fields = re.findall(r"(?<![A-Za-z0-9_])([A-Za-z][A-Za-z0-9_]*)=", producer.group("body"))

# The `fsgg:claim` marker's own written field set, from `Writes.fs`'s marker body.
with open(writes_path, encoding="utf-8") as fh:
    writes_src = fh.read()
claim_tpl = re.search(r'\$"<!-- fsgg:claim (?P<body>[^"]*)-->"', writes_src)
claim_fields = []
if claim_tpl:
    claim_fields = re.findall(r"(?<![A-Za-z0-9_])([A-Za-z][A-Za-z0-9_]*)=", claim_tpl.group("body"))

# The org's shared verdict contract, parsed from `scripts/lib/gate.py`'s ExitCode.
with open(gate_path, encoding="utf-8") as fh:
    gate_src = fh.read()
gate_codes = dict(
    (name, int(value))
    for name, value in re.findall(r"^\s{4}([A-Z][A-Z_]*)\s*=\s*([0-9]+)\s*$", gate_src, re.M)
)

# The gate's OWN transcription of both, read out of the extracted script.
inline_required = re.search(r'^\s*REQUIRED = \((?P<t>[^)]*)\)', run, re.M)
inline_codes = re.search(
    r"^\s*OK, FINDING, NO_VERDICT_RETRYABLE, NO_VERDICT_PERMANENT = (?P<t>[0-9, ]+)$", run, re.M
)

meta = {
    "job_name": job.get("name"),
    "job_if": " ".join(str(job.get("if", "")).split()),
    "job_has_paths": "paths" in job,
    "job_continue_on_error": bool(job.get("continue-on-error")),
    "job_outputs": sorted((job.get("outputs") or {}).keys()),
    "step_ids": [s.get("id") for s in steps if isinstance(s, dict)],
    "step_uses": [s.get("uses") for s in steps if isinstance(s, dict) and s.get("uses")],
    "step_env_keys": sorted((step.get("env") or {}).keys()),
    "step_env_values": {k: str(v) for k, v in (step.get("env") or {}).items()},
    "step_continue_on_error": bool(step.get("continue-on-error")),
    "run_interpolates": "${{" in run,
    "run_publishes_verdict": bool(re.search(r"verdict=%s\\n'\s+\"\$rc\"\s*>>\s*\"\$GITHUB_OUTPUT\"", run)),
    "run_last_statement": [ln.strip() for ln in run.splitlines() if ln.strip() and not ln.strip().startswith("#")][-1],
    "run_mentions_secrets": "secrets." in run,
    "workflow_secrets": sorted(((wf.get("on", wf.get(True)) or {}).get("workflow_call", {}).get("secrets") or {}).keys()),
    "producer_fields": producer_fields,
    "producer_found": bool(producer),
    "claim_fields": claim_fields,
    "claim_tpl_found": bool(claim_tpl),
    "gate_codes": gate_codes,
    "inline_required": [t.strip().strip("\"'") for t in (inline_required.group("t").split(",") if inline_required else []) if t.strip()],
    "inline_codes": [int(t) for t in (inline_codes.group("t").split(",") if inline_codes else [])],
    "selftest_pr_paths": list((trigger_block(selftest).get("pull_request") or {}).get("paths") or []),
    "selftest_runs_fixture": any(
        "tests/receiver-validate/run.sh" in str(s.get("run", ""))
        for j in (selftest.get("jobs") or {}).values()
        if isinstance(j, dict)
        for s in (j.get("steps") or [])
        if isinstance(s, dict)
    ),
}
with open(meta_out, "w", encoding="utf-8") as fh:
    json.dump(meta, fh)
PY

m() { python3 -c "import json,sys;print(json.load(open('$META'))$1)"; }

# ==================================================================================================
# The `gh` stub
# ==================================================================================================
# STUBBED, NOT CALLED: this fixture has no network by design. The stub reproduces exactly the
# contract the script depends on — `gh api --paginate repos/<owner>/<repo>/issues/<n>/comments`
# prints one JSON array, or fails non-zero — so the legs test the rule's LOGIC against a real-shaped
# answer, and a change to that contract shows up as a stub that no longer matches the call.
STUB="$WORK/bin"; mkdir -p "$STUB"
cat > "$STUB/gh" <<'SH'
#!/usr/bin/env bash
set -uo pipefail
: "${GH_STUB_DIR:?the fixture must set GH_STUB_DIR}"
target=""
for a in "$@"; do case "$a" in repos/*/issues/*/comments) target="$a";; esac; done
if [ -n "${GH_STUB_FAIL:-}" ]; then echo "HTTP 403: Resource not accessible by integration" >&2; exit 1; fi
[ -n "$target" ] || { echo "stub gh: unrecognised call: $*" >&2; exit 1; }
key="${target#repos/}"; key="${key%/comments}"; key="${key//\//__}"
file="$GH_STUB_DIR/$key.json"
[ -f "$file" ] || { echo "HTTP 404: Not Found ($target)" >&2; exit 1; }
cat "$file"
SH
chmod +x "$STUB/gh"

ITEM_REPO="FS-GG/.github"
ITEM_NUMBER=2721
ITEM="$ITEM_REPO#$ITEM_NUMBER"
GEN=5309849894
HEAD=1234567890abcdef1234567890abcdef12345678

# write_comments <owner/repo> <number> <spec-json>
#   spec entries: {"id":N,"kind":"claim|claim-badtime|claim-noid|other|quoted-claim","age":secs,"lease":mins}
write_comments() {
  python3 - "$WORK/stub" "$1" "$2" "$3" <<'PY'
import datetime, json, os, sys
stub_dir, repo, number, spec = sys.argv[1], sys.argv[2], sys.argv[3], json.loads(sys.argv[4])
os.makedirs(stub_dir, exist_ok=True)
now = datetime.datetime.now(datetime.timezone.utc)
out = []
for c in spec:
    at = now - datetime.timedelta(seconds=c.get("age", 60))
    stamp = at.strftime("%Y-%m-%dT%H:%M:%SZ")
    kind = c["kind"]
    entry = {"id": c["id"], "updated_at": stamp}
    if kind == "claim":
        entry["body"] = (
            "<!-- fsgg:claim worker=%s lease=%d renewed=639225061244938392 session=s1 prev=Ready "
            "pathRepo=.github -->" % (c.get("worker", "w-%s" % c["id"]), c.get("lease", 120))
        )
    elif kind == "claim-nolease":
        # No `lease=` at all: the gate must fall back rather than crash or treat it as live forever.
        entry["body"] = "<!-- fsgg:claim worker=%s renewed=1 -->" % c.get("worker", "w")
    elif kind == "claim-rawlease":
        # An arbitrary `lease=` token, so a leg can feed the parser text a human or a forger can type.
        # `'²'` is the one that matters: `str.isdigit()` returns True for it and `int()` raises.
        entry["body"] = "<!-- fsgg:claim worker=%s lease=%s renewed=1 -->" % (
            c.get("worker", "w"), c["lease_raw"],
        )
    elif kind == "claim-badtime":
        entry["updated_at"] = "not-a-timestamp"
        entry["body"] = "<!-- fsgg:claim worker=w lease=120 renewed=1 -->"
    elif kind == "claim-noid":
        entry["id"] = None
        entry["body"] = "<!-- fsgg:claim worker=w lease=120 renewed=1 -->"
    elif kind == "quoted-claim":
        # A comment that QUOTES a claim marker in prose. It must NOT enter the election — the exact
        # forgery `Reads.fs:220-226` anchors against.
        entry["body"] = (
            "Reviewing this: the marker on the item reads `<!-- fsgg:claim worker=forger lease=120 "
            "renewed=1 -->` which is why the winner is what it is."
        )
    else:
        entry["body"] = c.get("body", "an ordinary human comment")
    out.append(entry)
key = ("%s/issues/%s" % (repo, number)).replace("/", "__")
with open(os.path.join(stub_dir, key + ".json"), "w", encoding="utf-8") as fh:
    json.dump(out, fh)
PY
}

reset_world() {
  rm -rf "$WORK/stub"; mkdir -p "$WORK/stub"
  write_comments "$ITEM_REPO" "$ITEM_NUMBER" "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"curlew-d56f\"}]"
}

marker() { printf '<!-- fsgg:pr-authorization v=1 item=%s gen=%s head=%s -->' "${1:-$ITEM}" "${2:-$GEN}" "${3:-$HEAD}"; }

# run_validate <script> [VAR=VALUE ...] -> writes $WORK/out and $WORK/step_output, echoes step exit
#
# IT ALSO TALLIES WHETHER THE RUN EXPLAINED ITSELF, and leg H1 at the bottom asserts that EVERY run of
# the real rule did (round-1 repair). The material finding of review round 1 was that an escaping
# exception published `verdict=1` — a code this rule's vocabulary defines as FINDING — with NO
# annotation and NO summary, because every reporting statement lives below the call that crashed. A
# per-input leg would have caught the two inputs I thought of; this catches the property, on every
# input any leg ever feeds it. Only runs of the REAL rule are counted: the mutation legs deliberately
# drive mutants whose whole purpose is to misbehave.
#
# Note the tallies are FILES, not shell variables: every call site invokes this inside `$( )`, so a
# variable increment would happen in a subshell and be discarded — a counter that silently stays zero
# is the vacuous-pass shape this fixture is about.
run_validate() {
  local script="$1"; shift
  : > "$WORK/step_output"; : > "$WORK/summary.md"
  local rc=0
  env -i \
    PATH="$STUB:/usr/bin:/bin" \
    HOME="$HOME" \
    GH_STUB_DIR="$WORK/stub" \
    GITHUB_OUTPUT="$WORK/step_output" \
    GITHUB_STEP_SUMMARY="$WORK/summary.md" \
    PR_BODY="$(marker)" PR_HEAD="$HEAD" PR_BRANCH="item/2721-receiver-validate" \
    PR_NUMBER=99 SELF_REPO="FS-GG/FS.GG.Net" GH_TOKEN="stub-token" \
    "$@" \
    bash "$script" >"$WORK/out" 2>&1 || rc=$?
  if [ "$script" = "$VALIDATE" ]; then
    printf 'x' >> "$WORK/runs.tally"
    if ! grep -qE '::(notice|warning)::receiver-validate: ' "$WORK/out" || [ ! -s "$WORK/summary.md" ]; then
      printf 'x' >> "$WORK/unreported.tally"
      { echo "=== a run that published a verdict without explaining it ==="
        echo "--- env overrides: $* ---"
        cat "$WORK/out"; } >> "$WORK/unreported.log"
    fi
  fi
  echo "$rc"
}

out_value() { sed -n "s/^$1=//p" "$WORK/step_output" | tail -1; }

# verdict <name> <expected-code> <reason-pattern> [VAR=VALUE ...]
#
# Asserts THREE things at once, and all three are load-bearing:
#   1. the STEP exits 0 — observe-only, in every arm including the refusals;
#   2. the PUBLISHED verdict is the expected code — the rule's own real exit status;
#   3. the output SAYS WHY — a code with no reason is the vacuous-failure defect.
verdict() {
  local name="$1" want="$2" pat="$3"; shift 3
  reset_world
  local rc; rc="$(run_validate "$VALIDATE" "$@")"
  if [ "$rc" != 0 ]; then
    bad "$name" "$(printf 'the STEP exited %s — observe-only is broken, this would red seven receivers\n%s' "$rc" "$(cat "$WORK/out")")"; return
  fi
  local got; got="$(out_value verdict)"
  if [ "$got" != "$want" ]; then
    bad "$name" "$(printf 'expected verdict %s, got %q\n%s' "$want" "$got" "$(cat "$WORK/out")")"; return
  fi
  if grep -qi -- "$pat" "$WORK/out"; then ok "$name"
  else bad "$name" "$(printf 'reached verdict %s, but not for the stated reason (want %q):\n%s' "$want" "$pat" "$(cat "$WORK/out")")"; fi
}

# mutate <old> <new> [first|last] -> a copy of the extracted script with one regression applied
#
# `last` EXISTS BECAUSE OF A REAL BUG THIS FIXTURE HIT. D2 mutates the observe-only `exit 0`, and the
# FIRST occurrence of that text in the script is inside the comment that explains it — so a
# first-match mutation edited prose, the mutant behaved identically, and the leg reported that the
# boundary could not be inverted when in fact it had never been touched. A mutation that silently
# does nothing is the vacuous-pass defect wearing the costume of the test meant to catch it, which is
# why the guard below refuses a target it cannot find AND why the occurrence is chosen explicitly.
mutate() {
  local dest="$WORK/mutant.sh"
  cp "$VALIDATE" "$dest"
  python3 - "$dest" "$1" "$2" "${3:-first}" <<'PY'
import sys
path, old, new, which = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
src = open(path, encoding="utf-8").read()
if old not in src:
    sys.exit("MUTATION IMPOSSIBLE: %r is not in the extracted script. The mutation leg would report a "
             "vacuous pass — the very defect it exists to catch. Re-derive it from the real script." % old)
if which == "last":
    head, sep, tail = src.rpartition(old)
    out = head + new + tail
else:
    out = src.replace(old, new, 1)
if out == src:
    sys.exit("MUTATION WAS A NO-OP: the subject is unchanged, so the leg would measure nothing.")
open(path, "w", encoding="utf-8").write(out)
PY
  printf '%s\n' "$dest"
}

# mutate_more <mutant> <old> <new> — stack a second regression onto an existing mutant, with the same
# two guards. Needed because THIS CHANGE MADE TWO REPAIRS TO ONE DEFECT, and each is only load-bearing
# given the other's absence: the ascii-digit guard stops the crash HAPPENING, the catch-all changes
# what a crash REPORTS. Reverting either one alone leaves the rule well-behaved — which is a real
# finding about the repair, not a reason to weaken the leg — so reproducing review round 1's measured
# defect needs both reverted at once.
mutate_more() {
  python3 - "$1" "$2" "$3" <<'PY'
import sys
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
src = open(path, encoding="utf-8").read()
if old not in src:
    sys.exit("MUTATION IMPOSSIBLE: %r is not in the mutant. Re-derive this leg." % old)
out = src.replace(old, new, 1)
if out == src:
    sys.exit("MUTATION WAS A NO-OP: the subject is unchanged, so the leg would measure nothing.")
open(path, "w", encoding="utf-8").write(out)
PY
}

echo "== A. structure, asked of the PARSED workflow document =="

[ "$(m "['job_name']")" = "receiver-validate" ] \
  && ok "A1  the job's display name is 'receiver-validate' — the right-hand half of the context 'materialize / receiver-validate', defined here once" \
  || bad "A1  the job's display name is 'receiver-validate'" "got: $(m "['job_name']")"

# GitHub creates NO check run for a job a `paths:` filter excluded, and branch protection cannot tell
# that from "has not reported yet" (#1508). Applicability is decided INSIDE the job.
[ "$(m "['job_has_paths']")" = "False" ] \
  && ok "A2  the job carries no paths: narrowing (#1508: a filtered job produces no check run at all)" \
  || bad "A2  the job carries no paths: narrowing"

# A `continue-on-error` job reports SUCCESS while failing — it would turn the reporter off while
# leaving every line of it in place. Observe-only already exits 0; this must not ALSO be true, or a
# later arming would silently keep passing.
[ "$(m "['job_continue_on_error']")" = "False" ] && [ "$(m "['step_continue_on_error']")" = "False" ] \
  && ok "A3  neither the job nor the verdict step carries continue-on-error" \
  || bad "A3  continue-on-error is set somewhere on this job"

[ "$(m "['run_interpolates']")" = "False" ] \
  && ok "A4  the run: block contains no \${{ }} — the PR body is attacker-controlled text and a script-injection sink (closing-keywords.yml:97-105, design §6.2)" \
  || bad "A4  the run: block interpolates \${{ }} — a script-injection sink"

# Compared as PARSED LISTS, not as rendered text: `print(list)` and a JSON literal differ only in
# quoting, and a leg that failed on that would be a fixture bug reported as a workflow finding.
python3 - "$META" <<'PY' && ok "A5  every untrusted value arrives through env:, and the set is exactly the six the rule reads" || bad "A5  the verdict step's env: set changed" "got: $(m "['step_env_keys']")"
import json, sys
got = json.load(open(sys.argv[1]))["step_env_keys"]
want = ["GH_TOKEN", "PR_BODY", "PR_BRANCH", "PR_HEAD", "PR_NUMBER", "SELF_REPO"]
sys.exit(0 if got == want else 1)
PY

[ "$(m "['run_publishes_verdict']")" = "True" ] \
  && ok "A6  the rule's real exit status is captured and published as the step's verdict output" \
  || bad "A6  the run: block no longer publishes verdict=\$rc — the real code would be unobservable"

[ "$(m "['run_last_statement']")" = "exit 0" ] \
  && ok "A7  the verdict step's last statement is 'exit 0' — observe-only (design §9.1 step 1); arming is slice 8's job" \
  || bad "A7  the verdict step no longer ends in 'exit 0'" "got: $(m "['run_last_statement']")"

[ "$(m "['step_uses']")" = "[]" ] \
  && ok "A8  the job takes no checkout and uses no action — a job that runs on any PR, a fork head included, handles no credential and materialises no untrusted file" \
  || bad "A8  the job gained a uses: step" "got: $(m "['step_uses']")"

[ "$(m "['run_mentions_secrets']")" = "False" ] \
  && ok "A9  the rule reads no secret — it mints no App token, unlike the materialize job below it" \
  || bad "A9  the rule reads a secret"

[ "$(m "['selftest_runs_fixture']")" = "True" ] \
  && ok "A10 fsgg-receiver-validate-selftest.yml actually executes this fixture" \
  || bad "A10 nothing in CI runs this fixture — every leg below would be decorative"

# THE `paths:` FILTER IS PART OF THE GATE, NOT PACKAGING. The subject is a job inside
# kit-materialize.yml plus the producer in Client.fs; a filter that misses either means a change to
# them lands with this fixture never running.
for entry in ".github/workflows/kit-materialize.yml" "tests/receiver-validate/**" "src/FS.GG.Coord.Cli/Client.fs"; do
  if python3 -c "import json,sys;sys.exit(0 if '$entry' in json.load(open('$META'))['selftest_pr_paths'] else 1)"; then
    ok "A11 the selftest's pull_request paths: cover $entry"
  else
    bad "A11 the selftest's pull_request paths: cover $entry" "got: $(m "['selftest_pr_paths']")"
  fi
done

echo
echo "== B. the job's if: is a TAUTOLOGY over pull_request — evaluated, not read =="

# A GitHub-expression evaluator for the sub-grammar this `if:` uses. It is deliberately STRICT and
# REFUSES what it cannot read, reporting NOT-MEASURED — because a permissive evaluator would silently
# green every leg below the day someone edited the expression into something it mis-parsed, which is
# a fail-open inside the very fixture meant to close one.
cat > "$WORK/evalif.py" <<'PY'
import json, re, sys

TOKEN = re.compile(r"\s*(\(|\)|&&|\|\||!=|==|!|'[^']*'|[A-Za-z_][A-Za-z0-9_.]*\(?)")


class Refuse(Exception):
    pass


def tokenize(text):
    out, i = [], 0
    while i < len(text):
        m = TOKEN.match(text, i)
        if not m:
            raise Refuse("unreadable at %r" % text[i:i + 20])
        out.append(m.group(1))
        i = m.end()
    return out


def lookup(ctx, path):
    cur = ctx
    for part in path.split("."):
        if not isinstance(cur, dict) or part not in cur:
            return None
        cur = cur[part]
    return cur


def parse(tokens, ctx):
    pos = [0]

    def peek():
        return tokens[pos[0]] if pos[0] < len(tokens) else None

    def take():
        tok = peek()
        pos[0] += 1
        return tok

    def primary():
        tok = take()
        if tok is None:
            raise Refuse("ran out of tokens")
        if tok == "(":
            v = expr()
            if take() != ")":
                raise Refuse("unbalanced parenthesis")
            return v
        if tok == "!":
            return not primary()
        if tok == "startsWith(":
            a = primary()
            if take() != ",":
                raise Refuse("startsWith needs two arguments")
            b = primary()
            if take() != ")":
                raise Refuse("unbalanced startsWith")
            return isinstance(a, str) and isinstance(b, str) and a.startswith(b)
        if tok == "cancelled(":
            if take() != ")":
                raise Refuse("cancelled takes no arguments")
            return bool(ctx.get("cancelled"))
        if tok.startswith("'"):
            return tok[1:-1]
        if tok.endswith("("):
            raise Refuse("unknown function %s)" % tok)
        return lookup(ctx, tok)

    def comparison():
        left = primary()
        if peek() in ("==", "!="):
            op = take()
            right = primary()
            return (left == right) if op == "==" else (left != right)
        return left

    def expr():
        value = comparison()
        while peek() in ("&&", "||"):
            op = take()
            other = comparison()
            value = (bool(value) and bool(other)) if op == "&&" else (bool(value) or bool(other))
        return value

    result = expr()
    if pos[0] != len(tokens):
        raise Refuse("trailing tokens: %s" % tokens[pos[0]:])
    return bool(result)


expression, context = sys.argv[1], json.loads(sys.argv[2])
# `startsWith(a, b)` needs its comma to be a token; add it to the grammar the cheap way.
expression = expression.replace(",", " , ")
try:
    toks = [t for t in tokenize(expression) if t.strip()]
    toks = [t if t != "," else "," for t in toks]
    print("RUN" if parse(toks, context) else "SKIP")
except Refuse as why:
    print("NOT-MEASURED: %s" % why)
PY

evalif() { python3 "$WORK/evalif.py" "$1" "$2"; }

# EVERY CONTEXT CARRIES `repository` AND `head.repo.full_name`, and that is not padding. A context
# missing them makes `github.event.pull_request.head.repo.full_name == github.repository` compare
# None to None — TRUE — so the same-repo narrowing in B10 would evaluate RUN everywhere and its
# mutation leg would report a vacuous pass. A context too thin to distinguish the mutation is a
# fixture that measures nothing (#266), so the fork row is a REAL fork.
C_PR_ITEM='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","event":{"pull_request":{"head":{"ref":"item/2721-x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_RENOVATE='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","event":{"pull_request":{"head":{"ref":"renovate/fs.gg.kit-0.x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_FORK='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","event":{"pull_request":{"head":{"ref":"patch-1","repo":{"full_name":"outsider/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_DOCS='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","event":{"pull_request":{"head":{"ref":"docs/readme","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_CANCELLED='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","event":{"pull_request":{"head":{"ref":"item/2721-x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":true}'
C_DISPATCH='{"github":{"event_name":"workflow_dispatch","repository":"FS-GG/FS.GG.SDD","ref_name":"main"},"cancelled":false}'
C_PRTARGET='{"github":{"event_name":"pull_request_target","repository":"FS-GG/FS.GG.SDD","event":{"pull_request":{"head":{"ref":"item/2721-x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'

# THE EVALUATOR MUST ANSWER ALL THREE WAYS, or every leg below is a tautology about a broken tool
# rather than a verdict about the job.
if [ "$(evalif "github.event_name == 'pull_request'" "$C_PR_ITEM")" = "RUN" ] \
   && [ "$(evalif "github.event_name == 'pull_request'" "$C_DISPATCH")" = "SKIP" ] \
   && case "$(evalif "github.event_name =~ 'x'" "$C_PR_ITEM")" in NOT-MEASURED*) true;; *) false;; esac; then
  ok "B0  the expression evaluator answers RUN, SKIP and NOT-MEASURED, and refuses a token it cannot read"
else
  bad "B0  the expression evaluator is broken — the legs below would measure nothing (#266)"
fi

JOB_IF="$(m "['job_if']")"
# leg <label> <context> <want> <expression>
ifleg() {
  local got; got="$(evalif "$4" "$2")"
  case "$got" in
    NOT-MEASURED*) bad "$1" "NOT MEASURED — the evaluator refused this expression, so this is not a verdict about the job ($got)";;
    "$3") ok "$1";;
    *) bad "$1" "want $3, got $got — expression: $4";;
  esac
}

ifleg "B1  an item/<n>-* pull request RUNS this job"       "$C_PR_ITEM"      RUN  "$JOB_IF"
ifleg "B2  a renovate/* bump pull request RUNS it"          "$C_PR_RENOVATE" RUN  "$JOB_IF"
ifleg "B3  a fork-head pull request RUNS it"                "$C_PR_FORK"     RUN  "$JOB_IF"
ifleg "B4  a docs pull request RUNS it (#1508: the check run must exist on 100% of PRs)" "$C_PR_DOCS" RUN "$JOB_IF"
ifleg "B5  a cancelled pull-request run still evaluates RUN — this if: does not read cancelled()" "$C_PR_CANCELLED" RUN "$JOB_IF"
ifleg "B6  a workflow_dispatch repair run SKIPS it — there is no pull request to grade, and a red on the repair button teaches operators to ignore red" "$C_DISPATCH" SKIP "$JOB_IF"
ifleg "B7  a pull_request_target run SKIPS it" "$C_PRTARGET" SKIP "$JOB_IF"

# ---- MUTATIONS: every narrowing that reintroduces #1508 must be CAUGHT by B1-B4 --------------------
# Each is a REALISTIC edit: three of them are the exact shapes carried elsewhere in this very file.
mutleg() {
  local label="$1" expr="$2"
  local caught=0
  for ctx in "$C_PR_ITEM" "$C_PR_RENOVATE" "$C_PR_FORK" "$C_PR_DOCS"; do
    [ "$(evalif "$expr" "$ctx")" = "RUN" ] || caught=1
  done
  [ "$caught" = 1 ] && ok "$label" || bad "$label" "this narrowing skips NO pull request under B1-B4, so those legs cannot catch it: $expr"
}
mutleg "B8  MUTATION: gating on a renovate/* head is CAUGHT (the materialize job's own arm-1 shape)" \
       "github.event_name == 'pull_request' && startsWith(github.event.pull_request.head.ref, 'renovate/')"
mutleg "B9  MUTATION: gating on an item/<n>-* head is CAUGHT (the tempting 'only fence what we fence' edit)" \
       "github.event_name == 'pull_request' && startsWith(github.event.pull_request.head.ref, 'item/')"
mutleg "B10 MUTATION: gating on a same-repo head is CAUGHT (excludes every fork PR)" \
       "github.event_name == 'pull_request' && github.event.pull_request.head.repo.full_name == github.repository"
mutleg "B11 MUTATION: adding && !cancelled() is CAUGHT on a cancelled run" \
       "github.event_name == 'pull_request' && !cancelled() && github.event.pull_request.head.ref == 'nope'"

echo
echo "== C. the refusal truth table — EXECUTED against the real rule, reason asserted =="

verdict "C1  the happy path is authorized" 0 'names the live claim'

verdict "C2  a pull request with no authorization and no item/ branch ABSTAINS, and says it is not a pass" 0 \
        'this is not a pass' PR_BODY="just an ordinary pull request" PR_BRANCH="renovate/fs.gg.kit-0.x"

# #1858's OWN SHAPE, and it is not hypothetical: FS-GG/FS.GG.Net#68 (`item/67-adopt-coord-cli`) and
# #66 are live pull requests on item/ branches carrying no marker at all, in the very repository the
# incident reached.
verdict "C3  an item/<n>-* branch with NO marker is a FINDING (#1858's own shape)" 1 \
        'no .fsgg:pr-authorization. marker' PR_BODY="no marker here"

verdict "C4  two markers are a FINDING — declarations are not append-only" 1 \
        'exactly one is required' PR_BODY="$(marker)
$(marker "$ITEM" 5309849895)"

verdict "C5  a marker missing a required field is a FINDING" 1 \
        'missing required field' PR_BODY="<!-- fsgg:pr-authorization v=1 item=$ITEM gen=$GEN -->"

verdict "C6  a marker naming an unsupported version is a FINDING, not a narrower pass" 1 \
        'does not understand' PR_BODY="<!-- fsgg:pr-authorization v=2 item=$ITEM gen=$GEN head=$HEAD -->"

verdict "C7  the board's <repo>#N shorthand is refused BY NAME (.github#2107)" 1 \
        '2107' PR_BODY="$(marker ".github#2721")"

verdict "C8  a leading-zero generation is refused — one tenancy keyed two ways" 1 \
        'server-assigned' PR_BODY="$(marker "$ITEM" "0$GEN")"

verdict "C9  a head that is not 40-hex is refused" 1 \
        'lowercase hex' PR_BODY="$(marker "$ITEM" "$GEN" "HEAD")"

verdict "C10 a force-push after authorization is a FINDING — the authorization is for a different artifact" 1 \
        'is now at' PR_HEAD="fedcba0987654321fedcba0987654321fedcba09"

verdict "C11 a stale generation is a FINDING and NAMES the live holder" 1 \
        "curlew-d56f" PR_BODY="$(marker "$ITEM" 1111111111)"

echo "-- C12: the item carries comments but no claim marker: RELEASED, and that is a FINDING (§8.1) --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" '[{"id":1,"kind":"other"},{"id":2,"kind":"other"}]'
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 1 ] && grep -q 'tenancy that has ended' "$WORK/out"; then
  ok "C12 an item with comments but no fsgg:claim marker is RELEASED — a FINDING"
else
  bad "C12 an item with comments but no fsgg:claim marker is RELEASED — a FINDING" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C13: every lease lapsed: EXPIRED IS NOT EVICTED (.github#2656) --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":100000,\"lease\":10,\"worker\":\"curlew-d56f\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 1 ] && grep -q 'EXPIRED lease, not an eviction' "$WORK/out"; then
  ok "C13 an expired lease is reported as expiry, not as a lost race"
else
  bad "C13 an expired lease is reported as expiry, not as a lost race" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

# ==================================================================================================
# C14/C15 — THE TWO ARMS OF "EMPTY", AND WHY BOTH ARE LEGS
# ==================================================================================================
# A query that can return empty for two different reasons is not evidence. `[]` from this read means
# EITHER "this item has no comments" OR "the read reached the wrong subject" — a renumbered item, a
# transferred issue, a 200 with an empty page. C12 above is the arm where the read demonstrably
# reached a real subject (it returned comments) and the finding is real; C14 is the arm where it did
# not, and the answer must be a NO-VERDICT. Both arms are new subjects, including the one whose
# behaviour is unchanged — which is why C12 and C14 are separate legs rather than one.
echo "-- C14: ZERO comments is a NO-VERDICT, never the finding 'nobody holds this' --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" '[]'
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 3 ] && grep -q 'wrong subject' "$WORK/out"; then
  ok "C14 an empty comment list is a NO-VERDICT (3), not a FINDING"
else
  bad "C14 an empty comment list is a NO-VERDICT (3), not a FINDING" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

verdict "C15 a read that could not complete is a NO-VERDICT (2), never a pass (§6.3 check 6, §8.2)" 2 \
        'could not read' GH_STUB_FAIL=1

echo "-- C16: an item this token cannot find at all is a NO-VERDICT, not an unclaimed item --"
reset_world
rm -f "$WORK/stub/FS-GG__.github__issues__2721.json"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 2 ] && grep -q 'could not read' "$WORK/out"; then
  ok "C16 a 404 on the item is a NO-VERDICT — 'a 404 and an empty issue are the same bytes'"
else
  bad "C16 a 404 on the item is a NO-VERDICT" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C17: an unparseable marker refuses the WHOLE scan (Reads.fs:283, :473-486) --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"curlew-d56f\"},{\"id\":99,\"kind\":\"claim-badtime\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 3 ] && grep -q 'promotes the next candidate to winner' "$WORK/out"; then
  ok "C17 an unreadable claim marker refuses the whole scan rather than being dropped"
else
  bad "C17 an unreadable claim marker refuses the whole scan" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C18: a marker with no readable id refuses the whole scan too --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120},{\"id\":98,\"kind\":\"claim-noid\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 3 ]; then
  ok "C18 a claim marker with no readable id refuses the whole scan"
else
  bad "C18 a claim marker with no readable id refuses the whole scan" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C19: LOWEST LIVE id wins, and a lower STALE marker does not displace it --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" \
  "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"curlew-d56f\"},
    {\"id\":11,\"kind\":\"claim\",\"age\":100000,\"lease\":10,\"worker\":\"long-gone\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 0 ]; then
  ok "C19 a lower-id STALE marker does not beat the live winner (the lease filter is applied before the ordering)"
else
  bad "C19 a lower-id STALE marker does not beat the live winner" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C20: a lower LIVE marker DOES win, and the loser is refused BY NAME --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" \
  "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"curlew-d56f\"},
    {\"id\":11,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"rival-0001\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 1 ] && grep -q "rival-0001" "$WORK/out"; then
  ok "C20 a lower live marker wins and the loser is refused BY NAME — the two-executors case"
else
  bad "C20 a lower live marker wins and the loser is refused BY NAME" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C21: the marker's OWN lease= is what staleness is measured against (.github#2656) --"
reset_world
# 40 minutes old. Under the marker's declared 10-minute lease it is STALE; under the 120-minute
# fallback it would be live. The gate must read the marker, not the fallback.
write_comments "$ITEM_REPO" "$ITEM_NUMBER" "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":2400,\"lease\":10,\"worker\":\"curlew-d56f\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 1 ] && grep -q 'EXPIRED lease' "$WORK/out"; then
  ok "C21 staleness is measured against the lease THAT marker declares, not against a fleet default"
else
  bad "C21 staleness is measured against the marker's own lease=" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C22: a comment that QUOTES a claim marker in prose forges nothing --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" \
  "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"curlew-d56f\"},
    {\"id\":7,\"kind\":\"quoted-claim\",\"age\":60}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 0 ] && ! grep -q "forger" "$WORK/out"; then
  ok "C22 a quoted claim marker (comment id 7, lower than the real winner) does not become the winner"
else
  bad "C22 a quoted claim marker does not become the winner" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

# ==================================================================================================
# C23-C25 — AN ATTACKER-CONTROLLED FIELD MUST NOT BE ABLE TO CRASH THE RULE (review round 1)
# ==================================================================================================
# `merlin-9bb1` measured this against live GitHub: every `int()` in the rule took text straight from a
# pull-request body or an issue comment, and two of the three sites were reachable with an input that
# raises. An escaping ValueError exits the interpreter 1, and the wrapper publishes `verdict=1` —
# which this rule's own vocabulary defines as FINDING. It is not fail-OPEN (it never yields 0), and
# that is exactly what made it worth repairing rather than shrugging at: it collapses the
# FINDING/NO-VERDICT distinction C14-C18 spend five legs establishing, in the direction that emits a
# confident verdict with no reason behind it.
echo "-- C23: a 5000-digit item number does not crash the rule (CPython refuses int() past 4300) --"
BIG="$(python3 -c "print('9'*5000)")"
reset_world
rc="$(run_validate "$VALIDATE" PR_BODY="$(marker "FS-GG/.github#$BIG")")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 1 ] && grep -q 'server-assigned\|owner/repo#N' "$WORK/out"; then
  ok "C23 an absurdly long item number is reported as MALFORMED, not published as a bare verdict"
else
  bad "C23 an absurdly long item number is reported as MALFORMED" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C24: lease=² — str.isdigit() says True and int() raises (.github#266's shape in a stdlib call) --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" \
  "[{\"id\":$GEN,\"kind\":\"claim-rawlease\",\"age\":60,\"lease_raw\":\"²\",\"worker\":\"curlew-d56f\"}]"
rc="$(run_validate "$VALIDATE")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 0 ]; then
  ok "C24 a Unicode-digit lease= falls back to the default instead of crashing (and the claim stays live)"
else
  bad "C24 a Unicode-digit lease= falls back to the default instead of crashing" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo "-- C25: the same, via FSGG_CLAIM_LEASE_MIN, on a marker that declares no lease of its own --"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" "[{\"id\":$GEN,\"kind\":\"claim-nolease\",\"age\":60,\"worker\":\"curlew-d56f\"}]"
rc="$(run_validate "$VALIDATE" FSGG_CLAIM_LEASE_MIN="²")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 0 ]; then
  ok "C25 a Unicode-digit FSGG_CLAIM_LEASE_MIN falls back to the default instead of crashing"
else
  bad "C25 a Unicode-digit FSGG_CLAIM_LEASE_MIN falls back to the default" "$(printf 'step %s verdict %q\n%s' "$rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

echo
echo "== D. observe-only, proven by EXECUTION and inverted =="

# D1 is already implied by every leg above (each asserts step exit 0), but it is stated once
# explicitly on the arm that matters — a real FINDING — so a reader can see the boundary tested
# rather than inferred.
reset_world
rc="$(run_validate "$VALIDATE" PR_BODY="no marker here")"
if [ "$rc" = 0 ] && [ "$(out_value verdict)" = 1 ]; then
  ok "D1  a real FINDING still exits the step 0 while publishing verdict=1 — it reports, it gates nothing"
else
  bad "D1  a real FINDING still exits the step 0 while publishing verdict=1" "step $rc verdict $(out_value verdict)"
fi

# D2 — THE INVERSION. Arming this file (rather than slice 8's branch-protection edit) would red pull
# requests in SEVEN repositories this repo does not own, on the first push, before anyone has watched
# the context report once. If that mutation survived, the observe-only boundary would be a promise.
MUT="$(mutate 'exit 0' 'exit $rc' last)"
reset_world
rc="$(run_validate "$MUT" PR_BODY="no marker here")"
if [ "$rc" != 0 ]; then
  ok "D2  MUTATION: propagating the verdict (exit \$rc) makes the step FAIL — so D1 is a real assertion, not a tautology"
else
  bad "D2  MUTATION: propagating the verdict must make the step fail, but it exited 0" "$(cat "$WORK/out")"
fi

# D3 — REVIEW ROUND 1's DEFECT, RECONSTRUCTED EXACTLY. Revert BOTH repairs — the id-length guard, so
# the ValueError happens, and the catch-all, so it escapes — then feed the exact input `merlin-9bb1`
# used against live GitHub. The result is the shipped defect: `verdict=1`, which this rule's own
# vocabulary reads as FINDING, and NOTHING anywhere saying why.
#
# BOTH HALVES ARE REQUIRED AND THAT IS THE FINDING, NOT A WORKAROUND. Reverting the catch-all alone no
# longer crashes, because the guard now rejects a 5000-digit id as malformed before any `int()` runs —
# it reports a clean FINDING with a reason, which is correct behaviour. So this leg would have reported
# a vacuous pass if it had asserted "no annotation" against a single-mutation mutant.
MUT="$(mutate 'except Exception as crash:' 'except ZeroDivisionError as crash:')"
mutate_more "$MUT" 'return ascii_digits(value, MAX_ID_DIGITS) and value[0] != "0"' \
                   'return len(value) > 0 and value[0] != "0" and all("0" <= ch <= "9" for ch in value)'
reset_world
rc="$(run_validate "$MUT" PR_BODY="$(marker "FS-GG/.github#$BIG")")"
got="$(out_value verdict)"
if [ "$got" = 1 ] && ! grep -qE '::(notice|warning)::receiver-validate: ' "$WORK/out"; then
  ok "D3  MUTATION: without the catch-all, a crash publishes verdict=1 (FINDING) with NO annotation — so C23 and H1 are real assertions"
else
  bad "D3  MUTATION: without the catch-all a crash must publish an unexplained verdict=1" "$(printf 'verdict %q\n%s' "$got" "$(cat "$WORK/out")")"
fi

# D4 — AND THE SOURCE-LEVEL FIX IS INDEPENDENTLY LOAD-BEARING. Even WITH the catch-all, reverting the
# digit guard turns a well-formed pull request into a NO-VERDICT, because the rule stops evaluating.
# Two repairs, two subjects: the catch-all changes what a crash REPORTS, the guard stops the crash
# HAPPENING, and neither one alone makes C24 green.
MUT="$(mutate 'ascii_digits(declared, MAX_LEASE_DIGITS)' 'declared.isdigit()')"
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" \
  "[{\"id\":$GEN,\"kind\":\"claim-rawlease\",\"age\":60,\"lease_raw\":\"²\",\"worker\":\"curlew-d56f\"}]"
rc="$(run_validate "$MUT")"
got="$(out_value verdict)"
if [ "$got" = 3 ] && grep -q 'CRASHED' "$WORK/out"; then
  ok "D4  MUTATION: reverting the ascii-digit guard makes lease=² crash — caught by the catch-all as a NO-VERDICT, and C24 goes non-green"
else
  bad "D4  MUTATION: reverting the ascii-digit guard must make lease=² crash into a NO-VERDICT" "$(printf 'verdict %q\n%s' "$got" "$(cat "$WORK/out")")"
fi

echo
echo "== E. marker recognition: a corpus, not an enumeration of spellings =="

# THE ABSENCE ASSERTIONS BELOW ARE BOUND BY A CORPUS. "This body carries no marker" is satisfied
# equally well by a body with none and by a MATCHER THAT HAS GONE BLIND, so the corpus fixes both
# directions: N spellings that MUST be read as a marker, M lookalikes that MUST NOT — and the
# negatives are lifted from LIVE PRODUCTION TEXT in this repository, not invented.
corpus_leg() {
  local label="$1" want="$2" body="$3"
  reset_world
  local rc; rc="$(run_validate "$VALIDATE" PR_BODY="$body")"
  local got; got="$(out_value verdict)"
  if [ "$got" = "$want" ]; then ok "$label"
  else bad "$label" "$(printf 'want verdict %s, got %q\n%s' "$want" "$got" "$(cat "$WORK/out")")"; fi
}

# ---- MUST MATCH: every spelling the producer or a human editor can legitimately emit ----
corpus_leg "E1  canonical single-line marker is read"                 0 "$(marker)"
corpus_leg "E2  a marker with extra leading/trailing prose is read"   0 "Some description.

$(marker)

Closes #2721"
corpus_leg "E3  a marker with extra whitespace after <!-- is read"    0 "<!--   fsgg:pr-authorization v=1 item=$ITEM gen=$GEN head=$HEAD -->"
corpus_leg "E4  a MULTI-LINE marker is read (the design doc's own spelling spans lines)" 0 "<!-- fsgg:pr-authorization v=1 item=$ITEM
     gen=$GEN head=$HEAD -->"
# Client.fs's own doc comment: "The gate silently accepts (never rejects on) any ADDITIONAL
# key=value pair, so this stays forward-compatible with a future opkey=/grant= without either side
# having to change first." Slice 3's successor must not be redded by this gate for adding them.
corpus_leg "E5  a marker carrying the FUTURE opkey=/grant= fields is still read (Client.fs's stated forward-compatibility)" 0 \
  "<!-- fsgg:pr-authorization v=1 item=$ITEM gen=$GEN opkey=$(printf '0%.0s' $(seq 1 64)) grant=4242424242 head=$HEAD -->"

# ---- MUST NOT MATCH: lookalikes that would make this gate grade the wrong thing ----
# The suffix hazard. Without `\s+` after the prefix, EVERY `fsgg:pr-authorization<suffix>` is read as
# this marker. E7's spelling is the shape `check-claim-fence.py:248` names by name.
corpus_leg "E6  fsgg:pr-authorization-note is NOT this marker"        1 \
  "<!-- fsgg:pr-authorization-note v=1 item=$ITEM gen=$GEN head=$HEAD -->"
corpus_leg "E7  fsgg:pr-authorizations (plural) is NOT this marker"   1 \
  "<!-- fsgg:pr-authorizations v=1 item=$ITEM gen=$GEN head=$HEAD -->"
# A body that QUOTES the marker's text without opening a real HTML comment. This is the forgery
# `Reads.fs:220-226` anchors against, transplanted to the PR body.
corpus_leg "E8  the marker QUOTED in prose without an HTML comment is NOT a marker" 1 \
  "The reviewer asked what the marker looks like. It is
fsgg:pr-authorization v=1 item=$ITEM gen=$GEN head=$HEAD
and delivery writes it."
# A DIFFERENT marker family that legitimately appears in this org's PR bodies.
corpus_leg "E9  an fsgg:delivery-obligations declaration is NOT an authorization marker" 1 \
  "<!-- fsgg:delivery-obligations none head=$HEAD -->"

# ---- MUTATIONS: each corpus half must be able to catch its own regression ----
MUT="$(mutate 'fsgg:pr-authorization\s+(?P<fields>' 'fsgg:pr-authorization\s*(?P<fields>')"
reset_world
run_validate "$MUT" PR_BODY="<!-- fsgg:pr-authorization-note v=1 item=$ITEM gen=$GEN head=$HEAD -->" >/dev/null
if [ "$(out_value verdict)" = 0 ]; then
  ok "E10 MUTATION: \\s+ -> \\s* makes the -note lookalike read as a marker — so E6/E7 are real assertions"
else
  bad "E10 MUTATION: \\s+ -> \\s* must make the -note lookalike match, but the gate still refused" "$(cat "$WORK/out")"
fi

# THE CLAIM MARKER'S ANCHORING IS ENFORCED TWICE, and this measures what each half buys. Dropping
# `^` alone survives (re.match still anchors); switching `.match` to `.search` alone survives (`^`
# still anchors); doing BOTH lets a comment that merely QUOTES a claim marker enter the election.
MUT="$(mutate 'r"^<!--\s*fsgg:claim\s(?P<fields>.*?)-->"' 'r"<!--\s*fsgg:claim\s(?P<fields>.*?)-->"')"
python3 - "$MUT" <<'PY'
import sys
path = sys.argv[1]
src = open(path, encoding="utf-8").read()
old = "m = CLAIM_RE.match(body)"
if old not in src:
    sys.exit("MUTATION IMPOSSIBLE: the claim-marker match site moved; re-derive this leg.")
open(path, "w", encoding="utf-8").write(src.replace(old, "m = CLAIM_RE.search(body)", 1))
PY
reset_world
write_comments "$ITEM_REPO" "$ITEM_NUMBER" \
  "[{\"id\":$GEN,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"curlew-d56f\"},
    {\"id\":7,\"kind\":\"quoted-claim\",\"age\":60}]"
run_validate "$MUT" >/dev/null
if grep -q "forger" "$WORK/out"; then
  ok "E11 MUTATION: dropping BOTH anchors lets a quoted claim marker win the election — so C22 is a real assertion"
else
  bad "E11 MUTATION: dropping both anchors must let the quoted marker win, but it did not" "$(cat "$WORK/out")"
fi

echo
echo "== F. THE PRODUCER IS REAL — the leg slices 2 and 3 did not have =="

# A fixture that builds its own marker and then asserts the gate reads it proves only that the
# fixture and the gate agree. This section asks the PRODUCTION PATH instead, parsed from source:
# `authorizationMarker` in Client.fs is the exact body `delivery <ref> --pr N` PATCHes into the PR
# (`pnext-item` §6, `.github#2488`), and it is the only thing in the documented flow that writes one.
[ "$(m "['producer_found']")" = "True" ] \
  && ok "F1  the producer exists: Client.fs carries an authorizationMarker composing the marker this gate grades" \
  || bad "F1  Client.fs has NO authorizationMarker — this gate reads a marker nothing writes, which is exactly how slices 2 and 3 landed inert"

# BOTH DIRECTIONS. A producer that stops writing a field this gate requires reds here; a gate that
# starts requiring a field no producer writes reds here too. `check-claim-fence.py` requires six
# fields against a producer that writes four, which is why it finds nothing on any real pull request.
python3 - "$META" <<'PY' && ok "F2  the fields this gate REQUIRES are exactly the fields the producer WRITES, in both directions" || bad "F2  the gate's required fields and the producer's written fields disagree" "$(python3 -c "import json;d=json.load(open('$META'));print('gate requires:',d['inline_required']);print('producer writes:',d['producer_fields'])")"
import json, sys
d = json.load(open(sys.argv[1]))
gate, producer = set(d["inline_required"]), set(d["producer_fields"])
if not gate or not producer:
    sys.exit(1)
sys.exit(0 if gate == producer else 1)
PY

python3 - "$META" <<'PY' && ok "F3  the fsgg:claim fields this gate reads (worker, lease) are fields Writes.fs actually writes" || bad "F3  this gate reads an fsgg:claim field the engine does not write"
import json, sys
d = json.load(open(sys.argv[1]))
if not d["claim_tpl_found"]:
    sys.exit(1)
written = set(d["claim_fields"])
sys.exit(0 if {"worker", "lease"} <= written else 1)
PY

echo
echo "== G. the verdict contract is the org's shared one, not a second opinion =="

python3 - "$META" <<'PY' && ok "G1  the inline ExitCode transcription agrees with scripts/lib/gate.py (0 OK, 1 FINDING, 2/3 NO-VERDICT)" || bad "G1  the inline verdict codes have drifted from scripts/lib/gate.py" "$(python3 -c "import json;d=json.load(open('$META'));print('gate.py:',d['gate_codes']);print('inline:',d['inline_codes'])")"
import json, sys
d = json.load(open(sys.argv[1]))
shared, inline = d["gate_codes"], d["inline_codes"]
want = ["OK", "FINDING", "NO_VERDICT_RETRYABLE", "NO_VERDICT_PERMANENT"]
if not all(k in shared for k in want) or len(inline) != 4:
    sys.exit(1)
sys.exit(0 if [shared[k] for k in want] == inline else 1)
PY

echo
echo "== H. every verdict this fixture ever provoked explained itself =="

# THE GENERALISATION OF REVIEW ROUND 1's MATERIAL FINDING, and the reason it is a whole-fixture
# invariant rather than another row in the truth table. The defect was not "a 5000-digit item number
# crashes"; it was "a code can reach `$GITHUB_OUTPUT` with no reason attached". C23-C25 pin the two
# inputs that were found. THIS pins the property, across every input every leg above feeds the real
# rule — including inputs added by whoever edits this fixture next, who will not think to re-derive
# it. `run_validate` tallies as it goes; nothing here re-runs anything.
runs=0;   [ -f "$WORK/runs.tally" ]       && runs=$(wc -c < "$WORK/runs.tally")
unrep=0;  [ -f "$WORK/unreported.tally" ] && unrep=$(wc -c < "$WORK/unreported.tally")

# NON-VACUOUS FIRST. A tally of zero satisfies "none were unreported" perfectly, and would do so if
# `run_validate` stopped counting — the empty-result-set-as-a-negative-finding shape this repository
# has paid for repeatedly. So the count must be shown large before its zero means anything.
if [ "$runs" -ge 25 ]; then
  ok "H0  the tally is non-vacuous: $runs executions of the real rule were observed"
else
  bad "H0  the tally is non-vacuous" "only $runs execution(s) counted — H1's zero would assert nothing"
fi

if [ "$unrep" -eq 0 ]; then
  ok "H1  all $runs executions emitted an annotation AND wrote a step summary — no verdict was ever published without a reason"
else
  bad "H1  $unrep of $runs executions published a verdict with no annotation or no summary" "$(head -60 "$WORK/unreported.log")"
fi

# H2 — A SUMMARY THAT CANNOT BE WRITTEN LOSES A RENDERING, NEVER A VERDICT. This leg is why the
# summary write is guarded at all: it was written to test the annotation-before-summary ordering, and
# in the writing it showed that an `OSError` there escapes the same way review round 1's `ValueError`
# did — below the try/except, so `verdict=1` again, about a pull request whose answer was already
# correctly computed AND already announced.
#
# Invoked directly rather than through `run_validate`, deliberately: this run is EXPECTED to write no
# summary, and tallying it would make H1 red for the one input where an empty summary is the subject.
: > "$WORK/out"
env -i PATH="$STUB:/usr/bin:/bin" HOME="$HOME" GH_STUB_DIR="$WORK/stub" \
    GITHUB_OUTPUT="$WORK/step_output" GITHUB_STEP_SUMMARY="$WORK/no-such-dir/summary.md" \
    PR_BODY="$(marker)" PR_HEAD="$HEAD" PR_BRANCH="item/2721-receiver-validate" \
    PR_NUMBER=99 SELF_REPO="FS-GG/FS.GG.Net" GH_TOKEN="stub-token" \
    bash "$VALIDATE" >"$WORK/out" 2>&1
h2rc=$?
if [ "$h2rc" = 0 ] && [ "$(out_value verdict)" = 0 ] \
   && grep -q '::notice::receiver-validate: authorized' "$WORK/out" \
   && grep -q 'summary could not be written' "$WORK/out"; then
  ok "H2  an unwritable step summary loses the rendering and NOT the verdict — still verdict=0, still announced, and it says so"
else
  bad "H2  an unwritable step summary must lose the rendering and not the verdict" "$(printf 'step %s verdict %q\n%s' "$h2rc" "$(out_value verdict)" "$(cat "$WORK/out")")"
fi

# H3 — THE INVERSION FOR H2. Unguard the summary write and the OSError escapes: the rule exits
# non-zero even though it reached OK, and the wrapper would publish that as a FINDING.
# The target is the `except` CLAUSE, on one line. An earlier draft of this leg matched a two-line
# `try:`/`with` block copied out of the WORKFLOW — carrying the workflow's 14-space indentation, where
# the EXTRACTED script has 4, because YAML strips the block scalar's own indent. The mutation silently
# did not apply and this leg reported that the guard could not be inverted. Same class as the
# wrong-subject sweep and the comment-line `exit 0`: a mutation that quietly misses.
MUT="$(mutate 'except OSError as why:' 'except ZeroDivisionError as why:')"
: > "$WORK/out"
env -i PATH="$STUB:/usr/bin:/bin" HOME="$HOME" GH_STUB_DIR="$WORK/stub" \
    GITHUB_OUTPUT="$WORK/step_output" GITHUB_STEP_SUMMARY="$WORK/no-such-dir/summary.md" \
    PR_BODY="$(marker)" PR_HEAD="$HEAD" PR_BRANCH="item/2721-receiver-validate" \
    PR_NUMBER=99 SELF_REPO="FS-GG/FS.GG.Net" GH_TOKEN="stub-token" \
    bash "$MUT" >"$WORK/out" 2>&1
h3rc=$?
# THE ASSERTION IS ON THE PUBLISHED VERDICT, NOT ON THE STEP'S EXIT — observe-only pins that at 0 by
# design, which is exactly why the round-1 defect was publishable in the first place. An earlier draft
# of this leg asserted the step exit and reported that the guard could not be inverted while the
# traceback was sitting in the captured output.
h3got="$(out_value verdict)"
if [ "$h3rc" = 0 ] && [ "$h3got" = 1 ] && grep -q 'Traceback' "$WORK/out"; then
  ok "H3  MUTATION: unguarding the summary write publishes verdict=1 (FINDING) for a pull request the rule had already graded OK — so H2 is a real assertion"
else
  bad "H3  MUTATION: unguarding the summary write must publish an unearned verdict=1" "$(printf 'step %s verdict %q\n%s' "$h3rc" "$h3got" "$(cat "$WORK/out")")"
fi

echo
echo "-------------------------------------------------------------------------------"
echo "receiver-validate fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
