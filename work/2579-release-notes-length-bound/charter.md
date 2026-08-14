---
schemaVersion: 1
workId: 2579-release-notes-length-bound
title: "bounding PackageReleaseNotes, and gating the length the registry actually enforces"
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# bounding PackageReleaseNotes, and gating the length the registry actually enforces Charter

## Identity
- Work id: `2579-release-notes-length-bound`
- Lifecycle stage: charter
- Status: chartered
- Coordination item: `.github#2579`

## Principles

- **A gate must measure the quantity that fails, not one adjacent to it.**
  `check-engine-release-notes.py` validates the FIRST TOKEN of `PackageReleaseNotes`. nuget.org
  enforces its LENGTH. Both are properties of the same field, and only one of them 400'd. Every
  local gate the `0.52.0` release author ran was green on a tree that could not publish. This is the
  item's own recurring class, and the fix must not re-commit it.

- **A length gate alone is an improvement and not a solution.** It converts a partial publish into a
  blocked release. With headroom now NEGATIVE (37,279 evaluated characters against a 35,000 limit),
  a gate alone leaves every coherent-set cut — and therefore every kit-skill lane behind
  `check-kit-published-coherence`'s strictly-greater rule — blocked. The accumulation itself is the
  defect.

- **The accumulated history is redundant with the registry; the standing advisories are not.**
  Every published version's own notes are already served, immutably and permanently, on that
  version's own listing. Re-shipping them inside every LATER version's notes duplicates content the
  feed already hosts. What a later listing carries that an earlier one CANNOT is a correction ABOUT
  an earlier one — `DO NOT ADOPT 0.50.1`, `DO NOT ADOPT 0.50.5` — because those listings are
  immutable and wrong. That asymmetry is the whole basis of the split this work adopts.

- **Deleting old entries has already been tried and reverted, and care is not a mechanism.**
  `4fccc76d` replaced the field wholesale; `5d45ced4` restored it because the replacement had
  silently deleted the poisoned-set warnings, and the author "mentioned the truncation nowhere,
  because I had not noticed it". Any bound that relies on the next author noticing will fail the
  same way, under more pressure, because the next author will be trimming against a hard ceiling.
  The preservation must be structural: trimming the narrative and deleting an advisory must become
  DIFFERENT EDITS TO DIFFERENT PROPERTIES, with a gate that refuses the second.

- **Evaluated, never grepped.** `check-coherent-set-version.py` and `check-engine-release-notes.py`
  already establish this: a raw-text read of an MSBuild property is not the value. It is load-bearing
  here and not merely stylistic — the file's raw XML inner text is 37,334 characters while the
  EVALUATED property is 37,279, because `&lt;`/`&gt;` unescape. The nuspec receives the evaluated
  value, so that is the number the gate must score.

- **Reachability is part of the gate** (`.github#2551`). A gate keyed on `paths:` is selectively
  silent, and this repository has already paid for that exact mistake once: `.github#2512`'s
  `0.50.5` became a permanent two-of-three set because `engine-release-notes.yml`'s filter did not
  select `Directory.Build.props`. Widening this gate's SUBJECT to the whole coherent set therefore
  obliges widening its TRIGGER in the same change.

- **A leg never observed red is not evidence** (`.github#2551`). Every arm added here ships with a
  recorded inversion and an observed red, and the length arm additionally ships with its red observed
  on the REAL `origin/main` tree at 37,279 rather than only on a fixture.

## Scope Boundaries

- This work bounds the field and gates its length. It does NOT recover `0.52.0`. The recovery — an
  additive `0.52.1` re-cut, refusing both a non-byte-identical force-push and the deletion of a
  published artifact — is decided and is a separate item that lands AFTER this one.
- This work does not cut a release and does not move `FsggCoherentSetVersion`.
- `check-feed-coherence` is red on `main` because of the `0.52.0` partial publish. That is
  `.github#2580` and is deliberately not widened into here.
- `.github#1762`'s first-token check is preserved unchanged. This adds a property; it replaces none.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2579-release-notes-length-bound`.
