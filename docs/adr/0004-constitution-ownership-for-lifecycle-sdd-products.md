# ADR-0004: SDD owns the lifecycle constitution for `lifecycle=sdd` products, shipped at `.fsgg/constitution.md`

- **Status:** Accepted
- **Date:** 2026-06-27
- **Affects:** FS.GG.SDD, FS.GG.Rendering, FS.GG.Templates, .github

## Context

[ADR-0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md)
established that generated products are *composed at scaffold time* — `fsgg-sdd scaffold`
invokes the rendering provider (`scaffold-provider`) for an **app-only** product and reuses
`init`'s effects for the **SDD skeleton** — and that `lifecycle` is a `fs-gg-ui` template
parameter where `sdd` emits an app-only product plus the SDD skeleton, while `none` emits
neither `.specify/` nor a constitution.

ADR-0002 **Decision 4** deliberately left one question open as a P0 gate: for
`lifecycle=sdd` products, **which repo ships the F# lifecycle constitution — Rendering or
SDD?** That gate is tracked as the Coordination board card *"P0 · cross-repo — Decision:
constitution ownership for lifecycle=sdd products (Rendering vs SDD)"* and blocks
*"P2 · sdd — Implement constitution-ownership decision"*. No ADR recorded the resolution, so
the P2 item stayed `Blocked`.

Two facts constrain the choice:

- The reference **rendering provider is contractually app-only**. Per the FS.GG.SDD boundary,
  the provider produces `generatedProduct` paths and **must not write into the SDD tree**
  (`providerWroteSddTree` is a provider defect, exit 2). It is therefore not a coherent home
  for a *lifecycle* artifact.
- The **SDD skeleton is SDD's** to establish. `fsgg-sdd init` (`initEffects`) lays down the
  `.fsgg/`-namespaced skeleton — `.fsgg/project.yml`, `.fsgg/sdd.yml`, `.fsgg/agents.yml`,
  plus `work/`, `readiness/`, `CLAUDE.md`, `AGENTS.md`. A lifecycle constitution is a
  lifecycle artifact, which SDD owns. (Note: the `.specify/` tree some FS-GG repos carry is
  standard Spec Kit dogfooding, **not** what the SDD product emits.)

Today the SDD skeleton emits **no** constitution at all, so this is net-new content, not a
relocation.

## Decision

1. **SDD owns and ships the F# lifecycle constitution** for `lifecycle=sdd` products. The
   rendering provider stays app-only and never writes it.

2. **It lives at `.fsgg/constitution.md`**, reusing the existing SDD skeleton namespace
   alongside `.fsgg/project.yml` / `.fsgg/sdd.yml` / `.fsgg/agents.yml`. No new top-level
   dotdir (`.sdd/`) and no `.specify/memory/` dependency are introduced.

3. **It is emitted by the SDD skeleton** (the `init` effects that `scaffold` reuses), so both
   `fsgg-sdd init` and `fsgg-sdd scaffold --param lifecycle=sdd` produce it. The constitution
   is part of the SDD-owned skeleton and is therefore **not** a `generatedProduct` path; it
   does not appear in scaffold's app-only provenance.

## Consequences

- **FS.GG.SDD** adds `.fsgg/constitution.md` to the `init` skeleton. This intentionally moves
  the `init` byte-identical baseline (the CLAUDE.md "`init` stays byte-identical" invariant is
  re-baselined as part of this feature). Tracked as *P2 · sdd — Implement constitution-ownership
  decision*; specified and built under the SDD feature lifecycle. The artifact contract (seed
  vs fully-populated body, schema/structure) is settled in that spec.
- **FS.GG.Rendering** carries **no** constitution responsibility for `lifecycle=sdd`; the
  provider contract is unchanged and remains app-only. The `lifecycle=none` behavior
  (no `.specify/`, no constitution) is likewise unaffected.
- **FS.GG.Templates** needs no overlay change for the constitution; it is produced by the SDD
  skeleton at scaffold time, not by a Templates overlay.
- The **registry** edges are unchanged — this decision adds no new cross-repo contract; it
  assigns ownership of an artifact wholly inside SDD's skeleton.
- ADR-0002 Decision 4 is hereby resolved; the P0 board card is marked `Done` and the P2 SDD
  item moves to `Ready`.
