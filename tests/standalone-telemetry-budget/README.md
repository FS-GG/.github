# Standalone telemetry package budget

`run.sh` measures the exact candidate and preserved public baseline package. Qualification mode requires at least 20 fresh installs; smoke mode permits one to three samples and never reports `qualified: true`.

```bash
bash tests/standalone-telemetry-budget/run.sh \
  --candidate /path/FS.GG.Coord.Cli.VERSION.nupkg \
  --candidate-sha256 EXPECTED_ARCHIVE_SHA256 --candidate-label exact-final \
  --candidate-source-sha EXACT_MERGED_SOURCE_SHA \
  --baseline /path/FS.GG.Coord.Cli.0.87.0.nupkg \
  --baseline-evidence /tmp/standalone-baseline087/evidence.json \
  --output /tmp/standalone-budget.json --source public-only --mode qualification --samples 20
```

`public-only` uses only credential-free nuget.org and qualifies only when every acquired installed nupkg exactly matches the supplied public readback artifact. `prepared-local` copies only the digest-bound input nupkg into a private generated source and is always preparation or smoke evidence. Both use fresh private caches and tool paths and delete only their generated measurement tree. NuGet does not expose transport byte counts, so that field is explicitly unknown while acquired archive bytes are recorded. Results make no dashboard startup, durable-storage, submission, or public-release claim.
