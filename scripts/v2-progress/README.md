# V2 progress report workflow

Build and render a checked Markdown report from a metadata JSON file plus this
machine's local Codex session family:

```bash
scripts/v2-progress/run.sh METADATA.json ROOT_SESSION_ID FS-GG/.github /tmp/v2-progress.md
```

The wrapper restores the F# renderer in locked mode, reads bounded local JSONL counter histories,
and passes a typed snapshot to `render.fsx`. Local observation and reporting invoke no receiver or
provider process and do not require protected host-policy qualification. The report is written only
after the renderer accepts the snapshot. Metadata supplies the lane roster, launch evidence,
workstreams, declared counts, protected holds, checks, risks, next actions and completion history.

The collector discards metadata health/workspace observations rather than presenting them as fresh
receiver facts. Readiness and end-to-end capture remain Pending; remote queue counts, delivery and
lossless status are Unknown. A separately acquired snapshot may carry actual authenticated health and
workspace observations; the renderer still requires their exact identity, freshness and queue checks
before showing Active/Healthy. Local counters never become a Host receipt or fully attributed usage.

The scan is limited to 512 files, 64 MiB total, 8 MiB per file, 1 MiB per line and 10,000 counter
samples per file. It retains only allowlisted scalar samples and opaque session provenance, not raw
prompts/tool output or source paths. Incomplete reads, malformed tails, rotation/changes, missing
lineage, absent actual zero baseline and cumulative resets withhold a complete-period total. The
report still shows bounded observed cumulative samples with explicit coverage gaps; snapshots are
never summed as independent requests. A session younger than ten minutes has no completed-period
diagnostic, not zero usage. These finite diagnostics are not the durable passive-capture pipeline.
Modern `token_usage_record` response/turn capture through the existing native reader and journal is
a separate next window; a modern-only input has unavailable diagnostic totals, not zero tokens.

At a material update or explicitly requested report, rebuild metadata from actual owner/native evidence.
The ten-minute counter period is a diagnostic aggregation window, not a reporting schedule. Do not push
roadmap status/projection edits merely to report progress. Set `metadataObservedAt` to the actual UTC
read; the adapter's two-minute freshness guard is not proof those source checks happened.
Optional `boardInspection` carries an existing selected CLI inspection object into the same renderer.
Inspection/3 exposes explicit Outcome/Delivery, raw scoped promise/missing-fact/evidence, relationships
and native completion reason/date; old reports show Unknown metadata instead of dropping work. Parent
promise and acceptance remain independent from child completion or counts. The reporting projection
never mutates Status, Work level, enrollment or closure, and cannot certify acceptance. Retain actual
inspection observation cutoffs separately from report time and verify selected caller/binding provenance.

For each run, record a new evidence cutoff and verify the metadata against it:

1. Read the live worker roster; count only active lanes with explicit Sol/high
   launch evidence, including the reserved direct-V2 lane. Set every lane's
   required `currentWork` description to the concrete task it is doing at
   this cutoff. A running lane with blank work is refused by the F# renderer.
2. Read the current owner PR lists and exact head commit times. Rebuild the
   workstream state, declared PR/evidence counts, checks, risks, next actions,
   and newest five completions from those observations.
3. Add the cited draft heads to the roadmap, validate it, and push it. Use that
   new roadmap head in completion rows only after the push succeeds.
4. Set `metadataObservedAt` to the UTC verification time, run the wrapper,
   inspect the rendered labels and holds, and publish with the cutoff stated.

Do not carry forward an old `METADATA.json` with only its timestamp changed.
If a PR lands after the cutoff, rebuild and rerun or name the earlier cutoff
when sharing the report.

A complete counter window is always `LocalCounterDiagnostic`; an incomplete or unavailable window
uses `UnknownPeriodUsage`: the local JSONL scan has
no authenticated collector/account scope and no orchestration-runner/Host
receipt. The F# script always sets capture to pending. Its weekly projection
is conditional on the local root-session percentage samples sharing the
account scope and reset. `/status` context occupancy is omitted because the
live panel has no authenticated collector provenance here. Do not publish a
report as a protected receipt or infer GS2-09.9 native-effect acceptance.

Keep metadata and generated snapshots outside the repository. A metadata file
has these top-level fields: `metadataObservedAt`, `roadmapHead`, `lanes`, `declaredCounts`,
`workstreams`, `telemetry.workspaceId`, `protectedHolds`, `checks`, `risks`,
`nextActions`, `completions`. The F# adapter requires all fields and refuses
mismatched declared counts. Each completion needs its UTC commit time, PR
link, result, and a `roadmapHead` that actually contains that draft's source
evidence. Update and push the roadmap before writing that head. The report
renders the newest five completions without padding.
Each lane object requires `currentWork`; the Markdown lane table shows it
beside the lane ID, including the orchestrator and reserved direct-V2 worker.

The `render.fsx` entry point also accepts a saved snapshot JSON directly for
review and deterministic replay:

```bash
dotnet restore src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj --locked-mode
dotnet build src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj -c Release --no-restore
dotnet fsi scripts/v2-progress/render.fsx /tmp/v2-progress-snapshot.json
```
