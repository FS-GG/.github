---
title: Incoherent and contradictory proposals
category: Design
categoryindex: 4
index: 37
description: How freeform issue intake coexists with a coherent accepted Quint model.
---

# Incoherent and contradictory proposals

Freeform issue intake remains permissive. A user does not need to know Quint, and
an issue may be incomplete, ambiguous, contradictory, unsupported, stale, or
intentionally opaque. The invariant applies at acceptance instead:

> A proposal may be incoherent; the accepted canonical `WorkspaceModel` must remain coherent.

The system records the issue and prose digest first, then constructs an isolated,
revision-bound `ChangeProposal`. Work and investigation may continue without
mutating accepted semantic authority. Before acceptance, the proposal receives an
explicit disposition:

- `CoherentDelta` — checked semantics can be applied;
- `NoSemanticChange` — implementation, documentation, or mechanical work only;
- `AcceptedOpaque` — a human explicitly accepts named, visible formalization debt;
- `Ambiguous` — a human decision is still required;
- `Contradictory` — the proposed semantics cannot enter accepted authority; or
- `Stale` — the proposal requires semantic rebasing.

Failed formalization never silently becomes `AcceptedOpaque`. Contradictions and
stale bases produce readable diagnostics and counterexamples; raw tool output
remains available but is not the only explanation. Semantic merge, rather than a
successful Git text merge, decides whether the accepted model can advance.

Tracking authority: [FS.GG.SDD #927 clarification](https://github.com/FS-GG/FS.GG.SDD/issues/927#issuecomment-5423531974).
