# Usage and review census tools

The scripts behind the settled usage and review census reports
([#4823](../../docs/reports/2026-10-23-usage-census-4823.md),
[#4639 continuation](../../docs/reports/2026-10-24-usage-census-4639.md)), published so the next
extension window does not depend on one worker's scratch directory. They are read-only against
GitHub and the local transcripts. They open no receiver, telemetry store or UTEL database, and they
add no store of their own: everything they write goes under one work directory.

What a census records per window `[lo, hi)`: every PR merged in the organization and every Project 3
item closed; per PR the route marker, the review evidence (an exact-head independent verdict, or
**unsampled** with a reason), the required-check conclusion, Actions job wall time (PR phase and
merge-commit pushes) and open-to-merge elapsed time (labelled as including overlapping queues); and
native Claude Code and Codex token counters from the coordinator host through the strict reader.
Billed cost, other hosts, per-reviewer tokens and anything the provider does not support stay
**Unknown**, never zero. No attribution, baseline or cost is invented, and no transcript is recaptured.

## Requirements

- `gh` authenticated for the organization (the census credential), `python3` 3.10 or later (standard
  library only), and `dotnet` with the SDK in `global.json` to build the reader.
- The two-CPU budget: every tool is single-threaded except `prdetail.py` and `required.py` (two threads).
  Run them under `taskset -c 0,1` on a shared host.
- GraphQL and REST are the shared account budget (5,000 points and 5,000 calls an hour). `jobs.py`
  throttles while the remaining REST quota is below `--floor`; leave headroom for other agents.
- A **work directory** outside every repository checkout and outside the temporary directory. It holds
  raw per-response usage rows and must never be committed or published.

## Build the reader once

```
tools/usage-census/build-reader.sh "$HOME/.cache/usage-census/reader"
```

This builds the CLI at the pinned commit (`61526e94`, PR #4802), outside the repository. A built
reader is not checked in: it is a 67 MB .NET output tied to one commit. A newer commit may be passed
as the second argument; record it in the report, because the counters come from the reader alone.
The script was run for this publication: the rebuilt reader's `fsgg-coord-engine.dll` differs byte-wise from
the census-time build (build metadata), and on a sample transcript its counter columns are identical to the
census-time output (only the `source` transcript hash differs, because the live transcript had grown).

## Run the next extension window

Choose the new window so that it starts exactly where the last report ended (`lo` equals the previous
`hi`) and ends at a fixed cutoff `hi` far enough in the past that the last merge's Actions runs have
completed. Then:

```
export WORK="$HOME/.cache/usage-census/2026-10-25"      # new directory
export CENSUS_WORK="$WORK"
T=tools/usage-census
mkdir -p "$WORK" && cp $T/config.example.json "$WORK/config.json"   # edit: issue, task, lo, hi, paths

# 1. Freeze the population (member lists are digested; print the digests into the report)
python3 $T/population.py          # data/prs.json, data/repos.json
python3 $T/project.py             # data/project3.json
python3 $T/prdetail.py            # commits, comments, submitted reviews, closing references
python3 $T/issues.py              # closers of the closed Project 3 items

# 2. Required checks (rules are read now; changes during the window are Unknown)
python3 $T/required_rules.py
python3 $T/required.py
python3 $T/reqcheck.py

# 3. Actions machine time
python3 $T/runs.py                # all runs from the earliest member's creation day to the end day
python3 $T/match.py               # exclusive run-to-member assignment; writes match_extra.json
python3 $T/jobs.py --pace 0.8 --shard 0 --of 2 &   # two shards at most
python3 $T/jobs.py --pace 0.8 --shard 1 --of 2 &
wait
python3 $T/jobs.py --extra --pace 0.8              # force-pushed-head runs, disclosed outside the totals

# 4. Native usage (read-only; the receipt store is outside the work directory and every repository)
python3 $T/collect.py
python3 $T/joinkeys.py
python3 $T/usage_summary.py

# 5. Review evidence: adjudicate BY HAND from the full text
python3 $T/review.py --dump > "$WORK/review-dump.txt"
$EDITOR "$WORK/data/adjudication.json"    # format: adjudication.example.json

# 6. Assemble, then publish
python3 $T/assemble.py
python3 $T/export.py "docs/reports/evidence/<date>-usage-census-<issue>.json"
python3 $T/report.py "docs/reports/<date>-usage-census-<issue>.md" \
    --previous docs/reports/evidence/2026-10-24-usage-census-4639.json \
    --template $T/continuation-report.example.md
```

`config.json` keys: `org`, `project` (Project 3 number), `issue` (label written into the evidence),
`task` (reader task label), `lo` and `hi` (ISO UTC, `hi` exclusive), `coord_version`, `reader` (the
built `fsgg-coord-engine`; `CENSUS_READER` overrides it), `receipt_store`, `claude_home`, `codex_home`.
A step that failed is re-run alone; `jobs.py` resumes and skips runs it already holds.

### Review adjudication

`review.py` only proposes candidates. A PR scores **V** only when one review statement or one dedicated
review comment (a submitted GitHub review counts) names an independent review or critic, gives an
explicit verdict, and binds it to the **final** head SHA in the same statement. Everything else is
unsampled: **A** (verdict stated, head not named), **E** (verdict at an earlier head), or **N** (no
verdict in the record; a `NOTE` records a review mention without one). Read the whole comment, not an
excerpt: a second statement in the same comment can carry the verdict. A critic comment ending in the
machine-readable line `VERDICT: APPROVE @ <head>` can be matched exactly; older records cannot.

### Before publishing

- Run `python3 scripts/check-telemetry-git-safety.py --base origin/main`. Evidence under a `usage` or
  `telemetry` path is capped at 64 KiB; `export.py` refuses to write more.
- Private-repository members are redacted by `assemble.py` and listed last. Do not publish their
  numbers, titles, SHAs, dates or review comment ids.
- Re-read the prose of the template against the data. The figures are computed; the sentences about the
  window, the retention boundary and the coordinator-runtime changes are not.
- One PR with one independent review and green required CI, merged through `tools/routine-delivery.py`.
  Link the owning issue without closing it (`Refs`).

## Smoke test

```
python3 tests/usage-census/run.py
```

It builds a synthetic work directory and runs every offline tool, asserting exclusive run assignment,
push-only merge runs, the force-pushed-head disclosure, the zero-second job rule, review scoring,
unknown (never pass) for an unreadable repository, response deduplication, the branch join, private
redaction and the evidence cap. The network tools and the reader build are only compiled there.

## Known limits

- Job time is Jobs API wall time, not billed minutes. A job that completes before it starts counts as 0 s.
- Runs on force-pushed heads are not in the totals (`match_extra.json`, disclosed separately).
- Tokens join a member only through the `gitBranch` on a Claude Code response record; coordinator-run
  Codex usage and anything on `main` is unattributed.
- Retained transcripts bound the native usage: Codex rollouts exist from 2026-10-19 and Claude Code
  transcripts from 2026-10-22 on the coordinator host. Pruned transcripts are Unknown.
- Review independence is as the record asserts it: one account posts every record.
