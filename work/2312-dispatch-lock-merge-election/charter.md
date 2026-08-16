# Per-Receiver Dispatch Lock And Lease-Free Merge Election Charter

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** charter
- **Status:** chartered
- **Item:** [.github#2312](https://github.com/FS-GG/.github/issues/2312)

## Identity

Slice 2 of the eight planned in §11.2 of the GitHub-native executor fencing design, filed under
[.github#1858](https://github.com/FS-GG/.github/issues/1858) — *two workers ran `.github#1853` to completion
concurrently under ONE claim marker; six repos got PRs from an unlocked executor.* Critical, open since
2026-07-28.

This slice owns the rest of `#1858`'s replacement-plan step 1: a per-receiver dispatch lock, the merge
election's ordering rule, and the ADR that records both decisions.

## Principles

1. **Never compare identities.** The fence works because two contexts each obtain a grant and GitHub issues
   two different comment ids — not because anything can tell them apart. A design that compares worker ids
   is the design that already failed.
2. **The subject answers exclusion; the key answers idempotence.** One lock issue per receiver decides
   "may I act"; the opkey decides "has this already been applied". Confusing them is asymmetric: the first
   direction duplicates a no-op, the second makes an exclusion decision with a value that decides no lock.
3. **Reuse the CAS; do not refactor it.** `Writes.claim` is already a general comment-order CAS over an
   arbitrary issue ref. The write path gains nothing.
4. **One rule, one implementation.** "Lowest id wins" is written four times today and three of those decide
   locks. The election must not make a fifth.
5. **Fail closed everywhere.** A lock that cannot be found, a read that cannot be completed, and a marker
   that cannot be parsed are all refusals, never permissions.
6. **Derive, don't restate.** Completeness against the roster is proved from the roster.

## Scope Boundaries

**In:** the lock table and its refs; acquire/release; the exported ordering function; conversion of the four
copies; the ADR; the eight lock issues.

**Out:** posting the election marker (slice 3); the merge gate (slice 4); the broker (slice 5); receiver
validation (slice 6); the reproduction (slice 7); arming (slice 8); any CAS write-path change; any new CLI
verb; authenticating which executor is the rightful holder.

## Policy Pointers

- ADR-0027 (worker-keyed lock and identity), amended here.
- ADR-0041 (the chore lock is the item CAS on another subject), extended here.
- ADR-0042 (the lock ref is embedded beside the roster), inherited unchanged.
- ADR-0058 (derive, don't restate), which governs the completeness proof.
- ADR-0019 §1 / `.github#2332` (no CI credential carries the board read), which keeps this off CI.

## Lifecycle Notes

Route `sdd-required` (`fsgg:route-decision/v2` revision 1, agent `rook-1bad`), on four grounds: parent epic,
multi-slice plan, coordinated phases, and an ADR amendment. Recorded `internal-library-signature` rather
than `public-contract` deliberately, because both touched libraries are `IsPackable=false`.
