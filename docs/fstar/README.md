# F* specification experiments

This directory evaluates an F*-first authoring path for the typed specification
kernel. It formalizes two materially different slices from S.I.R. and keeps the
proof source, reproducible verification entry point, extraction output boundary,
and findings together.

The experiment is not S.I.R. runtime authority. Its source snapshot is
`EHotwagner/S.I.R.` commit
`b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3`; the reports identify the exact
source files and distinguish implemented behavior from accepted design.

## Contents

- `combat/SIR.CombatConsequences.fst` models the committed health, wound,
  incapacity, and suppression transition in `CombatRules.resolveConsequences`.
- `communication/SIR.CommunicationNetwork.fst` models command-route capacity,
  latency, relay behavior, and monotonic knowledge updates.
- `reports/` records traceability, proof coverage, extraction findings, and the
  architectural assessment.
- `toolchain.json` pins the verified F* release and Linux artifact digest.
- `verify.sh` downloads that artifact when necessary, verifies both modules,
  and extracts their executable definitions to a temporary directory.
- `fsharp-smoke/` compiles those derived sources behind a minimal private
  compatibility runtime and executes representative calls.

## Verify and extract

From the repository root:

```bash
docs/fstar/verify.sh
```

Set `FSTAR_EXE` to use an existing F* binary. Set `FSTAR_SPIKE_CACHE` to choose
the download/cache directory. The script never writes generated F# into the
repository; extraction output is derived evidence and is placed under a
temporary directory reported at the end of the run.

Set `FSTAR_DOTNET` to a .NET 8 `dotnet` host to compile and execute the sealed
F# extraction smoke consumer as part of the same run:

```bash
FSTAR_DOTNET=/path/to/dotnet docs/fstar/verify.sh
```

The smoke project uses F# 5 compatibility syntax because that is the dialect
currently emitted by F*'s F# backend. It is an internal build boundary, not the
language mode proposed for FS-GG product projects.

The pinned release is intentionally part of the evidence. F* verification is
SMT-backed, so changing F*, Z3, options, or extraction backend is a toolchain
change that must be requalified rather than silently absorbed.

## Authority boundary

These projects test the strongest reason to choose F*: definitions such as
`resolve`, `route_delay`, `route_capacity`, and `merge_observation` are both
proved and extractable. Proof-only lemmas erase during F# extraction. A future
production design would still emit a normalized, digest-bound AST for
documentation, semantic diffing, replay, schemas, and other representations;
the extracted library would provide verified executable semantics over that
model.
