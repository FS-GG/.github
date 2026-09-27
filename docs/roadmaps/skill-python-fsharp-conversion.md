# SKILL-FS-01 — Replace skill Python with packaged F# commands

Backlink: [Unified Development Roadmap section 9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

Status: planned. This plan does not claim a converted command, published package, or receiver adoption.

## Outcome and inventory

Move all Python implementation logic currently shipped by this repository's skill trees into typed F# commands. The
source skill root is `.agents/skills`; `.claude/skills` is its generated, byte-matched projection. The four
distinct current implementations are:

| Source under `.agents/skills` | Current responsibility | F# replacement boundary |
|---|---|---|
| `work-roadmap/scripts/fsgg_telemetry_defaults.py` | Host/workspace configuration, canonical repository identity and credential selection | Typed configuration and identity validation shared by the telemetry commands |
| `work-roadmap/scripts/native_collaboration_usage.py` | Native agent lineage and usage extraction, including unavailable/unsupported outcomes | Typed, bounded native-usage reader with explicit unknown coverage |
| `work-roadmap/scripts/roadmap-telemetry.py` | Root/child observation, population binding, lifecycle submissions, usage reconciliation and status | Packaged telemetry CLI adapter over the existing typed telemetry engine |
| `pipeline-preflight/scripts/preflight.py` | Advisory cost assessment and literal workflow dependency-graph checks | Packaged `assess` and `graph` commands with the same advisory and refusal boundaries |

The inventory includes the byte-identical `.claude/skills` copies. The exposed
`tools/roadmap-telemetry.py` wrapper and direct imports from other repository tools are caller-migration
work, not additional independent skill implementations. Recount both skill roots before closure so a newly
added Python script cannot escape the final absence check. Python elsewhere in the repository is outside this
part unless it calls one of these four implementations.

The [earlier telemetry design](../reports/2026-09-04-fsharp-roadmap-telemetry-and-projection-automation-design.md#7-migration-plan)
named four *different* legacy helpers for removal. They are absent from the current skill roots. Reuse its
parity, publish-before-flip and receiver principles without reviving removed routine phase or receipt
requirements.

## First executable window

1. Freeze the current CLI/input/output corpus for all four implementations, their positive tests and refusal
   mutations. Inventory every skill, tool, test, manifest and packaged caller, including generated mirrors and
   the `tools/roadmap-telemetry.py` entry point. Record which receiver versions still invoke Python paths.
2. Add typed F# configuration and native-usage readers behind the existing packaged telemetry tool. Preserve
   exact repository/credential validation, read-only GitHub access, private submission boundaries, bounded
   native-log parsing, deduplication and explicit unavailable/unknown results. Keep the current Python commands
   as the production oracle while differential tests prove parity.
3. Add F# telemetry adapter commands for every current `roadmap-telemetry.py` command and F# pipeline
   `assess`/`graph` commands. Preserve successful JSON bytes where a current contract requires byte identity;
   preserve exit status and typed refusal categories otherwise. For `graph`, reject duplicate YAML keys,
   expressions, unknown jobs, cycles and unsupported dependency shapes before any costly workflow begins.
   Cost estimates remain advisory and never waive required checks.
4. Publish a coherent tool/Kit/Drivers set containing the F# commands, verify the exact package from both
   feeds, and qualify a clean installed receiver. Then switch `.agents` skill instructions and repository tool
   callers to the packaged commands; regenerate `.claude` through its existing generator and verify equality.
   Compatibility launchers may contain argument translation and `exec` only, for at most one measured coherent
   release when a supported receiver still needs an old path.
5. Delete the four Python implementations and their generated copies after receiver qualification. Remove or
   replace the `tools/roadmap-telemetry.py` Python wrapper and stale imports. Add an executable absence gate
   over both skill roots, skill manifests, tests and packaged artifacts; it must catch future `.py` files in
   supported skill script paths. Retire compatibility launchers at the end of their bounded window.

The telemetry readers, telemetry CLI composition and pipeline preflight commands are independently testable
source lanes. Give them disjoint touch sets and one integrator for shared CLI, package and skill files. The
pipeline lane need not wait for telemetry source work; the skill flip waits for the complete packaged set and
receiver evidence.

## Acceptance

- Every current positive fixture and negative mutation passes against the packaged F# implementation.
  Differential tests show byte-identical contracted successes and equivalent typed refusal/exit behavior.
- Telemetry keeps prospective population, lineage, usage provenance, private data and unknown coverage intact;
  it never promotes missing native usage to measured usage or grants delivery authority.
- Pipeline preflight retains its bounded advisory economics and literal graph-only scope. Invalid YAML or
  dependency input refuses before the expensive job; no estimate skips a required gate.
- Skill instructions, tool callers, generated mirrors and clean installed receivers use the published F#
  commands. Both feeds serve byte-identical qualified artifacts and the selected receiver pins them exactly.
- No `.py` file remains in either skill root, no packaged or supported caller references a deleted
  script, and the absence gate fails on an injected Python skill script.
- Focused tests, skill quality, mirror generation, package verification and required hosted checks pass.
  Existing GS2 evidence and operation gates remain separate.

Completion requires a protected-main readback of the source, packaged and receiver results. A source-only PR
or a local parity run does not close SKILL-FS-01.
