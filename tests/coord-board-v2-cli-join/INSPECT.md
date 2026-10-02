# Read-only CLI contract

```sh
fsgg-coord-engine board-v2 inspect --binding-file reviewed-binding.json --report-file new-inspection.json
```

The binding must identify the loaded adapter, protected population blob and exact
selected native cohort. The output is `fsgg.coord.board-v2-inspection/1`. It records
current native issues, complete dependency reads, owning plans and human Status,
Roadmap and Track separately from Observation and source currentness.

An unavailable population produces `selected: null`, a population gap and no
candidates (exit 3). This is unknown coverage. Missing planning values remain null;
Unknown Observation remains Unknown. Native closure or human Done never supplies
outcome acceptance. Open PRs, touch sets, available slots and intake authorization
remain unknown unless supplied independently to the pure candidate function by the
current integrator. Candidates do not dispatch or authorize work.

Inspection performs queries and bounded dependency GETs only. It never sends or
retries mutations, changes board fields, creates projection timestamps or takes the
refresh writer lock. Optional `--previous-report-file` reads a refresh/v2 report
only to retain historical LastVerified; history never proves currentness.
