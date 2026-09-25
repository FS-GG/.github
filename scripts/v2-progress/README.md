# V2 progress report workflow

Build and render a checked Markdown report from a metadata JSON file plus this
machine's local Codex session family:

```bash
scripts/v2-progress/run.sh METADATA.json ROOT_SESSION_ID FS-GG/.github /tmp/v2-progress.md
```

The wrapper builds the F# renderer in locked mode, obtains fresh authenticated
`fdev-telemetry health` and exact-workspace status, reads local JSONL counter
histories, and passes a typed snapshot to `render.fsx`. It writes the report
only after the renderer accepts the snapshot. The metadata file supplies the
lane roster, explicit launch evidence, workstreams, declared counts, protected
holds, checks, risks, next actions and completion history. The collector
**overwrites** metadata telemetry observations with the direct authenticated
CLI results. It fails if health is not ready or the workspace is not
configured with `pending=0`, `pendingUnacknowledged=0`,
`unacknowledgedLossy=false`.

The counter input is always `LocalCounterDiagnostic`: the local JSONL scan has
no authenticated collector/account scope and no orchestration-runner/Host
receipt. The F# script always sets capture to pending. Its weekly projection
is conditional on the local root-session percentage samples sharing the
account scope and reset. `/status` context occupancy is omitted because the
live panel has no authenticated collector provenance here. Do not publish a
report as a protected receipt or infer GS2-09.9 native-effect acceptance.

Keep metadata and generated snapshots outside the repository. A metadata file
has these top-level fields: `roadmapHead`, `lanes`, `declaredCounts`,
`workstreams`, `telemetry.workspaceId`, `protectedHolds`, `checks`, `risks`,
`nextActions`, `completions`. The F# adapter requires all fields and refuses
mismatched declared counts. Each completion needs its UTC commit time, PR
link, result, and a `roadmapHead` that actually contains that draft's source
evidence. Update and push the roadmap before writing that head. The report
renders the newest five completions without padding.

The `render.fsx` entry point also accepts a saved snapshot JSON directly for
review and deterministic replay:

```bash
dotnet restore src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj --locked-mode
dotnet build src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj -c Release --no-restore
dotnet fsi scripts/v2-progress/render.fsx /tmp/v2-progress-snapshot.json
```
