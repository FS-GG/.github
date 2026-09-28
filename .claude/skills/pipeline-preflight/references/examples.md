# Helper examples

Run from an existing checkout/environment with the published coordination engine installed.
No command downloads tools or runs CI. Replace `<skill>` with the installed
`pipeline-preflight` skill directory.

```sh
fsgg-coord-engine skill preflight assess <skill>/references/one-off.json
fsgg-coord-engine skill preflight assess <skill>/references/recurring.json
fsgg-coord-engine skill preflight graph .github/workflows/build.yml --requires report:shardA,shardB
```

The graph command requires both shard jobs to be ancestors of `report`, including transitive
paths. Missing jobs, cycles, invalid YAML and expression-based `needs` are errors. This asserts
ordering only: `if: always()` may let a dependent job run after failure, so success/evidence rules
need additional checks. Shell commands, conditions, dynamic matrices, concurrency and called
workflows are not evaluated. No output from this command authorizes release or evidence reuse.

The JSON examples are illustrative estimates, not observed FS-GG statistics. The one-off case
prices 12 hours of setup at 60 currency units/hour against a single 30-runner-minute job, even
assuming perfect detection. The recurring case shows when a small reused check may pay back.
Replace the explicit values and provenance before relying on either result.

For stateful behavior, reuse the public
[FsQuint F# preflight example](https://github.com/FS-GG/FsQuint/tree/main/examples/CiPreflight).
It supplies a four-job Quint model, pinned tooling, a completion witness, a missing-dependency
counterexample and a workload-launch negative control. It is a learning example, not a model
of every FS-GG pipeline. Prefer the graph check for its simple dependency defect; use Quint when
additional state and interleavings make the static check insufficient.
