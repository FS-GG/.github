# ADR-0080: Scope child-unit qualification and qualify milestone closure comprehensively

- **Status:** Accepted
- **Date:** 2026-08-31
- **Amended by:** [ADR-0081](0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md)
- **Affects:** `.github`, `FS.GG.Coordination`, and future roadmap-owning FS-GG components

## Context

GitHub Substrate v2 originally made the complete tracked candidate tree the reuse subject for every
bootstrap gate. That is strong and simple, but it couples unrelated change classes: an adapter, receipt,
or documentation-only child unit reruns the complete canonical Quint corpus even when no formal input
changed. After the formal catalogue grew to eleven scenarios, 126 negative controls, 161 Quint processes,
and 47 Apalache verifications, the latest successful cold cohort settled around 18–20 minutes per changed
pull-request tree. GS2-04.2 then paid that cost again for a three-file acceptance checkpoint whose formal
inputs were unchanged.

Roadmap parents already provide a natural cumulative boundary. Child units are bounded evidence increments;
the parent milestone is where the platform claims that the increments compose. Applying comprehensive
qualification to both boundaries spends most verification time before the cumulative claim exists, and its
cost grows linearly with the retained corpus.

## Decision

Two evidence strengths are standard for every roadmap with parent and child units:

1. **Scoped child qualification is the default.** Each gate derives a canonical semantic subject from the
   tracked inputs capable of changing that gate's result. A changed subject executes that gate against the
   current candidate; an unchanged subject may reuse one immutable, unexpired, independently validated
   execution artifact with the same gate contract, toolchain, environment, and policy identities. Current-tree
   build, unit, architecture, dependency, package, and change-relevant adapter controls still run when their
   subjects change. Diff labels, branch names, issue prose, and caller-selected skip flags are never authority.

2. **Parent closure is comprehensive.** An explicit milestone-closure candidate binds the parent ID, the
   complete ordered child-unit contract set, every accepted child receipt digest, and the current protected
   candidate. It forces every declared gate to execute cold, rejects scoped reuse for that run, and emits a
   terminal closure manifest. After protected merge and exact-merge verification, an append-only milestone
   closure receipt records the comprehensive result. Missing, duplicate, stale, unaccepted, or contract-drifted
   children refuse closure.

3. **Formal drift overrides the fast path.** A canonical model, compiler/extractor, formal fixture, formal
   validator, tool pin, budget, gate contract, or formal-policy change changes the formal subject and therefore
   executes canonical qualification even inside a child milestone. Subject membership is declared once in the
   qualification plan and consumed by the renderer and validators; it is not a second hand-maintained workflow
   list.

4. **Production boundaries remain comprehensive.** Freeze, release, cutover, `OpenV2`, rollback-authority,
   and parent-milestone closure candidates always use comprehensive mode. Scoped child evidence cannot by
   itself authorize publication, deployment, production mutation, or cutover.

5. **The terminal manifest remains exact-head authority.** It binds the complete candidate revision and tree,
   the mode, every gate subject, each current or reused artifact digest and source execution, policy identity,
   and—when applicable—the milestone closure subject. Reuse is evidence transport, not relabeling: the prior
   subject and source remain explicit.

GS2-04 is the first active consumer of this generic contract. Its ordinary remaining child units use scoped
qualification; GS2-04 closes only after GS2-04.9 forces the comprehensive path and receives a protected closure
receipt.

## Consequences

Most child-unit changes stop paying for unrelated formal work, so corpus growth affects cold formal changes and
parent closure rather than every pull request. Feedback becomes faster and runner use falls substantially.

The accepted risk is delayed discovery of interactions omitted from a gate's declared semantic subject. The
closure run is the cumulative backstop, not a reason to make subject declarations vague: executable controls
must prove that every formal and gate-policy input changes the appropriate subject, and current-tree tests still
cover ordinary integration. A bad subject declaration can let a child merge with stale evidence, but cannot pass
the comprehensive parent closure.

`FS.GG.Coordination` owns the reusable implementation and evidence schemas. Roadmap owners declare parent/child
and closure facts through versioned contracts rather than workflow conditions. Existing accepted unit receipts
retain their historical meaning.

## Alternatives considered

**Keep complete-tree qualification for every PR.** Rejected because unrelated child units repeatedly execute a
growing corpus and make verification cost proportional to `children × accumulated corpus`.

**Skip expensive gates from path diffs or labels.** Rejected because rename, mode, generator, and policy drift can
escape a diff filter, while mutable labels and prose are not evidence authority.

**Run only the newest corpus partition.** Rejected as the default because it does not prove older obligations
remain compatible with changed shared formal inputs. Content-addressed subjects provide the fast path; closure
still executes the total corpus.

**Make comprehensive qualification scheduled-only.** Rejected because a schedule is not bound to the exact
parent-closure candidate and cannot authorize closure or cutover.
