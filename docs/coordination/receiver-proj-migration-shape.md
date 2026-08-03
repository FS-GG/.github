# Receiver-projection migration shape

Status: decided by [.github#2102](https://github.com/FS-GG/.github/issues/2102).

When a receiver migrates a workflow call from a hand-copied skill-root pair to
`scripts/skill-view generate --receiver-proj`, use one shape everywhere:

1. Put the exact `--receiver-proj .config/kit/FS.GG.Kit.receiver.proj`
   invocation at the workflow call site.
2. Put an inline, exact pin immediately after that call site. The pin must reject
   the retired `--source`/`--roots` form, a wrong receiver-project path, and an
   added flag.
3. Run the shipped `bash scripts/skill-view selftest` in the receiver gate. Its
   swapped-root lane is the sole behavioral proof that declaration resolution
   follows a legal root-disposition change.

Do not add a receiver-maintained synthetic fixture script merely to repeat the
swapped-root test. That creates a second fixture and a second invocation list to
maintain; the tool-owned selftest already exercises the same resolver behavior.
An inline pin is intentionally local: it proves the particular workflow call
that a future editor might accidentally change. Every call site gets one pin.

The distinction matters. Text pins catch a changed caller but cannot prove that
the resolver follows the declaration; `skill-view selftest` proves the resolver
but cannot tell whether a particular receiver workflow still calls it correctly.
The two controls cover different faults without duplicating a fixture.

## Receiver acceptance

A receiver migration is complete only when all of these are true:

- no hand-copied `--source`/`--roots` generation invocation remains at the
  migrated call sites;
- each migrated call has its inline negative controls, demonstrated red before
  restoration and green after;
- the receiver's required gate executes `skill-view selftest` and the
  swapped-root lane passes;
- generation runs before the dependent `skill-view check`; and
- no dedicated receiver fixture duplicates the resolver lane.

For a migration spanning repositories, the coordinating item owns the decision
and durable guidance. Each receiver owns its source change and evidence through
a typed child issue. The coordinator stays blocked until every receiver issue is
merged or otherwise independently verified converged; comments alone are not a
cross-repository completion signal.
