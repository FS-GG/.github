---
title: Output, automation & CI
category: FS.GG
categoryindex: 6
index: 17
description: The FS-GG output model — JSON is the contract, plain and rich are projections — and how to wire the CLIs into scripts and CI.
---

# Output, automation & CI

Every FS-GG CLI command projects **one** report three ways. Understanding which
projection is the contract is the key to scripting FS-GG reliably.

## The output model

Selected by flag, with precedence `--rich` > `--text` > `--json` > default:

| Flag | Output | Use it for |
|---|---|---|
| default / `--json` | The deterministic **JSON automation contract**. | Scripts, CI, agents — anything that parses output. |
| `--text` | A portable plain-text summary. | Logs, quick reads, environments without a JSON parser. |
| `--rich` | A human Spectre.Console rendering. | Interactive terminals. |

The rule across the org: **JSON is the contract; plain and rich are projections
of the same report.** The rich projection degrades to plain text with **zero
ANSI** when output is non-interactive or redirected, or when color is disabled
(`NO_COLOR`, `TERM=dumb`). So a command piped into a file or a CI log produces
clean, stable text without you asking for it.

## Scripting against the JSON

Because the default is the JSON contract, you can pipe straight into `jq`:

```sh
# Gate a script on ship readiness.
fsgg-sdd ship --json | jq -e '.ready == true'

# Pull findings out of a verify run.
fsgg-sdd verify --json | jq '.findings[]'
```

Build automation on the JSON shape, not on scraped human text. The plain/rich
projections may change wording; the JSON is the part held stable by the schema.
See the
[SDD schema reference](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/schema-reference.md)
for the exact shapes and
[compatibility matrix](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/compatibility-matrix.md)
for what's guaranteed across versions.

## Exit codes

Commands distinguish causes by exit code so a script can branch on *why* it
failed rather than parsing a message. For example, `fsgg-sdd scaffold` exits `1`
on malformed user input (unknown provider, unsupported contract version, missing
parameter, target collision) and `2` on a provider defect (provider failed,
engine unavailable, provider wrote into SDD-owned trees). An incomplete scaffold
is never reported as complete. Check each command's docs for its specific codes.

## In CI

A typical pipeline runs the lifecycle commands with default (JSON) output,
captures the artifacts, and branches on the structured result:

```sh
# Determinism / degradation / handoff conformance in one report.
fsgg-sdd validate --json > validation-report.json

# Fail the job if not ship-ready.
fsgg-sdd ship --json | jq -e '.ready == true' || exit 1
```

Because the rich projection degrades to zero-ANSI automatically, the **same**
commands give you readable plain text in the CI log and a clean JSON artifact —
no special CI flags required. If you adopt governance, its merge-boundary gate
recomputes from scratch against the base branch regardless of any local mode (see
[Adopting governance](governance.md)).

## Agents

`fsgg-sdd agents` generates per-target Claude/Codex command and skill guidance
from the work model, marked generated and never a second source of truth. Agents
consume the same JSON contract you do — there is no separate "agent API."
