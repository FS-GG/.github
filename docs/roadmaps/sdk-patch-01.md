# SDK-PATCH-01 — FS-GG .NET SDK from 10.0.401 to the patched 10.0.403 band

This is the owning plan for the Outcome [FS-GG/.github#4935](https://github.com/FS-GG/.github/issues/4935).
Root/fdev owns intake, classification, integration and Outcome acceptance. Each Delivery has its own source
owner and independent reviewer, named on its issue. Live state (what is merged, open, blocked or enrolled) is
kept on the issues and the board, not in this plan. Read those, not this file, for current status.

## Promise and acceptance

The Outcome's Done when is authoritative. In short: every **ordinary build pin** in every active FS-GG
repository moves coherently to SDK 10.0.403, with the coupled runtime and FSharp.Core, and required CI green.
Release-identity, frozen and historical pins stay unchanged unless explicitly re-qualified, each recorded with
its reason. `rollForward` is decided deliberately per repository. Published packages are not re-released solely
for this.

## Classification rule

| Class | Treatment |
|---|---|
| Ordinary build pin (`global.json`, `setup-dotnet`, image SDK, docs naming the build SDK) | Moves in its repository's own reviewed PR. |
| Release-identity or frozen pin (retained candidates, digest-bound inputs, workflow-byte fences, toolchain-identity checks of a release path) | Unchanged until its own re-qualification Delivery moves every coupled input together. |
| Historical (receipts, Atlas citations, archived reports) | Never rewritten. |

A pin is classed frozen when a gate, guard or digest requires the old SDK exactly. Moving it alone breaks that
gate. The coupled inputs move together in one re-qualification Delivery, or not at all.

## Decisions (Root, recorded on the Outcome)

- Only unfenced Rendering `global.json` files move under the ordinary route. Fenced Rendering pins wait for a
  Rendering release-toolchain re-qualification.
- The Coordination deterministic supply chain is a separate owner Delivery.
- Explicit FSharp.Core pins stay. Locks without one regenerate with the SDK.
- Template payloads stay `rollForward: disable` at exactly 10.0.403.
- The SDD product accepts a closed, fail-closed pair set (SDK 10.0.401 with its runtime, SDK 10.0.403 with its
  runtime), selected by the host runtime.
- The telemetry collector and host move to the matching aspnet runtime as their own Delivery. Source can be
  prepared first and installed adoption waits on the Home runtime.
- Owner gaps are recorded, not silently absorbed: a 10.0.400 dev container, an out-of-support Unity root SDK,
  and one FSBarV2 locked-restore nondeterminism.
- An SDK move changes the FSharp.Core lock content hash even where FSharp.Core is explicitly pinned. Every
  lock in every Delivery is checked for the SDK-401 bundled hash prefix `IGqjL9U8`.

## Deliveries

| Delivery | Promise | Depends on |
|---|---|---|
| Ordinary pins across the first waves ([#5088](https://github.com/FS-GG/.github/issues/5088)) | Each ordinary pin site moves in its repository's own PR, frozen pins kept. | None |
| Templates payload locks ([Templates#831](https://github.com/FS-GG/FS.GG.Templates/issues/831)) | The payload `global.json` files and locks move to 10.0.403 and the hash check is clean. | None |
| Templates repo-tool locks and frozen 401 routes ([Templates#832](https://github.com/FS-GG/FS.GG.Templates/issues/832)) | Each frozen Templates route is re-qualified on 10.0.403 or recorded as intentionally frozen with a reason. | Routes tied to retained or public-receiver qualification wait for the Rendering retained-candidate obligations. The rest are independent. |
| Wizard SDK host statement ([#5089](https://github.com/FS-GG/.github/issues/5089)) | The Wizard's statement names 10.0.403, matching what the shipped SDD accepts. | An SDD release containing the SDD pair-set change. |
| Rendering release-toolchain re-qualification ([#4943](https://github.com/FS-GG/.github/issues/4943)) | Release, settlement and gate pins, guard constants, toolchain hashes, plans, receipts and tests move together. Retained-candidate verification and fences are shown to hold. Only then does the root `global.json` move. | Closure of the Rendering retained-candidate acceptance obligations ([#4817](https://github.com/FS-GG/.github/issues/4817)). |
| Telemetry host and `.github` root `global.json` ([#4945](https://github.com/FS-GG/.github/issues/4945)) | The telemetry collector and host move to the matching aspnet runtime and SDK. The `.github` root `global.json` then moves. Installed adoption is read back. | Home runtime (owner input) for installed adoption. Source does not wait. |
| Coordination supply chain ([#4946](https://github.com/FS-GG/.github/issues/4946)) | Build, packaging and the published CLI toolchain move to 10.0.403. Published identities are re-qualified through the coherent publication path and installed adoption. | Capacity of the coherent publication path. No other Delivery. |
| Shared WASM release toolchain ([#4947](https://github.com/FS-GG/.github/issues/4947)) | The Game root `global.json` and the wasm-shared pins move with their frozen release scripts, with byte-identical or explicitly re-qualified outputs. | None. |
| Owner gaps ([#5090](https://github.com/FS-GG/.github/issues/5090)) | Each recorded owner gap has a disposition. | None. |

The FourD portable-workspace re-qualification was a Delivery of this plan and is closed. It is not repeated here.

## Sequencing

1. The ordinary route (first waves, SDD, Templates payload) precedes every re-qualification Delivery.
   Re-qualification Deliveries are independent of one another unless the table names a dependency.
2. The Wizard statement follows an SDD release, never the SDD source merge, so that the Wizard never
   describes a toolchain that the shipped SDD refuses.
3. Source merge does not discharge installed or recovery acceptance. A Delivery that changes a published or
   installed identity is accepted only after the canonical publication and an installed readback.
4. Root evaluates Outcome acceptance only after every Delivery's own acceptance is recorded, and lists each
   frozen pin that remains with its reason.

## Out of scope unless a Delivery accepts it

- Re-releasing published packages or templates solely for the SDK move.
- Rewriting historical receipts, Atlas citations or archived reports.
- Widening any `rollForward` policy without a recorded decision.
