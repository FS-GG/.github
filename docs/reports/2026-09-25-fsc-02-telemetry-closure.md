# FSC-02 telemetry/Core closure characterization

Date: 2026-09-25. Source base: `.github` `2e553e41e58ee2f5e27aedcffc7403ce50e7cdd4`. Status: provisional source and locally built package evidence for Q9 planning, not GS2-14 acceptance or permission to remove V1 code.

## Reproducible boundary check

The new [closure checker](../../scripts/check-fsc02-telemetry-closure.py) parses the six telemetry project files, their actual `Compile` sources and project references. It traverses the Host reference graph, records direct telemetry module tokens, classifies the modules compiled from Core's mixed `Telemetry.fs`, checks Store's historical CLI-path source links, and checks the Host package allowlist. Given a local `.nupkg`, it also verifies required assemblies and absence of `FS.GG.Coord.Cli` from the package members and `.deps.json`. It fails on a CLI assembly edge, a missing source, a changed link, direct Host/Contracts/Store use of a legacy lifecycle module, an unclassified module, or a missing/extra CLI package dependency. The [independent controls](../../tests/fsc02-telemetry-closure/run.py) introduce each of the key forbidden shapes in temporary copies and observe refusal.

```bash
python3 tests/fsc02-telemetry-closure/run.py
dotnet pack src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj -c Release \
  --output /tmp/fsc02-telemetry-pack -p:RestoreLockedMode=true
python3 scripts/check-fsc02-telemetry-closure.py --root . \
  --package /tmp/fsc02-telemetry-pack/FS.GG.Telemetry.Host.0.1.7.nupkg
```

At this source base the 11 fixture assertions passed, locked-mode Release pack succeeded, and the checker returned no issues over the resulting 5,034,724-byte package (`sha256 ca359399aea638371e904118e2cf409099a59ed6b76832fa6e9adf524985b374`, 54 archive members, 24 `.deps.json` libraries). These are local candidate bytes, not an installed or published Host observation.

## Measured dependency and symbol split

| Surface | Current exact source observation | Q9 disposition |
| --- | --- | --- |
| Host assembly graph | [Host project](../../src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj) directly references Contracts, Dashboard and Store. Contracts and Store each reference Core. Transitive closure is Host, Contracts, Dashboard, Store, Core; no CLI project reference. | Preserve this source boundary and prove it again on exact accepted candidate. |
| Store source identity | [Store project](../../src/FS.GG.Telemetry.Store/FS.GG.Telemetry.Store.fsproj) compiles `TelemetryStoreApplication.fsi/.fs` through `../FS.GG.Coord.Cli/` paths into **Store**. | Historical directory name does not imply a CLI binary dependency. Moving these public F# types changes assembly identity and requires consumer qualification. |
| Current local package | Host package includes `FS.GG.Coord.Core.dll`, Contracts, Dashboard and Store; its `.deps.json` has no `FS.GG.Coord.Cli` library. Existing [package allowlist](../../tests/standalone-telemetry-host-package/allowed-files.txt) agrees. | A no-CLI package is demonstrated locally. A no-V1-symbol package has **not** been demonstrated: Core carries mixed telemetry and lifecycle modules. |
| Reusable direct symbols | Host/Contracts/Store source directly names `TelemetryReceipt`, `TelemetryStore`, `TelemetryBudget`, `TelemetryCi`, `CanonicalJson` and Store's `TelemetryStoreApplication`. | Retain or extract with canonical bytes, schema/migration digests, receipt identities and CLR type compatibility tests. |
| Mixed Core source | [`Telemetry.fs`](../../src/FS.GG.Coord.Core/Telemetry.fs) compiles `CanonicalJson` beside `RuntimeUsage`, `UsageReceiptStore`, `LegacyReceiptProof`, `SyntheticCheckpointProof`, `LifecycleTelemetry`, `TelemetrySummary`, `CritiqueReceipt`, `FeedbackReceipt`, `RoadmapClosure` and `RoadmapProjection`. | These latter names are **legacy-facing candidates**, not deletion verdicts. Determine exactly which symbols serve prohibited V1 authoring versus sealed-history verification before GS2-14. `RuntimeUsage` and private `TelemetryJson` need their own reuse disposition. |

The older [F# telemetry proposal](2026-09-04-fsharp-roadmap-telemetry-and-projection-automation-design.md) names four Python helpers (`collect-runtime-usage.py`, `validate-lifecycle-log.py`, `validate-critique-state.py`, `validate-feedback-state.py`) in skill copies. The current tracked `.agents`/`.claude` trees contain none of those four filenames; Core already has [`RoadmapWorkUnit.fs`](../../src/FS.GG.Coord.Core/RoadmapWorkUnit.fs) and the modules above, and [`TelemetryApplication.fs`](../../src/FS.GG.Coord.Cli/TelemetryApplication.fs) exposes telemetry and roadmap-close commands. This is a source mapping only. It does not claim the old helper behavior was fully subsumed, nor does it warrant a second telemetry implementation.

## Qualification still required

Before a Q9 retain-or-extract decision, inventory public types and call sites within the mixed Core file and the sealed-history verifier; compare canonical JSON bytes, schema migration hashes, budget arithmetic and unknown-coverage semantics. If types move assemblies, rebuild and exercise all consumers or explicitly bound compatibility. Then install an exact published Host package from an empty tool/cache state and test provision/enroll, authenticated ingest, duplicate/conflicting receipt, lookup, restart/drain, backup/restore and private dashboard access with wrong-workspace/revoked-credential/unsafe-root controls. Verify package members, `.deps.json` and loaded assemblies at that installed receiver. Finally prove prohibited V1 claim, delivery, generic mutation and release calls refuse before effects while sealed history remains verifiable. This PR performs none of those effects and makes no cutover or GS2 acceptance claim.
