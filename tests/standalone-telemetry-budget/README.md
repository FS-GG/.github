# Standalone telemetry package budget

`run.sh` measures the exact candidate and preserved public baseline package. Qualification mode requires at least 20 fresh installs; smoke mode permits one to three samples and never reports `qualified: true`.

```bash
bash tests/standalone-telemetry-budget/run.sh \
  --candidate /path/FS.GG.Coord.Cli.VERSION.nupkg \
  --candidate-sha256 EXPECTED_ARCHIVE_SHA256 --candidate-label exact-final \
  --candidate-source-sha EXACT_MERGED_SOURCE_SHA \
  --baseline /path/FS.GG.Coord.Cli.0.87.0.nupkg \
  --baseline-evidence /tmp/standalone-baseline087/evidence.json \
  --output /tmp/standalone-budget.json --mode qualification --samples 20
```

The generated NuGet configuration contains only nuget.org. Each install also names the directory holding the exact prepared input artifact, uses fresh private caches and tool paths, and deletes only its generated measurement tree. Results cover compressed size, apparent installed bytes, file count, and cold tool-install time. They make no dashboard startup, durable-storage, submission, or public-release claim.
